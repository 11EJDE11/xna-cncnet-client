using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Net;
using System.Text;

using ClientCore;
using ClientCore.Extensions;

using ClientLogic.Launch;
using ClientLogic.Lobby;
using ClientLogic.Protocol;
using ClientLogic.UI;

using DTAClient.Domain.Multiplayer;
using DTAClient.Domain.Multiplayer.CnCNet;
using DTAClient.Online;
using DTAClient.Online.EventArguments;

using Rampastring.Tools;

namespace ClientLogic.CnCNet;

/// <summary>
/// A CnCNet room for loading a saved multiplayer game, as the XNA CnCNetGameLoadingLobby: the channel's players,
/// the host's options (OP), ready requests, file hashes, the tunnel (V2 ports with START, V3 with STARTV3 and
/// negotiation), and the game broadcast. The front end calls <see cref="Update"/> regularly.
/// </summary>
public sealed class CnCNetGameLoadingRoom : GameLoadingSession, IV3NegotiationHost, ITunnelSelectionTarget
{
    private const double GAME_BROADCAST_INTERVAL = 20.0;
    private const double INITIAL_GAME_BROADCAST_DELAY = 10.0;

    private const string NOT_ALL_PLAYERS_PRESENT_CTCP_COMMAND = "NPRSNT";
    private const string GET_READY_CTCP_COMMAND = "GTRDY";
    private const string FILE_HASH_CTCP_COMMAND = "FHSH";
    private const string INVALID_FILE_HASH_CTCP_COMMAND = "IHSH";
    private const string TUNNEL_PING_CTCP_COMMAND = "TNLPNG";
    private const string OPTIONS_CTCP_COMMAND = "OP";
    private const string INVALID_SAVED_GAME_INDEX_CTCP_COMMAND = "ISGI";
    private const string START_GAME_CTCP_COMMAND = "START";
    private const string START_GAME_V3_CTCP_COMMAND = "STARTV3";
    private const string PLAYER_READY_CTCP_COMMAND = "READY";

    private readonly CnCNetManager connectionManager;
    private readonly TunnelHandler tunnelHandler;
    private readonly GameCollection gameCollection;
    private readonly CnCNetUserData cncnetUserData;
    private readonly V3TunnelNegotiationManager negotiator;
    private readonly CtcpHandler[] ctcpCommandHandlers;
    private readonly string localGame;

    private Channel channel;
    private string hostName;
    private string gameFilesHash;
    private bool started;
    private TunnelMode tunnelMode;
    private bool broadcasting;
    private TimeSpan timeUntilGameBroadcast;

    public CnCNetGameLoadingRoom(CnCNetManager connectionManager, TunnelHandler tunnelHandler, IUiDispatcher uiDispatcher,
        GameCollection gameCollection, CnCNetUserData cncnetUserData, GameProcessService gameProcess, IDialogService dialogs,
        ISoundService sounds)
        : base(gameProcess, dialogs, sounds, uiDispatcher)
    {
        this.connectionManager = connectionManager;
        this.tunnelHandler = tunnelHandler;
        this.gameCollection = gameCollection;
        this.cncnetUserData = cncnetUserData;
        localGame = ClientConfiguration.Instance.LocalGame;

        negotiator = new V3TunnelNegotiationManager(this, tunnelHandler, uiDispatcher);

        ctcpCommandHandlers =
        [
            CtcpHandler.NoParam(NOT_ALL_PLAYERS_PRESENT_CTCP_COMMAND, HandleNotAllPresentNotification),
            CtcpHandler.NoParam(GET_READY_CTCP_COMMAND, HandleGetReadyNotification),
            CtcpHandler.String(FILE_HASH_CTCP_COMMAND, HandleFileHashCommand),
            CtcpHandler.String(INVALID_FILE_HASH_CTCP_COMMAND, HandleCheaterNotification),
            CtcpHandler.Int(TUNNEL_PING_CTCP_COMMAND, HandleTunnelPing),
            CtcpHandler.String(OPTIONS_CTCP_COMMAND, HandleOptionsMessage),
            CtcpHandler.NoParam(INVALID_SAVED_GAME_INDEX_CTCP_COMMAND, HandleInvalidSaveIndexCommand),
            CtcpHandler.String(START_GAME_V3_CTCP_COMMAND, HandleStartGameV3Command),
            CtcpHandler.String(START_GAME_CTCP_COMMAND, HandleStartGameCommand),
            CtcpHandler.Int(PLAYER_READY_CTCP_COMMAND, HandlePlayerReadyRequest),
            CtcpHandler.String(TunnelNegotiationCommands.ChangeTunnelServer, HandleTunnelServerChangeMessage),
            CtcpHandler.String(TunnelNegotiationCommands.NegotiationReport, HandleNegotiationReportMessage),
        ];

        connectionManager.ConnectionLost += (_, _) => Clear();
        connectionManager.Disconnected += (_, _) => Clear();
    }

