using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Security.Cryptography;
using System.Text;

using ClientCore;

using Microsoft.Xna.Framework;
using Rampastring.Tools;

#nullable enable

namespace DTAClient.Domain.Multiplayer.CnCNet;

/// <summary>
/// Orchestrates V3 dynamic-tunnel negotiation for a lobby: maintains the per-player
/// negotiation state, drives <see cref="V3PlayerNegotiator"/> instances, exchanges status
/// over IRC and prepares the data needed to start the game bridge. Shared by
/// CnCNetGameLobby and CnCNetGameLoadingLobby, which live on separate inheritance trees.
/// </summary>
public class V3TunnelNegotiationManager
{
    private readonly IV3NegotiationHost host;
    private readonly TunnelHandler tunnelHandler;
    private readonly List<V3PlayerInfo> _v3PlayerInfos = new();
    private readonly NegotiationDataManager _negotiationData = new();

    public V3TunnelNegotiationManager(IV3NegotiationHost host, TunnelHandler tunnelHandler)
    {
        this.host = host;
        this.tunnelHandler = tunnelHandler;
    }

    public IReadOnlyList<V3PlayerInfo> PlayerInfos => _v3PlayerInfos;

    public NegotiationDataManager NegotiationData => _negotiationData;

    public V3PlayerInfo? FindPlayer(string name) => _v3PlayerInfos.FirstOrDefault(p => p.Name == name);

    /// <summary>
    /// Derives a deterministic player ID that all clients can compute without communicating.
    /// </summary>
    public uint GeneratePlayerID(string playerName)
    {
        using var sha1 = SHA1.Create();
        byte[] hash = sha1.ComputeHash(Encoding.UTF8.GetBytes($"{playerName}:{host.ChannelName}"));
        return BinaryPrimitives.ReadUInt32LittleEndian(hash);
    }

    private List<CnCNetTunnel> GetAvailableTunnelsForNegotiation()
    {
        return tunnelHandler.Tunnels
            .Where(t => t.Version == 3 &&
                (UserINISettings.Instance.PingUnofficialCnCNetTunnels || t.Official || t.Recommended))
            .ToList();
    }

    /// <summary>
    /// Synchronises the V3 player list with the lobby's player list, creating entries for
    /// new players and tearing down negotiations for players who have left.
    /// </summary>
    public void RegenerateV3PlayerInfos()
    {
        // Remove players who are no longer in the game; clean up their negotiations first.
        var playersToRemove = _v3PlayerInfos.Where(v3p => host.Players.All(p => p.Name != v3p.Name)).ToList();
        foreach (var v3p in playersToRemove)
        {
            DetachNegotiator(v3p);
            v3p.StopNegotiation();
            _v3PlayerInfos.Remove(v3p);
        }

        for (int i = 0; i < host.Players.Count; i++)
        {
            var player = host.Players[i];
            var v3Player = FindPlayer(player.Name);
            if (v3Player == null)
            {
                _v3PlayerInfos.Add(new V3PlayerInfo(
                    GeneratePlayerID(player.Name),
                    player.Name,
                    i,
                    0)); // PlayerGameId is assigned at game start
            }
            else
            {
                v3Player.PlayerIndex = i;
            }
        }
    }

    /// <summary>
    /// Starts negotiating with a single player (by name), if dynamic tunnels are in use and
    /// the player isn't the local user.
    /// </summary>
    public void StartNegotiationForPlayerName(string playerName)
    {
        if (host.TunnelMode != TunnelMode.V3Dynamic || playerName == ProgramConstants.PLAYERNAME)
            return;

        var v3Player = FindPlayer(playerName);
        if (v3Player != null)
            StartTunnelNegotiationForPlayer(v3Player);
    }

    /// <summary>
    /// Starts negotiations with every remote player that hasn't yet negotiated (or isn't
    /// already negotiating). Used when joining a lobby that already has players.
    /// </summary>
    public void StartPendingNegotiations()
    {
        if (host.TunnelMode != TunnelMode.V3Dynamic || host.Players.Count <= 1)
            return;

        foreach (var v3Player in _v3PlayerInfos
            .Where(p => p.Name != ProgramConstants.PLAYERNAME && !p.HasNegotiated && !p.IsNegotiating)
            .ToList())
            StartTunnelNegotiationForPlayer(v3Player);
    }

