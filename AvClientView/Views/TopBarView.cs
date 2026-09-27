using System;
using System.Collections.Generic;

using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Threading;

using AvClientView.Theme;
using AvClientView.ViewModels;

using ClientCore;
using ClientCore.Extensions;

using ClientLogic.Layout;

namespace AvClientView.Views;

/// <summary>
/// The XNA TopBar: a black 39 px bar across the top that slides down when the cursor is at the top edge (or on
/// start-up and connection events) and back up a second after the cursor leaves it.
/// </summary>
public sealed class TopBarView : Canvas
{
    private const double DOWN_TIME_WAIT_SECONDS = 1.0;
    private const double EVENT_DOWN_TIME_WAIT_SECONDS = 2.0;
    private const double STARTUP_DOWN_TIME_WAIT_SECONDS = 3.5;
    private const double DOWN_MOVEMENT_RATE = 1.7;
    private const double UP_MOVEMENT_RATE = 1.7;
    private const int APPEAR_CURSOR_THRESHOLD_Y = 8;
    private const int BAR_HEIGHT = 39;

    private readonly TopBarViewModel viewModel;
    private readonly Dictionary<string, ThemedButton> buttons = [];
    private readonly TextBlock lblTime;
    private readonly TextBlock lblDate;
    private readonly TextBlock lblConnectionStatus;
    private readonly TextBlock lblCnCNetPlayerCount;
    private readonly DispatcherTimer timer;
    private DateTime lastUpdate = DateTime.Now;

    private TimeSpan downTime = TimeSpan.FromSeconds(DOWN_TIME_WAIT_SECONDS - STARTUP_DOWN_TIME_WAIT_SECONDS);
    private bool isDown = true;
    private double locationY = -40.0;

    public TopBarView(TopBarViewModel viewModel)
    {
        this.viewModel = viewModel;
        int width = ThemeAssets.RenderWidth;
        Width = width;
        Height = BAR_HEIGHT;
        Background = Brushes.Black;
        VerticalAlignment = Avalonia.Layout.VerticalAlignment.Top;
        HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Left;
        RenderTransform = new TranslateTransform(0, locationY);

        XnaLayoutReader reader = ThemedWindow.CreateReader();

        void AddButton(string name, int x, int w, string text, int fontIndex = 0)
        {
            var layout = new LayoutControl(name, "XNAClientButton", LayoutControlKind.Button) { X = x, Y = 9, Width = w, Height = 23, Text = text, FontIndex = fontIndex };
            reader.Initialize(layout);
            var button = new ThemedButton(layout) { Name = name };
            button.Click += (_, _) => viewModel.Press(name);
            SetLeft(button, x);
            SetTop(button, 9);
            Children.Add(button);
            buttons[name] = button;
        }

        TextBlock AddLabel(string text, int fontIndex = 1)
        {
            (FontFamily family, double size) = ThemeFonts.Get(fontIndex);
            var label = new TextBlock { Text = text, FontFamily = family, FontSize = size, Foreground = new SolidColorBrush(ThemeAssets.LabelColor) };
            Children.Add(label);
            return label;
        }

        AddButton("btnMainButton", 12, 160, viewModel.MainButtonText);
        AddButton("btnCnCNetLobby", 184, 160, "CnCNet Lobby (F3)".L10N("Client:Main:LobbyF3"));
        AddButton("btnPrivateMessages", 356, 160, "Private Messages (F4)".L10N("Client:Main:PMButtonF4"));

        lblDate = AddLabel(DateTime.Now.ToShortDateString());
        int dateX = width - ThemeFonts.Measure(lblDate.Text, 1).Width - 12;
        SetLeft(lblDate, dateX);
        SetTop(lblDate, 18);

        lblTime = AddLabel(new DateTime(1, 1, 1, 23, 59, 59).ToLongTimeString());
        SetLeft(lblTime, width - ThemeFonts.Measure(lblTime.Text, 1).Width - 12);
        SetTop(lblTime, 4);

        int logoutX = dateX - 87;
        AddButton("btnLogout", logoutX, 75, "Log Out".L10N("Client:Main:TopBarLogOut"), fontIndex: 1);
        int optionsX = logoutX - 122;
        AddButton("btnOptions", optionsX, 110, "Options (F12)".L10N("Client:Main:OptionsF12"));

        lblConnectionStatus = AddLabel(viewModel.ConnectionStatus);

        if (ClientConfiguration.Instance.DisplayPlayerCountInTopBar)
        {
            lblCnCNetPlayerCount = AddLabel(viewModel.PlayerCount);
            SetLeft(lblCnCNetPlayerCount, optionsX - 50);
            SetTop(lblCnCNetPlayerCount, 11);

            TextBlock lblCnCNetStatus = AddLabel(ClientConfiguration.Instance.LocalGame.ToUpper() + " " + "PLAYERS ONLINE:".L10N("Client:Main:OnlinePlayersNumber"));
            SetLeft(lblCnCNetStatus, optionsX - 50 - ThemeFonts.Measure(lblCnCNetStatus.Text, 1).Width - 6);
            SetTop(lblCnCNetStatus, 11);
        }

        // The panel border line along the bottom
        var line = new Border { Width = width, Height = 1, Background = new SolidColorBrush(ThemeAssets.PanelBorderColor) };
        SetTop(line, BAR_HEIGHT - 2);
        Children.Add(line);

        CenterConnectionStatus();
        Refresh();

        viewModel.PropertyChanged += (_, _) => Refresh();
        viewModel.ConnectionEventOccurred += (_, _) =>
        {
            CenterConnectionStatus();
            isDown = true;
            downTime = TimeSpan.FromSeconds(DOWN_TIME_WAIT_SECONDS - EVENT_DOWN_TIME_WAIT_SECONDS);
        };
        viewModel.BringDownRequested += (_, _) => BringDown();

        timer = new DispatcherTimer(TimeSpan.FromMilliseconds(10), DispatcherPriority.Render, (_, _) => Tick());
        AttachedToVisualTree += (_, _) => timer.Start();
        DetachedFromVisualTree += (_, _) => timer.Stop();
    }

