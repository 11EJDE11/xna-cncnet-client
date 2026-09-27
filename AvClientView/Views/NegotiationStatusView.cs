using System;
using System.Collections.Generic;

using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;

using AvClientView.Theme;
using AvClientView.ViewModels;

using ClientCore.Extensions;

using ClientLogic.Layout;
using ClientLogic.Tunnels;

using DTAClient.Domain.Multiplayer.CnCNet;

namespace AvClientView.Views;

/// <summary>
/// The XNA TunnelNegotiationStatusPanel: every pair's negotiation status and ping, as a list (worst first, with ping
/// bars) or a matrix, with Renegotiate All for the host; at the top right of the room.
/// </summary>
public sealed class NegotiationStatusView : Border
{
    private const int CELL_WIDTH = 90;
    private const int CELL_HEIGHT = 25;
    private const int HEADER_HEIGHT = 30;
    private const int PLAYER_NAME_WIDTH_LHS = 120;
    private const int PANEL_PADDING = 15;
    private const int TITLE_HEIGHT = 25;
    private const int CLOSE_BUTTON_SIZE = 20;
    private const int TAB_HEIGHT = 26;
    private const int LIST_PLAYER_COLUMN_WIDTH = 130;
    private const int LIST_BAR_COLUMN_WIDTH = 160;
    private const int LIST_PING_TEXT_COLUMN_WIDTH = 70;
    private const int LIST_BAR_MAX_WIDTH = 150;
    private const int LIST_BAR_HEIGHT = 14;
    private const int LIST_MIN_VISIBLE_ROWS = 3;
    private const int RENEGOTIATE_BUTTON_HEIGHT = 25;
    private const int LIST_TOTAL_WIDTH = LIST_PLAYER_COLUMN_WIDTH * 2 + LIST_BAR_COLUMN_WIDTH + LIST_PING_TEXT_COLUMN_WIDTH;

    private static readonly IBrush PlayerBrush = new SolidColorBrush(Colors.LightBlue);

    private readonly CnCNetGameRoomViewModel viewModel;
    private int selectedTab;

    public NegotiationStatusView(CnCNetGameRoomViewModel viewModel)
    {
        this.viewModel = viewModel;
        HorizontalAlignment = HorizontalAlignment.Right;
        VerticalAlignment = VerticalAlignment.Top;
        Margin = new Thickness(0, 60, 10, 0);
        BorderBrush = new SolidColorBrush(ThemeAssets.PanelBorderColor);
        BorderThickness = new Thickness(1);
        Background = ThemeAssets.LoadBitmap("ModalBG.png") is { } modal
            ? new ImageBrush(modal) { Stretch = Stretch.Fill }
            : ThemedStyle.PanelBackground;
        IsVisible = false;

        void Refreshed(object sender, EventArgs e) => Rebuild();
        AttachedToVisualTree += (_, _) => { viewModel.Refreshed += Refreshed; Rebuild(); };
        DetachedFromVisualTree += (_, _) => viewModel.Refreshed -= Refreshed;
    }

    private static IBrush TextBrush(PingQualityTier tier) => new SolidColorBrush(tier switch
    {
        PingQualityTier.Good => Colors.LightGreen,
        PingQualityTier.Fair => Colors.Yellow,
        PingQualityTier.Poor => Colors.Orange,
        PingQualityTier.Bad => Colors.Red,
        _ => Colors.Gray,
    });

    private static Color BarColor(PingQualityTier tier) => tier switch
    {
        PingQualityTier.Good => Color.FromArgb(200, 0, 180, 0),
        PingQualityTier.Fair => Color.FromArgb(200, 200, 180, 0),
        PingQualityTier.Poor => Color.FromArgb(200, 200, 100, 0),
        PingQualityTier.Bad => Color.FromArgb(200, 200, 0, 0),
        _ => Color.FromArgb(200, 128, 128, 128),
    };

