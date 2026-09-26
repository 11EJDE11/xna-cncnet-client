using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;

using ClientCore;

using DTAClient.Domain.Multiplayer;
using DTAClient.DXGUI;
using DTAClient.DXGUI.Multiplayer.GameLobby;

using Rampastring.Tools;

namespace ClientLogic.Launch;

/// <summary>
/// Builds the spawn.ini and spawnmap.ini of a game launch from the state of a game lobby.
/// </summary>
/// <remarks>
/// Every player's client must produce the same shared data from the same lobby, or the game desyncs.
/// All randomness comes from one <c>new Random(RandomSeed)</c>, consumed in this order:
/// <list type="number">
/// <item>side, colour and start of each player (humans first, then AI)</item>
/// <item>random co-op houses in <see cref="Map.ApplySpawnIniCode"/></item>
/// <item>random map code INIs in <see cref="GameMode.GetMapRulesIniFiles"/></item>
/// </list>
/// Don't change that order or the number of draws.
/// </remarks>
public sealed class GameLaunchBuilder
{
    private const int MAX_PLAYER_COUNT = 8;

    private readonly string gameRoot;
    private readonly string localPlayerName;
    private readonly bool isMultiplayer;

    private List<PlayerInfo> Players { get; }
    private List<PlayerInfo> AIPlayers { get; }
    private IReadOnlyList<string> PlayerIPAddresses { get; }
    private int RandomSeed { get; }
    private GameModeMap GameModeMap { get; }
    private Map Map => GameModeMap?.Map;
    private GameMode GameMode => GameModeMap?.GameMode;
    private IReadOnlyList<IGameSessionSetting> CheckBoxes { get; }
    private IReadOnlyList<IGameSessionSetting> DropDowns { get; }
    private List<MultiplayerColor> MPColors { get; }
    private int SideCount { get; }
    private List<int[]> RandomSelectors { get; }
    private int RandomSelectorCount { get; }
    private List<TeamStartMapping> TeamStartMappings { get; }
    private bool RemoveStartingLocations { get; }
    private IReadOnlyList<KeyValuePair<string, string>> ForcedSpawnIniOptions { get; }
    private IniFile SpawnIniAdditions { get; }
    private string BroadcastedGameOptionValues { get; }

    private IniFile spawnIni;

    private GameLaunchBuilder(LaunchRequest request, string gameRoot)
    {
        this.gameRoot = gameRoot;
        localPlayerName = request.LocalPlayerName;
        isMultiplayer = request.IsMultiplayer;
        Players = request.Players;
        AIPlayers = request.AIPlayers;
        PlayerIPAddresses = request.PlayerIPAddresses;
        RandomSeed = request.RandomSeed;
        GameModeMap = request.GameModeMap;
        CheckBoxes = request.CheckBoxes;
        DropDowns = request.DropDowns;
        MPColors = request.MPColors;
        SideCount = request.SideCount;
        RandomSelectors = request.RandomSelectors;
        RandomSelectorCount = request.RandomSelectorCount;
        TeamStartMappings = request.TeamStartMappings;
        RemoveStartingLocations = request.RemoveStartingLocations;
        ForcedSpawnIniOptions = request.ForcedSpawnIniOptions;
        SpawnIniAdditions = request.SpawnIniAdditions;
        BroadcastedGameOptionValues = request.BroadcastedGameOptionValues;
    }

    /// <summary>
    /// Builds the spawn files for a game launch. Reads the map and the map code INIs under
    /// <paramref name="gameRoot"/> but writes nothing.
    /// </summary>
    public static LaunchArtifacts Build(LaunchRequest request, string gameRoot)
    {
        var builder = new GameLaunchBuilder(request, gameRoot);

        Random pseudoRandom = new Random(builder.RandomSeed);

        PlayerHouseInfo[] houseInfos = builder.WriteSpawnIni(pseudoRandom);
        (IniFile mapIni, List<SupplementalMapFile> supplementalMapFiles) = builder.WriteMap(houseInfos, pseudoRandom);

        return new LaunchArtifacts(builder.spawnIni, mapIni, houseInfos, supplementalMapFiles);
    }

