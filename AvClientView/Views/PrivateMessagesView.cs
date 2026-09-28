using System;
using System.Collections.Generic;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Controls.Templates;
using Avalonia.Data;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Styling;
using AvClientView.Theme;
using AvClientView.ViewModels;
using ClientCore.Extensions;
using ClientLogic.Layout;
using DTAClient.Domain.Multiplayer.CnCNet;
using DTAClient.Online;
using Microsoft.Extensions.DependencyInjection;

namespace AvClientView.Views;

/// <summary>PrivateMessagingWindow, using its theme layout and the four XNA player-list tabs.</summary>
public sealed class PrivateMessagesView : UserControl
{
    public PrivateMessagesView()
    {
        DataContextChanged += (_, _) =>
        {
            if (DataContext is PrivateMessagesViewModel model)
                Content = Build(model);
        };
    }

    private static Control Build(PrivateMessagesViewModel model)
    {
        var window = new LayoutControl("PrivateMessagingWindow", "XNAWindow", LayoutControlKind.Panel)
        {
            Width = 600, Height = 600, BackgroundTexture = "privatemessagebg.png",
        };
        string title = "PRIVATE MESSAGING".L10N("Client:Main:PMLabel");
        var (w, h) = ThemeFonts.Measure(title, 1);
        ThemedWindow.Add(window, "lblPrivateMessaging", "XNALabel", (600 - w) / 2, 12, w, h, title).FontIndex = 1;
        int tabHeight = ThemeAssets.TextureSize("133pxtab.png")?.Height ?? 23;
        ThemedWindow.Add(window, "tabControl", "XNAClientTabControl", 34, 50, 532, tabHeight).FontIndex = 1;
        var players = ThemedWindow.Add(window, "lblPlayers", "XNALabel", 12, 50 + tabHeight + 24, 0, 0, "PLAYERS:".L10N("Client:Main:Players"));
        players.FontIndex = 1;
        int listY = players.Y + ThemeFonts.Measure(players.Text, 1).Height + 6;
        int listHeight = 600 - listY - 12;
        ThemedWindow.Add(window, "lbUserList", "XNAListBox", 12, listY, 150, listHeight);
        ThemedWindow.Add(window, "lblMessages", "XNALabel", 174, players.Y, 0, 0, "MESSAGES:".L10N("Client:Main:Messages")).FontIndex = 1;
        ThemedWindow.Add(window, "lbMessages", "ChatListBox", 174, listY, 414, listHeight - 25);
        ThemedWindow.Add(window, "tbMessageInput", "XNATextBox", 174, listY + listHeight - 19, 414, 19);

        var root = ThemedWindow.Build(window, (_, _) => { }, new Dictionary<string, Func<LayoutControl, Control>>
        {
            ["tabControl"] = layout =>
            {
                var tabs = new ThemedTabControl(layout.FontIndex) { DataContext = model };
                tabs.AddTab("Messages".L10N("Client:Main:MessagesTab"), 133);
                tabs.AddTab("Friend List".L10N("Client:Main:FriendListTab"), 133);
                tabs.AddTab("All Players".L10N("Client:Main:AllPlayersTab"), 133);
                tabs.AddTab("Recent Players".L10N("Client:Main:RecentPlayersTab"), 133);
                tabs.Bind(ThemedTabControl.SelectedTabProperty, new Binding(nameof(model.SelectedTab)) { Mode = BindingMode.TwoWay });
                return tabs;
            },
            ["lbUserList"] = layout =>
            {
                ListBox list = null;
                list = ThemedWindow.List(layout, model, nameof(model.Users), new FuncDataTemplate<PrivateMessagePlayer>((player, _) =>
                {
                    var row = PlayerRow(player);
                    row.PointerPressed += (_, e) =>
                    {
                        if (player == null || !e.GetCurrentPoint(row).Properties.IsRightButtonPressed)
                            return;
                        list.SelectedItem = player;
                        ThemedContextMenu.Open(list, e.GetPosition(list), model.PlayerMenu(player.Name, allowInvite: true));
                        e.Handled = true;
                    };
                    row.DoubleTapped += (_, _) =>
                    {
                        if (player != null)
                            model.OpenConversation(player.Name);
                    };
                    return row;
                }));
                list.Bind(SelectingItemsControl.SelectedItemProperty, new Binding(nameof(model.SelectedUser)) { Mode = BindingMode.TwoWay });
                list.Bind(IsVisibleProperty, new Binding("!ShowRecentPlayers"));
                return list;
            },
            ["lbMessages"] = layout =>
            {
                var list = ThemedWindow.ChatList(layout, model);
                list.Bind(IsVisibleProperty, new Binding("!ShowRecentPlayers"));
                return list;
            },
            ["tbMessageInput"] = layout =>
            {
                var input = ThemedWindow.ChatInput(layout, model, model.SendCommand, string.Empty);
                input.MaxLength = 200;
                input.Bind(IsEnabledProperty, new Binding(nameof(model.CanSend)));
                input.Bind(IsVisibleProperty, new Binding("!ShowRecentPlayers"));
                void FocusInput(object sender, EventArgs e) => input.Focus();
                input.AttachedToVisualTree += (_, _) => { model.OpenRequested += FocusInput; input.Focus(); };
                input.DetachedFromVisualTree += (_, _) => model.OpenRequested -= FocusInput;
                return input;
            },
        });

        var userLayout = window.Find("lbUserList");
        var messageLayout = window.Find("lbMessages");
        var recent = ThemedStyle.List(messageLayout.X + messageLayout.Width - userLayout.X, userLayout.Height - 23);
        recent.Styles.Add(new Avalonia.Styling.Style(selector => selector.OfType<ListBoxItem>())
        {
            Setters =
            {
                new Avalonia.Styling.Setter(TemplatedControl.MinHeightProperty, 0.0),
                new Avalonia.Styling.Setter(TemplatedControl.PaddingProperty, new Thickness(0)),
                new Avalonia.Styling.Setter(ContentControl.HorizontalContentAlignmentProperty, HorizontalAlignment.Stretch),
            },
        });
        recent.DataContext = model;
        recent.Bind(ItemsControl.ItemsSourceProperty, new Binding(nameof(model.RecentPlayers)));
        recent.Bind(IsVisibleProperty, new Binding(nameof(model.ShowRecentPlayers)));
        recent.ItemTemplate = new FuncDataTemplate<RecentPlayer>((player, _) =>
        {
            var row = new Grid { ColumnDefinitions = new ColumnDefinitions("*,*,*"), Background = Brushes.Transparent };
            if (player == null)
                return row;
            AddCell(row, player.PlayerName, 0);
            AddCell(row, player.GameName, 1);
            AddCell(row, player.GameTime.ToLocalTime().ToString("ddd, MMM d, yyyy @ h:mm tt"), 2);
            foreach (TextBlock cell in row.Children)
                cell.Foreground = new SolidColorBrush(model.IsPlayerOnline(player.PlayerName) ? ThemeAssets.ButtonTextColor : ThemeAssets.DisabledItemColor);
            row.PointerPressed += (_, e) =>
            {
                if (e.GetCurrentPoint(row).Properties.IsRightButtonPressed)
                {
                    ThemedContextMenu.Open(row, e.GetPosition(row), model.PlayerMenu(player.PlayerName));
                    e.Handled = true;
                }
            };
            return row;
        });
        Canvas.SetLeft(recent, userLayout.X);
        Canvas.SetTop(recent, userLayout.Y + 23);
        recent.ZIndex = 1001;
        root.Children.Add(recent);
        var headers = new Grid { Width = recent.Width, Height = 23, ColumnDefinitions = new ColumnDefinitions("*,*,*"), DataContext = model, ZIndex = 1001 };
        AddCell(headers, "Player".L10N("Client:Main:RecentPlayerPlayer"), 0);
        AddCell(headers, "Game".L10N("Client:Main:RecentPlayerGame"), 1);
        AddCell(headers, "Date/Time".L10N("Client:Main:RecentPlayerDateTime"), 2);
        headers.Bind(IsVisibleProperty, new Binding(nameof(model.ShowRecentPlayers)));
        Canvas.SetLeft(headers, userLayout.X);
        Canvas.SetTop(headers, userLayout.Y);
        root.Children.Add(headers);
        var playersLabel = LayoutView.FindNamed<TextBlock>(root, "lblPlayers");
        void RefreshLabel(object sender, System.ComponentModel.PropertyChangedEventArgs e)
        {
            if (playersLabel != null)
                playersLabel.Text = model.ShowRecentPlayers ? "RECENT PLAYERS:".L10N("Client:Main:RecentPlayers") : "PLAYERS:".L10N("Client:Main:Players");
        }
        root.AttachedToVisualTree += (_, _) => { model.PropertyChanged += RefreshLabel; RefreshLabel(null, null); };
        root.DetachedFromVisualTree += (_, _) => model.PropertyChanged -= RefreshLabel;
        var label = LayoutView.FindNamed<TextBlock>(root, "lblMessages");
        if (label != null)
        {
            label.DataContext = model;
            label.Bind(IsVisibleProperty, new Binding("!ShowRecentPlayers"));
        }
        return new Viewbox { Stretch = Stretch.Uniform, Child = root, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
    }

    private static void AddCell(Grid row, string text, int column)
    {
        var cell = new TextBlock { Text = text, Margin = new Thickness(3, 1), TextTrimming = TextTrimming.CharacterEllipsis };
        Grid.SetColumn(cell, column);
        row.Children.Add(cell);
    }

    private static Control PlayerRow(PrivateMessagePlayer player)
    {
        var row = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 3, Background = Brushes.Transparent, Margin = new Thickness(2, 1) };
        if (player == null)
            return row;
        var games = App.Services.GetRequiredService<GameCollection>().GameList;
        if (player.Online)
        {
            var icon = player.GameId >= 0 && player.GameId < games.Count ? ThemeAssets.GameIcon(games[player.GameId]) : ThemeAssets.EmbeddedIcon("unknownicon.png");
            row.Children.Add(new Image { Source = icon, Stretch = Stretch.None });
        }
        row.Children.Add(new TextBlock { Text = player.Name, Foreground = new SolidColorBrush(player.Online ? ThemeAssets.ButtonTextColor : ThemeAssets.DisabledItemColor) });
        return row;
    }
}
