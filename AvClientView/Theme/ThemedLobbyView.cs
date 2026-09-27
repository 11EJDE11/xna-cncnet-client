using System;
using System.Collections.Generic;
using System.Collections.Specialized;
using System.Linq;

using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Controls.Templates;
using Avalonia.Data;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;

using AvClientView.ViewModels;
using AvClientView.Views;

using ClientCore;

using ClientLogic.Layout;
using ClientLogic.Lobby;

using Rampastring.Tools;

namespace AvClientView.Theme;

/// <summary>Which XNA game lobby a themed lobby stands in for.</summary>
public sealed record ThemedLobbyKind(string WindowName, string LayoutIniName, bool IsMultiplayer);

/// <summary>
/// A game lobby drawn from the theme's layout INIs, as the XNA GameLobbyBase is: the window and its "$CC" controls
/// from the INI, the player rows generated as InitPlayerOptionDropdowns does, and the static parts drawn by
/// <see cref="LayoutView"/>. The interactive parts (map list, drop-downs, check boxes, map preview, chat) are
/// Avalonia controls at the layout's positions, bound to the lobby's view model.
/// </summary>
public static class ThemedLobbyView
{
    public static int RenderWidth => ThemeAssets.RenderWidth;

    public static int RenderHeight => ThemeAssets.RenderHeight;

    private const int DROP_DOWN_HEIGHT = 21;
    private const int MAX_PLAYER_COUNT = 8;

    public static readonly ThemedLobbyKind Skirmish = new("SkirmishLobby", "SkirmishLobby", false);

    public static ThemedLobbyKind Multiplayer(string layoutIniName) => new("MultiplayerGameLobby", layoutIniName, true);

    /// <summary>Builds the lobby; <paramref name="onButton"/> gets the XNA names of the clicked buttons.</summary>
    public static Control Build(LobbyViewModelBase viewModel, ThemedLobbyKind kind, bool isHost, Action<string, LayoutControl> onButton)
    {
        var window = new LayoutControl(kind.WindowName, "INItializableWindow", LayoutControlKind.Panel)
        {
            Width = RenderWidth - 60,
            Height = RenderHeight - 32,
            BackgroundTexture = "gamelobbybg.png",
        };

        var ini = new CCIniFile(LobbySession.FindLayoutIni(kind.LayoutIniName));
        var reader = new XnaLayoutReader(ThemeAssets.TextureSize, RenderWidth, RenderHeight) { MeasureText = ThemeFonts.Measure };
        Dictionary<string, int> constants = LayoutExpressionParser.ClientConstants(RenderWidth, RenderHeight,
            ClientConfiguration.Instance.GetParserConstants());

        LayoutExpressionParser parser = reader.ReadInitializableWindow(ini, window, constants);

        if (kind.IsMultiplayer)
        {
            // The multiplayer lobby places the chat by role (MultiplayerGameLobby)
            string role = isHost ? "_Host" : "_Player";
            if (window.Find("lbChatMessages") is LayoutControl chat)
                reader.ReadInitializableControl(ini, chat, parser, "lbChatMessages" + role);
            if (window.Find("tbChatInput") is LayoutControl input)
                reader.ReadInitializableControl(ini, input, parser, "tbChatInput" + role);
        }

        AddPlayerRows(ini, reader, parser, window);

        // Players don't see the map list (MultiplayerGameLobby.HideMapList); the room buttons the preview doesn't have
        // yet are hidden
        var hidden = new List<string> { "btnChangeTunnel", "btnGameLobbySettings", "btnNegotiationStatus" };
        if (kind.IsMultiplayer && !isHost)
            hidden.AddRange(["ddGameMode", "lblGameModeSelect", "lbMapList", "tbMapSearch", "btnPickRandomMap", "btnMapSortAlphabetically", "btnLockGame"]);

        foreach (string name in hidden)
        {
            if (window.Find(name) is LayoutControl control)
                control.Visible = false;
        }

        // The extra options panel starts hidden in XNA; the preview doesn't have it yet
        if (window.Find("PlayerExtraOptionsPanel") is LayoutControl extraOptions)
            extraOptions.Visible = false;
        if (window.Find("btnPlayerExtraOptionsOpen") is LayoutControl extraOptionsButton)
            extraOptionsButton.Visible = false;

        Canvas root = LayoutView.Build(window, name => onButton(name, window.Find(name)));
        AddInteractiveControls(root, window, viewModel);

        if (kind.IsMultiplayer)
            AddStatusIndicators(root, window, ini, viewModel);

        // The map labels the XNA lobby fills in
        foreach ((string label, string property) in new[]
        {
            ("lblMapName", nameof(LobbyViewModelBase.MapNameText)),
            ("lblMapAuthor", nameof(LobbyViewModelBase.MapAuthorText)),
            ("lblGameMode", nameof(LobbyViewModelBase.GameModeText)),
            ("lblMapSize", nameof(LobbyViewModelBase.MapSizeText)),
        })
        {
            if (LayoutView.FindNamed<TextBlock>(root, label) is TextBlock text)
            {
                text.DataContext = viewModel;
                text.Bind(TextBlock.TextProperty, new Binding(property));
            }
        }

        return root;
    }

