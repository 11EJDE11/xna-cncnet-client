using System;
using System.Collections.Generic;

using Avalonia.Controls;
using Avalonia.Data;
using Avalonia.Layout;
using Avalonia.Media;

using AvClientView.Theme;
using AvClientView.ViewModels;

using ClientCore.Extensions;

using ClientLogic.Layout;
using ClientLogic.Lobby;

using Rampastring.Tools;

namespace AvClientView.Views;

/// <summary>
/// The XNA GameLoadingLobbyBase window (590×510, loadmpsavebg.png, the theme's GameLoadingLobby.ini): the saved game's
/// players, map, game mode and save, the chat, Load Game / I'm Ready and Leave Game; CnCNet rooms add Change Tunnel.
/// </summary>
public sealed class GameLoadingRoomView : UserControl
{
    private const int BUTTON_WIDTH_133 = 133;
    private const int BUTTON_HEIGHT = 23;

    private GameLoadingRoomViewModel viewModel;

    public GameLoadingRoomView()
    {
        Background = Brushes.Black;
        DataContextChanged += (_, _) => Build();
    }

    private void Build()
    {
        if (DataContext is not GameLoadingRoomViewModel model || model == viewModel)
            return;

        viewModel = model;

        try
        {
            Canvas window = BuildWindow(model.HasChangeTunnelButton);
            if (model is CnCNetGameLoadingRoomViewModel cncnet)
                Content = new Panel { Children = { window, new TunnelSelectionView(cncnet.Tunnels) } };
            else
                Content = window;
        }
        catch (Exception ex)
        {
            Logger.Log("GameLoadingRoomView: building the saved game room failed: " + ex);
            Content = new TextBlock { Text = "The saved game room could not be built: " + ex.Message, Foreground = Brushes.White };
        }
    }

