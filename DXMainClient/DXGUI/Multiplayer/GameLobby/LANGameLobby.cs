using ClientCore;
using ClientLogic.Lobby;
using ClientLogic.Protocol;
using ClientGUI;
using DTAClient.Domain;
using DTAClient.Domain.LAN;
using DTAClient.Domain.Multiplayer;
using DTAClient.Domain.Multiplayer.LAN;
using DTAClient.DXGUI.Generic;
using DTAClient.DXGUI.Multiplayer.GameLobby.CommandHandlers;
using DTAClient.Online;
using ClientCore.Extensions;
using Microsoft.Xna.Framework;
using Rampastring.Tools;
using Rampastring.XNAUI;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using DTAClient.DXGUI.Multiplayer.CnCNet;


namespace DTAClient.DXGUI.Multiplayer.GameLobby
{
    public class LANGameLobby : MultiplayerGameLobby
    {
        private const double DROPOUT_TIMEOUT = 20.0;
        private const double GAME_BROADCAST_INTERVAL = 2.0;

        private const string CHAT_COMMAND = "GLCHAT";
        private const string RETURN_COMMAND = "RETURN";
        private const string GET_READY_COMMAND = "GETREADY";
        private const string PLAYER_OPTIONS_REQUEST_COMMAND = "POREQ";
        private const string PLAYER_OPTIONS_BROADCAST_COMMAND = "POPTS";
        private const string PLAYER_JOIN_COMMAND = "JOIN";
        private const string PLAYER_QUIT_COMMAND = "QUIT";
        private const string GAME_OPTIONS_COMMAND = "OPTS";
        private const string PLAYER_READY_REQUEST = "READY";
        private const string LAUNCH_GAME_COMMAND = "LAUNCH";
        private const string FILE_HASH_COMMAND = "FHASH";
        private const string DICE_ROLL_COMMAND = "DR";
        public const string PING = "PING";

        public LANGameLobby(WindowManager windowManager, string iniName,
            TopBar topBar, LANColor[] chatColors, MapLoader mapLoader, DiscordHandler discordHandler, PrivateMessagingWindow pmWindow, Random random) :
            base(windowManager, iniName, topBar, mapLoader, discordHandler, pmWindow, random)
        {
            this.chatColors = chatColors;
            encoding = Encoding.UTF8;
            hostCommandHandlers = new CommandHandlerBase[]
            {
                new StringCommandHandler(CHAT_COMMAND, GameHost_HandleChatCommand),
                new NoParamCommandHandler(RETURN_COMMAND, GameHost_HandleReturnCommand),
                new StringCommandHandler(PLAYER_OPTIONS_REQUEST_COMMAND, HandlePlayerOptionsRequest),
                new NoParamCommandHandler(PLAYER_QUIT_COMMAND, HandlePlayerQuit),
                new StringCommandHandler(PLAYER_READY_REQUEST, GameHost_HandleReadyRequest),
                new StringCommandHandler(FILE_HASH_COMMAND, HandleFileHashCommand),
                new StringCommandHandler(DICE_ROLL_COMMAND, Host_HandleDiceRoll),
                new NoParamCommandHandler(PING, s => { }),
            };

            playerCommandHandlers = new LANClientCommandHandler[]
            {
                new ClientStringCommandHandler(CHAT_COMMAND, Player_HandleChatCommand),
                new ClientNoParamCommandHandler(GET_READY_COMMAND, HandleGetReadyCommand),
                new ClientNoParamCommandHandler(PLAYER_QUIT_COMMAND, HandleHostQuit),
                new ClientStringCommandHandler(RETURN_COMMAND, Player_HandleReturnCommand),
                new ClientStringCommandHandler(PLAYER_OPTIONS_BROADCAST_COMMAND, HandlePlayerOptionsBroadcast),
                new ClientStringCommandHandler(PlayerExtraOptions.LAN_MESSAGE_KEY, HandlePlayerExtraOptionsBroadcast),
                new ClientStringCommandHandler(LAUNCH_GAME_COMMAND, HandleGameLaunchCommand),
                new ClientStringCommandHandler(GAME_OPTIONS_COMMAND, HandleGameOptionsMessage),
                new ClientStringCommandHandler(DICE_ROLL_COMMAND, Client_HandleDiceRoll),
                new ClientNoParamCommandHandler(PING, HandlePing),
            };

            localGame = ClientConfiguration.Instance.LocalGame;

            WindowManager.GameClosing += WindowManager_GameClosing;

            this.random = random;
        }

