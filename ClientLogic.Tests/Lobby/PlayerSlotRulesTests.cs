using System;
using System.Collections.Generic;
using System.Linq;

using ClientLogic.Lobby;

using DTAClient.Domain.Multiplayer;

using Xunit;

namespace ClientLogic.Tests.Lobby;

public class PlayerSlotRulesTests
{
    // 4 real sides; custom random selectors {0,1} and {2,3}: side ids 0 = Random, 1-2 selectors, 3-6 sides, 7 = Spectator
    private static readonly SlotIndexMapper Map = new(sideCount: 4, randomSelectorCount: 3);
    private static readonly List<int[]> Selectors = [[0, 1], [2, 3]];

    private static PlayerInfo Player(int side = 0, int color = 0, int start = 0, int team = 0) =>
        new("P", side, start, color, team);

    [Fact]
    public void IndicesFollowTheDropDownLayout()
    {
        Assert.Equal(7, Map.SpectatorSide);
        Assert.Equal(8, Map.SideItemCount);
        Assert.Equal(3, Map.RealSide(0));
        Assert.Equal(2, SlotIndexMapper.RandomSelectorSide(1));
        Assert.Equal(1, SlotIndexMapper.ColorId(0));
        Assert.Equal(3, SlotIndexMapper.AiLevelToNameItem(2));
    }

    [Fact]
    public void DisallowedSidesAndSelectorsAreNotSelectable()
    {
        SideAvailability availability = PlayerSlotRules.ComputeSideAvailability(
            [true, false, false, false], Selectors, Map, hasCoopInfo: false);

        // Selector {0,1} has only one allowed side left
        Assert.Equal([true, false, true, false, true, true, true, true], availability.Selectable);
        Assert.Equal(SlotIndexMapper.RandomSide, availability.DefaultSide);

        var players = new[] { Player(side: 1), Player(side: 3), Player(side: 4) };
        PlayerSlotRules.NormaliseSides(players, availability);
        Assert.Equal([0, 0, 4], players.Select(p => p.SideId));
    }

    [Fact]
    public void OneAllowedSideReplacesRandomAndCoopRemovesSpectator()
    {
        SideAvailability availability = PlayerSlotRules.ComputeSideAvailability(
            [true, true, false, true], Selectors, Map, hasCoopInfo: true);

        Assert.Equal(5, availability.DefaultSide);
        Assert.Equal([false, false, false, false, false, true, false, false], availability.Selectable);

        var players = new[] { Player(side: 0), Player(side: 2), Player(side: 7) };
        PlayerSlotRules.NormaliseSides(players, availability);
        Assert.All(players, p => Assert.Equal(5, p.SideId));
    }

    [Fact]
    public void SideRulesMatchTheOldLobbyCode()
    {
        var random = new Random(1234);
        for (int run = 0; run < 2000; run++)
        {
            bool[] disallowed = Enumerable.Range(0, 4).Select(_ => random.Next(3) == 0).ToArray();
            bool coop = random.Next(2) == 0;
            int[] sides = Enumerable.Range(0, 4).Select(_ => random.Next(-1, 8)).ToArray();

            var expectedPlayers = sides.Select(s => Player(side: s)).ToList();
            bool[] expectedSelectable = OldCheckDisallowedSides(disallowed, coop, expectedPlayers);

            var players = sides.Select(s => Player(side: s)).ToList();
            SideAvailability availability = PlayerSlotRules.ComputeSideAvailability(disallowed, Selectors, Map, coop);
            PlayerSlotRules.NormaliseSides(players, availability);

            Assert.Equal(expectedSelectable, availability.Selectable);
            Assert.Equal(expectedPlayers.Select(p => p.SideId), players.Select(p => p.SideId));
        }
    }

