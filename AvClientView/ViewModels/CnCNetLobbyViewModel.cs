using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Linq;
using System.Reflection;

using Avalonia.Threading;

using ClientCore;
using ClientCore.Extensions;

using ClientLogic.CnCNet;
using ClientLogic.UI;

using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

using DTAClient.Domain.Multiplayer;
using DTAClient.Domain.Multiplayer.CnCNet;
using DTAClient.Online;

namespace AvClientView.ViewModels;

/// <summary>The CnCNet lobby screen, over a <see cref="CnCNetLobbyService"/>: login, chat, game list, hosting and joining.</summary>
public sealed partial class CnCNetLobbyViewModel : ObservableObject
{
    private readonly CnCNetLobbyService lobby;
    private readonly IDialogService dialogs;
    private readonly GameCollection gameCollection;
    private readonly MapLoader mapLoader;
    private readonly DispatcherTimer timer;
    private readonly Stopwatch stopwatch = new();
    private List<HostedCnCNetGame> shownGames = [];
    private HostedCnCNetGame passwordGame;
    private bool initialized;

    public CnCNetLobbyViewModel(CnCNetLobbyService lobby, CnCNetGameRoomViewModel roomViewModel, IDialogService dialogs,
        GameCollection gameCollection, MapLoader mapLoader)
    {
        this.mapLoader = mapLoader;
        this.lobby = lobby;
        this.gameCollection = gameCollection;
        this.dialogs = dialogs;
        Room = roomViewModel;

        ChatColors = lobby.ChatColors.Select(c => c.Name).ToList();
        selectedChatColorIndex = lobby.ChatColorIndex;
        SkillLevels = ClientConfiguration.Instance.GetSkillLevelOptions()
            .Select((level, i) => level.L10N($"INI:ClientDefinitions:SkillLevel:{i}")).ToList();
        MaxPlayerItems = Enumerable.Range(2, 7).Reverse().Select(i => i.ToString()).ToList();

        lobby.MessageAdded += (_, message) => Messages.Add(ChatLineViewModel.From(message));
        lobby.MessagesReset += (_, _) => ResetMessages();
        lobby.UsersChanged += (_, _) => RefreshUsers();
        lobby.GamesChanged += (_, _) => RefreshGames();
        lobby.ConnectionChanged += (_, _) => RefreshConnection();
        lobby.RoomEntered += (_, _) => RoomEntered?.Invoke(this, EventArgs.Empty);
        lobby.RoomLeft += (_, _) => RoomLeft?.Invoke(this, EventArgs.Empty);

        timer = new DispatcherTimer(TimeSpan.FromMilliseconds(100), DispatcherPriority.Background, (_, _) => Tick());

        CnCNetPlayerCountTask.CnCNetGameCountUpdated += (_, e) => Dispatcher.UIThread.Post(() =>
            OnlineCount = e.PlayerCount < 0 ? "N/A" : e.PlayerCount.ToString());
    }

    public CnCNetGameRoomViewModel Room { get; }

    public ObservableCollection<ChatLineViewModel> Messages { get; } = [];

    public ObservableCollection<UserItemViewModel> Users { get; } = [];

    public ObservableCollection<HostedGameItemViewModel> Games { get; } = [];

    public IReadOnlyList<string> ChatColors { get; }

    public IReadOnlyList<string> ChatChannels => lobby.ChatChannelNames;

    public IReadOnlyList<string> SkillLevels { get; }

    public IReadOnlyList<string> MaxPlayerItems { get; }

    public bool IsConnected => lobby.IsConnected;

    public bool ShowLogin => !lobby.IsConnected && !lobby.IsAttemptingConnection;

    public string ConnectionStatus => lobby.IsConnected ? string.Empty
        : lobby.IsAttemptingConnection ? "Connecting to CnCNet..." : "Not connected.";

    public string LogOutText => UserINISettings.Instance.PersistentMode ? "Main Menu" : "Log Out";

    [ObservableProperty]
    private string playerName = UserINISettings.Instance.PlayerName;

    /// <summary>The CnCNet player count, as the XNA lobby's "Online:" label shows it.</summary>
    [ObservableProperty]
    private string onlineCount = CnCNetPlayerCountTask.PlayerCount > 0 ? CnCNetPlayerCountTask.PlayerCount.ToString() : "-";

