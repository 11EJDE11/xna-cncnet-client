#nullable enable

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;

using ClientCore;

using ClientUpdater;

using Rampastring.Tools;

namespace DTAClient.Domain;

/// <summary>
/// Replay paths, naming, listing and pruning.
/// </summary>
public static class ReplayManager
{
    /// <summary>
    /// Cap on the timestamp-plus-map-name part of a recording's file name, leaving room in a
    /// 260-character path for the game directory, the replay directory and the extension.
    /// </summary>
    private const int MaxRecordingBaseFileNameLength = 180;

    /// <summary>
    /// Whether the current game package enables replay support.
    /// </summary>
    public static bool IsSupported => ClientConfiguration.Instance.ReplaySupport;

    public static string DirectoryName => ClientConfiguration.Instance.ReplaysDirectory;

    public static string FileExtension => ClientConfiguration.Instance.ReplayFileExtension;

    public static string SearchPattern => "*." + FileExtension;

    /// <summary>
    /// Identifies the installed game package, e.g. "YR 9.3.1". Written to spawn.ini as
    /// GameClientVersion so a replay records the version it needs to play back against.
    /// Not localized - it is recorded into the replay file, not just displayed.
    /// </summary>
    public static string GameClientVersion
    {
        get
        {
            string gameVersion = string.IsNullOrWhiteSpace(Updater.GameVersion) ? "Unknown" : Updater.GameVersion;

            return $"{ClientConfiguration.Instance.LocalGame} {gameVersion}".Trim();
        }
    }

    public static DirectoryInfo GetDirectory()
        => SafePath.GetDirectory(ProgramConstants.GamePath, DirectoryName);

    public static FileInfo GetFile(string fileName)
        => SafePath.GetFile(ProgramConstants.GamePath, DirectoryName, fileName);

    /// <summary>
    /// A game-directory-relative path for a replay, as written to spawn.ini.
    /// </summary>
    public static string GetRelativePath(string fileName)
        => SafePath.CombineFilePath(DirectoryName, fileName);

    /// <summary>
    /// Adds the recording keys to a spawn.ini that already has EnableReplayRecording set. Does
    /// nothing when the game is not recording, so it is safe to call unconditionally.
    /// </summary>
    /// <param name="spawnIni">The spawn.ini being written for this game.</param>
    /// <param name="mapName">Untranslated map name, used to name the file.</param>
    public static void PrepareRecording(IniFile spawnIni, string mapName)
    {
        if (!IsSupported)
            return;

        if (!spawnIni.GetBooleanValue("Settings", "EnableReplayRecording", false))
            return;

        spawnIni.SetStringValue("Settings", "ReplayFileOut", BuildRecordingPath(mapName));

        ReplayFileHashes.Write(spawnIni);
    }

    /// <summary>
    /// A game-directory-relative path for a new recording, e.g.
    /// "Replays\2026-08-20 19-30-05 Heck Freezes Over.yrrp".
    /// </summary>
    public static string BuildRecordingPath(string mapName)
    {
        string timestamp = DateTime.Now.ToString("yyyy-MM-dd HH-mm-ss");
        string safeMapName = SanitizeForFileName(mapName);

        string baseName = string.IsNullOrWhiteSpace(safeMapName)
            ? timestamp
            : timestamp + " " + safeMapName;

        if (baseName.Length > MaxRecordingBaseFileNameLength)
        {
            int length = MaxRecordingBaseFileNameLength;

            // Do not cut a surrogate pair in half; the orphaned half is not a renderable character.
            if (char.IsHighSurrogate(baseName[length - 1]))
                length--;

            baseName = baseName.Substring(0, length).TrimEnd();
        }

        string fileName = baseName + "." + FileExtension;

        int counter = 1;
        while (GetFile(fileName).Exists)
        {
            fileName = $"{baseName} ({counter})." + FileExtension;
            counter++;
        }

        return GetRelativePath(fileName);
    }

    /// <summary>
    /// Result of parsing one replay file, kept so that listing the directory again does not
    /// re-read files that have not changed. Unparseable files are remembered too, so they are
    /// not read and logged over and over.
    /// </summary>
    private readonly struct CachedReplay
    {
        public CachedReplay(FileInfo file, ReplayGame? replay)
        {
            length = file.Length;
            lastWriteTicks = file.LastWriteTimeUtc.Ticks;
            Replay = replay;
        }

        private readonly long length;
        private readonly long lastWriteTicks;

        /// <summary>Null when the file could not be parsed.</summary>
        public ReplayGame? Replay { get; }

        public bool Matches(FileInfo file)
            => length == file.Length && lastWriteTicks == file.LastWriteTimeUtc.Ticks;
    }

