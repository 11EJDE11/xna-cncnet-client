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
        ApplyGraphicsMode();
    }

    private LayoutTransformControl scaler;

    /// <summary>
    /// GameClass.SetGraphicsMode: the window is the client resolution; the screens are laid out for the render
    /// resolution and scaled to the window (whole steps with integer scaling), with black bars around them;
    /// borderless windowed, and full screen when the client resolution is the desktop's.
    /// </summary>
    private void ApplyGraphicsMode()
    {
        ClientLogic.Settings.ClientGraphicsMode mode = AvClientView.Theme.ThemeAssets.GraphicsMode;
        int renderWidth = AvClientView.Theme.ThemeAssets.RenderWidth;
        int renderHeight = AvClientView.Theme.ThemeAssets.RenderHeight;

        Width = mode?.WindowWidth ?? renderWidth;
        Height = mode?.WindowHeight ?? renderHeight;

        Grid root = Root;
        Content = null;
        root.Width = renderWidth;
        root.Height = renderHeight;
        root.ClipToBounds = true;
        scaler = new LayoutTransformControl
        {
            Child = root,
            HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Center,
            VerticalAlignment = Avalonia.Layout.VerticalAlignment.Center,
        };
        Content = new Border { Background = Avalonia.Media.Brushes.Black, Child = scaler };

        if (mode != null)
        {
            // XNA's window can be resized only when windowed with integer scaling
            CanResize = !mode.Borderless && mode.IntegerScale;
            if (mode.Borderless)
                WindowDecorations = WindowDecorations.None;
            if (mode.FullScreen)
                WindowState = WindowState.FullScreen;
        }

        SizeChanged += (_, _) => UpdateScale();
    }

    private void UpdateScale()
    {
        double renderWidth = AvClientView.Theme.ThemeAssets.RenderWidth;
        double renderHeight = AvClientView.Theme.ThemeAssets.RenderHeight;
        Avalonia.Size size = ClientSize;
        if (size.Width <= 0 || size.Height <= 0)
            return;

        double scale = Math.Min(size.Width / renderWidth, size.Height / renderHeight);
        if (AvClientView.Theme.ThemeAssets.GraphicsMode?.IntegerScale == true && scale >= 1)
            scale = Math.Floor(scale);

        scaler.LayoutTransform = Math.Abs(scale - 1) < 0.001 ? null : new Avalonia.Media.ScaleTransform(scale, scale);
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
        topBar?.OnCursorMoved(e.GetPosition(Root).Y);
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
                // The loading screen shows neither the top bar nor the privacy notice (XNA adds them afterwards)
                var privacy = new PrivacyNotificationView { ZIndex = 11500 };
                bool privacyPending = privacy.IsVisible;
                privacy.IsVisible = false;
                Root.Children.Add(privacy);
                topBar.IsVisible = !viewModel.IsLoading;
                viewModel.PropertyChanged += (_, e) =>
                {
                    if (e.PropertyName != nameof(MainWindowViewModel.IsLoading) || viewModel.IsLoading)
                        return;

                    topBar.IsVisible = !viewModel.Options.Hotkeys.IsOpen;
                    privacy.IsVisible = privacyPending;
                };
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
