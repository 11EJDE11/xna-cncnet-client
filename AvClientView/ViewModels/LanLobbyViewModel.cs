using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Linq;

using Avalonia.Threading;

using ClientCore;

using ClientLogic.Lan;
using ClientLogic.UI;

using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

using DTAClient.Domain.LAN;
using DTAClient.Domain.Multiplayer;
using DTAClient.Online;

namespace AvClientView.ViewModels;

/// <summary>The LAN lobby screen, over a <see cref="LanLobby"/>; it opens the <see cref="LanGameRoom"/>.</summary>
public sealed partial class LanLobbyViewModel : ObservableObject
{
    private readonly LanLobby lobby;
    private readonly LanGameRoom room;
    private readonly LanGameLoadingRoom loadingRoom;
    private readonly MapLoader mapLoader;
    private readonly DispatcherTimer timer;
    private readonly Stopwatch stopwatch = new();
    private List<HostedLANGame> shownGames = [];

    public LanLobbyViewModel(LanLobby lobby, LanGameRoom room, LanGameRoomViewModel roomViewModel, MapLoader mapLoader,
        LanGameLoadingRoom loadingRoom)
    {
        this.lobby = lobby;
        this.loadingRoom = loadingRoom;
        LoadingRoom = new GameLoadingRoomViewModel(loadingRoom);
        this.mapLoader = mapLoader;
        this.room = room;
        Room = roomViewModel;

        ChatColors = LanChatColors.All.Select(c => c.Name).ToList();
        selectedChatColorIndex = lobby.ChatColorIndex;
        room.ChatColorIndex = lobby.ChatColorIndex;
        loadingRoom.ChatColorIndex = lobby.ChatColorIndex;

        loadingRoom.GameBroadcast += (_, message) => lobby.SendMessage(message);
        loadingRoom.LobbyNotification += (_, message) => lobby.AddMessage(new ChatMessage(ChatColor.Red, message));
        loadingRoom.Left += (_, _) => OnRoomLeft(null);

        lobby.MessageAdded += (_, message) => Messages.Add(ChatLineViewModel.From(message));
        lobby.PlayersChanged += (_, _) => RefreshPlayers();
        lobby.GamesChanged += (_, _) => RefreshGames();

        room.GameBroadcast += (_, message) => lobby.SendMessage(message);
        room.LobbyNotification += (_, message) => lobby.AddMessage(new ChatMessage(ChatColor.Red, message));
        room.Left += (_, message) => OnRoomLeft(message);

        timer = new DispatcherTimer(TimeSpan.FromMilliseconds(100), DispatcherPriority.Background, (_, _) => Tick());
    }

    public LanGameRoomViewModel Room { get; }

    /// <summary>The saved game room (LANGameLoadingLobby).</summary>
    public GameLoadingRoomViewModel LoadingRoom { get; }

    /// <summary>The LANGameCreationWindow is shown (New Game / Load Game / Cancel).</summary>
    [ObservableProperty]
    private bool showCreationWindow;

    /// <summary>The saved game can be hosted (the creation window's Load Game).</summary>
    [ObservableProperty]
    private bool canLoadGame;

    /// <summary>A hosted game's map name and preview, as the XNA game information panel finds them.</summary>
    public (string MapName, Avalonia.Media.Imaging.Bitmap Preview) FindMap(GenericHostedGame game) =>
        HostedGameItemViewModel.FindMap(mapLoader, game);

    public ObservableCollection<ChatLineViewModel> Messages { get; } = [];

    public ObservableCollection<string> Players { get; } = [];

    public ObservableCollection<HostedGameItemViewModel> Games { get; } = [];

    public IReadOnlyList<string> ChatColors { get; }

    public bool InRoom { get; private set; }

    [ObservableProperty]
    private int selectedChatColorIndex;

    [ObservableProperty]
    private int selectedGameIndex = -1;

    [ObservableProperty]
    private string chatInput = string.Empty;

    /// <summary>The player entered a game room (as host or player).</summary>
    public event EventHandler RoomEntered;

    /// <summary>The player entered the saved game room.</summary>
    public event EventHandler LoadingRoomEntered;

    /// <summary>The player left the room and is back in the lobby.</summary>
    public event EventHandler RoomLeft;

    public event EventHandler BackRequested;

