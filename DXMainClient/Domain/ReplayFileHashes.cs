using System;
using System.Collections.Generic;
using System.IO;

using ClientCore;

using DTAClient.Online;

using Rampastring.Tools;

namespace DTAClient.Domain
{
    /// <summary>
    /// Hashes of the game files that affect simulation outcome, written into spawn.ini so they end
    /// up inside every recorded replay (the spawner embeds spawn.ini verbatim).
    ///
    /// Replays are an input stream, not a state dump: they only replay correctly against the same
    /// game files they were recorded with. A mismatch does not error - the replay loads with the
    /// correct players and then silently diverges, because unit orders reference object IDs
    /// that no longer exist. These hashes let playback detect that up front and name the file.
    ///
    /// The file list is the same one the multiplayer compatibility check uses - see
    /// <see cref="FileHashCalculator.EnumerateTrackedFiles"/>. Both are answering the same
    /// question, so a file added for one is a file the other wanted anyway, and a separate list
    /// would just be somewhere for the two to drift apart.
    ///
    /// IMPORTANT: still needs to be kept in sync with the quick-match client's copy in
    /// replayhashes.cpp, which is a separate implementation.
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
        /// Hashing every tracked file means reading the game's mixes end to end, which is slow
        /// enough to stall the client on a cold disk - and it happens on every recorded game
        /// launch and every replay launch. Results are cached against each file's length and
        /// last-write time, so the work only repeats when a file actually changes.
        /// </summary>
        private const string CACHE_FILE = "ReplayFileHashes.cache.ini";
        private const string CACHE_SECTION = "Hashes";

        /// <summary>
        /// Bumped whenever the hashing rule changes, so stale entries from an older build are
        /// discarded rather than returned as though they were current.
        /// </summary>
        private const int CACHE_FORMAT_VERSION = 1;

        private static Dictionary<string, CachedHash> hashCache;
        private static bool hashCacheDirty;

        private struct CachedHash
        {
            public long Length;
            public long LastWriteTicks;
            public string Hash;
        }

        /// <summary>
        /// Writes the [ReplayFileHashes] section into the given spawn.ini. Writes nothing when the
        /// package lists no files, which playback treats as "cannot be checked".
        /// </summary>
        public static void Write(IniFile spawnIni)
        {
            SortedDictionary<string, string> hashes = Collect();

            if (hashes.Count == 0)
                return;

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
        /// A replay with no [ReplayFileHashes] section cannot be checked, so it returns no
        /// differences rather than blocking it.
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
                    mismatches.Add($"{relativePath}\n    replay: {recordedHash}\n    yours:  {localHash}");
            }

            return mismatches;
        }

        private static SortedDictionary<string, string> Collect()
        {
            var hashes = new SortedDictionary<string, string>(StringComparer.OrdinalIgnoreCase);

            LoadHashCache();

            foreach (FileHashCalculator.TrackedFile tracked in FileHashCalculator.EnumerateTrackedFiles())
            {
                if (!File.Exists(tracked.FullPath))
                {
                    hashes[tracked.RelativePath] = MISSING_MARKER;
                    continue;
                }

                string hash = HashFile(tracked.FullPath);
                if (hash != null)
                    hashes[tracked.RelativePath] = hash;
            }

            SaveHashCache();

            return hashes;
        }

        private static void LoadHashCache()
        {
            if (hashCache != null)
                return;

            hashCache = new Dictionary<string, CachedHash>(StringComparer.OrdinalIgnoreCase);

            FileInfo cacheFile = SafePath.GetFile(ProgramConstants.ClientUserFilesPath, CACHE_FILE);
            if (!cacheFile.Exists)
                return;

            try
            {
                var cacheIni = new IniFile(cacheFile.FullName);

                if (cacheIni.GetIntValue(CACHE_SECTION, "FormatVersion", 0) != CACHE_FORMAT_VERSION)
                    return;

                List<string> keys = cacheIni.GetSectionKeys(CACHE_SECTION);
                if (keys == null)
                    return;

                foreach (string key in keys)
                {
                    if (key == "FormatVersion")
                        continue;

                    // Indexed keys for the same reason as the spawn.ini section above. Value is
                    // "<full path>|<length>|<lastWriteTicks>|<sha1>".
                    string value = cacheIni.GetStringValue(CACHE_SECTION, key, string.Empty);
                    string[] parts = value.Split('|');
                    if (parts.Length != 4)
                        continue;

                    if (!long.TryParse(parts[1], out long length) || !long.TryParse(parts[2], out long ticks))
                        continue;

                    hashCache[parts[0]] = new CachedHash { Length = length, LastWriteTicks = ticks, Hash = parts[3] };
                }
            }
            catch (Exception ex)
            {
                Logger.Log($"ReplayFileHashes: could not read the hash cache: {ex.Message}");
                hashCache = new Dictionary<string, CachedHash>(StringComparer.OrdinalIgnoreCase);
            }
        }

        private static void SaveHashCache()
        {
            if (!hashCacheDirty)
                return;

            hashCacheDirty = false;

            try
            {
                DirectoryInfo userFilesDirectory = SafePath.GetDirectory(ProgramConstants.ClientUserFilesPath);
                if (!userFilesDirectory.Exists)
                    userFilesDirectory.Create();

                var cacheIni = new IniFile();
                var section = new IniSection(CACHE_SECTION);
                section.SetIntValue("FormatVersion", CACHE_FORMAT_VERSION);

                int index = 0;
                foreach (KeyValuePair<string, CachedHash> entry in hashCache)
                {
                    section.SetStringValue(index.ToString(),
                        $"{entry.Key}|{entry.Value.Length}|{entry.Value.LastWriteTicks}|{entry.Value.Hash}");
                    index++;
                }

                cacheIni.AddSection(section);
                cacheIni.WriteIniFile(SafePath.CombineFilePath(ProgramConstants.ClientUserFilesPath, CACHE_FILE));
            }
            catch (Exception ex)
            {
                Logger.Log($"ReplayFileHashes: could not write the hash cache: {ex.Message}");
            }
        }

        private static string HashFile(string path)
        {
            try
            {
                var fileInfo = new FileInfo(path);
                long length = fileInfo.Length;
                long lastWriteTicks = fileInfo.LastWriteTimeUtc.Ticks;

                if (hashCache != null
                    && hashCache.TryGetValue(path, out CachedHash cached)
                    && cached.Length == length
                    && cached.LastWriteTicks == lastWriteTicks)
                {
                    return cached.Hash;
                }

                // Same routine as the lobby check, so an INI that only differs by line endings
                // does not read as a modified file here either.
                string hash = FileHashCalculator.CalculateSHA1ForFile(path);
                if (string.IsNullOrEmpty(hash))
                    return null;

                if (hashCache != null)
                {
                    hashCache[path] = new CachedHash { Length = length, LastWriteTicks = lastWriteTicks, Hash = hash };
                    hashCacheDirty = true;
                }

                return hash;
            }
            catch (Exception ex)
            {
                Logger.Log($"ReplayFileHashes: could not hash {path}: {ex.Message}");
                return null;
            }
        }
    }
}
