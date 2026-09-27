using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Text;

using ClientCore;
using ClientCore.Extensions;

using ClientCore.Enums;

using ClientLogic.GameList;
using ClientLogic.UI;

using DTAClient.Domain.Multiplayer;
using DTAClient.Domain.LAN;
using DTAClient.Domain.Multiplayer.CnCNet;
using DTAClient.DXGUI.Multiplayer;
using DTAClient.Online;

using Rampastring.Tools;

namespace ClientLogic.Lan;

/// <summary>A player seen in the LAN lobby.</summary>
public sealed class LanLobbyPlayer(string name, int gameIndex, IPEndPoint endPoint)
{
    public string Name { get; } = name;

    /// <summary>The player's game in the game collection, or -1.</summary>
    public int GameIndex { get; } = gameIndex;

    public IPEndPoint EndPoint { get; } = endPoint;

    public TimeSpan TimeWithoutRefresh { get; set; }
}

/// <summary>
/// The LAN lobby without a user interface, as the XNA client's LAN lobby does it: UDP broadcasts (with message IDs to
/// drop duplicates) announce the players, chat and the hosted games; joining connects to the host over TCP. The
/// front end calls <see cref="Update"/> regularly. Events are raised on the UI thread.
/// </summary>
public sealed class LanLobby : IDisposable
{
    private const double ALIVE_MESSAGE_INTERVAL = 5.0;
    private const double INACTIVITY_REMOVE_TIME = 10.0;
    private const double MESSAGE_ID_EXPIRATION_SECONDS = 60.0;

    /// <summary>Seconds a game stays listed after its last announcement (shorter than on CnCNet).</summary>
    public const double GAME_LIFETIME = 15.0;

    private readonly GameCollection gameCollection;
    private readonly Encoding encoding = Encoding.UTF8;
    private readonly LANMessageDeduplicator messageDeduplicator;
    private readonly LANLobbyBroadcastManager broadcastManager;
    private readonly List<LanLobbyPlayer> players = [];
    private readonly List<HostedLANGame> games = [];
    private readonly string localGame;
    private readonly int localGameIndex;

    private TimeSpan timeSinceAliveMessage = TimeSpan.Zero;

    public LanLobby(GameCollection gameCollection, IUiDispatcher uiDispatcher, Random random)
    {
        this.gameCollection = gameCollection;

        localGame = ClientConfiguration.Instance.LocalGame;
        localGameIndex = gameCollection.GameList.FindIndex(g => g.InternalName.ToUpper() == localGame.ToUpper());

        messageDeduplicator = new LANMessageDeduplicator(random.Next(), MESSAGE_ID_EXPIRATION_SECONDS);
        broadcastManager = new LANLobbyBroadcastManager(ProgramConstants.LAN_LOBBY_PORT, encoding);
        broadcastManager.MessageReceived += (_, e) => uiDispatcher.Post(() => HandleNetworkMessage(e.Data, e.EndPoint));

        int selectedColor = UserINISettings.Instance.LANChatColor;
        ChatColorIndex = LanChatColors.IsValidIndex(selectedColor) ? selectedColor : 0;
    }

    /// <summary>A chat message or notice for the lobby's chat.</summary>
    public event EventHandler<ChatMessage> MessageAdded;

    /// <summary>The player list changed.</summary>
    public event EventHandler PlayersChanged;

    /// <summary>The game list changed.</summary>
    public event EventHandler GamesChanged;

    public IReadOnlyList<LanLobbyPlayer> Players => players;

    /// <summary>The hosted games in display order.</summary>
    public IEnumerable<HostedLANGame> Games => GameListState.Sort(games, localGame, ProgramConstants.GAME_VERSION,
        (SortDirection)UserINISettings.Instance.SortState.Value).Cast<HostedLANGame>();

    public GameCollection GameCollection => gameCollection;

