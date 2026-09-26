using System;
using System.Collections.Generic;
using System.Linq;

using ClientCore;
using ClientCore.Extensions;

using ClientLogic.UI;

using DTAClient.Domain.Multiplayer;
using DTAClient.Domain.Multiplayer.CnCNet;

using Rampastring.Tools;

namespace ClientLogic.Tunnels;

/// <summary>Sends messages to the other players of a game lobby.</summary>
public interface ILobbyTransport
{
    /// <summary>Sends a system message ("COMMAND payload") to all players in the lobby.</summary>
    void SendSystemMessage(string message);
}

/// <summary>The parts of a game lobby that its tunnel session reads.</summary>
public interface ITunnelSessionLobby
{
    List<PlayerInfo> Players { get; }

    bool IsHost { get; }

    string HostName { get; }
}

/// <summary>
/// The tunnel state of one CnCNet game lobby: the tunnel mode, the selected tunnel server and whether it is valid,
/// and the V3 dynamic negotiation round. Handles the CHTNL, RENEGALL, NEGRPT and TNLPNG messages.
/// The lobby shows the state; it refreshes its controls on the events.
/// </summary>
public sealed class TunnelSession
{
    private readonly TunnelHandler tunnelHandler;
    private readonly V3TunnelNegotiationManager negotiator;
    private readonly ITunnelSessionLobby lobby;
    private readonly ILobbyTransport transport;
    private readonly INoticeSink notices;

    private bool allNegotiationsCompleteMessageShown;

    public TunnelSession(TunnelHandler tunnelHandler, V3TunnelNegotiationManager negotiator,
        ITunnelSessionLobby lobby, ILobbyTransport transport, INoticeSink notices)
    {
        this.tunnelHandler = tunnelHandler;
        this.negotiator = negotiator;
        this.lobby = lobby;
        this.transport = transport;
        this.notices = notices;
    }

    public TunnelMode Mode { get; private set; }

    /// <summary>
    /// True when the host selected a tunnel server this client doesn't know; the game can't be launched then.
    /// </summary>
    public bool IsInTunnelError { get; private set; }

    /// <summary>The tunnel mode changed; refresh mode-dependent controls.</summary>
    public event Action ModeChanged;

    /// <summary>Player pings were reset or updated; refresh the ping indicators of the given players.</summary>
    public event Action<IReadOnlyList<PlayerInfo>> PlayerPingsChanged;

    /// <summary>
    /// Player data shown in the lobby changed (tunnel change or ping reset); refresh the player list.
    /// </summary>
    public event Action PlayerDataChanged;

    /// <summary>Whether the game can be launched may have changed.</summary>
    public event Action LaunchStatusChanged;

    /// <summary>Sets the mode for a new lobby session without announcing it.</summary>
    public void Start(TunnelMode mode)
    {
        Mode = mode;
        IsInTunnelError = false;
    }

    /// <summary>Clears the per-session state when the lobby is left.</summary>
    public void Clear()
    {
        allNegotiationsCompleteMessageShown = false;
        IsInTunnelError = false;
    }

    /// <summary>
    /// Lets <see cref="CheckAllNegotiationsComplete"/> announce the next completed negotiation round again.
    /// </summary>
    public void ResetNegotiationsCompleteNotice() => allNegotiationsCompleteMessageShown = false;