        private void WindowManager_GameClosing(object sender, EventArgs e)
        {
            if (client != null && client.Connected)
                Clear();
        }

        private void HandleFileHashCommand(string sender, string fileHash)
        {
            if (fileHash != localFileHash)
                AddNotice(string.Format("{0} has modified game files! They could be cheating!".L10N("Client:Main:PlayerModifiedFiles"), sender));

            PlayerInfo pInfo = Players.Find(p => p.Name == sender);
            if (pInfo == null)
                return;

            pInfo.HashReceived = true;
            CopyPlayerDataToUI();
        }

        public event EventHandler<LobbyNotificationEventArgs> LobbyNotification;
        public event EventHandler<GameLeftEventArgs> GameLeft;
        public event EventHandler<GameBroadcastEventArgs> GameBroadcast;

        private TcpListener listener;
        private TcpClient client;
        private volatile bool leaving;
        private int sessionId;

        private IPEndPoint hostEndPoint;
        private LANColor[] chatColors;
        private int chatColorIndex;
        private Encoding encoding;

        private CommandHandlerBase[] hostCommandHandlers;
        private LANClientCommandHandler[] playerCommandHandlers;

        private TimeSpan timeSinceGameBroadcast = TimeSpan.Zero;

        private TimeSpan timeSinceLastReceivedCommand = TimeSpan.Zero;

        private string overMessage = string.Empty;

        private string localGame;

        private string localFileHash;

        private Random random;

        public override void Initialize()
        {
            IniNameOverride = nameof(LANGameLobby);
            base.Initialize();
            PostInitialize();
        }

        public bool SetUp(bool isHost,
            IPEndPoint hostEndPoint, TcpClient client)
        {
            if (isHost && !StartHosting())
                return false;

            leaving = false;
            sessionId++;
            Refresh(isHost);

            this.hostEndPoint = hostEndPoint;

            if (isHost)
            {
                RandomSeed = random.Next();
                Thread thread = new Thread(ListenForClients);
                thread.Start();

                byte[] buffer = encoding.GetBytes(PLAYER_JOIN_COMMAND +
                    ProgramConstants.LAN_DATA_SEPARATOR + ProgramConstants.PLAYERNAME);

                this.client.GetStream().Write(buffer, 0, buffer.Length);
                this.client.GetStream().Flush();

                var fhc = new FileHashCalculator();
                fhc.CalculateHashes();
                localFileHash = fhc.GetCompleteHash();

                RefreshMapSelectionUI();
            }
            else
            {
                this.client = client;
            }

            new Thread(HandleServerCommunication).Start();

            if (IsHost)
                CopyPlayerDataToUI();

            WindowManager.SelectedControl = tbChatInput;
            return true;
        }

        public void PostJoin()
        {
            var fhc = new FileHashCalculator();
            fhc.CalculateHashes();
            SendMessageToHost(FILE_HASH_COMMAND + " " + fhc.GetCompleteHash());
            ResetAutoReadyCheckbox();
        }

        #region Server code

        private bool StartHosting()
        {
            try
            {
                listener = new TcpListener(IPAddress.Any, ProgramConstants.LAN_GAME_LOBBY_PORT);
                listener.Start();

                this.client = new TcpClient();
                this.client.Connect("127.0.0.1", ProgramConstants.LAN_GAME_LOBBY_PORT);
                return true;
            }
            catch (SocketException ex)
            {
                Logger.Log("Failed to start hosting the LAN game lobby: " + ex.ToString());
                listener?.Stop();
                this.client?.Close();
                XNAMessageBox.Show(WindowManager, "Error".L10N("Client:Main:Error"),
                    string.Format("Unable to host the game because TCP port {0} could not be opened. It may already be in use by another program.".L10N("Client:Main:LANListenerStartFailed"),
                    ProgramConstants.LAN_GAME_LOBBY_PORT));
                return false;
            }
        }

        private void ListenForClients()
        {
            while (true)
            {
                TcpClient client;

                try
                {
                    client = listener.AcceptTcpClient();
                }
                catch (Exception ex)
                {
                    Logger.Log("Listener error: " + ex.ToString());
                    break;
                }

                Logger.Log("New client connected from " + ((IPEndPoint)client.Client.RemoteEndPoint).Address.ToString());

                if (Players.Count >= MAX_PLAYER_COUNT)
                {
                    Logger.Log("Dropping client because of player limit.");
                    client.Close();
                    continue;
                }

                if (Locked)
                {
                    Logger.Log("Dropping client because the game room is locked.");
                    client.Close();
                    continue;
                }

                LANPlayerInfo lpInfo = new LANPlayerInfo(encoding);
                lpInfo.SetClient(client);

                Thread thread = new Thread(new ParameterizedThreadStart(HandleClientConnection));
                thread.Start(lpInfo);
            }
        }