    /// <summary>
    /// Only ever read by <see cref="List"/>, which drops entries for files that are gone.
    /// </summary>
    private static readonly Dictionary<string, CachedReplay> parsedReplays
        = new Dictionary<string, CachedReplay>(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// All parseable replays, newest first. Unreadable files are skipped and logged.
    /// Files that have not changed since the last call are not read again.
    /// </summary>
    public static List<ReplayGame> List()
    {
        var replays = new List<ReplayGame>();

        DirectoryInfo directory = GetDirectory();
        if (!directory.Exists)
        {
            parsedReplays.Clear();
            return replays;
        }

        var presentFiles = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (FileInfo file in directory.EnumerateFiles(SearchPattern, SearchOption.TopDirectoryOnly))
        {
            presentFiles.Add(file.Name);

            if (!parsedReplays.TryGetValue(file.Name, out CachedReplay cached) || !cached.Matches(file))
            {
                var parsed = new ReplayGame(file.Name);
                cached = new CachedReplay(file, parsed.ParseInfo() ? parsed : null);
                parsedReplays[file.Name] = cached;
            }

            if (cached.Replay != null)
                replays.Add(cached.Replay);
        }

        DropDeletedFromCache(presentFiles);

        return replays.OrderByDescending(replay => replay.RecordedAt).ToList();
    }

    private static void DropDeletedFromCache(HashSet<string> presentFiles)
    {
        List<string> deleted = parsedReplays.Keys.Where(name => !presentFiles.Contains(name)).ToList();

        foreach (string name in deleted)
            parsedReplays.Remove(name);
    }

    /// <summary>
    /// Deletes a replay.
    /// </summary>
    /// <returns>
    /// False when the file could not be removed, which normally means the game or another client
    /// instance still has it open.
    /// </returns>
    public static bool Delete(ReplayGame replay)
    {
        Logger.Log("Deleting replay " + replay.FileName);

        try
        {
            SafePath.DeleteFileIfExists(ProgramConstants.GamePath, DirectoryName, replay.FileName);
            return true;
        }
        catch (Exception ex)
        {
            Logger.Log($"ReplayManager: could not delete {replay.FileName}: {ex.Message}");
            return false;
        }
    }

    /// <summary>
    /// Deletes the oldest replays until both the count and the total size are within the
    /// user's limits. Either limit set to 0 means "no limit".
    /// </summary>
    public static void Prune()
    {
        if (!IsSupported)
            return;

        int maxCount = UserINISettings.Instance.MaxKeptReplays;
        int maxSizeMB = UserINISettings.Instance.MaxReplayFolderSizeMB;

        if (maxCount <= 0 && maxSizeMB <= 0)
            return;

        try
        {
            DirectoryInfo directory = GetDirectory();
            if (!directory.Exists)
                return;

            List<FileInfo> files = directory
                .EnumerateFiles(SearchPattern, SearchOption.TopDirectoryOnly)
                .OrderBy(file => file.LastWriteTime)
                .ToList();

            long maxSizeBytes = maxSizeMB > 0 ? maxSizeMB * 1024L * 1024L : long.MaxValue;
            long totalBytes = files.Sum(file => file.Length);
            int fileCount = files.Count;

            foreach (FileInfo file in files)
            {
                bool overCount = maxCount > 0 && fileCount > maxCount;
                bool overSize = totalBytes > maxSizeBytes;

                if (!overCount && !overSize)
                    break;

                long fileBytes = file.Length;

                try
                {
                    file.Delete();
                    Logger.Log($"ReplayManager: pruned {file.Name} ({GetPruneReason(overCount, overSize)} limit)");
                }
                catch (Exception ex)
                {
                    Logger.Log($"ReplayManager: could not delete {file.Name}: {ex.Message}");
                    continue;
                }

                fileCount--;
                totalBytes -= fileBytes;
            }
        }
        catch (Exception ex)
        {
            Logger.Log("ReplayManager: pruning failed: " + ex.Message);
        }
    }

    private static string GetPruneReason(bool overCount, bool overSize)
    {
        if (overCount && overSize)
            return "count and size";

        return overCount ? "count" : "size";
    }

    public static void OpenDirectory()
    {
        try
        {
            DirectoryInfo directory = GetDirectory();
            if (!directory.Exists)
                directory.Create();

            ProcessLauncher.StartShellProcess(directory.FullName);
        }
        catch (Exception ex)
        {
            Logger.Log("ReplayManager: could not open the replay directory: " + ex.Message);
        }
    }

    /// <summary>Characters a replay file name may not contain. Built once.</summary>
    private static readonly HashSet<char> invalidFileNameChars = BuildInvalidFileNameChars();

    private static HashSet<char> BuildInvalidFileNameChars()
    {
        var invalid = new HashSet<char>(Path.GetInvalidFileNameChars());

        // Explicit: the framework's list is platform-dependent, and replays get shared.
        foreach (char character in "<>:\"/\\|?*")
            invalid.Add(character);
        for (char character = '\0'; character < ' '; character++)
            invalid.Add(character);

        return invalid;
    }

    private static string SanitizeForFileName(string name)
    {
        if (string.IsNullOrWhiteSpace(name))
            return string.Empty;

        var builder = new StringBuilder(name.Length);

        foreach (char character in name)
        {
            if (!invalidFileNameChars.Contains(character))
                builder.Append(character);
        }

        return builder.ToString().Trim().TrimEnd('.');
    }
}