    /// <summary>The local player's IRC chat colour.</summary>
    public IRCColor IrcChatColor { get; set; }

    /// <summary>The room is open (set up, until cleared).</summary>
    public bool IsActive { get; private set; }

    /// <summary>The Load Game button can be used (not while the host's tunnel is invalid).</summary>
    public bool CanLoad { get; private set; } = true;

    public string HostName => hostName;

    public string ChannelName => channel?.ChannelName;

    public string RoomName => channel?.UIName;

    public CnCNetTunnel CurrentTunnel => tunnelHandler.CurrentTunnel;

    public TunnelMode TunnelMode => tunnelMode;

    /// <summary>The host must pick a tunnel (no V2 tunnel selected, or it failed); the argument is the description.</summary>
    public event EventHandler<string> TunnelSelectionRequested;

    /// <summary>The front end should show this room (GetReadyNotification: TopBar.SwitchToPrimary).</summary>
    public event EventHandler ShowRequested;

    /// <summary>
    /// Sets up events and information before joining the channel.
    /// </summary>
    public void SetUp(bool isHost, CnCNetTunnel tunnel, Channel channel, string hostName)
    {
        this.channel = channel;
        this.hostName = hostName;

        channel.MessageAdded += Channel_MessageAdded;
        channel.UserAdded += Channel_UserAdded;
        channel.UserLeft += Channel_UserLeft;
        channel.UserQuitIRC += Channel_UserQuitIRC;
        channel.CTCPReceived += Channel_CTCPReceived;

        tunnelMode = TunnelModeExtensions.FromTunnel(tunnel);

        tunnelHandler.CurrentTunnel = tunnelMode == TunnelMode.V3Dynamic ? null : tunnel;

        started = false;
        CanLoad = true;
        IsActive = true;

        negotiator.RegenerateV3PlayerInfos();
        Refresh(isHost);
        broadcasting = isHost;
        timeUntilGameBroadcast = TimeSpan.FromSeconds(GAME_BROADCAST_INTERVAL);
    }

    /// <summary>
    /// Clears event subscriptions and leaves the channel.
    /// </summary>
    public void Clear()
    {
        broadcasting = false;

        negotiator.ClearAll();

        if (channel != null)
        {
            // TODO leave channel only if we've joined the channel
            channel.Leave();

            channel.MessageAdded -= Channel_MessageAdded;
            channel.UserAdded -= Channel_UserAdded;
            channel.UserLeft -= Channel_UserLeft;
            channel.UserQuitIRC -= Channel_UserQuitIRC;
            channel.CTCPReceived -= Channel_CTCPReceived;

            connectionManager.RemoveChannel(channel);
        }

        bool wasActive = IsActive;
        IsActive = false;

        tunnelHandler.CurrentTunnel = null;

        if (wasActive)
            base.Leave();

        channel = null;
    }

    public override void Leave() => Clear();

    private void Channel_CTCPReceived(object sender, ChannelCTCPEventArgs e)
    {
        foreach (CtcpHandler cmdHandler in ctcpCommandHandlers)
        {
            if (cmdHandler.Handle(e.UserName, e.Message))
                return;
        }

        Logger.Log("Unhandled CTCP command: " + e.Message + " from " + e.UserName);
    }

