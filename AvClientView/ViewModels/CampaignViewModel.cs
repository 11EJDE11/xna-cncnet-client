using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;

using Avalonia.Threading;

using ClientCore;
using ClientCore.Extensions;

using ClientLogic.Campaign;
using ClientLogic.Launch;
using ClientLogic.Options;
using ClientLogic.Settings;
using ClientLogic.UI;

using CommunityToolkit.Mvvm.ComponentModel;

using DTAClient.Domain;

using Rampastring.Tools;

namespace AvClientView.ViewModels;

/// <summary>A row of the mission list.</summary>
/// <param name="IsHeader">A header row (no scenario): drawn in ListBoxHeaderColor and can't be selected.</param>
public sealed record MissionItemViewModel(Mission Mission, string Text, string IconName, bool IsHeader, bool Enabled);

/// <summary>
/// The campaign screen, as the XNA CampaignSelector: the mission list (official missions), description, preview,
/// difficulty, the theme's campaign options and settings, the modified-files ("cheater") warning, and the launch.
/// </summary>
public sealed partial class CampaignViewModel : ObservableObject
{
    private const string SETTINGS_PATH = "Client/CampaignSettings.ini";

    private readonly GameProcessService gameProcess;
    private readonly IDialogService dialogs;
    private readonly IniFile gameOptionsIni;
    private CampaignCatalog catalog;
    private List<Mission> selectedMissions = [];
    private bool loaded;

    private readonly DiscordHandler discord;

    public CampaignViewModel(GameProcessService gameProcess, IDialogService dialogs, DiscordHandler discord)
    {
        this.discord = discord;
        this.gameProcess = gameProcess;
        this.dialogs = dialogs;
        gameOptionsIni = new IniFile(SafePath.CombineFilePath(ProgramConstants.GetBaseResourcePath(), ClientConfiguration.GAME_OPTIONS));
    }

    public ObservableCollection<MissionItemViewModel> Missions { get; } = [];

    /// <summary>The campaign check-box options (CampaignCheckBox), in the window's order.</summary>
    public List<GameOption> CheckBoxes { get; } = [];

    /// <summary>The campaign drop-down options (CampaignDropDown), in the window's order.</summary>
    public List<GameOption> DropDowns { get; } = [];

    /// <summary>The theme's user settings on the window (SettingCheckBox etc.).</summary>
    public List<UserSetting> UserSettings { get; } = [];

    /// <summary>Options that reset to their default after each game (ResetToDefaultOnGameExit).</summary>
    public HashSet<GameOption> ResetOnGameExit { get; } = [];

    [ObservableProperty]
    private bool isOpen;

    [ObservableProperty]
    private int selectedIndex = -1;

    [ObservableProperty]
    private string description = string.Empty;

    [ObservableProperty]
    private bool canLaunch;

    [ObservableProperty]
    private int difficulty;

    /// <summary>False while the mission runs (ToggleControls).</summary>
    [ObservableProperty]
    private bool controlsEnabled = true;

    /// <summary>The mission preview image's file (Resources/Mission Previews), or the default one; null without previews.</summary>
    [ObservableProperty]
    private string previewPath;

    [ObservableProperty]
    private bool showCheaterWindow;

    private Mission missionToLaunch;

    public static string MissionPreviewFolder => SafePath.CombineDirectoryPath(ProgramConstants.GetBaseResourcePath(), "Mission Previews");

    public static string DefaultMissionPreviewPath => SafePath.CombineFilePath(MissionPreviewFolder, "Default.png");

    public static bool MissionPreviewEnabled => File.Exists(DefaultMissionPreviewPath);

