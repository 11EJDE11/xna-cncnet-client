using ClientCore;
using ClientLogic.Lobby;
using ClientLogic.MapSharing;
using ClientLogic.Protocol;
using ClientLogic.Tunnels;
using ClientLogic.UI;
using ClientGUI;
using DTAClient.Domain.Multiplayer;
using DTAClient.Domain;
using DTAClient.DXGUI.Generic;
using DTAClient.DXGUI.Multiplayer.CnCNet;
using DTAClient.DXGUI.Multiplayer.GameLobby.CommandHandlers;
using DTAClient.Online;
using DTAClient.Online.EventArguments;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Rampastring.Tools;
using Rampastring.XNAUI;
using Rampastring.XNAUI.XNAControls;
using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using DTAClient.Domain.Multiplayer.CnCNet;
using ClientCore.Extensions;
using System.Net;

namespace DTAClient.DXGUI.Multiplayer.GameLobby
{
    public class CnCNetGameLobby : MultiplayerGameLobby, IV3NegotiationHost, ITunnelSessionLobby, ILobbyTransport, IMapSharingTransport
    {

        private const double GAME_BROADCAST_INTERVAL = 30.0;
        private const double GAME_BROADCAST_ACCELERATION = 10.0;
        private const double INITIAL_GAME_BROADCAST_DELAY = 10.0;

        private static readonly Color ERROR_MESSAGE_COLOR = Color.Yellow;

        private const string CHEAT_DETECTED_MESSAGE = "CD";
        private const string DICE_ROLL_MESSAGE = CnCNetLobbySession.DICE_ROLL_MESSAGE;

        public CnCNetGameLobby(
            WindowManager windowManager,
            TopBar topBar,
            CnCNetManager connectionManager,
            TunnelHandler tunnelHandler,
            IUiDispatcher uiDispatcher,
            GameCollection gameCollection,
            CnCNetUserData cncnetUserData,
            MapLoader mapLoader,
            DiscordHandler discordHandler,
            PrivateMessagingWindow pmWindow,
            Random random
        ) : base(windowManager, "MultiplayerGameLobby", topBar, mapLoader, discordHandler, pmWindow, random)
        {
            this.connectionManager = connectionManager;
            localGame = ClientConfiguration.Instance.LocalGame;
            this.tunnelHandler = tunnelHandler;
            this.gameCollection = gameCollection;
            this.cncnetUserData = cncnetUserData;
            this.pmWindow = pmWindow;
            this.random = random;
            this.uiDispatcher = uiDispatcher;
            Session = new CnCNetLobbySession(() => channel, () => chatColor);
            _negotiator = new V3TunnelNegotiationManager(this, tunnelHandler, uiDispatcher);
            tunnelSession = new TunnelSession(tunnelHandler, _negotiator, this, this, this);
            tunnelSession.Start((TunnelMode)UserINISettings.Instance.TunnelMode.Value);
            tunnelSession.ModeChanged += TunnelSession_ModeChanged;
            tunnelSession.PlayerPingsChanged += players => players.ToList().ForEach(UpdatePlayerPingIndicator);
            tunnelSession.PlayerDataChanged += RefreshPlayerSlots;
            tunnelSession.LaunchStatusChanged += () => UpdateLaunchGameButtonStatus();

            gameHostInactiveChecker = ClientConfiguration.Instance.InactiveHostKickEnabled? new GameHostInactiveChecker(WindowManager, uiDispatcher) : null;

            ctcpCommandHandlers = new CommandHandlerBase[]
            {
                // Int handlers, as before: a string handler for "R" would also take RETURN and RENEGALL
                new IntCommandHandler("OR", (sender, options) => HandleOptionsRequest(sender, new PlayerOptionsRequestMessage(PackedPlayerOptions.Unpack(options)).Encode())),
                new IntCommandHandler("R", (sender, readyState) => HandleReadyRequest(sender, new ReadyRequestMessage(readyState).Encode())),
                new StringCommandHandler("PO", ApplyPlayerOptions),
                new StringCommandHandler(PlayerExtraOptions.CNCNET_MESSAGE_KEY, HandlePlayerExtraOptions),
                new StringCommandHandler("GO", ApplyGameOptions),
                new StringCommandHandler("STARTV2", NonHostLaunchGameV2),
                new StringCommandHandler("STARTV3", NonHostLaunchGameV3),
                new NotificationHandler("AISPECS", HandleNotification, AISpectatorsNotification),
                new NotificationHandler("GETREADY", HandleNotification, GetReadyNotification),
                new NotificationHandler("INSFSPLRS", HandleNotification, InsufficientPlayersNotification),
                new NotificationHandler("TMPLRS", HandleNotification, TooManyPlayersNotification),
                new NotificationHandler("CLRS", HandleNotification, SharedColorsNotification),
                new NotificationHandler("SLOC", HandleNotification, SharedStartingLocationNotification),
                new NotificationHandler("LCKGME", HandleNotification, LockGameNotification),
                new IntNotificationHandler("NVRFY", HandleIntNotification, NotVerifiedNotification),
                new IntNotificationHandler("INGM", HandleIntNotification, StillInGameNotification),
                new StringCommandHandler(MapSharingService.MAP_SHARING_UPLOAD_REQUEST, (sender, sha1) => MapSharing.HandleMapUploadRequest(sender, sha1)),
                new StringCommandHandler(MapSharingService.MAP_SHARING_FAIL_MESSAGE, (sender, sha1) => MapSharing.HandleMapTransferFailMessage(sender, sha1)),
                new StringCommandHandler(MapSharingService.MAP_SHARING_DOWNLOAD_REQUEST, (sender, sha1) => MapSharing.HandleMapDownloadRequest(sender, sha1)),
                new NoParamCommandHandler(MapSharingService.MAP_SHARING_DISABLED_MESSAGE, sender => MapSharing.HandleMapSharingBlockedMessage(sender)),
                new NoParamCommandHandler("STRTD", GameStartedNotification),
                new NoParamCommandHandler("RETURN", ReturnNotification),
                new IntCommandHandler("TNLPNG", tunnelSession.HandleTunnelPing),
                new StringCommandHandler("FHSH", FileHashNotification),
                new StringCommandHandler("MM", CheaterNotification),
                new StringCommandHandler(DICE_ROLL_MESSAGE, HandleDiceRollResult),
                new NoParamCommandHandler(CHEAT_DETECTED_MESSAGE, HandleCheatDetectedMessage),
                new StringCommandHandler(TunnelNegotiationCommands.ChangeTunnelServer, tunnelSession.HandleTunnelServerChangeMessage),
                new StringCommandHandler(TunnelNegotiationCommands.NegotiationReport, tunnelSession.HandleNegotiationReportMessage),
                new StringCommandHandler(TunnelNegotiationCommands.RenegotiateAll, tunnelSession.HandleRenegotiateAll),
                new StringCommandHandler("GSETTINGS", ApplyGameLobbySettings)
            };

            MapSharing = new MapSharingService(uiDispatcher, this, this, this, localGame);

            AddChatBoxCommand(new ChatBoxCommand("TUNNELINFO",
                "View tunnel server information".L10N("Client:Main:TunnelInfoCommand"), false, PrintTunnelServerInformation));
            AddChatBoxCommand(new ChatBoxCommand("CHANGETUNNEL",
                "Change the used CnCNet tunnel server (game host only)".L10N("Client:Main:ChangeTunnelCommand"),
                true, (s) => ShowTunnelSelectionWindow("Select tunnel server:".L10N("Client:Main:SelectTunnelServerCommand"))));
            AddChatBoxCommand(new ChatBoxCommand("DOWNLOADMAP",
                "Download a map from CNCNet's map server using a map ID and an optional filename.\nExample: \"/downloadmap MAPID [2] My Battle Map\"".L10N("Client:Main:DownloadMapCommandDescription"),
                false, parameters => MapSharing.DownloadMapById(parameters)));
            AddChatBoxCommand(new ChatBoxCommand("NEGSTATUS",
                "Toggle the tunnel negotiation status display".L10N("Client:Main:NegStatusCommand"),
                false, ToggleNegotiationStatus));
            AddChatBoxCommand(new ChatBoxCommand("NS",
                "Shorthand for /NEGSTATUS".L10N("Client:Main:NSCommand"),
                false, ToggleNegotiationStatus));
            AddChatBoxCommand(new ChatBoxCommand("RENEGOTIATE",
                "Force all players to renegotiate tunnel connections (V3 Dynamic, host only)".L10N("Client:Main:RenegotiateCommand"),
                true, RenegotiateAllCommand));
        }

        public event EventHandler GameLeft;

        private TunnelHandler tunnelHandler;
        private TunnelSelectionWindow tunnelSelectionWindow;
        private GameLobbySettingsWindow gameLobbySettingsWindow;
        private XNAClientButton btnChangeTunnel;
        private XNAClientButton btnGameLobbySettings;
        private XNAClientButton? btnNegotiationStatus;