    /// <summary>
    /// Gets a list of side indexes that are disallowed for human or computer players.
    /// </summary>
    /// <returns>A list of disallowed side indexes.</returns>
    public static bool[] GetDisallowedSidesForGroup(int sideCount, GameModeMap gameModeMap,
        IEnumerable<IGameSessionSetting> checkBoxes, bool forHumanPlayers)
    {
        GameMode gameMode = gameModeMap?.GameMode;

        var returnValue = GetDisallowedSides(sideCount, gameModeMap, checkBoxes);
        var sides = forHumanPlayers ? gameMode?.DisallowedHumanPlayerSides : gameMode?.DisallowedComputerPlayerSides;
        if (sides != null)
        {
            foreach (int i in sides)
                returnValue[i] = true;
        }

        return returnValue;
    }

    /// <summary>
    /// Gets a list of side indexes that are disallowed.
    /// </summary>
    /// <returns>A list of disallowed side indexes.</returns>
    public static bool[] GetDisallowedSides(int sideCount, GameModeMap gameModeMap, IEnumerable<IGameSessionSetting> checkBoxes)
    {
        GameMode gameMode = gameModeMap?.GameMode;

        var returnValue = new bool[sideCount];

        if (gameModeMap != null && gameModeMap.CoopInfo != null)
        {
            // Co-Op map disallowed side logic

            foreach (int disallowedSideIndex in gameModeMap.CoopInfo.DisallowedPlayerSides)
                returnValue[disallowedSideIndex] = true;
        }

        if (gameMode != null)
        {
            foreach (int disallowedSideIndex in gameMode.DisallowedPlayerSides)
                returnValue[disallowedSideIndex] = true;
        }

        foreach (var checkBox in checkBoxes)
            checkBox.ApplyDisallowedSideIndex(returnValue);

        return returnValue;
    }

    /// <summary>
    /// Returns the number of teams with human players in them.
    /// Does not count spectators and human players that don't have a team set.
    /// </summary>
    /// <returns>The number of human player teams in the game.</returns>
    public static int GetPvPTeamCount(IEnumerable<PlayerInfo> players, int spectatorSideIndex)
    {
        int[] teamPlayerCounts = new int[4];
        int playerTeamCount = 0;

        foreach (PlayerInfo pInfo in players)
        {
            if (pInfo.IsAI || pInfo.SideId == spectatorSideIndex)
                continue;

            if (pInfo.TeamId > 0)
            {
                teamPlayerCounts[pInfo.TeamId - 1]++;
                if (teamPlayerCounts[pInfo.TeamId - 1] == 2)
                    playerTeamCount++;
            }
        }

        return playerTeamCount;
    }

    public static GameType GetGameType(int pvpTeamCount)
    {
        if (pvpTeamCount == 0)
            return GameType.FFA;

        if (pvpTeamCount == 1)
            return GameType.Coop;

        return GameType.TeamGame;
    }

    /// <summary>
    /// Returns the files in <paramref name="basePath"/> named <paramref name="baseFileName"/>
    /// with one of the configured supplemental map file extensions.
    /// </summary>
    public static IEnumerable<string> GetSupplementalMapFiles(string basePath, string baseFileName)
    {
        // Get the supplemental file names for allowable extensions
        var supplementalMapFileNames = ClientConfiguration.Instance.SupplementalMapFileExtensions
            .Select(ext => $"{baseFileName}.{ext}")
            .ToList();

        if (!supplementalMapFileNames.Any())
            return new List<string>();

        // Get full file paths for all possible supplemental files
        return Directory.GetFiles(basePath, $"{baseFileName}.*")
            .Where(f => supplementalMapFileNames.Contains(Path.GetFileName(f)));
    }

    private int GetSpectatorSideIndex() => SideCount + RandomSelectorCount;