    /// <summary>A campaign option from the theme (the XNA GameSessionCheckBox / DropDown read its INI keys the same way).</summary>
    public GameOption AddOption(string name, bool isCheckBox, string label, IReadOnlyDictionary<string, string> attributes, string parentName)
    {
        var builder = new GameOptionDefinitionBuilder(name, isCheckBox ? GameOptionKind.CheckBox : GameOptionKind.DropDown, isLobbyOption: false,
            (attributeName, defaultValue) => ClientCore.I18N.Translation.Instance?.LookUp($"INI:Controls:{parentName}:{name}:{attributeName}",
                $"INI:Controls:Global:{name}:{attributeName}", defaultValue) ?? defaultValue);

        bool resetOnExit = false;
        foreach (KeyValuePair<string, string> attribute in attributes)
        {
            // CampaignCheckBox / CampaignDropDown: map code needs CopyMissionsToSpawnmapINI
            if (!ClientConfiguration.Instance.CopyMissionsToSpawnmapINI &&
                (isCheckBox && attribute.Key == "CustomIniPath" ||
                 !isCheckBox && attribute.Key == "DataWriteMode" && attribute.Value.ToUpper() == "MAPCODE"))
            {
                throw new Exception($"Campaign settings can't affect map code if {nameof(ClientConfiguration.Instance.CopyMissionsToSpawnmapINI)} is disabled!\n\n"
                    + $"Offending setting control: {name}");
            }

            if (isCheckBox && attribute.Key == "ResetToDefaultOnGameExit")
            {
                resetOnExit = Conversions.BooleanFromString(attribute.Value, false);
                continue;
            }

            builder.TryParse(null, attribute.Key, attribute.Value);
        }

        if (isCheckBox)
            builder.Label = label;

        var option = new GameOption(builder.Build(), builder.DefaultValue);
        (isCheckBox ? CheckBoxes : DropDowns).Add(option);
        if (resetOnExit)
            ResetOnGameExit.Add(option);

        return option;
    }

    /// <summary>The missions (read the first time they are needed; the load game window uses them too).</summary>
    public CampaignCatalog Catalog => catalog ??= CampaignCatalog.Load();

    /// <summary>Opens the window; the first time, the missions and settings are read.</summary>
    public void Open()
    {
        if (!loaded)
        {
            loaded = true;
            Difficulty = UserINISettings.Instance.Difficulty;
            LoadMissionsWithFilter(null, disableCustomMissions: true, disableOfficialMissions: false);
            LoadSettings();
        }

        IsOpen = true;
    }

    public void Cancel()
    {
        SaveSettings();
        IsOpen = false;
    }

    /// <summary>The Campaigns button (btnReturn, with the campaign tag selector): back to the tags.</summary>
    public event EventHandler ReturnRequested;

