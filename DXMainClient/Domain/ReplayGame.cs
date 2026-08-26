#nullable enable

using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.IO;

using ClientCore.PlatformShim;

using Rampastring.Tools;

namespace DTAClient.Domain;

/// <summary>
/// A replay file. Listing only reads the header and the embedded spawn.ini; the spawn files
/// themselves are re-read on demand.
/// </summary>
public class ReplayGame
{
    /// <summary>Highest game speed index the engine accepts.</summary>
    public const int MAX_GAME_SPEED_INDEX = 6;

    private const uint REPLAY_MAGIC = 0x4A455259;
    private const uint SUPPORTED_REPLAY_FORMAT_VERSION = 1;

    /// <summary>
    /// The spawner embeds spawn.ini and spawnmap.ini whole, so these are bounded by the size of
    /// two text files. The cap only exists so a corrupt header cannot make us try to allocate
    /// two gigabytes.
    /// </summary>
    private const uint MAX_EMBEDDED_FILE_SIZE = 32 * 1024 * 1024;

    // Replay header layout, written by the spawner. Both sides have to change together; see
    // docs/replay-format.md in the spawner repository.
    private const int HEADER_SIZE = 1384;
    private const int OFFSET_MAGIC = 0;
    private const int OFFSET_FORMAT_VERSION = 4;
    private const int OFFSET_MAP_NAME = 8;
    private const int LENGTH_MAP_NAME = 260;
    private const int OFFSET_GAME_CLIENT_VERSION = 272;
    private const int LENGTH_GAME_CLIENT_VERSION = 64;
    private const int OFFSET_SPAWN_INI_SIZE = 1356;
    private const int OFFSET_SPAWN_MAP_SIZE = 1360;
    private const int OFFSET_RECORDED_GAME_SPEED = 1364;
    private const int OFFSET_RECORDED_UNIX_TIME = 1368;
    private const int OFFSET_TOTAL_FRAMES = 1376;
    private const int OFFSET_FLAGS = 1380;

    /// <summary>Set by the spawner only once a recording has been closed cleanly.</summary>
    private const uint HEADER_FLAG_CLEAN_SHUTDOWN = 1;

    public ReplayGame(string fileName)
    {
        FileName = fileName;
    }

    public string FileName { get; }

    public string GUIName { get; private set; } = string.Empty;

    /// <summary>
    /// The game package the recording player was running, e.g. "YR 9.3.1". Empty when the
    /// recording client did not report one.
    /// </summary>
    public string GameClientVersion { get; private set; } = string.Empty;

    /// <summary>The lobby's game mode name, e.g. "Battle".</summary>
    public string UIGameMode { get; private set; } = string.Empty;

    /// <summary>Human players, in spawn.ini order, the recording player first.</summary>
    public IReadOnlyList<ReplayPlayer> Players => players;

    private readonly List<ReplayPlayer> players = new List<ReplayPlayer>();

    /// <summary>
    /// When the recording started, from the replay header.
    /// </summary>
    public DateTime RecordedAt { get; private set; }

    /// <summary>
    /// False when the spawner never got to close the recording, meaning the game crashed or was
    /// killed while recording and the frame stream is cut short.
    /// </summary>
    public bool IsComplete { get; private set; }

    /// <summary>
    /// How long the recorded game ran. <see cref="TimeSpan.Zero"/> for an incomplete recording.
    /// </summary>
    public TimeSpan Duration { get; private set; }

    /// <summary>
    /// Game frames the recording covers. Zero when <see cref="IsComplete"/> is false, because the
    /// spawner never got to stamp the real count into the header.
    /// </summary>
    public uint TotalFrames { get; private set; }

    /// <summary>
    /// The rate the recorded game ticked at, which playback is pinned to.
    /// </summary>
    public int FramesPerSecond { get; private set; }

    private uint spawnIniSize;
    private uint spawnMapSize;

