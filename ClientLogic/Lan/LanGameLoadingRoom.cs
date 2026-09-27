using System;
using System.Net;
using System.Net.Sockets;
using System.Text;

using ClientCore;
using ClientCore.Extensions;

using ClientLogic.Launch;
using ClientLogic.Lobby;
using ClientLogic.UI;

using DTAClient.Domain.Multiplayer;
using DTAClient.Domain.Multiplayer.LAN;
using DTAClient.Online;

using Rampastring.Tools;

namespace ClientLogic.Lan;

/// <summary>
/// A LAN room for loading a saved multiplayer game, as the XNA LANGameLoadingLobby: the host listens for the saved
/// game's players (their JOIN carries the saved game's ID), sends the ready statuses and the chosen save (OPTS), and
/// starts the game (START). The front end calls <see cref="Update"/> regularly.
/// </summary>
public sealed class LanGameLoadingRoom : GameLoadingSession
{
    private const double DROPOUT_TIMEOUT = 20.0;
    private const double GAME_BROADCAST_INTERVAL = 2.0;

    private const string OPTIONS_COMMAND = "OPTS";
    private const string GAME_LAUNCH_COMMAND = "START";
    private const string READY_STATUS_COMMAND = "READY";
    private const string CHAT_COMMAND = "CHAT";
    private const string PLAYER_QUIT_COMMAND = "QUIT";
    private const string FILE_HASH_COMMAND = "FHASH";

    private readonly LanGameConnection connection;
    private readonly (string Command, bool HasParameter, Action<LANPlayerInfo, string> Handler)[] hostCommandHandlers;
    private readonly LANClientCommandHandler[] playerCommandHandlers;
    private readonly string localGame;

    private TimeSpan timeSinceGameBroadcast = TimeSpan.Zero;
    private TimeSpan timeSinceLastReceivedCommand = TimeSpan.Zero;
    private string localFileHash;
    private int loadedGameId;
    private bool started;
    private bool active;

    public LanGameLoadingRoom(GameProcessService gameProcess, IDialogService dialogs, ISoundService sounds, IUiDispatcher uiDispatcher)
        : base(gameProcess, dialogs, sounds, uiDispatcher)
    {
        localGame = ClientConfiguration.Instance.LocalGame;

        connection = new LanGameConnection(uiDispatcher);
        connection.PlayerJoined += (_, lpInfo) => AddPlayer(lpInfo);
        connection.PlayerMessage += (_, e) => HandleClientMessage(e.Message, e.Player);
        connection.PlayerConnectionLost += (_, lpInfo) => HandleConnectionLost(lpInfo);
        connection.HostMessage += (_, message) => HandleMessageFromServer(message);
        connection.HostConnectionFailed += (_, _) => Leave();

        hostCommandHandlers =
        [
            (CHAT_COMMAND, true, Server_HandleChatMessage),
            (FILE_HASH_COMMAND, true, Server_HandleFileHashMessage),
            (READY_STATUS_COMMAND, false, (sender, _) => Server_HandleReadyRequest(sender)),
        ];

        playerCommandHandlers =
        [
            new ClientStringCommandHandler(CHAT_COMMAND, Client_HandleChatMessage),
            new ClientStringCommandHandler(OPTIONS_COMMAND, Client_HandleOptionsMessage),
            new ClientNoParamCommandHandler(GAME_LAUNCH_COMMAND, Client_HandleStartCommand),
            new ClientNoParamCommandHandler(PLAYER_QUIT_COMMAND, HandleHostQuit),
        ];
    }

    /// <summary>The local player's LAN chat colour.</summary>
    public int ChatColorIndex { get; set; }

    /// <summary>A game broadcast for the LAN lobby to send.</summary>
    public event EventHandler<string> GameBroadcast;

    /// <summary>A message for the LAN lobby's chat (the host timed out).</summary>
    public event EventHandler<string> LobbyNotification;

    public bool IsActive => active;

    /// <summary>
    /// Opens the room: the host starts listening; a player uses its connection to the host (SetUp).
    /// </summary>
    /// <returns>False if the host couldn't start listening (an error was shown).</returns>
    public bool SetUp(bool isHost, IPEndPoint hostEndPoint, TcpClient client, int loadedGameId)
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

        Refresh(isHost);

        this.loadedGameId = loadedGameId;
        started = false;
        active = true;
        timeSinceGameBroadcast = TimeSpan.Zero;
        timeSinceLastReceivedCommand = TimeSpan.Zero;

        connection.JoinGameId = loadedGameId.ToString();
        connection.Start(isHost, hostEndPoint, client);

        if (isHost)
        {
            var fhc = new FileHashCalculator();
            fhc.CalculateHashes();
            localFileHash = fhc.GetCompleteHash();
        }