    private void StartTunnelNegotiationForPlayer(V3PlayerInfo player)
    {
        if (host.TunnelMode != TunnelMode.V3Dynamic || player.Name == ProgramConstants.PLAYERNAME)
            return;

        var localV3Player = FindPlayer(ProgramConstants.PLAYERNAME);
        if (localV3Player == null)
            return;

        var availableTunnels = GetAvailableTunnelsForNegotiation();
        if (availableTunnels.Count == 0)
        {
            host.AddNotice("Cannot negotiate tunnel: no V3 tunnels are available. Wait for the tunnel list to refresh or switch to a different tunnel mode.", Color.Yellow);
            BroadcastNegotiationInfo(player.Name, NegotiationStatus.Failed);
            host.OnNegotiationStateChanged();
            return;
        }

        _negotiationData.UpdateStatus(ProgramConstants.PLAYERNAME, player.Name, NegotiationStatus.InProgress);
        BroadcastNegotiationInfo(player.Name, NegotiationStatus.InProgress);

        var pInfo = host.Players.Find(p => p.Name == player.Name);
        if (pInfo != null)
            host.OnLocalNegotiationStatus(pInfo, NegotiationStatus.InProgress, -1);

        // Disable the launch/load button until this negotiation succeeds.
        host.OnNegotiationStateChanged();

        try
        {
            var startResult = player.StartNegotiation(
                localV3Player,
                tunnelHandler,
                availableTunnels,
                p2pEnabled: UserINISettings.Instance.EnableP2P);

            switch (startResult)
            {
                case NegotiationStartResult.Started:
                    AttachNegotiator(player);
                    break;

                case NegotiationStartResult.AlreadyInProgress:
                    // A negotiation is already running for this player; leave its state untouched.
                    break;

                case NegotiationStartResult.Failed:
                    MarkNegotiationFailed(player.Name, pInfo);
                    break;
            }
        }
        catch (Exception ex)
        {
            Logger.Log($"Error negotiating with player {player.Name}: {ex.Message}");
            MarkNegotiationFailed(player.Name, pInfo);
        }
    }

    private void MarkNegotiationFailed(string playerName, PlayerInfo? pInfo)
    {
        _negotiationData.UpdateStatus(ProgramConstants.PLAYERNAME, playerName, NegotiationStatus.Failed);
        BroadcastNegotiationInfo(playerName, NegotiationStatus.Failed);

        if (pInfo != null)
            host.OnLocalNegotiationStatus(pInfo, NegotiationStatus.Failed, -1);

        host.OnNegotiationStateChanged();
    }

    private void OnPlayerNegotiationResult(object? sender, TunnelChosenEventArgs e)
    {
        var v3PlayerInfo = _v3PlayerInfos.FirstOrDefault(p => p.Id == e.PlayerId);
        if (v3PlayerInfo == null)
            return;

        v3PlayerInfo.HasNegotiated = true;
        v3PlayerInfo.IsNegotiating = false;

        var playerInfo = host.Players.FirstOrDefault(p => p.Name == e.PlayerName);

        if (e.ChosenTunnel != null)
        {
            // Success — this fires for the relay choice (round 1) and again if the P2P
            // upgrade round picks a direct path (round 2).
            v3PlayerInfo.Tunnel = e.ChosenTunnel;

            // Only re-broadcast when the pair's ping/status actually changed, so a P2P
            // upgrade is propagated to everyone while a round-2 that simply re-confirms
            // the relay (same values) doesn't add redundant IRC traffic.
            var prevPing = _negotiationData.GetPing(ProgramConstants.PLAYERNAME, e.PlayerName);
            var prevStatus = _negotiationData.GetNegotiationStatus(ProgramConstants.PLAYERNAME, e.PlayerName);
            bool changed = prevStatus != NegotiationStatus.Succeeded
                || (prevPing?.Milliseconds ?? -1) != e.NegotiationPing;

            if (e.IsLocalDecision)
                _negotiationData.UpdatePing(ProgramConstants.PLAYERNAME, e.PlayerName, e.NegotiationPing);
            else
                _negotiationData.UpdatePing(e.PlayerName, ProgramConstants.PLAYERNAME, e.NegotiationPing);

            _negotiationData.UpdateStatus(ProgramConstants.PLAYERNAME, e.PlayerName, NegotiationStatus.Succeeded);

            if (playerInfo != null)
                host.OnLocalNegotiationStatus(playerInfo, NegotiationStatus.Succeeded, e.NegotiationPing);

            host.OnNegotiationStateChanged();

            if (changed)
                BroadcastNegotiationInfo(e.PlayerName, NegotiationStatus.Succeeded, e.NegotiationPing);
        }
        else
        {
            // Failure
            _negotiationData.UpdateStatus(ProgramConstants.PLAYERNAME, e.PlayerName, NegotiationStatus.Failed);

            if (playerInfo != null)
                host.OnLocalNegotiationStatus(playerInfo, NegotiationStatus.Failed, -1);

            host.OnNegotiationStateChanged();

            BroadcastNegotiationInfo(e.PlayerName, NegotiationStatus.Failed);
        }
    }