    public void Return()
    {
        IsOpen = false;
        ReturnRequested?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>Lists missions (CampaignSelector.LoadMissionsWithFilter).</summary>
    public void LoadMissionsWithFilter(ISet<string> selectedTags, bool disableCustomMissions = true, bool disableOfficialMissions = false)
    {
        selectedMissions = Catalog.Filter(selectedTags, disableCustomMissions, disableOfficialMissions);
        Missions.Clear();
        foreach (Mission mission in selectedMissions)
        {
            bool isHeader = mission.Enabled && string.IsNullOrEmpty(mission.Scenario);
            Missions.Add(new MissionItemViewModel(mission, mission.GUIName,
                string.IsNullOrEmpty(mission.IconPath) ? null : mission.IconPath + "icon.png", isHeader, mission.Enabled));
        }

        SelectedIndex = -1;
        OnSelectedIndexChanged(-1);
    }

    partial void OnSelectedIndexChanged(int value)
    {
        if (value < 0 || value >= selectedMissions.Count)
        {
            Description = string.Empty;
            UpdatePreview(string.Empty);
            CanLaunch = false;
            return;
        }

        Mission mission = selectedMissions[value];
        UpdatePreview(mission.PreviewImage);

        if (string.IsNullOrEmpty(mission.Scenario))
        {
            Description = string.Empty;
            CanLaunch = false;
            return;
        }

        Description = mission.GUIDescription;
        CanLaunch = mission.Enabled;
    }

    private void UpdatePreview(string previewFileName)
    {
        if (!MissionPreviewEnabled)
        {
            PreviewPath = null;
            return;
        }

        string path = string.IsNullOrEmpty(previewFileName) ? null : SafePath.CombineFilePath(MissionPreviewFolder, previewFileName);
        PreviewPath = path != null && File.Exists(path) ? path : DefaultMissionPreviewPath;
    }

    public void Launch()
    {
        if (!CanLaunch || !ControlsEnabled || SelectedIndex < 0)
            return;

        SaveSettings();
        Mission mission = selectedMissions[SelectedIndex];

        if (!ClientConfiguration.Instance.ModMode &&
            (!ClientUpdater.Updater.IsFileNonexistantOrOriginal(mission.Scenario) || AreFilesModified()))
        {
            // Confront the user by showing the cheater screen
            missionToLaunch = mission;
            ShowCheaterWindow = true;
            return;
        }

        LaunchMission(mission);
    }

    private static bool AreFilesModified() =>
        CampaignLauncher.FilesToCheck.Any(filePath => !ClientUpdater.Updater.IsFileNonexistantOrOriginal(filePath));

    public void CheaterYes()
    {
        ShowCheaterWindow = false;
        LaunchMission(missionToLaunch);
    }

    public void CheaterCancel() => ShowCheaterWindow = false;

    private void LaunchMission(Mission mission)
    {
        string difficultyName = CampaignLauncher.WriteSpawnFiles(mission, Difficulty, CheckBoxes, DropDowns, gameOptionsIni);
        UserINISettings.Instance.Difficulty.Value = Difficulty;
        UserINISettings.Instance.SaveSettings();

        if (ClientConfiguration.Instance.ReturnToMainMenuOnMissionLaunch)
            IsOpen = false;
        else
            ControlsEnabled = false;

        discord.UpdatePresence(mission.UntranslatedGUIName, difficultyName, mission.IconPath, true);
        gameProcess.GameProcessExited += GameProcessExited_Callback;
        gameProcess.Start(dialogs);
    }

    private void GameProcessExited_Callback() => Dispatcher.UIThread.Post(GameProcessExited);

    private void GameProcessExited()
    {
        gameProcess.GameProcessExited -= GameProcessExited_Callback;
        CustomMissionHelper.DeleteSupplementalMissionFiles();
        discord.UpdatePresence();

        if (!ClientConfiguration.Instance.ReturnToMainMenuOnMissionLaunch)
            ControlsEnabled = true;

        // ResetToDefaultOnGameExit
        foreach (GameOption option in CheckBoxes.Where(ResetOnGameExit.Contains))
            option.Value = option.Definition.DefaultValue != 0 ? 1 : 0;

        foreach (UserSetting setting in UserSettings.Where(s => s.ResetToDefaultOnGameExit))
            setting.ResetToDefault();

        SaveSettings();
    }

    private void SaveSettings()
    {
        UserSettings.ForEach(s => s.Save());
        UserINISettings.Instance.SaveSettings();
        SaveCampaignSettings();
    }

    private void SaveCampaignSettings()
    {
        if (!ClientConfiguration.Instance.SaveCampaignGameOptions)
            return;

        try
        {
            FileInfo settingsFileInfo = SafePath.GetFile(ProgramConstants.GamePath, SETTINGS_PATH);
            settingsFileInfo.Delete();
            var settingsIni = new IniFile(settingsFileInfo.FullName);

            foreach (GameOption dd in DropDowns)
                settingsIni.SetStringValue("GameOptions", dd.Name, dd.Value.ToString());

            foreach (GameOption cb in CheckBoxes)
                settingsIni.SetStringValue("GameOptions", cb.Name, cb.IsChecked.ToString());

            settingsIni.WriteIniFile();
        }
        catch (Exception ex)
        {
            Logger.Log($"Saving campaign settings failed! Reason: {ex}");
        }
    }

    private void LoadSettings()
    {
        UserSettings.ForEach(s => s.Load());

        if (!ClientConfiguration.Instance.SaveCampaignGameOptions)
            return;

        var settingsIni = new IniFile(SafePath.CombineFilePath(ProgramConstants.GamePath, SETTINGS_PATH));

        foreach (GameOption dd in DropDowns)
            dd.Value = settingsIni.GetIntValue("GameOptions", dd.Name, dd.Value);

        foreach (GameOption cb in CheckBoxes)
            cb.Value = settingsIni.GetBooleanValue("GameOptions", cb.Name, cb.IsChecked) ? 1 : 0;
    }
}
