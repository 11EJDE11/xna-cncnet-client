using System;
using System.Collections.Generic;

using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Controls.Templates;
using Avalonia.Data;
using Avalonia.Input;
using Avalonia.Media;

using AvClientView.Theme;
using AvClientView.ViewModels;

using ClientLogic.Layout;

using DTAClient.Domain.Multiplayer.CnCNet;

using Rampastring.Tools;

namespace AvClientView.Views;

/// <summary>
/// The CnCNet lobby, laid out as the XNA CnCNetLobby creates its controls, with the theme's CnCNetLobby.ini applied.
/// The login, game creation and password panels are drawn over it.
/// </summary>
public partial class CnCNetLobbyView : UserControl
{
    public CnCNetLobbyView()
    {
        InitializeComponent();
        DataContextChanged += (_, _) => Build();
    }

    private void Build()
    {
        if (DataContext is not CnCNetLobbyViewModel viewModel)
            return;

        try
        {
            LobbyHost.Content = ThemedWindow.Build(CreateLayout(), (name, layout) => OnButton(viewModel, name, layout),
                new Dictionary<string, Func<LayoutControl, Control>>
                {
                    ["lbGameList"] = layout => GameList(layout, viewModel),
                    ["lbPlayerList"] = layout => ThemedWindow.List(layout, viewModel, nameof(CnCNetLobbyViewModel.Users),
                        new FuncDataTemplate<UserItemViewModel>((user, _) => new TextBlock { Text = user?.ToString(), Foreground = user?.Brush, Margin = new Thickness(4, 1) })),
                    ["lbChatMessages"] = layout => ThemedWindow.ChatList(layout, viewModel),
                    ["tbChatInput"] = layout => ThemedWindow.ChatInput(layout, viewModel, viewModel.SendChatCommand),
                    ["ddColor"] = layout => DropDown(layout, viewModel, nameof(CnCNetLobbyViewModel.ChatColors), nameof(CnCNetLobbyViewModel.SelectedChatColorIndex)),
                    ["ddCurrentChannel"] = layout => DropDown(layout, viewModel, nameof(CnCNetLobbyViewModel.ChatChannels), nameof(CnCNetLobbyViewModel.SelectedChannelIndex)),
                    ["tbGameSearch"] = layout => ThemedStyle.TextBox(layout.Width, layout.Height, "Filter by name, map, game mode, player..."),
                });

            LoginHost.Content = BuildLoginWindow(viewModel);
            CreateHost.Content = BuildGameCreationWindow(viewModel);
            PasswordHost.Content = BuildPasswordWindow(viewModel);

            if (LobbyHost.Content is Canvas root && LayoutView.FindNamed<TextBlock>(root, "lblOnlineCount") is TextBlock onlineCount)
            {
                onlineCount.DataContext = viewModel;
                onlineCount.Bind(TextBlock.TextProperty, new Binding(nameof(CnCNetLobbyViewModel.OnlineCount)));
            }
        }
        catch (Exception ex)
        {
            Logger.Log("CnCNetLobbyView: building the themed lobby failed: " + ex);
            LobbyHost.Content = new TextBlock { Text = "The theme's CnCNet lobby could not be loaded: " + ex.Message };
        }
    }