    public void ChangeMode(TunnelMode mode, bool isHostInitiated, bool autoSelectTunnel = true)
    {
        if (mode == Mode)
            return;

        bool newUseDynamic = mode == TunnelMode.V3Dynamic;
        var oldMode = Mode;
        Mode = mode;

        negotiator.ApplyModeTransition(oldMode, mode);

        string modeDescription = mode.GetDescription();
        notices.AddNotice(isHostInitiated
            ? string.Format("Tunnel mode changed to {0}.".L10N("Client:Main:TunnelModeChanged"), modeDescription)
            : string.Format("The game host has changed tunnel mode to {0}.".L10N("Client:Main:TunnelModeChangedByHost"), modeDescription),
            NoticeSeverity.Info);

        if (lobby.IsHost)
        {
            if (newUseDynamic)
                tunnelHandler.CurrentTunnel = null;
            else if (autoSelectTunnel)
                AutoSelectBestTunnel();
        }

        allNegotiationsCompleteMessageShown = false;

        ModeChanged?.Invoke();

        if (newUseDynamic)
        {
            foreach (PlayerInfo pInfo in lobby.Players)
                pInfo.Ping = PingValue.Unknown;

            PlayerPingsChanged?.Invoke(lobby.Players);
            PlayerDataChanged?.Invoke();
        }
    }

    /// <summary>Selects the lowest-ping tunnel of the current mode and tells the other players (host only).</summary>
    public void AutoSelectBestTunnel()
    {
        int targetVersion = Mode == TunnelMode.V2Legacy ? 2 : 3;

        var bestTunnel = tunnelHandler.Tunnels
            .Where(t => t.Ping.IsValid()
                && (UserINISettings.Instance.PingUnofficialCnCNetTunnels || t.Official || t.Recommended)
                && t.Version == targetVersion)
            .OrderBy(t => t.Ping.Milliseconds)
            .FirstOrDefault();

        if (bestTunnel != null)
        {
            notices.AddNotice(string.Format("Auto-selected tunnel: {0} (Ping: {1}ms)".L10N("Client:Main:AutoSelectedTunnel"), bestTunnel.Name, bestTunnel.Ping.Milliseconds), NoticeSeverity.Info);
            transport.SendSystemMessage($"{TunnelNegotiationCommands.ChangeTunnelServer} {bestTunnel.Address}:{bestTunnel.Port}");
            ChangeTunnelServer(bestTunnel);
        }
    }

    /// <summary>The host selected a tunnel server in the tunnel selection window.</summary>
    public void SelectTunnelServer(CnCNetTunnel tunnel)
    {
        transport.SendSystemMessage($"{TunnelNegotiationCommands.ChangeTunnelServer} {tunnel.Address}:{tunnel.Port}");
        notices.AddNotice(string.Format("Changed the tunnel server to: {0}".L10N("Client:Main:YouChangedTunnel"), tunnel.Name), NoticeSeverity.Info);
        ChangeTunnelServer(tunnel);
    }

    /// <summary>
    /// Changes the tunnel server used for the game.
    /// </summary>
    /// <param name="tunnel">The new tunnel server to use.</param>
    public void ChangeTunnelServer(CnCNetTunnel tunnel)
    {
        bool tunnelChanged = tunnelHandler.CurrentTunnel == null ||
            tunnelHandler.CurrentTunnel.Address != tunnel.Address ||
            tunnelHandler.CurrentTunnel.Port != tunnel.Port;

        tunnelHandler.CurrentTunnel = tunnel;

        // Old pings were measured against the previous tunnel — show unknown until fresh
        // TNLPNG values arrive rather than presenting stale values as current. Gated on an
        // actual tunnel change so repeated/no-op change messages can't flicker the display.
        if (tunnelChanged && Mode != TunnelMode.V3Dynamic)
        {
            foreach (PlayerInfo pInfo in lobby.Players)
                pInfo.Ping = PingValue.Unknown;

            PlayerPingsChanged?.Invoke(lobby.Players);
        }

        PlayerDataChanged?.Invoke();
        ReportCurrentTunnelPing();

        negotiator.ApplyStaticTunnel(tunnel);
    }