    /// <summary>
    /// Reads and sets the replay's metadata and returns true if successful.
    /// </summary>
    public bool ParseInfo()
    {
        try
        {
            FileInfo replayFileInfo = ReplayManager.GetFile(FileName);

            if (!replayFileInfo.Exists)
            {
                Logger.Log("Replay file does not exist: " + FileName);
                return false;
            }

            using FileStream stream = replayFileInfo.Open(FileMode.Open, FileAccess.Read);

            byte[] header = new byte[HEADER_SIZE];
            if (stream.Length < HEADER_SIZE || !TryReadExactly(stream, header, HEADER_SIZE))
            {
                Logger.Log("Replay file is too small to contain a header: " + FileName);
                return false;
            }

            uint magic = ReadUInt32(header, OFFSET_MAGIC);
            if (magic != REPLAY_MAGIC)
            {
                Logger.Log($"Invalid replay file magic number: 0x{magic:X8} (expected 0x{REPLAY_MAGIC:X8})");
                return false;
            }

            uint formatVersion = ReadUInt32(header, OFFSET_FORMAT_VERSION);
            if (formatVersion != SUPPORTED_REPLAY_FORMAT_VERSION)
            {
                Logger.Log("Unsupported replay version: " + formatVersion);
                return false;
            }

            spawnIniSize = ReadUInt32(header, OFFSET_SPAWN_INI_SIZE);
            spawnMapSize = ReadUInt32(header, OFFSET_SPAWN_MAP_SIZE);

            if (!AreEmbeddedSizesValid(stream.Length))
            {
                Logger.Log($"Replay {FileName} declares embedded file sizes that do not fit the file");
                return false;
            }

            string mapName = DecodeCString(header, OFFSET_MAP_NAME, LENGTH_MAP_NAME);
            GameClientVersion = DecodeCString(header, OFFSET_GAME_CLIENT_VERSION, LENGTH_GAME_CLIENT_VERSION);

            RecordedAt = FromUnixTime(ReadUInt64(header, OFFSET_RECORDED_UNIX_TIME), replayFileInfo.LastWriteTime);

            uint totalFrames = ReadUInt32(header, OFFSET_TOTAL_FRAMES);
            int gameSpeed = (int)Math.Min(ReadUInt32(header, OFFSET_RECORDED_GAME_SPEED), (uint)MAX_GAME_SPEED_INDEX);

            // A recording that died with the game never gets the flag stamped, which is the only
            // way to tell it apart from one that was quit immediately.
            IsComplete = (ReadUInt32(header, OFFSET_FLAGS) & HEADER_FLAG_CLEAN_SHUTDOWN) != 0;

            TotalFrames = IsComplete ? totalFrames : 0;
            FramesPerSecond = GetFramesPerSecond(gameSpeed);
            Duration = IsComplete
                ? TimeSpan.FromSeconds(TotalFrames / (double)FramesPerSecond)
                : TimeSpan.Zero;

            string? spawnIniContent = ReadText(stream, spawnIniSize);

            if (spawnIniContent == null || !StartsWithIniSection(spawnIniContent))
            {
                Logger.Log($"Replay {FileName} does not contain a readable spawn.ini; " +
                    "it was probably recorded by a different version of the spawner.");
                return false;
            }

            ReadEmbeddedSpawnIni(spawnIniContent);

            GUIName = string.IsNullOrWhiteSpace(mapName)
                ? Path.GetFileNameWithoutExtension(FileName)
                : mapName;

            return true;
        }
        catch (Exception ex)
        {
            Logger.Log("An error occurred while parsing replay " + FileName + ": " + ex.ToString());
            return false;
        }
    }

    /// <summary>
    /// Re-reads the spawn.ini and spawnmap.ini embedded in the replay.
    /// </summary>
    /// <returns>False when either file is absent or the replay can no longer be read.</returns>
    public bool TryReadSpawnFiles(out string spawnIni, out byte[] spawnMap)
    {
        spawnIni = string.Empty;
        spawnMap = Array.Empty<byte>();

        if (spawnIniSize == 0 || spawnMapSize == 0)
            return false;

        try
        {
            using FileStream stream = ReplayManager.GetFile(FileName).Open(FileMode.Open, FileAccess.Read);
            stream.Seek(HEADER_SIZE, SeekOrigin.Begin);

            string? readSpawnIni = ReadText(stream, spawnIniSize);
            byte[]? readSpawnMap = ReadBytes(stream, spawnMapSize);

            if (readSpawnIni == null || readSpawnMap == null)
            {
                Logger.Log("Replay file ended early while reading its spawn files: " + FileName);
                return false;
            }

            spawnIni = readSpawnIni;
            spawnMap = readSpawnMap;

            return true;
        }
        catch (Exception ex)
        {
            Logger.Log("An error occurred while reading the spawn files of " + FileName + ": " + ex.ToString());
            spawnIni = string.Empty;
            spawnMap = Array.Empty<byte>();
            return false;
        }
    }

    /// <summary>
    /// The rate the game ticks at for a given game speed index, matching the spawner's
    /// GetReplayFPSFromGameSpeed. Used to turn a frame count into a duration.
    /// </summary>
    public static int GetFramesPerSecond(int gameSpeed)
    {
        gameSpeed = Math.Max(0, Math.Min(MAX_GAME_SPEED_INDEX, gameSpeed));

        if (gameSpeed <= 0)
            return 60;

        if (gameSpeed == 1)
            return 45;

        return Math.Max(1, 60 / gameSpeed);
    }