    /// <summary>The controls CnCNetLobby.Initialize creates, at its positions.</summary>
    private static LayoutControl CreateLayout()
    {
        var window = new LayoutControl("CnCNetLobby", "XNAWindow", LayoutControlKind.Panel)
        {
            Width = ThemedWindow.RenderWidth - 64,
            Height = ThemedWindow.RenderHeight - 64,
            BackgroundTexture = "cncnetlobbybg.png",
        };

        LayoutControl btnNewGame = ThemedWindow.Add(window, "btnNewGame", "XNAClientButton", 12, window.Height - 29, 133, 23, "Create Game");
        LayoutControl btnJoinGame = ThemedWindow.Add(window, "btnJoinGame", "XNAClientButton", btnNewGame.X + btnNewGame.Width + 12, btnNewGame.Y, 133, 23, "Join Game");
        ThemedWindow.Add(window, "btnLogout", "XNAClientButton", window.Width - 145, btnNewGame.Y, 133, 23, "Log Out");

        LayoutControl lbGameList = ThemedWindow.Add(window, "lbGameList", "GameListBox", btnNewGame.X, 41,
            btnJoinGame.X + btnJoinGame.Width - btnNewGame.X, btnNewGame.Y - 47);
        LayoutControl lbPlayerList = ThemedWindow.Add(window, "lbPlayerList", "PlayerListBox", window.Width - 202, lbGameList.Y, 190, lbGameList.Height);
        int gameListRight = lbGameList.X + lbGameList.Width;
        LayoutControl lbChatMessages = ThemedWindow.Add(window, "lbChatMessages", "ChatListBox", gameListRight + 12, lbGameList.Y,
            lbPlayerList.X - gameListRight - 24, lbPlayerList.Height);
        ThemedWindow.Add(window, "tbChatInput", "XNAChatTextBox", lbChatMessages.X, btnNewGame.Y, lbChatMessages.Width, btnNewGame.Height);

        LayoutControl lblColor = ThemedWindow.Add(window, "lblColor", "XNALabel", lbChatMessages.X, 14, 0, 0, "YOUR COLOR:");
        lblColor.FontIndex = 1;
        LayoutControl ddColor = ThemedWindow.Add(window, "ddColor", "XNAClientDropDown", lblColor.X + 95, 12, 150, 21);

        LayoutControl ddCurrentChannel = ThemedWindow.Add(window, "ddCurrentChannel", "XNAClientDropDown",
            lbChatMessages.X + lbChatMessages.Width - 200, ddColor.Y, 200, 21);
        LayoutControl lblCurrentChannel = ThemedWindow.Add(window, "lblCurrentChannel", "XNALabel", ddCurrentChannel.X - 150, ddCurrentChannel.Y + 2, 0, 0, "CURRENT CHANNEL:");
        lblCurrentChannel.FontIndex = 1;

        LayoutControl lblOnline = ThemedWindow.Add(window, "lblOnline", "XNALabel", 310, 14, 0, 0, "Online:");
        lblOnline.FontIndex = 1;
        lblOnline.Visible = false;
        LayoutControl lblOnlineCount = ThemedWindow.Add(window, "lblOnlineCount", "XNALabel", lblOnline.X + 50, 14, 0, 0, "-");
        lblOnlineCount.FontIndex = 1;
        lblOnlineCount.Visible = false;

        LayoutControl tbGameSearch = ThemedWindow.Add(window, "tbGameSearch", "XNASuggestionTextBox", lbGameList.X, 12, lbGameList.Width - 62, 21);
        tbGameSearch.Visible = false;

        return window;
    }

    /// <summary>The login window, laid out as the XNA CnCNetLoginWindow, with the theme's INI applied.</summary>
    private static Control BuildLoginWindow(CnCNetLobbyViewModel viewModel)
    {
        var window = new LayoutControl("CnCNetLoginWindow", "XNAWindow", LayoutControlKind.Panel)
        {
            Width = 300,
            Height = 220,
            BackgroundTexture = "logindialogbg.png",
        };

        string title = "CONNECT TO CNCNET";
        (int titleWidth, int titleHeight) = ThemeFonts.Measure(title, 1);
        LayoutControl lblTitle = ThemedWindow.Add(window, "lblConnectToCnCNet", "XNALabel", (window.Width - titleWidth) / 2, 12, titleWidth, titleHeight, title);
        lblTitle.FontIndex = 1;

        LayoutControl tbPlayerName = ThemedWindow.Add(window, "tbPlayerName", "XNATextBox", window.Width - 132, 50, 120, 19);
        LayoutControl lblPlayerName = ThemedWindow.Add(window, "lblPlayerName", "XNALabel", 12, tbPlayerName.Y + 1, 0, 0, "PLAYER NAME:");
        lblPlayerName.FontIndex = 1;

        // The check boxes are placed before their size is known, as in XNA (hence the 30-pixel steps)
        LayoutControl chkRememberMe = ThemedWindow.Add(window, "chkRememberMe", "XNAClientCheckBox", 12, tbPlayerName.Y + tbPlayerName.Height + 12, 0, 0, "Remember me");
        LayoutControl chkPersistentMode = ThemedWindow.Add(window, "chkPersistentMode", "XNAClientCheckBox", 12, chkRememberMe.Y + 30, 0, 0, "Stay connected outside of the CnCNet lobby");
        ThemedWindow.Add(window, "chkAutoConnect", "XNAClientCheckBox", 12, chkPersistentMode.Y + 30, 0, 0, "Connect automatically on client startup");

        LayoutControl btnConnect = ThemedWindow.Add(window, "btnConnect", "XNAClientButton", 12, window.Height - 35, 110, 23, "Connect");
        ThemedWindow.Add(window, "btnCancel", "XNAClientButton", window.Width - 122, btnConnect.Y, 110, 23, "Cancel");

        Control CheckBox(LayoutControl layout, string property, string enabledProperty = null)
        {
            var checkBox = new ThemedCheckBox(layout.Text) { DataContext = viewModel };
            checkBox.Bind(ThemedCheckBox.IsCheckedProperty, new Binding(property) { Mode = BindingMode.TwoWay });
            if (enabledProperty != null)
                checkBox.Bind(InputElement.IsEnabledProperty, new Binding(enabledProperty));
            return checkBox;
        }

        return ThemedWindow.Build(window, (name, layout) =>
            {
                if (name == "btnConnect")
                    viewModel.ConnectCommand.Execute(null);
                else if (name == "btnCancel")
                    viewModel.BackCommand.Execute(null);
            },
            new Dictionary<string, Func<LayoutControl, Control>>
            {
                ["tbPlayerName"] = layout =>
                {
                    TextBox textBox = ThemedStyle.TextBox(layout.Width, layout.Height, string.Empty);
                    textBox.DataContext = viewModel;
                    textBox.Bind(TextBox.TextProperty, new Binding(nameof(CnCNetLobbyViewModel.PlayerName)) { Mode = BindingMode.TwoWay });
                    textBox.KeyBindings.Add(new Avalonia.Input.KeyBinding { Gesture = new Avalonia.Input.KeyGesture(Avalonia.Input.Key.Enter), Command = viewModel.ConnectCommand });
                    return textBox;
                },
                ["chkRememberMe"] = layout => CheckBox(layout, nameof(CnCNetLobbyViewModel.RememberMe)),
                ["chkPersistentMode"] = layout => CheckBox(layout, nameof(CnCNetLobbyViewModel.PersistentMode)),
                ["chkAutoConnect"] = layout => CheckBox(layout, nameof(CnCNetLobbyViewModel.AutoConnect), nameof(CnCNetLobbyViewModel.CanAutoConnect)),
            });
    }

