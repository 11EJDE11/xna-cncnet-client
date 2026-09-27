using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Text;

using ClientCore;
using ClientCore.Extensions;

using ClientLogic.Launch;
using ClientLogic.Lobby;
using ClientLogic.Protocol;
using ClientLogic.UI;

using DTAClient.Domain.Multiplayer;
using DTAClient.Domain.Multiplayer.LAN;
using DTAClient.Online;

using Rampastring.Tools;

namespace ClientLogic.Lan;

/// <summary>
/// A LAN game room without a user interface: the XNA client's LAN game lobby protocol over
/// <see cref="LanGameConnection"/>. The host relays the players' messages and owns the player list; players apply
/// what the host sends. The front end calls <see cref="Update"/> regularly for time-outs and game broadcasts.
/// Map sharing is not supported yet.
/// </summary>
public sealed class LanGameRoom : MultiplayerLobbySession
{
    private const double DROPOUT_TIMEOUT = 20.0;
    private const double GAME_BROADCAST_INTERVAL = 2.0;

    private const string CHAT_COMMAND = LanLobbySession.CHAT_COMMAND;
    private const string RETURN_COMMAND = "RETURN";
    private const string GET_READY_COMMAND = "GETREADY";
    private const string PLAYER_OPTIONS_REQUEST_COMMAND = LanLobbySession.PLAYER_OPTIONS_REQUEST_COMMAND;
    private const string PLAYER_OPTIONS_BROADCAST_COMMAND = LanLobbySession.PLAYER_OPTIONS_BROADCAST_COMMAND;
    private const string PLAYER_QUIT_COMMAND = "QUIT";
    private const string GAME_OPTIONS_COMMAND = LanLobbySession.GAME_OPTIONS_COMMAND;
    private const string PLAYER_READY_REQUEST = LanLobbySession.PLAYER_READY_REQUEST;
    private const string LAUNCH_GAME_COMMAND = "LAUNCH";
    private const string FILE_HASH_COMMAND = "FHASH";
    private const string DICE_ROLL_COMMAND = LanLobbySession.DICE_ROLL_COMMAND;
    public const string PING = "PING";

    /// <summary>The window section the XNA LAN lobby reads its game options from.</summary>
    public const string WINDOW_NAME = "MultiplayerGameLobby";

    /// <summary>The layout INI the XNA LAN lobby reads (its IniNameOverride).</summary>
    public const string LAYOUT_INI_NAME = "LANGameLobby";

    private readonly LanGameConnection connection;
    private readonly string localGame;
    private readonly List<(string Command, bool HasParameter, Action<string, string> Handler)> hostCommandHandlers;
    private readonly LANClientCommandHandler[] playerCommandHandlers;

    private TimeSpan timeSinceGameBroadcast = TimeSpan.Zero;
    private TimeSpan timeSinceLastReceivedCommand = TimeSpan.Zero;
    private string localFileHash;
    private bool active;

