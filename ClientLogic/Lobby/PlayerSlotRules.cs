using System.Collections.Generic;
using System.Linq;

using DTAClient.Domain.Multiplayer;

namespace ClientLogic.Lobby;

/// <summary>
/// Which side items a group of players (humans or AIs) may pick, and the side that replaces one they may not.
/// </summary>
/// <param name="Selectable">Per side index (see <see cref="SlotIndexMapper"/>), including Spectator.</param>
/// <param name="DefaultSide">The replacement side: the only allowed side, or Random.</param>
public sealed record SideAvailability(IReadOnlyList<bool> Selectable, int DefaultSide);

/// <summary>What a map or game mode decides about the player slots.</summary>
public sealed record MapSlotRules(
    bool HumanPlayersOnly,
    IReadOnlyCollection<int> AllowedStartingLocations,
    int MaxPlayers,
    bool ForceRandomStartLocations,
    bool ForceNoTeams,
    bool IsCoop,
    IReadOnlyList<int> CoopDisallowedColors)
{
    /// <summary>True when the map has co-op information (disallowed colours, forced team).</summary>
    public bool HasCoopInfo => CoopDisallowedColors != null;

    public static MapSlotRules FromGameModeMap(GameModeMap gameModeMap) => new(
        gameModeMap.HumanPlayersOnly,
        gameModeMap.AllowedStartingLocations,
        gameModeMap.MaxPlayers,
        gameModeMap.ForceRandomStartLocations,
        gameModeMap.ForceNoTeams,
        gameModeMap.IsCoop,
        gameModeMap.CoopInfo?.DisallowedPlayerColors);
}

/// <summary>Which of a player row's drop-downs the local player may use.</summary>
/// <param name="TeamAndStartKnown">
/// False when there is no map: the team and start drop-downs then keep their current state.
/// </param>
public sealed record SlotControls(bool Name, bool Side, bool Color, bool Start, bool Team, bool TeamAndStartKnown);

/// <summary>
/// The rules for player slots as pure functions: what can be picked, which controls are enabled, and the
/// corrections a map or the side restrictions make to the players.
/// </summary>
public static class PlayerSlotRules
{
    /// <summary>
    /// Computes the side items a group may pick. Random selectors are unavailable when only one side is allowed;
    /// a custom selector is unavailable when at most one of its sides is allowed; Spectator is unavailable on co-op
    /// maps.
    /// </summary>
    /// <param name="disallowedSides">Per real side, whether the group may not use it.</param>
    /// <param name="randomSelectors">The real sides of each custom random selector.</param>
    public static SideAvailability ComputeSideAvailability(bool[] disallowedSides, IReadOnlyList<int[]> randomSelectors,
        SlotIndexMapper map, bool hasCoopInfo)
    {
        var selectable = new bool[map.SideItemCount];
        int defaultSide = SlotIndexMapper.RandomSide;
        int allowedSideCount = disallowedSides.Count(disallowed => !disallowed);

        if (allowedSideCount == 1)
        {
            for (int i = 0; i < disallowedSides.Length; i++)
            {
                if (!disallowedSides[i])
                    defaultSide = map.RealSide(i);
            }
        }

        for (int i = 0; i < map.RandomSelectorCount; i++)
            selectable[i] = allowedSideCount != 1;

        for (int c = 0; c < randomSelectors.Count; c++)
        {
            int disallowedCount = randomSelectors[c].Count(side => disallowedSides[side]);
            selectable[SlotIndexMapper.RandomSelectorSide(c)] = disallowedCount < randomSelectors[c].Length - 1;
        }

        for (int i = 0; i < disallowedSides.Length; i++)
            selectable[map.RealSide(i)] = !disallowedSides[i];

        selectable[map.SpectatorSide] = !hasCoopInfo;

        return new SideAvailability(selectable, defaultSide);
    }

    /// <summary>Moves players whose side is not available to the default side.</summary>
    public static void NormaliseSides(IEnumerable<PlayerInfo> players, SideAvailability availability)
    {
        foreach (PlayerInfo pInfo in players)
        {
            if (pInfo.SideId >= 0 && pInfo.SideId < availability.Selectable.Count && !availability.Selectable[pInfo.SideId])
                pInfo.SideId = availability.DefaultSide;
        }
    }

    /// <summary>
    /// The start location items after "???": one per location up to the highest one the drop-down shows, and
    /// whether each can be picked.
    /// </summary>
    public static IReadOnlyList<bool> ComputeStartItems(MapSlotRules map, int maxPlayerCount)
    {
        int maxLocation = map.MaxPlayers == 0 ? 0
            : (map.AllowedStartingLocations.Max() == map.MaxPlayers ? map.MaxPlayers : maxPlayerCount);

        return Enumerable.Range(1, maxLocation).Select(map.AllowedStartingLocations.Contains).ToList();
    }

    /// <summary>Whether each colour item (0 = Random) can be picked.</summary>
    public static bool[] ComputeColorSelectable(int colorItemCount, MapSlotRules map, int mpColorCount) =>
        ComputeColorSelectable(colorItemCount, map.CoopDisallowedColors, mpColorCount);

