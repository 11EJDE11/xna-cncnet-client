using System;

using ClientCore;

using CommunityToolkit.Mvvm.ComponentModel;

namespace ClientLogic.Settings;

/// <summary>
/// The options window's Storage tab, as DXMainClient's StorageOptionsPanel: limits for the client logs, the game logs
/// (when the game supports them) and single-player saved games, typed as text; invalid text keeps the old value.
/// </summary>
public sealed partial class StorageOptionsModel : OptionsPanelModel
{
    private const int MAX_KEPT_FILES_LIMIT = 100000;
    private const int MAX_FOLDER_SIZE_LIMIT_MB = 1024 * 1024;
    private const int MAX_AGE_DAYS_LIMIT = 3650;

    public StorageOptionsModel() : base("StorageOptionsPanel")
    {
    }

    public bool GameLogsSupported => GameLogManager.IsSupported;

    [ObservableProperty]
    private string maxKeptLogFiles;

    [ObservableProperty]
    private string maxLogFolderSize;

    [ObservableProperty]
    private string maxGameLogAge;

    [ObservableProperty]
    private string maxGameLogFolderSize;

    [ObservableProperty]
    private string maxKeptSavedGames;

    [ObservableProperty]
    private string maxSavedGameFolderSize;

    public override void Load()
    {
        base.Load();
        MaxKeptLogFiles = IniSettings.MaxKeptClientLogFiles.Value.ToString();
        MaxLogFolderSize = IniSettings.MaxClientLogFolderSizeMB.Value.ToString();
        MaxKeptSavedGames = IniSettings.MaxKeptSavedGames.Value.ToString();
        MaxSavedGameFolderSize = IniSettings.MaxSavedGameFolderSizeMB.Value.ToString();

        if (GameLogsSupported)
        {
            MaxGameLogAge = IniSettings.MaxGameLogAgeDays.Value.ToString();
            MaxGameLogFolderSize = IniSettings.MaxGameLogFolderSizeMB.Value.ToString();
        }
    }

    public override bool Save()
    {
        bool restartRequired = base.Save();

        IniSettings.MaxKeptClientLogFiles.Value = ParseLimit(MaxKeptLogFiles, IniSettings.MaxKeptClientLogFiles.Value, MAX_KEPT_FILES_LIMIT);
        IniSettings.MaxClientLogFolderSizeMB.Value = ParseLimit(MaxLogFolderSize, IniSettings.MaxClientLogFolderSizeMB.Value, MAX_FOLDER_SIZE_LIMIT_MB);
        IniSettings.MaxKeptSavedGames.Value = ParseLimit(MaxKeptSavedGames, IniSettings.MaxKeptSavedGames.Value, MAX_KEPT_FILES_LIMIT);
        IniSettings.MaxSavedGameFolderSizeMB.Value = ParseLimit(MaxSavedGameFolderSize, IniSettings.MaxSavedGameFolderSizeMB.Value, MAX_FOLDER_SIZE_LIMIT_MB);

        if (GameLogsSupported)
        {
            IniSettings.MaxGameLogAgeDays.Value = ParseLimit(MaxGameLogAge, IniSettings.MaxGameLogAgeDays.Value, MAX_AGE_DAYS_LIMIT);
            IniSettings.MaxGameLogFolderSizeMB.Value = ParseLimit(MaxGameLogFolderSize, IniSettings.MaxGameLogFolderSizeMB.Value, MAX_FOLDER_SIZE_LIMIT_MB);
        }

        return restartRequired;
    }

    /// <summary>A non-negative number, capped at the maximum; anything else keeps the previous value.</summary>
    public static int ParseLimit(string text, int previousValue, int maximum)
    {
        if (!int.TryParse(text?.Trim(), out int value) || value < 0)
            return previousValue;

        return Math.Min(value, maximum);
    }
}