        RaiseChanged();
        return true;
    }

    /// <summary>A player joined: sends the file hash (PostJoin).</summary>
    public void PostJoin()
    {
        var fhc = new FileHashCalculator();
        fhc.CalculateHashes();
        connection.SendToHost(FILE_HASH_COMMAND + " " + fhc.GetCompleteHash());
        UpdateDiscordPresence(true);
    }

    #region Server code

    private void AddPlayer(LANPlayerInfo lpInfo)
    {
        if (Players.Find(p => p.Name == lpInfo.Name) != null ||
            Players.Count >= SGPlayers.Count ||
            SGPlayers.Find(p => p.Name == lpInfo.Name) == null)
        {
            lpInfo.TcpClient.Close();
            return;
        }

        if (Players.Count == 0)
            lpInfo.Ready = true;

        Players.Add(lpInfo);

        Sounds.Play(LobbySound.PlayerJoined);

        AddNotice(string.Format("{0} connected from {1}".L10N("Client:Main:PlayerFromIP"), lpInfo.Name, lpInfo.IPAddress));
        connection.StartReceiving(lpInfo);

        RaiseChanged();
        BroadcastOptions();
        UpdateDiscordPresence();
    }

    private void HandleConnectionLost(LANPlayerInfo lpInfo)
    {
        connection.CleanUpPlayer(lpInfo);
        Players.Remove(lpInfo);

        AddNotice(string.Format("{0} has left the game.".L10N("Client:Main:PlayerLeftGame"), lpInfo.Name));

        Sounds.Play(LobbySound.PlayerLeft);

        RaiseChanged();
        BroadcastOptions();
        UpdateDiscordPresence();
    }

    private void HandleClientMessage(string data, LANPlayerInfo lpInfo)
    {
        foreach ((string command, bool hasParameter, Action<LANPlayerInfo, string> handler) in hostCommandHandlers)
        {
            if (hasParameter)
            {
                if (data.Length < command.Length + 1 || !data.StartsWith(command))
                    continue;

                handler(lpInfo, data.Substring(command.Length + 1));
                return;
            }

            if (data == command)
            {
                handler(lpInfo, null);
                return;
            }
        }

        Logger.Log("Unknown LAN command from " + lpInfo + " : " + data);
    }

    #endregion

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

    private void HandleHostQuit()
    {
        if (!IsHost && !connection.IsLeaving)
            Leave();
    }

    public override void Leave()
    {
        if (!active)
            return;

        Clear();
        base.Leave();
    }

    /// <summary>Leaves the room without telling the front end (the client is closing).</summary>
    public void Clear()
    {
        if (!active)
            return;

        if (IsHost)
        {
            GameBroadcast?.Invoke(this, "GAMECLOSED");
            BroadcastMessage(PLAYER_QUIT_COMMAND);
            Players.ForEach(p => connection.CleanUpPlayer((LANPlayerInfo)p));
            Players.Clear();
        }
        else
        {
            connection.SendToHost(PLAYER_QUIT_COMMAND);
        }

        active = false;
        connection.Stop();
        RaiseChanged();
    }

    protected override void BroadcastOptions()
    {
        if (Players.Count > 0)
            Players[0].Ready = true;

        var sb = new ExtendedStringBuilder(OPTIONS_COMMAND + " ", true);
        sb.Separator = ProgramConstants.LAN_DATA_SEPARATOR;

        sb.Append(SelectedSavedGameIndex);

        foreach (PlayerInfo pInfo in Players)
        {
            sb.Append(pInfo.Name);
            sb.Append(Convert.ToInt32(pInfo.Ready));
            sb.Append(pInfo.IPAddress);
        }

        BroadcastMessage(sb.ToString());
    }

    protected override void HostStartGame() => BroadcastMessage(GAME_LAUNCH_COMMAND);

    protected override void RequestReadyStatus() => connection.SendToHost(READY_STATUS_COMMAND);

    public override void SendChatMessage(string message)
    {
        if (string.IsNullOrEmpty(message))
            return;

        connection.SendToHost(CHAT_COMMAND + " " + ChatColorIndex + ProgramConstants.LAN_DATA_SEPARATOR + message);

        Sounds.Play(LobbySound.Message);
    }

    #region Server's command handlers

    private void Server_HandleChatMessage(LANPlayerInfo sender, string data)
    {
        string[] parts = data.Split(ProgramConstants.LAN_DATA_SEPARATOR);

        if (parts.Length < 2)
            return;

        int colorIndex = Conversions.IntFromString(parts[0], -1);

        if (!LanChatColors.IsValidIndex(colorIndex))
            return;

        // As the XNA lobby: the colour is sent twice (sender;colour;colour;message)
        BroadcastMessage(CHAT_COMMAND + " " + sender + ProgramConstants.LAN_DATA_SEPARATOR + colorIndex +
            ProgramConstants.LAN_DATA_SEPARATOR + data);
    }

    private void Server_HandleFileHashMessage(LANPlayerInfo sender, string hash)
    {
        if (hash != localFileHash)
            AddNotice(string.Format("{0} - modified files detected! They could be cheating!".L10N("Client:Main:PlayerCheating"), sender.Name), ChatColor.Red);

        sender.HashReceived = true;
    }

    private void Server_HandleReadyRequest(LANPlayerInfo sender)
    {
        if (!sender.Ready)
        {
            sender.Ready = true;
            RaiseChanged();
            BroadcastOptions();
        }
    }

    #endregion

    #region Client's command handlers

    private void Client_HandleChatMessage(string data)
    {
        string[] parts = data.Split(ProgramConstants.LAN_DATA_SEPARATOR);

        if (parts.Length < 3)
            return;

        string playerName = parts[0];

        int colorIndex = Conversions.IntFromString(parts[1], -1);

        if (!LanChatColors.IsValidIndex(colorIndex))
            return;

        // The host sends the colour twice (sender;colour;colour;message); the XNA lobby shows parts[2], the
        // second colour, instead of the message
        string message = parts.Length >= 4 ? parts[3] : parts[2];

        AddMessage(new ChatMessage(playerName, LanChatColors.All[colorIndex].Color, DateTime.Now, message));

        Sounds.Play(LobbySound.Message);
    }

    private void Client_HandleOptionsMessage(string data)
    {
        if (IsHost)
            return;

        string[] parts = data.Split(ProgramConstants.LAN_DATA_SEPARATOR);
        const int PLAYER_INFO_PARTS = 3;
        int pCount = (parts.Length - 1) / PLAYER_INFO_PARTS;

        if (pCount * PLAYER_INFO_PARTS + 1 != parts.Length)
            return;

        int savedGameIndex = Conversions.IntFromString(parts[0], -1);
        if (savedGameIndex < 0 || savedGameIndex >= SavedGames.Count)
            return;

        SetSavedGameIndex(savedGameIndex);

        Players.Clear();

        for (int i = 0; i < pCount; i++)
        {
            int baseIndex = 1 + i * PLAYER_INFO_PARTS;

            var pInfo = new LANPlayerInfo(connection.Encoding)
            {
                Name = parts[baseIndex],
                Ready = Conversions.IntFromString(parts[baseIndex + 1], -1) > 0,
                IPAddress = parts[baseIndex + 2],
            };
            Players.Add(pInfo);
        }

        if (Players.Count > 0) // Set IP of host
            Players[0].IPAddress = connection.HostEndPoint.Address.ToString();

        RaiseChanged();
    }

    private void Client_HandleStartCommand()
    {
        started = true;

        LoadGame();
    }

    #endregion

    /// <summary>Broadcasts a command to all players in the game as the game host.</summary>
    private void BroadcastMessage(string message)
    {
        if (!IsHost)
            return;

        LanGameConnection.Broadcast(Players, message);
    }

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
                    RaiseChanged();
                    BroadcastOptions();
                    UpdateDiscordPresence();
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
                LobbyNotification?.Invoke(this, "Connection to the game host timed out.".L10N("Client:Main:HostConnectTimeOut"));
                Leave();
            }
        }
    }

    private void BroadcastGame()
    {
        var sb = new ExtendedStringBuilder("GAME ", true);
        sb.Separator = ProgramConstants.LAN_DATA_SEPARATOR;
        sb.Append(ProgramConstants.LAN_PROTOCOL_REVISION);
        sb.Append(ProgramConstants.GAME_VERSION);
        sb.Append(localGame);
        sb.Append(MapName);
        sb.Append(GameMode);
        sb.Append(0); // LoadedGameID
        var sbPlayers = new StringBuilder();
        SGPlayers.ForEach(p => sbPlayers.Append(p.Name + ","));
        sbPlayers.Remove(sbPlayers.Length - 1, 1);
        sb.Append(sbPlayers.ToString());
        sb.Append(Convert.ToInt32(started || Players.Count == SGPlayers.Count));
        sb.Append(1); // IsLoadedGame
        sb.Append(string.Empty); // MapHash

        GameBroadcast?.Invoke(this, sb.ToString());
    }

    protected override void HandleGameProcessExited()
    {
        base.HandleGameProcessExited();

        Leave();
    }

    protected override void UpdateDiscordPresence(bool resetTimer = false)
    {
        if (DiscordHandler == null)
            return;

        PlayerInfo player = Players.Find(p => p.Name == ProgramConstants.PLAYERNAME);
        if (player == null)
            return;

        string currentState = ProgramConstants.IsInGame ? "In Game" : "In Lobby"; // not UI strings

        DiscordHandler.UpdatePresence(MapName, GameMode, currentState, "LAN",
            Players.Count, SGPlayers.Count, "LAN Game", IsHost, resetTimer);
    }
}