    private bool[] GetDisallowedSidesForGroup(bool forHumanPlayers) =>
        GetDisallowedSidesForGroup(SideCount, GameModeMap, CheckBoxes, forHumanPlayers);

    private int GetPvPTeamCount() => GetPvPTeamCount(Players, GetSpectatorSideIndex());

    private GameType GetGameType() => GetGameType(GetPvPTeamCount());

    /// <summary>
    /// Randomizes options of both human and AI players
    /// and returns the options as an array of PlayerHouseInfos.
    /// </summary>
    /// <returns>An array of PlayerHouseInfos.</returns>
    private PlayerHouseInfo[] Randomize(List<TeamStartMapping> teamStartMappings, Random pseudoRandom)
    {
        int totalPlayerCount = Players.Count + AIPlayers.Count;
        PlayerHouseInfo[] houseInfos = new PlayerHouseInfo[totalPlayerCount];

        for (int i = 0; i < totalPlayerCount; i++)
            houseInfos[i] = new PlayerHouseInfo();

        // Gather list of spectators
        for (int i = 0; i < Players.Count; i++)
            houseInfos[i].IsSpectator = Players[i].SideId == GetSpectatorSideIndex();

        // Gather list of available colors

        List<int> freeColors = new List<int>();

        for (int cId = 0; cId < MPColors.Count; cId++)
            freeColors.Add(cId);

        if (GameModeMap.CoopInfo != null)
        {
            foreach (int colorIndex in GameModeMap.CoopInfo.DisallowedPlayerColors)
                freeColors.Remove(colorIndex);
        }

        foreach (PlayerInfo player in Players)
            freeColors.Remove(player.ColorId - 1); // The first color is Random

        foreach (PlayerInfo aiPlayer in AIPlayers)
            freeColors.Remove(aiPlayer.ColorId - 1);

        // Gather list of available starting locations

        List<int> freeStartingLocations = new List<int>();
        List<int> takenStartingLocations = new List<int>();

        foreach (int i in GameModeMap.AllowedStartingLocations)
            freeStartingLocations.Add(i - 1);

        for (int i = 0; i < Players.Count; i++)
        {
            if (!houseInfos[i].IsSpectator)
            {
                freeStartingLocations.Remove(Players[i].StartingLocation - 1);
                //takenStartingLocations.Add(Players[i].StartingLocation - 1);
                // ^ Gives everyone with a selected location a completely random
                // location in-game, because PlayerHouseInfo.RandomizeStart already
                // fills the list itself
            }
        }

        for (int i = 0; i < AIPlayers.Count; i++)
            freeStartingLocations.Remove(AIPlayers[i].StartingLocation - 1);

        foreach (var teamStartMapping in teamStartMappings.Where(mapping => mapping.IsBlock))
            freeStartingLocations.Remove(teamStartMapping.StartingWaypoint);

        // Randomize options

        for (int i = 0; i < totalPlayerCount; i++)
        {
            PlayerInfo pInfo;
            PlayerHouseInfo pHouseInfo = houseInfos[i];
            bool[] disallowedSides;

            if (i < Players.Count)
            {
                pInfo = Players[i];
                disallowedSides = GetDisallowedSidesForGroup(forHumanPlayers: true);
            }
            else
            {
                pInfo = AIPlayers[i - Players.Count];
                disallowedSides = GetDisallowedSidesForGroup(forHumanPlayers: false);
            }

            pHouseInfo.RandomizeSide(pInfo, SideCount, pseudoRandom, disallowedSides, RandomSelectors, RandomSelectorCount);

            pHouseInfo.RandomizeColor(pInfo, freeColors, MPColors, pseudoRandom);

            bool overrideGameRandomLocations = teamStartMappings.Any()
                || GameModeMap.AllowedStartingLocations.Max() > GameModeMap.MaxPlayers; // non-sequential AllowedStartingLocations
            pHouseInfo.RandomizeStart(pInfo, pseudoRandom, freeStartingLocations, takenStartingLocations, overrideGameRandomLocations);
        }

        return houseInfos;
    }

