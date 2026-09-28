using System;
using System.Collections.Generic;
using System.Linq;

using Avalonia;
using Avalonia.Controls;
using Avalonia.Data;
using Avalonia.Layout;
using Avalonia.Media;

using AvClientView.Theme;
using AvClientView.ViewModels;

using ClientCore;
using ClientCore.Extensions;

using ClientLogic.Layout;
using ClientLogic.Settings;

using Rampastring.Tools;

namespace AvClientView.Views;

/// <summary>
/// The XNA OptionsWindow over a darkening panel: the tab control, Save and Cancel, and one panel per tab, each laid
/// out as its XNA panel's Initialize does (with XNA's sizing: a check box has no size until it is added, a label
/// takes its text's size), then the theme's OptionsWindow.ini applied as XNA does (the window, then each panel's
/// section, its "{Name}ExtraControls" and its children). The theme's setting controls become
/// <see cref="UserSetting"/>s of their panel.
/// </summary>
public sealed class OptionsWindowView : Panel
{
    private const int CHECKBOX_TEXTURE_SIZE = 18;
    private const int CHECKBOX_TEXT_PADDING = 5;

    private readonly OptionsWindowViewModel viewModel;
    private readonly Dictionary<string, Func<LayoutControl, Control>> overlays = [];
    private readonly List<(string PanelName, int Index)> panelTabs = [];
    private Canvas windowCanvas;

