#nullable enable

using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.IO;

using ClientCore.PlatformShim;

using Rampastring.Tools;

namespace DTAClient.Domain;

/// <summary>
/// Whether a listed replay can be played by this build.
/// </summary>
public enum ReplayStatus
{
    Playable,

    /// <summary>
    /// A valid replay whose layout generation this build does not know - almost always a recording
    /// from a newer version of the game. Listed rather than hidden: the file is real, the player
    /// knows they recorded it, and silently omitting it reads as the update having deleted it.
    /// </summary>
    UnsupportedVersion
}

/// <summary>
/// A replay file. Listing only reads the header and the embedded spawn.ini; the spawn files
/// themselves are re-read on demand.
/// </summary>
public class ReplayGame
{
    /// <summary>Highest game speed index the engine accepts.</summary>
    public const int MAX_GAME_SPEED_INDEX = 6;

    private const uint REPLAY_MAGIC = 0x4A455259;

    /// <summary>
    /// Range of layout generations this build can read. Both ends are 1 because there has only
    /// ever been one, and they are separate constants because the day the spawner bumps its
    /// REPLAY_VERSION for an incompatible change, leaving the minimum behind is what keeps every
    /// replay recorded before that break in the list instead of silently dropping it.
    ///
    /// This says nothing about whether a replay will play back <em>correctly</em>. That depends on
    /// the game files matching, which is <see cref="ReplayFileHashes"/>'s job, and a replay can be
    /// a perfectly readable version 1 file and still diverge because the rules moved.
    /// </summary>
    private const uint MIN_SUPPORTED_REPLAY_FORMAT_VERSION = 1;
    private const uint MAX_SUPPORTED_REPLAY_FORMAT_VERSION = 1;

    /// <summary>
    /// The spawner embeds spawn.ini and spawnmap.ini whole, so these are bounded by the size of
    /// two text files. The cap only exists so a corrupt header cannot make us try to allocate
    /// two gigabytes.
    /// </summary>
    private const uint MAX_EMBEDDED_FILE_SIZE = 32 * 1024 * 1024;

    // Replay header layout, written by the spawner. Both sides have to change together; see
    // docs/replay-format.md in the spawner repository, where every offset below is also pinned by
    // a static_assert in ReplayFormat.h.

    /// <summary>
    /// Magic, format version and header size. The only part of the file whose meaning is fixed
    /// across every version there will ever be, so it is read and checked before anything else is
    /// assumed to be where this build thinks it is.
    /// </summary>
    private const int STABLE_PREFIX_SIZE = 12;

    /// <summary>
    /// Size of the header as this build knows it. The file's own header may be longer, if it was
    /// written by a build that appended fields; everything below is still at these offsets, and
    /// the extra bytes are skipped by seeking to the header size the file declares.
    /// </summary>
    private const int KNOWN_HEADER_SIZE = 1452;

    /// <summary>Refuses an absurd declared header size before it becomes an allocation.</summary>
    private const uint MAX_HEADER_SIZE = 64 * 1024;

    private const int OFFSET_MAGIC = 0;
    private const int OFFSET_FORMAT_VERSION = 4;
    private const int OFFSET_HEADER_SIZE = 8;
    private const int OFFSET_MAP_NAME = 12;
    private const int LENGTH_MAP_NAME = 260;
    private const int OFFSET_SPAWNER_VERSION = 272;
    private const int LENGTH_SPAWNER_VERSION = 4;
    private const int OFFSET_GAME_CLIENT_VERSION = 276;
    private const int LENGTH_GAME_CLIENT_VERSION = 64;
    private const int OFFSET_SPAWN_INI_SIZE = 1360;
    private const int OFFSET_SPAWN_MAP_SIZE = 1364;
    private const int OFFSET_RECORDED_GAME_SPEED = 1368;
    private const int OFFSET_RECORDED_UNIX_TIME = 1372;
    private const int OFFSET_TOTAL_FRAMES = 1380;
    private const int OFFSET_FLAGS = 1384;

    /// <summary>Set by the spawner only once a recording has been closed cleanly.</summary>
    private const uint HEADER_FLAG_CLEAN_SHUTDOWN = 1;

    public ReplayGame(string fileName)
    {
        FileName = fileName;
    }

    public string FileName { get; }

    public string GUIName { get; private set; } = string.Empty;

    /// <summary>
    /// Whether this build can play the replay at all. Anything other than
    /// <see cref="ReplayStatus.Playable"/> leaves most of the metadata below unset, because the
    /// offsets it lives at are only meaningful for a known layout generation.
    /// </summary>
    public ReplayStatus Status { get; private set; } = ReplayStatus.Playable;

    public bool IsPlayable => Status == ReplayStatus.Playable;

    /// <summary>The replay's on-disk layout generation, from the header.</summary>
    public uint FormatVersion { get; private set; }

    /// <summary>
    /// The game package the recording player was running, e.g. "YR 9.3.1". Empty when the
    /// recording client did not report one.
    /// </summary>
    public string GameClientVersion { get; private set; } = string.Empty;