    /// <summary>
    /// Builds spawn.ini. Returns the player house info returned from the randomizer.
    /// </summary>
    private PlayerHouseInfo[] WriteSpawnIni(Random pseudoRandom)
    {
        Logger.Log("Writing spawn.ini");

        if (GameModeMap.IsCoop)
        {
            foreach (PlayerInfo pInfo in Players)
            {
                Debug.Assert(pInfo.TeamId == 1, "Co-ops should always set TeamId to 1 before lanching the game");
                pInfo.TeamId = 1;
            }

            foreach (PlayerInfo pInfo in AIPlayers)
            {
                Debug.Assert(pInfo.TeamId == 1, "Co-ops should always set TeamId to 1 before lanching the game");
                pInfo.TeamId = 1;
            }
        }

        var teamStartMappings = TeamStartMappings;

        PlayerHouseInfo[] houseInfos = Randomize(teamStartMappings, pseudoRandom);

        spawnIni = new IniFile();

        IniSection settings = new IniSection("Settings");

        settings.SetStringValue("Name", localPlayerName);
        settings.SetStringValue("Scenario", ProgramConstants.SPAWNMAP_INI);
        settings.SetStringValue("UIGameMode", GameMode.UntranslatedUIName);
        settings.SetStringValue("UIMapName", Map.UntranslatedName);

        // needed for translation in game loading lobbies
        if (Map.Official)
            settings.SetStringValue("MapID", Map.BaseFilePath);

        settings.SetIntValue("PlayerCount", Players.Count);
        int myIndex = Players.FindIndex(c => c.Name == localPlayerName);
        settings.SetIntValue("Side", houseInfos[myIndex].InternalSideIndex);
        settings.SetBooleanValue("IsSpectator", houseInfos[myIndex].IsSpectator);
        settings.SetIntValue("Color", houseInfos[myIndex].ColorIndex);
        settings.SetStringValue("CustomLoadScreen", LoadingScreenController.GetLoadScreenName(houseInfos[myIndex].InternalSideIndex.ToString()));
        settings.SetIntValue("AIPlayers", AIPlayers.Count);
        settings.SetIntValue("Seed", RandomSeed);
        if (GetPvPTeamCount() > 1)
            settings.SetBooleanValue("CoachMode", true);
        if (GetGameType() == GameType.Coop)
            settings.SetBooleanValue("AutoSurrender", false);
        spawnIni.AddSection(settings);
        WriteSpawnIniAdditions(spawnIni);

        foreach (IGameSessionSetting chkBox in CheckBoxes)
            chkBox.ApplySpawnIniCode(spawnIni);

        foreach (IGameSessionSetting dd in DropDowns)
            dd.ApplySpawnIniCode(spawnIni);

        // Apply forced options from GameOptions.ini

        foreach (KeyValuePair<string, string> forcedOption in ForcedSpawnIniOptions)
        {
            spawnIni.SetStringValue("Settings", forcedOption.Key, forcedOption.Value);
        }

        GameMode.ApplySpawnIniCode(spawnIni); // Forced options from the game mode
        Map.ApplySpawnIniCode(spawnIni, Players.Count + AIPlayers.Count,
            AIPlayers.Count, GameModeMap.IsCoop, GameModeMap.CoopInfo, GameModeMap.CoopDifficultyLevel, pseudoRandom, SideCount); // Forced options from the map

        // Player options

        int otherId = 1;

        for (int pId = 0; pId < Players.Count; pId++)
        {
            PlayerInfo pInfo = Players[pId];
            PlayerHouseInfo pHouseInfo = houseInfos[pId];

            if (pInfo.Name == localPlayerName)
                continue;

            string sectionName = "Other" + otherId;

            spawnIni.SetStringValue(sectionName, "Name", pInfo.Name);
            spawnIni.SetIntValue(sectionName, "Side", pHouseInfo.InternalSideIndex);
            spawnIni.SetBooleanValue(sectionName, "IsSpectator", pHouseInfo.IsSpectator);
            spawnIni.SetIntValue(sectionName, "Color", pHouseInfo.ColorIndex);
            spawnIni.SetStringValue(sectionName, "Ip", PlayerIPAddresses[pId]);
            spawnIni.SetIntValue(sectionName, "Port", pInfo.Port);

            otherId++;
        }

        // The spawner assigns players to SpawnX houses based on their in-game color index
        List<int> multiCmbIndexes = new List<int>();
        var sortedColorList = MPColors.OrderBy(mpc => mpc.GameColorIndex).ToList();

        for (int cId = 0; cId < sortedColorList.Count; cId++)
        {
            for (int pId = 0; pId < Players.Count; pId++)
            {
                if (houseInfos[pId].ColorIndex == sortedColorList[cId].GameColorIndex)
                    multiCmbIndexes.Add(pId);
            }
        }

        if (AIPlayers.Count > 0)
        {
            for (int aiId = 0; aiId < AIPlayers.Count; aiId++)
            {
                int multiId = multiCmbIndexes.Count + aiId + 1;

                string keyName = "Multi" + multiId;

                spawnIni.SetIntValue("HouseHandicaps", keyName, AIPlayers[aiId].HouseHandicapAILevel);
                spawnIni.SetIntValue("HouseCountries", keyName, houseInfos[Players.Count + aiId].InternalSideIndex);
                spawnIni.SetIntValue("HouseColors", keyName, houseInfos[Players.Count + aiId].ColorIndex);
            }
        }

        for (int multiId = 0; multiId < multiCmbIndexes.Count; multiId++)
        {
            int pIndex = multiCmbIndexes[multiId];
            if (houseInfos[pIndex].IsSpectator)
                spawnIni.SetBooleanValue("IsSpectator", "Multi" + (multiId + 1), true);
        }

        // Write alliances, the code is pretty big so let's take it to another class
        AllianceHolder.WriteInfoToSpawnIni(Players, AIPlayers, multiCmbIndexes, houseInfos.ToList(), teamStartMappings, spawnIni);

        for (int pId = 0; pId < Players.Count; pId++)
        {
            int startingWaypoint = houseInfos[multiCmbIndexes[pId]].StartingWaypoint;

            // -1 means no starting location at all - let the game itself pick the starting location
            // using its own logic
            if (startingWaypoint > -1)
            {
                int multiIndex = pId + 1;
                spawnIni.SetIntValue("SpawnLocations", "Multi" + multiIndex,
                    startingWaypoint);
            }
        }

        for (int aiId = 0; aiId < AIPlayers.Count; aiId++)
        {
            int startingWaypoint = houseInfos[Players.Count + aiId].StartingWaypoint;

            if (startingWaypoint > -1)
            {
                int multiIndex = Players.Count + aiId + 1;
                spawnIni.SetIntValue("SpawnLocations", "Multi" + multiIndex,
                    startingWaypoint);
            }
        }

        spawnIni.SetStringValue("Settings", "MapSHA1", Map.SHA1);
        string packedGameOptionValues = BroadcastedGameOptionValues;
        spawnIni.SetStringValue("Settings", "BroadcastedGameOptionValues", packedGameOptionValues);

        return houseInfos;
    }

