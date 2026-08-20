using ClientCore;
using ClientCore.Enums;
using Rampastring.Tools;
using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;

namespace DTAClient.Domain
{
    /// <summary>
    /// A replay file.
    /// </summary>
    public class ReplayGame
    {
        public ReplayGame(string fileName)
        {
            FileName = fileName;
        }

        public string FileName { get; private set; }
        public string GUIName { get; private set; }
        public DateTime LastModified { get; private set; }

        public uint Version { get; private set; }
        public string MapName { get; private set; }
        public int Seed { get; private set; }
        public uint StartFrame { get; private set; }
        public uint RecordedGameSpeed { get; private set; }
        public string SpawnerVersion { get; private set; }
        public string GameClientVersion { get; private set; }
        public string GameVersion { get; private set; }
        public SessionGameMode GameMode { get; private set; }

        /// <summary>
        /// When the recording started, from the replay header. Preferred over the file's timestamp,
        /// which does not survive copying the file elsewhere.
        /// </summary>
        public DateTime RecordedAt { get; private set; }

        /// <summary>
        /// False when the spawner never got to stamp a frame count into the header, meaning the
        /// game crashed or was killed while recording and the frame stream is cut short.
        /// </summary>
        public bool IsComplete { get; private set; }

        /// <summary>
        /// How long the recorded game ran. <see cref="TimeSpan.Zero"/> for an incomplete recording.
        /// </summary>
        public TimeSpan Duration { get; private set; }

        /// <summary>Human players, in spawn.ini order, the local recorder first.</summary>
        public List<string> PlayerNames { get; } = new List<string>();

        /// <summary>The lobby's game mode name, e.g. "Battle". Empty if the replay predates it.</summary>
        public string UIGameMode { get; private set; }

        private const uint REPLAY_MAGIC = 0x4A455259;
        private const uint SUPPORTED_REPLAY_FORMAT_VERSION = 1;
        private const uint MAX_GAME_SPEED_INDEX = 6;

        /// <summary>
        /// The spawner embeds spawn.ini and spawnmap.ini whole, so these are bounded by the size of
        /// two text files. The cap only exists so a corrupt header cannot make us try to allocate
        /// two gigabytes.
        /// </summary>
        private const uint MAX_EMBEDDED_FILE_SIZE = 32 * 1024 * 1024;

        // Spawn ini file content
        private string spawnIniContent;
        private string spawnMapContent;

        /// <summary>
        /// Replay file header structure written by the spawner. Mirrors ReplayHeader in
        /// yrpp-spawner's src/Replay/ReplaySystem.cpp, documented in that repo's
        /// docs/replay-format.md. There is no compile-time link between the two - if one changes,
        /// this must change with it, or every field past the point of divergence misparses.
        /// </summary>
        [StructLayout(LayoutKind.Sequential, Pack = 1)]
        private struct ReplayHeader
        {
            public uint Magic;
            public uint Version;                    // Replay format version

            [MarshalAs(UnmanagedType.ByValArray, SizeConst = 260)]
            public byte[] MapName;                  // Map filename

            public uint StartFrame;                 // Frame when recording started

            // Version info
            public byte SpawnerVersionMajor;
            public byte SpawnerVersionMinor;
            public byte SpawnerVersionRevision;
            public byte SpawnerVersionPatch;

            [MarshalAs(UnmanagedType.ByValArray, SizeConst = 32)]
            public byte[] GameVersionString;
            [MarshalAs(UnmanagedType.ByValArray, SizeConst = 64)]
            public byte[] GameClientVersion;

            public uint GameMode;                   // GameMode (Skirmish=5, LAN=3, Internet=4, Campaign=0)

            public int UniqueIDCounter;             // UniqueID counter at recording start
            public int Seed;                        // Random seed used for this game
            public int RandomNext1;                 // Randomizer::Next1 index
            public int RandomNext2;                 // Randomizer::Next2 index

            [MarshalAs(UnmanagedType.ByValArray, SizeConst = 250)]
            public uint[] RandomizerTable;          // Complete RNG state (250 DWORDs)

            // Spawn file sizes (files stored after header, before events)
            public uint SpawnIniSize;
            public uint SpawnMapSize;
            public uint RecordedGameSpeed;

            public ulong RecordedUnixTime;          // time() when recording started
            public uint TotalFrames;                // 0 if the recording was never finalized
        }

