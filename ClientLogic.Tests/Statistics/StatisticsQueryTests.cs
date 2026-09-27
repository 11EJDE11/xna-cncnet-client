using System.Collections.Generic;

using ClientCore.Statistics;

using ClientLogic.Statistics;

using Xunit;

namespace ClientLogic.Tests.Statistics;

public class StatisticsQueryTests
{
    public StatisticsQueryTests() => TestGame.EnsureInitialized();

    private static MatchStatistics Match(string gameMode, params PlayerStatistics[] players)
    {
        var ms = new MatchStatistics { GameMode = gameMode, SawCompletion = true, LengthInSeconds = 600 };
        foreach (PlayerStatistics ps in players)
            ms.AddPlayer(ps);

        return ms;
    }

    private static PlayerStatistics Human(string name, int team, bool local = false, bool spectator = false) =>
        new(name, local, false, spectator, 1, team, 0, 10);

    private static PlayerStatistics Ai(int team, int level = 1) => new("Computer", false, true, false, 2, team, 0, level);

    private static readonly List<MatchStatistics> matches =
    [
        // 0: skirmish
        Match("Battle", Human("Me", 0, local: true), Ai(0)),
        // 1: PvP
        Match("Battle", Human("Me", 1, local: true), Human("You", 2)),
        // 2: co-op
        Match("Unholy Alliance", Human("Me", 1, local: true), Human("You", 1), Ai(2)),
        // 3: spectated PvP
        Match("Battle", Human("Me", 0, local: true, spectator: true), Human("A", 0), Human("B", 0)),
    ];

    [Fact]
    public void Game_classes_follow_the_xna_window()
    {
        Assert.Equal([3, 2, 1, 0], StatisticsQuery.ListGames(matches, StatisticsGameClass.All, null, true));
        Assert.Equal([3, 2, 1], StatisticsQuery.ListGames(matches, StatisticsGameClass.Online, null, true));
        Assert.Equal([3, 1], StatisticsQuery.ListGames(matches, StatisticsGameClass.PvP, null, true));
        Assert.Equal([2], StatisticsQuery.ListGames(matches, StatisticsGameClass.CoOp, null, true));
        Assert.Equal([0], StatisticsQuery.ListGames(matches, StatisticsGameClass.Skirmish, null, true));
    }

    [Fact]
    public void Game_mode_and_spectated_filters()
    {
        Assert.Equal([2], StatisticsQuery.ListGames(matches, StatisticsGameClass.All, "Unholy Alliance", true));
        Assert.Equal([2, 1, 0], StatisticsQuery.ListGames(matches, StatisticsGameClass.All, null, false));
        Assert.Equal(["Battle", "Unholy Alliance"], StatisticsQuery.GameModes(matches));
    }

    [Fact]
    public void Totals_count_the_local_players_results()
    {
        matches[0].Players[0].Won = true;
        matches[0].Players[0].Kills = 10;
        matches[0].Players[0].Losses = 4;
        matches[1].Players[0].Kills = 2;

        StatisticsTotals totals = StatisticsQuery.Totals(matches, [2, 1, 0], sideCount: 2);

        Assert.Equal("3", totals.GamesStarted);
        Assert.Equal("1", totals.Wins);
        Assert.Equal("2", totals.Losses);
        Assert.Equal("12", totals.TotalKills);
        Assert.Equal("4", totals.KillsPerGame);
        Assert.Equal("3", totals.KillLossRatio);
        Assert.Equal("00:30:00", totals.TotalTimePlayed);
        Assert.Equal("00:10:00", totals.AverageGameLength);
        Assert.Equal(0, totals.FavouriteSideIndex);
        Assert.Equal("Medium", totals.AverageAILevel);
    }

    [Fact]
    public void Totals_without_games_show_dashes()
    {
        StatisticsTotals totals = StatisticsQuery.Totals(matches, [], sideCount: 2);

        Assert.Equal("0", totals.GamesStarted);
        Assert.Equal("-", totals.WinLossRatio);
        Assert.Equal("-", totals.AverageGameLength);
        Assert.Equal("-", totals.KillsPerGame);
    }

    [Fact]
    public void Players_by_score_and_team_letters()
    {
        MatchStatistics ms = Match("Battle", Human("Low", 0), Human("High", 0));
        ms.Players[0].Score = 5;
        ms.Players[1].Score = 50;

        Assert.Equal("High", StatisticsQuery.PlayersByScore(ms)[0].Name);
        Assert.Equal("-", StatisticsQuery.TeamIndexToString(0));
        Assert.Equal("A", StatisticsQuery.TeamIndexToString(1));
    }
}
