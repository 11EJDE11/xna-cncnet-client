using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Linq;
using System.Reflection;

using Avalonia.Threading;

using ClientCore;
using ClientCore.Enums;
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
    private readonly CnCNetUserData userData;
    private readonly GameInvitationsViewModel invitations;
    private readonly DispatcherTimer timer;
    private readonly Stopwatch stopwatch = new();
    private List<HostedCnCNetGame> shownGames = [];
    private HostedCnCNetGame passwordGame;
    private bool initialized;

    public CnCNetLobbyViewModel(CnCNetLobbyService lobby, CnCNetGameRoomViewModel roomViewModel, IDialogService dialogs,
        GameCollection gameCollection, MapLoader mapLoader, CnCNetUserData userData, GameInvitationsViewModel invitations,
        TunnelHandler tunnelHandler)
    {
        LoadingRoom = new CnCNetGameLoadingRoomViewModel(lobby.LoadingRoom, tunnelHandler);
        CreationTunnels = new TunnelListViewModel(tunnelHandler);
        CreationTunnels.Changed += (_, _) => OnPropertyChanged(nameof(CanCreateGame));
        lobby.LoadingRoomEntered += (_, _) => LoadingRoomEntered?.Invoke(this, EventArgs.Empty);
        lobby.LoadingRoomLeft += (_, _) => LoadingRoomLeft?.Invoke(this, EventArgs.Empty);
        lobby.LoadingRoom.ShowRequested += (_, _) => LoadingRoomEntered?.Invoke(this, EventArgs.Empty);
        this.invitations = invitations;
        this.userData = userData;
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

        // An admin announced a new version: update now (the main menu checks), or stop asking
        lobby.UpdateAvailable += (_, _) => dialogs.Confirm("Update available".L10N("Client:Main:UpdateAvailableTitle"),
            "An update is available. Do you want to perform the update now?".L10N("Client:Main:UpdateAvailableText"),
            () => UpdateCheckRequested?.Invoke(this, EventArgs.Empty), lobby.DeclineUpdate);
        AvClientView.Theme.ThemeSound gameCreatedSound = AvClientView.Theme.ThemeSound.Load("gamecreated.wav");
        lobby.GameHostedNotification += (_, _) => gameCreatedSound.Play();
        lobby.ConnectionChanged += (_, _) => RefreshConnection();

        AvClientView.Theme.ThemeSound inviteSound = AvClientView.Theme.ThemeSound.Load("pm.wav");
        lobby.InvitationReceived += (_, invitation) =>
        {
            int gameId = lobby.FindUserGameId(invitation.Sender);
            invitations.Add(invitation, gameId >= 0 && gameId < gameCollection.GameList.Count ? gameCollection.GameList[gameId] : null);
            inviteSound.Play();
        };
        lobby.InvitationDismissed += (_, invitation) => invitations.Remove(invitation);
        invitations.Accepted += (_, invitation) => AcceptInvitation(invitation);
        invitations.Declined += (_, invitation) => lobby.DeclineInvitation(invitation);
        lobby.RoomEntered += (_, _) => RoomEntered?.Invoke(this, EventArgs.Empty);
        lobby.RoomLeft += (_, _) => RoomLeft?.Invoke(this, EventArgs.Empty);

        timer = new DispatcherTimer(TimeSpan.FromMilliseconds(100), DispatcherPriority.Background, (_, _) => Tick());

        CnCNetPlayerCountTask.CnCNetGameCountUpdated += (_, e) => Dispatcher.UIThread.Post(() =>
            OnlineCount = e.PlayerCount < 0 ? "N/A" : e.PlayerCount.ToString());
    }

    public CnCNetGameRoomViewModel Room { get; }

    /// <summary>The saved game room (CnCNetGameLoadingLobby).</summary>
    public CnCNetGameLoadingRoomViewModel LoadingRoom { get; }

    /// <summary>The creation window's Load Game can be used (the local player hosted the saved game).</summary>
    [ObservableProperty]
    private bool canLoadGame;

    /// <summary>The creation window's tunnel mode and tunnel list (its advanced options).</summary>
    public TunnelListViewModel CreationTunnels { get; }

    /// <summary>The creation window shows its advanced options (GameCreationWindow_Advanced).</summary>
    [ObservableProperty]
    private bool showAdvancedCreationOptions;

    /// <summary>The creation window's tunnel mode (saved in the TunnelMode setting).</summary>
    [ObservableProperty]
    private int creationTunnelModeIndex;

    /// <summary>A game can be created: tunnels are listed (LbTunnelList_ListRefreshed).</summary>
    public bool CanCreateGame => CreationTunnels.Tunnels.Count > 0;

    partial void OnCreationTunnelModeIndexChanged(int value)
    {
        TunnelMode mode = TunnelListViewModel.ModeAt(value);
        CreationTunnels.SetMode(mode);

        if ((TunnelMode)UserINISettings.Instance.TunnelMode.Value != mode)
        {
            UserINISettings.Instance.TunnelMode.Value = (int)mode;
            UserINISettings.Instance.SaveSettings();
        }
    }

    /// <summary>The creation window's Advanced Options button.</summary>
    public void ShowAdvancedOptions() => ShowAdvancedCreationOptions = true;

    /// <summary>The tunnel to create the game on: null for dynamic tunnels; false if a static mode has none selected.</summary>
    private bool TryGetCreationTunnel(out CnCNetTunnel tunnel)
    {
        tunnel = null;
        if ((TunnelMode)UserINISettings.Instance.TunnelMode.Value == TunnelMode.V3Dynamic)
            return true;

        if (!CreationTunnels.State.IsValidIndexSelected)
            return false;

        tunnel = CreationTunnels.State.GetSelectedTunnel();
        return true;
    }

    /// <summary>The player entered the saved game room.</summary>
    public event EventHandler LoadingRoomEntered;

    /// <summary>The player left the saved game room.</summary>
    public event EventHandler LoadingRoomLeft;

    public ObservableCollection<ChatLineViewModel> Messages { get; } = [];

    public ObservableCollection<UserItemViewModel> Users { get; } = [];

    public ObservableCollection<HostedGameItemViewModel> Games { get; } = [];

    public IReadOnlyList<string> ChatColors { get; }

    /// <summary>The chat colours with their values (the colour drop-down shows each in its colour).</summary>
    public IReadOnlyList<IRCColor> ChatColorOptions => lobby.ChatColors;

    /// <summary>The games of the chat channels, in the channel drop-down's order (for their icons).</summary>
    public IReadOnlyList<CnCNetGame> ChatChannelGames => gameCollection.GameList
        .Where(game => game.Supported && !string.IsNullOrEmpty(game.ChatChannel)).ToList();

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

    /// <summary>The user wants the announced update: the main menu checks for updates (UpdateCheck).</summary>
    public event EventHandler UpdateCheckRequested;

    /// <summary>The lobby page must be shown (an accepted invitation needs the password prompt).</summary>
    public event EventHandler ShowRequested;

    public Avalonia.Media.IBrush ChatInputBrush =>
        new Avalonia.Media.SolidColorBrush(AvClientView.Theme.ThemeAssets.ToColor(lobby.ChatColors[lobby.ChatColorIndex].Color));

    partial void OnSelectedChatColorIndexChanged(int value)
    {
        lobby.ChatColorIndex = value;
        OnPropertyChanged(nameof(ChatInputBrush));
    }

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
        string selectedName = SelectedUser?.Name;
        var updated = new List<UserItemViewModel>();
        foreach (ChannelUser user in lobby.CurrentUsers)
        {
            int gameId = user.IRCUser.GameID;
            CnCNetGame game = gameId >= 0 && gameId < gameCollection.GameList.Count ? gameCollection.GameList[gameId] : null;
            updated.Add(new UserItemViewModel(user.IRCUser.Name, user.IsAdmin, game,
                user.IRCUser.IsFriend, user.IRCUser.IsIgnored, user.HasVoice));
        }
        ObservableCollectionSync.Synchronize(Users, updated, user => user.Name);
        SelectedUser = Users.FirstOrDefault(user => user.Name == selectedName);
    }

    [ObservableProperty]
    private UserItemViewModel selectedUser;

    private void RefreshGames()
    {
        HostedCnCNetGame selected = SelectedGameIndex >= 0 && SelectedGameIndex < shownGames.Count ? shownGames[SelectedGameIndex] : null;

        shownGames = lobby.Games.Where(HostedGameMatches).ToList();
        ObservableCollectionSync.Synchronize(Games,
            shownGames.Select(game => new HostedGameItemViewModel(game, lobby.Room.Options)).ToList(),
            item => ((HostedCnCNetGame)item.Game).ChannelName);

        SelectedGameIndex = selected == null ? -1 : shownGames.FindIndex(g => g.HostName == selected.HostName);
    }

    /// <summary>The game search text (tbGameSearch); empty for none.</summary>
    [ObservableProperty]
    private string gameSearchText = string.Empty;

    partial void OnGameSearchTextChanged(string value) => RefreshGames();

    /// <summary>The game list's sort order (SortState: none, A-Z, Z-A).</summary>
    public SortDirection GameSortState => Enum.IsDefined(typeof(SortDirection), UserINISettings.Instance.SortState.Value)
        ? (SortDirection)UserINISettings.Instance.SortState.Value : SortDirection.None;

    /// <summary>The sort button (BtnGameSortAlpha_LeftClick): the next order, saved in the settings.</summary>
    public void CycleGameSort()
    {
        UserINISettings.Instance.SortState.Value = ((int)GameSortState + 1) % 3;
        OnPropertyChanged(nameof(GameSortState));
        RefreshGames();
        UserINISettings.Instance.SaveSettings();
    }

    /// <summary>Whether any game filter is set (the filter button's "active" texture).</summary>
    public bool GameFiltersApplied => UserINISettings.Instance.IsGameFiltersApplied();

    /// <summary>The filters panel was closed: the button and the list follow the saved filters.</summary>
    public void GameFiltersChanged()
    {
        OnPropertyChanged(nameof(GameFiltersApplied));
        RefreshGames();
    }

    /// <summary>The broadcast options that can be filtered (ShowInFilters), for the filters panel.</summary>
    public IReadOnlyList<ClientLogic.Options.GameOption> FilterableOptions =>
        lobby.Room.Options.CheckBoxes.Where(o => o.BroadcastToLobby && o.Definition.ShowInFilters)
            .Concat(lobby.Room.Options.DropDowns.Where(o => o.BroadcastToLobby && o.Definition.ShowInFilters)).ToList();

    /// <summary>CnCNetLobby.HostedGameMatches: the saved filters and the search text.</summary>
    private bool HostedGameMatches(GenericHostedGame hg)
    {
        UserINISettings settings = UserINISettings.Instance;
        var filter = new ClientLogic.GameList.GameListFilterSettings(
            ShowFriendGamesOnly: settings.ShowFriendGamesOnly,
            HideLockedGames: settings.HideLockedGames.Value,
            HideIncompatibleGames: settings.HideIncompatibleGames.Value,
            HidePasswordedGames: settings.HidePasswordedGames.Value,
            MaxPlayerCount: settings.MaxPlayerCount.Value,
            SearchText: string.IsNullOrEmpty(GameSearchText) ? null : GameSearchText,
            OptionFilters: lobby.Room.Options.CheckBoxes.Where(o => o.BroadcastToLobby)
                .Concat(lobby.Room.Options.DropDowns.Where(o => o.BroadcastToLobby))
                .Select(o => settings.GetGameOptionFilterValue(o.Name)).ToList());

        return ClientLogic.GameList.GameListFilter.Matches(hg, filter, userData.IsFriend,
            gameMode => string.IsNullOrEmpty(gameMode)
                ? "Unknown".L10N("Client:Main:Unknown")
                : gameMode.L10N($"INI:GameModes:{gameMode}:UIName", ClientCore.I18N.TranslationNotificationLevel.Verbose),
            map => string.IsNullOrEmpty(map)
                ? "Unknown".L10N("Client:Main:Unknown") : mapLoader.TranslatedMapNames.TryGetValue(map, out string translated)
                ? translated : null);
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
        CanLoadGame = !ClientConfiguration.Instance.DisableMultiplayerGameLoading && CnCNetLobbyService.CanHostLoadedGame();
        ShowAdvancedCreationOptions = ShowAdvancedCreationOptions || UserINISettings.Instance.AlwaysDisplayTunnelList;

        // GameCreationWindow.Refresh: the mode from the settings
        CreationTunnels.EnsureListed();
        CreationTunnelModeIndex = TunnelListViewModel.IndexOf((TunnelMode)UserINISettings.Instance.TunnelMode.Value);
        CreationTunnels.SetMode(TunnelListViewModel.ModeAt(CreationTunnelModeIndex));
        OnPropertyChanged(nameof(CanCreateGame));
        ShowCreateGame = true;
    }

    [RelayCommand]
    private void CancelCreateGame() => ShowCreateGame = false;

    [RelayCommand]
    private void CreateGame()
    {
        if (!CanCreateGame || !TryGetCreationTunnel(out CnCNetTunnel tunnel))
            return;

        Room.Start();
        string error = lobby.CreateGame(NewRoomName, NewRoomPassword, int.Parse(MaxPlayerItems[Math.Max(0, NewRoomMaxPlayersIndex)]), NewRoomSkillLevel, tunnel);
        if (error != null)
        {
            dialogs.ShowMessage("Invalid game name".L10N("Client:Main:InvalidGameName"), error);
            return;
        }

        ShowCreateGame = false;
    }

    /// <summary>The creation window's Load Game (BtnLoadMPGame_LeftClick): hosts the saved game.</summary>
    [RelayCommand]
    private void LoadGame()
    {
        if (!CanLoadGame || !CanCreateGame || !TryGetCreationTunnel(out CnCNetTunnel tunnel))
            return;

        LoadingRoom.Start();
        string error = lobby.CreateLoadedGame(NewRoomName, tunnel);
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

    /// <summary>The player menu's Join: joins the game the player is in, or says they aren't in one.</summary>
    public void JoinUser(string name)
    {
        HostedCnCNetGame game = shownGames.FirstOrDefault(g => g.Players.Contains(name));
        if (game == null)
        {
            lobby.AddNotice(string.Format("{0} is not in a game!".L10N("Client:Main:UserNotInGame"), name));
            return;
        }

        SelectedGameIndex = shownGames.IndexOf(game);
        Join(game, string.Empty);
    }

    private void Join(HostedCnCNetGame game, string password)
    {
        if (game.IsLoadedGame)
            LoadingRoom.Start();
        else
            Room.Start();

        if (lobby.JoinGame(game, password) == CnCNetLobbyService.JoinResult.NeedsPassword)
        {
            passwordGame = game;
            JoinPassword = string.Empty;
            ShowPasswordPrompt = true;
        }
    }

    /// <summary>An invitation's Yes: leaves the current room and joins the invited game.</summary>
    private void AcceptInvitation(GameInvitation invitation)
    {
        Room.Start();

        switch (lobby.AcceptInvitation(invitation))
        {
            case CnCNetLobbyService.JoinResult.Failed:
                dialogs.ShowMessage("Failed to join".L10N("Client:Main:JoinFailedTitle"),
                    string.Format("Unable to join {0}'s game. The game may be locked or closed.".L10N("Client:Main:JoinFailedText"), invitation.Sender));
                break;
            case CnCNetLobbyService.JoinResult.NeedsPassword:
                passwordGame = lobby.Games.FirstOrDefault(g => g.ChannelName == invitation.ChannelName);
                JoinPassword = string.Empty;
                ShowPasswordPrompt = passwordGame != null;
                ShowRequested?.Invoke(this, EventArgs.Empty);
                break;
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

    /// <summary>The top bar's Log Out: disconnects even in persistent mode.</summary>
    public void LogOutFromTopBar()
    {
        timer.Stop();
        lobby.Shutdown();
        RefreshConnection();
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