    /// <summary>
    /// Called when the local user has joined the game channel.
    /// </summary>
    public void OnJoined()
    {
        var fhc = new FileHashCalculator();
        fhc.CalculateHashes();

        if (IsHost)
        {
            connectionManager.SendCustomMessage(new QueuedMessage(
                string.Format("MODE {0} +klnNs {1} {2}", channel.ChannelName,
                channel.Password, SGPlayers.Count),
                QueuedMessageType.SYSTEM_MESSAGE, 50));

            connectionManager.SendCustomMessage(new QueuedMessage(
                string.Format("TOPIC {0} :{1}", channel.ChannelName,
                ProgramConstants.CNCNET_PROTOCOL_REVISION + ";" + localGame.ToLower()),
                QueuedMessageType.SYSTEM_MESSAGE, 50));

            gameFilesHash = fhc.GetCompleteHash();

            broadcasting = true;
            timeUntilGameBroadcast = TimeSpan.FromSeconds(INITIAL_GAME_BROADCAST_DELAY);
        }
        else
        {
            channel.SendCTCPMessage(FILE_HASH_CTCP_COMMAND + " " + fhc.GetCompleteHash(), QueuedMessageType.SYSTEM_MESSAGE, 10);

            if (tunnelHandler.CurrentTunnel != null)
            {
                channel.SendCTCPMessage(TUNNEL_PING_CTCP_COMMAND + " " + tunnelHandler.CurrentTunnel.Ping.Milliseconds, QueuedMessageType.SYSTEM_MESSAGE, 10);

                if (tunnelHandler.CurrentTunnel.Ping.IsUnknown())
                    AddNotice(string.Format("{0} - unknown ping to tunnel server.".L10N("Client:Main:PlayerUnknownPing"), ProgramConstants.PLAYERNAME));
                else
                    AddNotice(string.Format("{0} - ping to tunnel server: {1} ms".L10N("Client:Main:PlayerPing"), ProgramConstants.PLAYERNAME, tunnelHandler.CurrentTunnel.Ping.Milliseconds));
            }
        }

        UpdateDiscordPresence(true);
        RaiseChanged();
    }

    /// <summary>Called regularly by the front end: announces the game while hosting.</summary>
    public void Update(TimeSpan elapsed)
    {
        if (!broadcasting || !IsActive)
            return;

        timeUntilGameBroadcast -= elapsed;
        if (timeUntilGameBroadcast <= TimeSpan.Zero)
        {
            BroadcastGame();
            timeUntilGameBroadcast = TimeSpan.FromSeconds(GAME_BROADCAST_INTERVAL);
        }
    }

    private void Channel_UserAdded(object sender, ChannelUserEventArgs e)
    {
        var pInfo = new PlayerInfo { Name = e.User.IRCUser.Name };

        Players.Add(pInfo);

        Sounds.Play(LobbySound.PlayerJoined);

        negotiator.RegenerateV3PlayerInfos();

        BroadcastOptions();
        RaiseChanged();
        UpdateDiscordPresence();

        negotiator.StartNegotiationForPlayerName(pInfo.Name);
    }

    private void Channel_UserLeft(object sender, UserNameEventArgs e)
    {
        RemovePlayer(e.UserName);
        UpdateDiscordPresence();
    }

    private void Channel_UserQuitIRC(object sender, UserNameEventArgs e)
    {
        RemovePlayer(e.UserName);
        UpdateDiscordPresence();
    }

    private void RemovePlayer(string playerName)
    {
        int index = Players.FindIndex(p => p.Name == playerName);

        if (index == -1)
            return;

        Sounds.Play(LobbySound.PlayerLeft);

        negotiator.RemovePlayer(playerName);

        Players.RemoveAt(index);

        UpdateLoadGameButtonStatus();
        RaiseChanged();

        if (!IsHost && playerName == hostName && !ProgramConstants.IsInGame)
        {
            connectionManager.MainChannel.AddMessage(new ChatMessage(
                new ChatColor(255, 255, 0), "The game host left the game!".L10N("Client:Main:HostLeft")));

            Clear();
        }
    }

    private void Channel_MessageAdded(object sender, IRCMessageEventArgs e)
    {
        if (!string.IsNullOrEmpty(e.Message.SenderIdent) &&
            cncnetUserData.IsIgnored(e.Message.SenderIdent) &&
            !e.Message.SenderIsAdmin)
        {
            AddMessage(new ChatMessage(new ChatColor(192, 192, 192), string.Format("Message blocked from - {0}".L10N("Client:Main:PMBlockedFrom"), e.Message.SenderName)));
        }
        else
        {
            AddMessage(e.Message);
            Sounds.Play(LobbySound.Message);
        }
    }

    public override void AddNotice(string message, ChatColor color)
    {
        if (channel != null)
            channel.AddMessage(new ChatMessage(color, message));
        else
            base.AddNotice(message, color);
    }

