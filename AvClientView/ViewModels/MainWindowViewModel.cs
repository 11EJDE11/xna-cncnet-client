using System;

using Avalonia.Threading;

using ClientCore;
using ClientCore.Extensions;

using ClientLogic.Launch;

using CommunityToolkit.Mvvm.ComponentModel;

using Microsoft.Extensions.DependencyInjection;

namespace AvClientView.ViewModels;

/// <summary>The main window: shows the main menu, the skirmish lobby, the LAN screens or the CnCNet screens.</summary>
public sealed partial class MainWindowViewModel : ObservableObject
{
    private readonly IServiceProvider services;

    public MainWindowViewModel(MainMenuViewModel menu, TopBarViewModel topBar, OptionsWindowViewModel options, IServiceProvider services)
    {
        this.services = services;
        Menu = menu;
        TopBar = topBar;
        Options = options;
        PrivateMessages = services.GetRequiredService<PrivateMessagesViewModel>();
        Title = menu.Title;
        currentPage = menu;
        primaryPage = menu;

        // The main menu music (XNA MainMenu): played on start, faded while a game runs or, with StopMusicOnMenu,
        // while another screen is shown, and following the settings
        GameProcessService gameProcess = services.GetRequiredService<GameProcessService>();
        gameProcess.GameProcessStarted += () => Dispatcher.UIThread.Post(Music.FadeOut);
        gameProcess.GameProcessExited += () => Dispatcher.UIThread.Post(() =>
        {
            if (!UserINISettings.Instance.StopMusicOnMenu || CurrentPage == Menu)
                Music.Play();
        });
        UserINISettings.Instance.SettingsSaved += (_, _) => Dispatcher.UIThread.Post(() => Music.SettingsSaved(CurrentPage == Menu));
        Dispatcher.UIThread.Post(Music.Play);

        menu.SkirmishRequested += (_, _) => OpenSkirmish();
        menu.LanRequested += (_, _) => OpenLan();
        menu.CnCNetRequested += (_, _) => OpenCnCNet();
        menu.OptionsRequested += (_, _) => OpenOptions();
        Campaign = services.GetRequiredService<CampaignViewModel>();
        menu.CampaignRequested += (_, _) => Campaign.Open();
        LoadGame = services.GetRequiredService<LoadGameViewModel>();
        menu.LoadGameRequested += (_, _) => LoadGame.Open();
        Extras = services.GetRequiredService<ExtrasViewModel>();
        Statistics = services.GetRequiredService<StatisticsViewModel>();
        menu.ExtrasRequested += (_, _) => Extras.Open();
        menu.StatisticsRequested += (_, _) => OpenStatistics();
        Extras.StatisticsRequested += (_, _) => OpenStatistics();
        topBar.OptionsRequested += (_, _) =>
        {
            PrivateMessages.Close();
            OpenOptions();
        };
        options.RestartRequested += (_, _) => RestartRequested?.Invoke(this, EventArgs.Empty);

        // While the options window is open or an update runs, the top bar's switch and options buttons can't be
        // used (XNA)
        options.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(OptionsWindowViewModel.IsOpen))
                UpdateTopBarLock();
        };

        // The updater (the XNA main menu's update windows)
        Updater = services.GetRequiredService<UpdaterViewModel>();
        menu.UpdateStatus.Changed += (_, _) => UpdateTopBarLock();
        Updater.VersionChanged += (_, _) => menu.RefreshVersion();
        options.ForceUpdateRequested += (_, _) => Updater.ForceUpdate();
        Updater.RestartRequested += (_, _) => UpdaterRestartRequested?.Invoke(this, EventArgs.Empty);

        topBar.MainRequested += (_, _) => CurrentPage = primaryPage;
        topBar.CnCNetLobbyRequested += (_, _) => OpenCnCNet();
        topBar.PrivateMessagesRequested += (_, _) => PrivateMessages.Open();
        PrivateMessages.JoinRequested += (_, name) =>
        {
            OpenCnCNet();
            cncnetLobby.JoinUser(name);
        };
        PrivateMessages.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(PrivateMessagesViewModel.UnreadCount))
                TopBar.UnreadPrivateMessages = PrivateMessages.UnreadCount;
        };
        topBar.LogOutRequested += (_, _) =>
        {
            cncnetLobby?.LogOutFromTopBar();
            CurrentPage = primaryPage;
        };
    }

    public MainMenuViewModel Menu { get; }

    public TopBarViewModel TopBar { get; }

    public OptionsWindowViewModel Options { get; }

    public CampaignViewModel Campaign { get; }

    public LoadGameViewModel LoadGame { get; }

    public ExtrasViewModel Extras { get; }

    public StatisticsViewModel Statistics { get; }

    public PrivateMessagesViewModel PrivateMessages { get; }

    public UpdaterViewModel Updater { get; }

    /// <summary>The updater started its second stage, which replaces the client's files: the client exits.</summary>
    public event EventHandler UpdaterRestartRequested;

    private void UpdateTopBarLock()
    {
        bool locked = Options.IsOpen || Menu.UpdateStatus.IsUpdateInProgress;
        TopBar.CanOpenOptions = !locked;
        if (!TopBar.LanMode)
            TopBar.CanSwitch = !locked;
    }

    /// <summary>The options were saved and the user chose to restart the client.</summary>
    public event EventHandler RestartRequested;

    private void OpenStatistics() => Statistics.Open();

    private bool startupChecksRun;

    /// <summary>
    /// The main menu's checks once the window is shown (the XNA MainMenu's PostInit): missing and interfering files,
    /// the first run's question and the translation's game files.
    /// </summary>
    public void RunStartupChecks()
    {
        if (startupChecksRun)
            return;

        startupChecksRun = true;

        // The update check (PostInit), unless the user checks by hand
        Menu.UpdateStatus.Start();

        var dialogs = services.GetRequiredService<ClientLogic.UI.IDialogService>();

        if (ClientLogic.UI.StartupChecks.MissingRequiredFiles() is { } missing)
            dialogs.ShowMessage(missing.Title, missing.Text);

        if (ClientLogic.UI.StartupChecks.InterferingFiles() is { } interfering)
            dialogs.ShowMessage(interfering.Title, interfering.Text);

        if (ClientLogic.UI.StartupChecks.TakeFirstRunQuestion() is { } firstRun)
            dialogs.Confirm(firstRun.Title, firstRun.Text, OpenOptions);

        if (ClientLogic.UI.StartupChecks.ApplyTranslationGameFiles(ClientUpdater.Updater.GameVersion) is { } translation)
            dialogs.ShowMessage(translation.Title, translation.Text);
    }

    /// <summary>The main menu buttons' hotkeys (SetButtonHotkeys), while the main menu is shown without a window over it.</summary>
    public bool HandleMainMenuHotkey(Avalonia.Input.Key key)
    {
        if (CurrentPage != Menu || UserINISettings.Instance.DisableMainMenuHotkeys || Menu.UpdateStatus.IsUpdateInProgress ||
            Updater.IsQueryOpen || Updater.IsManualOpen || Updater.IsUpdateOpen || Options.IsOpen || Campaign.IsOpen || LoadGame.IsOpen || Extras.IsOpen || Statistics.IsOpen || PrivateMessages.IsOpen)
            return false;

        string button = key switch
        {
            Avalonia.Input.Key.C => "btnNewCampaign",
            Avalonia.Input.Key.L => "btnLoadGame",
            Avalonia.Input.Key.S => "btnSkirmish",
            Avalonia.Input.Key.M => "btnCnCNet",
            Avalonia.Input.Key.N => "btnLan",
            Avalonia.Input.Key.O => "btnOptions",
            Avalonia.Input.Key.E => "btnMapEditor",
            Avalonia.Input.Key.T => "btnStatistics",
            Avalonia.Input.Key.R => "btnCredits",
            Avalonia.Input.Key.X => "btnExtras",
            _ => null,
        };

        if (button == null)
            return false;

        Menu.Click(button, string.Empty);
        return true;
    }

    private void OpenOptions()
    {
        // TopBar: the main-menu-only options while the main menu is the only primary screen, outside LAN mode
        if (!Options.IsOpen)
            Options.Open(primaryPage == Menu && !TopBar.LanMode);
    }

    /// <summary>
    /// The top bar's primary screen (TopBar's primary switchables): the main menu, or the skirmish lobby or CnCNet
    /// game room while one is open.
    /// </summary>
    private object primaryPage;

    private void SetPrimary(object page, string switchName)
    {
        primaryPage = page;
        TopBar.SetPrimaryName(switchName);
    }

    private void ResetPrimary() => SetPrimary(Menu, "Main Menu".L10N("Client:Main:MainMenu"));

    public string Title { get; }

    [ObservableProperty]
    private object currentPage;

    partial void OnCurrentPageChanged(object oldValue, object newValue)
    {
        // The top bar's switches close the private messages window (XNA's tertiary switch)
        PrivateMessages.Close();

        // The main menu's SwitchOn: check for updates again
        if (newValue == Menu && oldValue != newValue)
            Menu.UpdateStatus.SwitchedOn();

        // The main menu's SwitchOn / SwitchOff
        if (UserINISettings.Instance.StopMusicOnMenu && oldValue != newValue)
        {
            if (newValue == Menu)
                Music.Play();
            else if (oldValue == Menu)
                Music.FadeOut();
        }
    }

    public AvClientView.Theme.ThemeMusic Music { get; } = new();

    public event EventHandler ExitRequested
    {
        add => Menu.ExitRequested += value;
        remove => Menu.ExitRequested -= value;
    }

    private void OpenSkirmish()
    {
        var skirmish = services.GetRequiredService<SkirmishViewModel>();
        skirmish.BackRequested += (_, _) =>
        {
            ResetPrimary();
            CurrentPage = Menu;
        };
        SetPrimary(skirmish, "Skirmish Lobby".L10N("Client:Main:SkirmishLobby"));
        CurrentPage = skirmish;
    }

    private LanLobbyViewModel lanLobby;

    private void OpenLan()
    {
        if (lanLobby == null)
        {
            lanLobby = services.GetRequiredService<LanLobbyViewModel>();
            lanLobby.BackRequested += (_, _) =>
            {
                TopBar.SetLanMode(false);
                CurrentPage = Menu;
            };
            lanLobby.RoomEntered += (_, _) => CurrentPage = lanLobby.Room;
            lanLobby.LoadingRoomEntered += (_, _) => CurrentPage = lanLobby.LoadingRoom;
            lanLobby.RoomLeft += (_, _) => CurrentPage = lanLobby;
        }

        TopBar.SetLanMode(true);
        lanLobby.Open();
        CurrentPage = lanLobby;
    }

    private CnCNetLobbyViewModel cncnetLobby;

    private void OpenCnCNet()
    {
        if (cncnetLobby == null)
        {
            cncnetLobby = services.GetRequiredService<CnCNetLobbyViewModel>();
            cncnetLobby.BackRequested += (_, _) => CurrentPage = Menu;
            cncnetLobby.UpdateCheckRequested += (_, _) =>
            {
                Menu.UpdateStatus.CheckForUpdates();
                CurrentPage = primaryPage;
            };
            cncnetLobby.ShowRequested += (_, _) => CurrentPage = cncnetLobby;
            cncnetLobby.RoomEntered += (_, _) =>
            {
                SetPrimary(cncnetLobby.Room, "Game Lobby".L10N("Client:Main:GameLobby"));
                CurrentPage = cncnetLobby.Room;
            };
            cncnetLobby.LoadingRoomEntered += (_, _) =>
            {
                SetPrimary(cncnetLobby.LoadingRoom, "Load Game".L10N("Client:Main:LoadGame"));
                CurrentPage = cncnetLobby.LoadingRoom;
            };
            cncnetLobby.LoadingRoomLeft += (_, _) =>
            {
                ResetPrimary();
                if (CurrentPage == cncnetLobby.LoadingRoom)
                    CurrentPage = cncnetLobby;
            };
            cncnetLobby.RoomLeft += (_, _) =>
            {
                ResetPrimary();
                if (CurrentPage == cncnetLobby.Room)
                    CurrentPage = cncnetLobby;
            };
        }

        cncnetLobby.Open();
        CurrentPage = cncnetLobby;
    }

    /// <summary>The client is closing: leave the multiplayer rooms and lobbies that were opened.</summary>
    public void Shutdown()
    {
        Music.Dispose();
        lanLobby?.Shutdown();
        cncnetLobby?.Shutdown();
    }
}