    private Canvas BuildWindow(bool isCnCNet)
    {
        var window = new LayoutControl("GameLoadingLobby", "XNAWindow", LayoutControlKind.Panel)
        {
            Width = 590,
            Height = 510,
            BackgroundTexture = "loadmpsavebg.png",
        };

        ThemedWindow.Add(window, "lblDescription", "XNALabel", 12, 12, 0, 0,
            "Wait for all players to join and get ready, then click Load Game to load the saved multiplayer game.".L10N("Client:Main:LobbyInitialTip"));
        LayoutControl panelPlayers = ThemedWindow.Add(window, "panelPlayers", "XNAPanel", 12, 32, 373, 125);
        int right = panelPlayers.X + panelPlayers.Width + 12;
        ThemedWindow.Add(window, "lblMapName", "XNALabel", right, panelPlayers.Y, 0, 0, "MAP:".L10N("Client:Main:MapLabel")).FontIndex = 1;
        ThemedWindow.Add(window, "lblMapNameValue", "XNALabel", right, panelPlayers.Y + 18, 0, 0, "Map name".L10N("Client:Main:MapName"));
        ThemedWindow.Add(window, "lblGameMode", "XNALabel", right, panelPlayers.Y + 40, 0, 0, "GAME MODE:".L10N("Client:Main:GameMode")).FontIndex = 1;
        ThemedWindow.Add(window, "lblGameModeValue", "XNALabel", right, panelPlayers.Y + 58, 0, 0, "Game mode".L10N("Client:Main:GameModeValueText"));
        int panelBottom = panelPlayers.Y + panelPlayers.Height;
        ThemedWindow.Add(window, "lblSavedGameTime", "XNALabel", right, panelBottom - 40, 0, 0, "SAVED GAME:".L10N("Client:Main:SavedGame")).FontIndex = 1;
        LayoutControl lbChatMessages = ThemedWindow.Add(window, "lbChatMessages", "ChatListBox", 12, panelBottom + 12, window.Width - 24,
            window.Height - panelBottom - 12 - 29 - 34);
        LayoutControl tbChatInput = ThemedWindow.Add(window, "tbChatInput", "XNATextBox", lbChatMessages.X, lbChatMessages.Y + lbChatMessages.Height + 3,
            lbChatMessages.Width, 19);
        int buttonY = tbChatInput.Y + tbChatInput.Height + 6;
        LayoutControl btnLoadGame = ThemedWindow.Add(window, "btnLoadGame", "XNAClientButton", lbChatMessages.X, buttonY, BUTTON_WIDTH_133, BUTTON_HEIGHT,
            "Load Game".L10N("Client:Main:LoadGame"));
        LayoutControl btnLeaveGame = ThemedWindow.Add(window, "btnLeaveGame", "XNAClientButton", window.Width - 145, buttonY, BUTTON_WIDTH_133, BUTTON_HEIGHT,
            "Leave Game".L10N("Client:Main:LeaveGame"));
        ThemedWindow.Add(window, "ddSavedGame", "XNAClientDropDown", right, panelBottom - 21, window.Width - right - 12, 21);

        if (isCnCNet)
        {
            ThemedWindow.Add(window, "btnChangeTunnel", "XNAClientButton", btnLeaveGame.X + btnLeaveGame.Width - btnLeaveGame.Width - 145, btnLeaveGame.Y,
                BUTTON_WIDTH_133, BUTTON_HEIGHT, "Change Tunnel".L10N("Client:Main:ChangeTunnel"));
        }

        Canvas canvas = ThemedWindow.Build(window, (name, _) =>
        {
            switch (name)
            {
                case "btnLoadGame":
                    viewModel.Load();
                    break;
                case "btnLeaveGame":
                    viewModel.Leave();
                    break;
                case "btnChangeTunnel":
                    (viewModel as CnCNetGameLoadingRoomViewModel)?.Tunnels.Open();
                    break;
            }
        }, new Dictionary<string, Func<LayoutControl, Control>>
        {
            ["panelPlayers"] = PlayersPanel,
            ["lbChatMessages"] = layout => ThemedWindow.ChatList(layout, viewModel),
            ["tbChatInput"] = layout => ThemedWindow.ChatInput(layout, viewModel, viewModel.SendChatCommand, string.Empty,
                colorProperty: isCnCNet ? nameof(CnCNetGameLoadingRoomViewModel.ChatInputBrush) : null),
            ["ddSavedGame"] = layout =>
            {
                var dropDown = new ThemedDropDown(layout.Width, 22, layout.FontIndex) { DataContext = viewModel };
                dropDown.Bind(ThemedDropDown.ItemsSourceProperty, new Binding(nameof(GameLoadingRoomViewModel.SavedGames)));
                dropDown.Bind(ThemedDropDown.SelectedIndexProperty, new Binding(nameof(GameLoadingRoomViewModel.SelectedSavedGameIndex)) { Mode = BindingMode.TwoWay });
                dropDown.Bind(ThemedDropDown.CanChangeProperty, new Binding(nameof(GameLoadingRoomViewModel.CanSelectSavedGame)));
                return dropDown;
            },
        });

        foreach ((string label, string property) in new[]
        {
            ("lblMapNameValue", nameof(GameLoadingRoomViewModel.MapNameText)),
            ("lblGameModeValue", nameof(GameLoadingRoomViewModel.GameModeText)),
        })
        {
            if (LayoutView.FindNamed<TextBlock>(canvas, label) is TextBlock text)
            {
                text.DataContext = viewModel;
                text.Bind(TextBlock.TextProperty, new Binding(property));
            }
        }

        void UpdateButtons()
        {
            if (LayoutView.FindNamed<ThemedButton>(canvas, "btnLoadGame") is ThemedButton load)
            {
                load.Text = viewModel.LoadButtonText;
                load.IsEnabled = viewModel is not CnCNetGameLoadingRoomViewModel cncnet || cncnet.CanLoad;
            }

            if (LayoutView.FindNamed<ThemedButton>(canvas, "btnChangeTunnel") is ThemedButton tunnel)
                tunnel.IsVisible = viewModel.IsHost;
        }

        GameLoadingRoomViewModel buttonModel = viewModel;
        void Refreshed(object sender, EventArgs e) => UpdateButtons();
        canvas.AttachedToVisualTree += (_, _) => { buttonModel.Refreshed += Refreshed; UpdateButtons(); };
        canvas.DetachedFromVisualTree += (_, _) => buttonModel.Refreshed -= Refreshed;
        UpdateButtons();

        canvas.HorizontalAlignment = HorizontalAlignment.Center;
        canvas.VerticalAlignment = VerticalAlignment.Center;
        return canvas;
    }

    /// <summary>panelPlayers: the saved game's players in two columns of four, gray while not present.</summary>
    private Control PlayersPanel(LayoutControl layout)
    {
        var canvas = new Canvas { Width = layout.Width, Height = layout.Height, Background = new SolidColorBrush(Color.FromArgb(128, 0, 0, 0)) };

        void Update()
        {
            canvas.Children.Clear();
            for (int i = 0; i < viewModel.PlayerRows.Count && i < 8; i++)
            {
                SavedGamePlayerRow row = viewModel.PlayerRows[i];
                var text = new TextBlock
                {
                    Text = row.Text,
                    Foreground = new SolidColorBrush(row.IsPresent ? ThemeAssets.ToColor(row.Color) : Colors.Gray),
                };
                Canvas.SetLeft(text, i < 4 ? 9 : 190);
                Canvas.SetTop(text, 9 + 30 * (i % 4));
                canvas.Children.Add(text);
            }
        }

        GameLoadingRoomViewModel playerModel = viewModel;
        void Refreshed(object sender, EventArgs e) => Update();
        canvas.AttachedToVisualTree += (_, _) => { playerModel.Refreshed += Refreshed; Update(); };
        canvas.DetachedFromVisualTree += (_, _) => playerModel.Refreshed -= Refreshed;
        Update();
        return canvas;
    }
}