    void INoticeSink.AddNotice(string message, NoticeSeverity severity) => AddNotice(message, severity switch
    {
        NoticeSeverity.Success => new ChatColor(144, 238, 144),
        NoticeSeverity.Warning => new ChatColor(255, 255, 0),
        NoticeSeverity.Degraded => new ChatColor(255, 165, 0),
        NoticeSeverity.Error => ChatColor.Red,
        _ => ChatColor.White,
    });

    protected override void BroadcastOptions()
    {
        if (!IsHost || Players.Count == 0)
            return;

        Players[0].Ready = true;

        var message = new StringBuilder(OPTIONS_CTCP_COMMAND + " ");
        message.Append(SelectedSavedGameIndex);
        message.Append(";");
        message.Append((int)tunnelMode);
        message.Append(";");
        foreach (PlayerInfo pInfo in Players)
        {
            message.Append(pInfo.Name);
            message.Append(":");
            message.Append(Convert.ToInt32(pInfo.Ready));
            message.Append(";");
        }
        message.Remove(message.Length - 1, 1);

        channel.SendCTCPMessage(message.ToString(), QueuedMessageType.GAME_SETTINGS_MESSAGE, 10);
    }

    public override void SendChatMessage(string message)
    {
        if (string.IsNullOrEmpty(message) || channel == null)
            return;

        Sounds.Play(LobbySound.Message);

        channel.SendChatMessage(message, IrcChatColor);
    }

    protected override void RequestReadyStatus() =>
        channel.SendCTCPMessage(PLAYER_READY_CTCP_COMMAND + " 1", QueuedMessageType.GAME_PLAYERS_READY_STATUS_MESSAGE, 10);

    protected override void GetReadyNotification()
    {
        base.GetReadyNotification();

        ShowRequested?.Invoke(this, EventArgs.Empty);

        if (IsHost)
            channel.SendCTCPMessage(GET_READY_CTCP_COMMAND, QueuedMessageType.GAME_GET_READY_MESSAGE, 0);
    }

    protected override void NotAllPresentNotification()
    {
        base.NotAllPresentNotification();

        if (IsHost)
        {
            channel.SendCTCPMessage(NOT_ALL_PLAYERS_PRESENT_CTCP_COMMAND,
                QueuedMessageType.GAME_NOTIFICATION_MESSAGE, 0);
        }
    }

    /// <summary>The tunnel selection's Apply (TunnelSelectionWindow_TunnelSelected).</summary>
    public void SelectTunnel(TunnelMode mode, CnCNetTunnel tunnel)
    {
        HandleTunnelModeChange(mode, true);

        if (mode != TunnelMode.V3Dynamic && tunnel != null)
        {
            channel.SendCTCPMessage($"{TunnelNegotiationCommands.ChangeTunnelServer} {tunnel.Address}:{tunnel.Port}",
                QueuedMessageType.SYSTEM_MESSAGE, 10);
            AddNotice(string.Format("Changed the tunnel server to: {0}".L10N("Client:Main:YouChangedTunnel"), tunnel.Name));
            HandleTunnelServerChange(tunnel);
        }

        BroadcastOptions();
        RaiseChanged();
    }

    #region CTCP Handlers

    private void HandleGetReadyNotification(string sender)
    {
        if (sender != hostName)
            return;

        GetReadyNotification();
    }

    private void HandleNotAllPresentNotification(string sender)
    {
        if (sender != hostName)
            return;

        NotAllPresentNotification();
    }

    private void HandleFileHashCommand(string sender, string fileHash)
    {
        if (!IsHost)
            return;

        PlayerInfo pInfo = Players.Find(p => p.Name == sender);
        if (pInfo == null)
            return;

        pInfo.HashReceived = true;

        if (fileHash != gameFilesHash)
            HandleCheaterNotification(hostName, sender); // This is kinda hacky
    }

    private void HandleCheaterNotification(string sender, string cheaterName)
    {
        if (sender != hostName)
            return;

        AddNotice(string.Format("{0} - modified files detected! They could be cheating!".L10N("Client:Main:PlayerCheating"), cheaterName), ChatColor.Red);

        if (IsHost)
            channel.SendCTCPMessage(INVALID_FILE_HASH_CTCP_COMMAND + " " + cheaterName, QueuedMessageType.SYSTEM_MESSAGE, 0);
    }