    private void OnPlayerNegotiationComplete(object? sender, EventArgs e)
    {
        var negotiator = (V3PlayerNegotiator)sender;
        var player = negotiator.RemotePlayer;
        if (player == null)
            return;

        if (!player.HasNegotiated)
        {
            player.HasNegotiated = true;
            player.IsNegotiating = false;
            BroadcastNegotiationInfo(player.Name, NegotiationStatus.Failed);
        }

        negotiator.NegotiationResult -= OnPlayerNegotiationResult;
        negotiator.NegotiationComplete -= OnPlayerNegotiationComplete;

        if (ReferenceEquals(player.Negotiator, negotiator))
            player.StopNegotiation();

        host.OnNegotiationStateChanged();
    }

    public void HandleNegotiationInfoMessage(string sender, string message)
    {
        string[] parts = message.Split(';');
        if (parts.Length < 2)
            return;

        string targetPlayer = parts[0];
        if (!Enum.TryParse<NegotiationStatus>(parts[1], out var status))
            return;

        _negotiationData.UpdateStatus(sender, targetPlayer, status);

        if (parts.Length >= 3 && int.TryParse(parts[2], out int ping) && ping >= 0)
        {
            _negotiationData.UpdatePing(sender, targetPlayer, ping);

            if (sender == ProgramConstants.PLAYERNAME)
            {
                PlayerInfo? pInfo = host.Players.Find(p => p.Name == targetPlayer);
                if (pInfo != null)
                    host.OnRemoteNegotiationStatus(pInfo, status, ping);
            }
            else if (targetPlayer == ProgramConstants.PLAYERNAME)
            {
                PlayerInfo? pInfo = host.Players.Find(p => p.Name == sender);
                if (pInfo != null)
                    host.OnRemoteNegotiationStatus(pInfo, status, ping);
            }
        }
        else if (targetPlayer == ProgramConstants.PLAYERNAME)
        {
            // No ping present, but another player's status with us changed.
            PlayerInfo? pInfo = host.Players.Find(p => p.Name == sender);
            if (pInfo != null)
                host.OnRemoteNegotiationStatus(pInfo, status, -1);
        }

        host.OnNegotiationStateChanged();
    }

    private void BroadcastNegotiationInfo(string targetPlayer, NegotiationStatus status, int ping = -1)
    {
        string message = ping >= 0
            ? $"{TunnelNegotiationCommands.NegotiationInfo} {targetPlayer};{status};{ping}"
            : $"{TunnelNegotiationCommands.NegotiationInfo} {targetPlayer};{status}";

        host.SendNegotiationMessage(message);
    }

    public bool AreAllNegotiationsSuccessful()
    {
        if (host.TunnelMode != TunnelMode.V3Dynamic || host.Players.Count <= 1)
            return true;

        return _negotiationData.AreAllNegotiationsSuccessful(host.Players.Select(p => p.Name).ToList());
    }

    /// <summary>
    /// Returns remote players whose negotiated tunnel matches the given address/port.
    /// </summary>
    public List<V3PlayerInfo> FindRemotePlayersUsingTunnel(string address, int port)
        => _v3PlayerInfos
            .Where(p => p.Name != ProgramConstants.PLAYERNAME &&
                        p.Tunnel?.Address == address && p.Tunnel?.Port == port)
            .ToList();

    /// <summary>
    /// Tears down and restarts negotiations for the given players.
    /// </summary>
    public void RestartNegotiations(IEnumerable<V3PlayerInfo> affectedPlayers)
    {
        host.OnNegotiationsRestarted();

        foreach (var v3Player in affectedPlayers.ToList())
        {
            v3Player.StopNegotiation();
            v3Player.ResetNegotiator();
            _negotiationData.ClearPlayer(v3Player.Name);

            if (v3Player.Name != ProgramConstants.PLAYERNAME)
                StartTunnelNegotiationForPlayer(v3Player);
        }

        host.OnNegotiationStateChanged();
    }

    /// <summary>
    /// Handles a remote player's request to renegotiate the tunnel shared with us.
    /// </summary>
    public void HandleRemoteTunnelRenegotiate(string sender, string tunnelAddressAndPort)
    {
        if (host.TunnelMode != TunnelMode.V3Dynamic)
            return;

        string[] split = tunnelAddressAndPort.Split(':');
        if (split.Length != 2 || !int.TryParse(split[1], out int tunnelPort))
            return;

        string tunnelAddress = split[0];

        var remoteV3Player = FindPlayer(sender);
        if (remoteV3Player == null)
            return;

        if (remoteV3Player.Tunnel?.Address == tunnelAddress && remoteV3Player.Tunnel?.Port == tunnelPort)
        {
            host.AddNotice($"{sender} needs to renegotiate tunnel. Starting renegotiation...", Color.Orange);
            RestartNegotiations(new[] { remoteV3Player });
        }
    }