    /// <summary>The player rows and their captions, as GameLobbyBase.InitPlayerOptionDropdowns creates them.</summary>
    private static void AddPlayerRows(IniFile ini, XnaLayoutReader reader, LayoutExpressionParser parser, LayoutControl window)
    {
        LayoutControl panel = window.Find("PlayerOptionsPanel");
        if (panel == null)
            return;

        string section = window.Name;
        int verticalMargin = ini.GetIntValue(section, "PlayerOptionVerticalMargin", 12);
        int horizontalMargin = ini.GetIntValue(section, "PlayerOptionHorizontalMargin", 3);
        int captionY = ini.GetIntValue(section, "PlayerOptionCaptionLocationY", 6);
        int nameWidth = ini.GetIntValue(section, "PlayerNameWidth", 136);
        int sideWidth = ini.GetIntValue(section, "SideWidth", 91);
        int colorWidth = ini.GetIntValue(section, "ColorWidth", 79);
        int startWidth = ini.GetIntValue(section, "StartWidth", 49);
        int teamWidth = ini.GetIntValue(section, "TeamWidth", 46);
        int locationX = ini.GetIntValue(section, "PlayerOptionLocationX", 25);
        int locationY = ini.GetIntValue(section, "PlayerOptionLocationY", 24);

        for (int i = MAX_PLAYER_COUNT - 1; i > -1; i--)
        {
            int y = locationY + (DROP_DOWN_HEIGHT + verticalMargin) * i;
            var name = new LayoutControl("ddPlayerName" + i, "XNAClientDropDown", LayoutControlKind.Other) { X = locationX, Y = y, Width = nameWidth, Height = DROP_DOWN_HEIGHT };
            var side = new LayoutControl("ddPlayerSide" + i, "XNAClientDropDown", LayoutControlKind.Other) { X = name.X + name.Width + horizontalMargin, Y = y, Width = sideWidth, Height = DROP_DOWN_HEIGHT };
            var color = new LayoutControl("ddPlayerColor" + i, "XNAClientColorDropDown", LayoutControlKind.Other) { X = side.X + side.Width + horizontalMargin, Y = y, Width = colorWidth, Height = DROP_DOWN_HEIGHT };
            var team = new LayoutControl("ddPlayerTeam" + i, "XNAClientDropDown", LayoutControlKind.Other) { X = color.X + color.Width + horizontalMargin, Y = y, Width = teamWidth, Height = DROP_DOWN_HEIGHT };
            var start = new LayoutControl("ddPlayerStart" + i, "XNAClientDropDown", LayoutControlKind.Other) { X = team.X + team.Width + horizontalMargin, Y = y, Width = startWidth, Height = DROP_DOWN_HEIGHT, Visible = false };

            foreach (LayoutControl control in new[] { name, side, color, start, team })
            {
                panel.AddChild(control);
                reader.ReadInitializableControl(ini, control, parser);
            }
        }

        (string Name, string Text, string Column)[] captions =
        [
            ("lblName", "PLAYER", "ddPlayerName0"),
            ("lblSide", "SIDE", "ddPlayerSide0"),
            ("lblColor", "COLOR", "ddPlayerColor0"),
            ("lblStart", "START", "ddPlayerStart0"),
            ("lblTeam", "TEAM", "ddPlayerTeam0"),
        ];

        foreach ((string captionName, string text, string column) in captions)
        {
            var caption = new LayoutControl(captionName, "XNALabel", LayoutControlKind.Label)
            {
                Text = text,
                FontIndex = 1,
                X = panel.Find(column).X,
                Y = captionY,
                Visible = captionName != "lblStart",
            };

            panel.AddChild(caption);
            reader.ReadInitializableControl(ini, caption, parser);
        }
    }