    public LanGameRoom(MapLoader mapLoader, GameProcessService gameProcess, IDialogService dialogs,
        ISoundService sounds, IUiDispatcher uiDispatcher, Random random)
        : base(WINDOW_NAME, mapLoader, gameProcess, dialogs, sounds, uiDispatcher, random, LAYOUT_INI_NAME)
    {
        localGame = ClientConfiguration.Instance.LocalGame;

        connection = new LanGameConnection(uiDispatcher)
        {
            CanAcceptPlayer = () => Players.Count < MAX_PLAYER_COUNT && !Locked,
        };
        connection.PlayerJoined += (_, lpInfo) => AddPlayer(lpInfo);
        connection.PlayerMessage += (_, e) => HandleClientMessage(e.Message, e.Player);
        connection.PlayerConnectionLost += (_, lpInfo) => HandleConnectionLost(lpInfo);
        connection.HostMessage += (_, message) => HandleMessageFromServer(message);
        connection.HostConnectionFailed += (_, message) => LeaveGame(message);

        Session = new LanLobbySession(connection.SendToHost, BroadcastMessage, () => ChatColorIndex);

        hostCommandHandlers =
        [
            (CHAT_COMMAND, true, GameHost_HandleChatCommand),
            (RETURN_COMMAND, false, (sender, _) => GameHost_HandleReturnCommand(sender)),
            (PLAYER_OPTIONS_REQUEST_COMMAND, true, HandlePlayerOptionsRequest),
            (PLAYER_QUIT_COMMAND, false, (sender, _) => HandlePlayerQuit(sender)),
            (PLAYER_READY_REQUEST, true, GameHost_HandleReadyRequest),
            (FILE_HASH_COMMAND, true, HandleFileHashCommand),
            (DICE_ROLL_COMMAND, true, Host_HandleDiceRoll),
            (PING, false, (_, _) => { }),
        ];

        playerCommandHandlers =
        [
            new ClientStringCommandHandler(CHAT_COMMAND, Player_HandleChatCommand),
            new ClientNoParamCommandHandler(GET_READY_COMMAND, HandleGetReadyCommand),
            new ClientNoParamCommandHandler(PLAYER_QUIT_COMMAND, HandleHostQuit),
            new ClientStringCommandHandler(RETURN_COMMAND, ReturnNotification),
            new ClientStringCommandHandler(PLAYER_OPTIONS_BROADCAST_COMMAND, HandlePlayerOptionsBroadcast),
            new ClientStringCommandHandler(PlayerExtraOptions.LAN_MESSAGE_KEY, ApplyPlayerExtraOptions),
            new ClientStringCommandHandler(LAUNCH_GAME_COMMAND, HandleGameLaunchCommand),
            new ClientStringCommandHandler(GAME_OPTIONS_COMMAND, HandleGameOptionsMessage),
            new ClientStringCommandHandler(DICE_ROLL_COMMAND, Client_HandleDiceRoll),
            new ClientNoParamCommandHandler(PING, HandlePing),
        ];
    }

    protected override string LobbyTypeName => "LANGameLobby";

    /// <summary>The local player's chat colour (an index into <see cref="LanChatColors.All"/>).</summary>
    public int ChatColorIndex { get; set; }

    /// <summary>
    /// Host: a game announcement to send to the LAN lobby (the "GAME ..." message, or "GAMECLOSED").
    /// </summary>
    public event EventHandler<string> GameBroadcast;

    /// <summary>A notice for the LAN lobby's chat (the host connection timed out).</summary>
    public event EventHandler<string> LobbyNotification;

    /// <summary>
    /// Opens the room: the host starts listening; a player uses its connection to the host.
    /// </summary>
    /// <returns>False if the host couldn't start listening (an error was shown).</returns>
    public bool SetUp(bool isHost, IPEndPoint hostEndPoint, TcpClient client)
    {
        if (isHost)
        {
            string error = connection.StartHosting();
            if (error != null)
            {
                Dialogs.ShowMessage("Error".L10N("Client:Main:Error"), error);
                return false;
            }
        }

        timeSinceGameBroadcast = TimeSpan.Zero;
        timeSinceLastReceivedCommand = TimeSpan.Zero;

        active = true;
        SetUp(isHost);

        if (isHost)
            RandomSeed = Random.Next();

        connection.Start(isHost, hostEndPoint, client);

        if (isHost)
        {
            var fhc = new FileHashCalculator();
            fhc.CalculateHashes();
            localFileHash = fhc.GetCompleteHash();
            NormalisePlayers();
            RaiseChanged();
        }

        return true;
    }

    /// <summary>Player: sends the local file hash after joining.</summary>
    public void PostJoin()
    {
        var fhc = new FileHashCalculator();
        fhc.CalculateHashes();
        connection.SendToHost(FILE_HASH_COMMAND + " " + fhc.GetCompleteHash());
        AutoReady = false;
        RaiseChanged();
    }

    #region Host

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

        AddNotice(string.Format("{0} connected from {1}".L10N("Client:Main:PlayerFromIP"), lpInfo.Name, lpInfo.IPAddress));
        connection.StartReceiving(lpInfo);