        /// <summary>
        /// Reads and sets the replay's metadata and returns true if successful.
        /// </summary>
        /// <returns>True if parsing the info was successful, otherwise false.</returns>
        public bool ParseInfo()
        {
            try
            {
                FileInfo replayFileInfo = SafePath.GetFile(
                    ProgramConstants.GamePath, ClientConfiguration.Instance.ReplaysDirectory, FileName);

                if (!replayFileInfo.Exists)
                {
                    Logger.Log("Replay file does not exist: " + FileName);
                    return false;
                }

                using (FileStream fs = replayFileInfo.Open(FileMode.Open, FileAccess.Read))
                using (BinaryReader reader = new BinaryReader(fs))
                {
                    if (fs.Length < Marshal.SizeOf<ReplayHeader>())
                    {
                        Logger.Log("Replay file is too small to contain a header: " + FileName);
                        return false;
                    }

                    // Read header
                    ReplayHeader header = ReadStruct<ReplayHeader>(reader);

                    // Validate magic number
                    if (header.Magic != REPLAY_MAGIC)
                    {
                        Logger.Log($"Invalid replay file magic number: 0x{header.Magic:X8} (expected 0x{REPLAY_MAGIC:X8})");
                        return false;
                    }

                    // Check version compatibility
                    if (header.Version != SUPPORTED_REPLAY_FORMAT_VERSION)
                    {
                        Logger.Log("Unsupported replay version: " + header.Version);
                        return false;
                    }

                    if (header.RecordedGameSpeed > MAX_GAME_SPEED_INDEX)
                    {
                        Logger.Log("Invalid replay RecordedGameSpeed: " + header.RecordedGameSpeed);
                        return false;
                    }

                    // The sizes come straight off disk, so check them against what is actually
                    // there before using them to size a read.
                    if (!AreEmbeddedSizesValid(header, fs.Length))
                    {
                        Logger.Log($"Replay {FileName} declares embedded file sizes that do not fit the file");
                        return false;
                    }

                    // Store header data
                    Version = header.Version;
                    MapName = Encoding.ASCII.GetString(header.MapName).TrimEnd('\0');
                    Seed = header.Seed;
                    StartFrame = header.StartFrame;
                    RecordedGameSpeed = header.RecordedGameSpeed;

                    // Store version info
                    SpawnerVersion = $"{header.SpawnerVersionMajor}.{header.SpawnerVersionMinor}.{header.SpawnerVersionRevision}.{header.SpawnerVersionPatch}";
                    GameVersion = Encoding.ASCII.GetString(header.GameVersionString).TrimEnd('\0');
                    GameClientVersion = Encoding.ASCII.GetString(header.GameClientVersion).TrimEnd('\0');
                    GameMode = (SessionGameMode)header.GameMode;

                    RecordedAt = FromUnixTime(header.RecordedUnixTime, replayFileInfo.LastWriteTime);

                    IsComplete = header.TotalFrames > 0;
                    Duration = IsComplete
                        ? TimeSpan.FromSeconds(header.TotalFrames / (double)GetFramesPerSecond((int)header.RecordedGameSpeed))
                        : TimeSpan.Zero;

                    // Read spawn.ini content
                    if (header.SpawnIniSize > 0)
                    {
                        byte[] spawnIniBytes = reader.ReadBytes((int)header.SpawnIniSize);
                        spawnIniContent = Encoding.ASCII.GetString(spawnIniBytes);
                    }

                    // Read spawnmap.ini content
                    if (header.SpawnMapSize > 0)
                    {
                        byte[] spawnMapBytes = reader.ReadBytes((int)header.SpawnMapSize);
                        spawnMapContent = Encoding.ASCII.GetString(spawnMapBytes);
                    }

                    // The embedded spawn.ini always starts with a section header. Anything
                    // else means the header we just read does not describe this file - most
                    // likely it was written by a different build of the spawner - and every
                    // offset past that point is meaningless.
                    if (!StartsWithIniSection(spawnIniContent))
                    {
                        Logger.Log($"Replay {FileName} does not contain a readable spawn.ini; " +
                            "it was probably recorded by a different version of the spawner.");
                        return false;
                    }

                    ReadEmbeddedSpawnIni();

                    if (!string.IsNullOrWhiteSpace(spawnMapContent))
                    {
                        using MemoryStream spawnMapStream = new MemoryStream(Encoding.UTF8.GetBytes(spawnMapContent));
                        IniFile spawnMapIni = new IniFile(spawnMapStream, applyBaseIni: false);
                        string mapDisplayName = spawnMapIni.GetStringValue("Basic", "Name", string.Empty);
                        if (!string.IsNullOrWhiteSpace(mapDisplayName))
                            MapName = mapDisplayName;
                    }

                    // Use map name for display, fallback to filename
                    if (!string.IsNullOrWhiteSpace(MapName))
                        GUIName = MapName;
                    else
                        GUIName = Path.GetFileNameWithoutExtension(FileName);
                }

                LastModified = replayFileInfo.LastWriteTime;
                return true;
            }
            catch (Exception ex)
            {
                Logger.Log("An error occurred while parsing replay " + FileName + ": " + ex.ToString());
                return false;
            }
        }

