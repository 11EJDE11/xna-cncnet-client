using System.Collections.Generic;

namespace ClientLogic.Protocol;

/// <summary>One player in a <see cref="PlayerOptionsMessage"/>.</summary>
/// <param name="Name">The player name; empty for AI players.</param>
/// <param name="AILevel">The AI difficulty (0-2), or -1 for a human player.</param>
/// <param name="Options">Side, colour, start and team.</param>
/// <param name="ReadyState">0 = not ready, 1 = ready, 2 = auto-ready.</param>
/// <param name="Address">The player's IP address on LAN; empty on CnCNet.</param>
public sealed record PlayerOptionsEntry(string Name, int AILevel, PackedPlayerOptions Options, int ReadyState, string Address)
{
    public bool IsAI => AILevel >= 0;
}

/// <summary>
/// The host's list of human and AI players with their options.
/// Sent as "PO" on CnCNet and "POPTS" on LAN.
/// </summary>
public sealed record PlayerOptionsMessage(IReadOnlyList<PlayerOptionsEntry> Players)
{
    private const int MAX_PLAYERS = 64;

    public string Encode()
    {
        var writer = new MessageFieldWriter().Add(Players.Count);

        foreach (PlayerOptionsEntry player in Players)
        {
            writer.AddText(player.IsAI ? string.Empty : player.Name)
                .Add(player.AILevel)
                .Add(player.Options.Pack())
                .Add(player.ReadyState)
                .AddText(player.Address ?? string.Empty);
        }

        return writer.ToString();
    }

    /// <summary>Decodes the whole message; fails without a partial result if any player is malformed.</summary>
    public static bool TryDecode(string payload, out PlayerOptionsMessage message)
    {
        message = null;
        var reader = new MessageFieldReader(payload);

        if (!reader.TryReadInt(0, MAX_PLAYERS, out int count))
            return false;

        var players = new List<PlayerOptionsEntry>(count);
        for (int i = 0; i < count; i++)
        {
            if (!reader.TryReadText(out string name)
                || !reader.TryReadInt(-1, 2, out int aiLevel)
                || !reader.TryReadInt(out int packedOptions)
                || !reader.TryReadInt(0, 2, out int readyState)
                || !reader.TryReadText(out string address))
            {
                return false;
            }

            if (aiLevel < 0 && string.IsNullOrEmpty(name))
                return false;

            players.Add(new PlayerOptionsEntry(name, aiLevel, PackedPlayerOptions.Unpack(packedOptions), readyState, address));
        }

        if (!reader.IsAtEnd)
            return false;

        message = new PlayerOptionsMessage(players);
        return true;
    }
}

/// <summary>
/// A player asks the host to change their side, colour, start or team.
/// Sent as "OR" on CnCNet and "POREQ" on LAN.
/// </summary>
public sealed record PlayerOptionsRequestMessage(PackedPlayerOptions Options)
{
    public string Encode() => new MessageFieldWriter().Add(Options.Pack()).ToString();

    public static bool TryDecode(string payload, out PlayerOptionsRequestMessage message)
    {
        message = null;
        var reader = new MessageFieldReader(payload);

        if (!reader.TryReadInt(out int packedOptions) || !reader.IsAtEnd)
            return false;

        message = new PlayerOptionsRequestMessage(PackedPlayerOptions.Unpack(packedOptions));
        return true;
    }
}

/// <summary>
/// A player tells the host their ready state: 0 = not ready, 1 = ready, 2 = auto-ready.
/// Sent as "R" on CnCNet and "READY" on LAN.
/// </summary>
public sealed record ReadyRequestMessage(int ReadyState)
{
    public string Encode() => new MessageFieldWriter().Add(ReadyState).ToString();

    public static bool TryDecode(string payload, out ReadyRequestMessage message)
    {
        message = null;
        var reader = new MessageFieldReader(payload);

        if (!reader.TryReadInt(0, 2, out int readyState) || !reader.IsAtEnd)
            return false;

        message = new ReadyRequestMessage(readyState);
        return true;
    }
}

/// <summary>
/// The game room's settings, sent by the host as "GSETTINGS" (CnCNet only).
/// </summary>
public sealed record GameRoomSettingsMessage(string RoomName, int PlayerLimit, int SkillLevel, bool IsCustomPassword)
{
    public string Encode() => new MessageFieldWriter()
        .AddText(RoomName)
        .Add(PlayerLimit)
        .Add(SkillLevel)
        .Add(IsCustomPassword)
        .ToString();

    public static bool TryDecode(string payload, out GameRoomSettingsMessage message)
    {
        message = null;
        var reader = new MessageFieldReader(payload);

        if (!reader.TryReadText(out string roomName)
            || !reader.TryReadInt(out int playerLimit)
            || !reader.TryReadInt(out int skillLevel)
            || !reader.TryReadBool(out bool isCustomPassword)
            || !reader.IsAtEnd)
        {
            return false;
        }

        message = new GameRoomSettingsMessage(roomName, playerLimit, skillLevel, isCustomPassword);
        return true;
    }
}
