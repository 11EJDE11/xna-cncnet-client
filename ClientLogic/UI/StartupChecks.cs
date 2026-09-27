#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;

using ClientCore;
using ClientCore.Enums;
using ClientCore.Extensions;
using ClientCore.I18N;

using Rampastring.Tools;

namespace ClientLogic.UI;

/// <summary>A message for the user: its title and text.</summary>
public sealed record StartupMessage(string Title, string Text);

/// <summary>
/// The checks the main menu runs once it's shown (the XNA MainMenu's CheckRequiredFiles, CheckForbiddenFiles,
/// CheckIfFirstRun and CheckAndApplyTranslationGameFiles).
/// </summary>
public static class StartupChecks
{
    /// <summary>The required files (RequiredFiles) that are missing, as a message; null if none are.</summary>
    public static StartupMessage? MissingRequiredFiles()
    {
        List<string> absentFiles = ClientConfiguration.Instance.RequiredFiles.ToList()
            .FindAll(f => !string.IsNullOrWhiteSpace(f) && !SafePath.GetFile(ProgramConstants.GamePath, f).Exists);

        if (absentFiles.Count == 0)
            return null;

        string description;
        if (ClientConfiguration.Instance.ClientGameType == ClientType.Ares)
        {
            description = ("You are missing Yuri's Revenge files that are required\n" +
                "to play this mod! Yuri's Revenge mods are not standalone,\n" +
                "so you need a copy of following Yuri's Revenge (v.1.001)\n" +
                "files placed in the mod folder to play the mod:").L10N("Client:Main:MissingFilesText1Ares");
        }
        else
        {
            description = "The following required files are missing:".L10N("Client:Main:MissingFilesText1NonAres");
        }

        description += Environment.NewLine + Environment.NewLine +
            string.Join(Environment.NewLine, absentFiles) +
            Environment.NewLine + Environment.NewLine +
            "You won't be able to play without those files.".L10N("Client:Main:MissingFilesText2");

        return new StartupMessage("Missing Files".L10N("Client:Main:MissingFilesTitle"), description);
    }

    /// <summary>The interfering files (ForbiddenFiles) that are present, as a message; null if none are.</summary>
    public static StartupMessage? InterferingFiles()
    {
        List<string> presentFiles = ClientConfiguration.Instance.ForbiddenFiles.ToList()
            .FindAll(f => !string.IsNullOrWhiteSpace(f) && SafePath.GetFile(ProgramConstants.GamePath, f).Exists);

        if (presentFiles.Count == 0)
            return null;

        string description;
        if (ClientConfiguration.Instance.ClientGameType == ClientType.TS)
        {
            description = ("You have installed the mod on top of a Tiberian Sun\n" +
            "copy! This mod is standalone, therefore you have to\n" +
            "install it in an empty folder. Otherwise the mod won't\n" +
            "function correctly.\n\n" +
            "Please reinstall the mod into an empty folder to play.").L10N("Client:Main:InterferingFilesDetectedTextTS");
        }
        else
        {
            description = "The following interfering files are present:".L10N("Client:Main:InterferingFilesDetectedTextNonTS1") +
            Environment.NewLine + Environment.NewLine +
            string.Join(Environment.NewLine, presentFiles) +
            Environment.NewLine + Environment.NewLine +
            "The mod won't work correctly without those files removed.".L10N("Client:Main:InterferingFilesDetectedTextNonTS2");
        }

        return new StartupMessage("Interfering Files Detected".L10N("Client:Main:InterferingFilesDetectedTitle"), description);
    }

    /// <summary>
    /// The first run's question (IsFirstRun, then cleared and saved): configure the settings now? Null after the first
    /// run.
    /// </summary>
    public static StartupMessage? TakeFirstRunQuestion()
    {
        if (!UserINISettings.Instance.IsFirstRun)
            return null;

        UserINISettings.Instance.IsFirstRun.Value = false;
        UserINISettings.Instance.SaveSettings();

        return new StartupMessage("Initial Installation".L10N("Client:Main:InitialInstallationTitle"),
            string.Format(("You have just installed {0}.\n" +
                "It's highly recommended that you configure your settings before playing.\n" +
                "Do you want to configure them now?").L10N("Client:Main:InitialInstallationText"),
            ClientConfiguration.Instance.LocalGame));
    }

    /// <summary>
    /// Applies the translation's game files once per game version (always in ModMode, where there's no updater).
    /// </summary>
    /// <returns>The error message if applying failed, or null.</returns>
    public static StartupMessage? ApplyTranslationGameFiles(string gameVersion, bool skipVersionCheck = false)
    {
        if (!skipVersionCheck && !ClientConfiguration.Instance.ModMode &&
            UserINISettings.Instance.TranslationGameFilesVersion.Value == gameVersion)
            return null;

        try
        {
            Translation.Instance.ApplyTranslationGameFiles();
            UserINISettings.Instance.TranslationGameFilesVersion.Value = gameVersion;
            UserINISettings.Instance.SaveSettings();
            return null;
        }
        catch (Exception ex)
        {
            Logger.Log("Failed to apply translation game files. " + ex.ToString());
            return new StartupMessage("Applying Translation Files Failed".L10N("Client:Main:ApplyTranslationFilesFailTitle"),
                "Applying translation files failed! Error message:".L10N("Client:Main:ApplyTranslationFilesFailText") + " " + ex.Message);
        }
    }
}
