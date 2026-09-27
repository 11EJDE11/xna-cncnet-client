using System.Collections.Generic;

using ClientCore;
using ClientCore.Statistics;

using ClientLogic.Statistics;

using DTAClient.Domain.Multiplayer;

using Xunit;

namespace ClientLogic.Tests.Statistics;

public class LobbyStatisticsTests
{
    private const int SPECTATOR_SIDE = 5;

    private static readonly GameModeMap fourBattle = TestGame.LoadGameModeMap("Maps/Test/four", "Battle");

    private static PlayerInfo Local(int teamId = 0, int sideId = 1) => new(ProgramConstants.PLAYERNAME, sideId, 0, 0, teamId);

    private static PlayerInfo Ai(int level, int teamId = 0) => new("Computer", 1, 0, 0, teamId) { AILevel = level, IsAI = true };

    private static int SkirmishRank(List<PlayerInfo> ais, PlayerInfo local = null, bool allowScoring = true) =>
        LobbyStatistics.GetRank(false, fourBattle, allowScoring, [local ?? Local()], ais, ProgramConstants.PLAYERNAME, SPECTATOR_SIDE);

    [Fact]
    public void Skirmish_against_a_full_map_earns_the_weakest_enemy_level_plus_one()
    {
        Assert.Equal(2, SkirmishRank([Ai(1), Ai(2), Ai(2)]));
        Assert.Equal(1, SkirmishRank([Ai(0), Ai(2), Ai(2)]));
    }

    [Fact]
    public void Skirmish_without_a_full_map_or_with_scoring_denied_earns_nothing()
    {
        Assert.Equal(LobbyStatistics.RANK_NONE, SkirmishRank([Ai(2), Ai(2)]));
        Assert.Equal(LobbyStatistics.RANK_NONE, SkirmishRank([Ai(2), Ai(2), Ai(2)], allowScoring: false));
        Assert.Equal(LobbyStatistics.RANK_NONE, SkirmishRank([Ai(2), Ai(2), Ai(2)], Local(sideId: SPECTATOR_SIDE)));
    }

    [Fact]
    public void Skirmish_team_rules_follow_the_xna_lobby()
    {
        // An ally stronger than the weakest enemy denies the rank
        Assert.Equal(LobbyStatistics.RANK_NONE, SkirmishRank([Ai(2, teamId: 1), Ai(1, teamId: 2), Ai(1, teamId: 2)], Local(teamId: 1)));

        // 2 vs 2 with an equal ally
        Assert.Equal(2, SkirmishRank([Ai(1, teamId: 1), Ai(1, teamId: 2), Ai(1, teamId: 2)], Local(teamId: 1)));

        // 2 vs 1: the enemy team is smaller
        Assert.Equal(LobbyStatistics.RANK_NONE, SkirmishRank([Ai(0, teamId: 1), Ai(0, teamId: 1), Ai(1, teamId: 2)], Local(teamId: 1)));
    }

    [Fact]
    public void Multiplayer_coop_against_ai_earns_the_weakest_enemy_level_plus_one()
    {
        List<PlayerInfo> players = [Local(teamId: 1), new PlayerInfo("Friend", 1, 0, 0, 1)];

        int rank = LobbyStatistics.GetRank(true, fourBattle, true, players, [Ai(2, teamId: 2), Ai(1, teamId: 2)],
            ProgramConstants.PLAYERNAME, SPECTATOR_SIDE);

        Assert.Equal(2, rank);

        // A human on another team makes it PvP: no co-op rank
        players[1].TeamId = 2;
        Assert.Equal(LobbyStatistics.RANK_NONE, LobbyStatistics.GetRank(true, fourBattle, true, players, [Ai(2, teamId: 2)],
            ProgramConstants.PLAYERNAME, SPECTATOR_SIDE));
    }

    [Fact]
    public void No_map_earns_nothing() =>
        Assert.Equal(LobbyStatistics.RANK_NONE, LobbyStatistics.GetRank(false, null, true, [Local()], [], ProgramConstants.PLAYERNAME, SPECTATOR_SIDE));

    [Fact]
    public void Match_statistics_record_the_players_then_the_ai_with_their_launch_houses()
    {
        List<MultiplayerColor> colors = MultiplayerColor.LoadColors();
        PlayerInfo spectator = new("Watcher", SPECTATOR_SIDE, 0, 0, 0);
        PlayerInfo ai = Ai(1, teamId: 2);
        PlayerHouseInfo[] houses =
        [
            new() { SideIndex = 0, ColorIndex = colors[1].GameColorIndex },
            new() { SideIndex = 2, ColorIndex = colors[0].GameColorIndex },
            new() { SideIndex = 1, ColorIndex = -5 },
        ];

        MatchStatistics ms = LobbyStatistics.CreateMatchStatistics(1234, fourBattle, allowScoring: false,
            [Local(teamId: 1), spectator], [ai], houses, colors, SPECTATOR_SIDE, ProgramConstants.PLAYERNAME);

        Assert.Equal(1234, ms.GameID);
        Assert.Equal(fourBattle.Map.UntranslatedName, ms.MapName);
        Assert.Equal(fourBattle.GameMode.UntranslatedUIName, ms.GameMode);
        Assert.Equal(2, ms.NumberOfHumanPlayers);
        Assert.False(ms.IsValidForStar);
        Assert.Equal(3, ms.GetPlayerCount());

        PlayerStatistics local = ms.GetPlayer(0);
        Assert.True(local.IsLocalPlayer);
        Assert.False(local.WasSpectator);
        Assert.Equal(1, local.Side);
        Assert.Equal(1, local.Team);
        Assert.Equal(1, local.Color);
        Assert.Equal(10, local.AILevel);

        Assert.True(ms.GetPlayer(1).WasSpectator);
        Assert.Equal(0, ms.GetPlayer(1).Color);

        PlayerStatistics computer = ms.GetPlayer(2);
        Assert.Equal("Computer", computer.Name);
        Assert.True(computer.IsAI);
        Assert.Equal(2, computer.Side);
        Assert.Equal(2, computer.Team);
        Assert.Equal(-1, computer.Color);
        Assert.Equal(1, computer.AILevel);
    }
}