    /// <summary>
    /// The game creation window, laid out as the XNA GameCreationWindow (without the advanced tunnel options and
    /// saved-game loading, which the preview doesn't have).
    /// </summary>
    private static Control BuildGameCreationWindow(CnCNetLobbyViewModel viewModel)
    {
        const int sides = 6, margin = 6, tunnelListWidth = 466;

        var window = new LayoutControl("GameCreationWindow", "XNAWindow", LayoutControlKind.Panel)
        {
            Width = tunnelListWidth + (sides * 2) + (margin * 2),
            BackgroundTexture = "gamecreationoptionsbg.png",
        };

        int left = sides + margin;
        LayoutControl tbGameName = ThemedWindow.Add(window, "tbGameName", "XNATextBox", window.Width - 150 - sides - margin, 6 + margin, 150, 21);
        ThemedWindow.Add(window, "lblRoomName", "XNALabel", left, tbGameName.Y + 1, 0, 0, "Game room name:");
        LayoutControl ddMaxPlayers = ThemedWindow.Add(window, "ddMaxPlayers", "XNAClientDropDown", tbGameName.X, tbGameName.Y + tbGameName.Height + 20, tbGameName.Width, 21);
        ThemedWindow.Add(window, "lblMaxPlayers", "XNALabel", left, ddMaxPlayers.Y + 1, 0, 0, "Maximum number of players:");
        LayoutControl ddSkillLevel = ThemedWindow.Add(window, "ddSkillLevel", "XNAClientDropDown", tbGameName.X, ddMaxPlayers.Y + ddMaxPlayers.Height + 20, tbGameName.Width, 21);
        ThemedWindow.Add(window, "lblSkillLevel", "XNALabel", left, ddSkillLevel.Y + 1, 0, 0, "Select preferred skill level of players:");
        LayoutControl tbPassword = ThemedWindow.Add(window, "tbPassword", "XNATextBox", tbGameName.X, ddSkillLevel.Y + ddSkillLevel.Height + 20, tbGameName.Width, 21);
        LayoutControl lblPassword = ThemedWindow.Add(window, "lblPassword", "XNALabel", left, tbPassword.Y + 1, 0, 0, "Password (leave blank for none):");
        (_, int labelHeight) = ThemeFonts.Measure(lblPassword.Text, 0);

        int advancedOptionsY = tbPassword.Y + 1 + labelHeight + (margin * 3);
        LayoutControl btnCreateGame = ThemedWindow.Add(window, "btnCreateGame", "XNAClientButton", left, advancedOptionsY + 23 + (margin * 3), 133, 23, "Create Game");
        ThemedWindow.Add(window, "btnCancel", "XNAClientButton", window.Width - 133 - sides - margin, btnCreateGame.Y, 133, 23, "Cancel");
        window.Height = btnCreateGame.Y + btnCreateGame.Height + margin + 6;

        TextBox BoundTextBox(LayoutControl layout, string property)
        {
            TextBox textBox = ThemedStyle.TextBox(layout.Width, layout.Height, string.Empty);
            textBox.DataContext = viewModel;
            textBox.Bind(Avalonia.Controls.TextBox.TextProperty, new Binding(property) { Mode = BindingMode.TwoWay });
            return textBox;
        }

        Control DropDown(LayoutControl layout, string items, string index)
        {
            var dropDown = new ThemedDropDown(layout.Width, layout.Height) { DataContext = viewModel };
            dropDown.Bind(ThemedDropDown.ItemsSourceProperty, new Binding(items));
            dropDown.Bind(ThemedDropDown.SelectedIndexProperty, new Binding(index) { Mode = BindingMode.TwoWay });
            return dropDown;
        }

        return ThemedWindow.Build(window, (name, layout) =>
            {
                if (name == "btnCreateGame")
                    viewModel.CreateGameCommand.Execute(null);
                else if (name == "btnCancel")
                    viewModel.CancelCreateGameCommand.Execute(null);
            },
            new Dictionary<string, Func<LayoutControl, Control>>
            {
                ["tbGameName"] = layout =>
                {
                    TextBox textBox = BoundTextBox(layout, nameof(CnCNetLobbyViewModel.NewRoomName));
                    textBox.MaxLength = 23;
                    return textBox;
                },
                ["tbPassword"] = layout => BoundTextBox(layout, nameof(CnCNetLobbyViewModel.NewRoomPassword)),
                ["ddMaxPlayers"] = layout => DropDown(layout, nameof(CnCNetLobbyViewModel.MaxPlayerItems), nameof(CnCNetLobbyViewModel.NewRoomMaxPlayersIndex)),
                ["ddSkillLevel"] = layout => DropDown(layout, nameof(CnCNetLobbyViewModel.SkillLevels), nameof(CnCNetLobbyViewModel.NewRoomSkillLevel)),
            });
    }

