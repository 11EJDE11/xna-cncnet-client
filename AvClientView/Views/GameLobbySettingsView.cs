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

using Rampastring.Tools;

namespace AvClientView.Views;

/// <summary>The XNA GameLobbySettingsWindow over a darkening panel, with the theme's GameLobbySettingsWindow.ini applied.</summary>
public sealed class GameLobbySettingsView : Panel
{
    private const int EMPTY_SPACE = 6;
    private const int MARGIN = 6;
    private const int BUTTON_WIDTH_133 = 133;
    private const int BUTTON_HEIGHT = 23;

    private readonly GameLobbySettingsViewModel viewModel;

    public GameLobbySettingsView(GameLobbySettingsViewModel viewModel)
    {
        this.viewModel = viewModel;
        Background = new SolidColorBrush(Color.FromArgb(128, 0, 0, 0));
        IsVisible = false;

        viewModel.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(GameLobbySettingsViewModel.IsOpen))
                IsVisible = viewModel.IsOpen;
        };

        try
        {
            Children.Add(Build());
        }
        catch (Exception ex)
        {
            Logger.Log("GameLobbySettingsView: building the game lobby settings window failed: " + ex);
            Children.Add(new TextBlock { Text = "The game lobby settings window could not be built: " + ex.Message, Foreground = Brushes.White });
        }
    }

    private Canvas Build()
    {
        var window = new LayoutControl("GameLobbySettingsWindow", "XNAWindow", LayoutControlKind.Panel)
        {
            Width = 400,
            Height = 240,
            BackgroundTexture = "gamecreationoptionsbg.png",
        };

        int x = EMPTY_SPACE + MARGIN;
        int fieldX = window.Width - 200 - EMPTY_SPACE - MARGIN;

        LayoutControl lblRoomName = ThemedWindow.Add(window, "lblRoomName", "XNALabel", x, EMPTY_SPACE + MARGIN, 0, 0, "Game room name:".L10N("Client:Main:GameRoomName"));
        LayoutControl tbGameName = ThemedWindow.Add(window, "tbGameName", "XNATextBox", fieldX, lblRoomName.Y - 2, 200, 21);
        int nextY = tbGameName.Y + tbGameName.Height + 15;

        LayoutControl lblPassword = ThemedWindow.Add(window, "lblPassword", "XNALabel", x, nextY, 0, 0, "Password:".L10N("Client:Main:LobbyPassword"));
        LayoutControl tbPassword = ThemedWindow.Add(window, "tbPassword", "XNATextBox", fieldX, lblPassword.Y - 2, 200, 21);
        nextY = tbPassword.Y + tbPassword.Height + 15;

        LayoutControl lblMaxPlayers = ThemedWindow.Add(window, "lblMaxPlayers", "XNALabel", x, nextY, 0, 0, "Max players:".L10N("Client:Main:GameMaxPlayers"));
        LayoutControl ddMaxPlayers = ThemedWindow.Add(window, "ddMaxPlayers", "XNAClientDropDown", fieldX, lblMaxPlayers.Y - 2, 200, 21);
        nextY = ddMaxPlayers.Y + ddMaxPlayers.Height + 15;

        LayoutControl lblSkillLevel = ThemedWindow.Add(window, "lblSkillLevel", "XNALabel", x, nextY, 0, 0, "Preferred skill level:".L10N("Client:Main:PreferredSkillLevel"));
        LayoutControl ddSkillLevel = ThemedWindow.Add(window, "ddSkillLevel", "XNAClientDropDown", fieldX, lblSkillLevel.Y - 2, 200, 21);
        nextY = ddSkillLevel.Y + ddSkillLevel.Height + 20;

        LayoutControl btnSave = ThemedWindow.Add(window, "btnSave", "XNAClientButton", x, nextY, BUTTON_WIDTH_133, BUTTON_HEIGHT, "Save".L10N("Client:Main:ButtonSave"));
        ThemedWindow.Add(window, "btnCancel", "XNAClientButton", window.Width - BUTTON_WIDTH_133 - EMPTY_SPACE - MARGIN, btnSave.Y,
            BUTTON_WIDTH_133, BUTTON_HEIGHT, "Cancel".L10N("Client:Main:ButtonCancel"));

        window.Height = btnSave.Y + btnSave.Height + MARGIN + EMPTY_SPACE;

        Canvas canvas = ThemedWindow.Build(window, (name, _) =>
        {
            if (name == "btnSave")
                viewModel.Save();
            else if (name == "btnCancel")
                viewModel.Cancel();
        }, new Dictionary<string, Func<LayoutControl, Control>>
        {
            ["tbGameName"] = layout => TextBox(layout, nameof(GameLobbySettingsViewModel.GameName), 23),
            ["tbPassword"] = layout => TextBox(layout, nameof(GameLobbySettingsViewModel.Password), 20),
            ["ddMaxPlayers"] = layout => DropDown(layout, nameof(GameLobbySettingsViewModel.MaxPlayerItems), nameof(GameLobbySettingsViewModel.MaxPlayersIndex)),
            ["ddSkillLevel"] = layout => DropDown(layout, nameof(GameLobbySettingsViewModel.SkillLevels), nameof(GameLobbySettingsViewModel.SkillLevel)),
        });

        canvas.HorizontalAlignment = HorizontalAlignment.Center;
        canvas.VerticalAlignment = VerticalAlignment.Center;
        return canvas;
    }

    private TextBox TextBox(LayoutControl layout, string path, int maxLength)
    {
        TextBox textBox = ThemedStyle.TextBox(layout.Width, layout.Height, string.Empty);
        textBox.MaxLength = maxLength;
        textBox.DataContext = viewModel;
        textBox.Bind(Avalonia.Controls.TextBox.TextProperty, new Binding(path) { Mode = BindingMode.TwoWay });
        return textBox;
    }

    private ThemedDropDown DropDown(LayoutControl layout, string itemsPath, string selectedPath)
    {
        var dropDown = new ThemedDropDown(layout.Width, 22, layout.FontIndex) { DataContext = viewModel };
        dropDown.Bind(ThemedDropDown.ItemsSourceProperty, new Binding(itemsPath));
        dropDown.Bind(ThemedDropDown.SelectedIndexProperty, new Binding(selectedPath) { Mode = BindingMode.TwoWay });
        return dropDown;
    }
}