    /// <summary>The local player's chat colour (an index into <see cref="LanChatColors.All"/>), saved in the settings.</summary>
    public int ChatColorIndex
    {
        get;
        set
        {
            if (!LanChatColors.IsValidIndex(value) || value == field)
                return;

            field = value;
            UserINISettings.Instance.LANChatColor.Value = value;
            UserINISettings.Instance.SaveSettings();
        }
    }

    /// <summary>Opens the lobby: clears the lists, opens the UDP socket and announces the local player.</summary>
    /// <returns>False if the socket couldn't be opened (a notice was added).</returns>
    public bool Open()
    {
        players.Clear();
        games.Clear();
        messageDeduplicator.Clear();
        PlayersChanged?.Invoke(this, EventArgs.Empty);
        GamesChanged?.Invoke(this, EventArgs.Empty);

        try
        {
            broadcastManager.Initialize();
        }
        catch (Exception ex)
        {
            AddMessage(new ChatMessage(ChatColor.Red,
                "Creating LAN socket failed! Message:".L10N("Client:Main:SocketFailure1") + " " + ex.Message + "\n" +
                "Please check your firewall settings.".L10N("Client:Main:SocketFailure2") + " " +
                "Also make sure that no other application is listening to traffic on UDP ports 1232 - 1234.".L10N("Client:Main:SocketFailure3")));

            return false;
        }

        SendAlive();
        return true;
    }

    /// <summary>Leaves the lobby: tells the others and closes the socket.</summary>
    public void Close()
    {
        SendMessage("QUIT");
        broadcastManager.Shutdown();
    }

    /// <summary>Sends a message to the LAN (a hosted game's announcements go through here too).</summary>
    public void SendMessage(string message)
    {
        string wrappedMessage = messageDeduplicator.WrapMessage(message);

        if (!broadcastManager.SendMessage(wrappedMessage))
        {
            AddMessage(new ChatMessage(ChatColor.Red,
                "Failed to send LAN broadcast message. The network socket may not be initialized."));
        }
    }

    public void SendChatMessage(string text)
    {
        if (string.IsNullOrEmpty(text))
            return;

        var sb = new StringBuilder("CHAT ");
        sb.Append(ChatColorIndex);
        sb.Append(ProgramConstants.LAN_DATA_SEPARATOR);
        sb.Append(text.Replace((char)01, '?'));

        SendMessage(sb.ToString());
    }

    public void AddMessage(ChatMessage message) => MessageAdded?.Invoke(this, message);

    private void HandleNetworkMessage(string data, IPEndPoint endPoint)
    {
        messageDeduplicator.UnwrapMessage(data, out string payload, out bool isDuplicate);

        if (isDuplicate || string.IsNullOrWhiteSpace(payload))
            return;

        string command = payload.Split(' ')[0];

        // For parameterless commands like "QUIT", locate the first space first
        int firstSpace = payload.IndexOf(' ');
        string[] parameters = firstSpace >= 0
            ? payload.Substring(firstSpace + 1).Split([ProgramConstants.LAN_DATA_SEPARATOR])
            : [];

        LanLobbyPlayer user = players.Find(p => p.EndPoint.Equals(endPoint));

        switch (command)
        {
            case "ALIVE":
                if (parameters.Length < 2)
                    return;

                if (user == null)
                {
                    user = new LanLobbyPlayer(parameters[1], Conversions.IntFromString(parameters[0], -1), endPoint);
                    players.Add(user);
                    PlayersChanged?.Invoke(this, EventArgs.Empty);
                }

                user.TimeWithoutRefresh = TimeSpan.Zero;
                break;

            case "CHAT":
                if (user == null || parameters.Length < 2)
                    return;

                int colorIndex = Conversions.IntFromString(parameters[0], -1);

                if (!LanChatColors.IsValidIndex(colorIndex))
                    return;

                AddMessage(new ChatMessage(user.Name, LanChatColors.All[colorIndex].Color, DateTime.Now, parameters[1]));
                break;

            case "QUIT":
                if (user == null)
                    return;

                players.Remove(user);
                PlayersChanged?.Invoke(this, EventArgs.Empty);
                break;

            case "GAMECLOSED":
                if (games.RemoveAll(g => g.EndPoint.Equals(endPoint)) > 0)
                    GamesChanged?.Invoke(this, EventArgs.Empty);

                break;

            case "GAME":
                if (user == null)
                    return;

                var game = new HostedLANGame();
                if (!game.SetDataFromStringArray(gameCollection, parameters))
                    return;

                game.EndPoint = endPoint;

                int existingGameIndex = games.FindIndex(g => g.EndPoint.Equals(endPoint));

                if (existingGameIndex > -1)
                    games[existingGameIndex] = game;
                else
                    games.Add(game);

                GamesChanged?.Invoke(this, EventArgs.Empty);
                break;
        }
    }