    /// <summary>
    /// Applies the lobby-specific spawn.ini values, in the order the lobby set them.
    /// </summary>
    private void WriteSpawnIniAdditions(IniFile iniFile)
    {
        foreach (string sectionName in SpawnIniAdditions.GetSections())
        {
            foreach (KeyValuePair<string, string> kvp in SpawnIniAdditions.GetSection(sectionName).Keys)
                iniFile.SetStringValue(sectionName, kvp.Key, kvp.Value);
        }
    }

    /// <summary>
    /// Builds spawnmap.ini.
    /// </summary>
    private (IniFile MapIni, List<SupplementalMapFile> SupplementalMapFiles) WriteMap(PlayerHouseInfo[] houseInfos, Random pseudoRandom)
    {
        Logger.Log("Writing map.");

        Logger.Log("Loading map INI from " + Map.CompleteFilePath);

        IniFile mapIni = Map.GetMapIni();

        IniFile globalCodeIni = new IniFile(SafePath.CombineFilePath(gameRoot, "INI", "Map Code", "GlobalCode.ini"));

        foreach (IniFile iniFile in GameMode.GetMapRulesIniFiles(pseudoRandom))
            MapCodeHelper.ApplyMapCode(mapIni, iniFile);

        MapCodeHelper.ApplyMapCode(mapIni, globalCodeIni);

        if (isMultiplayer)
        {
            IniFile mpGlobalCodeIni = new IniFile(SafePath.CombineFilePath(gameRoot, "INI", "Map Code", "MultiplayerGlobalCode.ini"));
            MapCodeHelper.ApplyMapCode(mapIni, mpGlobalCodeIni);
        }
        else
        {
            // Avoid writing the original filename to spawnmap.ini MP games, as it may vary between systems, e.g., when a host uploads a map while other players in game might download it with a diffrent filename.
            // This inconsistency can result in differing spawnmap.ini files among players, causing desyncs in CnCNet YR games.
            // Theoretically it can be useful for some singleplayer campaign tracking
            // But it isn't currently used by any CnCNet game or mod
            // The code below only applies to the single player case
            string mapIniFileName = Path.GetFileName(mapIni.FilePath);
            mapIni.SetStringValue("Basic", "OriginalFilename", mapIniFileName);
        }

        foreach (IGameSessionSetting checkBox in CheckBoxes)
            checkBox.ApplyMapCode(mapIni, GameMode);

        foreach (IGameSessionSetting dropDown in DropDowns)
            dropDown.ApplyMapCode(mapIni, GameMode);

        mapIni.MoveSectionToFirst("MultiplayerDialogSettings"); // Required by YR

        List<SupplementalMapFile> supplementalMapFiles = GetSupplementalMapFiles(mapIni);

        ManipulateStartingLocations(mapIni, houseInfos);

        return (mapIni, supplementalMapFiles);
    }

