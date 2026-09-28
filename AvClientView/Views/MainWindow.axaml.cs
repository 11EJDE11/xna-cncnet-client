using System;

using Avalonia.Controls;

using AvClientView.ViewModels;

using Microsoft.Extensions.DependencyInjection;

namespace AvClientView.Views;

public partial class MainWindow : Window
{
    public MainWindow()
    {
        InitializeComponent();

        // The XNA client's window size (its render resolution), so the theme's layouts come out the same
        Width = AvClientView.Theme.ThemeAssets.RenderWidth;
        Height = AvClientView.Theme.ThemeAssets.RenderHeight;
    }

    private TopBarView topBar;

    /// <summary>Restarts the client (WindowManager.RestartGame): starts a new instance, then closes this one.</summary>
    private void Restart()
    {
        string exe = Environment.ProcessPath;
        if (exe != null)
        {
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(exe)
            {
                WorkingDirectory = Environment.CurrentDirectory,
                UseShellExecute = false,
            });
        }

        Close();
    }

    protected override void OnPointerMoved(Avalonia.Input.PointerEventArgs e)
    {
        base.OnPointerMoved(e);
        topBar?.OnCursorMoved(e.GetPosition(this).Y);
    }

    protected override void OnKeyDown(Avalonia.Input.KeyEventArgs e)
    {
        base.OnKeyDown(e);
        if (!e.Handled && topBar != null && topBar.HandleKey(e.Key))
            e.Handled = true;

        if (!e.Handled && e.KeyModifiers == Avalonia.Input.KeyModifiers.None && DataContext is MainWindowViewModel viewModel &&
            viewModel.HandleMainMenuHotkey(e.Key))
            e.Handled = true;
    }

    protected override void OnDataContextChanged(EventArgs e)
    {
        base.OnDataContextChanged(e);

        if (DataContext is MainWindowViewModel viewModel)
        {
            viewModel.ExitRequested += (_, _) => viewModel.Music.FadeOutAndExit(Close);
            Opened += (_, _) => viewModel.RunStartupChecks();

            if (topBar == null)
            {
                Root.Children.Add(new CampaignView(viewModel.Campaign) { ZIndex = 8500 });
                Root.Children.Add(new CampaignTagSelectorView(viewModel.CampaignTagSelector) { ZIndex = 8500 });
                Root.Children.Add(new LoadGameView(viewModel.LoadGame) { ZIndex = 8500 });
                Root.Children.Add(new ExtrasView(viewModel.Extras) { ZIndex = 8500 });
                Root.Children.Add(new StatisticsView(viewModel.Statistics) { ZIndex = 8500 });
                Root.Children.Add(new UpdaterView(viewModel.Updater) { ZIndex = 8700 });
                Root.Children.Add(new GameOptionPresetsView(App.Services.GetRequiredService<GameOptionPresetsViewModel>()) { ZIndex = 8600 });
                Root.Children.Add(new OptionsWindowView(viewModel.Options) { ZIndex = 9000 });
                Root.Children.Add(new HotkeyWindowView(viewModel.Options.Hotkeys) { ZIndex = 11000 });
                topBar = new TopBarView(viewModel.TopBar) { ZIndex = 10000 };
                Root.Children.Add(topBar);

                Root.Children.Add(new PrivateMessagesOverlay(viewModel.PrivateMessages) { ZIndex = 9500 });
                Root.Children.Add(new PrivateMessageNotificationView(viewModel.PrivateMessages) { ZIndex = 12000 });
                Root.Children.Add(new PrivacyNotificationView { ZIndex = 11500 });
                Root.Children.Add(new GameInProgressView(App.Services.GetRequiredService<ClientLogic.Launch.GameInProgressTracker>()) { ZIndex = 13000 });
                Root.Children.Add(new GameInvitationsView(App.Services.GetRequiredService<GameInvitationsViewModel>()) { ZIndex = 12000 });

                // The XNA options disable the top bar while the hotkey window is open
                viewModel.Options.Hotkeys.PropertyChanged += (_, e) =>
                {
                    if (e.PropertyName == nameof(HotkeyWindowViewModel.IsOpen))
                        topBar.IsVisible = !viewModel.Options.Hotkeys.IsOpen;
                };
                viewModel.RestartRequested += (_, _) => Restart();

                // Updater_Restart: the second-stage updater restarts the client once it has replaced its files
                viewModel.UpdaterRestartRequested += (_, _) => Close();
            }
        }
    }
}