    private void SendAlive()
    {
        var sb = new StringBuilder("ALIVE ");
        sb.Append(localGameIndex);
        sb.Append(ProgramConstants.LAN_DATA_SEPARATOR);
        sb.Append(ProgramConstants.PLAYERNAME);
        SendMessage(sb.ToString());
        timeSinceAliveMessage = TimeSpan.Zero;
    }

    /// <summary>Called regularly by the front end: drops quiet players and old games, and announces the local player.</summary>
    public void Update(TimeSpan elapsed)
    {
        if (games.RemoveAll(g => DateTime.Now - g.LastRefreshTime > TimeSpan.FromSeconds(GAME_LIFETIME)) > 0)
            GamesChanged?.Invoke(this, EventArgs.Empty);

        foreach (LanLobbyPlayer player in players.ToList())
        {
            player.TimeWithoutRefresh += elapsed;

            if (player.TimeWithoutRefresh > TimeSpan.FromSeconds(INACTIVITY_REMOVE_TIME))
            {
                players.Remove(player);
                PlayersChanged?.Invoke(this, EventArgs.Empty);
            }
        }

        timeSinceAliveMessage += elapsed;
        if (timeSinceAliveMessage > TimeSpan.FromSeconds(ALIVE_MESSAGE_INTERVAL))
            SendAlive();
    }

    /// <summary>Opens a new game room as its host.</summary>
    /// <returns>False if the room couldn't be opened (an error was shown).</returns>
    public static bool HostGame(LanGameRoom room) =>
        room.SetUp(true, new IPEndPoint(IPAddress.Loopback, ProgramConstants.LAN_GAME_LOBBY_PORT), null);

    /// <summary>
    /// Whether the local player can host the saved multiplayer game (LANGameCreationWindow.AllowLoadingGame): they
    /// hosted it, and it wasn't a CnCNet game.
    /// </summary>
    public static bool CanHostLoadedGame()
    {
        FileInfo savedGameSpawnIniFile = SafePath.GetFile(ProgramConstants.GamePath, ProgramConstants.SAVED_GAME_SPAWN_INI);

        if (!savedGameSpawnIniFile.Exists)
            return false;

        var iniFile = new IniFile(savedGameSpawnIniFile.FullName);
        if (iniFile.GetStringValue("Settings", "Name", string.Empty) != ProgramConstants.PLAYERNAME)
            return false;

        if (!iniFile.GetBooleanValue("Settings", "Host", false))
            return false;

        // Don't allow loading CnCNet games in LAN mode
        if (iniFile.SectionExists("Tunnel"))
            return false;

        return true;
    }

    private static int SavedGameId() =>
        new IniFile(SafePath.CombineFilePath(ProgramConstants.GamePath, ProgramConstants.SAVED_GAME_SPAWN_INI)).GetIntValue("Settings", "GameID", -1);

