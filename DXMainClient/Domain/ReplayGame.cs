using ClientCore;
using Rampastring.Tools;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Text;

namespace DTAClient.Domain
{
    /// <summary>
    /// Version compatibility result from comparing a replay's stored DLL versions with the current ones.
    /// </summary>
    public struct VersionCheckResult
    {
        public bool SpawnerMismatch;
        public bool AresMismatch;
        public bool PhobosMismatch;

        public string StoredSpawnerVersion;
        public string CurrentSpawnerVersion;
        public string StoredAresVersion;
        public string CurrentAresVersion;
        public string StoredPhobosVersion;
        public string CurrentPhobosVersion;

        public bool HasAnyMismatch => SpawnerMismatch || AresMismatch || PhobosMismatch;

        public string ToWarningText()
        {
            var lines = new List<string>();
            if (SpawnerMismatch)
                lines.Add($"  Spawner:  recorded {StoredSpawnerVersion}  /  current {CurrentSpawnerVersion}");
            if (AresMismatch)
                lines.Add($"  Ares:     recorded {StoredAresVersion}  /  current {CurrentAresVersion}");
            if (PhobosMismatch)
                lines.Add($"  Phobos:   recorded {StoredPhobosVersion}  /  current {CurrentPhobosVersion}");
            return string.Join("\n", lines);
        }
    }

    /// <summary>
    /// Represents a .yrrp v2 replay file: spawn.ini + spawnmap.ini + RECORD.BIN packaged together.
    /// </summary>
    public class ReplayGame
    {
        private const string REPLAY_DIR = "replays";

        // .yrrp v2 binary format
        // Header (296 bytes total, little-endian):
        //   uint32  Magic = 0x59505259 ("YRPY")
        //   uint32  Version = 2
        //   uint32  SpawnIniSize
        //   uint32  SpawnMapSize
        //   uint32  InitSaveSize   (reserved for frame-0 save; always 0 for now)
        //   uint32  EventDataSize  (full RECORD.BIN content)
        //   int64   Timestamp      (UTC file time)         <- 8 bytes
        //   uint32  GameMode       (derived from spawn.ini at parse time; 0 if unknown)
        //   uint32  StartFrame     (always 0)
        //   char[64]  MapName
        //   char[32]  SpawnerVersion
        //   char[32]  AresVersion   ("N/A" if not loaded)
        //   char[32]  PhobosVersion ("N/A" if not loaded)
        //   char[64]  ClientVersion
        //   char[32]  GameVersion   (reserved)
        // Followed by: spawn.ini bytes, spawnmap.ini bytes, (no initSave), RECORD.BIN bytes
        private const uint MAGIC = 0x59505259u;
        private const uint FORMAT_VERSION = 2u;
        private const int HEADER_SIZE = 296;

        public ReplayGame(string fileName)
        {
            FileName = fileName;
        }

        public string FileName { get; }
        public string GUIName { get; private set; }
        public DateTime Timestamp { get; private set; }
        public string MapName { get; private set; }
        public uint GameMode { get; private set; }
        public string SpawnerVersion { get; private set; }
        public string AresVersion { get; private set; }
        public string PhobosVersion { get; private set; }
        public string ClientVersion { get; private set; }
        public DateTime LastModified { get; private set; }

        private uint spawnIniSize;
        private uint spawnMapSize;
        private uint initSaveSize;
        private uint eventDataSize;

