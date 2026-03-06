using ClientCore;
using Rampastring.Tools;
using System;
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
        const string REPLAY_GAMES_DIRECTORY = "replays";

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
        public string SpawnerVersion { get; private set; }
        public string PhobosVersion { get; private set; }
        public string GameClientVersion { get; private set; }
        public string GameVersion { get; private set; }
        public uint GameMode { get; private set; } //Skirmish, LAN, Online, ...

        private const uint REPLAY_MAGIC = 0x4A455259;
        private const uint SUPPORTED_REPLAY_FORMAT_VERSION = 1;

        // Spawn ini file content
        private string spawnIniContent;
        private string spawnMapContent;

        /// <summary>
        /// Replay file header structure written by the spawner.
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
        }

        /// <summary>
        /// Reads and sets the replay's metadata and returns true if successful.
        /// </summary>
        /// <returns>True if parsing the info was successful, otherwise false.</returns>
        public bool ParseInfo()
        {
            try
            {
                FileInfo replayFileInfo = SafePath.GetFile(ProgramConstants.GamePath, REPLAY_GAMES_DIRECTORY, FileName);

                if (!replayFileInfo.Exists)
                {
                    Logger.Log("Replay file does not exist: " + FileName);
                    return false;
                }

                using (FileStream fs = replayFileInfo.Open(FileMode.Open, FileAccess.Read))
                using (BinaryReader reader = new BinaryReader(fs))
                {
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

                    // Store header data
                    Version = header.Version;
                    MapName = Encoding.ASCII.GetString(header.MapName).TrimEnd('\0');
                    Seed = header.Seed;
                    StartFrame = header.StartFrame;

                    // Store version info
                    SpawnerVersion = $"{header.SpawnerVersionMajor}.{header.SpawnerVersionMinor}.{header.SpawnerVersionRevision}.{header.SpawnerVersionPatch}";
                    GameVersion = Encoding.ASCII.GetString(header.GameVersionString).TrimEnd('\0');
                    GameClientVersion = Encoding.ASCII.GetString(header.GameClientVersion).TrimEnd('\0');
                    GameMode = header.GameMode;

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

                    if (!string.IsNullOrWhiteSpace(spawnIniContent))
                    {
                        using MemoryStream spawnIniStream = new MemoryStream(Encoding.UTF8.GetBytes(spawnIniContent));
                        IniFile spawnIni = new IniFile(spawnIniStream, applyBaseIni: false);
                        string spawnIniSpawnerVersion = spawnIni.GetStringValue("Settings", "SpawnerVersion", string.Empty);
                        if (!string.IsNullOrWhiteSpace(spawnIniSpawnerVersion))
                            SpawnerVersion = spawnIniSpawnerVersion;
                        if (string.IsNullOrWhiteSpace(GameClientVersion))
                            GameClientVersion = spawnIni.GetStringValue("Settings", "GameClientVersion", string.Empty);
                        PhobosVersion = spawnIni.GetStringValue("Settings", "PhobosVersion", string.Empty);
                    }

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
