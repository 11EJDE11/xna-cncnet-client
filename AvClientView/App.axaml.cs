using System;

using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;

using AvClientView.Services;
using AvClientView.ViewModels;
using AvClientView.Views;

using ClientCore;

using ClientLogic;
using ClientLogic.CnCNet;
using ClientLogic.Lan;
using ClientLogic.Skirmish;

using DTAClient.Domain;
using DTAClient.Online;

using Microsoft.Extensions.DependencyInjection;

namespace AvClientView;

public sealed class App : Application
{
    public static IServiceProvider Services { get; private set; }

    public override void Initialize() => AvaloniaXamlLoader.Load(this);

    public override void OnFrameworkInitializationCompleted()
    {
        // A crash on the UI thread: logged, the log copied to ClientCrashLogs and reported, then the client closes
        // (the XNA client's PreStartup.HandleException)
        Avalonia.Threading.Dispatcher.UIThread.UnhandledException += (_, e) =>
            AvClientView.Services.CrashHandler.HandleException(e.Exception);

        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            AvClientView.Theme.ThemeFonts.Initialize();

            var mainWindow = new MainWindow();
            AvClientView.Theme.ThemedToolTips.Apply(this, mainWindow);
            AvClientView.Theme.ThemedScrollBars.Apply(this);

            Services = new ServiceCollection()
                .AddAvaloniaFrontEnd(mainWindow)
                .AddSingleton(new Random())
                .AddClientLogic()
                .AddSingleton<MainMenuViewModel>()
                .AddSingleton<MainWindowViewModel>()
                .AddSingleton<TopBarViewModel>()
                .AddSingleton<OptionsWindowViewModel>()
                .AddSingleton<HotkeyWindowViewModel>()
                .AddSingleton<CampaignViewModel>()
                .AddSingleton<LoadGameViewModel>()
                .AddSingleton<DiscordHandler>()
                .AddSingleton(sp => WithDiscord(ActivatorUtilities.CreateInstance<SkirmishSession>(sp), sp))
                .AddTransient<SkirmishViewModel>()
                .AddSingleton<DirectDrawWrapperManager>()
                .AddSingleton<LanLobby>()
                .AddSingleton(sp => WithDiscord(ActivatorUtilities.CreateInstance<LanGameRoom>(sp), sp))
                .AddSingleton<LanGameRoomViewModel>()
                .AddSingleton(sp => { var r = ActivatorUtilities.CreateInstance<LanGameLoadingRoom>(sp); r.DiscordHandler = sp.GetRequiredService<DiscordHandler>(); return r; })
                .AddSingleton<LanLobbyViewModel>()
                .AddSingleton(sp => WithDiscord(ActivatorUtilities.CreateInstance<CnCNetGameRoom>(sp), sp))
                .AddSingleton(sp => { var r = ActivatorUtilities.CreateInstance<CnCNetGameLoadingRoom>(sp); r.DiscordHandler = sp.GetRequiredService<DiscordHandler>(); return r; })
                .AddSingleton<CnCNetLobbyService>()
                .AddSingleton<CnCNetGameRoomViewModel>()
                .AddSingleton<CnCNetLobbyViewModel>()
                .AddSingleton<PrivateMessagesViewModel>()
                .AddSingleton<GameInvitationsViewModel>()
                .AddSingleton<ExtrasViewModel>()
                .AddSingleton<StatisticsViewModel>()
                .AddSingleton<UpdaterViewModel>()
                .AddSingleton<GameOptionPresetsViewModel>()
                .BuildServiceProvider();

            // As the XNA client does at start-up: the selected renderer sets the game process's qres and single-core
            // affinity options
            Services.GetRequiredService<DirectDrawWrapperManager>();

            // Discord rich presence, as the XNA client: connected at start-up when enabled, and on saving the settings
            var discord = Services.GetRequiredService<DiscordHandler>();
            UserINISettings.Instance.SettingsSaved += (_, _) => Avalonia.Threading.Dispatcher.UIThread.Post(() =>
            {
                if (UserINISettings.Instance.DiscordIntegration && !ClientConfiguration.Instance.DiscordIntegrationGloballyDisabled)
                    discord.Connect();
                else
                    discord.Disconnect();
            });

            // The game-in-progress state (IsInGame, error logs, settings reload), as the XNA GameInProgressWindow
            var gameInProgress = Services.GetRequiredService<ClientLogic.Launch.GameInProgressTracker>();
            WindowState stateBeforeGame = WindowState.Normal;
            gameInProgress.GameStarted += (_, _) =>
            {
                if (!UserINISettings.Instance.MinimizeWindowsOnGameStart)
                    return;

                stateBeforeGame = mainWindow.WindowState == WindowState.Minimized ? WindowState.Normal : mainWindow.WindowState;
                mainWindow.WindowState = WindowState.Minimized;
            };
            gameInProgress.GameExited += (_, _) =>
            {
                if (UserINISettings.Instance.MinimizeWindowsOnGameStart && mainWindow.WindowState == WindowState.Minimized)
                    mainWindow.WindowState = stateBeforeGame;
            };

            mainWindow.DataContext = Services.GetRequiredService<MainWindowViewModel>();
            desktop.MainWindow = mainWindow;
            desktop.ShutdownRequested += (_, _) =>
            {
                Services.GetRequiredService<MainWindowViewModel>().Shutdown();
                Services.GetRequiredService<CnCNetUserData>().Save();
                Services.GetRequiredService<DiscordHandler>().Dispose();
            };
        }

        base.OnFrameworkInitializationCompleted();
    }

    private static T WithDiscord<T>(T session, IServiceProvider services) where T : ClientLogic.Lobby.LobbySession
    {
        session.DiscordHandler = services.GetRequiredService<DiscordHandler>();
        return session;
    }
}