        private Channel channel;
        private CnCNetManager connectionManager;
        private string localGame;

        private readonly GameHostInactiveChecker gameHostInactiveChecker;

        private GameCollection gameCollection;
        private CnCNetUserData cncnetUserData;
        private readonly PrivateMessagingWindow pmWindow;
        private GlobalContextMenu globalContextMenu;

        private string hostName;

        private CommandHandlerBase[] ctcpCommandHandlers;

        private IRCColor chatColor;

        private XNATimerControl gameBroadcastTimer;

        private readonly GameRoomSettings roomSettings = new();

        /// <summary>Bindings between the lobby session's state and the controls; created in SetUp.</summary>
        private BindingScope sessionBindings;

        private readonly IUiDispatcher uiDispatcher;

        protected override int MaxPlayerCount => roomSettings.PlayerLimit;

        private bool closed = false;




        private string gameFilesHash;

        private Random random;

        private readonly V3TunnelNegotiationManager _negotiator;
        private readonly TunnelSession tunnelSession;
        private TunnelNegotiationStatusPanel _negotiationStatusPanel;

        public override void Initialize()
        {
            IniNameOverride = nameof(CnCNetGameLobby);
            base.Initialize();

            if (gameHostInactiveChecker != null)
            {
                MouseMove += (sender, args) => gameHostInactiveChecker.Reset();
                gameHostInactiveChecker.CloseEvent += GameHostInactiveChecker_CloseEvent;
            }

            btnChangeTunnel = FindChild<XNAClientButton>(nameof(btnChangeTunnel));
            btnChangeTunnel.LeftClick += BtnChangeTunnel_LeftClick;

            btnGameLobbySettings = FindChild<XNAClientButton>(nameof(btnGameLobbySettings), optional: true);
            btnGameLobbySettings?.LeftClick += BtnGameLobbySettings_LeftClick;

            gameBroadcastTimer = new XNATimerControl(WindowManager);
            gameBroadcastTimer.AutoReset = true;
            gameBroadcastTimer.Interval = TimeSpan.FromSeconds(GAME_BROADCAST_INTERVAL);
            gameBroadcastTimer.Enabled = false;
            gameBroadcastTimer.TimeElapsed += GameBroadcastTimer_TimeElapsed;

            tunnelSelectionWindow = new TunnelSelectionWindow(WindowManager, tunnelHandler);
            tunnelSelectionWindow.Initialize();
            tunnelSelectionWindow.DrawOrder = 1;
            tunnelSelectionWindow.UpdateOrder = 1;
            DarkeningPanel.AddAndInitializeWithControl(WindowManager, tunnelSelectionWindow);
            tunnelSelectionWindow.CenterOnParent();
            tunnelSelectionWindow.Disable();
            tunnelSelectionWindow.TunnelSelected += TunnelSelectionWindow_TunnelSelected;

            gameLobbySettingsWindow = new GameLobbySettingsWindow(WindowManager);
            gameLobbySettingsWindow.Initialize();
            gameLobbySettingsWindow.DrawOrder = 1;
            gameLobbySettingsWindow.UpdateOrder = 1;
            DarkeningPanel.AddAndInitializeWithControl(WindowManager, gameLobbySettingsWindow);
            gameLobbySettingsWindow.CenterOnParent();
            gameLobbySettingsWindow.Disable();


            WindowManager.AddAndInitializeControl(gameBroadcastTimer);

            globalContextMenu = new GlobalContextMenu(WindowManager, connectionManager, cncnetUserData, pmWindow);
            AddChild(globalContextMenu);

            MultiplayerNameRightClicked += MultiplayerName_RightClick;

            _negotiationStatusPanel = new TunnelNegotiationStatusPanel(WindowManager);
            _negotiationStatusPanel.Name = nameof(_negotiationStatusPanel);
            _negotiationStatusPanel.X = Width - _negotiationStatusPanel.Width - 10;
            _negotiationStatusPanel.Y = MapPreviewBox.Y;
            _negotiationStatusPanel.RenegotiateAllRequested += (s, e) => tunnelSession.TriggerRenegotiateAll();
            AddChild(_negotiationStatusPanel);

            btnNegotiationStatus = FindChild<XNAClientButton>(nameof(btnNegotiationStatus), optional: true);
            if (btnNegotiationStatus != null)
                btnNegotiationStatus?.LeftClick += (s, e) => ToggleNegotiationStatus(string.Empty);

            PostInitialize();
        }

        private void MultiplayerName_RightClick(object sender, MultiplayerNameRightClickedEventArgs args)
        {
            globalContextMenu.Show(new GlobalContextMenuData()
            {
                PlayerName = args.PlayerName,
                PreventJoinGame = true
            }, GetCursorPoint());
        }

        private void BtnChangeTunnel_LeftClick(object sender, EventArgs e) => ShowTunnelSelectionWindow("Select tunnel server:".L10N("Client:Main:SelectTunnelServer"));

        private void GameBroadcastTimer_TimeElapsed(object sender, EventArgs e) => BroadcastGame();

        public void SetUp(Channel channel, bool isHost, int playerLimit,
            CnCNetTunnel tunnel, string hostName, bool isCustomPassword,
            int skillLevel)
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

            sessionBindings?.Dispose();
            sessionBindings = new BindingScope(uiDispatcher);
            sessionBindings.Bind(roomSettings, nameof(GameRoomSettings.RoomName), () => channel.UIName = roomSettings.RoomName);
            sessionBindings.Bind(roomSettings, nameof(GameRoomSettings.PlayerLimit), RefreshPlayerSlots);
            sessionBindings.OnUserInput<GameLobbySettingsEventArgs>(
                h => gameLobbySettingsWindow.SettingsChanged += h,
                h => gameLobbySettingsWindow.SettingsChanged -= h,
                GameLobbySettingsWindow_SettingsChanged);
            MapSharing.Start();

            _negotiator.RegenerateV3PlayerInfos();

            tunnelSession.Start(TunnelModeExtensions.FromTunnel(tunnel));

            if (isHost)
            {
                RandomSeed = random.Next();
                RefreshMapSelectionUI();
                btnGameLobbySettings?.Enable();
                StartInactiveCheck();
            }
            else
            {

                channel.ChannelModesChanged += Channel_ChannelModesChanged;
                AIPlayers.Clear();
                btnGameLobbySettings?.Disable();
            }

            if (tunnelSession.Mode != TunnelMode.V3Dynamic)
                tunnelHandler.CurrentTunnel = tunnel;

            tunnelHandler.CurrentTunnelPinged += TunnelHandler_CurrentTunnelPinged;
            connectionManager.ConnectionLost += ConnectionManager_ConnectionLost;
            connectionManager.Disconnected += ConnectionManager_Disconnected;

            Refresh(isHost);

            if (IsHost)
                btnChangeTunnel.Enable();
            else
                btnChangeTunnel.Disable();

            _negotiationStatusPanel.SetIsHost(IsHost);
            if (tunnelSession.Mode == TunnelMode.V3Dynamic)
                btnNegotiationStatus?.Enable();
            else
                btnNegotiationStatus?.Disable();
        }

        private void TunnelHandler_CurrentTunnelPinged(object sender, EventArgs e) => tunnelSession.ReportCurrentTunnelPing();

        private void UpdateNegotiationUI()
        {
            if (tunnelSession.Mode != TunnelMode.V3Dynamic || !_negotiationStatusPanel.Enabled)
            {
                _negotiationStatusPanel.Disable();
                return;
            }

            var playerNames = Players.Select(p => p.Name).ToList();
            _negotiationStatusPanel.UpdateNegotiationStatus(playerNames, _negotiator.NegotiationData, inferInProgress: true);

            if (IsHost)
            {
                var summary = _negotiator.NegotiationData.GetStatusSummary(playerNames);
                Logger.Log($"Negotiation Status: {summary}");
            }
        }

        private void ToggleNegotiationStatus(string args)
        {
            if (tunnelSession.Mode != TunnelMode.V3Dynamic)
            {
                AddNotice("Negotiation status is only available when using dynamic tunnels.".L10N("Client:Main:NegStatusOnlyDynamic"));
                return;
            }

            if (_negotiationStatusPanel.Enabled)
            {
                _negotiationStatusPanel.Disable();
            }
            else
            {
                _negotiationStatusPanel.Enable();
                UpdateNegotiationUI();
            }
        }

        private void GameHostInactiveChecker_CloseEvent(object sender, EventArgs e) => LeaveGameLobby();

        public void StartInactiveCheck()
        {
            if (roomSettings.IsCustomPassword)
                return;

            gameHostInactiveChecker?.Start();
        }