    public OptionsWindowView(OptionsWindowViewModel viewModel)
    {
        this.viewModel = viewModel;
        ThemedStyle.Darken(this);
        IsVisible = false;

        viewModel.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(OptionsWindowViewModel.IsOpen))
                IsVisible = viewModel.IsOpen;
            else if (e.PropertyName == nameof(OptionsWindowViewModel.SelectedTab))
                ShowSelectedPanel();
        };

        try
        {
            Build();
        }
        catch (Exception ex)
        {
            Logger.Log("OptionsWindowView: building the options window failed: " + ex);
            Children.Add(new TextBlock { Text = "The options window could not be built: " + ex.Message, Foreground = Brushes.White });
        }
    }

    private static (int Width, int Height) Measure(string text, int fontIndex = 0) => ThemeFonts.Measure(text, fontIndex);

    /// <summary>An XNA check box's size once it is added: its text plus the texture, at least the texture's height.</summary>
    private static (int Width, int Height) CheckBoxSize(string text)
    {
        (int w, int h) = Measure(text);
        return string.IsNullOrEmpty(text)
            ? (CHECKBOX_TEXTURE_SIZE, CHECKBOX_TEXTURE_SIZE)
            : (w + CHECKBOX_TEXT_PADDING + CHECKBOX_TEXTURE_SIZE, Math.Max(h, CHECKBOX_TEXTURE_SIZE));
    }

    private static LayoutControl Label(LayoutControl parent, string name, int x, int y, string text, int fontIndex = 0)
    {
        LayoutControl label = ThemedWindow.Add(parent, name, "XNALabel", x, y, 0, 0, text);
        label.FontIndex = fontIndex;
        (label.Width, label.Height) = Measure(text, fontIndex);
        return label;
    }

    /// <summary>A check box; <paramref name="added"/> when the XNA panel adds it before placing the next control.</summary>
    private static LayoutControl CheckBox(LayoutControl parent, string name, int x, int y, string text, bool added)
    {
        LayoutControl checkBox = ThemedWindow.Add(parent, name, "XNAClientCheckBox", x, y, 0, 0, text);
        if (added)
            (checkBox.Width, checkBox.Height) = CheckBoxSize(text);

        return checkBox;
    }

    private static int Right(LayoutControl c) => c.X + c.Width;

    private static int Bottom(LayoutControl c) => c.Y + c.Height;

    private void Build()
    {
        int width = 576 + 92;
        var window = new LayoutControl("OptionsWindow", "XNAWindow", LayoutControlKind.Panel)
        {
            Width = width,
            Height = 475,
            BackgroundTexture = "optionsbg.png",
        };

        ThemedWindow.Add(window, "tabControl", "XNAClientTabControl", 12, 12, 0, 23);
        LayoutControl btnCancel = ThemedWindow.Add(window, "btnCancel", "XNAClientButton", window.Width - 104, window.Height - 35, 92, 23, "Cancel".L10N("Client:DTAConfig:ButtonCancel"));
        ThemedWindow.Add(window, "btnSave", "XNAClientButton", 12, btnCancel.Y, 92, 23, "Save".L10N("Client:DTAConfig:ButtonSave"));

        var panels = new (LayoutControl Panel, OptionsPanelModel Model)[]
        {
            (BuildDisplayPanel(window), viewModel.Display),
            (BuildAudioPanel(window), viewModel.Audio),
            (BuildGamePanel(window), viewModel.Game),
            (BuildCnCNetPanel(window), viewModel.CnCNet),
            (BuildStoragePanel(window), viewModel.Storage),
            (BuildUpdaterPanel(window), viewModel.Updater),
            (BuildComponentsPanel(window), viewModel.Components),
        };

        for (int i = 0; i < panels.Length; i++)
            panelTabs.Add((panels[i].Panel.Name, i));

        XnaLayoutReader reader = ThemedWindow.CreateReader();
        string iniPath = XnaLayoutReader.FindWindowIni(window.Name);
        if (iniPath != null)
        {
            var ini = new CCIniFile(iniPath);
            reader.ReadWindow(ini, window);

            foreach ((LayoutControl panel, OptionsPanelModel model) in panels)
            {
                XnaLayoutReader.AddExtraControls(ini, panel, panel.Name + "ExtraControls");
                foreach (LayoutControl child in panel.Children)
                    reader.ReadControl(ini, child);

                AddUserSettings(ini, panel, model);
            }
        }
        else
        {
            foreach ((LayoutControl panel, OptionsPanelModel model) in panels)
                AddUserSettings(null, panel, model);
        }

        foreach (LayoutControl control in ThemedWindow.All(window))
            reader.Initialize(control);

        overlays["tabControl"] = _ => TabControl();

        windowCanvas = ThemedWindow.Build(window, (name, layout) => OnButton(name, layout), overlays, readIni: false);
        BindComponentButtons();
        windowCanvas.HorizontalAlignment = HorizontalAlignment.Center;
        windowCanvas.VerticalAlignment = VerticalAlignment.Center;
        Children.Add(windowCanvas);
        ShowSelectedPanel();
    }

    /// <summary>
    /// The panel's setting controls: the ones it creates in code (already in its model) take the INI keys; the
    /// theme's SettingCheckBox / FileSettingCheckBox / SettingDropDown / FileSettingDropDown extra controls are added.
    /// </summary>
    private void AddUserSettings(IniFile ini, LayoutControl panel, OptionsPanelModel model)
    {
        foreach (LayoutControl control in panel.Children)
        {
            UserSetting setting = model.FindSetting(control.Name);
            if (setting == null)
            {
                setting = control.TypeName switch
                {
                    "SettingCheckBox" => new CheckBoxSetting(control.Name),
                    "FileSettingCheckBox" => new CheckBoxSetting(control.Name, isFileSetting: true),
                    "SettingDropDown" => new DropDownSetting(control.Name, panel.Name),
                    "FileSettingDropDown" => new DropDownSetting(control.Name, panel.Name, isFileSetting: true),
                    _ => null,
                };

                if (setting == null)
                    continue;

                model.AddSetting(setting);
            }

            foreach (KeyValuePair<string, string> attribute in control.Attributes)
                setting.TryParse(attribute.Key, attribute.Value);

            setting.ReadFiles(ini?.GetSection(control.Name));

            overlays[control.Name] = setting switch
            {
                CheckBoxSetting checkBox => layout => BoundCheckBox(layout, checkBox, nameof(CheckBoxSetting.IsChecked), nameof(CheckBoxSetting.AllowChecking)),
                DropDownSetting dropDown => layout => SettingDropDown(layout, dropDown),
                _ => null,
            };
        }

        model.LinkParents();
    }

    private void OnButton(string name, LayoutControl layout)
    {
        switch (name)
        {
            case "btnSave":
                viewModel.Save();
                break;
            case "btnCancel":
                viewModel.Cancel();
                break;
            case "btnConfigureHotkeys":
                viewModel.Hotkeys.Open();
                break;
            case "btnMoveUp":
                viewModel.Updater.MoveUp();
                break;
            case "btnMoveDown":
                viewModel.Updater.MoveDown();
                break;
            case "btnForceUpdate":
                if (viewModel.Updater.CanForceUpdate)
                    viewModel.ConfirmForceUpdate();
                break;
            default:
                if (viewModel.Components.Components.FirstOrDefault(row => "btn" + row.IniName == name) is { } component)
                    viewModel.Components.Click(component);
                else if (!string.IsNullOrEmpty(layout?.Url))
                    ThemeAssets.OpenUrl(layout.Url);
                break;
        }
    }

    private void ShowSelectedPanel()
    {
        if (windowCanvas == null)
            return;

        foreach ((string panelName, int index) in panelTabs)
        {
            if (LayoutView.FindNamed<Canvas>(windowCanvas, panelName) is Canvas panel)
                panel.IsVisible = index == viewModel.SelectedTab;
        }
    }

    private Control TabControl()
    {
        var tabs = new ThemedTabControl { DataContext = viewModel };
        for (int i = 0; i < OptionsWindowViewModel.TabNames.Count; i++)
            tabs.AddTab(OptionsWindowViewModel.TabNames[i], 92, viewModel.IsTabSelectable(i));

        tabs.Bind(ThemedTabControl.SelectedTabProperty, new Binding(nameof(OptionsWindowViewModel.SelectedTab)) { Mode = BindingMode.TwoWay });
        return tabs;
    }

    #region Controls

    private static Control WithToolTip(Control control, LayoutControl layout)
    {
        if (!string.IsNullOrEmpty(layout.ToolTip))
            ToolTip.SetTip(control, layout.ToolTip);

        return control;
    }

    private static ThemedCheckBox BoundCheckBox(LayoutControl layout, object source, string checkedPath, string enabledPath = null)
    {
        var checkBox = new ThemedCheckBox(layout.Text, layout.FontIndex) { DataContext = source };
        checkBox.Bind(ThemedCheckBox.IsCheckedProperty, new Binding(checkedPath) { Mode = BindingMode.TwoWay });
        if (enabledPath != null)
            checkBox.Bind(IsEnabledProperty, new Binding(enabledPath));

        WithToolTip(checkBox, layout);
        return checkBox;
    }

    private static ThemedDropDown BoundDropDown(LayoutControl layout, object source, IEnumerable<string> items, string indexPath,
        string canChangePath = null)
    {
        var dropDown = new ThemedDropDown(layout.Width, 22, layout.FontIndex) { DataContext = source, ItemsSource = items.ToList() };
        dropDown.Bind(ThemedDropDown.SelectedIndexProperty, new Binding(indexPath) { Mode = BindingMode.TwoWay });
        if (canChangePath != null)
            dropDown.Bind(ThemedDropDown.CanChangeProperty, new Binding(canChangePath));

        WithToolTip(dropDown, layout);
        return dropDown;
    }

    private static ThemedDropDown SettingDropDown(LayoutControl layout, DropDownSetting setting)
    {
        ThemedDropDown dropDown = BoundDropDown(layout, setting, setting.Items.Select(i => i.Text), nameof(DropDownSetting.SelectedIndex));
        dropDown.ItemSelectable = index => index >= 0 && index < setting.Items.Count && setting.Items[index].Selectable;
        return dropDown;
    }

    private static ThemedTrackbar BoundTrackbar(LayoutControl layout, object source, string valuePath, int min, int max)
    {
        var trackbar = new ThemedTrackbar(layout.Width, layout.Height > 0 ? layout.Height : 22, min, max) { DataContext = source };
        trackbar.Bind(ThemedTrackbar.ValueProperty, new Binding(valuePath) { Mode = BindingMode.TwoWay });
        return trackbar;
    }

    /// <summary>A value label (font 1) showing a number property.</summary>
    private static TextBlock BoundValueLabel(LayoutControl layout, object source, string valuePath)
    {
        (FontFamily family, double size) = ThemeFonts.Get(layout.FontIndex);
        var label = new TextBlock { FontFamily = family, FontSize = size, Foreground = new SolidColorBrush(ThemeAssets.LabelColor), DataContext = source };
        label.Bind(TextBlock.TextProperty, new Binding(valuePath));
        return label;
    }

    private static TextBox BoundTextBox(LayoutControl layout, object source, string textPath, int maxLength = 0)
    {
        TextBox textBox = ThemedStyle.TextBox(layout.Width, layout.Height, string.Empty);
        textBox.DataContext = source;
        textBox.MaxLength = maxLength;
        textBox.Bind(TextBox.TextProperty, new Binding(textPath) { Mode = BindingMode.TwoWay });
        return textBox;
    }

    #endregion

    #region Panels

    /// <summary>An option panel, as XNAOptionsPanel.Initialize places it: 12,47 in the window, black at alpha 128.</summary>
    private static LayoutControl Panel(LayoutControl window, string name)
    {
        var panel = new LayoutControl(name, "XNAOptionsPanel", LayoutControlKind.Panel)
        {
            X = 12,
            Y = 47,
            Width = window.Width - 24,
            Height = window.Height - 94,
            SolidBackground = new ClientLogic.UI.ChatColor(0, 0, 0, 128),
            DrawBorders = true,
        };

        window.AddChild(panel);
        return panel;
    }

    private LayoutControl BuildDisplayPanel(LayoutControl window)
    {
        DisplayOptionsModel model = viewModel.Display;
        LayoutControl panel = Panel(window, model.Name);

        LayoutControl lblIngameResolution = Label(panel, "lblIngameResolution", 12, 14, "In-game Resolution:".L10N("Client:DTAConfig:InGameResolution"));
        LayoutControl ddIngameResolution = ThemedWindow.Add(panel, "ddIngameResolution", "XNAClientDropDown", Right(lblIngameResolution) + 12, lblIngameResolution.Y - 2, 120, 19);

        LayoutControl lblDetailLevel = Label(panel, "lblDetailLevel", lblIngameResolution.X, Bottom(ddIngameResolution) + 16, "Detail Level:".L10N("Client:DTAConfig:DetailLevel"));
        LayoutControl ddDetailLevel = ThemedWindow.Add(panel, "ddDetailLevel", "XNAClientDropDown", ddIngameResolution.X, lblDetailLevel.Y - 2, ddIngameResolution.Width, ddIngameResolution.Height);

        LayoutControl lblRenderer = Label(panel, "lblRenderer", lblDetailLevel.X, Bottom(ddDetailLevel) + 16, "Renderer:".L10N("Client:DTAConfig:Renderer"));
        LayoutControl ddRenderer = ThemedWindow.Add(panel, "ddRenderer", "XNAClientDropDown", ddDetailLevel.X, lblRenderer.Y - 2, ddDetailLevel.Width, ddDetailLevel.Height);

        LayoutControl chkWindowedMode = CheckBox(panel, "chkWindowedMode", lblDetailLevel.X, Bottom(ddRenderer) + 16, "Windowed Mode".L10N("Client:DTAConfig:WindowedMode"), added: false);
        LayoutControl chkBorderlessWindowedMode = CheckBox(panel, "chkBorderlessWindowedMode", chkWindowedMode.X + 50, Bottom(chkWindowedMode) + 24,
            "Borderless Windowed Mode".L10N("Client:DTAConfig:BorderlessWindowedMode"), added: false);
        CheckBox(panel, "chkBackBufferInVRAM", lblDetailLevel.X, Bottom(chkBorderlessWindowedMode) + 28,
            "Back Buffer in Video Memory\n(lower performance, but is\nnecessary on some systems)".L10N("Client:DTAConfig:BackBuffer"), added: false);

        LayoutControl lblClientResolution = Label(panel, "lblClientResolution", 285, 14, "Client Resolution:".L10N("Client:DTAConfig:ClientResolution"));
        LayoutControl ddClientResolution = ThemedWindow.Add(panel, "ddClientResolution", "XNAClientPreferredItemDropDown",
            Right(lblClientResolution) + 12, lblClientResolution.Y - 2, panel.Width - (Right(lblClientResolution) + 24), ddIngameResolution.Height);

        CheckBox(panel, "chkBorderlessClient", lblClientResolution.X, lblDetailLevel.Y, "Fullscreen Client".L10N("Client:DTAConfig:FullscreenClient"), added: false);
        LayoutControl chkIntegerScaledClient = CheckBox(panel, "chkIntegerScaledClient", lblClientResolution.X, lblRenderer.Y, "Integer Scaled Client".L10N("Client:DTAConfig:IntegerScaledClient"), added: false);
        chkIntegerScaledClient.ToolTip = ("Enable integer scaling for the client. This will cause the client to use\n" +
            "the closest fitting resolution that is required to maintain sharp graphics,\n" +
            "at the expense of black borders that may appear at some resolutions.\n" +
            "Additionally, enabling this option will also allow the client window \n" +
            "to be resized (does not affect the selected client resolution).").L10N("Client:DTAConfig:IntegerScaledClientToolTip");

        LayoutControl lblClientTheme = Label(panel, "lblClientTheme", lblClientResolution.X, chkWindowedMode.Y, "Client Theme:".L10N("Client:DTAConfig:ClientTheme"));
        LayoutControl ddClientTheme = ThemedWindow.Add(panel, "ddClientTheme", "XNAClientDropDown", ddClientResolution.X, chkWindowedMode.Y, ddClientResolution.Width, ddRenderer.Height);

        LayoutControl lblTranslation = Label(panel, "lblTranslation", lblClientTheme.X, Bottom(ddClientTheme) + 16, "Language:".L10N("Client:DTAConfig:Language"));
        ThemedWindow.Add(panel, "ddTranslation", "XNAClientDropDown", ddClientTheme.X, lblTranslation.Y - 2, ddClientTheme.Width, ddClientTheme.Height);

        string recommended = "(recommended)".L10N("Client:DTAConfig:Recommended");
        List<string> clientResolutions = model.ClientResolutions
            .Select((text, i) => model.RecommendedClientResolutionIndexes.Contains(i) ? text + " " + recommended : text)
            .ToList();

        overlays["ddIngameResolution"] = layout => BoundDropDown(layout, model, model.IngameResolutions, nameof(DisplayOptionsModel.IngameResolutionIndex));
        overlays["ddDetailLevel"] = layout => BoundDropDown(layout, model, model.DetailLevels, nameof(DisplayOptionsModel.DetailLevelIndex));
        overlays["ddRenderer"] = layout =>
        {
            ThemedDropDown dropDown = BoundDropDown(layout, model, model.RendererNames, nameof(DisplayOptionsModel.RendererIndex));

            // A hidden renderer that is selected is added to the list when loading
            model.PropertyChanged += (_, e) =>
            {
                if (e.PropertyName == nameof(DisplayOptionsModel.RendererIndex))
                    dropDown.ItemsSource = model.RendererNames.ToList();
            };
            return dropDown;
        };
        overlays["chkWindowedMode"] = layout => BoundCheckBox(layout, model, nameof(DisplayOptionsModel.WindowedMode));
        overlays["chkBorderlessWindowedMode"] = layout => BoundCheckBox(layout, model, nameof(DisplayOptionsModel.BorderlessWindowedMode), nameof(DisplayOptionsModel.CanBorderlessWindowedMode));
        overlays["chkBackBufferInVRAM"] = layout => BoundCheckBox(layout, model, nameof(DisplayOptionsModel.BackBufferInVRAM));
        overlays["ddClientResolution"] = layout => BoundDropDown(layout, model, clientResolutions, nameof(DisplayOptionsModel.ClientResolutionIndex), nameof(DisplayOptionsModel.CanChangeClientResolution));
        overlays["chkBorderlessClient"] = layout => BoundCheckBox(layout, model, nameof(DisplayOptionsModel.BorderlessClient));
        overlays["chkIntegerScaledClient"] = layout => BoundCheckBox(layout, model, nameof(DisplayOptionsModel.IntegerScaledClient));
        overlays["ddClientTheme"] = layout => BoundDropDown(layout, model, model.Themes.Select(t => t.Text), nameof(DisplayOptionsModel.ThemeIndex));
        overlays["ddTranslation"] = layout => BoundDropDown(layout, model, model.Translations.Select(t => t.Text), nameof(DisplayOptionsModel.TranslationIndex));
        return panel;
    }

    private LayoutControl BuildAudioPanel(LayoutControl window)
    {
        const int PADDING_X = 12;
        const int PADDING_Y = 14;
        const int TRACKBAR_X_PADDING = 16;
        const int TRACKBAR_Y_PADDING = 16;
        const int TRACKBAR_Y_OFFSET = 2;
        const int TRACKBAR_HEIGHT = 22;
        const int CHECKBOX_SPACING = 4;
        const int GROUP_SPACING = 22;

        AudioOptionsModel model = viewModel.Audio;
        LayoutControl panel = Panel(window, model.Name);
        int valueX = panel.Width - Measure("0", 1).Width - PADDING_X;

        LayoutControl lblScoreVolume = Label(panel, "lblScoreVolume", PADDING_X, PADDING_Y, "Music Volume:".L10N("Client:DTAConfig:MusicVolume"));
        LayoutControl lblScoreVolumeValue = Label(panel, "lblScoreVolumeValue", valueX, lblScoreVolume.Y, "0", 1);
        LayoutControl trbScoreVolume = ThemedWindow.Add(panel, "trbScoreVolume", "XNATrackbar", Right(lblScoreVolume) + TRACKBAR_X_PADDING,
            lblScoreVolume.Y - TRACKBAR_Y_OFFSET, lblScoreVolumeValue.X - TRACKBAR_X_PADDING - Right(lblScoreVolume) - TRACKBAR_X_PADDING, TRACKBAR_HEIGHT);

        LayoutControl lblSoundVolume = Label(panel, "lblSoundVolume", lblScoreVolume.X, Bottom(trbScoreVolume) + TRACKBAR_Y_PADDING + TRACKBAR_Y_OFFSET, "Sound Volume:".L10N("Client:DTAConfig:SoundVolume"));
        Label(panel, "lblSoundVolumeValue", valueX, lblSoundVolume.Y, "0", 1);
        LayoutControl trbSoundVolume = ThemedWindow.Add(panel, "trbSoundVolume", "XNATrackbar", trbScoreVolume.X, Bottom(trbScoreVolume) + TRACKBAR_Y_PADDING, trbScoreVolume.Width, trbScoreVolume.Height);

        LayoutControl lblVoiceVolume = Label(panel, "lblVoiceVolume", lblScoreVolume.X, Bottom(trbSoundVolume) + TRACKBAR_Y_PADDING + TRACKBAR_Y_OFFSET, "Voice Volume:".L10N("Client:DTAConfig:VoiceVolume"));
        Label(panel, "lblVoiceVolumeValue", valueX, lblVoiceVolume.Y, "0", 1);
        LayoutControl trbVoiceVolume = ThemedWindow.Add(panel, "trbVoiceVolume", "XNATrackbar", trbScoreVolume.X, Bottom(trbSoundVolume) + TRACKBAR_Y_PADDING, trbScoreVolume.Width, trbScoreVolume.Height);

        LayoutControl chkScoreShuffle = CheckBox(panel, "chkScoreShuffle", lblScoreVolume.X, Bottom(trbVoiceVolume) + TRACKBAR_Y_PADDING, "Shuffle Music".L10N("Client:DTAConfig:ShuffleMusic"), added: true);

        LayoutControl lblClientVolume = Label(panel, "lblClientVolume", lblScoreVolume.X, Bottom(chkScoreShuffle) + GROUP_SPACING + TRACKBAR_Y_OFFSET, "Client Volume:".L10N("Client:DTAConfig:ClientVolume"));
        Label(panel, "lblClientVolumeValue", valueX, lblClientVolume.Y, "0", 1);
        LayoutControl trbClientVolume = ThemedWindow.Add(panel, "trbClientVolume", "XNATrackbar", trbScoreVolume.X, lblClientVolume.Y - TRACKBAR_Y_OFFSET, trbScoreVolume.Width, trbScoreVolume.Height);

        LayoutControl chkMainMenuMusic = CheckBox(panel, "chkMainMenuMusic", lblScoreVolume.X, Bottom(trbClientVolume) + PADDING_Y, "Main menu music".L10N("Client:DTAConfig:MainMenuMusic"), added: true);
        LayoutControl chkStopMusicOnMenu = CheckBox(panel, "chkStopMusicOnMenu", lblScoreVolume.X, Bottom(chkMainMenuMusic) + CHECKBOX_SPACING,
            "Don't play main menu music in lobbies".L10N("Client:DTAConfig:NoLobbiesMusic"), added: true);
        LayoutControl chkStopGameLobbyMessageAudio = CheckBox(panel, "chkStopGameLobbyMessageAudio", lblScoreVolume.X, Bottom(chkStopMusicOnMenu) + CHECKBOX_SPACING,
            "Don't play lobby message audio when game is running".L10N("Client:DTAConfig:NoGameLobbyMessageAudio"), added: true);
        CheckBox(panel, "chkPlaySoundOnGameHosted", lblScoreVolume.X, Bottom(chkStopGameLobbyMessageAudio) + CHECKBOX_SPACING,
            "Play sound when a game is hosted".L10N("Client:DTAConfig:PlaySoundGameHosted"), added: true);

        const int min = AudioOptionsModel.VOLUME_MIN;
        const int max = AudioOptionsModel.VOLUME_MAX;
        overlays["trbScoreVolume"] = layout => BoundTrackbar(layout, model, nameof(AudioOptionsModel.ScoreVolume), min, max);
        overlays["trbSoundVolume"] = layout => BoundTrackbar(layout, model, nameof(AudioOptionsModel.SoundVolume), min, max);
        overlays["trbVoiceVolume"] = layout => BoundTrackbar(layout, model, nameof(AudioOptionsModel.VoiceVolume), min, max);
        overlays["trbClientVolume"] = layout => BoundTrackbar(layout, model, nameof(AudioOptionsModel.ClientVolume), min, max);
        overlays["lblScoreVolumeValue"] = layout => BoundValueLabel(layout, model, nameof(AudioOptionsModel.ScoreVolume));
        overlays["lblSoundVolumeValue"] = layout => BoundValueLabel(layout, model, nameof(AudioOptionsModel.SoundVolume));
        overlays["lblVoiceVolumeValue"] = layout => BoundValueLabel(layout, model, nameof(AudioOptionsModel.VoiceVolume));
        overlays["lblClientVolumeValue"] = layout => BoundValueLabel(layout, model, nameof(AudioOptionsModel.ClientVolume));
        overlays["chkScoreShuffle"] = layout => BoundCheckBox(layout, model, nameof(AudioOptionsModel.ScoreShuffle));
        overlays["chkMainMenuMusic"] = layout => BoundCheckBox(layout, model, nameof(AudioOptionsModel.MainMenuMusic));
        overlays["chkStopMusicOnMenu"] = layout => BoundCheckBox(layout, model, nameof(AudioOptionsModel.StopMusicOnMenu), nameof(AudioOptionsModel.CanStopMusicOnMenu));
        overlays["chkStopGameLobbyMessageAudio"] = layout => BoundCheckBox(layout, model, nameof(AudioOptionsModel.StopGameLobbyMessageAudio));
        overlays["chkPlaySoundOnGameHosted"] = layout => BoundCheckBox(layout, model, nameof(AudioOptionsModel.PlaySoundOnGameHosted));
        return panel;
    }

    private LayoutControl BuildGamePanel(LayoutControl window)
    {
        GameOptionsModel model = viewModel.Game;
        LayoutControl panel = Panel(window, model.Name);

        LayoutControl lblScrollRate = Label(panel, "lblScrollRate", 12, 14, "Scroll Rate:".L10N("Client:DTAConfig:ScrollRate"));
        LayoutControl lblScrollRateValue = Label(panel, "lblScrollRateValue", panel.Width - Measure("0", 1).Width - 12, lblScrollRate.Y, "0", 1);
        LayoutControl trbScrollRate = ThemedWindow.Add(panel, "trbScrollRate", "XNATrackbar", Right(lblScrollRate) + 32, lblScrollRate.Y - 2,
            lblScrollRateValue.X - Right(lblScrollRate) - 47, 22);

        LayoutControl chkScrollCoasting = CheckBox(panel, "chkScrollCoasting", lblScrollRate.X, Bottom(trbScrollRate) + 20, "Scroll Coasting".L10N("Client:DTAConfig:ScrollCoasting"), added: false);
        LayoutControl chkTargetLines = CheckBox(panel, "chkTargetLines", lblScrollRate.X, Bottom(chkScrollCoasting) + 24, "Target Lines".L10N("Client:DTAConfig:TargetLines"), added: false);

        string playerNameText = "Player Name*:".L10N("Client:DTAConfig:PlayerName");
        LayoutControl lblPlayerName;
        if (model.IsTiberianSun)
        {
            LayoutControl chkTooltips = CheckBox(panel, "chkTooltips", lblScrollRate.X, Bottom(chkTargetLines) + 24, "Tooltips".L10N("Client:DTAConfig:Tooltips"), added: false);
            LayoutControl chkBlackChatBackground = CheckBox(panel, "chkBlackChatBackground", chkScrollCoasting.X, Bottom(chkTooltips) + 24,
                "Use black background for in-game chat messages".L10N("Client:DTAConfig:TSUseBlackBackgroundChat"), added: true);
            LayoutControl chkAltToUndeploy = CheckBox(panel, "chkAltToUndeploy", chkScrollCoasting.X, Bottom(chkBlackChatBackground) + 24,
                "Undeploy units by holding Alt key instead of a regular move command".L10N("Client:DTAConfig:TSUndeployAltKey"), added: true);
            lblPlayerName = Label(panel, "lblPlayerName", lblScrollRate.X, Bottom(chkAltToUndeploy) + 30, playerNameText);
        }
        else
        {
            LayoutControl chkShowHiddenObjects = CheckBox(panel, "chkShowHiddenObjects", lblScrollRate.X, Bottom(chkTargetLines) + 24,
                "Show Hidden Objects".L10N("Client:DTAConfig:YRShowHidden"), added: true);
            LayoutControl chkTooltips = CheckBox(panel, "chkTooltips", lblScrollRate.X, Bottom(chkShowHiddenObjects) + 24, "Tooltips".L10N("Client:DTAConfig:Tooltips"), added: false);
            lblPlayerName = Label(panel, "lblPlayerName", lblScrollRate.X, Bottom(chkTooltips) + 30, playerNameText);
        }

        ThemedWindow.Add(panel, "tbPlayerName", "XNATextBox", trbScrollRate.X, lblPlayerName.Y - 2, 200, 19);
        LayoutControl lblNotice = Label(panel, "lblNotice", lblPlayerName.X, Bottom(lblPlayerName) + 30,
            "* If you are currently connected to CnCNet, you need to log out and reconnect\nfor your new name to be applied.".L10N("Client:DTAConfig:ReconnectAfterRename"));
        ThemedWindow.Add(panel, "btnConfigureHotkeys", "XNAClientButton", lblPlayerName.X, Bottom(lblNotice) + 36, 160, 23, "Configure Hotkeys".L10N("Client:DTAConfig:ConfigureHotkeys"));

        overlays["trbScrollRate"] = layout => BoundTrackbar(layout, model, nameof(GameOptionsModel.ScrollRate), 0, GameOptionsModel.MAX_SCROLL_RATE);
        overlays["lblScrollRateValue"] = layout => BoundValueLabel(layout, model, nameof(GameOptionsModel.ScrollRate));
        overlays["tbPlayerName"] = layout => BoundTextBox(layout, model, nameof(GameOptionsModel.PlayerName), model.MaxNameLength);
        return panel;
    }

    /// <summary>DXMainClient's ComponentsPanel: a label and an install / update / uninstall button per component.</summary>
    private LayoutControl BuildComponentsPanel(LayoutControl window)
    {
        ComponentsOptionsModel model = viewModel.Components;
        LayoutControl panel = Panel(window, model.Name);

        int componentIndex = 0;
        foreach (ComponentRow row in model.Components)
        {
            LayoutControl button = ThemedWindow.Add(panel, "btn" + row.IniName, "XNAClientButton", panel.Width - 145, 12 + componentIndex * 35,
                133, 23, row.ButtonText);
            Label(panel, "lbl" + row.IniName, 12, button.Y + 2, row.Name);
            componentIndex++;
        }

        return panel;
    }

    /// <summary>The component buttons follow their rows' texts (downloading, installed...).</summary>
    private void BindComponentButtons()
    {
        foreach (ComponentRow row in viewModel.Components.Components)
        {
            if (LayoutView.FindNamed<ThemedButton>(windowCanvas, "btn" + row.IniName) is not ThemedButton button)
                continue;

            button.Text = row.ButtonText;
            row.PropertyChanged += (_, e) =>
            {
                if (e.PropertyName == nameof(ComponentRow.ButtonText))
                    button.Text = row.ButtonText;
            };
        }
    }

    /// <summary>DXMainClient's UpdaterOptionsPanel: the mirror list with Move Up / Down, auto check, Force Update.</summary>
    private LayoutControl BuildUpdaterPanel(LayoutControl window)
    {
        UpdaterOptionsModel model = viewModel.Updater;
        LayoutControl panel = Panel(window, model.Name);

        LayoutControl lblDescription = Label(panel, "lblDescription", 12, 12,
            ("To change download server priority, select a server from the list and\nuse the Move Up / Down buttons to change its priority.").L10N("Client:DTAConfig:ServerPriorityTip"));
        LayoutControl lbUpdateServerList = ThemedWindow.Add(panel, "lblUpdateServerList", "XNAListBox", lblDescription.X,
            Bottom(lblDescription) + 12, panel.Width - 24, 100);
        lbUpdateServerList.SolidBackground = new ClientLogic.UI.ChatColor(0, 0, 0, 128);
        LayoutControl btnMoveUp = ThemedWindow.Add(panel, "btnMoveUp", "XNAClientButton", lbUpdateServerList.X, Bottom(lbUpdateServerList) + 12,
            133, 23, "Move Up".L10N("Client:DTAConfig:MoveUp"));
        LayoutControl btnMoveDown = ThemedWindow.Add(panel, "btnMoveDown", "XNAClientButton", Right(lbUpdateServerList) - 133, btnMoveUp.Y,
            133, 23, "Move Down".L10N("Client:DTAConfig:MoveDown"));
        CheckBox(panel, "chkAutoCheck", lblDescription.X, Bottom(btnMoveUp) + 24,
            "Check for updates automatically".L10N("Client:DTAConfig:AutoCheckUpdate"), added: true);
        ThemedWindow.Add(panel, "btnForceUpdate", "XNAClientButton", btnMoveDown.X, Bottom(btnMoveDown) + 24,
            133, 23, "Force Update".L10N("Client:DTAConfig:ForceUpdate"));

        // The list keeps its XNA name (lblUpdateServerList) so a theme's section still applies
        overlays["lblUpdateServerList"] = layout =>
        {
            ListBox list = ThemedWindow.List(layout, model, nameof(UpdaterOptionsModel.Mirrors));
            list.Bind(Avalonia.Controls.Primitives.SelectingItemsControl.SelectedIndexProperty, new Binding(nameof(UpdaterOptionsModel.SelectedMirrorIndex)) { Mode = BindingMode.TwoWay });
            return list;
        };
        overlays["chkAutoCheck"] = layout => BoundCheckBox(layout, model, nameof(UpdaterOptionsModel.AutoCheck));
        return panel;
    }

    private LayoutControl BuildCnCNetPanel(LayoutControl window)
    {
        CnCNetOptionsModel model = viewModel.CnCNet;
        LayoutControl panel = Panel(window, model.Name);

        LayoutControl Check(string name, int x, int y, string text, string path, string enabledPath = null)
        {
            LayoutControl checkBox = CheckBox(panel, name, x, y, text, added: true);
            overlays[name] = layout => BoundCheckBox(layout, model, path, enabledPath);
            return checkBox;
        }

        LayoutControl chkPingUnofficialTunnels = Check("chkPingUnofficialTunnels", 12, 12, "Ping unofficial CnCNet tunnels".L10N("Client:DTAConfig:PingUnofficial"), nameof(CnCNetOptionsModel.PingUnofficialTunnels));
        LayoutControl chkWriteInstallPathToRegistry = Check("chkWriteInstallPathToRegistry", chkPingUnofficialTunnels.X, Bottom(chkPingUnofficialTunnels) + 12,
            ("Write game installation path to Windows\nRegistry (makes it possible to join\nother games' game rooms on CnCNet)").L10N("Client:DTAConfig:WriteGameRegistry"),
            nameof(CnCNetOptionsModel.WriteInstallPathToRegistry));
        LayoutControl chkDisableMainMenuHotkeys = Check("chkDisableMainMenuHotkeys", chkPingUnofficialTunnels.X, Bottom(chkWriteInstallPathToRegistry) + 12,
            "Disable main menu hotkeys".L10N("Client:DTAConfig:DisableMainMenuHotkeys"), nameof(CnCNetOptionsModel.DisableMainMenuHotkeys));
        chkDisableMainMenuHotkeys.ToolTip = "With this setting active, main menu buttons can only be clicked and will not respond to keyboard shortcuts to prevent accidental presses.".L10N("Client:DTAConfig:DisableMainMenuHotkeysTooltip");
        LayoutControl chkNotifyOnUserListChange = Check("chkNotifyOnUserListChange", chkPingUnofficialTunnels.X, Bottom(chkDisableMainMenuHotkeys) + 12,
            "Show player join / quit messages\non CnCNet lobby".L10N("Client:DTAConfig:ShowPlayerJoinQuit"), nameof(CnCNetOptionsModel.NotifyOnUserListChange));
        LayoutControl chkDisablePrivateMessagePopup = Check("chkDisablePrivateMessagePopup", chkPingUnofficialTunnels.X, Bottom(chkNotifyOnUserListChange) + 8,
            "Disable popups from private messages".L10N("Client:DTAConfig:DisablePMPopup"), nameof(CnCNetOptionsModel.DisablePrivateMessagePopup));

        LayoutControl lblAllPrivateMessagesFrom = Label(panel, "lblAllPrivateMessagesFrom", chkDisablePrivateMessagePopup.X, Bottom(chkDisablePrivateMessagePopup) + 12,
            "Allow private messages from:".L10N("Client:DTAConfig:AllowPMFrom"));
        (lblAllPrivateMessagesFrom.Width, lblAllPrivateMessagesFrom.Height) = (165, 0);
        ThemedWindow.Add(panel, "ddAllowPrivateMessagesFrom", "XNAClientDropDown", Right(lblAllPrivateMessagesFrom) - 110, lblAllPrivateMessagesFrom.Y + 22, 110, 22);

        LayoutControl chkSkipLoginWindow = Check("chkSkipLoginWindow", 276, 12, "Skip login dialog".L10N("Client:DTAConfig:SkipLoginDialog"), nameof(CnCNetOptionsModel.SkipLoginWindow));
        LayoutControl chkPersistentMode = Check("chkPersistentMode", chkSkipLoginWindow.X, Bottom(chkSkipLoginWindow) + 9,
            "Stay connected outside of the CnCNet lobby".L10N("Client:DTAConfig:StayConnect"), nameof(CnCNetOptionsModel.PersistentMode));
        LayoutControl chkConnectOnStartup = Check("chkConnectOnStartup", chkSkipLoginWindow.X, Bottom(chkPersistentMode) + 9,
            "Connect automatically on client startup".L10N("Client:DTAConfig:ConnectOnStart"), nameof(CnCNetOptionsModel.ConnectOnStartup), nameof(CnCNetOptionsModel.CanConnectOnStartup));
        LayoutControl chkDiscordIntegration = CheckBox(panel, "chkDiscordIntegration", chkSkipLoginWindow.X, Bottom(chkConnectOnStartup) + 9,
            "Show detailed game info in Discord status".L10N("Client:DTAConfig:DiscordStatus"), added: true);
        overlays["chkDiscordIntegration"] = layout =>
        {
            ThemedCheckBox checkBox = BoundCheckBox(layout, model, nameof(CnCNetOptionsModel.DiscordIntegration));
            checkBox.IsEnabled = !model.DiscordIntegrationGloballyDisabled;
            return checkBox;
        };
        LayoutControl chkAllowGameInvitesFromFriendsOnly = Check("chkAllowGameInvitesFromFriendsOnly", chkDiscordIntegration.X, Bottom(chkDiscordIntegration) + 9,
            "Only receive game invitations from friends".L10N("Client:DTAConfig:FriendsOnly"), nameof(CnCNetOptionsModel.AllowGameInvitesFromFriendsOnly));
        LayoutControl chkSteamIntegration = Check("chkSteamIntegration", chkAllowGameInvitesFromFriendsOnly.X, Bottom(chkAllowGameInvitesFromFriendsOnly) + 9,
            "Show the game being played in Steam".L10N("Client:DTAConfig:SteamStatus"), nameof(CnCNetOptionsModel.SteamIntegration));

        LayoutControl lblTunnelMode = Label(panel, "lblTunnelMode", chkSteamIntegration.X, Bottom(chkSteamIntegration) + 12, "Tunnel mode when hosting:".L10N("Client:DTAConfig:TunnelMode"));
        (lblTunnelMode.Width, lblTunnelMode.Height) = (165, 0);
        LayoutControl ddTunnelMode = ThemedWindow.Add(panel, "ddTunnelMode", "XNAClientDropDown", lblTunnelMode.X, lblTunnelMode.Y + 22, 220, 22);
        Check("chkEnableP2P", ddTunnelMode.X, Bottom(ddTunnelMode) + 9, "Enable direct P2P connections".L10N("Client:DTAConfig:EnableP2P"), nameof(CnCNetOptionsModel.EnableP2P));

        overlays["ddAllowPrivateMessagesFrom"] = layout => BoundDropDown(layout, model, CnCNetOptionsModel.PrivateMessageOptions.Select(o => o.Text), nameof(CnCNetOptionsModel.AllowPrivateMessagesFromIndex));
        overlays["ddTunnelMode"] = layout => BoundDropDown(layout, model, CnCNetOptionsModel.TunnelModeOptions.Select(o => o.Text), nameof(CnCNetOptionsModel.TunnelModeIndex));

        BuildFollowedGames(panel, model);
        return panel;
    }

    /// <summary>
    /// The "Show game rooms from the following games" panel: at "Bottom - 185" of the CnCNet panel (XNA uses the
    /// panel's Bottom, 47 px lower than its height would put it), games in columns of four with their icons.
    /// </summary>
    private void BuildFollowedGames(LayoutControl panel, CnCNetOptionsModel model)
    {
        const int gameListPanelHeight = 185;
        const int maxGamesPerColumn = 4;
        const int columnBuffer = 20;
        const int rowBuffer = 22;
        const int gameIconWidth = 16;
        const int gameIconBuffer = 6;

        var gameListPanel = new LayoutControl("gameListPanel", "XNAPanel", LayoutControlKind.Panel)
        {
            X = 0,
            Y = panel.Y + panel.Height - gameListPanelHeight,
            Width = panel.Width,
            Height = gameListPanelHeight,
        };
        panel.AddChild(gameListPanel);

        LayoutControl lblFollowedGames = Label(gameListPanel, "lblFollowedGames", 12, 12, "Show game rooms from the following games:".L10N("Client:DTAConfig:ShowRoomFromGame"));
        int startY = Bottom(lblFollowedGames) + 12;

        List<List<FollowedGameSetting>> columns = model.FollowedGames
            .Select((game, i) => (game, i))
            .GroupBy(x => x.i / maxGamesPerColumn)
            .Select(g => g.Select(x => x.game).ToList())
            .ToList();

        int columnOffset = 0;
        foreach (List<FollowedGameSetting> column in columns)
        {
            int columnWidth = 0;
            for (int row = 0; row < column.Count; row++)
            {
                FollowedGameSetting game = column[row];
                string text = game.Game.UIName;
                int checkBoxRight = gameIconWidth + gameIconBuffer + CheckBoxSize(text).Width;
                columnWidth = Math.Max(columnWidth, checkBoxRight + columnBuffer);

                string name = "gamePanel" + game.Name;
                ThemedWindow.Add(gameListPanel, name, "GamePanel", lblFollowedGames.X + columnOffset, startY + row * rowBuffer, checkBoxRight, gameIconWidth, text);
                overlays[name] = layout =>
                {
                    var row = new Canvas { Width = layout.Width, Height = layout.Height };
                    if (ThemeAssets.GameIcon(game.Game) is { } icon)
                        row.Children.Add(new Image { Source = icon, Width = gameIconWidth, Height = gameIconWidth });

                    ThemedCheckBox checkBox = BoundCheckBox(layout, game, nameof(FollowedGameSetting.IsFollowed), nameof(FollowedGameSetting.CanChange));
                    Canvas.SetLeft(checkBox, gameIconWidth + gameIconBuffer);
                    row.Children.Add(checkBox);
                    return row;
                };
            }

            columnOffset += columnWidth;
        }
    }

    private LayoutControl BuildStoragePanel(LayoutControl window)
    {
        const int TEXT_BOX_WIDTH = 70;
        const int TEXT_BOX_HEIGHT = 21;
        const int TEXT_BOX_X = 170;
        const int ROW_SPACING = 30;
        const int HEADER_SPACING = 11;
        const int SECTION_SPACING = 12;

        StorageOptionsModel model = viewModel.Storage;
        LayoutControl panel = Panel(window, model.Name);

        // Rows: (label name, label text, text box name, suffix label name, suffix text, model property, max length)
        int Section(int y, string headerName, string header,
            (string Label, string LabelText, string Box, string Suffix, string SuffixText, string Path, int MaxLength)[] rows,
            string hintName = null, string hint = null)
        {
            LayoutControl lblHeader = Label(panel, headerName, 12, y, header, 1);
            int rowY = Bottom(lblHeader) + HEADER_SPACING;
            LayoutControl last = null;
            foreach ((string labelName, string labelText, string boxName, string suffixName, string suffixText, string path, int maxLength) in rows)
            {
                LayoutControl label = Label(panel, labelName, 12, rowY, labelText);
                last = ThemedWindow.Add(panel, boxName, "XNATextBox", TEXT_BOX_X, label.Y - 4, TEXT_BOX_WIDTH, TEXT_BOX_HEIGHT);
                Label(panel, suffixName, Right(last) + 8, label.Y, suffixText);
                overlays[boxName] = layout => BoundTextBox(layout, model, path, maxLength);
                rowY = label.Y + ROW_SPACING;
            }

            if (hint == null)
                return Bottom(last) + SECTION_SPACING;

            LayoutControl lblHint = Label(panel, hintName, 12, rowY, hint);
            return Bottom(lblHint) + SECTION_SPACING;
        }

        string keepAtMost = "Keep at most:".L10N("Client:DTAConfig:StorageKeepAtMost");
        string maxSize = "Maximum size:".L10N("Client:DTAConfig:StorageMaxSize");
        string mbSuffix = "MB  (0 = no limit)".L10N("Client:DTAConfig:StorageMaxSizeSuffix");

        int y = Section(14, "lblLogsHeader", "Client Logs".L10N("Client:DTAConfig:StorageLogsHeader"),
        [
            ("lblKeptLogFiles", keepAtMost, "tbMaxKeptLogFiles", "lblKeptLogFilesSuffix",
                "old log files  (0 = no limit)".L10N("Client:DTAConfig:StorageKeepLogsAtMostSuffix"), nameof(StorageOptionsModel.MaxKeptLogFiles), 6),
            ("lblLogFolderSize", maxSize, "tbMaxLogFolderSize", "lblLogFolderSizeSuffix", mbSuffix, nameof(StorageOptionsModel.MaxLogFolderSize), 7),
        ]);

        if (model.GameLogsSupported)
        {
            y = Section(y, "lblGameLogsHeader", "Game Logs".L10N("Client:DTAConfig:StorageGameLogsHeader"),
            [
                ("lblGameLogAge", "Delete after:".L10N("Client:DTAConfig:StorageDeleteAfter"), "tbMaxGameLogAge", "lblGameLogAgeSuffix",
                    "days  (0 = never)".L10N("Client:DTAConfig:StorageDeleteAfterDaysSuffix"), nameof(StorageOptionsModel.MaxGameLogAge), 4),
                ("lblGameLogFolderSize", maxSize, "tbMaxGameLogFolderSize", "lblGameLogFolderSizeSuffix", mbSuffix, nameof(StorageOptionsModel.MaxGameLogFolderSize), 7),
            ], "lblGameLogRetentionHint", "The debug folder: crash snapshots and sync files.".L10N("Client:DTAConfig:StorageGameLogRetentionHint"));
        }

        Section(y, "lblSavedGamesHeader", "Single-Player Saved Games".L10N("Client:DTAConfig:StorageSavedGamesHeader"),
        [
            ("lblKeptSavedGames", keepAtMost, "tbMaxKeptSavedGames", "lblKeptSavedGamesSuffix",
                "saved games  (0 = no limit)".L10N("Client:DTAConfig:StorageKeepSavedGamesAtMostSuffix"), nameof(StorageOptionsModel.MaxKeptSavedGames), 6),
            ("lblSavedGameFolderSize", maxSize, "tbMaxSavedGameFolderSize", "lblSavedGameFolderSizeSuffix", mbSuffix, nameof(StorageOptionsModel.MaxSavedGameFolderSize), 7),
        ], "lblSavedGameRetentionHint", "Oldest saves are permanently deleted.".L10N("Client:DTAConfig:StorageSavedGameRetentionHint"));

        return panel;
    }

    #endregion
}