    [ObservableProperty]
    private bool rememberMe = UserINISettings.Instance.SkipConnectDialog;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanAutoConnect))]
    private bool persistentMode = UserINISettings.Instance.PersistentMode;

    [ObservableProperty]
    private bool autoConnect = UserINISettings.Instance.AutomaticCnCNetLogin;

    /// <summary>Auto-connect needs "remember me" and persistent mode (the XNA login window's rule).</summary>
    public bool CanAutoConnect => RememberMe && PersistentMode;

    partial void OnRememberMeChanged(bool value) => OnCanAutoConnectChanged();

    partial void OnPersistentModeChanged(bool value) => OnCanAutoConnectChanged();

    private void OnCanAutoConnectChanged()
    {
        OnPropertyChanged(nameof(CanAutoConnect));
        if (!CanAutoConnect)
            AutoConnect = false;
    }

    [ObservableProperty]
    private int selectedChatColorIndex;

    [ObservableProperty]
    private int selectedChannelIndex = -1;

    [ObservableProperty]
    private int selectedGameIndex = -1;

    [ObservableProperty]
    private string chatInput = string.Empty;

    [ObservableProperty]
    private bool showCreateGame;

    [ObservableProperty]
    private string newRoomName = string.Empty;

    [ObservableProperty]
    private string newRoomPassword = string.Empty;

    [ObservableProperty]
    private int newRoomMaxPlayersIndex;

    [ObservableProperty]
    private int newRoomSkillLevel = ClientConfiguration.Instance.DefaultSkillLevelIndex;

    [ObservableProperty]
    private bool showPasswordPrompt;

    [ObservableProperty]
    private string joinPassword = string.Empty;

    public event EventHandler RoomEntered;

    public event EventHandler RoomLeft;

    public event EventHandler BackRequested;

    partial void OnSelectedChatColorIndexChanged(int value) => lobby.ChatColorIndex = value;

    partial void OnSelectedChannelIndexChanged(int value)
    {
        if (value >= 0 && value != lobby.CurrentChannelIndex)
            lobby.SelectChannel(value);
    }

    /// <summary>Opens the lobby (from the main menu); connects at once if the player chose "remember me".</summary>
    public void Open()
    {
        if (!initialized)
        {
            initialized = true;
            lobby.Initialize(Assembly.GetEntryAssembly()?.GetName().Version?.ToString() ?? string.Empty);
            OnPropertyChanged(nameof(ChatChannels));
        }

        SelectedChannelIndex = lobby.CurrentChannelIndex;
        ResetMessages();
        RefreshUsers();
        RefreshGames();
        RefreshConnection();

        stopwatch.Restart();
        timer.Start();

        if (!lobby.IsConnected && !lobby.IsAttemptingConnection && UserINISettings.Instance.SkipConnectDialog)
            Connect();
    }

    private void Tick()
    {
        TimeSpan elapsed = stopwatch.Elapsed;
        stopwatch.Restart();
        lobby.Update(elapsed);
    }

    private void RefreshConnection()
    {
        OnPropertyChanged(nameof(IsConnected));
        OnPropertyChanged(nameof(ShowLogin));
        OnPropertyChanged(nameof(ConnectionStatus));
        OnPropertyChanged(nameof(LogOutText));
    }

    private void ResetMessages()
    {
        Messages.Clear();
        foreach (ChatMessage message in lobby.CurrentMessages)
            Messages.Add(ChatLineViewModel.From(message));

        SelectedChannelIndex = lobby.CurrentChannelIndex;
    }

    private void RefreshUsers()
    {
        Users.Clear();
        foreach (ChannelUser user in lobby.CurrentUsers)
        {
            int gameId = user.IRCUser.GameID;
            CnCNetGame game = gameId >= 0 && gameId < gameCollection.GameList.Count ? gameCollection.GameList[gameId] : null;
            Users.Add(new UserItemViewModel(user.IRCUser.Name, user.IsAdmin, game,
                user.IRCUser.IsFriend, user.IRCUser.IsIgnored, user.HasVoice));
        }
    }

    private void RefreshGames()
    {
        HostedCnCNetGame selected = SelectedGameIndex >= 0 && SelectedGameIndex < shownGames.Count ? shownGames[SelectedGameIndex] : null;

        shownGames = lobby.Games.ToList();
        Games.Clear();
        foreach (HostedCnCNetGame game in shownGames)
            Games.Add(new HostedGameItemViewModel(game, lobby.Room.Options));

        SelectedGameIndex = selected == null ? -1 : shownGames.FindIndex(g => g.HostName == selected.HostName);
    }

    /// <summary>A hosted game's map name and preview, as the XNA game information panel finds them.</summary>
    public (string MapName, Avalonia.Media.Imaging.Bitmap Preview) FindMap(GenericHostedGame game) =>
        HostedGameItemViewModel.FindMap(mapLoader, game);

    [RelayCommand]
    private void Connect()
    {
        string error = lobby.Connect(PlayerName, RememberMe, PersistentMode, AutoConnect && CanAutoConnect);
        if (error != null)
            dialogs.ShowMessage("Invalid Player Name".L10N("Client:Main:InvalidPlayerName"), error);

        RefreshConnection();
    }

    [RelayCommand]
    private void SendChat()
    {
        lobby.SendChatMessage(ChatInput);
        ChatInput = string.Empty;
    }

    [RelayCommand]
    private void OpenCreateGame()
    {
        if (lobby.State.IsInGameRoom)
            return;

        NewRoomName = CnCNetLobbyService.DefaultRoomName;
        NewRoomPassword = string.Empty;
        ShowCreateGame = true;
    }

    [RelayCommand]
    private void CancelCreateGame() => ShowCreateGame = false;

    [RelayCommand]
    private void CreateGame()
    {
        Room.Start();
        string error = lobby.CreateGame(NewRoomName, NewRoomPassword, int.Parse(MaxPlayerItems[Math.Max(0, NewRoomMaxPlayersIndex)]), NewRoomSkillLevel);
        if (error != null)
        {
            dialogs.ShowMessage("Invalid game name".L10N("Client:Main:InvalidGameName"), error);
            return;
        }

        ShowCreateGame = false;
    }

    [RelayCommand]
    private void JoinGame()
    {
        if (SelectedGameIndex < 0 || SelectedGameIndex >= shownGames.Count)
            return;

        Join(shownGames[SelectedGameIndex], string.Empty);
    }

    private void Join(HostedCnCNetGame game, string password)
    {
        Room.Start();

        if (lobby.JoinGame(game, password) == CnCNetLobbyService.JoinResult.NeedsPassword)
        {
            passwordGame = game;
            JoinPassword = string.Empty;
            ShowPasswordPrompt = true;
        }
    }

    [RelayCommand]
    private void SubmitPassword()
    {
        ShowPasswordPrompt = false;
        if (passwordGame != null && !string.IsNullOrEmpty(JoinPassword))
            Join(passwordGame, JoinPassword);

        passwordGame = null;
    }

    [RelayCommand]
    private void CancelPassword()
    {
        ShowPasswordPrompt = false;
        passwordGame = null;
    }

    [RelayCommand]
    private void Back()
    {
        lobby.LogOut();

        // Staying connected keeps the tunnels (and a hosted room's announcements) running, as the XNA client does
        if (!UserINISettings.Instance.PersistentMode)
            timer.Stop();

        BackRequested?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>The client is closing.</summary>
    public void Shutdown()
    {
        timer.Stop();
        lobby.Shutdown();
    }
}

/// <summary>A CnCNet lobby user; the game is null if the user's game is unknown.</summary>
public sealed record UserItemViewModel(string Name, bool IsAdmin, CnCNetGame Game = null,
    bool IsFriend = false, bool IsIgnored = false, bool HasVoice = false)
{
    public Avalonia.Media.IBrush Brush => IsAdmin
        ? new Avalonia.Media.SolidColorBrush(AvClientView.Theme.ThemeAssets.ParseColor(ClientConfiguration.Instance.AdminNameColor, Avalonia.Media.Colors.Red))
        : new Avalonia.Media.SolidColorBrush(AvClientView.Theme.ThemeAssets.ButtonTextColor);

    public override string ToString() => IsAdmin ? Name + " " + "(Admin)".L10N("Client:Main:AdminSuffix") : Name;
}
