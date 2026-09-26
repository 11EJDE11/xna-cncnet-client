using System.Collections.Generic;
using System.Linq;

using ClientCore;

using ClientLogic.Lobby;

using DTAClient.Domain.Multiplayer;

using Xunit;

namespace ClientLogic.Tests.Lobby;

public class PlayerSlotsStateTests
{
    // 4 real sides, one custom selector: 0 = Random, 1 = selector, 2-5 sides, 6 = Spectator
    private static readonly SlotIndexMapper Map = new(sideCount: 4, randomSelectorCount: 2);

    private static PlayerSlotsState Create(int humans = 2, int ais = 1)
    {
        TestGame.EnsureInitialized();
        var players = Enumerable.Range(0, humans).Select(i => new PlayerInfo("P" + i, 2, i + 1, 1, 1)).ToList();
        var aiPlayers = Enumerable.Range(0, ais).Select(i => new PlayerInfo(ProgramConstants.AI_PLAYER_NAMES[0], 3, 0, 2, 2) { IsAI = true }).ToList();
        return new PlayerSlotsState(players, aiPlayers);
    }

    [Fact]
    public void ColourChangesDontClearReadyStatus()
    {
        PlayerSlotsState slots = Create();

        Assert.False(slots.ApplyChange(0, SlotField.Color, 3, Map, isCoop: false).ClearsReady);
        Assert.True(slots.ApplyChange(0, SlotField.Side, 3, Map, false).ClearsReady);
        Assert.True(slots.ApplyChange(0, SlotField.Start, 2, Map, false).ClearsReady);
        Assert.True(slots.ApplyChange(0, SlotField.Team, 2, Map, false).ClearsReady);
        Assert.Equal((3, 3, 2, 2), (slots.Players[0].ColorId, slots.Players[0].SideId, slots.Players[0].StartingLocation, slots.Players[0].TeamId));
    }

    [Fact]
    public void NameItemsOnHumanRowsAreCommands()
    {
        PlayerSlotsState slots = Create();

        Assert.Equal(SlotCommand.Kick, slots.ApplyChange(1, SlotField.Name, SlotIndexMapper.KickNameItem, Map, false).Command);
        Assert.Equal(SlotCommand.Ban, slots.ApplyChange(1, SlotField.Name, SlotIndexMapper.BanNameItem, Map, false).Command);
        Assert.Equal(SlotCommand.None, slots.ApplyChange(1, SlotField.Name, 1, Map, false).Command);
        Assert.Equal(2, slots.Players.Count);
    }

    [Fact]
    public void SpectatorsLoseTheirStart()
    {
        PlayerSlotsState slots = Create();

        slots.ApplyChange(1, SlotField.Side, Map.SpectatorSide, Map, false);

        Assert.Equal(0, slots.Players[1].StartingLocation);
        Assert.Equal(1, slots.Players[0].StartingLocation);
    }

    [Fact]
    public void AiPlayersAreAddedChangedAndRemovedByRow()
    {
        PlayerSlotsState slots = Create(humans: 2, ais: 1);

        // Row 3 is the first free row: picking an AI level adds an AI with random options
        slots.ApplyChange(3, SlotField.Name, SlotIndexMapper.AiLevelToNameItem(2), Map, isCoop: false);
        PlayerInfo added = slots.AIPlayers[1];
        Assert.Equal((ProgramConstants.AI_PLAYER_NAMES[2], 2, true), (added.Name, added.AILevel, added.IsAI));
        Assert.Equal((0, 0, 0, 0), (added.SideId, added.ColorId, added.StartingLocation, added.TeamId));

        // Rows further down are not free rows yet
        slots.ApplyChange(5, SlotField.Name, 1, Map, false);
        Assert.Equal(2, slots.AIPlayers.Count);

        slots.ApplyChange(3, SlotField.Side, 4, Map, false);
        slots.ApplyChange(3, SlotField.Start, -1, Map, false);
        Assert.Equal((4, 0), (added.SideId, added.StartingLocation));

        // "-" removes the AI; the next one moves up a row
        slots.ApplyChange(2, SlotField.Name, 0, Map, false);
        Assert.Same(added, Assert.Single(slots.AIPlayers));
    }

    [Fact]
    public void AiPlayersAreOnTeamOneOnCoopMaps()
    {
        PlayerSlotsState slots = Create(ais: 2);

        slots.ApplyChange(0, SlotField.Color, 4, Map, isCoop: true);

        Assert.All(slots.AIPlayers, ai => Assert.Equal(1, ai.TeamId));
    }

    [Fact]
    public void OptionRequestsClearReadyUnlessOnlyTheColourChanges()
    {
        PlayerSlotsState slots = Create();
        PlayerInfo p = slots.Players[1];

        Assert.False(slots.ApplyOptionsRequest(p, p.SideId, 5, p.StartingLocation, p.TeamId).ClearsReady);
        Assert.True(slots.ApplyOptionsRequest(p, p.SideId, 5, p.StartingLocation, 3).ClearsReady);
        Assert.Equal((5, 3), (p.ColorId, p.TeamId));
    }

    [Fact]
    public void MapPreviewAssignsAndClearsStarts()
    {
        PlayerSlotsState slots = Create(humans: 2, ais: 1);

        // Player 0 is on start 1; assigning the AI (row 2) there moves player 0 off when only one player fits
        Assert.True(slots.AssignStart(2, 1, onePlayerPerStart: true));
        Assert.Equal((0, 1), (slots.Players[0].StartingLocation, slots.AIPlayers[0].StartingLocation));

        Assert.True(slots.AssignStart(0, 1, onePlayerPerStart: false));
        Assert.Equal(1, slots.Players[0].StartingLocation);

        Assert.False(slots.AssignStart(3, 1, true));
        Assert.Equal(1, slots.Players[0].StartingLocation);

        slots.ClearStart(1);
        Assert.All(slots.AllPlayers.Take(1).Concat(slots.AIPlayers), p => Assert.Equal(0, p.StartingLocation));
        Assert.Equal(2, slots.Players[1].StartingLocation);
    }
}
