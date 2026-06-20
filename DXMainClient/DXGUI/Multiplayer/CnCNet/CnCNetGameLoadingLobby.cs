using ClientCore;
using ClientGUI;
using DTAClient.Domain;
using DTAClient.Domain.Multiplayer;
using DTAClient.Domain.Multiplayer.CnCNet;
using DTAClient.DXGUI.Generic;
using DTAClient.DXGUI.Multiplayer.GameLobby.CommandHandlers;
using DTAClient.Online;
using DTAClient.Online.EventArguments;
using ClientCore.Extensions;
using Microsoft.Xna.Framework;
using Rampastring.Tools;
using Rampastring.XNAUI;
using Rampastring.XNAUI.XNAControls;
using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Security.Cryptography;
using System.Text;

namespace DTAClient.DXGUI.Multiplayer.CnCNet
{
    /// <summary>
    /// A game lobby for loading saved CnCNet games.
    /// </summary>
    public class CnCNetGameLoadingLobby : GameLoadingLobbyBase
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
        private const string CHANGE_TUNNEL_SERVER_MESSAGE = "CHTNL";
        private const string NEGOTIATION_INFO_MESSAGE = "NEGINFO";
        private const string TUNNEL_RENEGOTIATE_MESSAGE = "TNLRENEG";
        private const string TUNNEL_FAILED_MESSAGE = "TNLFAIL";

        public CnCNetGameLoadingLobby(
            WindowManager windowManager,
            TopBar topBar,
            CnCNetManager connectionManager,
            TunnelHandler tunnelHandler,
            MapLoader mapLoader,
            GameCollection gameCollection,
            DiscordHandler discordHandler,
            CnCNetUserData cncnetUserData
        ) : base(windowManager, discordHandler)
        {
            this.connectionManager = connectionManager;
            this.tunnelHandler = tunnelHandler;
            this.topBar = topBar;
            this.gameCollection = gameCollection;
            this.mapLoader = mapLoader;
            this.cncnetUserData = cncnetUserData;

            ctcpCommandHandlers = new CommandHandlerBase[]
            {
                new NoParamCommandHandler(NOT_ALL_PLAYERS_PRESENT_CTCP_COMMAND, HandleNotAllPresentNotification),
                new NoParamCommandHandler(GET_READY_CTCP_COMMAND, HandleGetReadyNotification),
                new StringCommandHandler(FILE_HASH_CTCP_COMMAND, HandleFileHashCommand),
                new StringCommandHandler(INVALID_FILE_HASH_CTCP_COMMAND, HandleCheaterNotification),
                new IntCommandHandler(TUNNEL_PING_CTCP_COMMAND, HandleTunnelPing),
                new StringCommandHandler(OPTIONS_CTCP_COMMAND, HandleOptionsMessage),
                new NoParamCommandHandler(INVALID_SAVED_GAME_INDEX_CTCP_COMMAND, HandleInvalidSaveIndexCommand),
                new StringCommandHandler(START_GAME_V3_CTCP_COMMAND, HandleStartGameV3Command),
                new StringCommandHandler(START_GAME_CTCP_COMMAND, HandleStartGameCommand),
                new IntCommandHandler(PLAYER_READY_CTCP_COMMAND, HandlePlayerReadyRequest),
                new StringCommandHandler(CHANGE_TUNNEL_SERVER_MESSAGE, HandleTunnelServerChangeMessage),
                new StringCommandHandler(NEGOTIATION_INFO_MESSAGE, HandleNegotiationInfoMessage),
                new StringCommandHandler(TUNNEL_RENEGOTIATE_MESSAGE, HandleTunnelRenegotiateMessage),
                new StringCommandHandler(TUNNEL_FAILED_MESSAGE, HandleTunnelFailedMessage),
            };
        }

        private CommandHandlerBase[] ctcpCommandHandlers;

        private CnCNetManager connectionManager;

        private CnCNetUserData cncnetUserData;

        private List<GameMode> gameModes;

        private TunnelHandler tunnelHandler;
        private readonly MapLoader mapLoader;
        private TunnelSelectionWindow tunnelSelectionWindow;
        private XNAClientButton btnChangeTunnel;

        private Channel channel;

        private GameCollection gameCollection;

        private IRCColor chatColor;

        private string hostName;

        private string localGame;

        private string gameFilesHash;

        private XNATimerControl gameBroadcastTimer;

        private bool started;

        private DarkeningPanel dp;

        private TopBar topBar;

        private readonly List<V3PlayerInfo> _v3PlayerInfos = new();
        private bool _useLegacyTunnels;
        private bool _useDynamicTunnels;
        private readonly NegotiationDataManager _negotiationData = new();