    public Avalonia.Media.IBrush ChatInputBrush =>
        new Avalonia.Media.SolidColorBrush(AvClientView.Theme.ThemeAssets.ToColor(LanChatColors.All[lobby.ChatColorIndex].Color));

    partial void OnSelectedChatColorIndexChanged(int value)
    {
        lobby.ChatColorIndex = value;
        room.ChatColorIndex = lobby.ChatColorIndex;
        loadingRoom.ChatColorIndex = lobby.ChatColorIndex;
        OnPropertyChanged(nameof(ChatInputBrush));
    }

    /// <summary>Opens the lobby (from the main menu).</summary>
    public void Open()
    {
        Messages.Clear();
        lobby.Open();
        stopwatch.Restart();
        timer.Start();
    }

    private void Tick()
    {
        TimeSpan elapsed = stopwatch.Elapsed;
        stopwatch.Restart();

        lobby.Update(elapsed);

        if (InRoom)
        {
            room.Update(elapsed);
            loadingRoom.Update(elapsed);
        }
    }

    private void RefreshPlayers()
    {
        Players.Clear();
        foreach (LanLobbyPlayer player in lobby.Players)
            Players.Add(player.Name);
    }

    private void RefreshGames()
    {
        HostedLANGame selected = SelectedGameIndex >= 0 && SelectedGameIndex < shownGames.Count ? shownGames[SelectedGameIndex] : null;

        shownGames = lobby.Games.ToList();
        ObservableCollectionSync.Synchronize(Games,
            shownGames.Select(game => new HostedGameItemViewModel(game)).ToList(),
            item => ((HostedLANGame)item.Game).EndPoint);

        SelectedGameIndex = selected == null ? -1 : shownGames.FindIndex(g => g.EndPoint.Equals(selected.EndPoint));
    }

    [RelayCommand]
    private void SendChat()
    {
        lobby.SendChatMessage(ChatInput);
        ChatInput = string.Empty;
    }

    /// <summary>Create Game: the LANGameCreationWindow.</summary>
    [RelayCommand]
    private void CreateGame()
    {
        CanLoadGame = LanLobby.CanHostLoadedGame();
        ShowCreationWindow = true;
    }

    /// <summary>The creation window's New Game.</summary>
    public void NewGame()
    {
        ShowCreationWindow = false;
        Room.Start();
        if (LanLobby.HostGame(room))
            EnterRoom();
    }

    /// <summary>The creation window's Load Game.</summary>
    public void LoadGame()
    {
        if (!CanLoadGame)
            return;

        ShowCreationWindow = false;
        LoadingRoom.Start();
        if (LanLobby.HostLoadedGame(loadingRoom))
            EnterLoadingRoom();
    }

    public void CancelCreation() => ShowCreationWindow = false;

    private void EnterLoadingRoom()
    {
        InRoom = true;
        LoadingRoomEntered?.Invoke(this, EventArgs.Empty);
    }

    [RelayCommand]
    private void JoinGame()
    {
        if (SelectedGameIndex < 0 || SelectedGameIndex >= shownGames.Count)
            return;

        HostedLANGame game = shownGames[SelectedGameIndex];
        if (game.IsLoadedGame)
        {
            LoadingRoom.Start();
            if (lobby.JoinGame(game, room, loadingRoom))
                EnterLoadingRoom();

            return;
        }

        Room.Start();
        if (lobby.JoinGame(game, room))
            EnterRoom();
    }

    private void EnterRoom()
    {
        InRoom = true;
        RoomEntered?.Invoke(this, EventArgs.Empty);
    }

    private void OnRoomLeft(string message)
    {
        InRoom = false;

        if (!string.IsNullOrWhiteSpace(message))
            lobby.AddMessage(new ChatMessage(ChatColor.Red, message));

        RoomLeft?.Invoke(this, EventArgs.Empty);
    }

    [RelayCommand]
    private void Back()
    {
        timer.Stop();
        lobby.Close();
        BackRequested?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>The client is closing: leave the room and the lobby.</summary>
    public void Shutdown()
    {
        if (!timer.IsEnabled)
            return;

        timer.Stop();

        if (InRoom)
        {
            room.Clear();
            loadingRoom.Clear();
        }

        lobby.Close();
        lobby.Dispose();
    }
}