    /// <summary>
    /// Pulls display metadata from the embedded spawn.ini.
    /// </summary>
    private void ReadEmbeddedSpawnIni(string spawnIniContent)
    {
        using MemoryStream spawnIniStream = new MemoryStream(EncodingExt.UTF8NoBOM.GetBytes(spawnIniContent));
        IniFile spawnIni = new IniFile(spawnIniStream, EncodingExt.UTF8NoBOM, applyBaseIni: false);

        if (string.IsNullOrWhiteSpace(GameClientVersion))
            GameClientVersion = spawnIni.GetStringValue("Settings", "GameClientVersion", string.Empty);

        UIGameMode = spawnIni.GetStringValue("Settings", "UIGameMode", string.Empty);

        // The recording player is always [Settings]; everyone else is [OtherN], in the order the
        // recording client wrote them. That order is the spawner's player slot order, which is what
        // a perspective choice is expressed in, so the indices have to be kept as they are found.
        AddPlayer(spawnIni, "Settings", 0);

        for (int otherId = 1; ; otherId++)
        {
            if (!AddPlayer(spawnIni, "Other" + otherId, otherId))
                break;
        }
    }

    /// <summary>
    /// Adds the player in <paramref name="sectionName"/>, if there is one.
    /// </summary>
    /// <returns>False when the section names no player, which ends the run of [OtherN] sections.</returns>
    private bool AddPlayer(IniFile spawnIni, string sectionName, int spawnIniIndex)
    {
        string name = spawnIni.GetStringValue(sectionName, "Name", string.Empty);
        if (string.IsNullOrWhiteSpace(name))
            return false;

        players.Add(new ReplayPlayer(
            spawnIniIndex,
            name,
            spawnIni.GetIntValue(sectionName, "Side", -1),
            spawnIni.GetBooleanValue(sectionName, "IsSpectator", false)));

        return true;
    }

    private bool AreEmbeddedSizesValid(long fileLength)
    {
        if (spawnIniSize > MAX_EMBEDDED_FILE_SIZE || spawnMapSize > MAX_EMBEDDED_FILE_SIZE)
            return false;

        return HEADER_SIZE + (long)spawnIniSize + spawnMapSize <= fileLength;
    }

    /// <summary>
    /// Cheap check that the bytes following the header really are the embedded spawn.ini,
    /// which is the only way to notice that a replay's header layout differs from ours.
    /// </summary>
    private static bool StartsWithIniSection(string content)
        => !string.IsNullOrWhiteSpace(content) && content.TrimStart().StartsWith("[", StringComparison.Ordinal);

    /// <summary>Returns null when the file ends before <paramref name="size"/> bytes are read.</summary>
    private static byte[]? ReadBytes(Stream stream, uint size)
    {
        byte[] buffer = new byte[size];

        return TryReadExactly(stream, buffer, (int)size) ? buffer : null;
    }

    /// <summary>Returns null when the file ends before <paramref name="size"/> bytes are read.</summary>
    private static string? ReadText(Stream stream, uint size)
    {
        byte[]? buffer = ReadBytes(stream, size);

        return buffer == null ? null : EncodingExt.UTF8NoBOM.GetString(buffer);
    }

    private static bool TryReadExactly(Stream stream, byte[] buffer, int count)
    {
        int offset = 0;
        while (offset < count)
        {
            int read = stream.Read(buffer, offset, count - offset);
            if (read <= 0)
                return false;

            offset += read;
        }

        return true;
    }

    private static uint ReadUInt32(byte[] header, int offset)
        => BinaryPrimitives.ReadUInt32LittleEndian(new ReadOnlySpan<byte>(header, offset, sizeof(uint)));

    private static ulong ReadUInt64(byte[] header, int offset)
        => BinaryPrimitives.ReadUInt64LittleEndian(new ReadOnlySpan<byte>(header, offset, sizeof(ulong)));

    private static DateTime FromUnixTime(ulong unixTime, DateTime fallback)
    {
        if (unixTime == 0)
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

    private static string DecodeCString(byte[] bytes, int offset, int length)
    {
        int end = Array.IndexOf(bytes, (byte)0, offset, length);
        if (end < 0)
            end = offset + length;

        return EncodingExt.UTF8NoBOM.GetString(bytes, offset, end - offset);
    }
}
