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
    private readonly MapLoader mapLoader;
    private readonly DispatcherTimer timer;
    private readonly Stopwatch stopwatch = new();
    private List<HostedLANGame> shownGames = [];

    public LanLobbyViewModel(LanLobby lobby, LanGameRoom room, LanGameRoomViewModel roomViewModel, MapLoader mapLoader)
    {
        this.lobby = lobby;
        this.mapLoader = mapLoader;
        this.room = room;
        Room = roomViewModel;

        ChatColors = LanChatColors.All.Select(c => c.Name).ToList();
        selectedChatColorIndex = lobby.ChatColorIndex;
        room.ChatColorIndex = lobby.ChatColorIndex;

        lobby.MessageAdded += (_, message) => Messages.Add(ChatLineViewModel.From(message));
        lobby.PlayersChanged += (_, _) => RefreshPlayers();
        lobby.GamesChanged += (_, _) => RefreshGames();

        room.GameBroadcast += (_, message) => lobby.SendMessage(message);
        room.LobbyNotification += (_, message) => lobby.AddMessage(new ChatMessage(ChatColor.Red, message));
        room.Left += (_, message) => OnRoomLeft(message);

        timer = new DispatcherTimer(TimeSpan.FromMilliseconds(100), DispatcherPriority.Background, (_, _) => Tick());
    }

    public LanGameRoomViewModel Room { get; }

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

    /// <summary>The player left the room and is back in the lobby.</summary>
    public event EventHandler RoomLeft;

    public event EventHandler BackRequested;

    partial void OnSelectedChatColorIndexChanged(int value)
    {
        lobby.ChatColorIndex = value;
        room.ChatColorIndex = lobby.ChatColorIndex;
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
            room.Update(elapsed);
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
        Games.Clear();
        foreach (HostedLANGame game in shownGames)
            Games.Add(new HostedGameItemViewModel(game));

        SelectedGameIndex = selected == null ? -1 : shownGames.FindIndex(g => g.EndPoint.Equals(selected.EndPoint));
    }

    [RelayCommand]
    private void SendChat()
    {
        lobby.SendChatMessage(ChatInput);
        ChatInput = string.Empty;
    }

    [RelayCommand]
    private void CreateGame()
    {
        Room.Start();
        if (LanLobby.HostGame(room))
            EnterRoom();
    }

    [RelayCommand]
    private void JoinGame()
    {
        if (SelectedGameIndex < 0 || SelectedGameIndex >= shownGames.Count)
            return;

        Room.Start();
        if (lobby.JoinGame(shownGames[SelectedGameIndex], room))
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
            room.Clear();

        lobby.Close();
        lobby.Dispose();
    }
}

