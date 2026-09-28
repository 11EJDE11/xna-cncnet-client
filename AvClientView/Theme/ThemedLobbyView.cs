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
using Avalonia.Input.Platform;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.VisualTree;

using AvClientView.ViewModels;
using AvClientView.Views;

using ClientCore;
using ClientCore.Extensions;

using ClientLogic.Layout;
using ClientLogic.Lobby;
using ClientLogic.Statistics;

using DTAClient.Domain.Multiplayer;

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
    /// <param name="showMapList">The map list is shown (default: for the host; /HIDEMAPS hides it).</param>
    public static Control Build(LobbyViewModelBase viewModel, ThemedLobbyKind kind, bool isHost, Action<string, LayoutControl> onButton,
        bool? showMapList = null)
    {
        bool mapListShown = showMapList ?? isHost;
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
            string role = mapListShown ? "_Host" : "_Player";
            if (window.Find("lbChatMessages") is LayoutControl chat)
                reader.ReadInitializableControl(ini, chat, parser, "lbChatMessages" + role);
            if (window.Find("tbChatInput") is LayoutControl input)
                reader.ReadInitializableControl(ini, input, parser, "tbChatInput" + role);
        }

        AddPlayerRows(ini, reader, parser, window);

        // The map sort button, as GameLobbyBase.InitBtnMapSort adds it left of the game mode drop-down
        if (window.Find("ddGameMode") is LayoutControl gameMode)
        {
            var sortButton = new LayoutControl("btnMapSortAlphabetically", "XNAClientStateButton", LayoutControlKind.Other)
            {
                X = gameMode.X - gameMode.Height - 4,
                Y = gameMode.Y,
                Width = gameMode.Height,
                Height = gameMode.Height,
            };
            gameMode.Parent.AddChild(sortButton);
            reader.ReadInitializableControl(ini, sortButton, parser);
        }

        // Players don't see the map list (MultiplayerGameLobby.HideMapList), nor the host's tunnel and room settings
        // buttons (disabled XNA controls are hidden); the negotiation status button is only in CnCNet rooms, where the
        // room view shows it with dynamic tunnels
        var hidden = new List<string>();
        if (viewModel is not CnCNetGameRoomViewModel)
            hidden.Add("btnNegotiationStatus");
        if (kind.IsMultiplayer && !mapListShown)
            hidden.AddRange(["ddGameMode", "lblGameModeSelect", "lbMapList", "tbMapSearch", "btnPickRandomMap", "btnMapSortAlphabetically"]);
        if (kind.IsMultiplayer && !isHost)
            hidden.AddRange(["btnLockGame", "btnChangeTunnel", "btnGameLobbySettings", "btnSaveLoadGameOptions"]);

        foreach (string name in hidden)
        {
            if (window.Find(name) is LayoutControl control)
                control.Visible = false;
        }

        // The extra player options: only when the theme has the button (GameLobbyBase), with the panel's controls
        // created as PlayerExtraOptionsPanel.Initialize does
        bool hasExtraOptions = window.Find("btnPlayerExtraOptionsOpen") != null && window.Find("PlayerExtraOptionsPanel") != null;
        if (hasExtraOptions)
        {
            AddPlayerExtraOptionsControls(ini, reader, parser, window.Find("PlayerExtraOptionsPanel"));
            viewModel.EnableExtraOptionsPanel();
        }
        else if (window.Find("PlayerExtraOptionsPanel") is LayoutControl unusedPanel)
        {
            unusedPanel.Visible = false;
        }

        Canvas root = null;
        root = LayoutView.Build(window, name =>
        {
            if (hasExtraOptions && HandleExtraOptionsButton(root, name))
                return;

            onButton(name, window.Find(name));
        });
        AddInteractiveControls(root, window, viewModel);

        if (hasExtraOptions)
            BindPlayerExtraOptions(root, window, viewModel);

        if (kind.IsMultiplayer)
            AddStatusIndicators(root, window, ini, viewModel);

        // GameLaunchButton.InitStarDisplay: the rank the setup can earn, right of the text
        if (LayoutView.FindNamed<ThemedButton>(root, "btnLaunchGame") is ThemedButton launchButton)
        {
            Bitmap[] rankTextures = LobbyStatistics.RankTextureNames.Select(ThemeAssets.LoadBitmap).ToArray();
            void UpdateRank(object sender, EventArgs e) => launchButton.SetStar(rankTextures[viewModel.Rank]);
            root.AttachedToVisualTree += (_, _) => { viewModel.Refreshed += UpdateRank; UpdateRank(null, EventArgs.Empty); };
            root.DetachedFromVisualTree += (_, _) => viewModel.Refreshed -= UpdateRank;
            UpdateRank(null, EventArgs.Empty);
        }

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
                "btnMapSortAlphabetically" => BuildMapSortButton(layout, viewModel),
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

        void RowsRefreshed(object sender, EventArgs e) => UpdateRows();
        root.AttachedToVisualTree += (_, _) => { viewModel.Refreshed += RowsRefreshed; UpdateRows(); };
        root.DetachedFromVisualTree += (_, _) => viewModel.Refreshed -= RowsRefreshed;
        UpdateRows();
    }

    #region Extra player options

    private const int CHECKBOX_TEXTURE_SIZE = 18;

    /// <summary>
    /// The controls PlayerExtraOptionsPanel.Initialize creates (close button, header, the four "force" check boxes,
    /// auto allying with its help button, presets and the eight team-start drop-downs), then the theme's sections
    /// for each of them (GameLobbyBase reads the panel's children).
    /// </summary>
    private static void AddPlayerExtraOptionsControls(IniFile ini, XnaLayoutReader reader, LayoutExpressionParser parser, LayoutControl panel)
    {
        const int defaultX = 24;

        panel.SolidBackground = new ClientLogic.UI.ChatColor(0, 0, 0, 255);
        panel.DrawBorders = true;

        (int Width, int Height) CheckBoxSize(string text)
        {
            (int w, int h) = ThemeFonts.Measure(text, 0);
            return (w + 5 + CHECKBOX_TEXTURE_SIZE, Math.Max(h, CHECKBOX_TEXTURE_SIZE));
        }

        LayoutControl Add(string name, string type, int x, int y, int width, int height, string text = null)
        {
            var control = new LayoutControl(name, type, XnaLayoutReader.KindOf(type)) { X = x, Y = y, Width = width, Height = height, Text = text ?? string.Empty };
            panel.AddChild(control);
            return control;
        }

        LayoutControl CheckBox(string name, int y, string text)
        {
            (int w, int h) = CheckBoxSize(text);
            return Add(name, "XNAClientCheckBox", defaultX, y, w, h, text);
        }

        LayoutControl btnClose = Add("btnClose", "XNAClientButton", 0, 0, 0, 0);
        btnClose.IdleTexture = "optionsButtonClose.png";
        btnClose.HoverTexture = "optionsButtonClose_c.png";

        LayoutControl lblHeader = Add("lblHeader", "XNALabel", defaultX, 4, 0, 18, "Extra Player Options".L10N("Client:Main:ExtraPlayerOptions"));
        LayoutControl sides = CheckBox("chkBoxForceRandomSides", lblHeader.Y + lblHeader.Height + 4, "Force Random Sides".L10N("Client:Main:ForceRandomSides"));
        LayoutControl colors = CheckBox("chkBoxForceRandomColors", sides.Y + sides.Height + 4, "Force Random Colors".L10N("Client:Main:ForceRandomColors"));
        LayoutControl noTeams = CheckBox("chkBoxForceNoTeams", colors.Y + colors.Height + 4, "Force No Teams".L10N("Client:Main:ForceNoTeams"));
        LayoutControl starts = CheckBox("chkBoxForceRandomStarts", noTeams.Y + noTeams.Height + 4, "Force Random Starts".L10N("Client:Main:ForceRandomStarts"));
        LayoutControl useMappings = CheckBox("chkBoxUseTeamStartMappings", starts.Y + starts.Height + 20, "Enable Auto Allying:".L10N("Client:Main:EnableAutoAllying"));

        LayoutControl btnHelp = Add("btnHelp", "XNAClientButton", useMappings.X + useMappings.Width + 4, useMappings.Y - 1, 0, 0);
        btnHelp.IdleTexture = "questionMark.png";
        btnHelp.HoverTexture = "questionMark_c.png";

        string presetText = "Presets:".L10N("Client:Main:Presets");
        (int presetW, int presetH) = ThemeFonts.Measure(presetText, 0);
        LayoutControl lblPreset = Add("lblPreset", "XNALabel", useMappings.X, useMappings.Y + useMappings.Height + 8, presetW, presetH, presetText);
        LayoutControl ddPreset = Add("ddTeamStartMappingPreset", "XNAClientDropDown", lblPreset.X + 50, lblPreset.Y - 2, 160, 22);

        var mappingsPanel = new LayoutControl("teamStartMappingsPanel", "TeamStartMappingsPanel", LayoutControlKind.Panel)
        {
            X = lblPreset.X,
            Y = ddPreset.Y + ddPreset.Height + 8,
            Width = panel.Width,
            Height = panel.Height - (ddPreset.Y + ddPreset.Height) + 4,
        };
        panel.AddChild(mappingsPanel);

        // GetTeamMappingPanelRectangle: two per column, 50x22, 4 px apart
        const int mappingWidth = 50;
        const int mappingHeight = 22;
        for (int i = 0; i < TeamStartMappingsEditor.MAX_START_COUNT; i++)
        {
            int x = i < 2 ? 4 : i / 2 * (mappingWidth + 4) + 3;
            int y = i % 2 == 0 ? 0 : mappingHeight + 4;

            var mapping = new LayoutControl("teamStartMappingPanel" + (i + 1), "TeamStartMappingPanel", LayoutControlKind.Panel)
            {
                X = x,
                Y = y,
                Width = mappingWidth,
                Height = mappingHeight,
            };
            mappingsPanel.AddChild(mapping);

            mapping.AddChild(new LayoutControl("lblStart" + (i + 1), "XNALabel", LayoutControlKind.Label) { X = 0, Y = 0, Width = 10, Height = 22, Text = (i + 1).ToString() });
            mapping.AddChild(new LayoutControl("ddTeamStart" + (i + 1), "XNAClientDropDown", LayoutControlKind.Other) { X = 10, Y = -3, Width = 35, Height = 22 });
        }

        foreach (LayoutControl child in panel.Children.ToList())
            reader.ReadInitializableControl(ini, child, parser);

        foreach (LayoutControl control in All(panel))
            reader.Initialize(control);
    }

    /// <summary>The open, close and help buttons of the extra options; true if <paramref name="name"/> was one.</summary>
    private static bool HandleExtraOptionsButton(Canvas root, string name)
    {
        switch (name)
        {
            case "btnPlayerExtraOptionsOpen":
                SetExtraOptionsVisible(root, !(LayoutView.FindNamed<Canvas>(root, "PlayerExtraOptionsPanel")?.IsVisible ?? false));
                return true;
            case "btnClose":
                SetExtraOptionsVisible(root, false);
                return true;
            case "btnHelp":
                if (App.Services?.GetService(typeof(ClientLogic.UI.IDialogService)) is ClientLogic.UI.IDialogService dialogs)
                    ShowAutoAllyingHelp(dialogs);
                return true;
            default:
                return false;
        }
    }

    private static void ShowAutoAllyingHelp(ClientLogic.UI.IDialogService dialogs)
    {
        dialogs.ShowMessage("Auto Allying".L10N("Client:Main:AutoAllyingTitle"),
            ("Auto allying allows the host to assign starting locations to teams, not players.\n" +
            "When players are assigned to spawn locations, they will be auto assigned to teams based on these mappings.\n" +
            "This is best used with random teams and random starts. However, only random teams is required.\n" +
            "Manually specified starts will take precedence.").L10N("Client:Main:AutoAllyingText1") + "\n\n" +
            $"{TeamStartMapping.NO_PLAYER} : " + "Block this location from being randomly assigned to a player if there are spare locations.".L10N("Client:Main:AutoAllyingTextNoPlayerV2") + "\n" +
            $"{TeamStartMapping.NO_TEAM} : " + "Allow a player here, but don't assign a team.".L10N("Client:Main:AutoAllyingTextNoTeamV2"));
    }

    private static void SetExtraOptionsVisible(Canvas root, bool visible)
    {
        if (LayoutView.FindNamed<Canvas>(root, "PlayerExtraOptionsPanel") is Canvas panel)
        {
            panel.IsVisible = visible;
            panel.ZIndex = 5000;
        }
    }

    /// <summary>Binds the panel's controls to the lobby's extra options, as PlayerExtraOptionsPanel.Bind and SetIsHost do.</summary>
    private static void BindPlayerExtraOptions(Canvas root, LayoutControl window, LobbyViewModelBase viewModel)
    {
        PlayerExtraOptionsState state = viewModel.ExtraOptions;
        TeamStartMappingsEditor editor = viewModel.ExtraOptionsEditor;
        LayoutControl panel = window.Find("PlayerExtraOptionsPanel");

        void CheckBox(string name, string checkedPath, Func<bool> canChange)
        {
            if (panel.Find(name) is not LayoutControl layout || !layout.Visible)
                return;

            var checkBox = new ThemedCheckBox(layout.Text, layout.FontIndex) { DataContext = state };
            checkBox.Bind(ThemedCheckBox.IsCheckedProperty, new Binding(checkedPath) { Mode = BindingMode.TwoWay });

            void Refresh() => checkBox.IsEnabled = viewModel.CanChangeExtraOptions && canChange();
            state.PropertyChanged += (_, _) => Refresh();
            viewModel.PropertyChanged += (_, _) => Refresh();
            Refresh();
            Place(root, layout, checkBox);
        }

        CheckBox("chkBoxForceRandomSides", nameof(PlayerExtraOptionsState.ForceRandomSides), () => true);
        CheckBox("chkBoxForceRandomColors", nameof(PlayerExtraOptionsState.ForceRandomColors), () => true);
        CheckBox("chkBoxForceNoTeams", nameof(PlayerExtraOptionsState.ForceNoTeams), () => state.CanChangeForceNoTeams);
        CheckBox("chkBoxForceRandomStarts", nameof(PlayerExtraOptionsState.ForceRandomStarts), () => true);
        CheckBox("chkBoxUseTeamStartMappings", nameof(PlayerExtraOptionsState.UseTeamStartMappings), () => state.CanChangeUseTeamStartMappings);

        if (panel.Find("ddTeamStartMappingPreset") is LayoutControl presetLayout && presetLayout.Visible)
        {
            var presets = new ThemedDropDown(presetLayout.Width, 22, presetLayout.FontIndex);
            bool refreshing = false;
            void Refresh()
            {
                refreshing = true;
                presets.ItemsSource = editor.PresetNames;
                presets.SelectedIndex = editor.PresetIndex;
                presets.CanChange = editor.CanChangePreset;
                refreshing = false;
            }

            presets.PropertyChanged += (_, e) =>
            {
                if (e.Property == ThemedDropDown.SelectedIndexProperty && !refreshing)
                    editor.SelectPreset(presets.SelectedIndex);
            };
            editor.Changed += (_, _) => Refresh();
            Refresh();
            Place(root, presetLayout, presets);
        }

        for (int i = 0; i < TeamStartMappingsEditor.MAX_START_COUNT; i++)
        {
            int slot = i;
            if (panel.Find("ddTeamStart" + (i + 1)) is not LayoutControl layout || !layout.Visible)
                continue;

            var dropDown = new ThemedDropDown(layout.Width, 22, layout.FontIndex) { ItemsSource = TeamStartMappingsEditor.Teams };
            bool refreshing = false;
            void Refresh()
            {
                refreshing = true;
                dropDown.SelectedIndex = editor.GetTeamIndex(slot);
                dropDown.CanChange = editor.CanChangeSlot(slot);
                refreshing = false;
            }

            dropDown.PropertyChanged += (_, e) =>
            {
                if (e.Property == ThemedDropDown.SelectedIndexProperty && !refreshing)
                    editor.SelectTeam(slot, dropDown.SelectedIndex);
            };
            editor.Changed += (_, _) => Refresh();
            Refresh();
            Place(root, layout, dropDown);
        }

        // RefreshBtnPlayerExtraOptionsOpenTexture: the "active" textures while any extra option is set
        if (LayoutView.FindNamed<ThemedButton>(root, "btnPlayerExtraOptionsOpen") is ThemedButton openButton)
        {
            void RefreshButton()
            {
                bool isDefault = state.ToPlayerExtraOptions().IsDefault();
                openButton.SetTextures(isDefault ? "optionsButton.png" : "optionsButtonActive.png",
                    isDefault ? "optionsButton_c.png" : "optionsButtonActive_c.png");
            }

            state.Changed += (_, _) => RefreshButton();
            RefreshButton();
        }

        // XNA's PlayerExtraOptionsPanel starts hidden
        SetExtraOptionsVisible(root, false);
    }

    #endregion

    private static Control BuildPlayerColumn(LayoutControl layout, List<(int Row, Control Control)> rowControls, LobbyViewModelBase viewModel)
    {
        string field = new string(layout.Name["ddPlayer".Length..].TakeWhile(char.IsLetter).ToArray());
        int row = int.Parse(layout.Name[("ddPlayer".Length + field.Length)..]);

        var comboBox = new ThemedDropDown(layout.Width, layout.Height);
        (string items, string index, string enabled) = field switch
        {
            "Name" => ("DisplayNameItems", "DisplayNameIndex", "CanChangeName"),
            "Side" => ("SideItems", "SideIndex", "CanChangeSide"),
            "Color" => ("ColorItems", "ColorIndex", "CanChangeColor"),
            "Start" => ("StartItems", "StartIndex", "CanChangeStart"),
            _ => ("TeamItems", "TeamIndex", "CanChangeTeam"),
        };

        comboBox.Bind(ThemedDropDown.ItemsSourceProperty, new Binding(items));
        comboBox.Bind(ThemedDropDown.SelectedIndexProperty, new Binding(index) { Mode = BindingMode.TwoWay });
        comboBox.Bind(ThemedDropDown.CanChangeProperty, new Binding(enabled));

        if (field == "Name")
        {
            // The ready status, as the XNA status indicator shows it
            ToolTip.SetTip(comboBox, null);
            comboBox.Bind(ToolTip.TipProperty, new Binding("Status"));

            // CnCNetGameLobby.MultiplayerName_RightClick: the player menu, without Join
            if (viewModel is CnCNetGameRoomViewModel)
            {
                comboBox.AddHandler(InputElement.PointerPressedEvent, (_, e) =>
                {
                    if (!e.GetCurrentPoint(comboBox).Properties.IsRightButtonPressed ||
                        comboBox.DataContext is not PlayerRowViewModel rowViewModel || !rowViewModel.IsHuman ||
                        rowViewModel.PlayerName == ProgramConstants.PLAYERNAME ||
                        ProgramConstants.AI_PLAYER_NAMES.Contains(rowViewModel.PlayerName))
                        return;

                    var messages = (PrivateMessagesViewModel)App.Services.GetService(typeof(PrivateMessagesViewModel));
                    ThemedContextMenu.Open(comboBox, e.GetPosition(comboBox), messages.PlayerMenu(rowViewModel.PlayerName, allowJoin: false));
                    e.Handled = true;
                }, Avalonia.Interactivity.RoutingStrategies.Tunnel);
            }
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
            var control = new ThemedCheckBox(checkBox.Label, layout.FontIndex) { DataContext = checkBox };
            control.Bind(ThemedCheckBox.IsCheckedProperty, new Binding(nameof(CheckBoxOptionViewModel.IsChecked)) { Mode = BindingMode.TwoWay });
            control.Bind(InputElement.IsEnabledProperty, new Binding(nameof(CheckBoxOptionViewModel.IsEnabled)));
            if (!string.IsNullOrEmpty(layout.ToolTip))
                ToolTip.SetTip(control, layout.ToolTip);
            return control;
        }

        if (viewModel.DropDownOptions.FirstOrDefault(o => o.Option.Name == layout.Name) is DropDownOptionViewModel dropDown)
        {
            var control = new ThemedDropDown(layout.Width > 0 ? layout.Width : 130, layout.Height > 0 ? layout.Height : DROP_DOWN_HEIGHT, layout.FontIndex)
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

    private static Control BuildMapSortButton(LayoutControl layout, LobbyViewModelBase viewModel)
    {
        string[] textures = ["sortAlphaNone.png", "sortAlphaAsc.png", "sortAlphaDesc.png"];
        var image = new Image { Width = layout.Width, Height = layout.Height, Stretch = Stretch.Fill, Cursor = new Cursor(StandardCursorType.Hand) };
        ToolTip.SetTip(image, "Sort Maps Alphabetically");

        void Update() => image.Source = ThemeAssets.LoadBitmap(textures[Math.Clamp(viewModel.MapSortState, 0, 2)]);
        Update();

        image.PointerReleased += (_, _) =>
        {
            viewModel.CycleMapSort();
            Update();
        };
        return image;
    }

    /// <summary>
    /// The map list with the XNA columns: the rank icon of the best result on the map and the map name, under their
    /// headers.
    /// </summary>
    private static Control BuildMapList(LayoutControl layout, LobbyViewModelBase viewModel)
    {
        const int headerHeight = 19;
        var bitmapRankHeader = ThemeAssets.LoadBitmap("rank.png");
        Bitmap[] rankTextures = LobbyStatistics.RankTextureNames.Select(ThemeAssets.LoadBitmap).ToArray();
        int rankWidth = bitmapRankHeader?.PixelSize.Width ?? 22;
        (FontFamily headerFont, double headerSize) = ThemeFonts.Get(1);

        var container = new Canvas { Width = layout.Width, Height = layout.Height, Background = ThemedStyle.PanelBackground };

        var rankHeader = new Border
        {
            Width = rankWidth,
            Height = headerHeight,
            BorderBrush = new SolidColorBrush(ThemeAssets.PanelBorderColor),
            BorderThickness = new Thickness(1),
            Child = bitmapRankHeader == null ? null : new Image { Source = bitmapRankHeader, Stretch = Stretch.None },
        };
        var nameHeader = new Border
        {
            Width = layout.Width - rankWidth,
            Height = headerHeight,
            BorderBrush = new SolidColorBrush(ThemeAssets.PanelBorderColor),
            BorderThickness = new Thickness(1),
            Child = new TextBlock
            {
                Text = "MAP NAME",
                FontFamily = headerFont,
                FontSize = headerSize,
                Foreground = new SolidColorBrush(ThemeAssets.LabelColor),
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(3, 0),
            },
        };
        Canvas.SetLeft(nameHeader, rankWidth);
        container.Children.Add(rankHeader);
        container.Children.Add(nameHeader);

        ListBox list = ThemedStyle.List(layout.Width, layout.Height - headerHeight);
        list.Background = Brushes.Transparent;
        list.BorderThickness = new Thickness(1, 0, 1, 1);
        Canvas.SetTop(list, headerHeight);
        container.Children.Add(list);
        list.DataContext = viewModel;
        list.Bind(ItemsControl.ItemsSourceProperty, new Binding(nameof(LobbyViewModelBase.Maps)));
        list.Bind(SelectingItemsControl.SelectedIndexProperty, new Binding(nameof(LobbyViewModelBase.SelectedMapIndex)) { Mode = BindingMode.TwoWay });
        list.Bind(InputElement.IsHitTestVisibleProperty, new Binding(nameof(LobbyViewModelBase.CanChangeMap)));
        list.ItemTemplate = new FuncDataTemplate<MapListItem>((item, _) => new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Children =
            {
                new Image { Source = item == null ? null : rankTextures[item.RankIndex], Width = rankWidth, Stretch = Stretch.None },
                new TextBlock { Text = item?.Text, Margin = new Thickness(3, 1), VerticalAlignment = VerticalAlignment.Center },
            },
        });
        list.Resources["ListBoxItemPadding"] = new Thickness(0);

        // LbGameModeMapList_RightClick: select the map under the cursor and open the map menu
        list.AddHandler(InputElement.PointerPressedEvent, (_, e) =>
        {
            if (!e.GetCurrentPoint(list).Properties.IsRightButtonPressed)
                return;

            if ((e.Source as Visual)?.FindAncestorOfType<ListBoxItem>(includeSelf: true) is not ListBoxItem item)
                return;

            int index = list.IndexFromContainer(item);
            if (index < 0)
                return;

            viewModel.SelectedMapIndex = index;
            e.Handled = true;
            OpenMapListMenu(container, e.GetPosition(container), viewModel);
        }, Avalonia.Interactivity.RoutingStrategies.Tunnel);

        return container;
    }

    /// <summary>The map list's menu (GameLobbyBase.mapContextMenu).</summary>
    private static void OpenMapListMenu(Canvas container, Point position, LobbyViewModelBase viewModel)
    {
        async void Copy(string text)
        {
            try
            {
                if (TopLevel.GetTopLevel(container)?.Clipboard is { } clipboard)
                    await clipboard.SetTextAsync(text);
            }
            catch (Exception)
            {
                (App.Services?.GetService(typeof(ClientLogic.UI.IDialogService)) as ClientLogic.UI.IDialogService)?.ShowMessage(
                    "Error".L10N("Client:Main:Error"), "Unable to copy map name to clipboard.".L10N("Client:Main:ClipboardCopyMapNameFailed"));
            }
        }

        var items = new List<ThemedMenuItem>
        {
            new(viewModel.IsFavoriteMap ? "Remove Favorite".L10N("Client:Main:RemoveFavorite") : "Add Favorite".L10N("Client:Main:AddFavorite"),
                viewModel.ToggleFavoriteMap),
            new("Copy Map Name".L10N("Client:Main:CopyMapName"), () => Copy(viewModel.SelectedMapName)),
        };

        if (viewModel.SelectedMapUntranslatedName != viewModel.SelectedMapName)
            items.Add(new("Copy Original Name".L10N("Client:Main:CopyOriginalMapName"), () => Copy(viewModel.SelectedMapUntranslatedName)));

        if (viewModel.CanDeleteMap && App.Services?.GetService(typeof(ClientLogic.UI.IDialogService)) is ClientLogic.UI.IDialogService dialogs)
            items.Add(new("Delete Map".L10N("Client:Main:DeleteMap"), () => viewModel.DeleteSelectedMap(dialogs)));

        items.Add(new("Show in Folder".L10N("Client:Main:ShowInFolder"), viewModel.ShowMapInFolder));
        ThemedContextMenu.Open(container, position, items, 192);
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

    private static Control BuildMapPreview(LayoutControl layout, LobbyViewModelBase viewModel) =>
        new MapPreviewView(layout, viewModel, viewModel.GameOptionsIni);

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
        ThemedWindow.OpenLinkOnDoubleClick(list);
        return list;
    }

    /// <summary>btnSaveLoadGameOptions: the Load / Save menu, then the preset window.</summary>
    public static void OpenGameOptionPresetMenu(Control root, LobbyViewModelBase viewModel)
    {
        if (root is not Canvas canvas || LayoutView.FindNamed<ThemedButton>(canvas, "btnSaveLoadGameOptions") is not ThemedButton button)
            return;

        var presets = (GameOptionPresetsViewModel)App.Services.GetService(typeof(GameOptionPresetsViewModel));
        ThemedContextMenu.Open(button, new Point(0, button.Bounds.Height),
        [
            new("Load".L10N("Client:Main:ButtonLoad"), () => presets.Open(true, viewModel.LoadGameOptionPreset, viewModel.SaveGameOptionPreset)),
            new("Save".L10N("Client:Main:ButtonSave"), () => presets.Open(false, viewModel.LoadGameOptionPreset, viewModel.SaveGameOptionPreset)),
        ], 75);
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