    /// <summary>The password prompt, laid out as the XNA PasswordRequestWindow.</summary>
    private static Control BuildPasswordWindow(CnCNetLobbyViewModel viewModel)
    {
        string description = "Please enter the password for the game and click OK.";
        (int descriptionWidth, int descriptionHeight) = ThemeFonts.Measure(description, 0);

        var window = new LayoutControl("PasswordRequestWindow", "XNAWindow", LayoutControlKind.Panel)
        {
            Width = descriptionWidth + 24,
            Height = 110,
            BackgroundTexture = "passwordquerybg.png",
        };

        LayoutControl lblDescription = ThemedWindow.Add(window, "lblDescription", "XNALabel", 12, 12, descriptionWidth, descriptionHeight, description);
        ThemedWindow.Add(window, "tbPassword", "XNATextBox", lblDescription.X, lblDescription.Y + lblDescription.Height + 12, window.Width - 24, 21);
        LayoutControl btnOK = ThemedWindow.Add(window, "btnOK", "XNAClientButton", lblDescription.X, window.Height - 35, 92, 23, "OK");
        ThemedWindow.Add(window, "btnCancel", "XNAClientButton", window.Width - 104, btnOK.Y, 92, 23, "Cancel");

        return ThemedWindow.Build(window, (name, layout) =>
            {
                if (name == "btnOK")
                    viewModel.SubmitPasswordCommand.Execute(null);
                else if (name == "btnCancel")
                    viewModel.CancelPasswordCommand.Execute(null);
            },
            new Dictionary<string, Func<LayoutControl, Control>>
            {
                ["tbPassword"] = layout =>
                {
                    TextBox textBox = ThemedStyle.TextBox(layout.Width, layout.Height, string.Empty);
                    textBox.PasswordChar = '*';
                    textBox.DataContext = viewModel;
                    textBox.Bind(Avalonia.Controls.TextBox.TextProperty, new Binding(nameof(CnCNetLobbyViewModel.JoinPassword)) { Mode = BindingMode.TwoWay });
                    textBox.KeyBindings.Add(new KeyBinding { Gesture = new KeyGesture(Key.Enter), Command = viewModel.SubmitPasswordCommand });
                    return textBox;
                },
            });
    }