        /// <summary>
        /// Reads the replay header and populates metadata. Returns false on any error.
        /// </summary>
        public bool ParseInfo()
        {
            try
            {
                FileInfo fileInfo = SafePath.GetFile(ProgramConstants.GamePath, REPLAY_DIR, FileName);
                if (!fileInfo.Exists)
                    return false;

                using FileStream fs = fileInfo.Open(FileMode.Open, FileAccess.Read);
                using BinaryReader reader = new BinaryReader(fs);

                uint magic = reader.ReadUInt32();
                if (magic != MAGIC)
                {
                    Logger.Log($"Replay {FileName}: invalid magic 0x{magic:X8}");
                    return false;
                }

                uint version = reader.ReadUInt32();
                if (version != FORMAT_VERSION)
                {
                    Logger.Log($"Replay {FileName}: unsupported version {version}");
                    return false;
                }

                spawnIniSize = reader.ReadUInt32();
                spawnMapSize = reader.ReadUInt32();
                initSaveSize = reader.ReadUInt32();
                eventDataSize = reader.ReadUInt32();
                long ticks = reader.ReadInt64();
                Timestamp = DateTime.FromFileTimeUtc(ticks).ToLocalTime();
                GameMode = reader.ReadUInt32();
                /* StartFrame */ reader.ReadUInt32();
                MapName = ReadFixedString(reader, 64);
                SpawnerVersion = ReadFixedString(reader, 32);
                AresVersion = ReadFixedString(reader, 32);
                PhobosVersion = ReadFixedString(reader, 32);
                ClientVersion = ReadFixedString(reader, 64);
                /* GameVersion */ ReadFixedString(reader, 32);

                // Try to improve the displayed map name from spawn.ini UIMapName
                if (spawnIniSize > 0)
                {
                    byte[] spawnIniBytes = reader.ReadBytes((int)spawnIniSize);
                    try
                    {
                        using MemoryStream ms = new MemoryStream(spawnIniBytes);
                        IniFile ini = new IniFile(ms, applyBaseIni: false);
                        string uiMapName = ini.GetStringValue("Settings", "UIMapName", string.Empty);
                        if (!string.IsNullOrWhiteSpace(uiMapName))
                            MapName = uiMapName;
                    }
                    catch { }
                }

                GUIName = !string.IsNullOrWhiteSpace(MapName)
                    ? MapName
                    : Path.GetFileNameWithoutExtension(FileName);

                LastModified = fileInfo.LastWriteTime;
                return true;
            }
            catch (Exception ex)
            {
                Logger.Log($"Error parsing replay {FileName}: {ex.Message}");
                return false;
            }
        }

        /// <summary>
        /// Extracts and returns spawn.ini text from the replay file.
        /// ParseInfo must have been called first.
        /// </summary>
        public string GetSpawnIni()
        {
            if (spawnIniSize == 0)
                return string.Empty;
            return Encoding.UTF8.GetString(ReadBytes(0));
        }

        /// <summary>
        /// Extracts and returns spawnmap.ini text from the replay file.
        /// ParseInfo must have been called first.
        /// </summary>
        public string GetSpawnMap()
        {
            if (spawnMapSize == 0)
                return string.Empty;
            return Encoding.UTF8.GetString(ReadBytes(1));
        }

        /// <summary>
        /// Extracts and returns the raw RECORD.BIN event stream bytes.
        /// ParseInfo must have been called first.
        /// </summary>
        public byte[] GetEventData()
        {
            if (eventDataSize == 0)
                return Array.Empty<byte>();
            // Section index 3: skip spawnIni, spawnMap, initSave to reach event data
            return ReadBytes(3);
        }

        /// <summary>
        /// Compares the stored DLL versions against what is currently installed in gamePath.
        /// </summary>
        public VersionCheckResult CheckVersions(string gamePath)
        {
            return new VersionCheckResult
            {
                StoredSpawnerVersion = SpawnerVersion ?? string.Empty,
                StoredAresVersion = AresVersion ?? string.Empty,
                StoredPhobosVersion = PhobosVersion ?? string.Empty,
                CurrentSpawnerVersion = GetBinaryVersion(gamePath, "CnCNet-Spawner.dll"),
                CurrentAresVersion = GetBinaryVersion(gamePath, "Ares.dll"),
                CurrentPhobosVersion = GetBinaryVersion(gamePath, "Phobos.dll"),
                SpawnerMismatch = VersionsDiffer(SpawnerVersion, GetBinaryVersion(gamePath, "CnCNet-Spawner.dll")),
                AresMismatch = VersionsDiffer(AresVersion, GetBinaryVersion(gamePath, "Ares.dll")),
                PhobosMismatch = VersionsDiffer(PhobosVersion, GetBinaryVersion(gamePath, "Phobos.dll"))
            };
        }

