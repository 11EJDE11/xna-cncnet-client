using System;

using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;

using AvClientView.Services;
using AvClientView.ViewModels;
using AvClientView.Views;

using ClientLogic;

using DTAClient.Online;

using Microsoft.Extensions.DependencyInjection;

namespace AvClientView;

public sealed class App : Application
{
    public static IServiceProvider Services { get; private set; }

    public override void Initialize() => AvaloniaXamlLoader.Load(this);

    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            var mainWindow = new MainWindow();

            Services = new ServiceCollection()
                .AddAvaloniaFrontEnd(mainWindow)
                .AddSingleton(new Random())
                .AddClientLogic()
                .AddSingleton<MainMenuViewModel>()
                .BuildServiceProvider();

            mainWindow.DataContext = Services.GetRequiredService<MainMenuViewModel>();
            desktop.MainWindow = mainWindow;
            desktop.ShutdownRequested += (_, _) => Services.GetRequiredService<CnCNetUserData>().Save();
        }

        base.OnFrameworkInitializationCompleted();
    }
}