    /// <summary>
    /// Sends the local player's ping to the current tunnel to the other players and shows it (static tunnels only).
    /// </summary>
    public void ReportCurrentTunnelPing()
    {
        if (tunnelHandler.CurrentTunnel == null || Mode == TunnelMode.V3Dynamic)
            return;

        transport.SendSystemMessage("TNLPNG " + tunnelHandler.CurrentTunnel.Ping.Milliseconds);

        PlayerInfo pInfo = lobby.Players.Find(p => p.Name.Equals(ProgramConstants.PLAYERNAME));
        if (pInfo != null)
        {
            pInfo.Ping = tunnelHandler.CurrentTunnel.Ping;
            PlayerPingsChanged?.Invoke([pInfo]);
            PlayerDataChanged?.Invoke();
        }
    }

    /// <summary>Handles the host's CHTNL (change tunnel server) message.</summary>
    public void HandleTunnelServerChangeMessage(string sender, string tunnelAddressAndPort)
    {
        if (sender != lobby.HostName)
            return;

        string[] split = tunnelAddressAndPort.Split(':');
        if (split.Length < 2 || !int.TryParse(split[1], out int tunnelPort))
            return;

        string tunnelAddress = split[0];

        CnCNetTunnel tunnel = tunnelHandler.Tunnels.Find(t => t.Address == tunnelAddress && t.Port == tunnelPort);
        if (tunnel == null)
        {
            IsInTunnelError = true;
            notices.AddNotice(("The game host has selected an invalid tunnel server! " +
                "The game host needs to change the server or you will be unable " +
                "to participate in the match.").L10N("Client:Main:HostInvalidTunnel"),
                NoticeSeverity.Warning);
            LaunchStatusChanged?.Invoke();
            return;
        }

        IsInTunnelError = false;
        notices.AddNotice(string.Format("The game host has changed the tunnel server to: {0}".L10N("Client:Main:HostChangeTunnel"), tunnel.Name), NoticeSeverity.Info);
        ChangeTunnelServer(tunnel);
        LaunchStatusChanged?.Invoke();
    }

    /// <summary>Handles a player's TNLPNG (ping to the current tunnel) message.</summary>
    public void HandleTunnelPing(string sender, int ping)
    {
        if (Mode == TunnelMode.V3Dynamic)
            return;

        PlayerInfo pInfo = lobby.Players.Find(p => p.Name.Equals(sender));
        if (pInfo != null)
        {
            pInfo.Ping = ping >= 0 ? PingValue.FromMs(ping) : PingValue.Unknown;
            PlayerPingsChanged?.Invoke([pInfo]);
        }
    }

    /// <summary>Handles a player's NEGRPT (negotiation report) message.</summary>
    public void HandleNegotiationReportMessage(string sender, string data)
    {
        negotiator.HandleNegotiationReportMessage(sender, data);
        CheckAllNegotiationsComplete();
    }

    /// <summary>Handles the host's RENEGALL (renegotiate all) message.</summary>
    public void HandleRenegotiateAll(string sender, string data)
    {
        if (sender != lobby.HostName || lobby.IsHost)
            return;

        // The running game routes its traffic through the current tunnels; tearing
        // them down would freeze or break it. RestartNegotiations refuses too — this
        // early-out just avoids a misleading "renegotiating" chat notice.
        if (ProgramConstants.IsInGame)
        {
            Logger.Log("Ignored a renegotiate-all request because the game is running.");
            return;
        }

        // The payload is the host's authoritative participant list: the players the
        // host sees in the lobby. Restart only pairs among them, so in-game players
        // are left alone even by clients that don't know they are in game (e.g.
        // someone who joined mid-game and never saw their STRTD notification).
        var participants = data
            .Split(new[] { ',' }, StringSplitOptions.RemoveEmptyEntries)
            .Select(name => name.Trim())
            .ToHashSet();

        if (!participants.Contains(ProgramConstants.PLAYERNAME))
        {
            Logger.Log("Ignored a renegotiate-all request that does not include the local player.");
            return;
        }

        notices.AddNotice(string.Format("{0} has requested all players renegotiate tunnel connections.".L10N("Client:Main:RenegotiateAllReceived"), sender), NoticeSeverity.Info);

        var affectedPlayers = negotiator.PlayerInfos
            .Where(p => p.Name != ProgramConstants.PLAYERNAME && participants.Contains(p.Name))
            .ToList();
        negotiator.RestartNegotiations(affectedPlayers);
    }

