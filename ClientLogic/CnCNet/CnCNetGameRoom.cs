using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Net;

using ClientCore;
using ClientCore.Extensions;

using ClientLogic.Launch;
using ClientLogic.Lobby;
using ClientLogic.Protocol;
using ClientLogic.Tunnels;
using ClientLogic.UI;

using DTAClient.Domain.Multiplayer;
using DTAClient.Domain.Multiplayer.CnCNet;
using DTAClient.Online;
using DTAClient.Online.EventArguments;

using Rampastring.Tools;

namespace ClientLogic.CnCNet;

/// <summary>
/// A CnCNet game room without a user interface: the XNA client's CnCNet game lobby protocol over an IRC channel
/// (CTCP messages), with the tunnel session and V3 negotiation. The front end calls <see cref="Update"/> regularly
/// for the game broadcasts. Map sharing, the inactive host check and the tunnel selection window are not supported
/// yet.
/// </summary>
public sealed class CnCNetGameRoom : MultiplayerLobbySession, IV3NegotiationHost, ITunnelSessionLobby, ILobbyTransport
{
    private const double GAME_BROADCAST_INTERVAL = 30.0;
    private const double GAME_BROADCAST_ACCELERATION = 10.0;
    private const double INITIAL_GAME_BROADCAST_DELAY = 10.0;

    private static readonly ChatColor ERROR_MESSAGE_COLOR = new(255, 255, 0);

    private const string CHEAT_DETECTED_MESSAGE = "CD";
    private const string DICE_ROLL_MESSAGE = CnCNetLobbySession.DICE_ROLL_MESSAGE;

    /// <summary>The window section the XNA CnCNet lobby reads its game options from.</summary>
    public const string WINDOW_NAME = "MultiplayerGameLobby";

    /// <summary>The layout INI the XNA CnCNet lobby reads (its IniNameOverride).</summary>
    public const string LAYOUT_INI_NAME = "CnCNetGameLobby";

    private readonly CnCNetManager connectionManager;
    private readonly TunnelHandler tunnelHandler;
    private readonly GameCollection gameCollection;
    private readonly CnCNetUserData cncnetUserData;
    private readonly string localGame;
    private readonly GameRoomSettings roomSettings = new();
    private readonly V3TunnelNegotiationManager negotiator;
    private readonly List<CtcpHandler> ctcpCommandHandlers;

    private Channel channel;
    private string hostName;
    private IRCColor chatColor;
    private string gameFilesHash;
    private bool closed;
    private bool broadcasting;
    private TimeSpan timeUntilGameBroadcast;

    public CnCNetGameRoom(CnCNetManager connectionManager, TunnelHandler tunnelHandler, IUiDispatcher uiDispatcher,
        GameCollection gameCollection, CnCNetUserData cncnetUserData, MapLoader mapLoader, GameProcessService gameProcess,
        IDialogService dialogs, ISoundService sounds, Random random)
        : base(WINDOW_NAME, mapLoader, gameProcess, dialogs, sounds, uiDispatcher, random, LAYOUT_INI_NAME)
    {
        this.connectionManager = connectionManager;
        this.tunnelHandler = tunnelHandler;
        this.gameCollection = gameCollection;
        this.cncnetUserData = cncnetUserData;
        localGame = ClientConfiguration.Instance.LocalGame;

        Session = new CnCNetLobbySession(() => channel, () => chatColor);
        LobbyState.RoomSettings = roomSettings;
        roomSettings.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(GameRoomSettings.RoomName) && channel != null)
                channel.UIName = roomSettings.RoomName;