    /// <summary>The cursor moved over the window (at the given height): the bar comes down at the top edge.</summary>
    public void OnCursorMoved(double y)
    {
        if (y < APPEAR_CURSOR_THRESHOLD_Y && y > -1)
            BringDown();
    }

    /// <summary>F1 brings the bar down; F2, F3, F4 and F12 press its buttons.</summary>
    public bool HandleKey(Key key)
    {
        switch (key)
        {
            case Key.F1:
                BringDown();
                return true;
            case Key.F2:
                viewModel.Press("btnMainButton");
                return true;
            case Key.F3:
                viewModel.Press("btnCnCNetLobby");
                return true;
            case Key.F4:
                viewModel.Press("btnPrivateMessages");
                return true;
            case Key.F12:
                viewModel.Press("btnOptions");
                return true;
            default:
                return false;
        }
    }

    protected override void OnPointerMoved(PointerEventArgs e)
    {
        base.OnPointerMoved(e);
        BringDown();
    }

    private void BringDown()
    {
        isDown = true;
        downTime = TimeSpan.Zero;
    }

    private void Refresh()
    {
        buttons["btnMainButton"].Text = viewModel.MainButtonText;
        buttons["btnMainButton"].IsEnabled = viewModel.CanSwitch;
        buttons["btnCnCNetLobby"].IsEnabled = viewModel.CanSwitch;
        buttons["btnPrivateMessages"].IsEnabled = viewModel.CanSwitch;
        buttons["btnOptions"].IsEnabled = viewModel.CanOpenOptions;
        buttons["btnLogout"].IsEnabled = viewModel.CanLogOut;

        if (lblCnCNetPlayerCount != null)
            lblCnCNetPlayerCount.Text = viewModel.PlayerCount;

        if (lblConnectionStatus.Text != viewModel.ConnectionStatus)
        {
            lblConnectionStatus.Text = viewModel.ConnectionStatus;
            CenterConnectionStatus();
        }
    }

    private void CenterConnectionStatus()
    {
        (int w, int h) = ThemeFonts.Measure(lblConnectionStatus.Text, 1);
        SetLeft(lblConnectionStatus, (Width - w) / 2);
        SetTop(lblConnectionStatus, (BAR_HEIGHT - h) / 2);
    }

    /// <summary>TopBar.Update: slide down or up, and update the time and date.</summary>
    private void Tick()
    {
        DateTime now = DateTime.Now;
        TimeSpan elapsed = now - lastUpdate;
        lastUpdate = now;

        if (IsPointerOver)
            BringDown();

        if (isDown)
        {
            if (locationY < 0)
                locationY = Math.Min(0, locationY + DOWN_MOVEMENT_RATE * (elapsed.TotalMilliseconds / 10.0));

            downTime += elapsed;
            isDown = downTime < TimeSpan.FromSeconds(DOWN_TIME_WAIT_SECONDS);
        }
        else if (locationY > -BAR_HEIGHT - 1)
        {
            locationY = Math.Max(-BAR_HEIGHT - 1, locationY - UP_MOVEMENT_RATE * (elapsed.TotalMilliseconds / 10.0));
        }

        ((TranslateTransform)RenderTransform).Y = (int)locationY;
        IsHitTestVisible = locationY > -BAR_HEIGHT - 1;

        lblTime.Text = now.ToLongTimeString();
        string dateText = now.ToShortDateString();
        if (lblDate.Text != dateText)
            lblDate.Text = dateText;
    }
}
