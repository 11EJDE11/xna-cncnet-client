using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;

using Rampastring.Tools;

namespace ClientLogic.Protocol;

/// <summary>
/// The "GAME" line a game host sends to the game broadcast channel every few seconds, which fills the CnCNet game
/// list. Every client version in the channel reads it (and possibly other tools), so its format doesn't change:
/// 14 <c>;</c>-separated fields, no escaping.
/// </summary>
/// <param name="Tunnel">"[DYN]" for dynamic tunnels, otherwise "address:port".</param>
/// <param name="GameOptionValues">The broadcast game option values (<see cref="BroadcastedGameOptionValues"/>).</param>
public sealed record GameBroadcastMessage(
    string Revision,
    string GameVersion,
    int MaxPlayers,
    string ChannelName,
    string RoomName,
    bool Locked,
    bool IsCustomPassword,
    bool IsClosed,
    bool IsLoadedGame,
    bool IsLadder,
    IReadOnlyList<string> Players,
    string MapName,
    string GameMode,
    string Tunnel,
    string LoadedGameId,
    string SkillLevel,
    string MapHash,
    string GameOptionValues)
{
    public const string COMMAND = "GAME";
    public const string DYNAMIC_TUNNELS = "[DYN]";
    public const int FIELD_COUNT = 14;

    public bool IsDynamicTunnels => Tunnel == DYNAMIC_TUNNELS;

    /// <summary>The CTCP message, including the "GAME " prefix.</summary>
    public string Encode()
    {
        var sb = new StringBuilder(COMMAND + " ");
        sb.Append(Revision).Append(';');
        sb.Append(GameVersion).Append(';');
        sb.Append(MaxPlayers.ToString(CultureInfo.InvariantCulture)).Append(';');
        sb.Append(ChannelName).Append(';');
        sb.Append(RoomName).Append(';');
        sb.Append(Flag(Locked)).Append(Flag(IsCustomPassword)).Append(Flag(IsClosed)).Append(Flag(IsLoadedGame)).Append(Flag(IsLadder));
        sb.Append(';');

        foreach (string player in Players)
            sb.Append(player).Append(',');

        // Drops the last comma (as the client always has; with no players this drops the ';' before them)
        sb.Remove(sb.Length - 1, 1);
        sb.Append(';');
        sb.Append(MapName).Append(';');
        sb.Append(GameMode).Append(';');
        sb.Append(Tunnel).Append(';');
        sb.Append(LoadedGameId).Append(';');
        sb.Append(SkillLevel).Append(';');
        sb.Append(MapHash).Append(';');
        sb.Append(GameOptionValues);
        return sb.ToString();
    }

    private static char Flag(bool value) => value ? '1' : '0';

    /// <summary>
    /// Reads the fields after "GAME ". Returns false if the field count is wrong (another client version).
    /// </summary>
    /// <exception cref="System.ArgumentOutOfRangeException">The flags field is too short.</exception>
    public static bool TryDecode(string payload, out GameBroadcastMessage message)
    {
        message = null;
        string[] fields = payload.Split(';');
        if (fields.Length != FIELD_COUNT)
            return false;

        string flags = fields[5];
        message = new GameBroadcastMessage(
            Revision: fields[0],
            GameVersion: fields[1],
            MaxPlayers: Conversions.IntFromString(fields[2], 0),
            ChannelName: fields[3],
            RoomName: fields[4],
            Locked: Conversions.BooleanFromString(flags.Substring(0, 1), true),
            IsCustomPassword: Conversions.BooleanFromString(flags.Substring(1, 1), false),
            IsClosed: Conversions.BooleanFromString(flags.Substring(2, 1), true),
            IsLoadedGame: Conversions.BooleanFromString(flags.Substring(3, 1), false),
            IsLadder: Conversions.BooleanFromString(flags.Substring(4, 1), false),
            Players: fields[6].Split(new[] { ',' }, System.StringSplitOptions.RemoveEmptyEntries).ToList(),
            MapName: fields[7],
            GameMode: fields[8],
            Tunnel: fields[9],
            LoadedGameId: fields[10],
            SkillLevel: fields[11],
            MapHash: fields[12],
            GameOptionValues: fields[13]);
        return true;
    }
}