        private void HandleClientConnection(object clientInfo)
        {
            var lpInfo = (LANPlayerInfo)clientInfo;

            byte[] message = new byte[1024];

            while (true)
            {
                int bytesRead = 0;

                try
                {
                    bytesRead = lpInfo.TcpClient.GetStream().Read(message, 0, message.Length);
                }
                catch (Exception ex)
                {
                    Logger.Log("Socket error with client " + lpInfo.IPAddress + "; removing. Message: " + ex.ToString());
                    break;
                }

                if (bytesRead == 0)
                {
                    Logger.Log("Connect attempt from " + lpInfo.IPAddress + " failed! (0 bytes read)");

                    break;
                }

                string msg = encoding.GetString(message, 0, bytesRead);

                string[] command = msg.Split(ProgramConstants.LAN_MESSAGE_SEPARATOR);
                string[] parts = command[0].Split(ProgramConstants.LAN_DATA_SEPARATOR);

                if (parts.Length != 2)
                    break;

                string name = parts[1].Trim();

                if (parts[0] == "JOIN" && !string.IsNullOrEmpty(name))
                {
                    lpInfo.Name = name;

                    AddCallback(new Action<LANPlayerInfo>(AddPlayer), lpInfo);
                    return;
                }

                break;
            }

            if (lpInfo.TcpClient.Connected)
                lpInfo.TcpClient.Close();
        }

        private void AddPlayer(LANPlayerInfo lpInfo)
        {
            if (Players.Find(p => p.Name == lpInfo.Name) != null ||
                Players.Count >= MAX_PLAYER_COUNT || Locked)
                return;

            Players.Add(lpInfo);

            while (Players.Count + AIPlayers.Count > MAX_PLAYER_COUNT && AIPlayers.Count > 0)
                AIPlayers.RemoveAt(AIPlayers.Count - 1);

            if (IsHost && Players.Count == 1)
                Players[0].Ready = true;

            lpInfo.MessageReceived += LpInfo_MessageReceived;
            lpInfo.ConnectionLost += LpInfo_ConnectionLost;

            AddNotice(string.Format("{0} connected from {1}".L10N("Client:Main:PlayerFromIP"), lpInfo.Name, lpInfo.IPAddress));
            lpInfo.StartReceiveLoop();

            OnGameOptionChanged();
            CopyPlayerDataToUI();
            BroadcastPlayerOptions();
            BroadcastPlayerExtraOptions();
            UpdateDiscordPresence();
        }

        private void LpInfo_ConnectionLost(object sender, EventArgs e)
        {
            AddCallback(new Action<LANPlayerInfo>(HandleConnectionLost), (LANPlayerInfo)sender);
        }

        private void HandleConnectionLost(LANPlayerInfo lpInfo)
        {
            CleanUpPlayer(lpInfo);
            Players.Remove(lpInfo);

            AddNotice(string.Format("{0} has left the game.".L10N("Client:Main:PlayerLeftGame"), lpInfo.Name));

            CopyPlayerDataToUI();
            BroadcastPlayerOptions();

            if (lpInfo.Name == ProgramConstants.PLAYERNAME)
                ResetDiscordPresence();
            else
                UpdateDiscordPresence();
        }

        private void LpInfo_MessageReceived(object sender, NetworkMessageEventArgs e)
        {
            AddCallback(new Action<string, LANPlayerInfo>(HandleClientMessage),
                e.Message, (LANPlayerInfo)sender);
        }

        private void HandleClientMessage(string data, LANPlayerInfo lpInfo)
        {
            lpInfo.TimeSinceLastReceivedMessage = TimeSpan.Zero;

            foreach (CommandHandlerBase cmdHandler in hostCommandHandlers)
            {
                if (cmdHandler.Handle(lpInfo.Name, data))
                    return;
            }

            Logger.Log("Unknown LAN command from " + lpInfo.ToString() + " : " + data);
        }

        private void CleanUpPlayer(LANPlayerInfo lpInfo)
        {
            lpInfo.MessageReceived -= LpInfo_MessageReceived;
            lpInfo.ConnectionLost -= LpInfo_ConnectionLost;
            lpInfo.TcpClient.Close();
        }

        #endregion