        public void StopInactiveCheck() => gameHostInactiveChecker?.Stop();

        public void OnJoined()
        {
            FileHashCalculator fhc = new FileHashCalculator();
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

                gameBroadcastTimer.Enabled = true;
                gameBroadcastTimer.Start();
                gameBroadcastTimer.SetTime(TimeSpan.FromSeconds(INITIAL_GAME_BROADCAST_DELAY));
            }
            else
            {
                channel.SendCTCPMessage("FHSH " + gameFilesHash, QueuedMessageType.SYSTEM_MESSAGE, 10);
            }

            TopBar.AddPrimarySwitchable(this);
            TopBar.SwitchToPrimary();
            WindowManager.SelectedControl = tbChatInput;
            ResetAutoReadyCheckbox();
            tunnelSession.ReportCurrentTunnelPing();
            UpdateDiscordPresence(true);
        }

        protected override void RenderPlayerSlots()
        {
            base.RenderPlayerSlots();

            for (int i = AIPlayers.Count + Players.Count; i < MAX_PLAYER_COUNT; i++)
            {
                StatusIndicators[i].SwitchTexture(
                    i < roomSettings.PlayerLimit ? PlayerSlotState.Empty : PlayerSlotState.Unavailable);
            }
        }

        /// <summary>
        /// Updates player ping indicator with V3 tunnel information if available.
        /// </summary>
        protected override void UpdatePlayerPingIndicator(PlayerInfo pInfo,
            NegotiationStatus? negotiationStatus = null,
            string? tooltipText = null)
        {
            // In dynamic mode there is no connection to yourself, so the local player
            // gets a blank (but equally sized) indicator instead of a ping icon.
            if (tunnelSession.Mode == TunnelMode.V3Dynamic && pInfo.Name == ProgramConstants.PLAYERNAME)
            {
                HidePlayerPingIndicator(pInfo);
                return;
            }

            if (tunnelSession.Mode == TunnelMode.V3Dynamic)
            {
                // Derive the icon from the pair's merged status instead of the event that
                // triggered this update. Events can arrive out of order (e.g. the peer's stale
                // Succeeded report landing mid-renegotiation) and full UI refreshes like
                // RefreshPlayerSlots pass no status at all — both would flicker the icon
                // between the negotiating icon and a ping icon while a negotiation runs.
                negotiationStatus = _negotiator.NegotiationData.GetNegotiationStatus(ProgramConstants.PLAYERNAME, pInfo.Name);
            }

            if (tooltipText != null)
            {
                base.UpdatePlayerPingIndicator(pInfo, negotiationStatus, tooltipText);
                return;
            }

            if (tunnelSession.Mode == TunnelMode.V3Dynamic)
            {
                var v3Info = _negotiator.FindPlayer(pInfo.Name);
                tooltipText = BuildV3Tooltip(pInfo, v3Info, negotiationStatus);
            }
            else
            {
                tooltipText = "Ping:".L10N("Client:Main:PlayerInfoPing") + " " + pInfo.Ping.ToString();
            }

            base.UpdatePlayerPingIndicator(pInfo, negotiationStatus, tooltipText);
        }

        protected override Texture2D GetTextureForPing(PingValue ping)
            => tunnelSession.Mode == TunnelMode.V3Dynamic
                ? PingTextures[PingQualityVisuals.GetTextureIndex(PingQualityRules.GetV3Tier(ping))]
                : base.GetTextureForPing(ping);

        /// <summary>
        /// Builds a tooltip for V3 dynamic tunnel ping indicator
        /// </summary>
        private string BuildV3Tooltip(PlayerInfo pInfo, V3PlayerInfo v3Info, NegotiationStatus? status)
        {
            if (status == NegotiationStatus.InProgress)
                return "Negotiating tunnel...".L10N("Client:Main:NegotiatingTunnel");

            if (status == NegotiationStatus.Failed)
                return "Tunnel negotiation failed".L10N("Client:Main:TunnelNegotiationFailed");

            if (v3Info?.Tunnel != null && status is null or NegotiationStatus.Succeeded)
            {
                // NegotiatedPacketLoss is set on both peers (decider measures it, non-decider
                // receives it in the TunnelChoice packet), so both sides display correct stats.
                string tooltip = "Ping:".L10N("Client:Main:PlayerInfoPing") + " " + pInfo.Ping.ToString() + "\n" +
                                 "Tunnel:".L10N("Client:Main:Tunnel") + " " + v3Info.Tunnel.Name;

                if (v3Info.NegotiatedPacketLoss.HasValue)
                    tooltip += "\n" + "Packet Loss:".L10N("Client:Main:PacketLoss") + " " + $"{v3Info.NegotiatedPacketLoss.Value:F1}%";

                return tooltip;
            }

            // NotStarted or no tunnel assigned
            return "Ping:".L10N("Client:Main:PlayerInfoPing") + " " + pInfo.Ping.ToString();
        }

        private void PrintTunnelServerInformation(string s)
        {
            // V3 dynamic (per-player)
            if (tunnelSession.Mode == TunnelMode.V3Dynamic)
            {
                AddNotice("V3 Tunnel Mode - Per-player tunnel information:".L10N("Client:Main:V3TunnelHeader"));

                foreach (var v3Player in _negotiator.PlayerInfos.Where(p => p.Name != ProgramConstants.PLAYERNAME))
                {
                    var t = v3Player.Tunnel;

                    if (t != null)
                    {
                        var negotiatedPing = _negotiator.NegotiationData.GetPing(ProgramConstants.PLAYERNAME, v3Player.Name);
                        string pingDisplay = negotiatedPing.HasValue ? negotiatedPing.Value.ToString() : "Unknown".L10N("Client:Main:UnknownPing");

                        AddNotice(string.Format(
                            "{0}: {1} {2} (Ping: {3}) (Players: {4}/{5}) (Official: {6}) Version: {7}"
                                .L10N("Client:Main:V3TunnelInfo"),

                            v3Player.Name,
                            t.Name,
                            t.Country,
                            pingDisplay,
                            t.Clients,
                            t.MaxClients,
                            t.Official,
                            t.Version.ToString()
                        ));
                    }
                    else
                    {
                        AddNotice(string.Format(
                           "{0}: Not negotiated yet".L10N("Client:Main:V3TunnelNotNegotiated"),
                            v3Player.Name
                        ));
                    }
                }
            }

            // V2 legacy tunnels or V3 static with a single tunnel
            else if (tunnelHandler.CurrentTunnel == null)
            {
                AddNotice("Tunnel server unavailable!".L10N("Client:Main:TunnelUnavailable"));
            }
            else
            {
                var t = tunnelHandler.CurrentTunnel;

                AddNotice(string.Format(
                    "Current tunnel server: {0} {1} (Players: {2}/{3}) (Official: {4}) Version: {5}"
                        .L10N("Client:Main:TunnelInfo"),
                    t.Name,
                    t.Country,
                    t.Clients,
                    t.MaxClients,
                    t.Official,
                    t.Version.ToString()
                ));
            }
        }

        private void ShowTunnelSelectionWindow(string description)
        {
            tunnelSelectionWindow.Open(description,
                tunnelHandler.CurrentTunnel,
                tunnelSession.Mode);
        }

        private void TunnelSelectionWindow_TunnelSelected(object sender, TunnelSelectedEventArgs e)
        {
            tunnelSession.ChangeMode(e.Mode, true, autoSelectTunnel: e.Mode == TunnelMode.V3Dynamic);

            if (e.Mode != TunnelMode.V3Dynamic && e.Tunnel != null)
                tunnelSession.SelectTunnelServer(e.Tunnel);

            OnGameOptionChanged();
            ClearReadyStatuses();
        }

        private void BtnGameLobbySettings_LeftClick(object sender, EventArgs e)
        {
            if (!IsHost)
                return;

            string displayPassword = roomSettings.IsCustomPassword ? channel.Password : string.Empty;
            gameLobbySettingsWindow.Open(roomSettings.RoomName, roomSettings.PlayerLimit, roomSettings.SkillLevel, displayPassword);
        }

        private void GameLobbySettingsWindow_SettingsChanged(object sender, GameLobbySettingsEventArgs e)
        {
            if (!IsHost)
                return;

            UpdateGameLobbySettings(e.GameRoomName, e.MaxPlayers, e.SkillLevel, e.Password);
        }

        private void UpdateGameLobbySettings(string newGameRoomName, int newMaxPlayers, int newSkillLevel, string newPassword)
        {
            if (!IsHost)
                return;

            bool gameNameChanged = roomSettings.RoomName != newGameRoomName;
            bool maxPlayersChanged = roomSettings.PlayerLimit != newMaxPlayers;
            int normalizedSkillLevel = ClientConfiguration.Instance.NormalizeSkillLevel(newSkillLevel);
            bool skillLevelChanged = roomSettings.SkillLevel != normalizedSkillLevel;

            string currentUserPassword = roomSettings.IsCustomPassword ? channel.Password : string.Empty;
            bool passwordChanged = currentUserPassword != newPassword;

            // ensure max players isn't less than current player count
            if (newMaxPlayers < Players.Count + AIPlayers.Count)
            {
                AddNotice(string.Format("Cannot reduce maximum players to {0} with {1} players currently in game."
                    .L10N("Client:Main:CannotReduceMaxPlayers"), newMaxPlayers, Players.Count + AIPlayers.Count));
                return;
            }

            string oldGameRoomName = roomSettings.RoomName;
            bool oldIsCustomPassword = roomSettings.IsCustomPassword;
            // The bindings set the channel name and refresh the player slots
            roomSettings.RoomName = newGameRoomName;
            roomSettings.PlayerLimit = newMaxPlayers;
            roomSettings.SkillLevel = normalizedSkillLevel;

            if (passwordChanged)
            {
                // if new password is empty, generate password from channel name
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

        private void BroadcastGameLobbySettings()
        {
            if (!IsHost)
                return;

            var message = roomSettings.ToMessage();

            channel.SendCTCPMessage("GSETTINGS " + message.Encode(), QueuedMessageType.GAME_SETTINGS_MESSAGE, 11);
        }

        private void ApplyGameLobbySettings(string sender, string message)
        {
            if (IsHost)
                return;

            if (sender != hostName)
                return;

            if (!GameRoomSettingsMessage.TryDecode(message, out GameRoomSettingsMessage settings))
                return;

            // The bindings set the channel name and refresh the player slots
            foreach (string notice in roomSettings.ApplyFromHost(settings, sender))
                AddNotice(notice);
        }

        public void ChangeChatColor(IRCColor chatColor)
        {
            this.chatColor = chatColor;
            tbChatInput.TextColor = chatColor.XnaColor;
        }

        public override void Clear()
        {
            base.Clear();

            _negotiator.ClearAll();
            tunnelSession.Clear();
            MapSharing.Stop();

            sessionBindings?.Dispose();
            sessionBindings = null;

            _negotiationStatusPanel?.Disable();

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

                if (!IsHost)
                {
                    channel.ChannelModesChanged -= Channel_ChannelModesChanged;
                }

                connectionManager.RemoveChannel(channel);
            }

            Disable();
            PlayerExtraOptionsPanel?.Disable();

            connectionManager.ConnectionLost -= ConnectionManager_ConnectionLost;
            connectionManager.Disconnected -= ConnectionManager_Disconnected;

            gameBroadcastTimer.Enabled = false;
            closed = false;

            tbChatInput.Text = string.Empty;

            tunnelHandler.CurrentTunnel = null;
            tunnelHandler.CurrentTunnelPinged -= TunnelHandler_CurrentTunnelPinged;

            GameLeft?.Invoke(this, EventArgs.Empty);

            TopBar.RemovePrimarySwitchable(this);
            ResetDiscordPresence();
        }

        public void LeaveGameLobby()
        {
            if (IsHost)
            {
                StopInactiveCheck();
                closed = true;
                BroadcastGame();
            }

            Clear();
            channel?.Leave();
        }

        private void ConnectionManager_Disconnected(object sender, EventArgs e) => HandleConnectionLoss();

        private void ConnectionManager_ConnectionLost(object sender, ConnectionLostEventArgs e) => HandleConnectionLoss();

        private void HandleConnectionLoss()
        {
            Clear();
            Disable();
        }

        private void Channel_UserNameChanged(object sender, UserNameChangedEventArgs e)
        {
            Logger.Log("CnCNetGameLobby: Nickname change: " + e.OldUserName + " to " + e.User.Name);
            int index = Players.FindIndex(p => p.Name == e.OldUserName);
            if (index > -1)
            {
                PlayerInfo player = Players[index];
                player.Name = e.User.Name;
                ddPlayerNames[index].Items[0].Text = player.Name;
                AddNotice(string.Format("Player {0} changed their name to {1}".L10N("Client:Main:PlayerRename"), e.OldUserName, e.User.Name));
            }
        }

        protected override void BtnLeaveGame_LeftClick(object sender, EventArgs e) => LeaveGameLobby();

        protected override void UpdateDiscordPresence(bool resetTimer = false)
        {
            if (discordHandler == null)
                return;

            PlayerInfo player = FindLocalPlayer();
            if (player == null || Map == null || GameMode == null)
                return;
            string side = "";
            int playerIndex = Players.IndexOf(player);
            if (playerIndex > -1 && playerIndex < ddPlayerSides.Length &&
                ddPlayerSides[playerIndex].SelectedItem != null)
            {
                side = (string)ddPlayerSides[playerIndex].SelectedItem.Tag;
            }
            string currentState = ProgramConstants.IsInGame ? "In Game" : "In Lobby"; // not UI strings

            discordHandler.UpdatePresence(
                Map.UntranslatedName, GameMode.UntranslatedUIName, "Multiplayer",
                currentState, Players.Count, roomSettings.PlayerLimit, side,
                channel.UIName, IsHost, roomSettings.IsCustomPassword, Locked, resetTimer);
        }

        private void Channel_UserQuitIRC(object sender, UserNameEventArgs e)
        {
            RemovePlayer(e.UserName);

            if (e.UserName == hostName)
            {
                connectionManager.MainChannel.AddMessage(new ChatMessage(
                    ERROR_MESSAGE_COLOR, "The game host abandoned the game.".L10N("Client:Main:HostAbandoned")));
                BtnLeaveGame_LeftClick(this, EventArgs.Empty);
            }
            else
                UpdateDiscordPresence();
        }

        private void Channel_UserLeft(object sender, UserNameEventArgs e)
        {
            RemovePlayer(e.UserName);

            if (e.UserName == hostName)
            {
                connectionManager.MainChannel.AddMessage(new ChatMessage(
                    ERROR_MESSAGE_COLOR, "The game host abandoned the game.".L10N("Client:Main:HostAbandoned")));
                BtnLeaveGame_LeftClick(this, EventArgs.Empty);
            }
            else
                UpdateDiscordPresence();
        }

        private void Channel_UserKicked(object sender, UserNameEventArgs e)
        {
            if (e.UserName == ProgramConstants.PLAYERNAME)
            {
                connectionManager.MainChannel.AddMessage(new ChatMessage(
                    ERROR_MESSAGE_COLOR, "You were kicked from the game!".L10N("Client:Main:YouWereKicked")));
                Clear();
                this.Visible = false;
                this.Enabled = false;
                return;
            }

            int index = Players.FindIndex(p => p.Name == e.UserName);

            if (index > -1)
            {
                _negotiator.RemovePlayer(e.UserName);
                Players.RemoveAt(index);
                RefreshPlayerSlots();
                UpdateDiscordPresence();
                ClearReadyStatuses();
            }
        }

        private void Channel_UserListReceived(object sender, EventArgs e)
        {
            if (!IsHost)
            {
                if (channel.Users.Find(hostName) == null)
                {
                    connectionManager.MainChannel.AddMessage(new ChatMessage(
                        ERROR_MESSAGE_COLOR, "The game host has abandoned the game.".L10N("Client:Main:HostHasAbandoned")));
                    BtnLeaveGame_LeftClick(this, EventArgs.Empty);
                }
            }

            _negotiator.RegenerateV3PlayerInfos();

            UpdateDiscordPresence();
        }

        private void Channel_UserAdded(object sender, ChannelUserEventArgs e)
        {
            PlayerInfo pInfo = new PlayerInfo(e.User.IRCUser.Name);
            Players.Add(pInfo);

            while (Players.Count + AIPlayers.Count > MAX_PLAYER_COUNT && AIPlayers.Count > 0)
                AIPlayers.RemoveAt(AIPlayers.Count - 1);

            sndJoinSound.Play();
#if WINFORMS
            WindowManager.FlashWindow();
#endif

            _negotiator.RegenerateV3PlayerInfos();
            RefreshPlayerSlots();

            if (IsHost)
            {
                if (e.User.IRCUser.Name != ProgramConstants.PLAYERNAME)
                {
                    // Changing the map applies forced settings (co-op sides etc.) to the
                    // new player, and it also sends an options broadcast message
                    ChangeMap(GameModeMap);
                    BroadcastPlayerOptions();
                    BroadcastPlayerExtraOptions();
                    UpdateDiscordPresence();
                }
                else
                {
                    Players[0].Ready = true;
                    RefreshPlayerSlots();
                }

                if (RoomLock.IsFull(Players.Count, roomSettings.PlayerLimit))
                {
                    AddNotice("Player limit reached. The game room has been locked.".L10N("Client:Main:GameRoomNumberLimitReached"));
                    LockGame();
                }
            }

            _negotiator.StartNegotiationForPlayerName(pInfo.Name);
        }

        private void RemovePlayer(string playerName)
        {
            PlayerInfo pInfo = Players.Find(p => p.Name == playerName);

            if (pInfo != null)
            {
                _negotiator.RemovePlayer(playerName);

                Players.Remove(pInfo);
                RefreshPlayerSlots();

                if (IsHost)
                    BroadcastPlayerOptions();
            }

            sndLeaveSound.Play();

            if (IsHost && Locked && !ProgramConstants.IsInGame)
            {
                UnlockGame(true);
            }

            tunnelSession.ResetNegotiationsCompleteNotice();

            UpdateNegotiationUI();

            if (Players.Count > 1 && tunnelSession.Mode == TunnelMode.V3Dynamic)
                tunnelSession.CheckAllNegotiationsComplete();
        }

        private void Channel_ChannelModesChanged(object sender, ChannelModeEventArgs e)
        {
            if (e.ModeString == "+i")
            {
                if (RoomLock.IsFull(Players.Count, roomSettings.PlayerLimit))
                    AddNotice("Player limit reached. The game room has been locked.".L10N("Client:Main:GameRoomNumberLimitReached"));
                else
                    AddNotice("The game host has locked the game room.".L10N("Client:Main:RoomLockedByHost"));
                Locked = true;
            }
            else if (e.ModeString == "-i")
            {
                AddNotice("The game room has been unlocked.".L10N("Client:Main:GameRoomUnlocked"));
                Locked = false;
            }
        }

        private void Channel_CTCPReceived(object sender, ChannelCTCPEventArgs e)
        {
            Logger.Log("CnCNetGameLobby_CTCPReceived");

            foreach (CommandHandlerBase cmdHandler in ctcpCommandHandlers)
            {
                if (cmdHandler.Handle(e.UserName, e.Message))
                {
                    UpdateDiscordPresence();
                    return;
                }
            }

            Logger.Log("Unhandled CTCP command: " + e.Message + " from " + e.UserName);
        }

        private void Channel_MessageAdded(object sender, IRCMessageEventArgs e)
        {
            if (cncnetUserData.IsIgnored(e.Message.SenderIdent))
            {
                lbChatMessages.AddMessage(new ChatMessage(Color.Silver,
                    string.Format("Message blocked from {0}".L10N("Client:Main:MessageBlockedFromPlayer"), e.Message.SenderName)));
            }
            else
            {
                lbChatMessages.AddMessage(e.Message);

                if (e.Message.SenderName != null)
                    sndMessageSound.Play();
            }
        }

        /// <summary>
        /// Starts the game for the game host.
        /// </summary>
        protected override void HostLaunchGame()
        {
            if (_negotiator.LaunchConnectivityCheckInProgress)
            {
                AddNotice("Still verifying player connections...".L10N("Client:Main:VerifyingConnectionsWait"), Color.Yellow);
                return;
            }

            if (tunnelSession.Mode == TunnelMode.V3Dynamic && !_negotiator.AreAllNegotiationsSuccessful())
            {
                var (incomplete, failed) = _negotiator.NegotiationData.GetNegotiationStatusCounts(Players.Select(p => p.Name).ToList());

                if (failed > 0)
                {
                    AddNotice("Cannot start game: Some tunnel negotiations have failed.".L10N("Client:Main:CannotStartNegotiationsFailed"), Color.Red);
                    ShowFailedNegotiations();

                    // Put the recovery tool in front of the host: the negotiation status panel
                    // lists the failed pairs and carries the Renegotiate All button.
                    if (!_negotiationStatusPanel.Enabled)
                    {
                        _negotiationStatusPanel.Enable();
                        UpdateNegotiationUI();
                    }

                    return;
                }

                if (incomplete > 0)
                {
                    var incompleteNegotiations = _negotiator.NegotiationData.GetIncompleteNegotiations(Players.Select(p => p.Name).ToList());
                    AddNotice("Waiting for negotiations between:".L10N("Client:Main:WaitingForNegotiations"), Color.Yellow);
                    foreach (var (p1, p2, status) in incompleteNegotiations)
                        AddNotice($"  {p1} <-> {p2} ({status.GetDescription()})", Color.Yellow);
                    return;
                }
            }

            if (Players.Count > 1)
            {
                // with V2 tunnels we get our ids from the tunnel server
                // V3 tunnels register on the fly
                if (tunnelHandler.CurrentTunnel?.Version == 2)
                {
                    AddNotice("Contacting V2 tunnel server...".L10N("Client:Main:ConnectingTunnelV2"));

                    List<int> playerPorts = tunnelHandler.CurrentTunnel.GetPlayerPortInfo(Players.Count);

                    if (playerPorts.Count < Players.Count)
                    {
                        ShowTunnelSelectionWindow(("An error occured while contacting " +
                            "the CnCNet tunnel server.\nTry picking a different tunnel server:").L10N("Client:Main:ConnectTunnelError1"));
                        AddNotice(("An error occured while contacting the specified CnCNet " +
                            "tunnel server. Please try using a different tunnel server").L10N("Client:Main:ConnectTunnelError2") + " ", ERROR_MESSAGE_COLOR);
                        return;
                    }

                    SendStartV2ToPlayers(playerPorts);
                }
                else if (!_negotiator.TryReserveGamePort())
                {
                    AddNotice("Could not reserve a local port for the game tunnel bridge. Try again or restart the client.".L10N("Client:Main:GamePortReserveFailed"), ERROR_MESSAGE_COLOR);
                    return;
                }
                else if (tunnelSession.Mode == TunnelMode.V3Dynamic)
                {
                    // Double-check everyone is still reachable before STARTV3 goes out over
                    // IRC — IRC can take minutes to notice a dead connection, and a start
                    // command sent to a player who never receives it strands the rest at the
                    // loading screen. Launch continues from FinishV3DynamicLaunch.
                    _negotiator.BeginLaunchConnectivityCheck(FinishV3DynamicLaunch);
                    return;
                }
                else if (tunnelHandler.CurrentTunnel?.Version == 3)
                {
                    SendStartV3ToPlayers();
                }
            }

            cncnetUserData.AddRecentPlayers(Players.Select(p => p.Name), channel.UIName);

            StartGame();
        }

        /// <summary>
        /// Launch tail for V3 dynamic mode, run once the pre-launch connectivity check verifies
        /// that every player is still reachable.
        /// </summary>
        private void FinishV3DynamicLaunch()
        {
            SendStartV3ToPlayers();
            cncnetUserData.AddRecentPlayers(Players.Select(p => p.Name), channel.UIName);
            StartGame();
        }

        private void SendStartV2ToPlayers(List<int> playerPorts)
        {
            var entries = new List<StartV2Entry>(Players.Count);
            for (int pId = 0; pId < Players.Count; pId++)
            {
                Players[pId].Port = playerPorts[pId];

                // Carry the tunnel address so clients can verify/correct their tunnel at start —
                // a client that joined around a tunnel change may still have the old one selected.
                entries.Add(new StartV2Entry(Players[pId].Name, tunnelHandler.CurrentTunnel.Address, playerPorts[pId]));
            }

            channel.SendCTCPMessage("STARTV2 " + new StartV2Message(UniqueGameID, entries).Encode(), QueuedMessageType.SYSTEM_MESSAGE, 10);
        }

        private void SendStartV3ToPlayers()
        {
            var message = new StartV3Message(UniqueGameID, _negotiator.GenerateV3StartEntries());
            channel.SendCTCPMessage("STARTV3 " + message.Encode(), QueuedMessageType.SYSTEM_MESSAGE, 10);
        }

        private void ShowFailedNegotiations()
        {
            var failedPairs = _negotiator.NegotiationData.GetFailedPairs(Players.Select(p => p.Name).ToList());

            if (failedPairs.Count > 0)
            {
                AddNotice("Failed negotiations between:".L10N("Client:Main:FailedNegotiationsBetween"), Color.Red);
                foreach (var (p1, p2) in failedPairs)
                    AddNotice($" {p1} <-> {p2}", Color.Red);
                AddNotice("Use the Renegotiate All button (or type /renegotiate) to retry. If failures persist, consider changing tunnel mode or having the affected players rejoin.".L10N("Client:Main:RenegotiateHint"), Color.Yellow);
            }
        }

        // IV3NegotiationHost implementation — the shared negotiation orchestration lives in
        // V3TunnelNegotiationManager; these members supply lobby-specific transport and UI.
        List<PlayerInfo> IV3NegotiationHost.Players => Players;

        string IV3NegotiationHost.ChannelName => channel.ChannelName;

        TunnelMode IV3NegotiationHost.TunnelMode => tunnelSession.Mode;

        bool IV3NegotiationHost.IsHost => IsHost;

        void IV3NegotiationHost.SendNegotiationReport(string message)
            => channel.SendCTCPMessage(message, QueuedMessageType.GAME_NEGOTIATION_MESSAGE, 10);


        void IV3NegotiationHost.OnNegotiationStateChanged()
        {
            UpdateNegotiationUI();
            UpdateLaunchGameButtonStatus();
        }

        void IV3NegotiationHost.OnLocalNegotiationStatus(PlayerInfo player, NegotiationStatus status, int ping)
        {
            if (status == NegotiationStatus.Succeeded)
                RefreshV3PlayerPing(player, ping);

            UpdatePlayerPingIndicator(player, status);

            if (status == NegotiationStatus.Succeeded)
                RefreshPlayerSlots();
        }

        void IV3NegotiationHost.OnRemoteNegotiationStatus(PlayerInfo player, NegotiationStatus status, int ping)
        {
            if (ping >= 0)
            {
                RefreshV3PlayerPing(player, ping);
                UpdatePlayerPingIndicator(player, status);
                RefreshPlayerSlots();
            }
            else
            {
                UpdatePlayerPingIndicator(player, status);
            }
        }

        private void TunnelSession_ModeChanged()
        {
            bool useDynamic = tunnelSession.Mode == TunnelMode.V3Dynamic;

            if (IsHost)
                btnChangeTunnel.Enable();
            else
                btnChangeTunnel.Disable();

            if (useDynamic)
                btnNegotiationStatus?.Enable();
            else
                btnNegotiationStatus?.Disable();

            if (!useDynamic)
                _negotiationStatusPanel.Disable();
        }

        List<PlayerInfo> ITunnelSessionLobby.Players => Players;

        bool ITunnelSessionLobby.IsHost => IsHost;

        string ITunnelSessionLobby.HostName => hostName;

        void ILobbyTransport.SendSystemMessage(string message) =>
            channel.SendCTCPMessage(message, QueuedMessageType.SYSTEM_MESSAGE, 10);

        void IMapSharingTransport.SendMapSharingMessage(string message) =>
            channel.SendCTCPMessage(message, QueuedMessageType.SYSTEM_MESSAGE, 9);

        protected override string MapSharingHostName => hostName;

        void IV3NegotiationHost.OnNegotiationsRestarted() => tunnelSession.ResetNegotiationsCompleteNotice();

        void IV3NegotiationHost.OnPairPingUpdated(PlayerInfo player, int ping)
        {
            RefreshV3PlayerPing(player, ping);
            UpdatePlayerPingIndicator(player);
        }

        /// <summary>
        /// Sets the roster ping from the merged pair ping — the same value the negotiation
        /// status panel displays — instead of the raw ping of whichever event fired last.
        /// The two directions of a pair can carry different measurements (local keepalive
        /// vs. the peer's report).
        /// </summary>
        private void RefreshV3PlayerPing(PlayerInfo player, int eventPing)
        {
            var pairPing = _negotiator.NegotiationData.GetPing(ProgramConstants.PLAYERNAME, player.Name);
            player.Ping = pairPing ?? (eventPing >= 0 ? PingValue.FromMs(eventPing) : PingValue.Unknown);
        }

        protected override void RequestPlayerOptions(int side, int color, int start, int team)
        {
            Session.RequestPlayerOptions(new PackedPlayerOptions(side, color, start, team));
        }

        protected override void RequestReadyStatus()
        {
            if (Map == null || GameMode == null)
            {
                AddNotice(("The game host needs to select a different map or " +
                    "you will be unable to participate in the match.").L10N("Client:Main:HostMustReplaceMap"));

                if (chkAutoReady.Checked)
                    Session.RequestReady(0);

                return;
            }

            PlayerInfo pInfo = Players.Find(p => p.Name == ProgramConstants.PLAYERNAME);
            if (pInfo == null)
                return;

            int readyState = 0;

            if (chkAutoReady.Checked)
                readyState = 2;
            else if (!pInfo.Ready)
                readyState = 1;

            Session.RequestReady(readyState);
        }

        protected override void AddNotice(string message, Color color) => channel.AddMessage(new ChatMessage(color, message));

        /// <summary>
        /// Handles player option requests received from non-host players.
        /// </summary>
        private void HandleOptionsRequest(string playerName, string message)
        {
            if (!IsHost)
                return;

            if (ProgramConstants.IsInGame)
                return;

            PlayerInfo pInfo = Players.Find(p => p.Name == playerName);

            if (pInfo == null)
                return;

            if (!PlayerOptionsRequestMessage.TryDecode(message, out PlayerOptionsRequestMessage request))
                return;

            int side = request.Options.Side;
            int color = request.Options.Color;
            int start = request.Options.Start;
            int team = request.Options.Team;

            if (!IsPlayerOptionsRequestAllowed(side, color, start, team))
                return;

            if (Slots.ApplyOptionsRequest(pInfo, side, color, start, team).ClearsReady)
                ClearReadyStatuses();

            RefreshPlayerSlots();
            BroadcastPlayerOptions();
        }

        /// <summary>
        /// Handles "I'm ready" messages received from non-host players.
        /// </summary>
        private void HandleReadyRequest(string playerName, string message)
        {
            if (!IsHost)
                return;

            PlayerInfo pInfo = Players.Find(p => p.Name == playerName);

            if (pInfo == null)
                return;

            if (!ReadyRequestMessage.TryDecode(message, out ReadyRequestMessage request))
                return;

            int readyStatus = request.ReadyState;
            pInfo.Ready = readyStatus > 0;
            pInfo.AutoReady = readyStatus > 1;

            RefreshPlayerSlots();
            BroadcastPlayerOptions();
        }

        /// <summary>
        /// Broadcasts player options to non-host players.
        /// </summary>
        protected override void BroadcastPlayerOptions()
        {
            // Broadcast player options
            var entries = Players.Concat(AIPlayers).Select(pInfo => new PlayerOptionsEntry(
                pInfo.IsAI ? string.Empty : pInfo.Name,
                pInfo.IsAI ? pInfo.AILevel : -1,
                new PackedPlayerOptions(pInfo.SideId, pInfo.ColorId, pInfo.StartingLocation, pInfo.TeamId),
                pInfo.IsAI ? 1 : pInfo.AutoReady && !pInfo.IsInGame && !LastMapChangeWasInvalid ? 2 : Convert.ToInt32(pInfo.Ready),
                string.Empty)).ToList();

            Session.SendPlayerOptions(new PlayerOptionsMessage(entries));
        }

        protected override void PlayerExtraOptions_OptionsChanged(object sender, EventArgs e)
        {
            base.PlayerExtraOptions_OptionsChanged(sender, e);
            BroadcastPlayerExtraOptions();
        }

        /// <summary>
        /// Handles player extra options received from the game host.
        /// </summary>
        private void HandlePlayerExtraOptions(string sender, string message)
        {
            if (sender != hostName)
                return;

            ApplyPlayerExtraOptions(sender, message);
        }

        protected override void BroadcastPlayerExtraOptions()
        {
            if (!IsHost)
                return;

            var playerExtraOptions = GetPlayerExtraOptions();

            Session.SendPlayerExtraOptions(playerExtraOptions);
        }

        /// <summary>
        /// Handles player option messages received from the game host.
        /// </summary>
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

                if (options.Side > SideCount + RandomSelectorCount)
                    return;

                if (options.Color > MPColors.Count)
                    return;

                if (options.Start > MAX_PLAYER_COUNT)
                    return;

                if (options.Team > 4)
                    return;
            }

            var savedPings = Players.ToDictionary(p => p.Name, p => p.Ping);

            // "PO" rebuilds the player list from scratch, so carry over locally tracked
            // state the host's message doesn't include — without this, every options
            // broadcast forgets who is still in a running game.
            var savedInGameStatuses = Players.ToDictionary(p => p.Name, p => p.IsInGame);

            Players.Clear();
            AIPlayers.Clear();

            foreach (PlayerOptionsEntry entry in playerOptions.Players)
            {
                PlayerInfo pInfo = new PlayerInfo();

                if (entry.IsAI)
                {
                    pInfo.IsAI = true;
                    pInfo.AILevel = entry.AILevel;
                    pInfo.Name = AILevelToName(entry.AILevel);
                }
                else
                {
                    pInfo.Name = entry.Name;

                    // If we can't find the player from the channel user list,
                    // ignore the player
                    // They've either left the channel or got kicked before the
                    // player options message reached us
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

                    if (pInfo.Name == ProgramConstants.PLAYERNAME)
                        btnLaunchGame.Text = pInfo.Ready ? BTN_LAUNCH_NOT_READY : BTN_LAUNCH_READY;

                    if (savedPings.TryGetValue(pInfo.Name, out PingValue savedPing))
                        pInfo.Ping = savedPing;

                    if (savedInGameStatuses.TryGetValue(pInfo.Name, out bool savedInGame))
                        pInfo.IsInGame = savedInGame;

                    Players.Add(pInfo);
                }
            }

            _negotiator.RegenerateV3PlayerInfos();

            RefreshPlayerSlots();

            // When you join a lobby, you get existing player information here.
            // Start negotiating with players that we haven't already negotiated with or in the middle of negotiating
            _negotiator.StartPendingNegotiations();
        }

        private void RenegotiateAllCommand(string parameters)
        {
            if (tunnelSession.Mode != TunnelMode.V3Dynamic)
            {
                AddNotice("Renegotiate is only available when using dynamic tunnels.".L10N("Client:Main:RenegotiateOnlyDynamic"));
                return;
            }

            // Peers only obey RENEGALL from the host, so a non-host restart would run
            // one-sided and leave its pairs stuck in progress.
            if (!IsHost)
            {
                AddNotice("Only the host can request a renegotiation.".L10N("Client:Main:RenegotiateHostOnly"));
                return;
            }

            tunnelSession.TriggerRenegotiateAll();
        }

        /// <summary>
        /// Broadcasts game options to non-host players
        /// when the host has changed an option.
        /// </summary>
        protected override void OnGameOptionChanged()
        {
            base.OnGameOptionChanged();

            if (!IsHost)
                return;

            var message = new GameOptionsMessage
            {
                CheckBoxValues = GameOptions.CheckBoxes.Select(o => o.IsChecked).ToList(),
                DropDownIndices = GameOptions.DropDowns.Select(o => o.Value).ToList(),
                IsMapOfficial = Map?.Official ?? false,
                MapSHA1 = Map?.SHA1 ?? string.Empty,
                GameModeName = GameMode?.Name ?? string.Empty,
                FrameSendRate = FrameSendRate,
                MaxAhead = MaxAhead,
                ProtocolVersion = ProtocolVersion,
                RandomSeed = RandomSeed,
                RemoveStartingLocations = RemoveStartingLocations,
                MapName = Map?.UntranslatedName ?? string.Empty,
                TunnelMode = (int)tunnelSession.Mode,
            };

            Session.SendGameOptions(message);
        }

        /// <summary>
        /// Handles game option messages received from the game host.
        /// </summary>
        private void ApplyGameOptions(string sender, string message)
        {
            if (sender != hostName)
                return;

            if (!GameOptionsMessage.TryDecode(message, CheckBoxes.Count, DropDowns.Count, out GameOptionsMessage gameOptions))
            {
                AddNotice(("The game host has sent an invalid game options message! " +
                    "The game host's game version might be different from yours.").L10N("Client:Main:HostGameOptionInvalid"), Color.Red);
                return;
            }

            GameOptionsUpdate update = GameOptionsApplier.Plan(gameOptions,
                new LobbyGameSettings(FrameSendRate, MaxAhead, ProtocolVersion, GameModeMap),
                (gameMode, mapSHA1) => GameModeMaps.FirstOrDefault(gmm => gmm.GameMode.Name == gameMode && gmm.Map.SHA1 == mapSHA1),
                UserINISettings.Instance.EnableMapSharing);

            ApplyGameOptionsUpdate(update);

            tunnelSession.ChangeMode((TunnelMode)update.TunnelMode, false);
        }

        protected override void HandleMapUpdated(Map updatedMap, string previousSHA1)
        {
            base.HandleMapUpdated(updatedMap, previousSHA1);

            // If the host's currently selected map was updated, broadcast the new map to other players
            if (IsHost && Map != null && Map.SHA1 == updatedMap.SHA1)
                OnGameOptionChanged();
        }

        /// <summary>
        /// Signals other players that the local player has returned from the game,
        /// and unlocks the game as well as generates a new random seed as the game host.
        /// </summary>
        protected override void GameProcessExited()
        {
            ResetGameState();
        }

        protected void GameStartAborted()
        {
            ResetGameState();
        }

        protected void ResetGameState()
        {
            base.GameProcessExited();

            tunnelHandler.StopGameBridge();

            channel.SendCTCPMessage("RETURN", QueuedMessageType.SYSTEM_MESSAGE, 20);
            ReturnNotification(ProgramConstants.PLAYERNAME);

            if (IsHost)
            {
                RandomSeed = random.Next();
                OnGameOptionChanged();
                ClearReadyStatuses();
                RefreshPlayerSlots();
                BroadcastPlayerOptions();
                BroadcastPlayerExtraOptions();
                StartInactiveCheck();

                if (!RoomLock.IsFull(Players.Count, roomSettings.PlayerLimit))
                    UnlockGame(true);
            }
        }

        /// <summary>
        /// Handles the "STARTV2" (game start) command sent by the game host.
        /// </summary>
        private void NonHostLaunchGameV2(string sender, string message)
        {
            if (sender != hostName)
                return;

            if (Map == null)
            {
                GameStartAborted();
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
                int port = entry.Port;

                // The host's tunnel address is authoritative: if our CurrentTunnel is out of
                // sync (e.g. we joined around a tunnel change and missed the CHTNL message),
                // playing on the wrong tunnel would silently break the match for us.
                if (pName == ProgramConstants.PLAYERNAME &&
                    !string.Equals(tunnelHandler.CurrentTunnel?.Address, tunnelAddress, StringComparison.OrdinalIgnoreCase))
                {
                    var matchedTunnel = tunnelHandler.Tunnels.FirstOrDefault(t => t.Version == 2 &&
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

                pInfo.Port = port;
                recentPlayers.Add(pName);
            }
            cncnetUserData.AddRecentPlayers(recentPlayers, channel.UIName);

            StartGame();
        }

        /// <summary>
        /// Handles the "STARTV3" (game start) command sent by the game host.
        /// </summary>
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

            if (Map == null)
            {
                GameStartAborted();
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
                if (!_negotiator.ApplyV3StartEntry(start.Players[i], i))
                {
                    Logger.Log($"NonHostLaunchGameV3: Could not apply start entry for player at position {i}.");
                    NotifyStartFailed();
                    return;
                }

                recentPlayers.Add(start.Players[i].Name);
            }

            cncnetUserData.AddRecentPlayers(recentPlayers, channel.UIName);
            StartGame();
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
                ERROR_MESSAGE_COLOR);
        }

        protected override void StartGame()
        {
            AddNotice("Starting game...".L10N("Client:Main:StartingGame"));

            FileHashCalculator fhc = new FileHashCalculator();
            fhc.CalculateHashes();

            if (gameFilesHash != fhc.GetCompleteHash())
            {
                Logger.Log("Game files modified during client session!");
                channel.SendCTCPMessage(CHEAT_DETECTED_MESSAGE, QueuedMessageType.INSTANT_MESSAGE, 0);
                HandleCheatDetectedMessage(ProgramConstants.PLAYERNAME);
            }

            StopInactiveCheck();

            if (tunnelSession.Mode == TunnelMode.V3Dynamic || tunnelHandler.CurrentTunnel?.Version == 3)
            {
                PlayerInfo localPlayer = FindLocalPlayer();
                if (localPlayer == null)
                {
                    Logger.Log("Could not find local player.");
                    return;
                }

                if (!_negotiator.StartGameBridge())
                {
                    AddNotice("Could not reserve a local port for the game tunnel bridge. Try again or restart the client.".L10N("Client:Main:GamePortReserveFailed"), ERROR_MESSAGE_COLOR);
                    return;
                }
            }

            channel.SendCTCPMessage("STRTD", QueuedMessageType.SYSTEM_MESSAGE, 20);

            base.StartGame();
        }

        protected override void WriteSpawnIniAdditions(IniFile iniFile)
        {
            base.WriteSpawnIniAdditions(iniFile);

            PlayerInfo localPlayer = FindLocalPlayer();
            if (localPlayer == null)
                return;

            if (tunnelSession.Mode != TunnelMode.V2Legacy)
            {
                // Tell the game to connect to our bridge.
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
            inputs["TunnelMode"] = tunnelSession.Mode.ToString();
            inputs["TunnelAddress"] = tunnelHandler.CurrentTunnel?.Address;
            inputs["TunnelPort"] = tunnelHandler.CurrentTunnel?.Port;
            inputs["ReservedGamePort"] = tunnelHandler.ReservedGamePort;
        }

        protected override void SendChatMessage(string message) => Session.SendChatMessage(message);

        #region Notifications

        private void HandleNotification(string sender, Action handler)
        {
            if (sender != hostName)
                return;

            handler();
        }

        private void HandleIntNotification(string sender, int parameter, Action<int> handler)
        {
            if (sender != hostName)
                return;

            handler(parameter);
        }

        protected override void GetReadyNotification()
        {
            base.GetReadyNotification();
#if WINFORMS
            WindowManager.FlashWindow();
#endif
            TopBar.SwitchToPrimary();

            if (IsHost)
                channel.SendCTCPMessage("GETREADY", QueuedMessageType.GAME_GET_READY_MESSAGE, 0);
        }

        protected override void AISpectatorsNotification()
        {
            base.AISpectatorsNotification();

            if (IsHost)
                channel.SendCTCPMessage("AISPECS", QueuedMessageType.GAME_NOTIFICATION_MESSAGE, 0);
        }

        protected override void InsufficientPlayersNotification()
        {
            base.InsufficientPlayersNotification();

            if (IsHost)
                channel.SendCTCPMessage("INSFSPLRS", QueuedMessageType.GAME_NOTIFICATION_MESSAGE, 0);
        }

        protected override void TooManyPlayersNotification()
        {
            base.TooManyPlayersNotification();

            if (IsHost)
                channel.SendCTCPMessage("TMPLRS", QueuedMessageType.GAME_NOTIFICATION_MESSAGE, 0);
        }

        protected override void SharedColorsNotification()
        {
            base.SharedColorsNotification();

            if (IsHost)
                channel.SendCTCPMessage("CLRS", QueuedMessageType.GAME_NOTIFICATION_MESSAGE, 0);
        }

        protected override void SharedStartingLocationNotification()
        {
            base.SharedStartingLocationNotification();

            if (IsHost)
                channel.SendCTCPMessage("SLOC", QueuedMessageType.GAME_NOTIFICATION_MESSAGE, 0);
        }

        protected override void LockGameNotification()
        {
            base.LockGameNotification();

            if (IsHost)
                channel.SendCTCPMessage("LCKGME", QueuedMessageType.GAME_NOTIFICATION_MESSAGE, 0);
        }

        protected override void NotVerifiedNotification(int playerIndex)
        {
            base.NotVerifiedNotification(playerIndex);

            if (IsHost)
                channel.SendCTCPMessage("NVRFY " + playerIndex, QueuedMessageType.GAME_NOTIFICATION_MESSAGE, 0);
        }

        protected override void StillInGameNotification(int playerIndex)
        {
            base.StillInGameNotification(playerIndex);

            if (IsHost)
                channel.SendCTCPMessage("INGM " + playerIndex, QueuedMessageType.GAME_NOTIFICATION_MESSAGE, 0);
        }

        private void GameStartedNotification(string sender)
        {
            PlayerInfo pInfo = Players.Find(p => p.Name == sender);

            if (pInfo != null)
                pInfo.IsInGame = true;

            RefreshPlayerSlots();
        }

        private void ReturnNotification(string sender)
        {
            AddNotice(string.Format("{0} has returned from the game.".L10N("Client:Main:PlayerReturned"), sender));

            PlayerInfo pInfo = Players.Find(p => p.Name == sender);

            if (pInfo != null)
                pInfo.IsInGame = false;

            sndReturnSound.Play();
            RefreshPlayerSlots();
        }

        private void FileHashNotification(string sender, string filesHash)
        {
            if (!IsHost)
                return;

            PlayerInfo pInfo = Players.Find(p => p.Name == sender);

            if (pInfo != null)
                pInfo.HashReceived = true;
            RefreshPlayerSlots();

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

            AddNotice(string.Format("Player {0} has different files compared to the game host. Either {0} or the game host could be cheating.".L10N("Client:Main:DifferentFileCheating"), cheaterName), Color.Red);
        }

        protected override void BroadcastDiceRoll(int dieSides, int[] results)
        {
            Session.SendDiceRoll(dieSides, results);
            PrintDiceRollResult(ProgramConstants.PLAYERNAME, dieSides, results);
        }

        #endregion

        protected override void HandleLockGameButtonClick()
        {
            switch (RoomLock.OnLockButton(Locked, Players.Count, roomSettings.PlayerLimit))
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

        protected override void LockGame()
        {
            connectionManager.SendCustomMessage(new QueuedMessage(
                string.Format("MODE {0} +i", channel.ChannelName), QueuedMessageType.INSTANT_MESSAGE, -1));

            Locked = true;
            btnLockGame.Text = "Unlock Game".L10N("Client:Main:UnlockGame");
            AccelerateGameBroadcasting();
        }

        protected override void UnlockGame(bool announce)
        {
            connectionManager.SendCustomMessage(new QueuedMessage(
                string.Format("MODE {0} -i", channel.ChannelName), QueuedMessageType.INSTANT_MESSAGE, -1));

            Locked = false;
            if (announce)
                AddNotice("The game room has been unlocked.".L10N("Client:Main:GameRoomUnlocked"));
            btnLockGame.Text = "Lock Game".L10N("Client:Main:LockGame");
            AccelerateGameBroadcasting();
        }

        protected override void KickPlayer(int playerIndex)
        {
            if (playerIndex >= Players.Count)
                return;

            var pInfo = Players[playerIndex];

            AddNotice(string.Format("Kicking {0} from the game...".L10N("Client:Main:KickPlayer"), pInfo.Name));
            channel.SendKickMessage(pInfo.Name, 8);
        }

        protected override void BanPlayer(int playerIndex)
        {
            if (playerIndex >= Players.Count)
                return;

            var pInfo = Players[playerIndex];

            var user = connectionManager.UserList.Find(u => u.Name == pInfo.Name);

            if (user != null)
            {
                AddNotice(string.Format("Banning and kicking {0} from the game...".L10N("Client:Main:BanAndKickPlayer"), pInfo.Name));
                channel.SendBanMessage(user.Hostname, 8);
                channel.SendKickMessage(user.Name, 8);
            }
        }

        private void HandleCheatDetectedMessage(string sender) =>
            AddNotice(string.Format("{0} has modified game files during the client session. They are likely attempting to cheat!".L10N("Client:Main:PlayerModifyFileCheat"), sender), Color.Red);

        protected override bool UpdateLaunchGameButtonStatus()
        {
            btnLaunchGame.Enabled = base.UpdateLaunchGameButtonStatus() && !tunnelSession.IsInTunnelError;
            return btnLaunchGame.Enabled;
        }


        #region Game broadcasting logic

        /// <summary>
        /// Lowers the time until the next game broadcasting message.
        /// </summary>
        private void AccelerateGameBroadcasting() =>
            gameBroadcastTimer.Accelerate(TimeSpan.FromSeconds(GAME_BROADCAST_ACCELERATION));

        private void BroadcastGame()
        {
            Channel broadcastChannel = connectionManager.FindChannel(gameCollection.GetGameBroadcastingChannelNameFromIdentifier(localGame));

            if (broadcastChannel == null)
                return;

            if (ProgramConstants.IsInGame && broadcastChannel.Users.Count > 500)
                return;

            StringBuilder sb = new StringBuilder("GAME ");
            sb.Append(ProgramConstants.CNCNET_PROTOCOL_REVISION);
            sb.Append(";");
            sb.Append(ProgramConstants.GAME_VERSION);
            sb.Append(";");
            sb.Append(roomSettings.PlayerLimit);
            sb.Append(";");
            sb.Append(channel.ChannelName);
            sb.Append(";");
            sb.Append(roomSettings.RoomName);
            sb.Append(";");
            if (Locked)
                sb.Append("1");
            else
                sb.Append("0");
            sb.Append(Convert.ToInt32(roomSettings.IsCustomPassword));
            sb.Append(Convert.ToInt32(closed));
            sb.Append("0"); // IsLoadedGame
            sb.Append("0"); // IsLadder
            sb.Append(";");
            foreach (PlayerInfo pInfo in Players)
            {
                sb.Append(pInfo.Name);
                sb.Append(",");
            }

            sb.Remove(sb.Length - 1, 1);
            sb.Append(";");
            sb.Append(Map?.UntranslatedName ?? string.Empty);
            sb.Append(";");
            sb.Append(GameMode?.UntranslatedUIName ?? string.Empty);
            sb.Append(";");
            if (tunnelSession.Mode == TunnelMode.V3Dynamic)
                sb.Append("[DYN]");
            else
                sb.Append(tunnelHandler.CurrentTunnel != null
                    ? tunnelHandler.CurrentTunnel.Address + ":" + tunnelHandler.CurrentTunnel.Port
                    : "0.0.0.0:0");
            sb.Append(";");
            sb.Append(0); // LoadedGameId
            sb.Append(";");
            sb.Append(roomSettings.SkillLevel); // SkillLevel
            sb.Append(";");
            sb.Append(Map?.SHA1);

            string gameOptionValues = GetPackedGameOptionValuesString();
            sb.Append(";");
            sb.Append(gameOptionValues);

            broadcastChannel.SendCTCPMessage(sb.ToString(), QueuedMessageType.SYSTEM_MESSAGE, 20);
        }

        #endregion

        public override string GetSwitchName() => "Game Lobby".L10N("Client:Main:GameLobby");
    }
}