    private static Control GameList(LayoutControl layout, CnCNetLobbyViewModel viewModel)
    {
        ListBox list = ThemedWindow.List(layout, viewModel, nameof(CnCNetLobbyViewModel.Games),
            new FuncDataTemplate<CnCNetGameItemViewModel>((game, _) => GameRow(game)));

        list.Bind(SelectingItemsControl.SelectedIndexProperty, new Binding(nameof(CnCNetLobbyViewModel.SelectedGameIndex)) { Mode = BindingMode.TwoWay });
        list.DoubleTapped += (_, _) => viewModel.JoinGameCommand.Execute(null);
        return list;
    }

    /// <summary>
    /// A game list row as the XNA GameListBox draws it: game icon (for other games, or if the theme shows it), locked
    /// and incompatible icons, the room name (greyed if it can't be joined), and the password and skill level icons
    /// on the right.
    /// </summary>
    private static Control GameRow(CnCNetGameItemViewModel item)
    {
        var row = new DockPanel { Margin = new Thickness(2, 1), LastChildFill = true };
        if (item == null)
            return row;

        HostedCnCNetGame game = item.Game;

        void AddIcon(Avalonia.Media.Imaging.Bitmap bitmap, Dock dock)
        {
            if (bitmap == null)
                return;

            var image = new Image { Source = bitmap, Stretch = Stretch.None, Margin = new Thickness(0, 0, 2, 0), VerticalAlignment = Avalonia.Layout.VerticalAlignment.Center };
            DockPanel.SetDock(image, dock);
            row.Children.Add(image);
        }

        if (game.SkillLevel > 0)
            AddIcon(ThemeAssets.LoadBitmap($"skillLevel{game.SkillLevel}.png"), Dock.Right);
        if (game.Passworded)
            AddIcon(ThemeAssets.LoadBitmap("passwordedgame.png"), Dock.Right);

        bool showGameIcon = ClientCore.ClientConfiguration.Instance.ShowGameIconInGameList ||
            !string.Equals(game.Game?.InternalName, ClientCore.ClientConfiguration.Instance.LocalGame, StringComparison.OrdinalIgnoreCase);
        if (showGameIcon)
            AddIcon(ThemeAssets.GameIcon(game.Game), Dock.Left);
        if (game.Locked)
            AddIcon(ThemeAssets.LoadBitmap("lockedgame.png"), Dock.Left);
        if (game.Incompatible)
            AddIcon(ThemeAssets.LoadBitmap("incompatible.png"), Dock.Left);

        var text = new TextBlock
        {
            Text = game.RoomName + (game.IsLoadedGame ? " (Loaded Game)" : string.Empty),
            VerticalAlignment = Avalonia.Layout.VerticalAlignment.Center,
            Foreground = game.Locked || game.Incompatible ? Brushes.Gray : new SolidColorBrush(ThemeAssets.ButtonTextColor),
            TextTrimming = TextTrimming.CharacterEllipsis,
        };
        row.Children.Add(text);

        ToolTip.SetTip(row, item.Details + Environment.NewLine + item.Players);
        return row;
    }

    private static Control DropDown(LayoutControl layout, CnCNetLobbyViewModel viewModel, string items, string index)
    {
        var dropDown = new ThemedDropDown(layout.Width, layout.Height) { DataContext = viewModel };
        dropDown.Bind(ThemedDropDown.ItemsSourceProperty, new Binding(items));
        dropDown.Bind(ThemedDropDown.SelectedIndexProperty, new Binding(index) { Mode = BindingMode.TwoWay });
        dropDown.Bind(ThemedDropDown.CanChangeProperty, new Binding(nameof(CnCNetLobbyViewModel.IsConnected)));
        return dropDown;
    }

    private static void OnButton(CnCNetLobbyViewModel viewModel, string name, LayoutControl layout)
    {
        switch (name)
        {
            case "btnNewGame":
                if (viewModel.IsConnected)
                    viewModel.OpenCreateGameCommand.Execute(null);
                break;
            case "btnJoinGame":
                if (viewModel.IsConnected)
                    viewModel.JoinGameCommand.Execute(null);
                break;
            case "btnLogout":
                viewModel.BackCommand.Execute(null);
                break;
            default:
                ThemeAssets.OpenUrl(layout?.Url);
                break;
        }
    }
}
