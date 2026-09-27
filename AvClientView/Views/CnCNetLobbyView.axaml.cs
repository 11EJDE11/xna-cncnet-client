using System;
using System.Collections.Generic;

using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Controls.Templates;
using Avalonia.Data;
using Avalonia.Media;

using AvClientView.Theme;
using AvClientView.ViewModels;

using ClientLogic.Layout;

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

    private static Control GameList(LayoutControl layout, CnCNetLobbyViewModel viewModel)
    {
        ListBox list = ThemedWindow.List(layout, viewModel, nameof(CnCNetLobbyViewModel.Games),
            new FuncDataTemplate<CnCNetGameItemViewModel>((game, _) =>
            {
                var text = new TextBlock { Text = game?.RoomName, Margin = new Thickness(4, 1) };
                ToolTip.SetTip(text, game == null ? null : game.Details + Environment.NewLine + game.Players);
                return text;
            }));

        list.Bind(SelectingItemsControl.SelectedIndexProperty, new Binding(nameof(CnCNetLobbyViewModel.SelectedGameIndex)) { Mode = BindingMode.TwoWay });
        list.DoubleTapped += (_, _) => viewModel.JoinGameCommand.Execute(null);
        return list;
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