    private void HandleTunnelPing(string sender, int pingInMs)
    {
        if (pingInMs < 0)
            AddNotice(string.Format("{0} - unknown ping to tunnel server.".L10N("Client:Main:PlayerUnknownPing"), sender));
        else
            AddNotice(string.Format("{0} - ping to tunnel server: {1} ms".L10N("Client:Main:PlayerPing"), sender, pingInMs));
    }

    /// <summary>
    /// Handles an options broadcast sent by the game host.
    /// </summary>
    private void HandleOptionsMessage(string sender, string data)
    {
        if (sender != hostName)
            return;

        string[] parts = data.Split(new char[] { ';' }, StringSplitOptions.RemoveEmptyEntries);

        if (parts.Length < 1)
            return;

        int sgIndex = Conversions.IntFromString(parts[0], -1);

        if (sgIndex < 0)
            return;

        if (sgIndex >= SavedGames.Count)
        {
            AddNotice("The game host has selected an invalid saved game index!".L10N("Client:Main:HostInvalidIndex") + " " + sgIndex);
            channel.SendCTCPMessage(INVALID_SAVED_GAME_INDEX_CTCP_COMMAND, QueuedMessageType.SYSTEM_MESSAGE, 10);
            return;
        }

        SetSavedGameIndex(sgIndex);

        int tunnelModeFromOp = -1;
        int playerStartIndex = 1;
        if (parts.Length >= 2 && !parts[1].Contains(":") && int.TryParse(parts[1], out int parsedMode))
        {
            tunnelModeFromOp = parsedMode;
            playerStartIndex = 2;
        }

        Players.Clear();

        for (int i = playerStartIndex; i < parts.Length; i++)
        {
            string[] playerAndReadyStatus = parts[i].Split(':');
            if (playerAndReadyStatus.Length < 2)
                return;

            string playerName = playerAndReadyStatus[0];
            int readyStatus = Conversions.IntFromString(playerAndReadyStatus[1], -1);

            if (string.IsNullOrEmpty(playerName) || readyStatus == -1)
                return;

            Players.Add(new PlayerInfo { Name = playerName, Ready = Convert.ToBoolean(readyStatus) });
        }

        RaiseChanged();

        negotiator.RegenerateV3PlayerInfos();

        if (tunnelModeFromOp >= 0 && !IsHost)
            HandleTunnelModeChange((TunnelMode)tunnelModeFromOp, false);

        negotiator.StartPendingNegotiations();
    }

    private void HandleInvalidSaveIndexCommand(string sender)
    {
        PlayerInfo pInfo = Players.Find(p => p.Name == sender);

        if (pInfo == null)
            return;

        pInfo.Ready = false;

        AddNotice(string.Format("{0} does not have the selected saved game on their system! Try selecting an earlier saved game.".L10N("Client:Main:PlayerDontHaveSavedGame"), pInfo.Name));

        RaiseChanged();
    }

    private void HandleStartGameCommand(string sender, string data)
    {
        if (sender != hostName)
            return;

        string[] parts = data.Split(';');

        int playerCount = parts.Length / 2;

        for (int i = 0; i < playerCount; i++)
        {
            if (parts.Length < i * 2 + 1)
                return;

            string pName = parts[i * 2];
            string ipAndPort = parts[i * 2 + 1];
            string[] ipAndPortSplit = ipAndPort.Split(':');

            if (ipAndPortSplit.Length < 2)
                return;

            if (!int.TryParse(ipAndPortSplit[1], out int port))
                return;

            PlayerInfo pInfo = Players.Find(p => p.Name == pName);

            if (pInfo == null)
                continue;

            pInfo.Port = port;
        }

        LoadGame();
    }

    private void HandleStartGameV3Command(string sender, string data)
    {
        if (sender != hostName)
            return;

        if (ProgramConstants.IsInGame)
        {
            Logger.Log("HandleStartGameV3Command: Ignoring game start while still in a running game.");
            NotifyStartFailed();
            return;
        }

        if (!StartV3Message.TryDecode(data, out StartV3Message start) || start.Players.Count != Players.Count)
        {
            Logger.Log($"HandleStartGameV3Command: Invalid start message for {Players.Count} players.");
            NotifyStartFailed();
            return;
        }

        for (int i = 0; i < Players.Count; i++)
        {
            if (!negotiator.ApplyV3StartEntry(start.Players[i], i))
            {
                Logger.Log($"HandleStartGameV3Command: Could not apply start entry for player at position {i}.");
                NotifyStartFailed();
                return;
            }
        }

        StartV3Game();
    }

