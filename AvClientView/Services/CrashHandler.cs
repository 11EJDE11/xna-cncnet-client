using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;

using ClientCore;
using ClientCore.Extensions;

using Rampastring.Tools;

namespace AvClientView.Services;

/// <summary>
/// The XNA client's PreStartup log handling: the previous log is kept as a timestamped backup (pruned to the
/// ClientLogs limits), and a crash is logged, copied to ClientCrashLogs and reported before the client closes.
/// </summary>
internal static class CrashHandler
{
    private const int DEFAULT_MAX_KEPT_LOG_FILES = 20;
    private const int DEFAULT_MAX_LOG_FOLDER_SIZE_MB = 50;
    private const string LOG_NAME = "avclient";

    /// <summary>Renames the previous avclient.log to avclient_{time}.log and prunes old backups (RotateLogFiles).</summary>
    public static void RotateLogFiles(DirectoryInfo directory, FileInfo logFile)
    {
        if (!logFile.Exists)
            return;

        (int maxKeptLogFiles, int maxFolderSizeMB) = ReadLogRetentionSettings();

        try
        {
            File.Move(logFile.FullName, SafePath.CombineFilePath(directory.FullName, FormattableString.Invariant($"{LOG_NAME}_{DateTime.Now:yyyyMMdd_HHmmss_fff}.log")));
        }
        catch
        {
            // The previous log is overwritten instead
        }

        List<FileInfo> backups = directory.EnumerateFiles(LOG_NAME + "_*.log").OrderByDescending(f => f.LastWriteTimeUtc).ToList();

        if (maxKeptLogFiles > 0)
        {
            foreach (FileInfo old in backups.Skip(maxKeptLogFiles))
                TryDelete(old);

            backups = backups.Take(maxKeptLogFiles).ToList();
        }

        if (maxFolderSizeMB > 0)
        {
            long maxBytes = maxFolderSizeMB * 1024L * 1024L;
            long total = backups.Sum(f => f.Length);
            for (int i = backups.Count - 1; i >= 0 && total > maxBytes; i--)
            {
                total -= backups[i].Length;
                TryDelete(backups[i]);
            }
        }
    }

    private static void TryDelete(FileInfo file)
    {
        try
        {
            file.Delete();
        }
        catch
        {
            // Considered again on the next start
        }
    }

    private static (int MaxKeptLogFiles, int MaxFolderSizeMB) ReadLogRetentionSettings()
    {
        try
        {
            FileInfo settingsFile = SafePath.GetFile(ProgramConstants.GamePath, ClientConfiguration.Instance.SettingsIniName);
            if (settingsFile.Exists)
            {
                var ini = new IniFile(settingsFile.FullName);
                return (Math.Max(0, ini.GetIntValue("ClientLogs", "MaxKeptLogFiles", DEFAULT_MAX_KEPT_LOG_FILES)),
                    Math.Max(0, ini.GetIntValue("ClientLogs", "MaxLogFolderSizeMB", DEFAULT_MAX_LOG_FOLDER_SIZE_MB)));
            }
        }
        catch
        {
            // The defaults
        }

        return (DEFAULT_MAX_KEPT_LOG_FILES, DEFAULT_MAX_LOG_FOLDER_SIZE_MB);
    }

    /// <summary>
    /// A crash (PreStartup.HandleException): the exception is logged, the log copied to ClientCrashLogs and the user
    /// told where it is. The client then closes, as the XNA client does.
    /// </summary>
    public static void HandleException(Exception ex)
    {
        try
        {
            ExceptionLogger.LogException(ex, innerException: false);
        }
        catch
        {
            Logger.Log("Unhandled exception: " + ex);
        }

        string crashLogPath = SafePath.CombineFilePath(ProgramConstants.ClientUserFilesPath, "ClientCrashLogs",
            FormattableString.Invariant($"ClientCrashLog{DateTime.Now:_yyyy_MM_dd_HH_mm}.txt"));
        bool crashLogCopied = false;

        try
        {
            DirectoryInfo crashLogs = SafePath.GetDirectory(ProgramConstants.ClientUserFilesPath, "ClientCrashLogs");
            if (!crashLogs.Exists)
                crashLogs.Create();

            File.Copy(ProgramConstants.LogFileName, crashLogPath, true);
            crashLogCopied = true;
        }
        catch
        {
            // Reported without the file
        }

        ClientConfiguration config = ClientConfiguration.Instance;
        string error = string.Format("{0} has crashed. Error message:".L10N("Client:Main:FatalErrorText1") + Environment.NewLine + Environment.NewLine +
            ex.Message + Environment.NewLine + Environment.NewLine + (crashLogCopied ?
            "A crash log has been saved to the following file:".L10N("Client:Main:FatalErrorText2") + " " + Environment.NewLine + Environment.NewLine +
            crashLogPath + Environment.NewLine + Environment.NewLine : string.Empty) +
            (crashLogCopied ? "If the issue is repeatable, contact the {1} staff at {2} and provide the crash log file.".L10N("Client:Main:FatalErrorText3") :
            "If the issue is repeatable, contact the {1} staff at {2}.".L10N("Client:Main:FatalErrorText4")),
            config.LongGameName, config.LocalGame, config.ShortSupportURL);

        // A native box: the client's own windows may not work any more
        if (OperatingSystem.IsWindows())
            _ = MessageBoxW(IntPtr.Zero, error, "KABOOOOOOOM".L10N("Client:Main:FatalErrorTitle"), 0x10);
    }

#pragma warning disable SYSLIB1054 // DllImport: LibraryImport would need unsafe code in this project
    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern int MessageBoxW(IntPtr hWnd, string text, string caption, uint type);
#pragma warning restore SYSLIB1054
}
