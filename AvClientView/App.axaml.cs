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
        // A crash on the UI thread is written to the client log first
        Avalonia.Threading.Dispatcher.UIThread.UnhandledException += (_, e) =>
            Rampastring.Tools.Logger.Log("Unhandled exception on the UI thread: " + e.Exception);

        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            AvClientView.Theme.ThemeFonts.Initialize();

            var mainWindow = new MainWindow();

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
                .AddSingleton<SkirmishSession>()
                .AddTransient<SkirmishViewModel>()
                .AddSingleton<DirectDrawWrapperManager>()
                .AddSingleton<LanLobby>()
                .AddSingleton<LanGameRoom>()
                .AddSingleton<LanGameRoomViewModel>()
                .AddSingleton<LanLobbyViewModel>()
                .AddSingleton<CnCNetGameRoom>()
                .AddSingleton<CnCNetLobbyService>()
                .AddSingleton<CnCNetGameRoomViewModel>()
                .AddSingleton<CnCNetLobbyViewModel>()
                .AddSingleton<PrivateMessagesViewModel>()
                .AddSingleton<GameInvitationsViewModel>()
                .AddSingleton<ExtrasViewModel>()
                .AddSingleton<StatisticsViewModel>()
                .BuildServiceProvider();

            // As the XNA client does at start-up: the selected renderer sets the game process's qres and single-core
            // affinity options
            Services.GetRequiredService<DirectDrawWrapperManager>();

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
            };
        }

        base.OnFrameworkInitializationCompleted();
    }
}
