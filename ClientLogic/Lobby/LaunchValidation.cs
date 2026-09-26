using System.Collections.Generic;
using System.Linq;

using DTAClient.Domain.Multiplayer;

namespace ClientLogic.Lobby;

/// <summary>A reason the game host can't launch the game yet.</summary>
public enum LaunchBlockerKind
{
    RoomNotLocked,
    TeamMappings,
    SharedColors,
    AiSpectators,
    SharedStartingLocation,
    InsufficientPlayers,
    TooManyPlayers,
    NotVerified,
    StillInGame,
    NotReady,
}

/// <param name="PlayerIndex">The player concerned (NotVerified, StillInGame), or -1.</param>
/// <param name="Message">The error text (TeamMappings), or null.</param>
public sealed record LaunchBlocker(LaunchBlockerKind Kind, int PlayerIndex = -1, string Message = null);

/// <summary>What the launch check needs to know about the lobby.</summary>
public sealed record LaunchCheck(
    bool Locked,
    string TeamMappingsError,
    IReadOnlyList<PlayerInfo> Players,
    IReadOnlyList<PlayerInfo> AIPlayers,
    string LocalPlayerName,
    int SpectatorSide,
    bool EnforceMinPlayers,
    int MinPlayers,
    bool EnforceMaxPlayers,
    int MaxPlayers);

/// <summary>The checks the game host's Launch button makes before a multiplayer game starts.</summary>
public static class LaunchValidation
{
    /// <summary>
    /// Returns everything that blocks the launch, in the order the lobby reports them; the lobby shows the first.
    /// Empty when the game can start.
    /// </summary>
    public static IReadOnlyList<LaunchBlocker> Validate(LaunchCheck check)
    {
        var blockers = new List<LaunchBlocker>();

        if (!check.Locked)
            blockers.Add(new(LaunchBlockerKind.RoomNotLocked));

        if (!string.IsNullOrEmpty(check.TeamMappingsError))
            blockers.Add(new(LaunchBlockerKind.TeamMappings, Message: check.TeamMappingsError));

        // Human players can't share a colour (Random excepted)
        var chosenColors = check.Players.Where(p => p.ColorId > 0).Select(p => p.ColorId).ToList();
        if (chosenColors.Count != chosenColors.Distinct().Count())
            blockers.Add(new(LaunchBlockerKind.SharedColors));

        if (check.AIPlayers.Any(ai => ai.SideId == check.SpectatorSide))
            blockers.Add(new(LaunchBlockerKind.AiSpectators));

        if (check.EnforceMaxPlayers && HasSharedStartingLocation(check.Players, check.AIPlayers))
            blockers.Add(new(LaunchBlockerKind.SharedStartingLocation));

        int totalPlayerCount = check.Players.Count(p => p.SideId < check.SpectatorSide) + check.AIPlayers.Count;

        if (check.EnforceMinPlayers && totalPlayerCount < check.MinPlayers)
            blockers.Add(new(LaunchBlockerKind.InsufficientPlayers));

        if (check.EnforceMaxPlayers && totalPlayerCount > check.MaxPlayers)
            blockers.Add(new(LaunchBlockerKind.TooManyPlayers));

        for (int i = 0; i < check.Players.Count; i++)
        {
            PlayerInfo player = check.Players[i];

            if (player.Name == check.LocalPlayerName)
                continue;

            if (!player.HashReceived)
                blockers.Add(new(LaunchBlockerKind.NotVerified, i));

            if (player.IsInGame)
                blockers.Add(new(LaunchBlockerKind.StillInGame, i));

            if (!player.Ready)
                blockers.Add(new(LaunchBlockerKind.NotReady));
        }

        return blockers;
    }

    private static bool HasSharedStartingLocation(IReadOnlyList<PlayerInfo> players, IReadOnlyList<PlayerInfo> aiPlayers)
    {
        var all = players.Concat(aiPlayers).ToList();

        // A human player on a start that anyone else (by name) also has
        foreach (PlayerInfo pInfo in players)
        {
            if (pInfo.StartingLocation != 0 &&
                all.Any(p => p.StartingLocation == pInfo.StartingLocation && p.Name != pInfo.Name))
            {
                return true;
            }
        }

        // Two AI players on the same start (AI players can share a name)
        for (int aiId = 0; aiId < aiPlayers.Count; aiId++)
        {
            int startingLocation = aiPlayers[aiId].StartingLocation;
            if (startingLocation == 0)
                continue;

            int index = aiPlayers.ToList().FindIndex(aip => aip.StartingLocation == startingLocation);
            if (index > -1 && index != aiId)
                return true;
        }

        return false;
    }
}
