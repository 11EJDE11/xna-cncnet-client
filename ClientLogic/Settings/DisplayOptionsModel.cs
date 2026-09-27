using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

using ClientCore;
using ClientCore.Display;
using ClientCore.Enums;
using ClientCore.Extensions;
using ClientCore.I18N;

using CommunityToolkit.Mvvm.ComponentModel;

using DTAClient.Domain;

using Rampastring.Tools;

namespace ClientLogic.Settings;

/// <summary>
/// The options window's Display tab, as DXMainClient's DisplayOptionsPanel: in-game resolution, detail level,
/// renderer and windowed modes; client resolution, fullscreen and integer-scaled client, theme and language.
/// (TS's legacy compatibility-fix buttons and the DirectDraw compatibility check after a renderer change are not
/// ported.)
/// </summary>
public sealed partial class DisplayOptionsModel : OptionsPanelModel
{
    private const int DRAG_DISTANCE_DEFAULT = 4;
    private const int ORIGINAL_RESOLUTION_WIDTH = 640;

    private readonly DirectDrawWrapperManager directDrawWrapperManager;
    private readonly ScreenResolutions screenResolutions;
    private readonly List<DirectDrawWrapper> renderers;

    public DisplayOptionsModel(DirectDrawWrapperManager directDrawWrapperManager, ScreenResolutions screenResolutions)
        : base("DisplayOptionsPanel")
    {
        this.directDrawWrapperManager = directDrawWrapperManager;
        this.screenResolutions = screenResolutions;
        IsTiberianSun = ClientConfiguration.Instance.ClientGameType == ClientType.TS;

        IngameResolutions = screenResolutions.GetIngameResolutions().Select(r => r.ToString()).ToList();

        DetailLevels =
        [
            "Low".L10N("Client:DTAConfig:DetailLevelLow"),
            "Medium".L10N("Client:DTAConfig:DetailLevelMedium"),
            "High".L10N("Client:DTAConfig:DetailLevelHigh"),
        ];

        renderers = directDrawWrapperManager.GetRenderers(ClientConfiguration.Instance.GetOperatingSystemVersion()).ToList();
        RendererNames = renderers.Select(r => r.UIName).ToList();

        (List<ScreenResolution> clientResolutions, List<int> recommended) = screenResolutions.GetClientResolutions();
        ClientResolutions = clientResolutions.Select(r => r.ToString()).ToList();
        RecommendedClientResolutionIndexes = recommended;

        Themes = Enumerable.Range(0, ClientConfiguration.Instance.ThemeCount)
            .Select(i => ClientConfiguration.Instance.GetThemeInfoFromIndex(i).Name)
            .Select(name => (Text: name.L10N($"INI:Themes:{name}"), Tag: name))
            .ToList();

        Translations = Translation.GetTranslations().Select(t => (Text: t.Value, Tag: t.Key)).ToList();

        // As the XNA panel's Initialize: the fullscreen client starts checked, and the integer scaling from the INI
        BorderlessClient = true;
        integerScaledClient = IniSettings.IntegerScaledClient.Value;
    }

    public bool IsTiberianSun { get; }

    public IReadOnlyList<string> IngameResolutions { get; }

    public IReadOnlyList<string> DetailLevels { get; }

    /// <summary>The renderer names; a hidden renderer that is selected is added when loading.</summary>
    public List<string> RendererNames { get; }

    public IReadOnlyList<string> ClientResolutions { get; }

    /// <summary>The recommended client resolutions (shown with "(recommended)"), ascending.</summary>
    public IReadOnlyList<int> RecommendedClientResolutionIndexes { get; }

    public IReadOnlyList<(string Text, string Tag)> Themes { get; }

    public IReadOnlyList<(string Text, string Tag)> Translations { get; }

    [ObservableProperty]
    private int ingameResolutionIndex;

    [ObservableProperty]
    private int detailLevelIndex;

    [ObservableProperty]
    private int rendererIndex;

    [ObservableProperty]
    private bool windowedMode;

    [ObservableProperty]
    private bool borderlessWindowedMode;

    /// <summary>Borderless windowed mode needs windowed mode.</summary>
    [ObservableProperty]
    private bool canBorderlessWindowedMode;

    [ObservableProperty]
    private bool backBufferInVRAM;

    [ObservableProperty]
    private int clientResolutionIndex;

    /// <summary>The client resolution can only be picked for a windowed (not fullscreen) client.</summary>
    [ObservableProperty]
    private bool canChangeClientResolution;

