using System.Collections.Generic;

namespace ClientLogic.Protocol;

/// <summary>
/// The host's game options: option values, map, game mode and game settings.
/// Sent as "GO" on CnCNet and "OPTS" on LAN.
/// </summary>
public sealed record GameOptionsMessage
{
    /// <summary>The check-box values, in the lobby's check-box order.</summary>
    public IReadOnlyList<bool> CheckBoxValues { get; init; } = [];

    /// <summary>The selected drop-down indices, in the lobby's drop-down order.</summary>
    public IReadOnlyList<int> DropDownIndices { get; init; } = [];

    public bool IsMapOfficial { get; init; }

    public string MapSHA1 { get; init; } = string.Empty;

    public string GameModeName { get; init; } = string.Empty;

    public int FrameSendRate { get; init; }

    public int MaxAhead { get; init; }

    public int ProtocolVersion { get; init; }

    public int RandomSeed { get; init; }

    public bool RemoveStartingLocations { get; init; }

    /// <summary>The untranslated map name, used as the file name when the map is downloaded.</summary>
    public string MapName { get; init; } = string.Empty;

    /// <summary>The <see cref="DTAClient.Domain.Multiplayer.CnCNet.TunnelMode"/> (CnCNet only).</summary>
    public int TunnelMode { get; init; }

    public string Encode()
    {
        var writer = new MessageFieldWriter();

        writer.Add(CheckBoxValues.Count);
        foreach (int packed in PackedCheckBoxes.Pack(CheckBoxValues))
            writer.Add(packed);

        writer.Add(DropDownIndices.Count);
        foreach (int index in DropDownIndices)
            writer.Add(index);

        return writer
            .Add(IsMapOfficial)
            .AddText(MapSHA1)
            .AddText(GameModeName)
            .Add(FrameSendRate)
            .Add(MaxAhead)
            .Add(ProtocolVersion)
            .Add(RandomSeed)
            .Add(RemoveStartingLocations)
            .AddText(MapName)
            .Add(TunnelMode)
            .ToString();
    }

    /// <summary>
    /// Decodes a game options message. Fails if the message is malformed or its option counts differ
    /// from the local lobby's, which means the host uses different game option INIs.
    /// </summary>
    public static bool TryDecode(string payload, int checkBoxCount, int dropDownCount, out GameOptionsMessage message)
    {
        message = null;
        var reader = new MessageFieldReader(payload);

        if (!reader.TryReadInt(out int sentCheckBoxCount) || sentCheckBoxCount != checkBoxCount)
            return false;

        var packedCheckBoxes = new int[PackedCheckBoxes.IntCount(checkBoxCount)];
        for (int i = 0; i < packedCheckBoxes.Length; i++)
        {
            if (!reader.TryReadInt(out packedCheckBoxes[i]))
                return false;
        }

        if (!reader.TryReadInt(out int sentDropDownCount) || sentDropDownCount != dropDownCount)
            return false;

        var dropDownIndices = new int[dropDownCount];
        for (int i = 0; i < dropDownCount; i++)
        {
            if (!reader.TryReadInt(out dropDownIndices[i]))
                return false;
        }

        if (!reader.TryReadBool(out bool isMapOfficial)
            || !reader.TryReadText(out string mapSHA1)
            || !reader.TryReadText(out string gameModeName)
            || !reader.TryReadInt(out int frameSendRate)
            || !reader.TryReadInt(out int maxAhead)
            || !reader.TryReadInt(out int protocolVersion)
            || !reader.TryReadInt(out int randomSeed)
            || !reader.TryReadBool(out bool removeStartingLocations)
            || !reader.TryReadText(out string mapName)
            || !reader.TryReadInt(out int tunnelMode)
            || !reader.IsAtEnd)
        {
            return false;
        }

        message = new GameOptionsMessage
        {
            CheckBoxValues = PackedCheckBoxes.Unpack(packedCheckBoxes, checkBoxCount),
            DropDownIndices = dropDownIndices,
            IsMapOfficial = isMapOfficial,
            MapSHA1 = mapSHA1,
            GameModeName = gameModeName,
            FrameSendRate = frameSendRate,
            MaxAhead = maxAhead,
            ProtocolVersion = protocolVersion,
            RandomSeed = randomSeed,
            RemoveStartingLocations = removeStartingLocations,
            MapName = mapName,
            TunnelMode = tunnelMode,
        };
        return true;
    }
}

/// <summary>Packs check-box values 32 to an integer: value i is bit (i % 32) of integer i / 32.</summary>
public static class PackedCheckBoxes
{
    public static int IntCount(int checkBoxCount) => (checkBoxCount + 31) / 32;

    public static int[] Pack(IReadOnlyList<bool> values)
    {
        var packed = new int[IntCount(values.Count)];
        for (int i = 0; i < values.Count; i++)
        {
            if (values[i])
                packed[i / 32] |= 1 << (i % 32);
        }

        return packed;
    }

    public static bool[] Unpack(IReadOnlyList<int> packed, int checkBoxCount)
    {
        var values = new bool[checkBoxCount];
        for (int i = 0; i < checkBoxCount && i / 32 < packed.Count; i++)
            values[i] = (packed[i / 32] & (1 << (i % 32))) != 0;

        return values;
    }
}