        /// <summary>
        /// Pulls the lobby state out of the spawn.ini the spawner embedded. Everything except the
        /// players' IP addresses survives verbatim, so this is where the player list, game mode and
        /// client version come from.
        /// </summary>
        private void ReadEmbeddedSpawnIni()
        {
            using MemoryStream spawnIniStream = new MemoryStream(Encoding.UTF8.GetBytes(spawnIniContent));
            IniFile spawnIni = new IniFile(spawnIniStream, applyBaseIni: false);

            if (string.IsNullOrWhiteSpace(GameClientVersion))
                GameClientVersion = spawnIni.GetStringValue("Settings", "GameClientVersion", string.Empty);

            UIGameMode = spawnIni.GetStringValue("Settings", "UIGameMode", string.Empty);

            // The recording player is always [Settings] Name; everyone else is [OtherN].
            string localPlayer = spawnIni.GetStringValue("Settings", "Name", string.Empty);
            if (!string.IsNullOrWhiteSpace(localPlayer))
                PlayerNames.Add(localPlayer);

            for (int otherId = 1; ; otherId++)
            {
                string otherName = spawnIni.GetStringValue("Other" + otherId, "Name", string.Empty);
                if (string.IsNullOrWhiteSpace(otherName))
                    break;

                PlayerNames.Add(otherName);
            }
        }

        /// <summary>
        /// Extracts spawn.ini content from the replay.
        /// </summary>
        public string ExtractSpawnIni()
        {
            return spawnIniContent ?? string.Empty;
        }

        /// <summary>
        /// Extracts spawnmap.ini content from the replay.
        /// </summary>
        public string ExtractSpawnMap()
        {
            return spawnMapContent ?? string.Empty;
        }

        /// <summary>
        /// The rate the game ticks at for a given game speed index, matching the spawner's
        /// GetReplayFPSFromGameSpeed. Used to turn a frame count into a duration.
        /// </summary>
        public static int GetFramesPerSecond(int gameSpeed)
        {
            gameSpeed = Math.Max(0, Math.Min(6, gameSpeed));

            if (gameSpeed <= 0)
                return 60;

            if (gameSpeed == 1)
                return 45;

            return Math.Max(1, 60 / gameSpeed);
        }

        /// <summary>
        /// Cheap check that the bytes following the header really are the embedded spawn.ini,
        /// which is the only way to notice that a replay's header layout differs from ours.
        /// </summary>
        private static bool StartsWithIniSection(string content)
            => !string.IsNullOrWhiteSpace(content) && content.TrimStart().StartsWith("[", StringComparison.Ordinal);

        private static bool AreEmbeddedSizesValid(ReplayHeader header, long fileLength)
        {
            if (header.SpawnIniSize > MAX_EMBEDDED_FILE_SIZE || header.SpawnMapSize > MAX_EMBEDDED_FILE_SIZE)
                return false;

            long required = Marshal.SizeOf<ReplayHeader>() + (long)header.SpawnIniSize + header.SpawnMapSize;
            return required <= fileLength;
        }

        private static DateTime FromUnixTime(ulong unixTime, DateTime fallback)
        {
            if (unixTime == 0 || unixTime > (ulong)int.MaxValue * 4)
                return fallback;

            try
            {
                return DateTimeOffset.FromUnixTimeSeconds((long)unixTime).LocalDateTime;
            }
            catch (ArgumentOutOfRangeException)
            {
                return fallback;
            }
        }

        /// <summary>
        /// Helper method to read a struct from a binary reader.
        /// </summary>
        private static T ReadStruct<T>(BinaryReader reader) where T : struct
        {
            int size = Marshal.SizeOf<T>();
            byte[] buffer = reader.ReadBytes(size);

            GCHandle handle = GCHandle.Alloc(buffer, GCHandleType.Pinned);
            try
            {
                return Marshal.PtrToStructure<T>(handle.AddrOfPinnedObject());
            }
            finally
            {
                handle.Free();
            }
        }
    }
}