    /// <summary>
    /// Removes a single player's V3 negotiation state (the lobby still owns its own player list).
    /// </summary>
    public void RemovePlayer(string playerName)
    {
        var v3Player = FindPlayer(playerName);
        if (v3Player != null)
        {
            _v3PlayerInfos.Remove(v3Player);

            if (host.TunnelMode == TunnelMode.V3Dynamic)
            {
                DetachNegotiator(v3Player);
                v3Player.StopNegotiation();
            }
        }

        _negotiationData.ClearPlayer(playerName);
    }

    /// <summary>
    /// Stops every active negotiation without clearing the player list or negotiation data.
    /// </summary>
    public void StopAllNegotiations()
    {
        foreach (var v3Player in _v3PlayerInfos)
        {
            DetachNegotiator(v3Player);
            v3Player.StopNegotiation();
        }
    }

    /// <summary>Resets every player's negotiation state (used when switching to dynamic mode).</summary>
    public void ResetAllNegotiators()
    {
        foreach (var v3Player in _v3PlayerInfos)
            v3Player.ResetNegotiator();
    }

    /// <summary>Discards all negotiation status/ping data.</summary>
    public void ClearNegotiationData() => _negotiationData.ClearAll();

    /// <summary>
    /// Stops all negotiations and clears every piece of negotiation state. Used on teardown.
    /// </summary>
    public void ClearAll()
    {
        StopAllNegotiations();
        _negotiationData.ClearAll();
        _v3PlayerInfos.Clear();
    }

    /// <summary>
    /// Assigns final game IDs/ports/tunnels to every player and builds the
    /// "id;name;address;..." payload used in the STARTV3 message. Sets each player's Port.
    /// </summary>
    public string GenerateV3StartPayload()
    {
        var sb = new StringBuilder();

        // The player order here defines each player's in-game id (port). All clients must
        // iterate in this same order; the STARTV3 handler keys the id off message position.
        for (int i = 0; i < host.Players.Count; i++)
        {
            var player = host.Players[i];
            uint id = GeneratePlayerID(player.Name);
            int port = 48000 - i; // with V3 this is more like an ID for the game (first bytes of packet data)
            player.Port = port;

            string address = IPAddress.Any + ":0";
            var v3PlayerInfo = FindPlayer(player.Name);
            if (v3PlayerInfo != null)
            {
                v3PlayerInfo.Id = id;
                v3PlayerInfo.PlayerIndex = i;
                v3PlayerInfo.PlayerGameId = (ushort)port;

                if (host.TunnelMode == TunnelMode.V3Static)
                    v3PlayerInfo.Tunnel = tunnelHandler.CurrentTunnel;

                // In dynamic mode each client uses its own per-peer negotiated tunnel, so this
                // address is informational only; it is only consumed by clients in V3 static mode.
                address = v3PlayerInfo.Tunnel == null
                    ? IPAddress.Any + ":0"
                    : v3PlayerInfo.Tunnel.Address + ":" + v3PlayerInfo.Tunnel.Port;
            }
            else
            {
                Logger.Log($"GenerateV3StartPayload: Missing V3 player info for {player.Name}, using fallback tunnel address.");
            }

            sb.Append(id).Append(';')
              .Append(player.Name).Append(';')
              .Append(address).Append(';');
        }

        return sb.ToString().TrimEnd(';');
    }

    /// <summary>
    /// Starts the in-game tunnel bridge for the local player. Returns false if the local
    /// player's V3 info could not be found.
    /// </summary>
    public bool StartGameBridge()
    {
        var localV3Player = FindPlayer(ProgramConstants.PLAYERNAME);
        if (localV3Player == null)
        {
            Logger.Log("V3TunnelNegotiationManager: Could not find local V3 player info.");
            return false;
        }

        tunnelHandler.StartGameBridge(localV3Player.Id, localV3Player.PlayerGameId, _v3PlayerInfos);
        return true;
    }

    private void AttachNegotiator(V3PlayerInfo player)
    {
        if (player.Negotiator == null)
            return;

        player.Negotiator.NegotiationResult += OnPlayerNegotiationResult;
        player.Negotiator.NegotiationComplete += OnPlayerNegotiationComplete;
    }

    private void DetachNegotiator(V3PlayerInfo player)
    {
        if (player.Negotiator == null)
            return;

        player.Negotiator.NegotiationResult -= OnPlayerNegotiationResult;
        player.Negotiator.NegotiationComplete -= OnPlayerNegotiationComplete;
    }
}