    /// <summary>The player slot status indicators (MultiplayerGameLobby: PlayerStatusIndicatorX/Y beside each row).</summary>
    private static void AddStatusIndicators(Canvas root, LayoutControl window, IniFile ini, LobbyViewModelBase viewModel)
    {
        Canvas panel = LayoutView.FindNamed<Canvas>(root, "PlayerOptionsPanel");
        if (panel == null)
            return;

        int x = ini.GetIntValue(window.Name, "PlayerStatusIndicatorX", 0);
        int y = ini.GetIntValue(window.Name, "PlayerStatusIndicatorY", 0);
        var images = new List<Image>();

        for (int i = 0; i < MAX_PLAYER_COUNT; i++)
        {
            LayoutControl team = window.Find("ddPlayerTeam" + i);
            if (team == null)
                continue;

            var image = new Image { Stretch = Stretch.None, ZIndex = 1000 };
            Canvas.SetLeft(image, x);
            Canvas.SetTop(image, team.Y + y);
            panel.Children.Add(image);
            images.Add(image);
        }

        void Update()
        {
            IReadOnlyList<string> textures = viewModel.SlotStatusTextures;
            for (int i = 0; i < images.Count; i++)
                images[i].Source = textures != null && i < textures.Count ? ThemeAssets.LoadBitmap(textures[i]) : null;
        }

        viewModel.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(LobbyViewModelBase.SlotStatusTextures))
                Update();
        };
        Update();
    }

    private static Canvas ParentCanvas(Canvas root, LayoutControl control) =>
        control.Parent == null || control.Parent.Parent == null ? root : LayoutView.FindNamed<Canvas>(root, control.Parent.Name) ?? root;

    private static void Place(Canvas root, LayoutControl layout, Control control)
    {
        Canvas.SetLeft(control, layout.X);
        Canvas.SetTop(control, layout.Y);
        control.ZIndex = 1000;
        ParentCanvas(root, layout).Children.Add(control);
    }

    private static IEnumerable<LayoutControl> All(LayoutControl control) => control.Children.SelectMany(c => All(c).Prepend(c));

    private static bool IsShown(LayoutControl control)
    {
        for (LayoutControl c = control; c != null; c = c.Parent)
        {
            if (!c.Visible)
                return false;
        }

        return true;
    }

    private static void AddInteractiveControls(Canvas root, LayoutControl window, LobbyViewModelBase viewModel)
    {
        var rowControls = new List<(int Row, Control Control)>();

        foreach (LayoutControl layout in All(window).Where(c => c.Kind is LayoutControlKind.Other or LayoutControlKind.CheckBox && IsShown(c)).ToList())
        {
            Control control = layout.Name switch
            {
                "MapPreviewBox" => BuildMapPreview(layout, viewModel),
                "lbMapList" => BuildMapList(layout, viewModel),
                "ddGameMode" => BuildGameModes(layout, viewModel),
                "tbMapSearch" => BuildMapSearch(layout, viewModel),
                "lbChatMessages" when viewModel is MultiplayerRoomViewModel room => BuildChat(layout, room),
                "tbChatInput" when viewModel is MultiplayerRoomViewModel room => BuildChatInput(layout, room),
                "chkAutoReady" when viewModel is MultiplayerRoomViewModel room => BuildAutoReady(layout, room),
                _ when layout.Name.StartsWith("ddPlayer") => BuildPlayerColumn(layout, rowControls, viewModel),
                _ => BuildGameOption(layout, viewModel),
            };

            if (control != null)
                Place(root, layout, control);
        }

        void UpdateRows()
        {
            foreach ((int row, Control control) in rowControls)
            {
                PlayerRowViewModel rowViewModel = row < viewModel.Rows.Count ? viewModel.Rows[row] : null;
                control.DataContext = rowViewModel;
                if (rowViewModel == null && control is ThemedDropDown dropDown)
                {
                    dropDown.ItemsSource = null;
                    dropDown.SelectedIndex = -1;
                    dropDown.CanChange = false;
                }
            }
        }

        viewModel.Rows.CollectionChanged += (_, _) => UpdateRows();
        UpdateRows();
    }

    private static Control BuildPlayerColumn(LayoutControl layout, List<(int Row, Control Control)> rowControls, LobbyViewModelBase viewModel)
    {
        string field = new string(layout.Name["ddPlayer".Length..].TakeWhile(char.IsLetter).ToArray());
        int row = int.Parse(layout.Name[("ddPlayer".Length + field.Length)..]);

        var comboBox = new ThemedDropDown(layout.Width, layout.Height);
        (string items, string index, string enabled) = field switch
        {
            "Name" => ("DisplayNameItems", "DisplayNameIndex", "CanChangeName"),
            "Side" => ("SideItems", "SideIndex", "CanChangeOptions"),
            "Color" => ("ColorItems", "ColorIndex", "CanChangeOptions"),
            "Start" => ("StartItems", "StartIndex", "CanChangeOptions"),
            _ => ("TeamItems", "TeamIndex", "CanChangeOptions"),
        };

        comboBox.Bind(ThemedDropDown.ItemsSourceProperty, new Binding(items));
        comboBox.Bind(ThemedDropDown.SelectedIndexProperty, new Binding(index) { Mode = BindingMode.TwoWay });
        comboBox.Bind(ThemedDropDown.CanChangeProperty, new Binding(enabled));

        if (field == "Name")
        {
            // The ready status, as the XNA status indicator shows it
            ToolTip.SetTip(comboBox, null);
            comboBox.Bind(ToolTip.TipProperty, new Binding("Status"));
        }

        if (field == "Side")
        {
            // The side icons, as GameLobbyBase.AddSideToDropDown loads them
            comboBox.ItemDecoration = index =>
            {
                if (index < 0 || index >= viewModel.SideItems.Count)
                    return null;

                string side = viewModel.SideItems[index];
                string texture = index == 0 ? "randomicon.png" : side == "Spectator" ? "spectatoricon.png" : side + "icon.png";
                return ThemeAssets.LoadBitmap(texture) is { } icon ? new Image { Source = icon, Stretch = Stretch.None } : null;
            };
        }
        else if (field == "Color")
        {
            // The colour swatches of XNAClientColorDropDown (TextAndIcon)
            int width = Conversions.IntFromString(layout.Attributes.GetValueOrDefault("ColorTextureWidth"), 18);
            int height = Conversions.IntFromString(layout.Attributes.GetValueOrDefault("ColorTextureHeight"), 16);
            string randomTexture = layout.Attributes.GetValueOrDefault("RandomColorTexture", "randomicon.png");

            comboBox.ItemDecoration = index =>
            {
                if (index == 0)
                    return ThemeAssets.LoadBitmap(randomTexture) is { } random ? new Image { Source = random, Width = width, Height = height, Stretch = Stretch.Fill } : null;

                return index < viewModel.ColorValues.Count && viewModel.ColorValues[index] is (byte r, byte g, byte b)
                    ? new Border { Width = width, Height = height, Background = new SolidColorBrush(Color.FromRgb(r, g, b)) }
                    : null;
            };
        }

        rowControls.Add((row, comboBox));
        return comboBox;
    }

    private static Control BuildGameOption(LayoutControl layout, LobbyViewModelBase viewModel)
    {
        if (viewModel.CheckBoxOptions.FirstOrDefault(o => o.Option.Name == layout.Name) is CheckBoxOptionViewModel checkBox)
        {
            var control = new ThemedCheckBox(checkBox.Label) { DataContext = checkBox };
            control.Bind(ThemedCheckBox.IsCheckedProperty, new Binding(nameof(CheckBoxOptionViewModel.IsChecked)) { Mode = BindingMode.TwoWay });
            control.Bind(InputElement.IsEnabledProperty, new Binding(nameof(CheckBoxOptionViewModel.IsEnabled)));
            if (!string.IsNullOrEmpty(layout.ToolTip))
                ToolTip.SetTip(control, layout.ToolTip);
            return control;
        }

        if (viewModel.DropDownOptions.FirstOrDefault(o => o.Option.Name == layout.Name) is DropDownOptionViewModel dropDown)
        {
            var control = new ThemedDropDown(layout.Width > 0 ? layout.Width : 130, layout.Height > 0 ? layout.Height : DROP_DOWN_HEIGHT)
            {
                DataContext = dropDown,
                ItemsSource = dropDown.Items,
            };
            control.Bind(ThemedDropDown.SelectedIndexProperty, new Binding(nameof(DropDownOptionViewModel.SelectedIndex)) { Mode = BindingMode.TwoWay });
            control.Bind(ThemedDropDown.CanChangeProperty, new Binding(nameof(DropDownOptionViewModel.IsEnabled)));
            if (!string.IsNullOrEmpty(layout.ToolTip))
                ToolTip.SetTip(control, layout.ToolTip);
            return control;
        }

        return null;
    }

    private static Control BuildMapList(LayoutControl layout, LobbyViewModelBase viewModel)
    {
        ListBox list = ThemedStyle.List(layout.Width, layout.Height);
        list.DataContext = viewModel;
        list.Bind(ItemsControl.ItemsSourceProperty, new Binding(nameof(LobbyViewModelBase.Maps)));
        list.Bind(SelectingItemsControl.SelectedIndexProperty, new Binding(nameof(LobbyViewModelBase.SelectedMapIndex)) { Mode = BindingMode.TwoWay });
        list.Bind(InputElement.IsHitTestVisibleProperty, new Binding(nameof(LobbyViewModelBase.CanChangeMap)));
        list.ItemTemplate = new FuncDataTemplate<string>((text, _) => new TextBlock { Text = text, Margin = new Thickness(4, 1) });
        list.Resources["ListBoxItemPadding"] = new Thickness(0);
        return list;
    }

    private static Control BuildGameModes(LayoutControl layout, LobbyViewModelBase viewModel)
    {
        var dropDown = new ThemedDropDown(layout.Width, layout.Height) { DataContext = viewModel };
        dropDown.Bind(ThemedDropDown.ItemsSourceProperty, new Binding(nameof(LobbyViewModelBase.GameModes)));
        dropDown.Bind(ThemedDropDown.SelectedIndexProperty, new Binding(nameof(LobbyViewModelBase.SelectedGameModeIndex)) { Mode = BindingMode.TwoWay });
        dropDown.Bind(ThemedDropDown.CanChangeProperty, new Binding(nameof(LobbyViewModelBase.CanChangeMap)));
        return dropDown;
    }

    private static Control BuildMapSearch(LayoutControl layout, LobbyViewModelBase viewModel)
    {
        TextBox textBox = ThemedStyle.TextBox(layout.Width, layout.Height, layout.Attributes.GetValueOrDefault("Suggestion", string.Empty));
        textBox.DataContext = viewModel;
        textBox.Bind(TextBox.TextProperty, new Binding(nameof(LobbyViewModelBase.MapSearchText)) { Mode = BindingMode.TwoWay });
        return textBox;
    }

    private static Control BuildMapPreview(LayoutControl layout, LobbyViewModelBase viewModel)
    {
        viewModel.SetPreviewArea(layout.Width, layout.Height);

        var canvas = new Canvas { Width = layout.Width, Height = layout.Height, Background = ThemedStyle.PanelBackground, ClipToBounds = true, DataContext = viewModel };

        var image = new Image { Stretch = Stretch.Fill };
        image.Bind(Image.SourceProperty, new Binding(nameof(LobbyViewModelBase.Preview)));
        image.Bind(Canvas.LeftProperty, new Binding(nameof(LobbyViewModelBase.PreviewX)));
        image.Bind(Canvas.TopProperty, new Binding(nameof(LobbyViewModelBase.PreviewY)));
        image.Bind(Layoutable.WidthProperty, new Binding(nameof(LobbyViewModelBase.PreviewImageWidth)));
        image.Bind(Layoutable.HeightProperty, new Binding(nameof(LobbyViewModelBase.PreviewImageHeight)));
        canvas.Children.Add(image);

        var markers = new ItemsControl
        {
            Width = layout.Width,
            Height = layout.Height,
            ItemsPanel = new FuncTemplate<Panel>(() => new Canvas()),
            ItemTemplate = new FuncDataTemplate<StartMarkerViewModel>((marker, _) =>
            {
                var border = new Border
                {
                    Width = 20,
                    Height = 20,
                    CornerRadius = new CornerRadius(10),
                    Background = Brushes.Black,
                    BorderBrush = Brushes.White,
                    BorderThickness = new Thickness(2),
                    Child = new TextBlock
                    {
                        Text = marker?.Number.ToString(),
                        Foreground = Brushes.White,
                        FontSize = 11,
                        FontWeight = FontWeight.Bold,
                        HorizontalAlignment = HorizontalAlignment.Center,
                        VerticalAlignment = VerticalAlignment.Center,
                    },
                };
                ToolTip.SetTip(border, marker?.Players);
                Canvas.SetLeft(border, marker?.Left ?? 0);
                Canvas.SetTop(border, marker?.Top ?? 0);
                return new Canvas { Children = { border } };
            }),
        };
        markers.Bind(ItemsControl.ItemsSourceProperty, new Binding(nameof(LobbyViewModelBase.StartMarkers)));
        canvas.Children.Add(markers);

        return canvas;
    }

    private static Control BuildChat(LayoutControl layout, MultiplayerRoomViewModel room)
    {
        ListBox list = ThemedStyle.List(layout.Width, layout.Height);
        list.DataContext = room;
        list.Bind(ItemsControl.ItemsSourceProperty, new Binding(nameof(MultiplayerRoomViewModel.Messages)));
        list.ItemTemplate = new FuncDataTemplate<ChatLineViewModel>((line, _) => new TextBlock
        {
            Text = line?.Text,
            Foreground = line?.Brush,
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(4, 0),
        });
        list.Resources["ListBoxItemPadding"] = new Thickness(0);
        ChatScroll.SetFollowNewItems(list, true);
        return list;
    }

    private static Control BuildChatInput(LayoutControl layout, MultiplayerRoomViewModel room)
    {
        TextBox textBox = ThemedStyle.TextBox(layout.Width, layout.Height, layout.Attributes.GetValueOrDefault("Suggestion", "Type here to chat..."));
        textBox.DataContext = room;
        textBox.Bind(TextBox.TextProperty, new Binding(nameof(MultiplayerRoomViewModel.ChatInput)) { Mode = BindingMode.TwoWay });
        textBox.KeyBindings.Add(new KeyBinding { Gesture = new KeyGesture(Key.Enter), Command = room.SendChatCommand });
        return textBox;
    }

    private static Control BuildAutoReady(LayoutControl layout, MultiplayerRoomViewModel room)
    {
        var checkBox = new ThemedCheckBox(layout.Text) { DataContext = room };
        checkBox.Bind(ThemedCheckBox.IsCheckedProperty, new Binding(nameof(MultiplayerRoomViewModel.AutoReady)) { Mode = BindingMode.TwoWay });
        checkBox.Bind(Visual.IsVisibleProperty, new Binding(nameof(MultiplayerRoomViewModel.IsPlayer)));
        return checkBox;
    }
}
