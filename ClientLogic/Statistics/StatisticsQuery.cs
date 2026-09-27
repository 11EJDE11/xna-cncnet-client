using System;
using System.Collections.Generic;
using System.Linq;

using ClientCore;
using ClientCore.Extensions;
using ClientCore.Statistics;

namespace ClientLogic.Statistics;

/// <summary>The statistics window's game class filter, in its drop-down's order.</summary>
public enum StatisticsGameClass
{
    All,
    Online,
    PvP,
    CoOp,
    Skirmish,
}

/// <summary>The "Total Statistics" tab's values, as the XNA statistics window shows them.</summary>
public sealed record StatisticsTotals(
    string GamesStarted,
    string GamesFinished,
    string Wins,
    string Losses,
    string WinLossRatio,
    string AverageGameLength,
    string TotalTimePlayed,
    string AverageEnemyCount,
    string AverageAllyCount,
    string TotalKills,
    string KillsPerGame,
    string TotalLosses,
    string LossesPerGame,
    string KillLossRatio,
    string TotalScore,
    string AverageEconomy,
    int FavouriteSideIndex,
    string AverageAILevel);

/// <summary>
/// The statistics window's queries (the XNA StatisticsWindow's game listing and totals): which recorded matches a
/// filter lists, the game modes to filter by, the totals of the listed matches and a match's player order.
/// </summary>
public static class StatisticsQuery
{
    /// <summary>The recorded matches, oldest first.</summary>
    public static List<MatchStatistics> AllMatches(GenericStatisticsManager statistics) =>
        Enumerable.Range(0, statistics.GetMatchCount()).Select(statistics.GetMatchByIndex).ToList();

    /// <summary>The recorded game modes, sorted (the game mode filter after "All").</summary>
    public static List<string> GameModes(IReadOnlyList<MatchStatistics> matches)
    {
        var gameModes = new List<string>();

        foreach (MatchStatistics ms in matches)
        {
            if (!gameModes.Contains(ms.GameMode))
                gameModes.Add(ms.GameMode);
        }

        gameModes.Sort();
        return gameModes;
    }

    /// <summary>
    /// The indexes of the matches the filters list, newest first.
    /// </summary>
    /// <param name="gameMode">The game mode to list, or null for all.</param>
    public static List<int> ListGames(IReadOnlyList<MatchStatistics> matches, StatisticsGameClass gameClass,
        string gameMode, bool includeSpectatedGames)
    {
        var listed = new List<int>();

        for (int i = 0; i < matches.Count; i++)
        {
            MatchStatistics ms = matches[i];

            if (IsOfClass(ms, gameClass) && MeetsPrerequisites(ms, gameMode, includeSpectatedGames))
                listed.Add(i);
        }

        listed.Reverse();
        return listed;
    }

    private static bool IsOfClass(MatchStatistics ms, StatisticsGameClass gameClass) => gameClass switch
    {
        StatisticsGameClass.All => true,
        StatisticsGameClass.Online => IsOnline(ms),
        StatisticsGameClass.PvP => IsPvP(ms),
        StatisticsGameClass.CoOp => IsCoOp(ms),
        StatisticsGameClass.Skirmish => IsSkirmish(ms),
        _ => false,
    };

    /// <summary>More than one human player.</summary>
    private static bool IsOnline(MatchStatistics ms) => ms.Players.Count(ps => !ps.IsAI) > 1;

    /// <summary>A playing human on a different team than another, or without a team.</summary>
    private static bool IsPvP(MatchStatistics ms)
    {
        int pTeam = -1;

        foreach (PlayerStatistics ps in ms.Players)
        {
            if (!ps.IsAI && !ps.WasSpectator)
            {
                // If we find a single player on a different team than another player,
                // we'll count the game as a PvP game
                if (pTeam > -1 && (ps.Team != pTeam || ps.Team == 0))
                    return true;

                pTeam = ps.Team;
            }
        }

        return false;
    }

    /// <summary>More than one playing human, all on the same team.</summary>
    private static bool IsCoOp(MatchStatistics ms)
    {
        int hpCount = 0;
        int pTeam = -1;

        foreach (PlayerStatistics ps in ms.Players)
        {
            if (!ps.IsAI && !ps.WasSpectator)
            {
                hpCount++;

                if (pTeam > -1 && (ps.Team != pTeam || ps.Team == 0))
                    return false;

                pTeam = ps.Team;
            }
        }

        return hpCount > 1;
    }

    /// <summary>At most one human player.</summary>
    private static bool IsSkirmish(MatchStatistics ms) => ms.Players.Count(ps => !ps.IsAI) <= 1;

    private static bool MeetsPrerequisites(MatchStatistics ms, string gameMode, bool includeSpectatedGames)
    {
        if (gameMode != null && ms.GameMode != gameMode)
            return false;

        PlayerStatistics ps = ms.Players.Find(p => p.IsLocalPlayer);

        if (ps != null && !includeSpectatedGames && ps.WasSpectator)
            return false;

        return true;
    }

    /// <summary>A match's players, highest score first (the game statistics list's order).</summary>
    public static List<PlayerStatistics> PlayersByScore(MatchStatistics ms) =>
        ms.Players.OrderBy(p => p.Score).Reverse().ToList();