        /// <summary>
        /// Packages spawn.ini, spawnmap.ini, and RECORD.BIN into a single .yrrp v2 file.
        /// </summary>
        public static void Package(
            string outputPath,
            string spawnIniPath,
            string spawnMapPath,
            string recordBinPath,
            string spawnerVersion,
            string aresVersion,
            string phobosVersion,
            string clientVersion)
        {
            byte[] spawnIniBytes = SafeReadAllBytes(spawnIniPath);
            byte[] spawnMapBytes = SafeReadAllBytes(spawnMapPath);
            byte[] eventData = SafeReadAllBytes(recordBinPath);

            string mapName = string.Empty;
            if (spawnIniBytes.Length > 0)
            {
                try
                {
                    using MemoryStream ms = new MemoryStream(spawnIniBytes);
                    IniFile ini = new IniFile(ms, applyBaseIni: false);
                    mapName = ini.GetStringValue("Settings", "UIMapName", string.Empty);
                    if (string.IsNullOrWhiteSpace(mapName))
                        mapName = ini.GetStringValue("Settings", "Scenario", string.Empty);
                }
                catch { }
            }

            using FileStream fs = new FileStream(outputPath, FileMode.Create, FileAccess.Write);
            using BinaryWriter writer = new BinaryWriter(fs);

            writer.Write(MAGIC);
            writer.Write(FORMAT_VERSION);
            writer.Write((uint)spawnIniBytes.Length);
            writer.Write((uint)spawnMapBytes.Length);
            writer.Write((uint)0);                              // InitSaveSize (reserved)
            writer.Write((uint)eventData.Length);
            writer.Write(DateTime.UtcNow.ToFileTimeUtc());
            writer.Write((uint)0);                              // GameMode (not stored; derived at parse time)
            writer.Write((uint)0);                              // StartFrame
            WriteFixedString(writer, mapName, 64);
            WriteFixedString(writer, spawnerVersion ?? string.Empty, 32);
            WriteFixedString(writer, aresVersion ?? string.Empty, 32);
            WriteFixedString(writer, phobosVersion ?? string.Empty, 32);
            WriteFixedString(writer, clientVersion ?? string.Empty, 64);
            WriteFixedString(writer, string.Empty, 32);         // GameVersion (reserved)

            writer.Write(spawnIniBytes);
            writer.Write(spawnMapBytes);
            // (no initSave section for now)
            writer.Write(eventData);
        }

        // --- Private helpers ---

        private byte[] ReadBytes(int sectionIndex)
        {
            string fullPath = Path.Combine(ProgramConstants.GamePath, REPLAY_DIR, FileName);
            using FileStream fs = new FileStream(fullPath, FileMode.Open, FileAccess.Read);
            using BinaryReader reader = new BinaryReader(fs);

            reader.BaseStream.Seek(HEADER_SIZE, SeekOrigin.Begin);

            uint[] sizes = [spawnIniSize, spawnMapSize, initSaveSize, eventDataSize];
            for (int i = 0; i < sectionIndex; i++)
                reader.BaseStream.Seek(sizes[i], SeekOrigin.Current);

            return reader.ReadBytes((int)sizes[sectionIndex]);
        }

        private static string ReadFixedString(BinaryReader reader, int maxBytes)
        {
            byte[] bytes = reader.ReadBytes(maxBytes);
            int nullIndex = Array.IndexOf(bytes, (byte)0);
            return Encoding.UTF8.GetString(bytes, 0, nullIndex < 0 ? maxBytes : nullIndex);
        }

        private static void WriteFixedString(BinaryWriter writer, string value, int maxBytes)
        {
            byte[] bytes = new byte[maxBytes];
            if (!string.IsNullOrEmpty(value))
            {
                byte[] encoded = Encoding.UTF8.GetBytes(value);
                int len = Math.Min(encoded.Length, maxBytes - 1);
                Array.Copy(encoded, bytes, len);
            }
            writer.Write(bytes);
        }

        private static byte[] SafeReadAllBytes(string path)
        {
            if (string.IsNullOrEmpty(path) || !File.Exists(path))
                return Array.Empty<byte>();
            return File.ReadAllBytes(path);
        }

        private static bool VersionsDiffer(string stored, string current)
        {
            if (string.IsNullOrWhiteSpace(stored) || string.IsNullOrWhiteSpace(current))
                return false;
            if (stored == "N/A" || current == "N/A")
                return false;
            return !string.Equals(stored, current, StringComparison.OrdinalIgnoreCase);
        }

        private static string GetBinaryVersion(string gamePath, string fileName)
        {
            string path = Path.Combine(gamePath, fileName);
            if (!File.Exists(path))
                return "N/A";
            try
            {
                FileVersionInfo info = FileVersionInfo.GetVersionInfo(path);
                if (!string.IsNullOrWhiteSpace(info.FileVersion))
                    return info.FileVersion;
                if (!string.IsNullOrWhiteSpace(info.ProductVersion))
                    return info.ProductVersion;
            }
            catch { }
            return "N/A";
        }
    }
}