    private void Rebuild()
    {
        IsVisible = viewModel.ShowNegotiationStatus;
        if (!IsVisible)
            return;

        List<string> players = viewModel.NegotiationPlayers;
        int lineHeight = ThemeFonts.Measure("Test String @", 0).Height - 1;
        int listHeaderHeight = ThemeFonts.Measure("Ping", 1).Height + 3;

        // ApplyLayout
        int visiblePlayerCount = Math.Max(players.Count, 2);
        int matrixWidth = PLAYER_NAME_WIDTH_LHS + visiblePlayerCount * CELL_WIDTH + PANEL_PADDING * 2;
        int listWidth = LIST_TOTAL_WIDTH + PANEL_PADDING * 2;
        int matrixContentHeight = HEADER_HEIGHT + visiblePlayerCount * CELL_HEIGHT;
        int listMinContentHeight = listHeaderHeight + LIST_MIN_VISIBLE_ROWS * lineHeight + 4;
        int width = Math.Max(500, Math.Max(matrixWidth, listWidth));
        int contentY = TITLE_HEIGHT + TAB_HEIGHT + PANEL_PADDING;
        int height = Math.Max(300, contentY + Math.Max(matrixContentHeight, listMinContentHeight) + PANEL_PADDING + RENEGOTIATE_BUTTON_HEIGHT + PANEL_PADDING);
        int buttonY = height - RENEGOTIATE_BUTTON_HEIGHT - PANEL_PADDING;
        int contentHeight = Math.Max(0, buttonY - PANEL_PADDING - contentY);

        Width = width;
        Height = height;
        var canvas = new Canvas { Width = width, Height = height };

        (FontFamily titleFamily, double titleSize) = ThemeFonts.Get(1);
        string title = "Tunnel Negotiation Status".L10N("Client:Main:NegStatusTitle");
        (int titleWidth, int titleHeight) = ThemeFonts.Measure(title, 1);
        Place(canvas, new TextBlock { Text = title, FontFamily = titleFamily, FontSize = titleSize, Foreground = new SolidColorBrush(ThemeAssets.LabelColor) },
            (width - titleWidth) / 2, TITLE_HEIGHT / 2 + 2 - titleHeight / 2);

        ThemedButton close = Button("btnClose", string.Empty, CLOSE_BUTTON_SIZE, CLOSE_BUTTON_SIZE, "optionsButtonClose.png", "optionsButtonClose_c.png");
        close.Click += (_, _) => viewModel.CloseNegotiationStatus();
        Place(canvas, close, width - CLOSE_BUTTON_SIZE - 8, 5);

        var tabs = new ThemedTabControl(1);
        tabs.AddTab("List".L10N("Client:Main:NegStatusTabList"), 92);
        tabs.AddTab("Matrix".L10N("Client:Main:NegStatusTabMatrix"), 92);
        tabs.SelectedTab = selectedTab;
        tabs.PropertyChanged += (_, e) =>
        {
            if (e.Property == ThemedTabControl.SelectedTabProperty)
            {
                selectedTab = tabs.SelectedTab;
                Rebuild();
            }
        };
        Place(canvas, tabs, PANEL_PADDING, TITLE_HEIGHT + 3);

        Control content = selectedTab == 0
            ? BuildList(lineHeight, listHeaderHeight, contentHeight)
            : BuildMatrix(players, width - PANEL_PADDING * 2, contentHeight);
        Place(canvas, content, PANEL_PADDING, contentY);

        if (viewModel.IsHost)
        {
            ThemedButton renegotiate = Button("btnRenegotiateAll", "Renegotiate All".L10N("Client:Main:RenegotiateAll"), 160, RENEGOTIATE_BUTTON_HEIGHT);
            renegotiate.Click += (_, _) => viewModel.RenegotiateAll();
            Place(canvas, renegotiate, PANEL_PADDING, buttonY);
        }

        Child = canvas;
    }

    private static void Place(Canvas canvas, Control control, double x, double y)
    {
        Canvas.SetLeft(control, x);
        Canvas.SetTop(control, y);
        canvas.Children.Add(control);
    }

    private static ThemedButton Button(string name, string text, int width, int height, string idle = null, string hover = null)
    {
        var layout = new LayoutControl(name, "XNAClientButton", LayoutControlKind.Button) { Width = width, Height = height, Text = text };
        ThemedWindow.CreateReader().Initialize(layout);
        if (idle != null)
        {
            layout.IdleTexture = idle;
            layout.HoverTexture = hover;
        }

        return new ThemedButton(layout);
    }