        private void HandleServerCommunication()
        {
            byte[] message = new byte[1024];

            var msg = string.Empty;

            int bytesRead = 0;

            int mySessionId = sessionId;

            if (!client.Connected)
                return;

            var stream = client.GetStream();

            while (true)
            {
                bytesRead = 0;

                try
                {
                    bytesRead = stream.Read(message, 0, message.Length);
                }
                catch (Exception ex)
                {
                    // Disconnect from server

                    if (leaving)
                        break;

                    Logger.Log(string.Format(
                        "Reading data from the server failed! Server address: {0}. Exception: {1}",
                        hostEndPoint.Address.ToString(), ex.ToString()));

                    string localizedMessage = string.Format(
                        "Reading data from the server failed! Server address: {0}. Exception: {1}".L10N("Client:Main:LanServerReadError"),
                         hostEndPoint.Address.ToString(), ex.Message);

                    AddCallback(() =>
                    {
                        if (sessionId == mySessionId)
                            LeaveGame(localizedMessage);
                    });
                    break;
                }

                if (bytesRead > 0)
                {
                    msg = encoding.GetString(message, 0, bytesRead);

                    msg = overMessage + msg;
                    List<string> commands = new List<string>();

                    while (true)
                    {
                        int index = msg.IndexOf(ProgramConstants.LAN_MESSAGE_SEPARATOR);

                        if (index == -1)
                        {
                            overMessage = msg;
                            break;
                        }
                        else
                        {
                            commands.Add(msg.Substring(0, index));
                            msg = msg.Substring(index + 1);
                        }
                    }

                    foreach (string cmd in commands)
                    {
                        string capturedCmd = cmd;
                        AddCallback(() =>
                        {
                            if (sessionId == mySessionId)
                                HandleMessageFromServer(capturedCmd);
                        });
                    }

                    continue;
                }

                // Disconnect from server
                if (leaving)
                    break;

                {
                    Logger.Log(string.Format(
                        "Reading data from the server failed (0 bytes received)! Server address: {0}", hostEndPoint.Address.ToString()));

                    string localizedMessage = string.Format(
                        "Reading data from the server failed (0 bytes received)! Server address: {0}".L10N("Client:Main:LanServerReadZero"),
                         hostEndPoint.Address.ToString());

                    AddCallback(() =>
                    {
                        if (sessionId == mySessionId)
                            LeaveGame(localizedMessage);
                    });
                }

                break;
            }
        }

        private void HandleMessageFromServer(string message)
        {
            timeSinceLastReceivedCommand = TimeSpan.Zero;

            foreach (var cmdHandler in playerCommandHandlers)
            {
                if (cmdHandler.Handle(message))
                    return;
            }

            Logger.Log("Unknown LAN command from the server: " + message);
        }

        protected override void BtnLeaveGame_LeftClick(object sender, EventArgs e) => LeaveGame();