        OnGameOptionChanged();
        NormalisePlayers();
        RaiseChanged();
        BroadcastPlayerOptions();
        BroadcastPlayerExtraOptions();
    }

    private void HandleConnectionLost(LANPlayerInfo lpInfo)
    {
        connection.CleanUpPlayer(lpInfo);
        Players.Remove(lpInfo);

        AddNotice(string.Format("{0} has left the game.".L10N("Client:Main:PlayerLeftGame"), lpInfo.Name));

        NormalisePlayers();
        RaiseChanged();
        BroadcastPlayerOptions();
    }

    private void HandleClientMessage(string data, LANPlayerInfo lpInfo)
    {
        foreach ((string command, bool hasParameter, Action<string, string> handler) in hostCommandHandlers)
        {
            // The XNA lobby's StringCommandHandler / NoParamCommandHandler rules
            if (hasParameter)
            {
                if (data.Length < command.Length + 1 || !data.StartsWith(command))
                    continue;

                handler(lpInfo.Name, data.Substring(command.Length + 1));
                return;
            }

            if (data == command)
            {
                handler(lpInfo.Name, null);
                return;
            }
        }

        Logger.Log("Unknown LAN command from " + lpInfo + " : " + data);
    }

    /// <summary>Host: sends a message to every player (or all but itself).</summary>
    private void BroadcastMessage(string message, bool otherPlayersOnly = false)
    {
        if (IsHost)
            LanGameConnection.Broadcast(Players, message, otherPlayersOnly);
    }

    private void GameHost_HandleChatCommand(string sender, string data)
    {
        string[] parts = data.Split(ProgramConstants.LAN_DATA_SEPARATOR);

        if (parts.Length < 2)
            return;

        int colorIndex = Conversions.IntFromString(parts[0], -1);

        if (!LanChatColors.IsValidIndex(colorIndex))
            return;

        BroadcastMessage(CHAT_COMMAND + " " + sender + ProgramConstants.LAN_DATA_SEPARATOR + data);
    }

    private void GameHost_HandleReturnCommand(string sender) =>
        BroadcastMessage(RETURN_COMMAND + ProgramConstants.LAN_DATA_SEPARATOR + sender);

    private void HandlePlayerOptionsRequest(string sender, string data)
    {
        if (PlayerOptionsRequestMessage.TryDecode(data, out PlayerOptionsRequestMessage request))
            HandlePlayerOptionsRequest(sender, request.Options);
    }

    private void HandlePlayerQuit(string sender)
    {
        PlayerInfo pInfo = Players.Find(p => p.Name == sender);

        if (pInfo == null)
            return;

        AddNotice(string.Format("{0} has left the game.".L10N("Client:Main:PlayerLeftGame"), pInfo.Name));
        Players.Remove(pInfo);
        LobbyState.ClearReadyStatuses();
        NormalisePlayers();
        RaiseChanged();
        BroadcastPlayerOptions();
    }

    private void GameHost_HandleReadyRequest(string sender, string message)
    {
        if (ReadyRequestMessage.TryDecode(message, out ReadyRequestMessage request))
            HandleReadyRequest(sender, request.ReadyState);
    }

    private void HandleFileHashCommand(string sender, string fileHash)
    {
        if (fileHash != localFileHash)
            AddNotice(string.Format("{0} has modified game files! They could be cheating!".L10N("Client:Main:PlayerModifiedFiles"), sender));

        PlayerInfo pInfo = Players.Find(p => p.Name == sender);
        if (pInfo == null)
            return;

        pInfo.HashReceived = true;
        RaiseChanged();
    }

    private void Host_HandleDiceRoll(string sender, string result) =>
        BroadcastMessage($"{DICE_ROLL_COMMAND} {sender}{ProgramConstants.LAN_DATA_SEPARATOR}{result}");

    public override void BroadcastPlayerOptions()
    {
        if (!IsHost)
            return;

        var entries = Players.Concat(AIPlayers).Select(pInfo => new PlayerOptionsEntry(
            pInfo.IsAI ? string.Empty : pInfo.Name,
            pInfo.IsAI ? pInfo.AILevel : -1,
            new PackedPlayerOptions(pInfo.SideId, pInfo.ColorId, pInfo.StartingLocation, pInfo.TeamId),
            pInfo.AutoReady && !pInfo.IsInGame && !LastMapChangeWasInvalid ? 2 : Convert.ToInt32(pInfo.IsAI || pInfo.Ready),
            pInfo.IPAddress)).ToList();

        Session.SendPlayerOptions(new PlayerOptionsMessage(entries));
    }

    protected override void HostLaunchGame() =>
        BroadcastMessage(LAUNCH_GAME_COMMAND + " " + new LaunchMessage(UniqueGameID).Encode());

    public override void GetReadyNotification()
    {
        base.GetReadyNotification();

        if (IsHost)
            BroadcastMessage(GET_READY_COMMAND);
    }

    /// <summary>Host: locks or unlocks the room.</summary>
    public void ToggleLock()
    {
        if (!IsHost)
            return;

        if (Locked)
            UnlockGame(true);
        else
            LockGame();
    }

    private void LockGame()
    {
        LobbyState.Locked = true;
        AddNotice("You've locked the game room.".L10N("Client:Main:RoomLockedByYou"));
        RaiseChanged();
    }

    private void UnlockGame(bool manual)
    {
        LobbyState.Locked = false;

        if (manual)
            AddNotice("You've unlocked the game room.".L10N("Client:Main:RoomUnlockedByYou"));

        RaiseChanged();
    }

    private void BroadcastGame()
    {
        var sb = new ExtendedStringBuilder("GAME ", true);
        sb.Separator = ProgramConstants.LAN_DATA_SEPARATOR;
        sb.Append(ProgramConstants.LAN_PROTOCOL_REVISION);
        sb.Append(ProgramConstants.GAME_VERSION);
        sb.Append(localGame);
        sb.Append(GameModeMap?.Map?.UntranslatedName ?? string.Empty);
        sb.Append(GameModeMap?.GameMode?.UntranslatedUIName ?? string.Empty);
        sb.Append(0); // LoadedGameID
        var sbPlayers = new StringBuilder();
        Players.ForEach(p => sbPlayers.Append(p.Name + ","));
        sbPlayers.Remove(sbPlayers.Length - 1, 1);
        sb.Append(sbPlayers.ToString());
        sb.Append(Convert.ToInt32(Locked));
        sb.Append(0); // IsLoadedGame
        sb.Append(GameModeMap?.Map?.SHA1);

        GameBroadcast?.Invoke(this, sb.ToString());
    }

    #endregion

    #region Player

    private void HandleMessageFromServer(string message)
    {
        timeSinceLastReceivedCommand = TimeSpan.Zero;

        foreach (LANClientCommandHandler cmdHandler in playerCommandHandlers)
        {
            if (cmdHandler.Handle(message))
                return;
        }

        Logger.Log("Unknown LAN command from the server: " + message);
    }

    private void Player_HandleChatCommand(string data)
    {
        string[] parts = data.Split(ProgramConstants.LAN_DATA_SEPARATOR);

        if (parts.Length < 3)
            return;

        int colorIndex = Conversions.IntFromString(parts[1], -1);

        if (!LanChatColors.IsValidIndex(colorIndex))
            return;

        AddChatMessage(new ChatMessage(parts[0], LanChatColors.All[colorIndex].Color, DateTime.Now, parts[2]));
    }

    private void HandleGetReadyCommand()
    {
        if (!IsHost)
            GetReadyNotification();
    }

    private void HandleHostQuit()
    {
        if (!IsHost && !connection.IsLeaving)
            LeaveGame();
    }

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

            if (options.Side > Sides.Count + SlotIndices.RandomSelectorCount)
                return;

            if (options.Color > MPColors.Count)
                return;

            if (options.Start > MAX_PLAYER_COUNT)
                return;

            if (options.Team > 4)
                return;
        }

        Players.Clear();
        AIPlayers.Clear();

        foreach (PlayerOptionsEntry entry in playerOptions.Players)
        {
            string ipAddress = entry.Address;

            if (ipAddress == "127.0.0.1")
                ipAddress = connection.HostEndPoint.Address.ToString();

            PlayerInfo pInfo;

            if (!entry.IsAI)
            {
                pInfo = new LANPlayerInfo(connection.Encoding);
                pInfo.Name = entry.Name;
                Players.Add(pInfo);
            }
            else
            {
                pInfo = new PlayerInfo();
                pInfo.Name = ProgramConstants.GetAILevelName(entry.AILevel);
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

        NormalisePlayers();
        RaiseChanged();
    }

    private void HandleGameOptionsMessage(string data)
    {
        if (IsHost)
            return;

        if (!GameOptionsMessage.TryDecode(data, Options.CheckBoxes.Count, Options.DropDowns.Count, out GameOptionsMessage gameOptions))
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

    private void HandleGameLaunchCommand(string message)
    {
        Players.ForEach(pInfo => pInfo.IsInGame = true);

        if (!LaunchMessage.TryDecode(message, out LaunchMessage launch))
            return;

        UniqueGameID = launch.GameId;

        NormalisePlayers();
        RaiseChanged();
        StartMultiplayerGame();
    }

    private void HandlePing() => connection.SendToHost(PING);

    private void Client_HandleDiceRoll(string data)
    {
        string[] parts = data.Split(ProgramConstants.LAN_DATA_SEPARATOR);
        if (parts.Length != 2)
            return;

        HandleDiceRollResult(parts[0], parts[1]);
    }

    public override void RequestReady() => Session.RequestReady(AutoReady ? 2 : 1);

    #endregion

    /// <summary>
    /// Called regularly by the front end: the host drops players that timed out and announces the game; a player
    /// leaves if the host has gone quiet.
    /// </summary>
    public void Update(TimeSpan elapsed)
    {
        if (!active)
            return;

        if (IsHost)
        {
            for (int i = 1; i < Players.Count; i++)
            {
                var lpInfo = (LANPlayerInfo)Players[i];
                if (!lpInfo.Update(elapsed))
                {
                    connection.CleanUpPlayer(lpInfo);
                    Players.RemoveAt(i);
                    AddNotice(string.Format("{0} - connection timed out".L10N("Client:Main:PlayerTimeout"), lpInfo.Name));
                    NormalisePlayers();
                    RaiseChanged();
                    BroadcastPlayerOptions();
                    BroadcastPlayerExtraOptions();
                    i--;
                }
            }

            timeSinceGameBroadcast += elapsed;

            if (timeSinceGameBroadcast > TimeSpan.FromSeconds(GAME_BROADCAST_INTERVAL))
            {
                BroadcastGame();
                timeSinceGameBroadcast = TimeSpan.Zero;
            }
        }
        else
        {
            timeSinceLastReceivedCommand += elapsed;

            if (timeSinceLastReceivedCommand > TimeSpan.FromSeconds(DROPOUT_TIMEOUT))
            {
                string localizedMessage = string.Format(
                    "Connection to the game host timed out. Server address: {0}".L10N("Client:Main:HostConnectTimeOutWithAddress"),
                    connection.HostEndPoint.Address.ToString());

                LobbyNotification?.Invoke(this, localizedMessage);
                LeaveGame(localizedMessage);
            }
        }
    }

    /// <summary>The extra player options changed on the host's side.</summary>
    public void OnPlayerExtraOptionsChanged() => BroadcastPlayerExtraOptions();

    public override void Leave() => LeaveGame();

    private void LeaveGame(string message = null)
    {
        if (connection.IsLeaving)
            return;

        Clear();
        RaiseLeft(message);
    }

    /// <summary>Leaves the room without telling the front end (the client is closing).</summary>
    public void Clear()
    {
        if (IsHost)
        {
            GameBroadcast?.Invoke(this, "GAMECLOSED");
            BroadcastMessage(PLAYER_QUIT_COMMAND);
            Players.ForEach(p => connection.CleanUpPlayer((LANPlayerInfo)p));
        }
        else
        {
            connection.SendToHost(PLAYER_QUIT_COMMAND);
        }

        if (!IsHost)
            AIPlayers.Clear();

        Players.Clear();
        active = false;
        connection.Stop();
        RaiseChanged();
    }

    protected override void HandleGameProcessExited()
    {
        base.HandleGameProcessExited();

        connection.SendToHost(RETURN_COMMAND);

        if (IsHost)
        {
            RandomSeed = Random.Next();
            OnGameOptionChanged();
            LobbyState.ClearReadyStatuses();
            NormalisePlayers();
            BroadcastPlayerOptions();
            BroadcastPlayerExtraOptions();

            if (Players.Count < MAX_PLAYER_COUNT)
                UnlockGame(true);
        }

        RaiseChanged();
    }

    protected override string GetIPAddressForPlayer(PlayerInfo player) => player.IPAddress;

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