        public override void Initialize()
        {
            dp = new DarkeningPanel(WindowManager);

            localGame = ClientConfiguration.Instance.LocalGame;

            base.Initialize();

            connectionManager.ConnectionLost += ConnectionManager_ConnectionLost;
            connectionManager.Disconnected += ConnectionManager_Disconnected;

            tunnelSelectionWindow = new TunnelSelectionWindow(WindowManager, tunnelHandler);
            tunnelSelectionWindow.Initialize();
            tunnelSelectionWindow.DrawOrder = 1;
            tunnelSelectionWindow.UpdateOrder = 1;
            DarkeningPanel.AddAndInitializeWithControl(WindowManager, tunnelSelectionWindow);
            tunnelSelectionWindow.CenterOnParent();
            tunnelSelectionWindow.Disable();
            tunnelSelectionWindow.TunnelSelected += TunnelSelectionWindow_TunnelSelected;

            btnChangeTunnel = new XNAClientButton(WindowManager);
            btnChangeTunnel.Name = nameof(btnChangeTunnel);
            btnChangeTunnel.ClientRectangle = new Rectangle(btnLeaveGame.Right - btnLeaveGame.Width - 145,
                btnLeaveGame.Y, UIDesignConstants.BUTTON_WIDTH_133, UIDesignConstants.BUTTON_HEIGHT);
            btnChangeTunnel.Text = "Change Tunnel".L10N("Client:Main:ChangeTunnel");
            btnChangeTunnel.LeftClick += BtnChangeTunnel_LeftClick;
            AddChild(btnChangeTunnel);

            gameBroadcastTimer = new XNATimerControl(WindowManager);
            gameBroadcastTimer.AutoReset = true;
            gameBroadcastTimer.Interval = TimeSpan.FromSeconds(GAME_BROADCAST_INTERVAL);
            gameBroadcastTimer.Enabled = false;
            gameBroadcastTimer.TimeElapsed += GameBroadcastTimer_TimeElapsed;

            WindowManager.AddAndInitializeControl(gameBroadcastTimer);
        }

        public override void Refresh(bool isHost)
        {
            base.Refresh(isHost);

            btnChangeTunnel.Visible = isHost;
            gameBroadcastTimer.Enabled = isHost;
        }

        private void BtnChangeTunnel_LeftClick(object sender, EventArgs e) => ShowTunnelSelectionWindow("Select tunnel server:".L10N("Client:Main:SelectTunnelServer"));

        private void GameBroadcastTimer_TimeElapsed(object sender, EventArgs e) => BroadcastGame();

        private void ConnectionManager_Disconnected(object sender, EventArgs e) => Clear();

        private void ConnectionManager_ConnectionLost(object sender, ConnectionLostEventArgs e) => Clear();

        /// <summary>
        /// Sets up events and information before joining the channel.
        /// </summary>
        public void SetUp(bool isHost, CnCNetTunnel tunnel, Channel channel,
            string hostName)
        {
            this.channel = channel;
            this.hostName = hostName;

            channel.MessageAdded += Channel_MessageAdded;
            channel.UserAdded += Channel_UserAdded;
            channel.UserLeft += Channel_UserLeft;
            channel.UserQuitIRC += Channel_UserQuitIRC;
            channel.CTCPReceived += Channel_CTCPReceived;

            _useDynamicTunnels = tunnel == null;
            _useLegacyTunnels = tunnel?.Version == 2;

            tunnelHandler.CurrentTunnel = _useDynamicTunnels ? null : tunnel;
            tunnelHandler.CurrentTunnelPinged += TunnelHandler_CurrentTunnelPinged;
            tunnelHandler.TunnelFailed += TunnelHandler_TunnelFailed;

            started = false;

            RegenerateV3PlayerInfos();
            Refresh(isHost);
        }

        private void TunnelHandler_CurrentTunnelPinged(object sender, EventArgs e)
        {
            // TODO Rampastring pls, review and merge that XNAIndicator PR already
        }

        /// <summary>
        /// Clears event subscriptions and leaves the channel.
        /// </summary>
        public void Clear()
        {
            gameBroadcastTimer.Enabled = false;

            foreach (var v3Player in _v3PlayerInfos)
            {
                if (v3Player.Negotiator != null)
                {
                    v3Player.Negotiator.NegotiationResult -= OnPlayerNegotiationResult;
                    v3Player.Negotiator.NegotiationComplete -= OnPlayerNegotiationComplete;
                }
                v3Player.StopNegotiation();
            }
            _negotiationData.ClearAll();
            _v3PlayerInfos.Clear();

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

            if (Enabled)
            {
                Enabled = false;
                Visible = false;

                base.LeaveGame();
            }

            tunnelHandler.CurrentTunnel = null;
            tunnelHandler.CurrentTunnelPinged -= TunnelHandler_CurrentTunnelPinged;
            tunnelHandler.TunnelFailed -= TunnelHandler_TunnelFailed;

            topBar.RemovePrimarySwitchable(this);
        }