    private Control BuildList(int lineHeight, int headerHeight, int height)
    {
        (FontFamily headerFamily, double headerSize) = ThemeFonts.Get(1);
        var borderBrush = new SolidColorBrush(ThemeAssets.PanelBorderColor);
        (string Header, int Width)[] columns =
        [
            ("Player 1".L10N("Client:Main:NegStatusPlayer1Header"), LIST_PLAYER_COLUMN_WIDTH),
            ("Player 2".L10N("Client:Main:NegStatusPlayer2Header"), LIST_PLAYER_COLUMN_WIDTH),
            ("Ping".L10N("Client:Main:PingHeader"), LIST_BAR_COLUMN_WIDTH),
            (string.Empty, LIST_PING_TEXT_COLUMN_WIDTH),
        ];

        var headers = new StackPanel { Orientation = Orientation.Horizontal };
        foreach ((string header, int columnWidth) in columns)
        {
            headers.Children.Add(new Border
            {
                Width = columnWidth,
                Height = headerHeight,
                BorderBrush = borderBrush,
                BorderThickness = new Thickness(1),
                Child = new TextBlock { Text = header, FontFamily = headerFamily, FontSize = headerSize, Foreground = new SolidColorBrush(ThemeAssets.LabelColor), Margin = new Thickness(3, 2, 0, 0) },
            });
        }

        var rows = new StackPanel();
        foreach (NegotiationPairRow row in viewModel.NegotiationRows)
        {
            var grid = new Grid { Height = lineHeight, ColumnDefinitions = new ColumnDefinitions("130,130,160,70") };
            grid.Children.Add(new TextBlock { Text = row.Player1, Foreground = PlayerBrush, Margin = new Thickness(3, 0), VerticalAlignment = VerticalAlignment.Center });
            var p2 = new TextBlock { Text = row.Player2, Foreground = PlayerBrush, Margin = new Thickness(3, 0), VerticalAlignment = VerticalAlignment.Center };
            Grid.SetColumn(p2, 1);
            grid.Children.Add(p2);

            if (row.BarPing is int ms)
            {
                int barHeight = Math.Min(LIST_BAR_HEIGHT, lineHeight - 2);
                int fillWidth = Math.Max(2, Math.Min(LIST_BAR_MAX_WIDTH, ms * LIST_BAR_MAX_WIDTH / NegotiationStatusRows.BAR_MAX_PING));
                var bar = new Canvas { Width = LIST_BAR_MAX_WIDTH, Height = barHeight, Margin = new Thickness(2, 0), HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Center };
                bar.Children.Add(new Border { Width = LIST_BAR_MAX_WIDTH, Height = barHeight, Background = new SolidColorBrush(Color.FromArgb(120, 30, 30, 30)) });
                bar.Children.Add(new Border { Width = fillWidth, Height = barHeight, Background = new SolidColorBrush(BarColor(PingQualityRules.GetV3Tier(ms))) });
                Grid.SetColumn(bar, 2);
                grid.Children.Add(bar);
            }

            var text = new TextBlock { Text = row.Text, Foreground = TextBrush(row.TextTier), Margin = new Thickness(3, 0), VerticalAlignment = VerticalAlignment.Center };
            Grid.SetColumn(text, 3);
            grid.Children.Add(text);
            rows.Children.Add(grid);
        }

        return new Border
        {
            Width = LIST_TOTAL_WIDTH,
            Height = height,
            Background = new SolidColorBrush(Color.FromArgb(128, 0, 0, 0)),
            Child = new DockPanel
            {
                Children =
                {
                    new Border { [DockPanel.DockProperty] = Dock.Top, Child = headers },
                    new ScrollViewer { Content = rows },
                },
            },
        };
    }

    private Control BuildMatrix(List<string> players, int width, int height)
    {
        var canvas = new Canvas { Width = width, Height = height };
        if (players.Count < 2)
            return canvas;

        for (int i = 0; i < players.Count; i++)
        {
            int nameWidth = ThemeFonts.Measure(players[i], 0).Width;
            Place(canvas, new TextBlock { Text = players[i], Foreground = PlayerBrush },
                PLAYER_NAME_WIDTH_LHS + i * CELL_WIDTH + (CELL_WIDTH - nameWidth) / 2, HEADER_HEIGHT / 2 - 7);
        }

        for (int i = 0; i < players.Count; i++)
        {
            Place(canvas, new TextBlock { Text = players[i], Foreground = PlayerBrush, Width = PLAYER_NAME_WIDTH_LHS - 5, Height = CELL_HEIGHT },
                0, HEADER_HEIGHT + i * CELL_HEIGHT);

            for (int j = 0; j < players.Count; j++)
            {
                if (i == j)
                    continue;

                NegotiationPairRow pair = viewModel.NegotiationPair(players[i], players[j]);
                var cell = new Border
                {
                    Width = CELL_WIDTH,
                    Height = CELL_HEIGHT,
                    Background = new SolidColorBrush(Color.FromArgb(120, 30, 30, 30)),
                    BorderBrush = new SolidColorBrush(ThemeAssets.PanelBorderColor),
                    BorderThickness = new Thickness(1),
                    Child = new TextBlock
                    {
                        Text = pair.Text,
                        Foreground = TextBrush(pair.TextTier),
                        HorizontalAlignment = HorizontalAlignment.Center,
                        VerticalAlignment = VerticalAlignment.Center,
                    },
                };
                Place(canvas, cell, PLAYER_NAME_WIDTH_LHS + j * CELL_WIDTH, HEADER_HEIGHT + i * CELL_HEIGHT);
            }
        }

        return canvas;
    }
}