    /// <summary>
    /// Tells the player their client could not act on the host's game start message —
    /// everyone else launches, so silence here would leave them stranded in the lobby
    /// with no explanation.
    /// </summary>
    private void NotifyStartFailed()
    {
        AddNotice(("Failed to process the game start message from the host. The game was started " +
            "without you; the host's player list may be out of sync with yours. Try rejoining the game.").L10N("Client:Main:StartMessageInvalid"),
            new ChatColor(255, 255, 0));
    }

    private void HandlePlayerReadyRequest(string sender, int readyStatus)
    {
        PlayerInfo pInfo = Players.Find(p => p.Name == sender);

        if (pInfo == null)
            return;

        pInfo.Ready = Convert.ToBoolean(readyStatus);

        RaiseChanged();

        if (IsHost)
            BroadcastOptions();
    }

    private void HandleTunnelServerChangeMessage(string sender, string tunnelAddressAndPort)
    {
        if (sender != hostName)
            return;

        string[] split = tunnelAddressAndPort.Split(':');
        if (split.Length < 2 || !int.TryParse(split[1], out int tunnelPort))
            return;

        string tunnelAddress = split[0];

        CnCNetTunnel tunnel = tunnelHandler.Tunnels.Find(t => t.Address == tunnelAddress && t.Port == tunnelPort);
        if (tunnel == null)
        {
            AddNotice(("The game host has selected an invalid tunnel server! " +
                "The game host needs to change the server or you will be unable " +
                "to participate in the match.").L10N("Client:Main:HostInvalidTunnel"),
                new ChatColor(255, 255, 0));
            CanLoad = false;
            RaiseChanged();
            return;
        }

        AddNotice(string.Format("The game host has changed the tunnel server to: {0}".L10N("Client:Main:HostChangeTunnel"), tunnel.Name));
        HandleTunnelServerChange(tunnel);
        CanLoad = true;
        RaiseChanged();
    }

    /// <summary>
    /// Changes the tunnel server used for the game.
    /// </summary>
    private void HandleTunnelServerChange(CnCNetTunnel tunnel)
    {
        tunnelHandler.CurrentTunnel = tunnel;

        negotiator.ApplyStaticTunnel(tunnel);
    }

    private void HandleNegotiationReportMessage(string sender, string data)
        => negotiator.HandleNegotiationReportMessage(sender, data);

    #endregion

    protected override void HostStartGame()
    {
        if (tunnelMode == TunnelMode.V3Dynamic && !negotiator.AreAllNegotiationsSuccessful())
        {
            AddNotice("Cannot start game: tunnel negotiations have not completed.".L10N("Client:Main:CannotStartNegotiationsIncomplete"), new ChatColor(255, 255, 0));
            return;
        }

        if (tunnelMode == TunnelMode.V2Legacy || tunnelHandler.CurrentTunnel?.Version == 2)
        {
            if (tunnelHandler.CurrentTunnel == null)
            {
                TunnelSelectionRequested?.Invoke(this, "No tunnel server is selected. Please pick one:".L10N("Client:Main:NoTunnelSelected"));
                return;
            }

            AddNotice("Contacting tunnel server...".L10N("Client:Main:ConnectingTunnel"));
            List<int> playerPorts = tunnelHandler.CurrentTunnel.GetPlayerPortInfo(SGPlayers.Count);

            if (playerPorts.Count < Players.Count)
            {
                TunnelSelectionRequested?.Invoke(this, "An error occured while contacting the CnCNet tunnel server.\nTry picking a different tunnel server:".L10N("Client:Main:ConnectTunnelError1"));
                AddNotice(("An error occured while contacting the specified CnCNet " +
                    "tunnel server. Please try using a different tunnel server").L10N("Client:Main:ConnectTunnelError2") + " ", new ChatColor(255, 255, 0));
                return;
            }

            var sb = new StringBuilder(START_GAME_CTCP_COMMAND + " ");
            for (int pId = 0; pId < Players.Count; pId++)
            {
                Players[pId].Port = playerPorts[pId];
                sb.Append(Players[pId].Name);
                sb.Append(";");
                sb.Append("0.0.0.0:");
                sb.Append(playerPorts[pId]);
                sb.Append(";");
            }
            sb.Remove(sb.Length - 1, 1);
            channel.SendCTCPMessage(sb.ToString(), QueuedMessageType.SYSTEM_MESSAGE, 9);

            AddNotice("Starting game...".L10N("Client:Main:StartingGame"));
            started = true;
            LoadGame();
        }
        else if (!negotiator.TryReserveGamePort())
        {
            AddNotice("Could not reserve a local port for the game tunnel bridge. Try again or restart the client.".L10N("Client:Main:GamePortReserveFailed"), ChatColor.Red);
        }
        else if (tunnelMode == TunnelMode.V3Dynamic && Players.Count > 1)
        {
            // Double-check everyone is still reachable before STARTV3 goes out over IRC —
            // IRC can take minutes to notice a dead connection, and a start command sent
            // to a player who never receives it strands the rest at the loading screen.
            if (negotiator.LaunchConnectivityCheckInProgress)
            {
                AddNotice("Still verifying player connections...".L10N("Client:Main:VerifyingConnectionsWait"), new ChatColor(255, 255, 0));
                return;
            }

            negotiator.BeginLaunchConnectivityCheck(FinishV3DynamicLaunch);
        }
        else
        {
            // V3 static (or dynamic with no other players)
            SendStartV3ToPlayers();
            AddNotice("Starting game...".L10N("Client:Main:StartingGame"));
            started = true;
            StartV3Game();
        }
    }