    /// <summary>Asks every player in the lobby to renegotiate their tunnels (host, dynamic mode).</summary>
    public void TriggerRenegotiateAll()
    {
        // Only players in the lobby take part; in-game players' routes carry live game
        // traffic and are left alone. The list rides along so every receiver applies
        // the host's view instead of relying on its own possibly stale in-game flags.
        var participatingPlayers = lobby.Players.Where(p => !p.IsInGame).Select(p => p.Name).ToList();

        if (participatingPlayers.Count <= 1)
        {
            notices.AddNotice("Cannot renegotiate: all other players are currently in game.".L10N("Client:Main:RenegotiateAllInGame"), NoticeSeverity.Warning);
            return;
        }

        // One renegotiation round at a time: firing another while one is running tears
        // down in-flight negotiations whose stale packets then corrupt the fresh round.
        // Local negotiations aren't enough — the host's own pairs can finish while a pair
        // between two other players is still negotiating (their reports say InProgress),
        // and a RENEGALL landing mid-round on them is just as destructive. Only pairs
        // among the participants count, though: a pair involving an in-game player can
        // sit at InProgress (e.g. a one-sided report) without blocking renegotiation of
        // the lobby-side pairs that would fix exactly that.
        bool remoteNegotiationRunning = negotiator.NegotiationData
            .GetIncompleteNegotiations(participatingPlayers)
            .Any(pair => pair.status == NegotiationStatus.InProgress);

        if (negotiator.HasActiveNegotiations || remoteNegotiationRunning)
        {
            notices.AddNotice("Tunnel negotiations are already in progress. Wait for them to finish before renegotiating.".L10N("Client:Main:RenegotiateAlreadyRunning"), NoticeSeverity.Warning);
            return;
        }

        notices.AddNotice("Requesting all players renegotiate tunnel connections...".L10N("Client:Main:RenegotiateAllSent"), NoticeSeverity.Info);
        transport.SendSystemMessage($"{TunnelNegotiationCommands.RenegotiateAll} {string.Join(",", participatingPlayers)}");
        negotiator.RestartAllNegotiations();
    }

    /// <summary>
    /// When every negotiation of a dynamic-tunnel lobby has succeeded, warns about high-ping pairs once per round.
    /// </summary>
    public void CheckAllNegotiationsComplete()
    {
        if (Mode != TunnelMode.V3Dynamic || lobby.Players.Count <= 1)
            return;

        if (negotiator.AreAllNegotiationsSuccessful() && !allNegotiationsCompleteMessageShown)
        {
            allNegotiationsCompleteMessageShown = true;
            CheckHighPingPairs();
        }

        LaunchStatusChanged?.Invoke();
    }

    private void CheckHighPingPairs()
    {
        var playerNames = lobby.Players.Select(p => p.Name).ToList();

        List<(string, string, int)> highPingPairs = TunnelPingAnalysis.GetHighPingPairs(negotiator.NegotiationData, playerNames);

        if (highPingPairs.Count > 0)
        {
            notices.AddNotice("Warning: The following player pairs have high ping:".L10N("Client:Main:HighPingPairsWarning"), NoticeSeverity.Warning);
            foreach (var (p1, p2, ping) in highPingPairs)
                notices.AddNotice($"  {p1} <-> {p2}: {ping}ms", NoticeSeverity.Warning);
        }

        SuggestKickForLagReduction(playerNames);
    }