    /// <summary>Opens a room for loading the saved game as its host (GameCreationWindow_LoadGame).</summary>
    /// <returns>False if the room couldn't be opened (an error was shown).</returns>
    public static bool HostLoadedGame(LanGameLoadingRoom room) =>
        room.SetUp(true, new IPEndPoint(IPAddress.Loopback, ProgramConstants.LAN_GAME_LOBBY_PORT), null, SavedGameId());

    /// <summary>
    /// Joins a hosted game (the XNA lobby's checks, then connects and sends JOIN): a game room, or the saved game room
    /// for a saved game.
    /// </summary>
    /// <returns>True if the room was joined.</returns>
    public bool JoinGame(HostedLANGame hg, LanGameRoom room, LanGameLoadingRoom loadingRoom = null)
    {
        if (hg.Game.InternalName.ToUpper() != localGame.ToUpper())
        {
            AddMessage(new ChatMessage(string.Format("The selected game is for {0}!".L10N("Client:Main:GameIsOfPurpose"),
                gameCollection.GetGameNameFromInternalName(hg.Game.InternalName))));
            return false;
        }

        if (hg.Locked)
        {
            AddMessage(new ChatMessage(string.Format("The game {0} is locked!".L10N("Client:Main:GameLockedWithName"), hg.RoomName)));
            return false;
        }

        if (hg.IsLoadedGame)
        {
            if (loadingRoom == null)
                return false;

            if (!hg.Players.Contains(ProgramConstants.PLAYERNAME))
            {
                AddMessage(new ChatMessage("You do not exist in the saved game!".L10N("Client:Main:NotInSavedGame")));
                return false;
            }
        }
        else
        {
            if (hg.Players.Contains(ProgramConstants.PLAYERNAME))
            {
                AddMessage(new ChatMessage("Your name is already taken in the game.".L10N("Client:Main:NameOccupied")));
                return false;
            }
        }

        if (hg.GameVersion != ProgramConstants.GAME_VERSION)
        {
            AddMessage(new ChatMessage(new ChatColor(255, 255, 0),
                "The game host is on a different game version than you. Version incompatibilities may cause issues.".L10N("Client:Main:JoinGameVersionMismatch")));
        }

        AddMessage(new ChatMessage(string.Format("Attempting to join game {0} ...".L10N("Client:Main:AttemptJoin"), hg.RoomName)));

        try
        {
            var client = new TcpClient(hg.EndPoint.Address.ToString(), ProgramConstants.LAN_GAME_LOBBY_PORT);

            if (hg.IsLoadedGame)
            {
                int loadedGameId = SavedGameId();

                loadingRoom.SetUp(false, hg.EndPoint, client, loadedGameId);

                byte[] loadBuffer = encoding.GetBytes(LanGameConnection.PLAYER_JOIN_COMMAND + ProgramConstants.LAN_DATA_SEPARATOR +
                    ProgramConstants.PLAYERNAME + ProgramConstants.LAN_DATA_SEPARATOR +
                    loadedGameId + ProgramConstants.LAN_MESSAGE_SEPARATOR);

                client.GetStream().Write(loadBuffer, 0, loadBuffer.Length);
                client.GetStream().Flush();

                loadingRoom.PostJoin();
                return true;
            }

            room.SetUp(false, hg.EndPoint, client);

            byte[] buffer = encoding.GetBytes(LanGameConnection.PLAYER_JOIN_COMMAND + ProgramConstants.LAN_DATA_SEPARATOR +
                ProgramConstants.PLAYERNAME + ProgramConstants.LAN_MESSAGE_SEPARATOR);

            client.GetStream().Write(buffer, 0, buffer.Length);
            client.GetStream().Flush();

            room.PostJoin();
            return true;
        }
        catch (Exception ex)
        {
            AddMessage(new ChatMessage("Connecting to the game failed! Message:".L10N("Client:Main:ConnectGameFailed") + " " + ex.Message));
            return false;
        }
    }

    public void Dispose()
    {
        broadcastManager.Dispose();
        messageDeduplicator.Dispose();
    }
}