    [ObservableProperty]
    private bool borderlessClient;

    [ObservableProperty]
    private bool integerScaledClient;

    [ObservableProperty]
    private int themeIndex;

    [ObservableProperty]
    private int translationIndex;

    partial void OnWindowedModeChanged(bool value)
    {
        if (value)
        {
            CanBorderlessWindowedMode = true;
            return;
        }

        CanBorderlessWindowedMode = false;
        BorderlessWindowedMode = false;
    }

    partial void OnBorderlessClientChanged(bool value)
    {
        if (value)
        {
            CanChangeClientResolution = false;
            int nativeIndex = ClientResolutions.ToList().IndexOf(screenResolutions.SafeFullScreenResolution.ToString());
            if (nativeIndex > -1)
                ClientResolutionIndex = nativeIndex;
        }
        else
        {
            CanChangeClientResolution = true;
            if (RecommendedClientResolutionIndexes.Count > 0)
                ClientResolutionIndex = RecommendedClientResolutionIndexes[^1];
        }
    }

    private DirectDrawWrapper SelectedRenderer => RendererIndex >= 0 && RendererIndex < renderers.Count ? renderers[RendererIndex] : null;

    private void LoadRenderer()
    {
        int index = renderers.FindIndex(r => r.InternalName == directDrawWrapperManager.SelectedRenderer.InternalName);
        if (index < 0 && directDrawWrapperManager.SelectedRenderer.Hidden)
        {
            renderers.Add(directDrawWrapperManager.SelectedRenderer);
            RendererNames.Add(directDrawWrapperManager.SelectedRenderer.UIName);
            index = renderers.Count - 1;
        }

        RendererIndex = index;
    }

    public override void Load()
    {
        base.Load();
        LoadRenderer();
        DetailLevelIndex = IniSettings.DetailLevel;

        string currentRes = IniSettings.IngameScreenWidth.Value + "x" + IniSettings.IngameScreenHeight.Value;
        int index = IngameResolutions.ToList().IndexOf(currentRes);
        IngameResolutionIndex = index > -1 ? index : 0;

        IniSettings.Win8CompatMode.Value = "No";

        DirectDrawWrapper renderer = SelectedRenderer;
        if (renderer != null && renderer.UsesCustomWindowedOption())
        {
            // Renderers with their own windowed mode setting in their config INI (e.g. DxWnd, CnC-DDraw)
            var rendererSettingsIni = new IniFile(SafePath.CombineFilePath(ProgramConstants.GamePath, renderer.ConfigFileName));
            WindowedMode = rendererSettingsIni.GetBooleanValue(renderer.WindowedModeSection, renderer.WindowedModeKey, false);

            if (!string.IsNullOrEmpty(renderer.BorderlessWindowedModeKey))
            {
                bool setting = rendererSettingsIni.GetBooleanValue(renderer.WindowedModeSection, renderer.BorderlessWindowedModeKey, false);
                BorderlessWindowedMode = renderer.IsBorderlessWindowedModeKeyReversed ? !setting : setting;
            }
            else
            {
                BorderlessWindowedMode = IniSettings.BorderlessWindowedMode;
            }
        }
        else
        {
            WindowedMode = IniSettings.WindowedMode;
            BorderlessWindowedMode = IniSettings.BorderlessWindowedMode;
        }

        string currentClientRes = IniSettings.ClientResolutionX.Value + "x" + IniSettings.ClientResolutionY.Value;
        int clientResIndex = ClientResolutions.ToList().IndexOf(currentClientRes);
        ClientResolutionIndex = clientResIndex > -1 ? clientResIndex : 0;

        BorderlessClient = IniSettings.BorderlessWindowedClient;

        int themeIndex = Themes.ToList().FindIndex(t => t.Tag == IniSettings.ClientTheme);
        ThemeIndex = themeIndex > -1 ? themeIndex : 0;

        foreach (string localeCode in new[] { IniSettings.Translation.Value, Translation.GetDefaultTranslationLocaleCode(), ProgramConstants.HARDCODED_LOCALE_CODE })
        {
            int translationIndex = Translations.ToList().FindIndex(t => localeCode.Equals(t.Tag, StringComparison.InvariantCultureIgnoreCase));
            if (translationIndex > -1)
            {
                TranslationIndex = translationIndex;
                break;
            }
        }

        BackBufferInVRAM = IsTiberianSun ? !IniSettings.BackBufferInVRAM : IniSettings.BackBufferInVRAM;
    }

