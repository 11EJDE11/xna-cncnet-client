using System;

using ClientCore.Extensions;

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

        menu.SkirmishRequested += (_, _) => OpenSkirmish();
        menu.LanRequested += (_, _) => OpenLan();
        menu.CnCNetRequested += (_, _) => OpenCnCNet();
        menu.OptionsRequested += (_, _) => OpenOptions();
        Campaign = services.GetRequiredService<CampaignViewModel>();
        menu.CampaignRequested += (_, _) => Campaign.Open();
        LoadGame = services.GetRequiredService<LoadGameViewModel>();
        menu.LoadGameRequested += (_, _) => LoadGame.Open();
        topBar.OptionsRequested += (_, _) => OpenOptions();
        options.RestartRequested += (_, _) => RestartRequested?.Invoke(this, EventArgs.Empty);

        // While the options window is open, the top bar's switch and options buttons can't be used (XNA)
        options.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName != nameof(OptionsWindowViewModel.IsOpen))
                return;

            topBar.CanOpenOptions = !options.IsOpen;
            if (!topBar.LanMode)
                topBar.CanSwitch = !options.IsOpen;
        };

        topBar.MainRequested += (_, _) => CurrentPage = primaryPage;
        topBar.CnCNetLobbyRequested += (_, _) => OpenCnCNet();
        topBar.PrivateMessagesRequested += (_, _) => PrivateMessages.Open();
        PrivateMessages.OpenRequested += (_, _) => CurrentPage = PrivateMessages;
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

    public PrivateMessagesViewModel PrivateMessages { get; }

    /// <summary>The options were saved and the user chose to restart the client.</summary>
    public event EventHandler RestartRequested;

    private void OpenOptions()
    {
        if (!Options.IsOpen)
            Options.Open();
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

    partial void OnCurrentPageChanged(object value)
    {
        if (value != PrivateMessages)
            PrivateMessages.Close();
    }

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
            cncnetLobby.RoomEntered += (_, _) =>
            {
                SetPrimary(cncnetLobby.Room, "Game Lobby".L10N("Client:Main:GameLobby"));
                CurrentPage = cncnetLobby.Room;
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
        lanLobby?.Shutdown();
        cncnetLobby?.Shutdown();
    }
}
