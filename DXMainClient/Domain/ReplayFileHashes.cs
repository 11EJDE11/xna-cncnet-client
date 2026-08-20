#nullable enable

using System;
using System.Collections.Generic;
using System.IO;

using ClientCore;

using DTAClient.Online;

using Rampastring.Tools;

namespace DTAClient.Domain;

/// <summary>
/// Records per-file hashes for replay compatibility checks.
/// Uses the same tracked file list as the multiplayer hash check.
/// </summary>
public static class ReplayFileHashes
{
    public const string SECTION = "ReplayFileHashes";

    /// <summary>
    /// Recorded for tracked files that do not exist locally.
    /// </summary>
    private const string MISSING_MARKER = "MISSING";

    /// <summary>
    /// Caches hashes by file length and write time to avoid repeated full-file reads.
    /// </summary>
    private const string CACHE_FILE = "ReplayFileHashes.cache.ini";
    private const string CACHE_SECTION = "Hashes";
    private const string CACHE_FORMAT_VERSION_KEY = "FormatVersion";

    /// <summary>
    /// Bump when hashing rules change.
    /// </summary>
    private const int CACHE_FORMAT_VERSION = 1;

    private static Dictionary<string, CachedHash>? hashCache;
    private static bool hashCacheDirty;

    private static Dictionary<string, CachedHash> HashCache => hashCache ??= LoadHashCache();

    private struct CachedHash
    {
        public long Length;
        public long LastWriteTicks;
        public string Hash;
    }

    /// <summary>
    /// Writes replay compatibility hashes into spawn.ini.
    /// </summary>
    public static void Write(IniFile spawnIni)
    {
        SortedDictionary<string, string> hashes = Collect();

        if (hashes.Count == 0)
            return;

        var section = new IniSection(SECTION);

        // Value is "<relative path>|<sha1>".
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
        var recordedPaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (string key in keys)
        {
            string value = replaySpawnIni.GetStringValue(SECTION, key, string.Empty);

            // Split on the last separator so a path containing '|' cannot corrupt the parse.
            int separator = value.LastIndexOf('|');
            if (separator <= 0)
                continue;

            string relativePath = NormalizeRelativePath(value.Substring(0, separator));
            string recordedHash = value.Substring(separator + 1);
            recordedPaths.Add(relativePath);

            // Every file on the fixed list is collected either way, so a miss here means the
            // recording had a file in one of the scanned INI directories that we do not.
            if (!local.TryGetValue(relativePath, out string? localHash))
            {
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

        foreach (string relativePath in local.Keys)
        {
            if (!recordedPaths.Contains(relativePath))
                mismatches.Add($"{relativePath} exists locally but did not when this replay was recorded");
        }

        return mismatches;
    }

    private static SortedDictionary<string, string> Collect()
    {
        var hashes = new SortedDictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var hashedPaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (FileHashCalculator.TrackedFile tracked in FileHashCalculator.EnumerateTrackedFiles())
        {
            string relativePath = NormalizeRelativePath(tracked.RelativePath);

            if (!File.Exists(tracked.FullPath))
            {
                hashes[relativePath] = MISSING_MARKER;
                continue;
            }

            string? hash = HashFile(tracked.FullPath);
            if (hash != null)
            {
                hashes[relativePath] = hash;
                hashedPaths.Add(tracked.FullPath);
            }
        }

        DropStaleCacheEntries(hashedPaths);
        SaveHashCache();

        return hashes;
    }

    private static string NormalizeRelativePath(string path) => path.Replace('\\', '/');

    /// <summary>
    /// Forgets cached hashes for files that are no longer tracked, so the cache does not grow
    /// without bound as the game's files change.
    /// </summary>
    private static void DropStaleCacheEntries(HashSet<string> hashedPaths)
    {
        var stalePaths = new List<string>();

        foreach (string path in HashCache.Keys)
        {
            if (!hashedPaths.Contains(path))
                stalePaths.Add(path);
        }

        foreach (string path in stalePaths)
        {
            HashCache.Remove(path);
            hashCacheDirty = true;
        }
    }

    private static Dictionary<string, CachedHash> LoadHashCache()
    {
        var cache = new Dictionary<string, CachedHash>(StringComparer.OrdinalIgnoreCase);

        FileInfo cacheFile = SafePath.GetFile(ProgramConstants.ClientUserFilesPath, CACHE_FILE);
        if (!cacheFile.Exists)
            return cache;

        try
        {
            var cacheIni = new IniFile(cacheFile.FullName);

            if (cacheIni.GetIntValue(CACHE_SECTION, CACHE_FORMAT_VERSION_KEY, 0) != CACHE_FORMAT_VERSION)
                return cache;

            List<string> keys = cacheIni.GetSectionKeys(CACHE_SECTION);
            if (keys == null)
                return cache;

            foreach (string key in keys)
            {
                if (key == CACHE_FORMAT_VERSION_KEY)
                    continue;

                // Value is "<full path>|<length>|<lastWriteTicks>|<sha1>".
                string value = cacheIni.GetStringValue(CACHE_SECTION, key, string.Empty);
                string[] parts = value.Split('|');
                if (parts.Length != 4)
                    continue;

                if (!long.TryParse(parts[1], out long length) || !long.TryParse(parts[2], out long ticks))
                    continue;

                cache[parts[0]] = new CachedHash { Length = length, LastWriteTicks = ticks, Hash = parts[3] };
            }
        }
        catch (Exception ex)
        {
            Logger.Log($"ReplayFileHashes: could not read the hash cache: {ex.Message}");
            cache.Clear();
        }

        return cache;
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
            section.SetIntValue(CACHE_FORMAT_VERSION_KEY, CACHE_FORMAT_VERSION);

            int index = 0;
            foreach (KeyValuePair<string, CachedHash> entry in HashCache)
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

    private static string? HashFile(string path)
    {
        try
        {
            var fileInfo = new FileInfo(path);
            long length = fileInfo.Length;
            long lastWriteTicks = fileInfo.LastWriteTimeUtc.Ticks;

            if (HashCache.TryGetValue(path, out CachedHash cached)
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

            HashCache[path] = new CachedHash { Length = length, LastWriteTicks = lastWriteTicks, Hash = hash };
            hashCacheDirty = true;

            return hash;
        }
        catch (Exception ex)
        {
            Logger.Log($"ReplayFileHashes: could not hash {path}: {ex.Message}");
            return null;
        }
    }
}