    public override bool Save()
    {
        bool restartRequired = base.Save();

        IniSettings.DetailLevel.Value = DetailLevelIndex;

        ScreenResolution ingameRes = IngameResolutions[IngameResolutionIndex];
        (IniSettings.IngameScreenWidth.Value, IniSettings.IngameScreenHeight.Value) = ingameRes;

        // The drag selection distance scales with the resolution width, unless CustomDragDistance overrides it
        IniSettings.DragDistance.Value = IniSettings.CustomDragDistance.Value > 0
            ? IniSettings.CustomDragDistance.Value
            : ingameRes.Width / ORIGINAL_RESOLUTION_WIDTH * DRAG_DISTANCE_DEFAULT;

        DirectDrawWrapper newSelectedRenderer = SelectedRenderer;

        IniSettings.WindowedMode.Value = WindowedMode && !newSelectedRenderer.UsesCustomWindowedOption();
        IniSettings.BorderlessWindowedMode.Value = BorderlessWindowedMode &&
            string.IsNullOrEmpty(newSelectedRenderer.BorderlessWindowedModeKey);

        ScreenResolution clientRes = ClientResolutions[ClientResolutionIndex];
        if (clientRes.Width != IniSettings.ClientResolutionX.Value || clientRes.Height != IniSettings.ClientResolutionY.Value)
            restartRequired = true;

        (IniSettings.ClientResolutionX.Value, IniSettings.ClientResolutionY.Value) = clientRes;

        if (IniSettings.BorderlessWindowedClient.Value != BorderlessClient)
            restartRequired = true;

        IniSettings.BorderlessWindowedClient.Value = BorderlessClient;

        if (IniSettings.IntegerScaledClient.Value != IntegerScaledClient)
            restartRequired = true;

        IniSettings.IntegerScaledClient.Value = IntegerScaledClient;

        string theme = Themes[ThemeIndex].Tag;
        restartRequired = restartRequired || IniSettings.ClientTheme != theme;
        IniSettings.ClientTheme.Value = theme;

        string translation = Translations[TranslationIndex].Tag;
        bool updateTranslation = !IniSettings.Translation.ToString().Equals(translation, StringComparison.InvariantCultureIgnoreCase);
        restartRequired = restartRequired || updateTranslation;
        IniSettings.Translation.Value = translation;
        if (updateTranslation)
            IniSettings.TranslationGameFilesVersion.Value = string.Empty;

        IniSettings.BackBufferInVRAM.Value = IsTiberianSun ? !BackBufferInVRAM : BackBufferInVRAM;

        directDrawWrapperManager.Save(newSelectedRenderer);

        DirectDrawWrapper selected = directDrawWrapperManager.SelectedRenderer;
        if (selected.UsesCustomWindowedOption())
        {
            var rendererSettingsIni = new IniFile(SafePath.CombineFilePath(ProgramConstants.GamePath, selected.ConfigFileName));
            rendererSettingsIni.SetBooleanValue(selected.WindowedModeSection, selected.WindowedModeKey, WindowedMode);

            if (!string.IsNullOrEmpty(selected.BorderlessWindowedModeKey))
            {
                bool borderlessModeIniValue = BorderlessWindowedMode;
                if (selected.IsBorderlessWindowedModeKeyReversed)
                    borderlessModeIniValue = !borderlessModeIniValue;

                rendererSettingsIni.SetBooleanValue(selected.WindowedModeSection, selected.BorderlessWindowedModeKey, borderlessModeIniValue);
            }

            rendererSettingsIni.WriteIniFile();
        }

        if (IsTiberianSun && ClientConfiguration.Instance.CopyResolutionDependentLanguageDLL)
            CopyLanguageDll(ingameRes);

        return restartRequired;
    }

    private static void CopyLanguageDll(ScreenResolution ingameRes)
    {
        string destination = SafePath.CombineFilePath(ProgramConstants.GamePath, "Language.dll");
        FileInfo fileInfo = SafePath.GetFile(destination);
        if (fileInfo.Exists)
        {
            fileInfo.IsReadOnly = false;
            fileInfo.Delete();
        }

        string source = ingameRes.Width >= 1024 && ingameRes.Height >= 720 ? "language_1024x720.dll"
            : ingameRes.Width >= 800 && ingameRes.Height >= 600 ? "language_800x600.dll"
            : "language_640x480.dll";

        File.Copy(SafePath.CombineFilePath(ProgramConstants.GamePath, "Resources", source), destination);
    }
}