    /// <summary>GameLobbyBase.CheckDisallowedSidesForGroup before PR 20, with one side drop-down's items.</summary>
    private static bool[] OldCheckDisallowedSides(bool[] disallowedSideArray, bool coop, List<PlayerInfo> playerInfos)
    {
        const int RandomSelectorCount = 3, SideCount = 4;
        var items = new bool[RandomSelectorCount + SideCount + 1];
        int defaultSide = 0;
        int allowedSideCount = disallowedSideArray.Count(b => b == false);

        if (allowedSideCount == 1)
        {
            for (int i = 0; i < disallowedSideArray.Length; i++)
            {
                if (!disallowedSideArray[i])
                    defaultSide = i + RandomSelectorCount;
            }

            for (int i = 0; i < RandomSelectorCount; i++)
                items[i] = false;
        }
        else
        {
            for (int i = 0; i < RandomSelectorCount; i++)
                items[i] = true;
        }

        int c = 0;
        foreach (int[] randomSides in Selectors)
        {
            int disableCount = randomSides.Count(side => disallowedSideArray[side]);
            bool disabled = disableCount >= randomSides.Length - 1;
            items[1 + c] = !disabled;

            foreach (PlayerInfo pInfo in playerInfos)
            {
                if (pInfo.SideId == 1 + c && disabled)
                    pInfo.SideId = defaultSide;
            }

            c++;
        }

        for (int i = 0; i < disallowedSideArray.Length; i++)
        {
            items[i + RandomSelectorCount] = !disallowedSideArray[i];
            foreach (PlayerInfo pInfo in playerInfos)
            {
                if (disallowedSideArray[i] && pInfo.SideId == i + RandomSelectorCount)
                    pInfo.SideId = defaultSide;
            }
        }

        if (allowedSideCount == 1)
        {
            foreach (PlayerInfo pInfo in playerInfos)
            {
                if (pInfo.SideId == 0)
                    pInfo.SideId = defaultSide;
            }
        }

        if (coop)
        {
            foreach (PlayerInfo pInfo in playerInfos)
            {
                if (pInfo.SideId == SideCount + RandomSelectorCount)
                    pInfo.SideId = defaultSide;
            }
        }

        items[SideCount + RandomSelectorCount] = !coop;
        return items;
    }

    private static MapSlotRules MapRules(int maxPlayers = 4, int[] allowedStarts = null, bool humanOnly = false,
        bool forceRandomStarts = false, bool forceNoTeams = false, bool isCoop = false, int[] coopColors = null) =>
        new(humanOnly, allowedStarts ?? Enumerable.Range(1, maxPlayers).ToArray(), maxPlayers, forceRandomStarts,
            forceNoTeams, isCoop, coopColors);

    [Fact]
    public void StartItemsCoverTheAllowedLocations()
    {
        Assert.Equal([true, true, true, true], PlayerSlotRules.ComputeStartItems(MapRules(), maxPlayerCount: 8));

        // Allowed locations that don't end at MaxPlayers show all eight
        IReadOnlyList<bool> gaps = PlayerSlotRules.ComputeStartItems(MapRules(allowedStarts: [1, 3, 5, 7]), 8);
        Assert.Equal([true, false, true, false, true, false, true, false], gaps);

        Assert.Empty(PlayerSlotRules.ComputeStartItems(MapRules(maxPlayers: 0, allowedStarts: []), 8));
    }

    [Fact]
    public void MapCorrectionsFixStartsTeamsColoursAndAi()
    {
        var players = new List<PlayerInfo> { Player(start: 5, team: 2, color: 2), Player(start: 2, team: 3, color: 4) };
        var ai = new List<PlayerInfo> { Player() };

        PlayerSlotRules.NormaliseForMap(players, ai, MapRules(humanOnly: true, forceNoTeams: true), mpColorCount: 8);

        Assert.Empty(ai);
        Assert.Equal([0, 2], players.Select(p => p.StartingLocation));
        Assert.Equal([0, 0], players.Select(p => p.TeamId));

        PlayerSlotRules.NormaliseForMap(players, ai, MapRules(isCoop: true, coopColors: [1, 20]), mpColorCount: 8);
        Assert.Equal([0, 4], players.Select(p => p.ColorId));
        Assert.Equal([1, 1], players.Select(p => p.TeamId));

        bool[] colors = PlayerSlotRules.ComputeColorSelectable(9, MapRules(coopColors: [1, 20]), mpColorCount: 8);
        Assert.Equal([true, true, false, true, true, true, true, true, true], colors);
    }

    [Fact]
    public void ControlsFollowHostAndExtraOptions()
    {
        var none = new PlayerExtraOptions();

        SlotControls own = PlayerSlotRules.ComputeControls(isAi: false, allowOptionsChange: false, isLocalPlayer: true, none,
            hasMap: true, mapForcesRandomStarts: false, mapForbidsTeams: false);
        Assert.Equal(new SlotControls(false, true, true, true, true, true), own);

        SlotControls other = PlayerSlotRules.ComputeControls(false, false, false, none, true, false, false);
        Assert.Equal(new SlotControls(false, false, false, false, false, true), other);

        var forced = new PlayerExtraOptions { IsForceRandomSides = true, IsForceNoTeams = true };
        SlotControls aiAsHost = PlayerSlotRules.ComputeControls(true, true, false, forced, true, true, false);
        Assert.Equal(new SlotControls(true, false, true, false, false, true), aiAsHost);

        SlotControls noMap = PlayerSlotRules.ComputeControls(false, true, true, none, false, false, false);
        Assert.False(noMap.TeamAndStartKnown);
    }
}