        private void Channel_CTCPReceived(object sender, ChannelCTCPEventArgs e)
        {
            foreach (CommandHandlerBase cmdHandler in ctcpCommandHandlers)
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
            FileHashCalculator fhc = new FileHashCalculator();
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

                gameBroadcastTimer.Enabled = true;
                gameBroadcastTimer.Start();
                gameBroadcastTimer.SetTime(TimeSpan.FromSeconds(INITIAL_GAME_BROADCAST_DELAY));
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

            topBar.AddPrimarySwitchable(this);
            topBar.SwitchToPrimary();
            WindowManager.SelectedControl = tbChatInput;
            UpdateDiscordPresence(true);
        }

        private void Channel_UserAdded(object sender, ChannelUserEventArgs e)
        {
            PlayerInfo pInfo = new PlayerInfo();
            pInfo.Name = e.User.IRCUser.Name;

            Players.Add(pInfo);

            sndJoinSound.Play();

            RegenerateV3PlayerInfos();

            BroadcastOptions();
            CopyPlayerDataToUI();
            UpdateDiscordPresence();

            if (pInfo.Name != ProgramConstants.PLAYERNAME && _useDynamicTunnels)
            {
                var newV3Player = _v3PlayerInfos.FirstOrDefault(p => p.Name == pInfo.Name);
                if (newV3Player != null)
                    StartTunnelNegotiationForPlayer(newV3Player);
            }
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

            sndLeaveSound.Play();

            var v3Player = _v3PlayerInfos.FirstOrDefault(p => p.Name == playerName);
            if (v3Player != null)
            {
                _v3PlayerInfos.Remove(v3Player);

                if (_useDynamicTunnels)
                {
                    if (v3Player.Negotiator != null)
                    {
                        v3Player.Negotiator.NegotiationResult -= OnPlayerNegotiationResult;
                        v3Player.Negotiator.NegotiationComplete -= OnPlayerNegotiationComplete;
                    }
                    v3Player.StopNegotiation();
                }
            }

            Players.RemoveAt(index);
            _negotiationData.ClearPlayer(playerName);

            CopyPlayerDataToUI();
            UpdateLoadGameButtonStatus();

            if (!IsHost && playerName == hostName && !ProgramConstants.IsInGame)
            {
                connectionManager.MainChannel.AddMessage(new ChatMessage(
                    Color.Yellow, "The game host left the game!".L10N("Client:Main:HostLeft")));

                Clear();
            }
        }

        private void Channel_MessageAdded(object sender, IRCMessageEventArgs e)
        {
            if (!string.IsNullOrEmpty(e.Message.SenderIdent) &&
                cncnetUserData.IsIgnored(e.Message.SenderIdent) &&
                !e.Message.SenderIsAdmin)
            {
                lbChatMessages.AddMessage(new ChatMessage(Color.Silver, string.Format("Message blocked from - {0}".L10N("Client:Main:PMBlockedFrom"), e.Message.SenderName)));
            }
            else
            {
                lbChatMessages.AddMessage(e.Message);
                sndMessageSound.Play();
            }
        }

        protected override void AddNotice(string message, Color color) => channel.AddMessage(new ChatMessage(color, message));