    /// <summary>
    /// Launch tail for V3 dynamic mode, run once the pre-launch connectivity check verifies
    /// that every player is still reachable.
    /// </summary>
    private void FinishV3DynamicLaunch()
    {
        SendStartV3ToPlayers();
        AddNotice("Starting game...".L10N("Client:Main:StartingGame"));
        started = true;
        StartV3Game();
    }

    protected override void WriteSpawnIniAdditions(IniFile spawnIni)
    {
        if (tunnelMode == TunnelMode.V2Legacy && tunnelHandler.CurrentTunnel != null)
        {
            spawnIni.SetStringValue("Tunnel", "Ip", tunnelHandler.CurrentTunnel.Address);
            spawnIni.SetIntValue("Tunnel", "Port", tunnelHandler.CurrentTunnel.Port);
        }
        else
        {
            PlayerInfo localPlayer = Players.Find(p => p.Name == ProgramConstants.PLAYERNAME);
            if (localPlayer != null)
            {
                // This is the client's own real, OS-assigned relay socket (see
                // TunnelHandler.ReserveLocalGamePort) — distinct from localPlayer.Port,
                // which is this player's deterministic in-game id, not a real endpoint.
                spawnIni.SetStringValue("Tunnel", "Ip", IPAddress.Loopback.ToString());
                spawnIni.SetIntValue("Tunnel", "Port", tunnelHandler.ReservedGamePort ?? localPlayer.Port);
            }
        }

        base.WriteSpawnIniAdditions(spawnIni);
    }

    protected override void HandleGameProcessExited()
    {
        tunnelHandler.StopGameBridge();
        base.HandleGameProcessExited();
        Clear();
    }

    private void BroadcastGame()
    {
        Channel broadcastChannel = connectionManager.FindChannel(gameCollection.GetGameBroadcastingChannelNameFromIdentifier(localGame));

        if (broadcastChannel == null || channel == null)
            return;

        var message = new GameBroadcastMessage(
            Revision: ProgramConstants.CNCNET_PROTOCOL_REVISION,
            GameVersion: ProgramConstants.GAME_VERSION,
            MaxPlayers: SGPlayers.Count,
            ChannelName: channel.ChannelName,
            RoomName: channel.UIName,
            Locked: started || Players.Count == SGPlayers.Count,
            IsCustomPassword: false,
            IsClosed: false,
            IsLoadedGame: true,
            IsLadder: false,
            Players: SGPlayers.Select(p => p.Name).ToList(),
            MapName: MapName,
            GameMode: GameMode,
            Tunnel: tunnelMode == TunnelMode.V3Dynamic
                ? GameBroadcastMessage.DYNAMIC_TUNNELS
                : tunnelHandler.CurrentTunnel != null
                    ? tunnelHandler.CurrentTunnel.Address + ":" + tunnelHandler.CurrentTunnel.Port
                    : "0.0.0.0:0",
            LoadedGameId: "0",
            SkillLevel: ClientConfiguration.Instance.DefaultSkillLevelIndex.ToString(CultureInfo.InvariantCulture), // we don't know the original skill level
            MapHash: SavedMapSHA1,
            GameOptionValues: SavedBroadcastOptionValues);

        broadcastChannel.SendCTCPMessage(message.Encode(), QueuedMessageType.SYSTEM_MESSAGE, 20);
    }