    /// <summary>
    /// Some mods require that .map files also have supplemental files copied over with the spawnmap.ini.
    ///
    /// This function scans the directory containing the map file and looks for other files with the
    /// same base filename as the map file that are allowed by the client configuration.
    /// Those files are to be copied to the game base path with the base filename of "spawnmap.EXT".
    /// </summary>
    /// <param name="mapIni"></param>
    private static List<SupplementalMapFile> GetSupplementalMapFiles(IniFile mapIni)
    {
        var mapFileInfo = new FileInfo(mapIni.FilePath);
        string mapFileBaseName = Path.GetFileNameWithoutExtension(mapFileInfo.Name);

        IEnumerable<string> supplementalMapFiles = GetSupplementalMapFiles(mapFileInfo.DirectoryName, mapFileBaseName).ToList();
        if (!supplementalMapFiles.Any())
            return new List<SupplementalMapFile>();

        List<SupplementalMapFile> supplementalFiles = new();
        foreach (string file in supplementalMapFiles)
        {
            string supplementalFileName = $"spawnmap{Path.GetExtension(file)}";
            supplementalFiles.Add(new SupplementalMapFile(file, supplementalFileName));
        }

        // Write the supplemental map files to the INI (eventual spawnmap.ini)
        mapIni.SetStringValue("Basic", "SupplementalFiles", string.Join(",", supplementalFiles.Select(f => f.TargetFileName)));

        return supplementalFiles;
    }

