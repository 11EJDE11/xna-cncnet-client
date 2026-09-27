using System;
using System.Collections.Generic;
using System.Linq;

using ClientCore;
using ClientCore.Statistics;

using DTAClient.Domain.Multiplayer;

namespace ClientLogic.Statistics;

/// <summary>
/// The lobby's statistics rules, as the XNA GameLobbyBase has them: the rank (stars) the current setup can earn, the
/// rank icon of each map in the map list, and the match record written when a game starts.
/// </summary>
public static class LobbyStatistics
{
    public const int RANK_NONE = 0;
    public const int RANK_EASY = 1;
    public const int RANK_MEDIUM = 2;
    public const int RANK_HARD = 3;

    /// <summary>The rank icon files, indexed by rank (rankNone.png … rankHard.png).</summary>
    public static readonly IReadOnlyList<string> RankTextureNames = ["rankNone.png", "rankEasy.png", "rankNormal.png", "rankHard.png"];

    /// <summary>
    /// The rank the local player can earn with this setup (GameLobbyBase.GetRank), shown as stars on the launch button.
    /// </summary>
    /// <param name="allowScoring">Every game option allows scoring.</param>
    /// <param name="spectatorSideIndex">The spectator entry of the side drop-down.</param>
    public static int GetRank(bool isMultiplayer, GameModeMap gameModeMap, bool allowScoring,
        IReadOnlyList<PlayerInfo> players, IReadOnlyList<PlayerInfo> aiPlayers, string localPlayerName, int spectatorSideIndex)
    {
        if (gameModeMap?.GameMode == null || gameModeMap.Map == null)
            return RANK_NONE;

        if (!allowScoring)
            return RANK_NONE;

        PlayerInfo localPlayer = players.FirstOrDefault(p => p.Name == localPlayerName);

        if (localPlayer == null)
            return RANK_NONE;

        bool IsSpectator(PlayerInfo p) => p.SideId == spectatorSideIndex;

        if (IsSpectator(localPlayer))
            return RANK_NONE;

        // These variables are used by both the skirmish and multiplayer code paths
        int[] teamMemberCounts = new int[5];
        int lowestEnemyAILevel = 2;
        int highestAllyAILevel = 0;

        foreach (PlayerInfo aiPlayer in aiPlayers)
        {
            teamMemberCounts[aiPlayer.TeamId]++;

            if (aiPlayer.TeamId > 0 && aiPlayer.TeamId == localPlayer.TeamId)
            {
                if (aiPlayer.AILevel > highestAllyAILevel)
                    highestAllyAILevel = aiPlayer.AILevel;
            }
            else
            {
                if (aiPlayer.AILevel < lowestEnemyAILevel)
                    lowestEnemyAILevel = aiPlayer.AILevel;
            }
        }

        if (isMultiplayer)
        {
            if (players.Count == 1)
                return RANK_NONE;

            // PvP stars for 2-player and 3-player maps
            if (gameModeMap.MaxPlayers <= 3)
            {
                List<PlayerInfo> filteredPlayers = players.Where(p => !IsSpectator(p)).ToList();

                if (aiPlayers.Count > 0)
                    return RANK_NONE;

                if (filteredPlayers.Count != gameModeMap.MaxPlayers)
                    return RANK_NONE;

                int localTeamIndex = localPlayer.TeamId;
                if (localTeamIndex > 0 && filteredPlayers.Count(p => p.TeamId == localTeamIndex) > 1)
                    return RANK_NONE;

                return RANK_HARD;
            }

            // Coop stars for maps with 4 or more players
            // See the code in StatisticsManager.GetRankForCoopMatch for the conditions

            if (players.Any(IsSpectator))
                return RANK_NONE;

            if (aiPlayers.Count == 0)
                return RANK_NONE;

            if (players.Any(p => p.TeamId != localPlayer.TeamId))
                return RANK_NONE;

            if (players.Any(p => p.TeamId == 0))
                return RANK_NONE;

            if (aiPlayers.Any(p => p.TeamId == 0))
                return RANK_NONE;

            teamMemberCounts[localPlayer.TeamId] += players.Count;

            if (lowestEnemyAILevel < highestAllyAILevel)
            {
                // Check that the player's AI allies aren't stronger
                return RANK_NONE;
            }

            // Check that all teams have at least as many players
            // as the human players' team
            int allyCount = teamMemberCounts[localPlayer.TeamId];

            for (int i = 1; i < 5; i++)
            {
                if (i == localPlayer.TeamId)
                    continue;

                if (teamMemberCounts[i] > 0)
                {
                    if (teamMemberCounts[i] < allyCount)
                        return RANK_NONE;
                }
            }

            return lowestEnemyAILevel + 1;
        }

        // *********
        // Skirmish!
        // *********

        if (aiPlayers.Count != gameModeMap.MaxPlayers - 1)
            return RANK_NONE;

        teamMemberCounts[localPlayer.TeamId]++;

        if (lowestEnemyAILevel < highestAllyAILevel)
        {
            // Check that the player's AI allies aren't stronger
            return RANK_NONE;
        }

        if (localPlayer.TeamId > 0)
        {
            // Check that all teams have at least as many players
            // as the local player's team
            int allyCount = teamMemberCounts[localPlayer.TeamId];

            for (int i = 1; i < 5; i++)
            {
                if (i == localPlayer.TeamId)
                    continue;

                if (teamMemberCounts[i] > 0)
                {
                    if (teamMemberCounts[i] < allyCount)
                        return RANK_NONE;
                }
            }

            // Check that there is a team other than the players' team that is at least as large
            bool pass = false;
            for (int i = 1; i < 5; i++)
            {
                if (i == localPlayer.TeamId)
                    continue;

                if (teamMemberCounts[i] >= allyCount)
                {
                    pass = true;
                    break;
                }
            }

            if (!pass)
                return RANK_NONE;
        }

        return lowestEnemyAILevel + 1;
    }