    /// <summary>
    /// The game's input lag is driven by the worst pair ping in the lobby (the spawner
    /// derives the latency level from the worst connection). If removing a single player
    /// would significantly lower that worst ping, tell the host — they're the one who
    /// can act on it. Only shown once per negotiation round (called from the
    /// all-negotiations-complete path) and only for a meaningful saving.
    /// </summary>
    private void SuggestKickForLagReduction(List<string> playerNames)
    {
        if (!lobby.IsHost)
            return;

        KickSuggestion suggestion = TunnelPingAnalysis.SuggestKick(negotiator.NegotiationData, playerNames);
        if (suggestion == null)
            return;

        if (suggestion.PlayerName == ProgramConstants.PLAYERNAME)
        {
            notices.AddNotice(string.Format("Note: your connection is the bottleneck — the worst ping in this game is {0} ms, and without you it would be {1} ms.".L10N("Client:Main:KickSuggestionSelf"),
                suggestion.WorstPingMs, suggestion.WorstPingWithoutMs), NoticeSeverity.Warning);
        }
        else
        {
            notices.AddNotice(string.Format("Note: {0} has high ping with the other players. Kicking them would improve the worst connection from {1} ms to {2} ms.".L10N("Client:Main:KickSuggestion"),
                suggestion.PlayerName, suggestion.WorstPingMs, suggestion.WorstPingWithoutMs), NoticeSeverity.Warning);
        }
    }
}

/// <summary>A player whose removal would lower the worst ping between the remaining players.</summary>
public sealed record KickSuggestion(string PlayerName, int WorstPingMs, int WorstPingWithoutMs);

/// <summary>Ping checks over the negotiated player pairs of a lobby.</summary>
public static class TunnelPingAnalysis
{
    public static List<(string Player1, string Player2, int PingMs)> GetHighPingPairs(NegotiationDataManager negotiationData, List<string> playerNames)
    {
        var highPingPairs = new List<(string, string, int)>();

        foreach (var (player1, player2) in negotiationData.GetPlayerPairs(playerNames))
        {
            var ping = negotiationData.GetPing(player1, player2);
            if (ping.HasValue && PingQualityRules.IsHighForWarning(ping.Value))
                highPingPairs.Add((player1, player2, ping.Value.Milliseconds));
        }

        return highPingPairs;
    }

    /// <summary>
    /// Returns the player whose removal lowers the worst pair ping the most, or null if there are fewer than
    /// three players, a pair ping is unknown, the worst ping is acceptable or the saving is too small.
    /// </summary>
    public static KickSuggestion SuggestKick(NegotiationDataManager negotiationData, List<string> playerNames)
    {
        if (playerNames.Count < 3)
            return null;

        // A complete ping matrix is required — with unknown pairs the math would lie.
        var pairPings = new List<(string p1, string p2, int ping)>();
        foreach (var (p1, p2) in negotiationData.GetPlayerPairs(playerNames))
        {
            var ping = negotiationData.GetPing(p1, p2);
            if (!ping.HasValue || !ping.Value.IsValid())
                return null;

            pairPings.Add((p1, p2, ping.Value.Milliseconds));
        }

        if (pairPings.Count == 0)
            return null;

        int worstOverall = pairPings.Max(p => p.ping);
        if (worstOverall < PingQualityRules.KickSuggestionMinWorstMs)
            return null;

        string bestCandidate = null;
        int bestWorstWithout = worstOverall;

        foreach (string player in playerNames)
        {
            var remainingPairs = pairPings.Where(p => p.p1 != player && p.p2 != player).ToList();
            if (remainingPairs.Count == 0)
                continue;

            int worstWithout = remainingPairs.Max(p => p.ping);
            if (worstWithout < bestWorstWithout)
            {
                bestWorstWithout = worstWithout;
                bestCandidate = player;
            }
        }

        if (bestCandidate == null || worstOverall - bestWorstWithout < PingQualityRules.KickSuggestionMinImprovementMs)
            return null;

        return new KickSuggestion(bestCandidate, worstOverall, bestWorstWithout);
    }
}