            RaiseChanged();
        };

        negotiator = new V3TunnelNegotiationManager(this, tunnelHandler, uiDispatcher);
        TunnelSession = new TunnelSession(tunnelHandler, negotiator, this, this, this);
        TunnelSession.Start((TunnelMode)UserINISettings.Instance.TunnelMode.Value);
        TunnelSession.PlayerPingsChanged += _ => RaiseChanged();
        TunnelSession.PlayerDataChanged += RaiseChanged;
        TunnelSession.LaunchStatusChanged += RaiseChanged;

        // The XNA lobby's handlers, in its order and with its matching rules (the first match wins)
        ctcpCommandHandlers =
        [
            // Int handlers: a string handler for "R" would also take RETURN and RENEGALL
            CtcpHandler.Int("OR", (sender, options) => HandleOptionsRequest(sender, new PlayerOptionsRequestMessage(PackedPlayerOptions.Unpack(options)).Encode())),
            CtcpHandler.Int("R", (sender, readyState) => HandleReadyRequest(sender, new ReadyRequestMessage(readyState).Encode())),
            CtcpHandler.String("PO", ApplyPlayerOptions),
            CtcpHandler.String(PlayerExtraOptions.CNCNET_MESSAGE_KEY, HandlePlayerExtraOptions),
            CtcpHandler.String("GO", ApplyGameOptions),
            CtcpHandler.String("STARTV2", NonHostLaunchGameV2),
            CtcpHandler.String("STARTV3", NonHostLaunchGameV3),
            CtcpHandler.Notification("AISPECS", sender => HostNotice(sender, LaunchBlockerKind.AiSpectators)),
            CtcpHandler.Notification("GETREADY", sender => { if (sender == hostName) GetReadyNotification(); }),
            CtcpHandler.Notification("INSFSPLRS", sender => HostNotice(sender, LaunchBlockerKind.InsufficientPlayers)),
            CtcpHandler.Notification("TMPLRS", sender => HostNotice(sender, LaunchBlockerKind.TooManyPlayers)),
            CtcpHandler.Notification("CLRS", sender => HostNotice(sender, LaunchBlockerKind.SharedColors)),
            CtcpHandler.Notification("SLOC", sender => HostNotice(sender, LaunchBlockerKind.SharedStartingLocation)),
            CtcpHandler.Notification("LCKGME", sender => HostNotice(sender, LaunchBlockerKind.RoomNotLocked)),
            CtcpHandler.IntNotification("NVRFY", (sender, index) => HostNotice(sender, LaunchBlockerKind.NotVerified, index)),
            CtcpHandler.IntNotification("INGM", (sender, index) => HostNotice(sender, LaunchBlockerKind.StillInGame, index)),
            CtcpHandler.NoParam("STRTD", GameStartedNotification),
            CtcpHandler.NoParam("RETURN", ReturnNotification),
            CtcpHandler.Int("TNLPNG", TunnelSession.HandleTunnelPing),
            CtcpHandler.String("FHSH", FileHashNotification),
            CtcpHandler.String("MM", CheaterNotification),
            CtcpHandler.String(DICE_ROLL_MESSAGE, HandleDiceRollResult),
            CtcpHandler.NoParam(CHEAT_DETECTED_MESSAGE, HandleCheatDetectedMessage),
            CtcpHandler.String(TunnelNegotiationCommands.ChangeTunnelServer, TunnelSession.HandleTunnelServerChangeMessage),
            CtcpHandler.String(TunnelNegotiationCommands.NegotiationReport, TunnelSession.HandleNegotiationReportMessage),
            CtcpHandler.String(TunnelNegotiationCommands.RenegotiateAll, TunnelSession.HandleRenegotiateAll),
            CtcpHandler.String("GSETTINGS", ApplyGameLobbySettings),
        ];
    }

    protected override string LobbyTypeName => "CnCNetGameLobby";

    public TunnelSession TunnelSession { get; }

    public GameRoomSettings RoomSettings => roomSettings;

    public int PlayerLimit => roomSettings.PlayerLimit;

    public string HostName => hostName;

    public string ChannelName => channel?.ChannelName;

    /// <summary>The local player's IRC chat colour.</summary>
    public IRCColor IrcChatColor
    {
        get => chatColor;
        set => chatColor = value;
    }

    /// <summary>The launch button is blocked by a tunnel problem.</summary>
    public bool IsInTunnelError => TunnelSession.IsInTunnelError;

    protected override List<ChatBoxCommand> CreateChatCommands()
    {
        List<ChatBoxCommand> commands = base.CreateChatCommands();
        commands.Add(new ChatBoxCommand("TUNNELINFO",
            "View tunnel server information".L10N("Client:Main:TunnelInfoCommand"), false, PrintTunnelServerInformation));
        commands.Add(new ChatBoxCommand("RENEGOTIATE",
            "Force all players to renegotiate tunnel connections (V3 Dynamic, host only)".L10N("Client:Main:RenegotiateCommand"),
            true, RenegotiateAllCommand));
        return commands;
    }

    /// <summary>
    /// Opens the room on a channel the local player joined (the XNA lobby's SetUp); call <see cref="OnJoined"/> once
    /// the channel is joined.
    /// </summary>
    public void SetUp(Channel channel, bool isHost, int playerLimit, CnCNetTunnel tunnel, string hostName,
        bool isCustomPassword, int skillLevel)
    {
        this.channel = channel;
        channel.MessageAdded += Channel_MessageAdded;
        channel.CTCPReceived += Channel_CTCPReceived;
        channel.UserKicked += Channel_UserKicked;
        channel.UserQuitIRC += Channel_UserQuitIRC;
        channel.UserLeft += Channel_UserLeft;
        channel.UserAdded += Channel_UserAdded;
        channel.UserNameChanged += Channel_UserNameChanged;
        channel.UserListReceived += Channel_UserListReceived;

        this.hostName = hostName;
        roomSettings.RoomName = channel.UIName;
        roomSettings.PlayerLimit = playerLimit;
        roomSettings.SkillLevel = ClientConfiguration.Instance.NormalizeSkillLevel(skillLevel);
        roomSettings.IsCustomPassword = isCustomPassword;

        negotiator.RegenerateV3PlayerInfos();

        TunnelSession.Start(TunnelModeExtensions.FromTunnel(tunnel));

        if (isHost)
        {
            RandomSeed = Random.Next();
        }
        else
        {
            channel.ChannelModesChanged += Channel_ChannelModesChanged;
            AIPlayers.Clear();
        }

        if (TunnelSession.Mode != TunnelMode.V3Dynamic)
            tunnelHandler.CurrentTunnel = tunnel;

        tunnelHandler.CurrentTunnelPinged += TunnelHandler_CurrentTunnelPinged;
        connectionManager.ConnectionLost += ConnectionManager_ConnectionLost;
        connectionManager.Disconnected += ConnectionManager_Disconnected;

        SetUp(isHost);
    }

    private void TunnelHandler_CurrentTunnelPinged(object sender, EventArgs e) => TunnelSession.ReportCurrentTunnelPing();

    /// <summary>The channel was joined: the host sets the channel modes and starts announcing the game.</summary>
    public void OnJoined()
    {
        var fhc = new FileHashCalculator();
        fhc.CalculateHashes();

        gameFilesHash = fhc.GetCompleteHash();

        if (IsHost)
        {
            connectionManager.SendCustomMessage(new QueuedMessage(
                string.Format("MODE {0} +klnNs {1} {2}", channel.ChannelName,
                channel.Password, roomSettings.PlayerLimit),
                QueuedMessageType.SYSTEM_MESSAGE, 50));

            connectionManager.SendCustomMessage(new QueuedMessage(
                string.Format("TOPIC {0} :{1}", channel.ChannelName,
                ProgramConstants.CNCNET_PROTOCOL_REVISION + ";" + localGame.ToLower()),
                QueuedMessageType.SYSTEM_MESSAGE, 50));

            broadcasting = true;
            timeUntilGameBroadcast = TimeSpan.FromSeconds(INITIAL_GAME_BROADCAST_DELAY);
        }
        else
        {
            channel.SendCTCPMessage("FHSH " + gameFilesHash, QueuedMessageType.SYSTEM_MESSAGE, 10);
        }

        AutoReady = false;
        TunnelSession.ReportCurrentTunnelPing();
        RaiseChanged();
    }

    /// <summary>Called regularly by the front end: announces the game while hosting.</summary>
    public void Update(TimeSpan elapsed)
    {
        if (!broadcasting)
            return;

        timeUntilGameBroadcast -= elapsed;
        if (timeUntilGameBroadcast <= TimeSpan.Zero)
        {
            BroadcastGame();
            timeUntilGameBroadcast = TimeSpan.FromSeconds(GAME_BROADCAST_INTERVAL);
        }
    }

    private void AccelerateGameBroadcasting() =>
        timeUntilGameBroadcast -= TimeSpan.FromSeconds(GAME_BROADCAST_ACCELERATION);

    #region Room settings

    /// <summary>Host: changes the room name, player limit, skill level and password, and tells everyone.</summary>
    public void UpdateGameLobbySettings(string newGameRoomName, int newMaxPlayers, int newSkillLevel, string newPassword)
    {
        if (!IsHost)
            return;

        bool gameNameChanged = roomSettings.RoomName != newGameRoomName;
        bool maxPlayersChanged = roomSettings.PlayerLimit != newMaxPlayers;
        int normalizedSkillLevel = ClientConfiguration.Instance.NormalizeSkillLevel(newSkillLevel);
        bool skillLevelChanged = roomSettings.SkillLevel != normalizedSkillLevel;

        string currentUserPassword = roomSettings.IsCustomPassword ? channel.Password : string.Empty;
        bool passwordChanged = currentUserPassword != newPassword;

        if (newMaxPlayers < Players.Count + AIPlayers.Count)
        {
            AddNotice(string.Format("Cannot reduce maximum players to {0} with {1} players currently in game."
                .L10N("Client:Main:CannotReduceMaxPlayers"), newMaxPlayers, Players.Count + AIPlayers.Count));
            return;
        }

        string oldGameRoomName = roomSettings.RoomName;
        bool oldIsCustomPassword = roomSettings.IsCustomPassword;
        roomSettings.RoomName = newGameRoomName;
        roomSettings.PlayerLimit = newMaxPlayers;
        roomSettings.SkillLevel = normalizedSkillLevel;

        if (passwordChanged)
        {
            // An empty password means the password generated from the channel name
            string actualNewPassword = newPassword;
            if (string.IsNullOrEmpty(newPassword))
            {
                actualNewPassword = Utilities.CalculateSHA1ForString(channel.ChannelName).Substring(0, 10);
                roomSettings.IsCustomPassword = false;
            }
            else
            {
                roomSettings.IsCustomPassword = true;
            }

            channel.ChangePassword(actualNewPassword, 10);
        }

        BroadcastGameLobbySettings();

        if (gameNameChanged)
        {
            AddNotice(string.Format("Game room name changed from \"{0}\" to \"{1}\"."
                .L10N("Client:Main:GameNameChanged"), oldGameRoomName, roomSettings.RoomName));
        }

        if (maxPlayersChanged)
        {
            AddNotice(string.Format("Maximum players changed to {0}."
                .L10N("Client:Main:MaxPlayersChanged"), newMaxPlayers));
        }

        if (skillLevelChanged)
        {
            AddNotice(string.Format("Skill level changed to {0}."
                .L10N("Client:Main:SkillLevelChanged"), GameRoomSettings.GetSkillLevelName(roomSettings.SkillLevel)));
        }

        if (passwordChanged)
        {
            if (string.IsNullOrEmpty(newPassword))
                AddNotice("Password removed from the game.".L10N("Client:Main:PasswordRemoved"));
            else if (!oldIsCustomPassword)
                AddNotice("Password added to the game.".L10N("Client:Main:PasswordAdded"));
            else
                AddNotice("Password changed.".L10N("Client:Main:PasswordChanged"));
        }

        BroadcastGame();
    }

    /// <summary>Host: the room's custom password, or empty.</summary>
    public string CustomPassword => roomSettings.IsCustomPassword ? channel?.Password ?? string.Empty : string.Empty;

    private void BroadcastGameLobbySettings()
    {
        if (!IsHost)
            return;

        channel.SendCTCPMessage("GSETTINGS " + roomSettings.ToMessage().Encode(), QueuedMessageType.GAME_SETTINGS_MESSAGE, 11);
    }

    private void ApplyGameLobbySettings(string sender, string message)
    {
        if (IsHost || sender != hostName)
            return;

        if (!GameRoomSettingsMessage.TryDecode(message, out GameRoomSettingsMessage settings))
            return;

        foreach (string notice in roomSettings.ApplyFromHost(settings, sender))
            AddNotice(notice);
    }

    #endregion

    #region Leaving

    /// <summary>Leaves the room (the XNA lobby's LeaveGameLobby).</summary>
    public override void Leave()
    {
        if (IsHost)
        {
            closed = true;
            BroadcastGame();
        }

        Clear();
        channel?.Leave();
    }

    /// <summary>Closes the room (the XNA lobby's Clear).</summary>
    public void Clear()
    {
        if (!IsHost)
            AIPlayers.Clear();

        Players.Clear();

        negotiator.ClearAll();
        TunnelSession.Clear();

        if (channel != null)
        {
            channel.MessageAdded -= Channel_MessageAdded;
            channel.CTCPReceived -= Channel_CTCPReceived;
            channel.UserKicked -= Channel_UserKicked;
            channel.UserQuitIRC -= Channel_UserQuitIRC;
            channel.UserLeft -= Channel_UserLeft;
            channel.UserAdded -= Channel_UserAdded;
            channel.UserNameChanged -= Channel_UserNameChanged;
            channel.UserListReceived -= Channel_UserListReceived;
            channel.ChannelModesChanged -= Channel_ChannelModesChanged;

            connectionManager.RemoveChannel(channel);
        }

        connectionManager.ConnectionLost -= ConnectionManager_ConnectionLost;
        connectionManager.Disconnected -= ConnectionManager_Disconnected;

        broadcasting = false;
        closed = false;

        tunnelHandler.CurrentTunnel = null;
        tunnelHandler.CurrentTunnelPinged -= TunnelHandler_CurrentTunnelPinged;

        RaiseLeft(null);
    }

    private void ConnectionManager_Disconnected(object sender, EventArgs e) => Clear();

    private void ConnectionManager_ConnectionLost(object sender, ConnectionLostEventArgs e) => Clear();

    private void AbandonedByHost(string message)
    {
        connectionManager.MainChannel.AddMessage(new ChatMessage(ERROR_MESSAGE_COLOR, message));
        Leave();
    }

    #endregion

    #region Channel events

    private void Channel_UserNameChanged(object sender, UserNameChangedEventArgs e)
    {
        Logger.Log("CnCNetGameRoom: Nickname change: " + e.OldUserName + " to " + e.User.Name);
        PlayerInfo player = Players.Find(p => p.Name == e.OldUserName);
        if (player != null)
        {
            player.Name = e.User.Name;
            AddNotice(string.Format("Player {0} changed their name to {1}".L10N("Client:Main:PlayerRename"), e.OldUserName, e.User.Name));
            RaiseChanged();
        }
    }

    private void Channel_UserQuitIRC(object sender, UserNameEventArgs e)
    {
        RemovePlayer(e.UserName);

        if (e.UserName == hostName)
            AbandonedByHost("The game host abandoned the game.".L10N("Client:Main:HostAbandoned"));
    }

    private void Channel_UserLeft(object sender, UserNameEventArgs e)
    {
        RemovePlayer(e.UserName);

        if (e.UserName == hostName)
            AbandonedByHost("The game host abandoned the game.".L10N("Client:Main:HostAbandoned"));
    }

    private void Channel_UserKicked(object sender, UserNameEventArgs e)
    {
        if (e.UserName == ProgramConstants.PLAYERNAME)
        {
            connectionManager.MainChannel.AddMessage(new ChatMessage(
                ERROR_MESSAGE_COLOR, "You were kicked from the game!".L10N("Client:Main:YouWereKicked")));
            Clear();
            return;
        }

        int index = Players.FindIndex(p => p.Name == e.UserName);

        if (index > -1)
        {
            negotiator.RemovePlayer(e.UserName);
            Players.RemoveAt(index);
            NormalisePlayers();
            LobbyState.ClearReadyStatuses();
            RaiseChanged();
        }
    }

    private void Channel_UserListReceived(object sender, EventArgs e)
    {
        if (!IsHost && channel.Users.Find(hostName) == null)
            AbandonedByHost("The game host has abandoned the game.".L10N("Client:Main:HostHasAbandoned"));

        negotiator.RegenerateV3PlayerInfos();
    }

    private void Channel_UserAdded(object sender, ChannelUserEventArgs e)
    {
        var pInfo = new PlayerInfo(e.User.IRCUser.Name);
        Players.Add(pInfo);

        while (Players.Count + AIPlayers.Count > MAX_PLAYER_COUNT && AIPlayers.Count > 0)
            AIPlayers.RemoveAt(AIPlayers.Count - 1);

        Sounds.Play(LobbySound.PlayerJoined);

        negotiator.RegenerateV3PlayerInfos();
        NormalisePlayers();

        if (IsHost)
        {
            if (e.User.IRCUser.Name != ProgramConstants.PLAYERNAME)
            {
                // Changing the map applies forced settings (co-op sides etc.) to the
                // new player, and it also sends an options broadcast message
                ChangeMap(GameModeMap);
                BroadcastPlayerOptions();
                BroadcastPlayerExtraOptions();
            }
            else
            {
                Players[0].Ready = true;
            }

            if (RoomLock.IsFull(Players.Count, roomSettings.PlayerLimit))
            {
                AddNotice("Player limit reached. The game room has been locked.".L10N("Client:Main:GameRoomNumberLimitReached"));
                LockGame();
            }
        }

        RaiseChanged();
        negotiator.StartNegotiationForPlayerName(pInfo.Name);
    }

    private void RemovePlayer(string playerName)
    {
        PlayerInfo pInfo = Players.Find(p => p.Name == playerName);

        if (pInfo != null)
        {
            negotiator.RemovePlayer(playerName);

            Players.Remove(pInfo);
            NormalisePlayers();

            if (IsHost)
                BroadcastPlayerOptions();
        }

        Sounds.Play(LobbySound.PlayerLeft);

        if (IsHost && Locked && !ProgramConstants.IsInGame)
            UnlockGame(true);

        TunnelSession.ResetNegotiationsCompleteNotice();

        if (Players.Count > 1 && TunnelSession.Mode == TunnelMode.V3Dynamic)
            TunnelSession.CheckAllNegotiationsComplete();

        RaiseChanged();
    }

    private void Channel_ChannelModesChanged(object sender, ChannelModeEventArgs e)
    {
        if (e.ModeString == "+i")
        {
            if (RoomLock.IsFull(Players.Count, roomSettings.PlayerLimit))
                AddNotice("Player limit reached. The game room has been locked.".L10N("Client:Main:GameRoomNumberLimitReached"));
            else
                AddNotice("The game host has locked the game room.".L10N("Client:Main:RoomLockedByHost"));
            LobbyState.Locked = true;
        }
        else if (e.ModeString == "-i")
        {
            AddNotice("The game room has been unlocked.".L10N("Client:Main:GameRoomUnlocked"));
            LobbyState.Locked = false;
        }

        RaiseChanged();
    }

    private void Channel_CTCPReceived(object sender, ChannelCTCPEventArgs e)
    {
        Logger.Log("CnCNetGameRoom_CTCPReceived");

        foreach (CtcpHandler handler in ctcpCommandHandlers)
        {
            if (handler.Handle(e.UserName, e.Message))
            {
                RaiseChanged();
                return;
            }
        }

        Logger.Log("Unhandled CTCP command: " + e.Message + " from " + e.UserName);
    }

    private void Channel_MessageAdded(object sender, IRCMessageEventArgs e)
    {
        if (cncnetUserData.IsIgnored(e.Message.SenderIdent))
        {
            AddChatMessage(new ChatMessage(new ChatColor(192, 192, 192),
                string.Format("Message blocked from {0}".L10N("Client:Main:MessageBlockedFromPlayer"), e.Message.SenderName)));
        }
        else
        {
            AddChatMessage(e.Message);

            if (e.Message.SenderName != null)
                Sounds.Play(LobbySound.Message);
        }
    }

    #endregion

    #region Host: players

    private void HandleOptionsRequest(string playerName, string message)
    {
        if (!IsHost || ProgramConstants.IsInGame)
            return;

        if (PlayerOptionsRequestMessage.TryDecode(message, out PlayerOptionsRequestMessage request))
            HandlePlayerOptionsRequest(playerName, request.Options);
    }

    private void HandleReadyRequest(string playerName, string message)
    {
        if (!IsHost)
            return;

        if (ReadyRequestMessage.TryDecode(message, out ReadyRequestMessage request))
            HandleReadyRequest(playerName, request.ReadyState);
    }

    public override void BroadcastPlayerOptions()
    {
        if (!IsHost)
            return;

        var entries = Players.Concat(AIPlayers).Select(pInfo => new PlayerOptionsEntry(
            pInfo.IsAI ? string.Empty : pInfo.Name,
            pInfo.IsAI ? pInfo.AILevel : -1,
            new PackedPlayerOptions(pInfo.SideId, pInfo.ColorId, pInfo.StartingLocation, pInfo.TeamId),
            pInfo.IsAI ? 1 : pInfo.AutoReady && !pInfo.IsInGame && !LastMapChangeWasInvalid ? 2 : Convert.ToInt32(pInfo.Ready),
            string.Empty)).ToList();

        Session.SendPlayerOptions(new PlayerOptionsMessage(entries));
    }

    protected override void KickPlayer(int playerIndex)
    {
        if (playerIndex >= Players.Count)
            return;

        PlayerInfo pInfo = Players[playerIndex];

        AddNotice(string.Format("Kicking {0} from the game...".L10N("Client:Main:KickPlayer"), pInfo.Name));
        channel.SendKickMessage(pInfo.Name, 8);
    }

    protected override void BanPlayer(int playerIndex)
    {
        if (playerIndex >= Players.Count)
            return;

        PlayerInfo pInfo = Players[playerIndex];
        IRCUser user = connectionManager.UserList.Find(u => u.Name == pInfo.Name);

        if (user != null)
        {
            AddNotice(string.Format("Banning and kicking {0} from the game...".L10N("Client:Main:BanAndKickPlayer"), pInfo.Name));
            channel.SendBanMessage(user.Hostname, 8);
            channel.SendKickMessage(user.Name, 8);
        }
    }

    /// <summary>Host: locks or unlocks the room (the lock button).</summary>
    public void ToggleLock()
    {
        if (!IsHost)
            return;

        switch (LobbyState.LockButtonAction(roomSettings.PlayerLimit))
        {
            case LockButtonAction.Lock:
                AddNotice("You've locked the game room.".L10N("Client:Main:RoomLockedByYou"));
                LockGame();
                break;
            case LockButtonAction.Unlock:
                AddNotice("You've unlocked the game room.".L10N("Client:Main:RoomUnlockedByYou"));
                UnlockGame(false);
                break;
            case LockButtonAction.RefuseUnlock:
                AddNotice(string.Format(
                    "Cannot unlock game; the player limit ({0}) has been reached.".L10N("Client:Main:RoomCantUnlockAsLimit"), roomSettings.PlayerLimit));
                break;
        }
    }

    private void LockGame()
    {
        connectionManager.SendCustomMessage(new QueuedMessage(
            string.Format("MODE {0} +i", channel.ChannelName), QueuedMessageType.INSTANT_MESSAGE, -1));

        LobbyState.Locked = true;
        AccelerateGameBroadcasting();
        RaiseChanged();
    }

    private void UnlockGame(bool announce)
    {
        connectionManager.SendCustomMessage(new QueuedMessage(
            string.Format("MODE {0} -i", channel.ChannelName), QueuedMessageType.INSTANT_MESSAGE, -1));

        LobbyState.Locked = false;
        if (announce)
            AddNotice("The game room has been unlocked.".L10N("Client:Main:GameRoomUnlocked"));
        AccelerateGameBroadcasting();
        RaiseChanged();
    }

    #endregion

    #region Player: options from the host

    private void HandlePlayerExtraOptions(string sender, string message)
    {
        if (sender == hostName)
            ApplyPlayerExtraOptions(message);
    }

    private void ApplyPlayerOptions(string sender, string message)
    {
        if (sender != hostName)
            return;

        if (!PlayerOptionsMessage.TryDecode(message, out PlayerOptionsMessage playerOptions))
            return;

        // Check every player before touching the player list, so a bad message changes nothing
        foreach (PlayerOptionsEntry entry in playerOptions.Players)
        {
            PackedPlayerOptions options = entry.Options;

            if (options.Side > Sides.Count + SlotIndices.RandomSelectorCount)
                return;

            if (options.Color > MPColors.Count)
                return;

            if (options.Start > MAX_PLAYER_COUNT)
                return;

            if (options.Team > 4)
                return;
        }

        var savedPings = Players.ToDictionary(p => p.Name, p => p.Ping);

        // "PO" rebuilds the player list from scratch, so carry over the in-game state the host's message doesn't include
        var savedInGameStatuses = Players.ToDictionary(p => p.Name, p => p.IsInGame);

        Players.Clear();
        AIPlayers.Clear();

        foreach (PlayerOptionsEntry entry in playerOptions.Players)
        {
            var pInfo = new PlayerInfo();

            if (entry.IsAI)
            {
                pInfo.IsAI = true;
                pInfo.AILevel = entry.AILevel;
                pInfo.Name = ProgramConstants.GetAILevelName(entry.AILevel);
            }
            else
            {
                pInfo.Name = entry.Name;

                // A player not in the channel has left or was kicked before the message arrived
                if (channel.Users.Find(entry.Name) == null)
                    continue;
            }

            pInfo.TeamId = entry.Options.Team;
            pInfo.StartingLocation = entry.Options.Start;
            pInfo.ColorId = entry.Options.Color;
            pInfo.SideId = entry.Options.Side;

            if (pInfo.IsAI)
            {
                pInfo.Ready = true;
                AIPlayers.Add(pInfo);
            }
            else
            {
                int readyStatus = entry.ReadyState;

                pInfo.Ready = readyStatus > 0;
                pInfo.AutoReady = readyStatus > 1;

                if (savedPings.TryGetValue(pInfo.Name, out PingValue savedPing))
                    pInfo.Ping = savedPing;

                if (savedInGameStatuses.TryGetValue(pInfo.Name, out bool savedInGame))
                    pInfo.IsInGame = savedInGame;

                Players.Add(pInfo);
            }
        }

        negotiator.RegenerateV3PlayerInfos();

        NormalisePlayers();
        RaiseChanged();

        // Joining gets the existing players here: negotiate with the ones not negotiated with yet
        negotiator.StartPendingNegotiations();
    }

    protected override GameOptionsMessage CreateGameOptionsMessage() =>
        base.CreateGameOptionsMessage() with { TunnelMode = (int)TunnelSession.Mode };

    private void ApplyGameOptions(string sender, string message)
    {
        if (sender != hostName)
            return;

        if (!GameOptionsMessage.TryDecode(message, Options.CheckBoxes.Count, Options.DropDowns.Count, out GameOptionsMessage gameOptions))
        {
            AddNotice(("The game host has sent an invalid game options message! " +
                "The game host's game version might be different from yours.").L10N("Client:Main:HostGameOptionInvalid"), ChatColor.Red);
            return;
        }

        GameOptionsUpdate update = GameOptionsApplier.Plan(gameOptions,
            new LobbyGameSettings(FrameSendRate, MaxAhead, ProtocolVersion, GameModeMap),
            (gameMode, mapSHA1) => GameModeMaps.FirstOrDefault(gmm => gmm.GameMode.Name == gameMode && gmm.Map.SHA1 == mapSHA1),
            isMapSharingEnabled: false);

        ApplyGameOptionsUpdate(update);

        TunnelSession.ChangeMode((TunnelMode)update.TunnelMode, false);
    }

    /// <summary>Asks the host to toggle the local player's ready state (the CnCNet lobby toggles, unlike LAN).</summary>
    public override void RequestReady()
    {
        if (GameModeMap == null)
        {
            AddNotice(("The game host needs to select a different map or " +
                "you will be unable to participate in the match.").L10N("Client:Main:HostMustReplaceMap"));

            if (AutoReady)
                Session.RequestReady(0);

            return;
        }

        PlayerInfo pInfo = FindLocalPlayer();
        if (pInfo == null)
            return;

        int readyState = 0;

        if (AutoReady)
            readyState = 2;
        else if (!pInfo.Ready)
            readyState = 1;

        Session.RequestReady(readyState);
    }

    #endregion

    #region Notifications

    /// <summary>Shows a launch blocker the host announced.</summary>
    private void HostNotice(string sender, LaunchBlockerKind kind, int playerIndex = -1)
    {
        if (sender == hostName)
            ShowLaunchBlocker(new LaunchBlocker(kind, playerIndex));
    }

    protected override void ShowLaunchBlocker(LaunchBlocker blocker)
    {
        base.ShowLaunchBlocker(blocker);

        if (!IsHost)
            return;

        string message = blocker.Kind switch
        {
            LaunchBlockerKind.AiSpectators => "AISPECS",
            LaunchBlockerKind.InsufficientPlayers => "INSFSPLRS",
            LaunchBlockerKind.TooManyPlayers => "TMPLRS",
            LaunchBlockerKind.SharedColors => "CLRS",
            LaunchBlockerKind.SharedStartingLocation => "SLOC",
            LaunchBlockerKind.RoomNotLocked => "LCKGME",
            LaunchBlockerKind.NotVerified => "NVRFY " + blocker.PlayerIndex,
            LaunchBlockerKind.StillInGame => "INGM " + blocker.PlayerIndex,
            _ => null,
        };

        if (message != null)
            channel.SendCTCPMessage(message, QueuedMessageType.GAME_NOTIFICATION_MESSAGE, 0);
    }

    public override void GetReadyNotification()
    {
        base.GetReadyNotification();

        if (IsHost)
            channel.SendCTCPMessage("GETREADY", QueuedMessageType.GAME_GET_READY_MESSAGE, 0);
    }

    private void GameStartedNotification(string sender)
    {
        PlayerInfo pInfo = Players.Find(p => p.Name == sender);

        if (pInfo != null)
            pInfo.IsInGame = true;

        RaiseChanged();
    }

    private void FileHashNotification(string sender, string filesHash)
    {
        if (!IsHost)
            return;

        PlayerInfo pInfo = Players.Find(p => p.Name == sender);

        if (pInfo != null)
            pInfo.HashReceived = true;

        RaiseChanged();

        if (filesHash != gameFilesHash)
        {
            channel.SendCTCPMessage("MM " + sender, QueuedMessageType.GAME_CHEATER_MESSAGE, 10);
            CheaterNotification(ProgramConstants.PLAYERNAME, sender);
        }
    }

    private void CheaterNotification(string sender, string cheaterName)
    {
        if (sender != hostName)
            return;

        AddNotice(string.Format("Player {0} has different files compared to the game host. Either {0} or the game host could be cheating.".L10N("Client:Main:DifferentFileCheating"), cheaterName), ChatColor.Red);
    }

    private void HandleCheatDetectedMessage(string sender) =>
        AddNotice(string.Format("{0} has modified game files during the client session. They are likely attempting to cheat!".L10N("Client:Main:PlayerModifyFileCheat"), sender), ChatColor.Red);

    protected override void OnLocalDiceRoll(int dieSides, int[] results)
    {
        // Other players' clients print CTCP rolls; the channel doesn't echo them back to the sender
        AddNotice(DiceRoll.FormatResult(ProgramConstants.PLAYERNAME, dieSides, results));
    }

    #endregion

    #region Tunnels

    private void PrintTunnelServerInformation(string s)
    {
        if (TunnelSession.Mode == TunnelMode.V3Dynamic)
        {
            AddNotice("V3 Tunnel Mode - Per-player tunnel information:".L10N("Client:Main:V3TunnelHeader"));

            foreach (V3PlayerInfo v3Player in negotiator.PlayerInfos.Where(p => p.Name != ProgramConstants.PLAYERNAME))
            {
                CnCNetTunnel t = v3Player.Tunnel;

                if (t != null)
                {
                    PingValue? negotiatedPing = negotiator.NegotiationData.GetPing(ProgramConstants.PLAYERNAME, v3Player.Name);
                    string pingDisplay = negotiatedPing.HasValue ? negotiatedPing.Value.ToString() : "Unknown".L10N("Client:Main:UnknownPing");

                    AddNotice(string.Format(
                        "{0}: {1} {2} (Ping: {3}) (Players: {4}/{5}) (Official: {6}) Version: {7}".L10N("Client:Main:V3TunnelInfo"),
                        v3Player.Name, t.Name, t.Country, pingDisplay, t.Clients, t.MaxClients, t.Official, t.Version.ToString()));
                }
                else
                {
                    AddNotice(string.Format("{0}: Not negotiated yet".L10N("Client:Main:V3TunnelNotNegotiated"), v3Player.Name));
                }
            }
        }
        else if (tunnelHandler.CurrentTunnel == null)
        {
            AddNotice("Tunnel server unavailable!".L10N("Client:Main:TunnelUnavailable"));
        }
        else
        {
            CnCNetTunnel t = tunnelHandler.CurrentTunnel;

            AddNotice(string.Format(
                "Current tunnel server: {0} {1} (Players: {2}/{3}) (Official: {4}) Version: {5}".L10N("Client:Main:TunnelInfo"),
                t.Name, t.Country, t.Clients, t.MaxClients, t.Official, t.Version.ToString()));
        }
    }

    private void RenegotiateAllCommand(string parameters)
    {
        if (TunnelSession.Mode != TunnelMode.V3Dynamic)
        {
            AddNotice("Renegotiate is only available when using dynamic tunnels.".L10N("Client:Main:RenegotiateOnlyDynamic"));
            return;
        }

        if (!IsHost)
        {
            AddNotice("Only the host can request a renegotiation.".L10N("Client:Main:RenegotiateHostOnly"));
            return;
        }

        TunnelSession.TriggerRenegotiateAll();
    }

    List<PlayerInfo> IV3NegotiationHost.Players => Players;

    string IV3NegotiationHost.ChannelName => channel.ChannelName;

    TunnelMode IV3NegotiationHost.TunnelMode => TunnelSession.Mode;

    bool IV3NegotiationHost.IsHost => IsHost;

    void IV3NegotiationHost.SendNegotiationReport(string message)
        => channel.SendCTCPMessage(message, QueuedMessageType.GAME_NEGOTIATION_MESSAGE, 10);

    void IV3NegotiationHost.OnNegotiationStateChanged() => RaiseChanged();

    void IV3NegotiationHost.OnLocalNegotiationStatus(PlayerInfo player, NegotiationStatus status, int ping)
    {
        if (status == NegotiationStatus.Succeeded)
            RefreshV3PlayerPing(player, ping);

        RaiseChanged();
    }

    void IV3NegotiationHost.OnRemoteNegotiationStatus(PlayerInfo player, NegotiationStatus status, int ping)
    {
        if (ping >= 0)
            RefreshV3PlayerPing(player, ping);

        RaiseChanged();
    }

    void IV3NegotiationHost.OnNegotiationsRestarted() => TunnelSession.ResetNegotiationsCompleteNotice();

    void IV3NegotiationHost.OnPairPingUpdated(PlayerInfo player, int ping)
    {
        RefreshV3PlayerPing(player, ping);
        RaiseChanged();
    }

    void INoticeSink.AddNotice(string message, NoticeSeverity severity) => AddNotice(message, severity switch
    {
        NoticeSeverity.Success => new ChatColor(144, 238, 144),
        NoticeSeverity.Warning => new ChatColor(255, 255, 0),
        NoticeSeverity.Degraded => new ChatColor(255, 165, 0),
        NoticeSeverity.Error => ChatColor.Red,
        _ => ChatColor.White,
    });

    /// <summary>Sets the roster ping from the merged pair ping, as the negotiation status shows it.</summary>
    private void RefreshV3PlayerPing(PlayerInfo player, int eventPing)
    {
        PingValue? pairPing = negotiator.NegotiationData.GetPing(ProgramConstants.PLAYERNAME, player.Name);
        player.Ping = pairPing ?? (eventPing >= 0 ? PingValue.FromMs(eventPing) : PingValue.Unknown);
    }

    List<PlayerInfo> ITunnelSessionLobby.Players => Players;

    bool ITunnelSessionLobby.IsHost => IsHost;

    string ITunnelSessionLobby.HostName => hostName;

    void ILobbyTransport.SendSystemMessage(string message) =>
        channel.SendCTCPMessage(message, QueuedMessageType.SYSTEM_MESSAGE, 10);

    #endregion

    #region Launch

    protected override void HostLaunchGame()
    {
        if (negotiator.LaunchConnectivityCheckInProgress)
        {
            AddNotice("Still verifying player connections...".L10N("Client:Main:VerifyingConnectionsWait"), new ChatColor(255, 255, 0));
            return;
        }

        if (TunnelSession.Mode == TunnelMode.V3Dynamic && !negotiator.AreAllNegotiationsSuccessful())
        {
            var (incomplete, failed) = negotiator.NegotiationData.GetNegotiationStatusCounts(Players.Select(p => p.Name).ToList());

            if (failed > 0)
            {
                AddNotice("Cannot start game: Some tunnel negotiations have failed.".L10N("Client:Main:CannotStartNegotiationsFailed"), ChatColor.Red);
                ShowFailedNegotiations();
                return;
            }

            if (incomplete > 0)
            {
                var incompleteNegotiations = negotiator.NegotiationData.GetIncompleteNegotiations(Players.Select(p => p.Name).ToList());
                AddNotice("Waiting for negotiations between:".L10N("Client:Main:WaitingForNegotiations"), new ChatColor(255, 255, 0));
                foreach (var (p1, p2, status) in incompleteNegotiations)
                    AddNotice($"  {p1} <-> {p2} ({status.GetDescription()})", new ChatColor(255, 255, 0));
                return;
            }
        }

        if (Players.Count > 1)
        {
            // With V2 tunnels the ports come from the tunnel server; V3 tunnels register on the fly
            if (tunnelHandler.CurrentTunnel?.Version == 2)
            {
                AddNotice("Contacting V2 tunnel server...".L10N("Client:Main:ConnectingTunnelV2"));

                List<int> playerPorts = tunnelHandler.CurrentTunnel.GetPlayerPortInfo(Players.Count);

                if (playerPorts.Count < Players.Count)
                {
                    AddNotice(("An error occured while contacting the specified CnCNet " +
                        "tunnel server. Please try using a different tunnel server").L10N("Client:Main:ConnectTunnelError2") + " ", ERROR_MESSAGE_COLOR);
                    return;
                }

                SendStartV2ToPlayers(playerPorts);
            }
            else if (!negotiator.TryReserveGamePort())
            {
                AddNotice("Could not reserve a local port for the game tunnel bridge. Try again or restart the client.".L10N("Client:Main:GamePortReserveFailed"), ERROR_MESSAGE_COLOR);
                return;
            }
            else if (TunnelSession.Mode == TunnelMode.V3Dynamic)
            {
                // Check everyone is still reachable before STARTV3 goes out; the launch continues in FinishV3DynamicLaunch
                negotiator.BeginLaunchConnectivityCheck(FinishV3DynamicLaunch);
                return;
            }
            else if (tunnelHandler.CurrentTunnel?.Version == 3)
            {
                SendStartV3ToPlayers();
            }
        }

        cncnetUserData.AddRecentPlayers(Players.Select(p => p.Name), channel.UIName);

        StartCnCNetGame();
    }

    private void FinishV3DynamicLaunch()
    {
        SendStartV3ToPlayers();
        cncnetUserData.AddRecentPlayers(Players.Select(p => p.Name), channel.UIName);
        StartCnCNetGame();
    }

    private void SendStartV2ToPlayers(List<int> playerPorts)
    {
        var entries = new List<StartV2Entry>(Players.Count);
        for (int pId = 0; pId < Players.Count; pId++)
        {
            Players[pId].Port = playerPorts[pId];
            entries.Add(new StartV2Entry(Players[pId].Name, tunnelHandler.CurrentTunnel.Address, playerPorts[pId]));
        }

        channel.SendCTCPMessage("STARTV2 " + new StartV2Message(UniqueGameID, entries).Encode(), QueuedMessageType.SYSTEM_MESSAGE, 10);
    }

    private void SendStartV3ToPlayers()
    {
        var message = new StartV3Message(UniqueGameID, negotiator.GenerateV3StartEntries());
        channel.SendCTCPMessage("STARTV3 " + message.Encode(), QueuedMessageType.SYSTEM_MESSAGE, 10);
    }

    private void ShowFailedNegotiations()
    {
        var failedPairs = negotiator.NegotiationData.GetFailedPairs(Players.Select(p => p.Name).ToList());

        if (failedPairs.Count > 0)
        {
            AddNotice("Failed negotiations between:".L10N("Client:Main:FailedNegotiationsBetween"), ChatColor.Red);
            foreach (var (p1, p2) in failedPairs)
                AddNotice($" {p1} <-> {p2}", ChatColor.Red);
            AddNotice("Use the Renegotiate All button (or type /renegotiate) to retry. If failures persist, consider changing tunnel mode or having the affected players rejoin.".L10N("Client:Main:RenegotiateHint"), new ChatColor(255, 255, 0));
        }
    }

    private void NonHostLaunchGameV2(string sender, string message)
    {
        if (sender != hostName)
            return;

        if (GameModeMap == null)
        {
            ResetGameState();
            return;
        }

        if (!StartV2Message.TryDecode(message, out StartV2Message start))
            return;

        UniqueGameID = start.GameId;

        var recentPlayers = new List<string>();

        foreach (StartV2Entry entry in start.Players)
        {
            string pName = entry.Name;
            string tunnelAddress = entry.TunnelAddress;

            // The host's tunnel address is authoritative
            if (pName == ProgramConstants.PLAYERNAME &&
                !string.Equals(tunnelHandler.CurrentTunnel?.Address, tunnelAddress, StringComparison.OrdinalIgnoreCase))
            {
                CnCNetTunnel matchedTunnel = tunnelHandler.Tunnels.FirstOrDefault(t => t.Version == 2 &&
                    string.Equals(t.Address, tunnelAddress, StringComparison.OrdinalIgnoreCase));

                if (matchedTunnel != null)
                {
                    Logger.Log($"NonHostLaunchGameV2: Correcting tunnel to host-specified {matchedTunnel.Name} ({tunnelAddress}).");
                    tunnelHandler.CurrentTunnel = matchedTunnel;
                }
                else
                {
                    AddNotice(("Failed to match the tunnel address provided by the host to any " +
                        "available tunnel server. The game cannot be started.").L10N("Client:Main:TunnelErrorMessage"),
                        ERROR_MESSAGE_COLOR);
                    Logger.Log("NonHostLaunchGameV2: Failed to match tunnel address: " + tunnelAddress);
                    return;
                }
            }

            PlayerInfo pInfo = Players.Find(p => p.Name == pName);

            if (pInfo == null)
                return;

            pInfo.Port = entry.Port;
            recentPlayers.Add(pName);
        }

        cncnetUserData.AddRecentPlayers(recentPlayers, channel.UIName);

        StartCnCNetGame();
    }

    private void NonHostLaunchGameV3(string sender, string message)
    {
        if (sender != hostName)
            return;

        if (ProgramConstants.IsInGame)
        {
            Logger.Log("NonHostLaunchGameV3: Ignoring game start while still in a running game.");
            NotifyStartFailed();
            return;
        }

        if (GameModeMap == null)
        {
            ResetGameState();
            return;
        }

        if (!StartV3Message.TryDecode(message, out StartV3Message start))
        {
            Logger.Log("NonHostLaunchGameV3: Invalid start message.");
            NotifyStartFailed();
            return;
        }

        if (start.Players.Count != Players.Count)
        {
            Logger.Log($"NonHostLaunchGameV3: Invalid start message: expected {Players.Count} players, got {start.Players.Count}.");
            NotifyStartFailed();
            return;
        }

        UniqueGameID = start.GameId;

        var recentPlayers = new List<string>();

        for (int i = 0; i < Players.Count; i++)
        {
            if (!negotiator.ApplyV3StartEntry(start.Players[i], i))
            {
                Logger.Log($"NonHostLaunchGameV3: Could not apply start entry for player at position {i}.");
                NotifyStartFailed();
                return;
            }

            recentPlayers.Add(start.Players[i].Name);
        }

        cncnetUserData.AddRecentPlayers(recentPlayers, channel.UIName);
        StartCnCNetGame();
    }

    private void NotifyStartFailed()
    {
        AddNotice(("Failed to process the game start message from the host. The game was started " +
            "without you; the host's player list may be out of sync with yours. Try rejoining the game.").L10N("Client:Main:StartMessageInvalid"),
            ERROR_MESSAGE_COLOR);
    }

    /// <summary>The XNA lobby's StartGame: file check, tunnel bridge, STRTD, then the launch.</summary>
    private void StartCnCNetGame()
    {
        AddNotice("Starting game...".L10N("Client:Main:StartingGame"));

        var fhc = new FileHashCalculator();
        fhc.CalculateHashes();

        if (gameFilesHash != fhc.GetCompleteHash())
        {
            Logger.Log("Game files modified during client session!");
            channel.SendCTCPMessage(CHEAT_DETECTED_MESSAGE, QueuedMessageType.INSTANT_MESSAGE, 0);
            HandleCheatDetectedMessage(ProgramConstants.PLAYERNAME);
        }

        if (TunnelSession.Mode == TunnelMode.V3Dynamic || tunnelHandler.CurrentTunnel?.Version == 3)
        {
            if (FindLocalPlayer() == null)
            {
                Logger.Log("Could not find local player.");
                return;
            }

            if (!negotiator.StartGameBridge())
            {
                AddNotice("Could not reserve a local port for the game tunnel bridge. Try again or restart the client.".L10N("Client:Main:GamePortReserveFailed"), ERROR_MESSAGE_COLOR);
                return;
            }
        }

        channel.SendCTCPMessage("STRTD", QueuedMessageType.SYSTEM_MESSAGE, 20);

        StartMultiplayerGame();
    }

    protected override void HandleGameProcessExited() => ResetGameState();

    private void ResetGameState()
    {
        base.HandleGameProcessExited();

        tunnelHandler.StopGameBridge();

        channel.SendCTCPMessage("RETURN", QueuedMessageType.SYSTEM_MESSAGE, 20);
        ReturnNotification(ProgramConstants.PLAYERNAME);

        if (IsHost)
        {
            RandomSeed = Random.Next();
            OnGameOptionChanged();
            LobbyState.ClearReadyStatuses();
            NormalisePlayers();
            BroadcastPlayerOptions();
            BroadcastPlayerExtraOptions();

            if (!RoomLock.IsFull(Players.Count, roomSettings.PlayerLimit))
                UnlockGame(true);
        }

        RaiseChanged();
    }

    protected override void WriteSpawnIniAdditions(IniFile iniFile)
    {
        base.WriteSpawnIniAdditions(iniFile);

        PlayerInfo localPlayer = FindLocalPlayer();
        if (localPlayer == null)
            return;

        if (TunnelSession.Mode != TunnelMode.V2Legacy)
        {
            // Tell the game to connect to our bridge
            iniFile.SetStringValue("Tunnel", "Ip", IPAddress.Loopback.ToString());
            iniFile.SetIntValue("Tunnel", "Port", tunnelHandler.ReservedGamePort ?? localPlayer.Port);
        }
        else if (tunnelHandler.CurrentTunnel != null)
        {
            iniFile.SetStringValue("Tunnel", "Ip", tunnelHandler.CurrentTunnel.Address);
            iniFile.SetIntValue("Tunnel", "Port", tunnelHandler.CurrentTunnel.Port);
        }

        iniFile.SetIntValue("Settings", "GameID", UniqueGameID);
        iniFile.SetBooleanValue("Settings", "Host", IsHost);
        iniFile.SetIntValue("Settings", "Port", localPlayer.Port);
    }

    protected override void AddLaunchCaptureInputs(IDictionary<string, object> inputs)
    {
        base.AddLaunchCaptureInputs(inputs);
        inputs["UniqueGameID"] = UniqueGameID;
        inputs["TunnelMode"] = TunnelSession.Mode.ToString();
        inputs["TunnelAddress"] = tunnelHandler.CurrentTunnel?.Address;
        inputs["TunnelPort"] = tunnelHandler.CurrentTunnel?.Port;
        inputs["ReservedGamePort"] = tunnelHandler.ReservedGamePort;
    }

    protected override string GetIPAddressForPlayer(PlayerInfo player) => "0.0.0.0";

    #endregion

    #region Game broadcasting

    private void BroadcastGame()
    {
        Channel broadcastChannel = connectionManager.FindChannel(gameCollection.GetGameBroadcastingChannelNameFromIdentifier(localGame));

        if (broadcastChannel == null)
            return;

        if (ProgramConstants.IsInGame && broadcastChannel.Users.Count > 500)
            return;

        var message = new GameBroadcastMessage(
            Revision: ProgramConstants.CNCNET_PROTOCOL_REVISION,
            GameVersion: ProgramConstants.GAME_VERSION,
            MaxPlayers: roomSettings.PlayerLimit,
            ChannelName: channel.ChannelName,
            RoomName: roomSettings.RoomName,
            Locked: Locked,
            IsCustomPassword: roomSettings.IsCustomPassword,
            IsClosed: closed,
            IsLoadedGame: false,
            IsLadder: false,
            Players: Players.Select(p => p.Name).ToList(),
            MapName: GameModeMap?.Map?.UntranslatedName ?? string.Empty,
            GameMode: GameModeMap?.GameMode?.UntranslatedUIName ?? string.Empty,
            Tunnel: TunnelSession.Mode == TunnelMode.V3Dynamic
                ? GameBroadcastMessage.DYNAMIC_TUNNELS
                : tunnelHandler.CurrentTunnel != null
                    ? tunnelHandler.CurrentTunnel.Address + ":" + tunnelHandler.CurrentTunnel.Port
                    : "0.0.0.0:0",
            LoadedGameId: "0",
            SkillLevel: roomSettings.SkillLevel.ToString(CultureInfo.InvariantCulture),
            MapHash: GameModeMap?.Map?.SHA1 ?? string.Empty,
            GameOptionValues: GetPackedGameOptionValues());

        broadcastChannel.SendCTCPMessage(message.Encode(), QueuedMessageType.SYSTEM_MESSAGE, 20);
    }

    #endregion

    /// <summary>
    /// One CTCP command handler, with the matching rules of the XNA lobby's handler classes (StringCommandHandler,
    /// NoParamCommandHandler, NotificationHandler, IntNotificationHandler, IntCommandHandler).
    /// </summary>
    private sealed class CtcpHandler
    {
        private readonly Func<string, string, bool> handle;

        private CtcpHandler(Func<string, string, bool> handle) => this.handle = handle;

        public bool Handle(string sender, string message) => handle(sender, message);

        public static CtcpHandler String(string command, Action<string, string> handler) => new((sender, message) =>
        {
            if (message.Length < command.Length + 1 || !message.StartsWith(command))
                return false;

            handler(sender, message.Substring(command.Length + 1));
            return true;
        });

        public static CtcpHandler NoParam(string command, Action<string> handler) => new((sender, message) =>
        {
            if (message != command)
                return false;

            handler(sender);
            return true;
        });

        public static CtcpHandler Notification(string command, Action<string> handler) => NoParam(command, handler);

        public static CtcpHandler IntNotification(string command, Action<string, int> handler) => new((sender, message) =>
        {
            if (!message.StartsWith(command))
                return false;

            // As the XNA handler: a value that doesn't parse is passed as 0
            if (!int.TryParse(message.Substring(command.Length + 1), out int value))
                value = 0;

            handler(sender, value);
            return true;
        });

        public static CtcpHandler Int(string command, Action<string, int> handler) => new((sender, message) =>
        {
            if (message.Length < command.Length + 1 || !message.StartsWith(command))
                return false;

            if (!int.TryParse(message.Substring(command.Length + 1), out int value))
                return false;

            handler(sender, value);
            return true;
        });
    }
}