    /// <summary>Whether each colour item (0 = Random) can be picked.</summary>
    /// <param name="coopDisallowedColors">The co-op map's disallowed colours, or null.</param>
    public static bool[] ComputeColorSelectable(int colorItemCount, IReadOnlyList<int> coopDisallowedColors, int mpColorCount)
    {
        var selectable = Enumerable.Repeat(true, colorItemCount).ToArray();

        if (coopDisallowedColors != null)
        {
            foreach (int disallowedColorIndex in coopDisallowedColors)
            {
                if (disallowedColorIndex < mpColorCount)
                    selectable[SlotIndexMapper.ColorId(disallowedColorIndex)] = false;
            }
        }

        return selectable;
    }

    /// <summary>The highest team index (teams are 1-4, 0 = none).</summary>
    public const int MaxTeam = 4;

    /// <summary>
    /// Whether the game host accepts a player's request to change their own side, colour, start and team.
    /// Real sides, Spectator and colours must be selectable as in the player's own drop-downs. Random and the
    /// random selectors are accepted even when not selectable: the host's side check moves them to the default
    /// side, as it does for everyone.
    /// </summary>
    /// <param name="humanSides">The side availability for human players.</param>
    /// <param name="colorSelectable">From <see cref="ComputeColorSelectable(int, IReadOnlyList{int}, int)"/>.</param>
    /// <param name="isStartAllowed">Whether a start location other than random is allowed.</param>
    public static bool IsOptionsRequestAllowed(int side, int color, int start, int team, SideAvailability humanSides,
        SlotIndexMapper map, bool[] colorSelectable, System.Func<int, bool> isStartAllowed)
    {
        if (side < 0 || side >= humanSides.Selectable.Count)
            return false;

        if (side >= map.RandomSelectorCount && !humanSides.Selectable[side])
            return false;

        if (color < 0 || color >= colorSelectable.Length || !colorSelectable[color])
            return false;

        if (start != SlotIndexMapper.RandomStart && !isStartAllowed(start))
            return false;

        return team >= 0 && team <= MaxTeam;
    }

    /// <summary>
    /// The corrections a new map makes: no AI players on human-only maps, starts the map doesn't allow (or all
    /// starts, if it forces random ones) become random, forced "no teams", and on co-op maps the disallowed colours
    /// become random and everyone joins team 1.
    /// </summary>
    public static void NormaliseForMap(List<PlayerInfo> players, List<PlayerInfo> aiPlayers, MapSlotRules map, int mpColorCount)
    {
        if (map.HumanPlayersOnly)
            aiPlayers.Clear();

        List<PlayerInfo> allPlayers = players.Concat(aiPlayers).ToList();

        foreach (PlayerInfo pInfo in allPlayers)
        {
            if (!map.AllowedStartingLocations.Contains(pInfo.StartingLocation) || map.ForceRandomStartLocations)
                pInfo.StartingLocation = SlotIndexMapper.RandomStart;

            if (!map.IsCoop && map.ForceNoTeams)
                pInfo.TeamId = 0;
        }

        if (!map.HasCoopInfo)
            return;

        foreach (int disallowedColorIndex in map.CoopDisallowedColors)
        {
            if (disallowedColorIndex >= mpColorCount)
                continue;

            foreach (PlayerInfo pInfo in allPlayers)
            {
                if (pInfo.ColorId == SlotIndexMapper.ColorId(disallowedColorIndex))
                    pInfo.ColorId = SlotIndexMapper.RandomColor;
            }
        }

        foreach (PlayerInfo pInfo in allPlayers)
            pInfo.TeamId = 1;
    }

    /// <summary>
    /// Which drop-downs of a player row are enabled.
    /// </summary>
    /// <param name="allowOptionsChange">Whether the local player may change other players' options (the host).</param>
    /// <param name="isLocalPlayer">The row is the local player.</param>
    /// <param name="hasMap">False when there is no map: team and start then keep their current state.</param>
    /// <param name="mapForcesRandomStarts">The map or game mode forces random start locations.</param>
    /// <param name="mapForbidsTeams">The map or game mode is co-op or forces "no teams".</param>
    public static SlotControls ComputeControls(bool isAi, bool allowOptionsChange, bool isLocalPlayer,
        PlayerExtraOptions extraOptions, bool hasMap, bool mapForcesRandomStarts, bool mapForbidsTeams)
    {
        bool canChange = isAi ? allowOptionsChange : allowOptionsChange || isLocalPlayer;

        return new SlotControls(
            Name: isAi && allowOptionsChange,
            Side: !extraOptions.IsForceRandomSides && canChange,
            Color: !extraOptions.IsForceRandomColors && canChange,
            Start: hasMap && !extraOptions.IsForceRandomStarts && canChange && !mapForcesRandomStarts,
            Team: hasMap && !extraOptions.IsForceNoTeams && canChange && !mapForbidsTeams,
            TeamAndStartKnown: hasMap);
    }
}