        protected void LeaveGame(string message = null)
        {
            if (leaving)
                return;

            Clear();
            GameLeft?.Invoke(this, new GameLeftEventArgs() { Message = message });
            PlayerExtraOptionsPanel?.Disable();
            Disable();
        }

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
                Map.UntranslatedName, GameMode.UntranslatedUIName, "LAN",
                currentState, Players.Count, 8, side,
                "LAN Game", IsHost, false, Locked, resetTimer);
        }

        public override void Clear()
        {
            if (IsHost)
            {
                GameBroadcast?.Invoke(this, new GameBroadcastEventArgs("GAMECLOSED"));
                BroadcastMessage(PLAYER_QUIT_COMMAND);
                Players.ForEach(p => CleanUpPlayer((LANPlayerInfo)p));
                listener.Stop();
            }
            else
            {
                SendMessageToHost(PLAYER_QUIT_COMMAND);
            }

            base.Clear();

            leaving = true;

            if (this.client.Connected)
                this.client.Close();

            ResetDiscordPresence();
        }

        public void SetChatColorIndex(int colorIndex)
        {
            chatColorIndex = colorIndex;
            tbChatInput.TextColor = chatColors[colorIndex].XNAColor;
        }

        public override string GetSwitchName() => "LAN Game Lobby".L10N("Client:Main:LANGameLobby");

        protected override void AddNotice(string message, Color color) =>
            lbChatMessages.AddMessage(null, message, color);

        protected override void BroadcastPlayerOptions()
        {
            if (!IsHost)
                return;

            var entries = Players.Concat(AIPlayers).Select(pInfo => new PlayerOptionsEntry(
                pInfo.IsAI ? string.Empty : pInfo.Name,
                pInfo.IsAI ? pInfo.AILevel : -1,
                new PackedPlayerOptions(pInfo.SideId, pInfo.ColorId, pInfo.StartingLocation, pInfo.TeamId),
                pInfo.AutoReady && !pInfo.IsInGame && !LastMapChangeWasInvalid ? 2 : Convert.ToInt32(pInfo.IsAI || pInfo.Ready),
                pInfo.IPAddress)).ToList();

            BroadcastMessage(PLAYER_OPTIONS_BROADCAST_COMMAND + " " + new PlayerOptionsMessage(entries).Encode());
        }

        protected override void BroadcastPlayerExtraOptions()
        {
            var playerExtraOptions = GetPlayerExtraOptions();

            BroadcastMessage(playerExtraOptions.ToLanMessage(), true);
        }

        protected override void HostLaunchGame() => BroadcastMessage(LAUNCH_GAME_COMMAND + " " + new LaunchMessage(UniqueGameID).Encode());

        protected override string GetIPAddressForPlayer(PlayerInfo player)
        {
            var lpInfo = (LANPlayerInfo)player;
            return lpInfo.IPAddress;
        }

        protected override void RequestPlayerOptions(int side, int color, int start, int team)
        {
            var message = new PlayerOptionsRequestMessage(new PackedPlayerOptions(side, color, start, team));
            SendMessageToHost(PLAYER_OPTIONS_REQUEST_COMMAND + " " + message.Encode());
        }

        protected override void RequestReadyStatus() =>
            SendMessageToHost(PLAYER_READY_REQUEST + " " + new ReadyRequestMessage(chkAutoReady.Checked ? 2 : 1).Encode());

        protected override void SendChatMessage(string message)
        {
            var sb = new ExtendedStringBuilder(CHAT_COMMAND + " ", true);
            sb.Separator = ProgramConstants.LAN_DATA_SEPARATOR;
            sb.Append(chatColorIndex);
            sb.Append(message);
            SendMessageToHost(sb.ToString());
        }

        protected override void OnGameOptionChanged()
        {
            base.OnGameOptionChanged();

            if (!IsHost)
                return;

            var message = new GameOptionsMessage
            {
                CheckBoxValues = CheckBoxes.Select(chkBox => chkBox.Checked).ToList(),
                DropDownIndices = DropDowns.Select(dd => dd.SelectedIndex).ToList(),
                IsMapOfficial = Map?.Official ?? false,
                MapSHA1 = Map?.SHA1 ?? string.Empty,
                GameModeName = GameMode?.Name ?? string.Empty,
                FrameSendRate = FrameSendRate,
                MaxAhead = MaxAhead,
                ProtocolVersion = ProtocolVersion,
                RandomSeed = RandomSeed,
                RemoveStartingLocations = RemoveStartingLocations,
                MapName = Map?.UntranslatedName ?? string.Empty,
            };

            BroadcastMessage(GAME_OPTIONS_COMMAND + " " + message.Encode());
        }

        protected override void GetReadyNotification()
        {
            base.GetReadyNotification();
#if WINFORMS
            WindowManager.FlashWindow();
#endif

            if (IsHost)
                BroadcastMessage(GET_READY_COMMAND);
        }

        protected override void ClearPingIndicators()
        {
            // TODO Implement pings for LAN lobbies
        }

        protected override void UpdatePlayerPingIndicator(PlayerInfo pInfo)
        {
            // TODO Implement pings for LAN lobbies
        }

        /// <summary>
        /// Broadcasts a command to all players in the game as the game host.
        /// </summary>
        /// <param name="message">The command to send.</param>
        /// <param name="otherPlayersOnly">If true, only send this to other players. Otherwise, even the sender will receive their message.</param>
        private void BroadcastMessage(string message, bool otherPlayersOnly = false)
        {
            if (!IsHost)
                return;

            foreach (PlayerInfo pInfo in Players.Where(p => !otherPlayersOnly || p.Name != ProgramConstants.PLAYERNAME))
            {
                var lpInfo = (LANPlayerInfo)pInfo;
                lpInfo.SendMessage(message);
            }
        }

        protected override void PlayerExtraOptions_OptionsChanged(object sender, EventArgs e)
        {
            base.PlayerExtraOptions_OptionsChanged(sender, e);
            BroadcastPlayerExtraOptions();
        }

        private void SendMessageToHost(string message)
        {
            if (!client.Connected)
                return;

            byte[] buffer = encoding.GetBytes(message + ProgramConstants.LAN_MESSAGE_SEPARATOR);

            NetworkStream ns = client.GetStream();

            try
            {
                ns.Write(buffer, 0, buffer.Length);
                ns.Flush();
            }
            catch
            {
                Logger.Log("Sending message to game host failed!");
            }
        }

        protected override void UnlockGame(bool manual)
        {
            Locked = false;

            btnLockGame.Text = "Lock Game".L10N("Client:Main:LockGame");

            if (manual)
                AddNotice("You've unlocked the game room.".L10N("Client:Main:RoomUnlockedByYou"));
        }

        protected override void LockGame()
        {
            Locked = true;

            btnLockGame.Text = "Unlock Game".L10N("Client:Main:UnlockGame");

            if (Locked)
                AddNotice("You've locked the game room.".L10N("Client:Main:RoomLockedByYou"));
        }

        protected override void GameProcessExited()
        {
            base.GameProcessExited();

            SendMessageToHost(RETURN_COMMAND);

            if (IsHost)
            {
                RandomSeed = random.Next();
                OnGameOptionChanged();
                ClearReadyStatuses();
                CopyPlayerDataToUI();
                BroadcastPlayerOptions();
                BroadcastPlayerExtraOptions();

                if (Players.Count < MAX_PLAYER_COUNT)
                {
                    UnlockGame(true);
                }
            }
        }

        private void ReturnNotification(string sender)
        {
            AddNotice(string.Format("{0} has returned from the game.".L10N("Client:Main:PlayerReturned"), sender));

            PlayerInfo pInfo = Players.Find(p => p.Name == sender);

            if (pInfo != null)
                pInfo.IsInGame = false;

            sndReturnSound.Play();
            CopyPlayerDataToUI();
        }

        public override void Update(GameTime gameTime)
        {
            if (IsHost)
            {
                for (int i = 1; i < Players.Count; i++)
                {
                    LANPlayerInfo lpInfo = (LANPlayerInfo)Players[i];
                    if (!lpInfo.Update(gameTime))
                    {
                        CleanUpPlayer(lpInfo);
                        Players.RemoveAt(i);
                        AddNotice(string.Format("{0} - connection timed out".L10N("Client:Main:PlayerTimeout"), lpInfo.Name));
                        CopyPlayerDataToUI();
                        BroadcastPlayerOptions();
                        BroadcastPlayerExtraOptions();
                        UpdateDiscordPresence();
                        i--;
                    }
                }

                timeSinceGameBroadcast += gameTime.ElapsedGameTime;

                if (timeSinceGameBroadcast > TimeSpan.FromSeconds(GAME_BROADCAST_INTERVAL))
                {
                    BroadcastGame();
                    timeSinceGameBroadcast = TimeSpan.Zero;
                }
            }
            else
            {
                timeSinceLastReceivedCommand += gameTime.ElapsedGameTime;

                if (timeSinceLastReceivedCommand > TimeSpan.FromSeconds(DROPOUT_TIMEOUT))
                {
                    string localizedMessage = string.Format(
                        "Connection to the game host timed out. Server address: {0}".L10N("Client:Main:HostConnectTimeOutWithAddress"),
                        hostEndPoint.Address.ToString());

                    LobbyNotification?.Invoke(this,
                        new LobbyNotificationEventArgs(localizedMessage));
                    LeaveGame(localizedMessage);
                }
            }

            base.Update(gameTime);
        }

        private void BroadcastGame()
        {
            var sb = new ExtendedStringBuilder("GAME ", true);
            sb.Separator = ProgramConstants.LAN_DATA_SEPARATOR;
            sb.Append(ProgramConstants.LAN_PROTOCOL_REVISION);
            sb.Append(ProgramConstants.GAME_VERSION);
            sb.Append(localGame);
            sb.Append(Map?.UntranslatedName ?? string.Empty);
            sb.Append(GameMode?.UntranslatedUIName ?? string.Empty);
            sb.Append(0); // LoadedGameID
            var sbPlayers = new StringBuilder();
            Players.ForEach(p => sbPlayers.Append(p.Name + ","));
            sbPlayers.Remove(sbPlayers.Length - 1, 1);
            sb.Append(sbPlayers.ToString());
            sb.Append(Convert.ToInt32(Locked));
            sb.Append(0); // IsLoadedGame
            sb.Append(Map?.SHA1);

            GameBroadcast?.Invoke(this, new GameBroadcastEventArgs(sb.ToString()));
        }

        #region Command Handlers

        private void GameHost_HandleChatCommand(string sender, string data)
        {
            string[] parts = data.Split(ProgramConstants.LAN_DATA_SEPARATOR);

            if (parts.Length < 2)
                return;

            int colorIndex = Conversions.IntFromString(parts[0], -1);

            if (colorIndex < 0 || colorIndex >= chatColors.Length)
                return;

            BroadcastMessage(CHAT_COMMAND + " " + sender + ProgramConstants.LAN_DATA_SEPARATOR + data);
        }

        private void Player_HandleChatCommand(string data)
        {
            string[] parts = data.Split(ProgramConstants.LAN_DATA_SEPARATOR);

            if (parts.Length < 3)
                return;

            string playerName = parts[0];

            int colorIndex = Conversions.IntFromString(parts[1], -1);

            if (colorIndex < 0 || colorIndex >= chatColors.Length)
                return;

            lbChatMessages.AddMessage(new ChatMessage(playerName,
                chatColors[colorIndex].XNAColor, DateTime.Now, parts[2]));
        }

        private void GameHost_HandleReturnCommand(string sender)
        {
            BroadcastMessage(RETURN_COMMAND + ProgramConstants.LAN_DATA_SEPARATOR + sender);
        }

        private void Player_HandleReturnCommand(string sender)
        {
            ReturnNotification(sender);
        }

        private void HandleGetReadyCommand()
        {
            if (!IsHost)
                GetReadyNotification();
        }

        private void HandleHostQuit()
        {
            if (!IsHost && !leaving)
                LeaveGame();
        }

        private void HandlePlayerOptionsRequest(string sender, string data)
        {
            if (!IsHost)
                return;

            PlayerInfo pInfo = Players.Find(p => p.Name == sender);

            if (pInfo == null)
                return;

            if (!PlayerOptionsRequestMessage.TryDecode(data, out PlayerOptionsRequestMessage request))
                return;

            int side = request.Options.Side;
            int color = request.Options.Color;
            int start = request.Options.Start;
            int team = request.Options.Team;

            if (side < 0 || side > SideCount + RandomSelectorCount)
                return;

            if (color < 0 || color > MPColors.Count)
                return;

            var disallowedSides = GetDisallowedSides();

            if (side > 0 && side <= SideCount && disallowedSides[side - 1])
                return;

            if (GameModeMap.CoopInfo != null)
            {
                if (GameModeMap.CoopInfo.DisallowedPlayerSides.Contains(side - 1) || side == SideCount + RandomSelectorCount)
                    return;

                if (GameModeMap.CoopInfo.DisallowedPlayerColors.Contains(color - 1))
                    return;
            }

            if (!(start == 0 || (GameModeMap?.AllowedStartingLocations?.Contains(start) ?? true)))
                return;

            if (team < 0 || team > 4)
                return;

            if (side != pInfo.SideId
                || start != pInfo.StartingLocation
                || team != pInfo.TeamId)
            {
                ClearReadyStatuses();
            }

            pInfo.SideId = side;
            pInfo.ColorId = color;
            pInfo.StartingLocation = start;
            pInfo.TeamId = team;

            CopyPlayerDataToUI();
            BroadcastPlayerOptions();
        }

        private void HandlePlayerExtraOptionsBroadcast(string data) => ApplyPlayerExtraOptions(null, data);

        private void HandlePlayerOptionsBroadcast(string data)
        {
            if (IsHost)
                return;

            if (!PlayerOptionsMessage.TryDecode(data, out PlayerOptionsMessage playerOptions))
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

            PlayerInfo localPlayer = FindLocalPlayer();
            int oldSideId = localPlayer == null ? -1 : localPlayer.SideId;

            Players.Clear();
            AIPlayers.Clear();

            foreach (PlayerOptionsEntry entry in playerOptions.Players)
            {
                string ipAddress = entry.Address;

                if (ipAddress == "127.0.0.1")
                    ipAddress = hostEndPoint.Address.ToString();

                PlayerInfo pInfo;

                if (!entry.IsAI)
                {
                    pInfo = new LANPlayerInfo(encoding);
                    pInfo.Name = entry.Name;
                    Players.Add(pInfo);
                }
                else
                {
                    pInfo = new PlayerInfo();
                    pInfo.Name = AILevelToName(entry.AILevel);
                    pInfo.IsAI = true;
                    pInfo.AILevel = entry.AILevel;
                    AIPlayers.Add(pInfo);
                }

                pInfo.SideId = entry.Options.Side;
                pInfo.ColorId = entry.Options.Color;
                pInfo.StartingLocation = entry.Options.Start;
                pInfo.TeamId = entry.Options.Team;
                pInfo.Ready = entry.ReadyState > 0;
                pInfo.AutoReady = entry.ReadyState > 1;
                pInfo.IPAddress = ipAddress;
            }

            CopyPlayerDataToUI();

            localPlayer = FindLocalPlayer();
            if (localPlayer != null && oldSideId != localPlayer.SideId)
                UpdateDiscordPresence();
        }

        private void HandlePlayerQuit(string sender)
        {
            PlayerInfo pInfo = Players.Find(p => p.Name == sender);

            if (pInfo == null)
                return;

            AddNotice(string.Format("{0} has left the game.".L10N("Client:Main:PlayerLeftGame"), pInfo.Name));
            Players.Remove(pInfo);
            ClearReadyStatuses();
            CopyPlayerDataToUI();
            BroadcastPlayerOptions();
            UpdateDiscordPresence();
        }

        private void HandleGameOptionsMessage(string data)
        {
            if (IsHost)
                return;

            if (!GameOptionsMessage.TryDecode(data, CheckBoxes.Count, DropDowns.Count, out GameOptionsMessage gameOptions))
            {
                AddNotice(("The game host has sent an invalid game options message! " +
                    "The game host's game version might be different from yours.").L10N("Client:Main:HostGameOptionInvalid"));
                Logger.Log("Invalid game options message from host: " + data);
                return;
            }

            GameOptionsUpdate update = GameOptionsApplier.Plan(gameOptions,
                new LobbyGameSettings(FrameSendRate, MaxAhead, ProtocolVersion, GameModeMap),
                (gameMode, mapSHA1) => GameModeMaps.FirstOrDefault(gmm => gmm.GameMode.Name == gameMode && gmm.Map.SHA1 == mapSHA1),
                isMapSharingEnabled: false);

            ApplyGameOptionsUpdate(update);
        }

        private void GameHost_HandleReadyRequest(string sender, string message)
        {
            PlayerInfo pInfo = Players.Find(p => p.Name == sender);

            if (pInfo == null)
                return;

            if (!ReadyRequestMessage.TryDecode(message, out ReadyRequestMessage request))
                return;

            pInfo.Ready = request.ReadyState > 0;
            pInfo.AutoReady = request.ReadyState > 1;
            CopyPlayerDataToUI();
            BroadcastPlayerOptions();
        }

        private void HandleGameLaunchCommand(string message)
        {
            Players.ForEach(pInfo => pInfo.IsInGame = true);

            if (!LaunchMessage.TryDecode(message, out LaunchMessage launch))
                return;

            UniqueGameID = launch.GameId;

            CopyPlayerDataToUI();
            StartGame();
        }

        private void HandlePing()
        {
            SendMessageToHost(PING);
        }

        protected override void BroadcastDiceRoll(int dieSides, int[] results)
        {
            string resultString = string.Join(",", results);
            SendMessageToHost($"DR {dieSides},{resultString}");
        }

        private void Host_HandleDiceRoll(string sender, string result)
        {
            BroadcastMessage($"{DICE_ROLL_COMMAND} {sender}{ProgramConstants.LAN_DATA_SEPARATOR}{result}");
        }

        private void Client_HandleDiceRoll(string data)
        {
            string[] parts = data.Split(ProgramConstants.LAN_DATA_SEPARATOR);
            if (parts.Length != 2)
                return;

            HandleDiceRollResult(parts[0], parts[1]);
        }

        #endregion

        protected override void AddLaunchCaptureInputs(IDictionary<string, object> inputs)
        {
            base.AddLaunchCaptureInputs(inputs);
            inputs["UniqueGameID"] = UniqueGameID;
            inputs["Port"] = ProgramConstants.LAN_INGAME_PORT;
        }

        protected override void WriteSpawnIniAdditions(IniFile iniFile)
        {
            base.WriteSpawnIniAdditions(iniFile);

            iniFile.SetIntValue("Settings", "Port", ProgramConstants.LAN_INGAME_PORT);
            iniFile.SetIntValue("Settings", "GameID", UniqueGameID);
            iniFile.SetBooleanValue("Settings", "Host", IsHost);
        }
    }

    public class LobbyNotificationEventArgs : EventArgs
    {
        public LobbyNotificationEventArgs(string notification)
        {
            Notification = notification;
        }

        public string Notification { get; private set; }
    }

    public class GameBroadcastEventArgs : EventArgs
    {
        public GameBroadcastEventArgs(string message)
        {
            Message = message;
        }

        public string Message { get; private set; }
    }

}
