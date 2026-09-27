using System.Collections.Generic;

namespace ClientLogic.Protocol;

/// <summary>A player's tunnel endpoint in a <see cref="StartV2Message"/>.</summary>
public sealed record StartV2Entry(string Name, string TunnelAddress, int Port);

/// <summary>
/// The host starts a game over a V2 tunnel: the game ID and each player's tunnel address and port.
/// Sent as "STARTV2" on CnCNet.
/// </summary>
public sealed record StartV2Message(int GameId, IReadOnlyList<StartV2Entry> Players)
{
    public string Encode()
    {
        var writer = new MessageFieldWriter().Add(GameId).Add(Players.Count);

        foreach (StartV2Entry player in Players)
            writer.AddText(player.Name).AddText(player.TunnelAddress).Add(player.Port);

        return writer.ToString();
    }

    public static bool TryDecode(string payload, out StartV2Message message)
    {
        message = null;
        var reader = new MessageFieldReader(payload);

        if (!reader.TryReadInt(0, int.MaxValue, out int gameId) || !reader.TryReadInt(0, 64, out int count))
            return false;

        var players = new List<StartV2Entry>(count);
        for (int i = 0; i < count; i++)
        {
            // The "port" is the V2 tunnel's client ID, which can be negative (a signed short)
            if (!reader.TryReadText(out string name)
                || !reader.TryReadText(out string address)
                || !reader.TryReadInt(out int port))
            {
                return false;
            }

            players.Add(new StartV2Entry(name, address, port));
        }

        if (!reader.IsAtEnd)
            return false;

        message = new StartV2Message(gameId, players);
        return true;
    }
}

/// <summary>A player's V3 tunnel ID and tunnel endpoint in a <see cref="StartV3Message"/>.</summary>
public sealed record StartV3Entry(uint Id, string Name, string TunnelAddress, int TunnelPort);

/// <summary>
/// The host starts a game over V3 tunnels: the game ID and each player's tunnel ID and tunnel.
/// The player order defines each player's in-game ID. Sent as "STARTV3" on CnCNet.
/// </summary>
public sealed record StartV3Message(int GameId, IReadOnlyList<StartV3Entry> Players)
{
    public string Encode()
    {
        var writer = new MessageFieldWriter().Add(GameId).Add(Players.Count);

        foreach (StartV3Entry player in Players)
            writer.Add(player.Id).AddText(player.Name).AddText(player.TunnelAddress).Add(player.TunnelPort);

        return writer.ToString();
    }

    public static bool TryDecode(string payload, out StartV3Message message)
    {
        message = null;
        var reader = new MessageFieldReader(payload);

        if (!reader.TryReadInt(0, int.MaxValue, out int gameId) || !reader.TryReadInt(0, 64, out int count))
            return false;

        var players = new List<StartV3Entry>(count);
        for (int i = 0; i < count; i++)
        {
            if (!reader.TryReadUInt(out uint id)
                || !reader.TryReadText(out string name)
                || !reader.TryReadText(out string address)
                || !reader.TryReadInt(0, ushort.MaxValue, out int port))
            {
                return false;
            }

            players.Add(new StartV3Entry(id, name, address, port));
        }

        if (!reader.IsAtEnd)
            return false;

        message = new StartV3Message(gameId, players);
        return true;
    }
}

/// <summary>The LAN host starts the game. Sent as "LAUNCH" on LAN.</summary>
public sealed record LaunchMessage(int GameId)
{
    public string Encode() => new MessageFieldWriter().Add(GameId).ToString();

    public static bool TryDecode(string payload, out LaunchMessage message)
    {
        message = null;
        var reader = new MessageFieldReader(payload);

        if (!reader.TryReadInt(0, int.MaxValue, out int gameId) || !reader.IsAtEnd)
            return false;

        message = new LaunchMessage(gameId);
        return true;
    }
}