    private void ManipulateStartingLocations(IniFile mapIni, PlayerHouseInfo[] houseInfos)
    {
        if (RemoveStartingLocations)
        {
            if (GameModeMap.EnforceMaxPlayers)
                return;

            // All random starting locations given by the game
            IniSection waypointSection = mapIni.GetSection("Waypoints");
            if (waypointSection == null)
                return;

            // TODO implement IniSection.RemoveKey in Rampastring.Tools, then
            // remove implementation that depends on internal implementation
            // of IniSection
            for (int i = 0; i <= 7; i++)
            {
                int index = waypointSection.Keys.FindIndex(k => !string.IsNullOrEmpty(k.Key) && k.Key == i.ToString());
                if (index > -1)
                    waypointSection.Keys.RemoveAt(index);
            }
        }

        // Multiple players cannot properly share the same starting location
        // without breaking the SpawnX house logic that pre-placed objects depend on

        // To work around this, we add new starting locations that just point
        // to the same cell coordinates as existing stacked starting locations
        // and make additional players in the same start loc start from the new
        // starting locations instead.

        // As an additional restriction, players can only start from waypoints 0 to 7.
        // That means that if the map already has too many starting waypoints,
        // we need to move existing (but un-occupied) starting waypoints to point
        // to the stacked locations so we can spawn the players there.


        // Check for stacked starting locations (locations with more than 1 player on it)
        bool[] startingLocationUsed = new bool[MAX_PLAYER_COUNT];
        bool stackedStartingLocations = false;
        foreach (PlayerHouseInfo houseInfo in houseInfos)
        {
            if (houseInfo.RealStartingWaypoint > -1)
            {
                startingLocationUsed[houseInfo.RealStartingWaypoint] = true;

                // If assigned starting waypoint is unknown while the real
                // starting location is known, it means that
                // the location is shared with another player
                if (houseInfo.StartingWaypoint == -1)
                {
                    stackedStartingLocations = true;
                }
            }
        }

        // If any starting location is stacked, re-arrange all starting locations
        // so that unused starting locations are removed and made to point at used
        // starting locations
        if (!stackedStartingLocations)
            return;

        // We also need to modify spawn.ini because WriteSpawnIni
        // doesn't handle stacked positions.
        // We could move this code there, but then we'd have to process
        // the stacked locations in two places (here and in WriteSpawnIni)
        // because we'd need to modify the map anyway.
        // Not sure whether having it like this or in WriteSpawnIni
        // is better, but this implementation is quicker to write for now.
        spawnIni = ReloadAsWritten(spawnIni);

        // For each player, check if they're sharing the starting location
        // with someone else
        // If they are, find an unused waypoint and assign their
        // starting location to match that
        for (int pId = 0; pId < houseInfos.Length; pId++)
        {
            PlayerHouseInfo houseInfo = houseInfos[pId];

            if (houseInfo.RealStartingWaypoint > -1 &&
                houseInfo.StartingWaypoint == -1)
            {
                // Find first unused starting location index
                int unusedLocation = -1;
                for (int i = 0; i < startingLocationUsed.Length; i++)
                {
                    if (!startingLocationUsed[i])
                    {
                        unusedLocation = i;
                        startingLocationUsed[i] = true;
                        break;
                    }
                }

                houseInfo.StartingWaypoint = unusedLocation;
                mapIni.SetIntValue("Waypoints", unusedLocation.ToString(),
                    mapIni.GetIntValue("Waypoints", houseInfo.RealStartingWaypoint.ToString(), 0));
                spawnIni.SetIntValue("SpawnLocations", $"Multi{pId + 1}", unusedLocation);
            }
        }
    }

    /// <summary>
    /// Returns the INI file as it reads back after being written: comments (text after ';') are dropped
    /// and keys and values are trimmed. The stacked start location handling used to re-read spawn.ini
    /// from disk, so this keeps its output identical.
    /// </summary>
    private static IniFile ReloadAsWritten(IniFile iniFile)
    {
        byte[] written;
        using (var stream = new MemoryStream())
        {
            iniFile.WriteIniStream(stream);
            written = stream.ToArray();
        }

        using var reader = new MemoryStream(written);
        return new IniFile(reader, applyBaseIni: false);
    }
}
