using System;
using System.Collections.Generic;
using System.Linq;

using ClientCore.Enums;

using DTAClient.Domain.Multiplayer;

namespace ClientLogic.GameList;

/// <summary>
/// The games hosted in a lobby's game list (CnCNet or LAN), one per host. Hosts announce their game every few
/// seconds; a game that hasn't been announced for <see cref="GameLifetime"/> is dropped.
/// </summary>
public sealed class GameListState
{
    /// <summary>The hosted games in the order they were first announced.</summary>
    public List<GenericHostedGame> Games { get; } = [];

    /// <summary>Seconds a game stays listed after its last announcement.</summary>
    public double GameLifetime { get; set; } = 35.0;

    /// <summary>Adds a game, or replaces the game of the same host.</summary>
    /// <returns>True if the game was added (its host had no game listed).</returns>
    public bool AddOrUpdate(GenericHostedGame game)
    {
        int index = Games.FindIndex(hg => hg.HostName == game.HostName);
        if (index > -1)
        {
            Games[index] = game;
            return false;
        }

        Games.Add(game);
        return true;
    }

    /// <summary>Removes the game of a host.</summary>
    /// <returns>True if the host had a game listed.</returns>
    public bool RemoveByHost(string hostName)
    {
        int index = Games.FindIndex(hg => hg.HostName == hostName);
        if (index < 0)
            return false;

        Games.RemoveAt(index);
        return true;
    }

    /// <summary>Drops the games not announced within <see cref="GameLifetime"/> of <paramref name="now"/>.</summary>
    public void RemoveExpired(DateTime now) =>
        Games.RemoveAll(hg => now - hg.LastRefreshTime > TimeSpan.FromSeconds(GameLifetime));

    /// <summary>
    /// The display order: open games first, then the local game's games, then games on the local game version, then
    /// games without a password, then by room name in the chosen direction.
    /// </summary>
    /// <remarks>
    /// The boolean keys sort false before true, as the client always has; e.g. games of the local game come after
    /// games of other games.
    /// </remarks>
    public static IEnumerable<GenericHostedGame> Sort(IEnumerable<GenericHostedGame> games, string localGameIdentifier,
        string localGameVersion, SortDirection direction)
    {
        var sortedGames = games
            .OrderBy(hg => hg.Locked)
            .ThenBy(hg => string.Equals(hg.Game.InternalName, localGameIdentifier, StringComparison.InvariantCultureIgnoreCase))
            .ThenBy(hg => hg.GameVersion != localGameVersion)
            .ThenBy(hg => hg.Passworded);

        return direction switch
        {
            SortDirection.Asc => sortedGames.ThenBy(hg => hg.RoomName),
            SortDirection.Desc => sortedGames.ThenByDescending(hg => hg.RoomName),
            _ => sortedGames,
        };
    }
}