    /// <summary>A team's letter, or "-" for no team.</summary>
    public static string TeamIndexToString(int teamIndex)
    {
        if (teamIndex < 1 || teamIndex >= ProgramConstants.TEAMS.Count)
            return "-";

        return ProgramConstants.TEAMS[teamIndex - 1];
    }

    /// <summary>The totals of the listed matches (SetTotalStatistics).</summary>
    /// <param name="sideCount">The number of sides (for the favourite side).</param>
    public static StatisticsTotals Totals(IReadOnlyList<MatchStatistics> matches, IEnumerable<int> listedGameIndexes, int sideCount)
    {
        int gamesStarted = 0;
        int gamesFinished = 0;
        int gamesPlayed = 0;
        int wins = 0;
        int gameLosses = 0;
        TimeSpan timePlayed = TimeSpan.Zero;
        int numEnemies = 0;
        int numAllies = 0;
        int totalKills = 0;
        int totalLosses = 0;
        int totalScore = 0;
        int totalEconomy = 0;
        int[] sideGameCounts = new int[sideCount];
        int numEasyAIs = 0;
        int numMediumAIs = 0;
        int numHardAIs = 0;

        foreach (int gameIndex in listedGameIndexes)
        {
            MatchStatistics ms = matches[gameIndex];

            gamesStarted++;

            if (ms.SawCompletion)
                gamesFinished++;

            timePlayed += TimeSpan.FromSeconds(ms.LengthInSeconds);

            PlayerStatistics localPlayer = ms.Players.Find(ps => !ps.IsAI && ps.IsLocalPlayer);

            // The XNA window fails on a match without a local player; such a match only counts as started here
            if (localPlayer == null)
                continue;

            if (!localPlayer.WasSpectator)
            {
                totalKills += localPlayer.Kills;
                totalLosses += localPlayer.Losses;
                totalScore += localPlayer.Score;
                totalEconomy += localPlayer.Economy;

                if (localPlayer.Side > 0 && localPlayer.Side <= sideCount)
                    sideGameCounts[localPlayer.Side - 1]++;

                if (!ms.SawCompletion)
                    continue;

                if (localPlayer.Won)
                    wins++;
                else
                    gameLosses++;

                gamesPlayed++;

                foreach (PlayerStatistics ps in ms.Players)
                {
                    if (!ps.WasSpectator && (!ps.IsLocalPlayer || ps.IsAI))
                    {
                        if (ps.Team == 0 || localPlayer.Team != ps.Team)
                            numEnemies++;
                        else
                            numAllies++;

                        if (ps.IsAI)
                        {
                            if (ps.AILevel == 0)
                                numEasyAIs++;
                            else if (ps.AILevel == 1)
                                numMediumAIs++;
                            else
                                numHardAIs++;
                        }
                    }
                }
            }
        }

        string averageAILevel;
        if (numEasyAIs >= numMediumAIs && numEasyAIs >= numHardAIs)
            averageAILevel = "Easy".L10N("Client:Main:EasyAI");
        else if (numMediumAIs >= numEasyAIs && numMediumAIs >= numHardAIs)
            averageAILevel = "Medium".L10N("Client:Main:MediumAI");
        else
            averageAILevel = "Hard".L10N("Client:Main:HardAI");

        return new StatisticsTotals(
            GamesStarted: gamesStarted.ToString(),
            GamesFinished: gamesFinished.ToString(),
            Wins: wins.ToString(),
            Losses: gameLosses.ToString(),
            WinLossRatio: gameLosses > 0 ? Math.Round(wins / (double)gameLosses, 2).ToString() : "-",
            AverageGameLength: gamesStarted > 0 ? TimeSpan.FromSeconds((int)timePlayed.TotalSeconds / gamesStarted).ToString() : "-",
            TotalTimePlayed: timePlayed.ToString(),
            AverageEnemyCount: gamesPlayed > 0 ? Math.Round(numEnemies / (double)gamesPlayed, 2).ToString() : "-",
            AverageAllyCount: gamesPlayed > 0 ? Math.Round(numAllies / (double)gamesPlayed, 2).ToString() : "-",
            TotalKills: totalKills.ToString(),
            KillsPerGame: gamesPlayed > 0 ? (totalKills / gamesPlayed).ToString() : "-",
            TotalLosses: totalLosses.ToString(),
            LossesPerGame: gamesPlayed > 0 ? (totalLosses / gamesPlayed).ToString() : "-",
            KillLossRatio: totalLosses > 0 ? Math.Round(totalKills / (double)totalLosses, 2).ToString() : "-",
            TotalScore: totalScore.ToString(),
            AverageEconomy: gamesPlayed > 0 ? (totalEconomy / gamesPlayed).ToString() : "-",
            FavouriteSideIndex: GetHighestIndex(sideGameCounts),
            AverageAILevel: averageAILevel);
    }

    private static int GetHighestIndex(int[] t)
    {
        int highestIndex = -1;
        int highest = int.MinValue;

        for (int i = 0; i < t.Length; i++)
        {
            if (t[i] > highest)
            {
                highest = t[i];
                highestIndex = i;
            }
        }

        return highestIndex;
    }
}
