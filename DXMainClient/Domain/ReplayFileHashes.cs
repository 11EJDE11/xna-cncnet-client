using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;

using ClientCore;

using Rampastring.Tools;

namespace DTAClient.Domain
{
    /// <summary>
    /// Hashes of the game files that affect simulation outcome, written into spawn.ini so they end
    /// up inside every recorded replay (the spawner embeds spawn.ini verbatim).
    ///
    /// Replays are an input stream, not a state dump: they only replay correctly against the same
    /// game files they were recorded with. A mismatch does not error - the replay loads with the
    /// correct players and then silently does nothing, because unit orders reference object IDs
    /// that no longer exist. These hashes let playback detect that up front and name the file.
    ///
    /// IMPORTANT: the file list must be kept in sync with the quick-match-client copy in
    /// replayhashes.cpp. Deliberately separate from Resources/FHCConfig.ini: that list is missing
    /// the main game mixes, and extending it would change every player's lobby hash (FHCConfig.ini
    /// is itself part of that hash), which is not something a replay change should drag along.
    /// </summary>
    public static class ReplayFileHashes
    {
        public const string SECTION = "ReplayFileHashes";

        /// <summary>
        /// Written when a listed file is absent, so absence is detected as a difference rather
        /// than silently skipped.
        /// </summary>
        private const string MISSING_MARKER = "MISSING";

        /// <summary>
        /// The list of tracked files ships with the game package, in the same file the lobby
        /// file-hash check uses. Keeping it as data rather than code means it can be corrected by
        /// shipping a package, without rebuilding either client, and there is only one copy to
        /// maintain across this client and the Quick Match one.
        /// </summary>
        private const string CONFIG_FILE = "FHCConfig.ini";
        private const string CONFIG_SECTION = "ReplayFilenameList";

        /// <summary>
        /// Used only when the package predates [ReplayFilenameList], so an older install still
        /// records something useful rather than silently recording no hashes at all. A trailing /
        /// means every .ini inside that directory.
        /// </summary>
        private static readonly string[] FallbackEntries =
        {
            "gamemd-spawn.exe",
            "Ares.dll",
            "Phobos.dll",

            "ra2.mix",
            "ra2md.mix",
            "expandmd01.mix",
            "multi.mix",
            "multimd.mix",
            "cncnet.mix",
            "ra2mode.mix",
            "ares.mix",

            "rulesmd.ini",
            "artmd.ini",
            "aimd.ini",

            "INI/Game Options/",
            "INI/Map Code/",
        };

        /// <summary>
        /// Writes the [ReplayFileHashes] section into the given spawn.ini.
        /// </summary>
        public static void Write(IniFile spawnIni)
        {
            SortedDictionary<string, string> hashes = Collect();

            var section = new IniSection(SECTION);

            // Indexed keys rather than the path as the key: paths contain spaces and slashes, which
            // are not safe as INI keys. Value is "<relative path>|<sha1>".
            int index = 0;
            foreach (KeyValuePair<string, string> entry in hashes)
            {
                section.SetStringValue(index.ToString(), entry.Key + "|" + entry.Value);
                index++;
            }

            spawnIni.AddSection(section);

            Logger.Log($"ReplayFileHashes: wrote {hashes.Count} file hashes to spawn.ini");
        }

        /// <summary>
        /// Compares the hashes recorded in a replay's spawn.ini against the local game files.
        /// Returns a human readable description of each difference, empty when everything matches.
        ///
        /// A replay with no [ReplayFileHashes] section was recorded before this existed and cannot
        /// be checked, so it returns no differences rather than blocking it.
        /// </summary>
        public static List<string> FindMismatches(IniFile replaySpawnIni)
        {
            var mismatches = new List<string>();

            List<string> keys = replaySpawnIni.GetSectionKeys(SECTION);
            if (keys == null || keys.Count == 0)
                return mismatches;

            SortedDictionary<string, string> local = Collect();

            foreach (string key in keys)
            {
                string value = replaySpawnIni.GetStringValue(SECTION, key, string.Empty);

                // Split on the last separator so a path containing '|' cannot corrupt the parse.
                int separator = value.LastIndexOf('|');
                if (separator <= 0)
                    continue;

                string relativePath = value.Substring(0, separator);
                string recordedHash = value.Substring(separator + 1);

                if (!local.TryGetValue(relativePath, out string localHash))
                {
                    // Only reachable for files under the tracked directories, since the fixed list
                    // always produces an entry.
                    mismatches.Add($"{relativePath} is missing locally");
                    continue;
                }

                if (string.Equals(localHash, recordedHash, StringComparison.OrdinalIgnoreCase))
                    continue;

                if (recordedHash == MISSING_MARKER)
                    mismatches.Add($"{relativePath} exists locally but did not when this replay was recorded");
                else if (localHash == MISSING_MARKER)
                    mismatches.Add($"{relativePath} is missing locally");
                else
                    mismatches.Add($"{relativePath} — replay: {ShortHash(recordedHash)}, yours: {ShortHash(localHash)}");
            }

            return mismatches;
        }

