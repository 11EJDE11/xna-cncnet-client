using System;
using System.Collections.Generic;
using System.Linq;

using ClientLogic.Lobby;

using DTAClient.Domain.Multiplayer;

using Xunit;

namespace ClientLogic.Tests.Lobby;

public class LobbyRulesTests
{
    private const int Spectator = 12;

    private static PlayerInfo Human(string name, int color = 0, int start = 0, bool ready = true) =>
        new(name, 0, start, color, 0) { Ready = ready, HashReceived = true };

    private static LaunchCheck Check(List<PlayerInfo> players, List<PlayerInfo> ais = null, bool locked = true,
        string teamMappingsError = null, bool enforceMin = false, int min = 0, bool enforceMax = false, int max = 8) =>
        new(locked, teamMappingsError, players, ais ?? [], "Host", Spectator, enforceMin, min, enforceMax, max);

    private static List<LaunchBlockerKind> Kinds(LaunchCheck check) =>
        LaunchValidation.Validate(check).Select(b => b.Kind).ToList();

    [Fact]
    public void ALockedReadyRoomCanLaunch()
    {
        TestGame.EnsureInitialized();
        Assert.Empty(LaunchValidation.Validate(Check([Human("Host", ready: false), Human("B", 1)])));
    }

    [Fact]
    public void BlockersComeInTheLobbysOrder()
    {
        TestGame.EnsureInitialized();
        var players = new List<PlayerInfo>
        {
            Human("Host", 2, start: 1),
            Human("B", 2, start: 1, ready: false),
            new("C", 0, 0, 0, 0) { HashReceived = false, IsInGame = true },
        };
        var ais = new List<PlayerInfo> { new("AI", Spectator, 0, 0, 0) { IsAI = true } };

        List<LaunchBlockerKind> kinds = Kinds(Check(players, ais, locked: false, teamMappingsError: "mappings",
            enforceMin: true, min: 9, enforceMax: true, max: 2));

        Assert.Equal(
        [
            LaunchBlockerKind.RoomNotLocked, LaunchBlockerKind.TeamMappings, LaunchBlockerKind.SharedColors,
            LaunchBlockerKind.AiSpectators, LaunchBlockerKind.SharedStartingLocation, LaunchBlockerKind.InsufficientPlayers,
            LaunchBlockerKind.TooManyPlayers, LaunchBlockerKind.NotReady, LaunchBlockerKind.NotVerified,
            LaunchBlockerKind.StillInGame, LaunchBlockerKind.NotReady,
        ], kinds);

        Assert.Equal(2, LaunchValidation.Validate(Check(players)).First(b => b.Kind == LaunchBlockerKind.NotVerified).PlayerIndex);
    }

    [Fact]
    public void RandomColoursAndStartsCanBeShared()
    {
        TestGame.EnsureInitialized();
        var players = new List<PlayerInfo> { Human("Host"), Human("B") };
        var ais = new List<PlayerInfo> { new("AI", 0, 0, 0, 0), new("AI", 0, 0, 0, 0) };

        Assert.Empty(LaunchValidation.Validate(Check(players, ais, enforceMax: true)));

        // Two AIs with the same name on the same start
        ais[0].StartingLocation = ais[1].StartingLocation = 3;
        Assert.Equal([LaunchBlockerKind.SharedStartingLocation], Kinds(Check(players, ais, enforceMax: true)));
    }

    [Theory]
    [InlineData("", 1, 6)]
    [InlineData("3d20", 3, 20)]
    [InlineData("10d100", 10, 100)]
    [InlineData("junk", 1, 6)]   // no "d": the default die
    public void DiceSpecsParse(string spec, int count, int sides)
    {
        TestGame.EnsureInitialized();
        Assert.True(DiceRoll.TryParse(spec, out int c, out int s, out string error));
        Assert.Equal((count, sides, null), (c, s, error));
    }

    [Theory]
    [InlineData("xd6")]
    [InlineData("11d6")]
    [InlineData("0d6")]
    [InlineData("2d1")]
    [InlineData("2d101")]
    public void BadDiceSpecsGiveANotice(string spec)
    {
        TestGame.EnsureInitialized();
        Assert.False(DiceRoll.TryParse(spec, out _, out _, out string error));
        Assert.False(string.IsNullOrEmpty(error));
    }

    [Fact]
    public void DiceResultsRoundTripAndBadOnesAreIgnored()
    {
        int[] rolled = DiceRoll.Roll(3, 6, new Random(5));
        Assert.All(rolled, r => Assert.InRange(r, 1, 6));

        Assert.True(DiceRoll.TryParseResult("6," + string.Join(",", rolled), out int sides, out int[] results));
        Assert.Equal(6, sides);
        Assert.Equal(rolled, results);

        Assert.False(DiceRoll.TryParseResult("6,7", out _, out _));
        Assert.False(DiceRoll.TryParseResult("6", out _, out _));
        Assert.False(DiceRoll.TryParseResult("101,1", out _, out _));
        Assert.False(DiceRoll.TryParseResult("6,1,1,1,1,1,1,1,1,1,1,1", out _, out _));
    }

    [Fact]
    public void ChatCommandsRunByNameAndCheckTheHost()
    {
        TestGame.EnsureInitialized();
        var ran = new List<string>();
        var commands = new List<ChatBoxCommand>
        {
            new("ROLL", "Roll", false, p => ran.Add("roll " + p)),
            new("MAXAHEAD", "MaxAhead", true, p => ran.Add("maxahead " + p)),
        };

        Assert.Equal(ChatCommandOutcome.Executed, ChatBoxCommands.Execute("/roll 3d6", commands, isHost: false).Outcome);
        Assert.Equal(ChatCommandOutcome.Executed, ChatBoxCommands.Execute("/Roll", commands, false).Outcome);
        Assert.Equal(ChatCommandOutcome.HostOnly, ChatBoxCommands.Execute("/maxahead 2", commands, false).Outcome);
        Assert.Equal(ChatCommandOutcome.Executed, ChatBoxCommands.Execute("/maxahead 2", commands, true).Outcome);
        Assert.Equal(ChatCommandOutcome.Unknown, ChatBoxCommands.Execute("/nope", commands, true).Outcome);

        Assert.Equal(["roll 3d6", "roll ", "maxahead 2"], ran);
        Assert.Contains("MAXAHEAD: MaxAhead", ChatBoxCommands.HelpText(commands));
    }

    [Theory]
    [InlineData(false, 8, 8, LockButtonAction.Lock)]
    [InlineData(true, 3, 8, LockButtonAction.Unlock)]
    [InlineData(true, 8, 8, LockButtonAction.RefuseUnlock)]
    public void TheLockButtonRespectsThePlayerLimit(bool locked, int players, int limit, LockButtonAction expected) =>
        Assert.Equal(expected, RoomLock.OnLockButton(locked, players, limit));
}
