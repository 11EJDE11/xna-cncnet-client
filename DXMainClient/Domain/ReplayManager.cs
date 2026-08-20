using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;

using ClientCore;

using Rampastring.Tools;

namespace DTAClient.Domain
{
    /// <summary>
    /// Everything the client knows about where replays live and what they are called. The spawner
    /// writes recordings straight to the path we hand it, so this is the only place that decides
    /// the directory, the extension or the file name.
    /// </summary>
    public static class ReplayManager
    {
        /// <summary>
        /// Whether the current game supports replays at all. Everything else here is only
        /// meaningful when this is true.
        /// </summary>
        public static bool IsSupported => ClientConfiguration.Instance.ReplaySupport;

        public static string DirectoryName => ClientConfiguration.Instance.ReplaysDirectory;

        public static string FileExtension => ClientConfiguration.Instance.ReplayFileExtension;

        private static string SearchPattern => "*." + FileExtension;

        public static DirectoryInfo GetDirectory()
            => SafePath.GetDirectory(ProgramConstants.GamePath, DirectoryName);

        public static string GetFullPath(string fileName)
            => SafePath.CombineFilePath(ProgramConstants.GamePath, DirectoryName, fileName);

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

            // Deliberately relative: the spawner resolves it against the game directory, and this
            // spawn.ini is embedded verbatim inside the replay, so an absolute path would put the
            // recorder's Windows user name into every file they share.
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

            string fileName = baseName + "." + FileExtension;

            // Two games starting in the same second is far-fetched, but the spawner uses
            // CREATE_ALWAYS and would silently overwrite, so make sure the name is free.
            int counter = 1;
            while (File.Exists(GetFullPath(fileName)))
            {
                fileName = $"{baseName} ({counter})." + FileExtension;
                counter++;
            }

            return Path.Combine(DirectoryName, fileName);
        }

        /// <summary>
        /// All parseable replays, newest first. Unreadable files are skipped and logged.
        /// </summary>
        public static List<ReplayGame> List()
        {
            var replays = new List<ReplayGame>();

            DirectoryInfo directory = GetDirectory();
            if (!directory.Exists)
                return replays;

            foreach (FileInfo file in directory.EnumerateFiles(SearchPattern, SearchOption.TopDirectoryOnly))
            {
                var replay = new ReplayGame(file.Name);
                if (replay.ParseInfo())
                    replays.Add(replay);
            }

            return replays.OrderByDescending(replay => replay.RecordedAt).ToList();
        }

        public static void Delete(ReplayGame replay)
        {
            Logger.Log("Deleting replay " + replay.FileName);
            SafePath.DeleteFileIfExists(ProgramConstants.GamePath, DirectoryName, replay.FileName);
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

                // Newest first, so the files that survive are the ones at the front.
                List<FileInfo> files = directory
                    .EnumerateFiles(SearchPattern, SearchOption.TopDirectoryOnly)
                    .OrderByDescending(file => file.LastWriteTime)
                    .ToList();

                long maxSizeBytes = maxSizeMB > 0 ? maxSizeMB * 1024L * 1024L : long.MaxValue;
                long keptBytes = 0;
                int keptCount = 0;

                foreach (FileInfo file in files)
                {
                    keptCount++;
                    keptBytes += file.Length;

                    bool overCount = maxCount > 0 && keptCount > maxCount;
                    bool overSize = keptBytes > maxSizeBytes;

                    if (!overCount && !overSize)
                        continue;

                    try
                    {
                        file.Delete();
                        Logger.Log($"ReplayManager: pruned {file.Name} ({(overCount ? "count" : "size")} limit)");
                    }
                    catch (Exception ex)
                    {
                        Logger.Log($"ReplayManager: could not delete {file.Name}: {ex.Message}");

                        // It is still taking up space, so keep counting it.
                        continue;
                    }

                    keptCount--;
                    keptBytes -= file.Length;
                }
            }
            catch (Exception ex)
            {
                Logger.Log("ReplayManager: pruning failed: " + ex.Message);
            }
        }

        public static void OpenDirectory()
        {
            try
            {
                DirectoryInfo directory = GetDirectory();
                if (!directory.Exists)
                    directory.Create();

                Process.Start(new ProcessStartInfo
                {
                    FileName = directory.FullName,
                    UseShellExecute = true
                });
            }
            catch (Exception ex)
            {
                Logger.Log("ReplayManager: could not open the replay directory: " + ex.Message);
            }
        }

        private static string SanitizeForFileName(string name)
        {
            if (string.IsNullOrWhiteSpace(name))
                return string.Empty;

            var invalid = new HashSet<char>(Path.GetInvalidFileNameChars());
            var builder = new System.Text.StringBuilder(name.Length);

            foreach (char character in name)
            {
                if (!invalid.Contains(character))
                    builder.Append(character);
            }

            return builder.ToString().Trim();
        }
    }
}