    /// <summary>
    /// Build of the spawner that recorded this replay, e.g. "0.0.0.16". The spawner stamps its own
    /// compiled-in version unless spawn.ini overrides it, so this identifies the DLL that produced
    /// the file - which is the first thing worth knowing when a replay will not play.
    /// </summary>
    public string SpawnerVersion { get; private set; } = string.Empty;

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
    /// Header size as the file declares it, which is where the embedded spawn.ini starts. Equal to
    /// <see cref="KNOWN_HEADER_SIZE"/> for anything this build recorded, and larger for a recording
    /// from a build that appended header fields.
    /// </summary>
    private uint headerSize;

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

            // The stable prefix first, and nothing beyond it assumed until the version it carries
            // has been checked. A newer replay may lay the rest of its header out differently.
            byte[] prefix = new byte[STABLE_PREFIX_SIZE];
            if (stream.Length < STABLE_PREFIX_SIZE || !TryReadExactly(stream, prefix, STABLE_PREFIX_SIZE))
            {
                Logger.Log("Replay file is too small to contain a header: " + FileName);
                return false;
            }

            uint magic = ReadUInt32(prefix, OFFSET_MAGIC);
            if (magic != REPLAY_MAGIC)
            {
                Logger.Log($"Invalid replay file magic number: 0x{magic:X8} (expected 0x{REPLAY_MAGIC:X8})");
                return false;
            }

            FormatVersion = ReadUInt32(prefix, OFFSET_FORMAT_VERSION);
            headerSize = ReadUInt32(prefix, OFFSET_HEADER_SIZE);

            if (FormatVersion < MIN_SUPPORTED_REPLAY_FORMAT_VERSION
                || FormatVersion > MAX_SUPPORTED_REPLAY_FORMAT_VERSION)
            {
                Logger.Log($"Replay {FileName} is format version {FormatVersion}; this build reads " +
                    $"{MIN_SUPPORTED_REPLAY_FORMAT_VERSION} to {MAX_SUPPORTED_REPLAY_FORMAT_VERSION}.");

                // Listed, not dropped. Nothing past the prefix can be trusted at an unknown
                // version, so it is described by the only things that do not depend on the layout.
                Status = ReplayStatus.UnsupportedVersion;
                GUIName = Path.GetFileNameWithoutExtension(FileName);
                RecordedAt = replayFileInfo.LastWriteTime;
                return true;
            }

            // Shorter than this build's header means fields it reads are absent. Longer is the case
            // HeaderSize exists for: a later build appended to the header, everything this one knows
            // is still at the offsets below, and the seek past it lands on the spawn.ini regardless.
            if (headerSize < KNOWN_HEADER_SIZE || headerSize > MAX_HEADER_SIZE)
            {
                Logger.Log($"Replay {FileName} declares an unusable header size of {headerSize}.");
                return false;
            }

            byte[] header = new byte[KNOWN_HEADER_SIZE];
            Array.Copy(prefix, header, STABLE_PREFIX_SIZE);

            if (stream.Length < KNOWN_HEADER_SIZE
                || !TryReadExactly(stream, header, KNOWN_HEADER_SIZE - STABLE_PREFIX_SIZE, STABLE_PREFIX_SIZE))
            {
                Logger.Log("Replay file is too small to contain a header: " + FileName);
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
            SpawnerVersion = DecodeVersion(header, OFFSET_SPAWNER_VERSION, LENGTH_SPAWNER_VERSION);

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

            // By the header's declared size rather than this build's, so a header that grew in a
            // later version is stepped over instead of being read as the start of the spawn.ini.
            stream.Seek(headerSize, SeekOrigin.Begin);

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

        if (!IsPlayable || spawnIniSize == 0 || spawnMapSize == 0)
            return false;

        try
        {
            using FileStream stream = ReplayManager.GetFile(FileName).Open(FileMode.Open, FileAccess.Read);
            stream.Seek(headerSize, SeekOrigin.Begin);

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

        return headerSize + (long)spawnIniSize + spawnMapSize <= fileLength;
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
        => TryReadExactly(stream, buffer, count, 0);

    /// <summary>
    /// Reads <paramref name="count"/> bytes into <paramref name="buffer"/> starting at
    /// <paramref name="bufferOffset"/>, so a partly filled buffer can be topped up.
    /// </summary>
    private static bool TryReadExactly(Stream stream, byte[] buffer, int count, int bufferOffset)
    {
        int offset = 0;
        while (offset < count)
        {
            int read = stream.Read(buffer, bufferOffset + offset, count - offset);
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

    /// <summary>
    /// Reads a run of version bytes as a dotted string, e.g. "0.0.0.16". All-zero reads as empty:
    /// the spawner writes its compiled-in version, so zero means the field was never filled in
    /// rather than that the version really is 0.0.0.0.
    /// </summary>
    private static string DecodeVersion(byte[] bytes, int offset, int length)
    {
        var parts = new string[length];
        bool anyNonZero = false;

        for (int i = 0; i < length; i++)
        {
            byte value = bytes[offset + i];
            anyNonZero |= value != 0;
            parts[i] = value.ToString();
        }

        return anyNonZero ? string.Join(".", parts) : string.Empty;
    }

    private static string DecodeCString(byte[] bytes, int offset, int length)
    {
        int end = Array.IndexOf(bytes, (byte)0, offset, length);
        if (end < 0)
            end = offset + length;

        return EncodingExt.UTF8NoBOM.GetString(bytes, offset, end - offset);
    }
}