        protected override void BroadcastOptions()
        {
            if (!IsHost)
                return;

            Players[0].Ready = true;

            StringBuilder message = new StringBuilder(OPTIONS_CTCP_COMMAND + " ");
            message.Append(ddSavedGame.SelectedIndex);
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

        protected override void SendChatMessage(string message)
        {
            sndMessageSound.Play();

            channel.SendChatMessage(message, chatColor);
        }

        protected override void RequestReadyStatus() =>
            channel.SendCTCPMessage(PLAYER_READY_CTCP_COMMAND + " 1", QueuedMessageType.GAME_PLAYERS_READY_STATUS_MESSAGE, 10);

        protected override void GetReadyNotification()
        {
            base.GetReadyNotification();

            topBar.SwitchToPrimary();

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

        private void ShowTunnelSelectionWindow(string description)
        {
            if (_useDynamicTunnels)
            {
                AddNotice("Cannot manually select tunnel when using dynamic tunnels.", Color.Yellow);
                return;
            }

            tunnelSelectionWindow.Open(description,
                tunnelHandler.CurrentTunnel?.Address,
                targetVersion: _useLegacyTunnels ? 2 : 3);
        }

        private void TunnelSelectionWindow_TunnelSelected(object sender, TunnelEventArgs e)
        {
            channel.SendCTCPMessage($"{CHANGE_TUNNEL_SERVER_MESSAGE} {e.Tunnel.Address}:{e.Tunnel.Port}",
                QueuedMessageType.SYSTEM_MESSAGE, 10);
            HandleTunnelServerChange(e.Tunnel);
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

            AddNotice(string.Format("{0} - modified files detected! They could be cheating!".L10N("Client:Main:PlayerCheating"), cheaterName), Color.Red);

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

            if (sgIndex >= ddSavedGame.Items.Count)
            {
                AddNotice("The game host has selected an invalid saved game index!".L10N("Client:Main:HostInvalidIndex") + " " + sgIndex);
                channel.SendCTCPMessage(INVALID_SAVED_GAME_INDEX_CTCP_COMMAND, QueuedMessageType.SYSTEM_MESSAGE, 10);
                return;
            }

            ddSavedGame.SelectedIndex = sgIndex;

            Players.Clear();

            for (int i = 1; i < parts.Length; i++)
            {
                string[] playerAndReadyStatus = parts[i].Split(':');
                if (playerAndReadyStatus.Length < 2)
                    return;

                string playerName = playerAndReadyStatus[0];
                int readyStatus = Conversions.IntFromString(playerAndReadyStatus[1], -1);

                if (string.IsNullOrEmpty(playerName) || readyStatus == -1)
                    return;

                PlayerInfo pInfo = new PlayerInfo();
                pInfo.Name = playerName;
                pInfo.Ready = Convert.ToBoolean(readyStatus);

                Players.Add(pInfo);
            }

            CopyPlayerDataToUI();

            RegenerateV3PlayerInfos();

            if (_useDynamicTunnels && Players.Count > 1)
                foreach (var v3Player in _v3PlayerInfos.Where(p => p.Name != ProgramConstants.PLAYERNAME && !p.HasNegotiated && !p.IsNegotiating))
                    StartTunnelNegotiationForPlayer(v3Player);
        }

        private void HandleInvalidSaveIndexCommand(string sender)
        {
            PlayerInfo pInfo = Players.Find(p => p.Name == sender);

            if (pInfo == null)
                return;

            pInfo.Ready = false;

            AddNotice(string.Format("{0} does not have the selected saved game on their system! Try selecting an earlier saved game.".L10N("Client:Main:PlayerDontHaveSavedGame"), pInfo.Name));

            CopyPlayerDataToUI();
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

                int port = 0;
                bool success = int.TryParse(ipAndPortSplit[1], out port);
                if (!success)
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

            string[] parts = data.Split(';');

            if (parts.Length != Players.Count * 3)
                return;

            for (int i = 0; i < parts.Length; i += 3)
            {
                if (!uint.TryParse(parts[i], out uint id))
                    return;

                string pName = parts[i + 1];
                string[] ipAndPort = parts[i + 2].Split(':');

                if (ipAndPort.Length != 2 || !int.TryParse(ipAndPort[1], out int tunnelPort))
                    return;

                PlayerInfo pInfo = Players.Find(p => p.Name == pName);
                if (pInfo == null)
                    return;

                int playerPosition = i / 3;
                int gamePort = 48000 - playerPosition;
                pInfo.Port = gamePort;

                V3PlayerInfo v3PlayerInfo = _v3PlayerInfos.Find(p => p.Name == pName);
                if (v3PlayerInfo != null)
                {
                    if (!_useDynamicTunnels)
                    {
                        CnCNetTunnel tunnel = tunnelHandler.Tunnels.Find(t => t.Address == ipAndPort[0] && t.Port == tunnelPort);
                        v3PlayerInfo.Tunnel = tunnel;
                    }
                    v3PlayerInfo.PlayerIndex = playerPosition;
                    v3PlayerInfo.PlayerGameId = (ushort)gamePort;
                    v3PlayerInfo.Id = id;
                }
            }

            StartV3Game();
        }

        private void HandlePlayerReadyRequest(string sender, int readyStatus)
        {
            PlayerInfo pInfo = Players.Find(p => p.Name == sender);

            if (pInfo == null)
                return;

            pInfo.Ready = Convert.ToBoolean(readyStatus);

            CopyPlayerDataToUI();

            if (IsHost)
                BroadcastOptions();
        }

        private void HandleTunnelServerChangeMessage(string sender, string tunnelAddressAndPort)
        {
            if (sender != hostName)
                return;

            string[] split = tunnelAddressAndPort.Split(':');
            string tunnelAddress = split[0];
            int tunnelPort = int.Parse(split[1]);

            CnCNetTunnel tunnel = tunnelHandler.Tunnels.Find(t => t.Address == tunnelAddress && t.Port == tunnelPort);
            if (tunnel == null)
            {
                AddNotice(("The game host has selected an invalid tunnel server! " +
                    "The game host needs to change the server or you will be unable " +
                    "to participate in the match.").L10N("Client:Main:HostInvalidTunnel"),
                    Color.Yellow);
                btnLoadGame.AllowClick = false;
                return;
            }

            HandleTunnelServerChange(tunnel);
            btnLoadGame.AllowClick = true;
        }

        /// <summary>
        /// Changes the tunnel server used for the game.
        /// </summary>
        private void HandleTunnelServerChange(CnCNetTunnel tunnel)
        {
            tunnelHandler.CurrentTunnel = tunnel;
            AddNotice(string.Format("The game host has changed the tunnel server to: {0}".L10N("Client:Main:HostChangeTunnel"), tunnel.Name));

            // For V3 static mode, propagate the new tunnel to all players
            if (!_useLegacyTunnels && !_useDynamicTunnels)
            {
                foreach (var v3Player in _v3PlayerInfos)
                    v3Player.Tunnel = tunnel;
            }
        }

        private void HandleNegotiationInfoMessage(string sender, string message)
        {
            string[] parts = message.Split(';');
            if (parts.Length < 2)
                return;

            string targetPlayer = parts[0];
            if (!Enum.TryParse<NegotiationStatus>(parts[1], out var status))
                return;

            _negotiationData.UpdateStatus(sender, targetPlayer, status);

            if (parts.Length >= 3 && int.TryParse(parts[2], out int ping) && ping >= 0)
                _negotiationData.UpdatePing(sender, targetPlayer, ping);

            UpdateLoadGameButtonStatus();
        }

        private void HandleTunnelRenegotiateMessage(string sender, string tunnelAddressAndPort)
        {
            if (!_useDynamicTunnels)
                return;

            string[] split = tunnelAddressAndPort.Split(':');
            if (split.Length != 2 || !int.TryParse(split[1], out int tunnelPort))
                return;

            string tunnelAddress = split[0];

            var remoteV3Player = _v3PlayerInfos.FirstOrDefault(p => p.Name == sender);
            if (remoteV3Player == null)
                return;

            if (remoteV3Player.Tunnel?.Address == tunnelAddress && remoteV3Player.Tunnel?.Port == tunnelPort)
            {
                AddNotice($"{sender} needs to renegotiate tunnel. Starting renegotiation...", Color.Orange);
                RestartNegotiations(new List<V3PlayerInfo> { remoteV3Player });
            }
        }

        private void HandleTunnelFailedMessage(string sender, string tunnelName)
        {
            AddNotice($"{sender} can no longer connect to tunnel: {tunnelName}.", Color.Orange);
        }

        #endregion

        protected override void HostStartGame()
        {
            if (_useDynamicTunnels && !AreAllNegotiationsSuccessful())
            {
                AddNotice("Cannot start game: tunnel negotiations have not completed.", Color.Yellow);
                return;
            }

            if (_useLegacyTunnels || tunnelHandler.CurrentTunnel?.Version == 2)
            {
                AddNotice("Contacting tunnel server...".L10N("Client:Main:ConnectingTunnel"));
                List<int> playerPorts = tunnelHandler.CurrentTunnel.GetPlayerPortInfo(SGPlayers.Count);

                if (playerPorts.Count < Players.Count)
                {
                    ShowTunnelSelectionWindow(("An error occured while contacting the CnCNet tunnel server.\nTry picking a different tunnel server:").L10N("Client:Main:ConnectTunnelError1"));
                    AddNotice(("An error occured while contacting the specified CnCNet " +
                        "tunnel server. Please try using a different tunnel server").L10N("Client:Main:ConnectTunnelError2") + " ", Color.Yellow);
                    return;
                }

                StringBuilder sb = new StringBuilder(START_GAME_CTCP_COMMAND + " ");
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
            else
            {
                // V3 static or dynamic
                SendStartV3ToPlayers();
                AddNotice("Starting game...".L10N("Client:Main:StartingGame"));
                started = true;
                StartV3Game();
            }
        }

        protected override void WriteSpawnIniAdditions(IniFile spawnIni)
        {
            if (_useLegacyTunnels)
            {
                spawnIni.SetStringValue("Tunnel", "Ip", tunnelHandler.CurrentTunnel.Address);
                spawnIni.SetIntValue("Tunnel", "Port", tunnelHandler.CurrentTunnel.Port);
            }
            else
            {
                PlayerInfo localPlayer = Players.Find(p => p.Name == ProgramConstants.PLAYERNAME);
                if (localPlayer != null)
                {
                    spawnIni.SetStringValue("Tunnel", "Ip", IPAddress.Loopback.ToString());
                    spawnIni.SetIntValue("Tunnel", "Port", localPlayer.Port);
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

        protected override void LeaveGame() => Clear();

        public void ChangeChatColor(IRCColor chatColor)
        {
            this.chatColor = chatColor;
            tbChatInput.TextColor = chatColor.XnaColor;
        }

        private void BroadcastGame()
        {
            Channel broadcastChannel = connectionManager.FindChannel(gameCollection.GetGameBroadcastingChannelNameFromIdentifier(localGame));

            if (broadcastChannel == null)
                return;

            StringBuilder sb = new StringBuilder("GAME ");
            sb.Append(ProgramConstants.CNCNET_PROTOCOL_REVISION);
            sb.Append(";");
            sb.Append(ProgramConstants.GAME_VERSION);
            sb.Append(";");
            sb.Append(SGPlayers.Count);
            sb.Append(";");
            sb.Append(channel.ChannelName);
            sb.Append(";");
            sb.Append(channel.UIName);
            sb.Append(";");
            if (started || Players.Count == SGPlayers.Count)
                sb.Append("1");
            else
                sb.Append("0");
            sb.Append("0"); // IsCustomPassword
            sb.Append("0"); // Closed
            sb.Append("1"); // IsLoadedGame
            sb.Append("0"); // IsLadder
            sb.Append(";");
            foreach (SavedGamePlayer sgPlayer in SGPlayers)
            {
                sb.Append(sgPlayer.Name);
                sb.Append(",");
            }

            sb.Remove(sb.Length - 1, 1);
            sb.Append(";");
            sb.Append((string)lblMapNameValue.Tag);
            sb.Append(";");
            sb.Append((string)lblGameModeValue.Tag);
            sb.Append(";");
            sb.Append(_useDynamicTunnels
                ? "[DYN]"
                : tunnelHandler.CurrentTunnel != null
                    ? tunnelHandler.CurrentTunnel.Address + ":" + tunnelHandler.CurrentTunnel.Port
                    : "0.0.0.0:0");
            sb.Append(";");
            sb.Append(0); // LoadedGameId
            sb.Append(";");
            sb.Append(ClientConfiguration.Instance.DefaultSkillLevelIndex); // we don't know the original skill level
            sb.Append(";"); // Map SHA1
            sb.Append(";"); // Game option values

            broadcastChannel.SendCTCPMessage(sb.ToString(), QueuedMessageType.SYSTEM_MESSAGE, 20);
        }

        public override string GetSwitchName() => "Load Game".L10N("Client:Main:LoadGame");

        protected override void UpdateDiscordPresence(bool resetTimer = false)
        {
            if (discordHandler == null)
                return;

            PlayerInfo player = Players.Find(p => p.Name == ProgramConstants.PLAYERNAME);
            if (player == null)
                return;
            string currentState = ProgramConstants.IsInGame ? "In Game" : "In Lobby"; // not UI strings

            discordHandler.UpdatePresence(
                (string)lblMapNameValue.Tag, (string)lblGameModeValue.Tag, "Multiplayer",
                currentState, Players.Count, SGPlayers.Count,
                channel.UIName, IsHost, resetTimer);
        }

        #region V3 Tunnel Support

        private List<CnCNetTunnel> GetAvailableTunnelsForNegotiation()
        {
            return tunnelHandler.Tunnels
                .Where(t => t.Version == 3 &&
                    (UserINISettings.Instance.PingUnofficialCnCNetTunnels || t.Official || t.Recommended))
                .ToList();
        }

        private void RegenerateV3PlayerInfos()
        {
            var playersToRemove = _v3PlayerInfos.Where(v3p => !Players.Any(p => p.Name == v3p.Name)).ToList();
            foreach (var v3p in playersToRemove)
            {
                if (v3p.Negotiator != null)
                {
                    v3p.Negotiator.NegotiationResult -= OnPlayerNegotiationResult;
                    v3p.Negotiator.NegotiationComplete -= OnPlayerNegotiationComplete;
                }
                v3p.StopNegotiation();
                _v3PlayerInfos.Remove(v3p);
            }

            for (int i = 0; i < Players.Count; i++)
            {
                var player = Players[i];
                var v3Player = _v3PlayerInfos.FirstOrDefault(v3p => v3p.Name == player.Name);
                if (v3Player == null)
                {
                    _v3PlayerInfos.Add(new V3PlayerInfo(
                        GeneratePlayerID(player.Name),
                        player.Name,
                        i,
                        0
                    ));
                }
                else
                {
                    v3Player.PlayerIndex = i;
                }
            }
        }

        private uint GeneratePlayerID(string playerName)
        {
            using var sha1 = SHA1.Create();
            byte[] hash = sha1.ComputeHash(Encoding.UTF8.GetBytes($"{playerName}:{channel.ChannelName}"));
            return BinaryPrimitives.ReadUInt32LittleEndian(hash);
        }

        private bool AreAllNegotiationsSuccessful()
        {
            if (!_useDynamicTunnels || Players.Count <= 1)
                return true;

            return _negotiationData.AreAllNegotiationsSuccessful(Players.Select(p => p.Name).ToList());
        }

        private void UpdateLoadGameButtonStatus()
        {
            if (IsHost)
                btnLoadGame.AllowClick = !_useDynamicTunnels || AreAllNegotiationsSuccessful();
        }

        private void StartTunnelNegotiationForPlayer(V3PlayerInfo player)
        {
            if (!_useDynamicTunnels || player.Name == ProgramConstants.PLAYERNAME)
                return;

            var localV3Player = _v3PlayerInfos.FirstOrDefault(p => p.Name == ProgramConstants.PLAYERNAME);
            if (localV3Player == null)
                return;

            var availableTunnels = GetAvailableTunnelsForNegotiation();

            if (availableTunnels.Count == 0)
            {
                AddNotice("Cannot negotiate tunnel: no V3 tunnels are available.", Color.Yellow);
                BroadcastNegotiationInfo(player.Name, NegotiationStatus.Failed);
                UpdateLoadGameButtonStatus();
                return;
            }

            _negotiationData.UpdateStatus(ProgramConstants.PLAYERNAME, player.Name, NegotiationStatus.InProgress);
            BroadcastNegotiationInfo(player.Name, NegotiationStatus.InProgress);
            UpdateLoadGameButtonStatus();

            try
            {
                var startResult = player.StartNegotiation(localV3Player, tunnelHandler, availableTunnels);

                switch (startResult)
                {
                    case NegotiationStartResult.Started:
                        if (player.Negotiator != null)
                        {
                            player.Negotiator.NegotiationResult += OnPlayerNegotiationResult;
                            player.Negotiator.NegotiationComplete += OnPlayerNegotiationComplete;
                        }
                        break;

                    case NegotiationStartResult.AlreadyInProgress:
                        break;

                    case NegotiationStartResult.Failed:
                        _negotiationData.UpdateStatus(ProgramConstants.PLAYERNAME, player.Name, NegotiationStatus.Failed);
                        BroadcastNegotiationInfo(player.Name, NegotiationStatus.Failed);
                        UpdateLoadGameButtonStatus();
                        break;
                }
            }
            catch (Exception ex)
            {
                Logger.Log($"Error negotiating with player {player.Name}: {ex.Message}");
                _negotiationData.UpdateStatus(ProgramConstants.PLAYERNAME, player.Name, NegotiationStatus.Failed);
                BroadcastNegotiationInfo(player.Name, NegotiationStatus.Failed);
                UpdateLoadGameButtonStatus();
            }
        }

        private void OnPlayerNegotiationResult(object sender, TunnelChosenEventArgs e)
        {
            var negotiator = sender as V3PlayerNegotiator;
            var v3PlayerInfo = _v3PlayerInfos.FirstOrDefault(p => p.Id == e.PlayerId);
            if (v3PlayerInfo == null) return;

            v3PlayerInfo.HasNegotiated = true;
            v3PlayerInfo.IsNegotiating = false;

            if (e.ChosenTunnel != null)
            {
                v3PlayerInfo.Tunnel = e.ChosenTunnel;

                if (e.IsLocalDecision)
                    _negotiationData.UpdatePing(ProgramConstants.PLAYERNAME, e.PlayerName, e.NegotiationPing);
                else
                    _negotiationData.UpdatePing(e.PlayerName, ProgramConstants.PLAYERNAME, e.NegotiationPing);

                _negotiationData.UpdateStatus(ProgramConstants.PLAYERNAME, e.PlayerName, NegotiationStatus.Succeeded);
                BroadcastNegotiationInfo(e.PlayerName, NegotiationStatus.Succeeded, e.NegotiationPing);

                AddNotice($"Tunnel negotiated with {e.PlayerName}: {e.ChosenTunnel.Name}");
            }
            else
            {
                _negotiationData.UpdateStatus(ProgramConstants.PLAYERNAME, e.PlayerName, NegotiationStatus.Failed);
                BroadcastNegotiationInfo(e.PlayerName, NegotiationStatus.Failed);
            }

            if (negotiator != null)
                negotiator.NegotiationResult -= OnPlayerNegotiationResult;

            UpdateLoadGameButtonStatus();
        }

        private void OnPlayerNegotiationComplete(object sender, EventArgs e)
        {
            var negotiator = (V3PlayerNegotiator)sender;
            var player = negotiator.RemotePlayer;
            if (player == null) return;

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

            UpdateLoadGameButtonStatus();
        }

        private void BroadcastNegotiationInfo(string targetPlayer, NegotiationStatus status, int ping = -1)
        {
            string message = ping >= 0
                ? $"{NEGOTIATION_INFO_MESSAGE} {targetPlayer};{status};{ping}"
                : $"{NEGOTIATION_INFO_MESSAGE} {targetPlayer};{status}";

            channel.SendCTCPMessage(message, QueuedMessageType.SYSTEM_MESSAGE, 10);
        }

        private void SendStartV3ToPlayers()
        {
            var sb = new StringBuilder(START_GAME_V3_CTCP_COMMAND + " ");
            bool first = true;

            for (int i = 0; i < Players.Count; i++)
            {
                var player = Players[i];
                uint id = GeneratePlayerID(player.Name);
                int port = 48000 - i;
                player.Port = port;

                var v3PlayerInfo = _v3PlayerInfos.FirstOrDefault(v3p => v3p.Name == player.Name);
                if (v3PlayerInfo != null)
                {
                    v3PlayerInfo.Id = id;
                    v3PlayerInfo.PlayerIndex = i;
                    if (!_useDynamicTunnels)
                        v3PlayerInfo.Tunnel = tunnelHandler.CurrentTunnel;
                    v3PlayerInfo.PlayerGameId = (ushort)port;
                }

                string tunnelAddress = (!_useDynamicTunnels && v3PlayerInfo?.Tunnel != null)
                    ? $"{v3PlayerInfo.Tunnel.Address}:{v3PlayerInfo.Tunnel.Port}"
                    : "0.0.0.0:0";

                if (!first) sb.Append(';');
                sb.Append(id).Append(';').Append(player.Name).Append(';').Append(tunnelAddress);
                first = false;
            }

            channel.SendCTCPMessage(sb.ToString(), QueuedMessageType.SYSTEM_MESSAGE, 9);
        }

        private void StartV3Game()
        {
            var localV3Player = _v3PlayerInfos.FirstOrDefault(p => p.Name == ProgramConstants.PLAYERNAME);
            if (localV3Player == null)
            {
                Logger.Log("CnCNetGameLoadingLobby: Could not find local V3 player info.");
                return;
            }

            tunnelHandler.StartGameBridge(localV3Player.Id, localV3Player.PlayerGameId, _v3PlayerInfos);
            LoadGame();
        }

        private void RestartNegotiations(List<V3PlayerInfo> affectedPlayers)
        {
            foreach (var v3Player in affectedPlayers)
            {
                v3Player.StopNegotiation();
                v3Player.ResetNegotiator();
                _negotiationData.ClearPlayer(v3Player.Name);

                if (v3Player.Name != ProgramConstants.PLAYERNAME)
                    StartTunnelNegotiationForPlayer(v3Player);
            }
        }

        private void TunnelHandler_TunnelFailed(object sender, CnCNetTunnel failedTunnel)
        {
            if (tunnelHandler.GameTunnelBridge != null && tunnelHandler.GameTunnelBridge.IsRunning)
                return;

            if (_useDynamicTunnels)
            {
                var affectedPlayers = _v3PlayerInfos
                    .Where(p => p.Name != ProgramConstants.PLAYERNAME &&
                               p.Tunnel?.Address == failedTunnel.Address &&
                               p.Tunnel?.Port == failedTunnel.Port)
                    .ToList();

                if (affectedPlayers.Count > 0)
                {
                    AddNotice($"Tunnel {failedTunnel.Name} failed. Starting renegotiation with affected players...", Color.Orange);
                    channel.SendCTCPMessage($"{TUNNEL_RENEGOTIATE_MESSAGE} {failedTunnel.Address}:{failedTunnel.Port}",
                        QueuedMessageType.SYSTEM_MESSAGE, 10);
                    RestartNegotiations(affectedPlayers);
                }
            }
            else
            {
                if (IsHost)
                    AddNotice($"Tunnel {failedTunnel.Name} failed. Please select a different tunnel.", Color.Orange);
                else
                {
                    AddNotice($"Tunnel {failedTunnel.Name} failed. Waiting for host to select a new tunnel...", Color.Orange);
                    channel.SendCTCPMessage($"{TUNNEL_FAILED_MESSAGE} {failedTunnel.Name}",
                        QueuedMessageType.SYSTEM_MESSAGE, 10);
                }
            }
        }

        #endregion
    }
}
