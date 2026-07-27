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
        /// Files that can change simulation outcome. Cosmetic content (language, movies, theme and
        /// maps mixes) is left out on purpose - it cannot desync a replay and would only add noise.
        ///
        /// The spawner DLLs are also left out: Quick Match injects CnCNet-QM-Spawner.dll while this
        /// client uses CnCNet-Spawner.dll, so hashing them by name would flag a mismatch on every
        /// cross-client playback. The spawner is covered by the SpawnerVersion key instead. Syringe
        /// is a loader rather than part of the simulation, so it is out for the same reason.
        /// </summary>
        private static readonly string[] TrackedFiles =
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
        };

        /// <summary>
        /// Every .ini below these is tracked. These hold the settings that change the rules of a
        /// match - crate amounts, AI difficulty, playstyle and so on.
        /// </summary>
        private static readonly string[] TrackedDirectories =
        {
            "INI/Game Options",
            "INI/Map Code",
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
                    mismatches.Add($"{relativePath} differs from the version this replay was recorded with");
            }

            return mismatches;
        }

        private static SortedDictionary<string, string> Collect()
        {
            var hashes = new SortedDictionary<string, string>(StringComparer.OrdinalIgnoreCase);

            foreach (string relativePath in TrackedFiles)
            {
                string fullPath = SafePath.CombineFilePath(ProgramConstants.GamePath, relativePath.Replace('/', Path.DirectorySeparatorChar));

                if (!File.Exists(fullPath))
                {
                    hashes[relativePath] = MISSING_MARKER;
                    continue;
                }

                string hash = HashFile(fullPath);
                if (hash != null)
                    hashes[relativePath] = hash;
            }

            foreach (string relativeDir in TrackedDirectories)
            {
                DirectoryInfo dir = SafePath.GetDirectory(ProgramConstants.GamePath, relativeDir.Replace('/', Path.DirectorySeparatorChar));
                if (!dir.Exists)
                    continue;

                foreach (FileInfo file in dir.EnumerateFiles("*.ini").OrderBy(f => f.Name, StringComparer.OrdinalIgnoreCase))
                {
                    string hash = HashFile(file.FullName);
                    if (hash != null)
                        hashes[relativeDir + "/" + file.Name] = hash;
                }
            }

            return hashes;
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