        /// <summary>
        /// Reads the tracked entries from the package, falling back to the built-in list when the
        /// installed package is older than this feature.
        /// </summary>
        private static List<string> ReadTrackedEntries()
        {
            var entries = new List<string>();

            FileInfo configFile = SafePath.GetFile(ProgramConstants.GamePath, ProgramConstants.BASE_RESOURCE_PATH, CONFIG_FILE);
            if (configFile.Exists)
            {
                var config = new IniFile(configFile.FullName);
                List<string> keys = config.GetSectionKeys(CONFIG_SECTION);

                if (keys != null)
                {
                    foreach (string key in keys)
                    {
                        string value = config.GetStringValue(CONFIG_SECTION, key, string.Empty).Trim();
                        if (!string.IsNullOrEmpty(value))
                            entries.Add(value);
                    }
                }
            }

            if (entries.Count == 0)
            {
                Logger.Log($"ReplayFileHashes: no [{CONFIG_SECTION}] in {CONFIG_FILE}, using the built-in list");
                entries.AddRange(FallbackEntries);
            }

            return entries;
        }

        private static SortedDictionary<string, string> Collect()
        {
            var hashes = new SortedDictionary<string, string>(StringComparer.OrdinalIgnoreCase);

            foreach (string entry in ReadTrackedEntries())
            {
                // A trailing slash means "every .ini in this directory".
                if (entry.EndsWith("/", StringComparison.Ordinal))
                {
                    string relativeDir = entry.TrimEnd('/');
                    DirectoryInfo dir = SafePath.GetDirectory(ProgramConstants.GamePath, relativeDir.Replace('/', Path.DirectorySeparatorChar));
                    if (!dir.Exists)
                        continue;

                    foreach (FileInfo file in dir.EnumerateFiles("*.ini").OrderBy(f => f.Name, StringComparer.OrdinalIgnoreCase))
                    {
                        string dirHash = HashFile(file.FullName);
                        if (dirHash != null)
                            hashes[relativeDir + "/" + file.Name] = dirHash;
                    }

                    continue;
                }

                string fullPath = SafePath.CombineFilePath(ProgramConstants.GamePath, entry.Replace('/', Path.DirectorySeparatorChar));

                if (!File.Exists(fullPath))
                {
                    hashes[entry] = MISSING_MARKER;
                    continue;
                }

                string hash = HashFile(fullPath);
                if (hash != null)
                    hashes[entry] = hash;
            }

            return hashes;
        }

        /// <summary>
        /// Enough of a SHA1 to identify a build without filling the dialog. Full hashes go to the
        /// client log for anyone who needs to match them exactly.
        /// </summary>
        private static string ShortHash(string hash)
        {
            if (string.IsNullOrEmpty(hash))
                return "unknown";

            return hash.Length <= 12 ? hash : hash.Substring(0, 12);
        }

        private static string HashFile(string path)
        {
            try
            {
                using (var sha1 = SHA1.Create())
                using (FileStream stream = File.OpenRead(path))
                {
                    return BitConverter.ToString(sha1.ComputeHash(stream)).Replace("-", string.Empty).ToLowerInvariant();
                }
            }
            catch (Exception ex)
            {
                Logger.Log($"ReplayFileHashes: could not hash {path}: {ex.Message}");
                return null;
            }
        }
    }
}