    /// <summary>
    /// The best rank earned on a map that isn't co-op, or -1 (GetDefaultMapRankIndex of the skirmish and multiplayer
    /// lobbies).
    /// </summary>
    public static int GetDefaultMapRankIndex(StatisticsManager statistics, GameModeMap gameModeMap, bool isMultiplayer)
    {
        if (!isMultiplayer)
            return statistics.GetSkirmishRankForDefaultMap(gameModeMap.Map.UntranslatedName, gameModeMap.MaxPlayers);

        if (gameModeMap.MaxPlayers > 3)
            return statistics.GetCoopRankForDefaultMap(gameModeMap.Map.UntranslatedName, gameModeMap.MaxPlayers);

        if (statistics.HasWonMapInPvP(gameModeMap.Map.UntranslatedName, gameModeMap.GameMode.UntranslatedUIName, gameModeMap.MaxPlayers))
            return 2;

        return -1;
    }

    /// <summary>The map list's rank icon for a map (an index into <see cref="RankTextureNames"/>).</summary>
    public static int GetMapListRankIndex(StatisticsManager statistics, GameModeMap gameModeMap, bool isMultiplayer)
    {
        if (gameModeMap.IsCoop)
        {
            // Note: the statistics must have been read to call HasBeatCoOpMap
            return statistics.HasBeatCoOpMap(gameModeMap.Map.UntranslatedName, gameModeMap.GameMode.UntranslatedUIName)
                ? Math.Abs(2 - gameModeMap.CoopDifficultyLevel) + 1
                : 0;
        }

        return GetDefaultMapRankIndex(statistics, gameModeMap, isMultiplayer) + 1;
    }

    /// <summary>
    /// The match record for a game that is starting (GameLobbyBase.InitializeMatchStatistics); the lobby parses the
    /// game's results into it and saves it when the game exits.
    /// </summary>
    /// <param name="allowScoring">Every game option allows scoring (the record's IsValidForStar).</param>
    /// <param name="houseInfos">The launch's house infos: the players, then the AI players.</param>
    public static MatchStatistics CreateMatchStatistics(int uniqueGameId, GameModeMap gameModeMap, bool allowScoring,
        IReadOnlyList<PlayerInfo> players, IReadOnlyList<PlayerInfo> aiPlayers, PlayerHouseInfo[] houseInfos,
        IReadOnlyList<MultiplayerColor> mpColors, int spectatorSideIndex, string localPlayerName)
    {
        var matchStatistics = new MatchStatistics(ProgramConstants.GAME_VERSION, uniqueGameId,
            gameModeMap.Map.UntranslatedName, gameModeMap.GameMode.UntranslatedUIName, players.Count, gameModeMap.IsCoop);

        matchStatistics.IsValidForStar = allowScoring;

        int ColorIndex(int gameColorIndex)
        {
            for (int i = 0; i < mpColors.Count; i++)
            {
                if (mpColors[i].GameColorIndex == gameColorIndex)
                    return i;
            }

            return -1;
        }

        for (int pId = 0; pId < players.Count; pId++)
        {
            PlayerInfo pInfo = players[pId];
            matchStatistics.AddPlayer(pInfo.Name, pInfo.Name == localPlayerName,
                false, pInfo.SideId == spectatorSideIndex, houseInfos[pId].SideIndex + 1, pInfo.TeamId,
                ColorIndex(houseInfos[pId].ColorIndex), 10);
        }

        for (int aiId = 0; aiId < aiPlayers.Count; aiId++)
        {
            PlayerHouseInfo pHouseInfo = houseInfos[players.Count + aiId];
            PlayerInfo aiInfo = aiPlayers[aiId];
            matchStatistics.AddPlayer("Computer", false, true, false,
                pHouseInfo.SideIndex + 1, aiInfo.TeamId,
                ColorIndex(pHouseInfo.ColorIndex),
                aiInfo.AILevel);
        }

        return matchStatistics;
    }
}
