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

/// <summary>The LAN lobby, laid out as the XNA LANLobby creates its controls, with the theme's LANLobby.ini applied.</summary>
public partial class LanLobbyView : UserControl
{
    public LanLobbyView()
    {
        InitializeComponent();
        DataContextChanged += (_, _) => Build();
    }

    private void Build()
    {
        if (DataContext is not LanLobbyViewModel viewModel)
            return;

        try
        {
            LobbyHost.Content = ThemedWindow.Build(CreateLayout(), (name, layout) => OnButton(viewModel, name, layout),
                new Dictionary<string, Func<LayoutControl, Control>>
                {
                    ["lbGameList"] = layout => GameList(layout, viewModel),
                    ["lbPlayerList"] = layout => ThemedWindow.List(layout, viewModel, nameof(LanLobbyViewModel.Players)),
                    ["lbChatMessages"] = layout => ThemedWindow.ChatList(layout, viewModel),
                    ["tbChatInput"] = layout => ThemedWindow.ChatInput(layout, viewModel, viewModel.SendChatCommand),
                    ["ddColor"] = layout => ColorDropDown(layout, viewModel),
                });
        }
        catch (Exception ex)
        {
            Logger.Log("LanLobbyView: building the themed lobby failed: " + ex);
            LobbyHost.Content = new TextBlock { Text = "The theme's LAN lobby could not be loaded: " + ex.Message };
        }
    }

    /// <summary>The controls LANLobby.Initialize creates, at its positions.</summary>
    private static LayoutControl CreateLayout()
    {
        var window = new LayoutControl("LANLobby", "XNAWindow", LayoutControlKind.Panel)
        {
            Width = ThemedWindow.RenderWidth - 64,
            Height = ThemedWindow.RenderHeight - 64,
            BackgroundTexture = "cncnetlobbybg.png",
        };

        LayoutControl btnNewGame = ThemedWindow.Add(window, "btnNewGame", "XNAClientButton", 12, window.Height - 35, 133, 23, "Create Game");
        LayoutControl btnJoinGame = ThemedWindow.Add(window, "btnJoinGame", "XNAClientButton", btnNewGame.X + btnNewGame.Width + 12, btnNewGame.Y, 133, 23, "Join Game");
        ThemedWindow.Add(window, "btnMainMenu", "XNAClientButton", window.Width - 145, btnNewGame.Y, 133, 23, "Main Menu");

        LayoutControl lbGameList = ThemedWindow.Add(window, "lbGameList", "GameListBox", btnNewGame.X, 41,
            btnJoinGame.X + btnJoinGame.Width - btnNewGame.X, btnNewGame.Y - 53);
        LayoutControl lbPlayerList = ThemedWindow.Add(window, "lbPlayerList", "XNAListBox", window.Width - 202, lbGameList.Y, 190, lbGameList.Height);
        int gameListRight = lbGameList.X + lbGameList.Width;
        LayoutControl lbChatMessages = ThemedWindow.Add(window, "lbChatMessages", "ChatListBox", gameListRight + 12, lbGameList.Y,
            lbPlayerList.X - gameListRight - 24, lbGameList.Height);
        ThemedWindow.Add(window, "tbChatInput", "XNAChatTextBox", lbChatMessages.X, btnNewGame.Y, lbChatMessages.Width, btnNewGame.Height);

        LayoutControl lblColor = ThemedWindow.Add(window, "lblColor", "XNALabel", lbChatMessages.X, 14, 0, 0, "YOUR COLOR:");
        lblColor.FontIndex = 1;
        ThemedWindow.Add(window, "ddColor", "XNAClientDropDown", lblColor.X + 95, 12, 150, 21);

        return window;
    }

    private static Control GameList(LayoutControl layout, LanLobbyViewModel viewModel)
    {
        ListBox list = ThemedWindow.List(layout, viewModel, nameof(LanLobbyViewModel.Games),
            new FuncDataTemplate<LanGameItemViewModel>((game, _) =>
            {
                var text = new TextBlock { Text = game?.RoomName, Margin = new Thickness(4, 1) };
                ToolTip.SetTip(text, game == null ? null : game.Details + Environment.NewLine + game.Players);
                return text;
            }));

        list.Bind(SelectingItemsControl.SelectedIndexProperty, new Binding(nameof(LanLobbyViewModel.SelectedGameIndex)) { Mode = BindingMode.TwoWay });
        list.DoubleTapped += (_, _) => viewModel.JoinGameCommand.Execute(null);
        return list;
    }

    private static Control ColorDropDown(LayoutControl layout, LanLobbyViewModel viewModel)
    {
        var dropDown = new ThemedDropDown(layout.Width, layout.Height) { DataContext = viewModel, ItemsSource = viewModel.ChatColors };
        dropDown.Bind(ThemedDropDown.SelectedIndexProperty, new Binding(nameof(LanLobbyViewModel.SelectedChatColorIndex)) { Mode = BindingMode.TwoWay });
        return dropDown;
    }

    private static void OnButton(LanLobbyViewModel viewModel, string name, LayoutControl layout)
    {
        switch (name)
        {
            case "btnNewGame":
                viewModel.CreateGameCommand.Execute(null);
                break;
            case "btnJoinGame":
                viewModel.JoinGameCommand.Execute(null);
                break;
            case "btnMainMenu":
                viewModel.BackCommand.Execute(null);
                break;
            default:
                ThemeAssets.OpenUrl(layout?.Url);
                break;
        }
    }
}