    protected override void UpdateDiscordPresence(bool resetTimer = false)
    {
        if (DiscordHandler == null || channel == null)
            return;

        PlayerInfo player = Players.Find(p => p.Name == ProgramConstants.PLAYERNAME);
        if (player == null)
            return;

        string currentState = ProgramConstants.IsInGame ? "In Game" : "In Lobby"; // not UI strings

        DiscordHandler.UpdatePresence(MapName, GameMode, "Multiplayer",
            currentState, Players.Count, SGPlayers.Count, channel.UIName, IsHost, resetTimer);
    }

    #region V3 Tunnel Support

    private void UpdateLoadGameButtonStatus()
    {
        if (IsHost)
            CanLoad = true;
    }

    // IV3NegotiationHost implementation — the shared negotiation orchestration lives in
    // V3TunnelNegotiationManager; these members supply lobby-specific transport and UI.
    List<PlayerInfo> IV3NegotiationHost.Players => Players;

    string IV3NegotiationHost.ChannelName => channel.ChannelName;

    TunnelMode IV3NegotiationHost.TunnelMode => tunnelMode;

    bool IV3NegotiationHost.IsHost => IsHost;

    void IV3NegotiationHost.SendNegotiationReport(string message)
        => channel.SendCTCPMessage(message, QueuedMessageType.GAME_NEGOTIATION_MESSAGE, 10);

    void IV3NegotiationHost.OnNegotiationStateChanged()
    {
        UpdateLoadGameButtonStatus();
        RaiseChanged();
    }

    void IV3NegotiationHost.OnLocalNegotiationStatus(PlayerInfo player, NegotiationStatus status, int ping)
    {
        if (status == NegotiationStatus.Succeeded)
        {
            CnCNetTunnel tunnel = negotiator.FindPlayer(player.Name)?.Tunnel;
            if (tunnel != null)
                AddNotice(string.Format("Tunnel negotiated with {0}: {1}".L10N("Client:Main:TunnelNegotiatedWith"), player.Name, tunnel.Name));
        }
    }

    void IV3NegotiationHost.OnRemoteNegotiationStatus(PlayerInfo player, NegotiationStatus status, int ping)
    {
    }

    void IV3NegotiationHost.OnNegotiationsRestarted()
    {
    }

    void IV3NegotiationHost.OnPairPingUpdated(PlayerInfo player, int ping)
    {
        // The loading lobby has no per-player ping display to refresh.
    }

    private void SendStartV3ToPlayers()
        => channel.SendCTCPMessage($"{START_GAME_V3_CTCP_COMMAND} {new StartV3Message(0, negotiator.GenerateV3StartEntries()).Encode()}",
            QueuedMessageType.SYSTEM_MESSAGE, 9);

    private void StartV3Game()
    {
        if (!negotiator.StartGameBridge())
        {
            AddNotice("Could not reserve a local port for the game tunnel bridge. Try again or restart the client.".L10N("Client:Main:GamePortReserveFailed"), ChatColor.Red);
            return;
        }

        LoadGame();
    }

    private void HandleTunnelModeChange(TunnelMode mode, bool isHostInitiated)
    {
        if (mode == tunnelMode)
            return;

        TunnelMode oldMode = tunnelMode;
        tunnelMode = mode;

        negotiator.ApplyModeTransition(oldMode, mode);

        string modeDescription = mode.GetDescription();
        AddNotice(isHostInitiated
            ? string.Format("Tunnel mode changed to {0}.".L10N("Client:Main:TunnelModeChanged"), modeDescription)
            : string.Format("The game host has changed tunnel mode to {0}.".L10N("Client:Main:TunnelModeChangedByHost"), modeDescription));

        if (mode == TunnelMode.V3Dynamic)
            tunnelHandler.CurrentTunnel = null;

        UpdateLoadGameButtonStatus();
    }

    #endregion
}
