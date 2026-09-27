using System;
using System.Globalization;
using System.IO;
using System.Text;

using Avalonia;

using ClientCore;
using ClientCore.I18N;
using ClientCore.INIProcessing;

using DTAClient.Domain.Multiplayer.CnCNet;

using Rampastring.Tools;

namespace AvClientView;

/// <summary>
/// The Avalonia front end (preview). Install it in the game's Resources folder (e.g. Resources/BinariesAvalonia);
/// it finds the game folder the same way the XNA client does.
/// </summary>
internal static class Program
{
    [STAThread]
    public static void Main(string[] args)
    {
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
        InitializeClient();

        BuildAvaloniaApp().StartWithClassicDesktopLifetime(args);
    }

    public static AppBuilder BuildAvaloniaApp() => AppBuilder.Configure<App>()
        .UsePlatformDetect()
        .LogToTrace();

    /// <summary>
    /// The start-up steps the front end needs from the XNA client's PreStartup: working folder, log file, settings
    /// and translation.
    /// </summary>
    private static void InitializeClient()
    {
        Translation.InitialUICulture = CultureInfo.CurrentUICulture;
        CultureInfo.CurrentUICulture = new CultureInfo(ProgramConstants.HARDCODED_LOCALE_CODE);
        IniFile.DisallowDesktopIni = true;

        DirectoryInfo gameDirectory = SafePath.GetDirectory(ProgramConstants.GamePath);
        Environment.CurrentDirectory = gameDirectory.FullName;

        DirectoryInfo clientUserFilesDirectory = SafePath.GetDirectory(ProgramConstants.ClientUserFilesPath);
        if (!clientUserFilesDirectory.Exists)
            clientUserFilesDirectory.Create();

        FileInfo logFile = SafePath.GetFile(clientUserFilesDirectory.FullName, "avclient.log");
        ProgramConstants.LogFileName = logFile.FullName;
        if (logFile.Exists)
            logFile.Delete();

        Logger.Initialize(clientUserFilesDirectory.FullName, logFile.Name);
        Logger.WriteLogFile = true;
        Logger.Log("***Logfile for the Avalonia client (preview)***");

        UserINISettings.Initialize(ClientConfiguration.Instance.SettingsIniName);

        // The theme folder, as the XNA client's Startup sets it
        ProgramConstants.RESOURCES_DIR = SafePath.CombineDirectoryPath(ProgramConstants.BASE_RESOURCE_PATH, UserINISettings.Instance.ThemeFolderPath);

        // The theme's client settings (colours, layout constants), as the XNA client's Startup reloads them
        ClientConfiguration.Instance.RefreshSettings();

        // The INI preprocessor, as the XNA client's Startup starts it; launching the game waits for it
        PreprocessorBackgroundTask.Instance.Run();

        // The client resolution settings, with the XNA client's defaults from the screen
        new ClientLogic.Settings.ScreenResolutions(new AvClientView.Services.WindowsDisplayModeSource()).CreateClientResolutionSettings();

        // The updater's local file information, as the XNA client's Startup and loading screen set it up (no
        // update check): the campaign's modified-files warning needs it
        SafePath.DeleteFileIfExists(ProgramConstants.GamePath, "version_u");
        ClientUpdater.Updater.Initialize(ProgramConstants.GamePath, ProgramConstants.GetBaseResourcePath(),
            ClientConfiguration.Instance.SettingsIniName, ClientConfiguration.Instance.LocalGame,
            SafePath.GetFile(ProgramConstants.StartupExecutable).Name);
        System.Threading.Tasks.Task.Run(ClientUpdater.Updater.CheckLocalFileVersions);

        // Custom mission files, as the XNA client's PreStartup: leftovers of the last mission are removed
        DTAClient.Domain.CustomMissionHelper.Initialize();
        DTAClient.Domain.CustomMissionHelper.DeleteSupplementalMissionFiles();

        // The theme's textures and fonts are found as the XNA client finds them
        AvClientView.Theme.ThemeAssets.Initialize();

        try
        {
            FileInfo translationThemeFile = SafePath.GetFile(UserINISettings.Instance.TranslationThemeFolderPath, ClientConfiguration.Instance.TranslationIniName);
            FileInfo translationFile = SafePath.GetFile(UserINISettings.Instance.TranslationFolderPath, ClientConfiguration.Instance.TranslationIniName);

            if (translationFile.Exists)
            {
                var translation = new Translation(translationFile.FullName, UserINISettings.Instance.Translation);
                if (translationThemeFile.Exists)
                    translation.AppendValuesFromIniFile(translationThemeFile.FullName);

                Translation.Instance = translation;
            }

            Logger.Log("Loaded translation: " + Translation.Instance.Name);
        }
        catch (Exception ex)
        {
            Logger.Log("Failed to load the translation file. " + ex);
            Translation.Instance = new Translation(UserINISettings.Instance.Translation);
        }

        CultureInfo.CurrentUICulture = Translation.Instance.Culture;

        // The player name, as the XNA client's GameClass sets it (without the font check)
        string playerName = UserINISettings.Instance.PlayerName.Value.Trim();
        if (UserINISettings.Instance.AutoRemoveUnderscoresFromName)
            playerName = playerName.TrimEnd('_');

        if (string.IsNullOrEmpty(playerName))
            playerName = Environment.UserName.Substring(Environment.UserName.IndexOf('\\') + 1);

        ProgramConstants.PLAYERNAME = NameValidator.GetValidOfflineName(playerName);

        // The game version sent in game announcements, as the XNA client sets it from the updater's version file
        ProgramConstants.GAME_VERSION = ClientConfiguration.Instance.ModMode ? "N/A" :
            new IniFile(SafePath.CombineFilePath(ProgramConstants.GamePath, "version")).GetStringValue("DTA", "Version", "N/A");
        Logger.Log("Game version: " + ProgramConstants.GAME_VERSION);
    }
}
