using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

using ClientCore;
using ClientCore.Extensions;

using ClientLogic.Launch;
using ClientLogic.Lobby;
using ClientLogic.Options;
using ClientLogic.Protocol;
using ClientLogic.UI;

using CommunityToolkit.Mvvm.ComponentModel;

using DTAClient.Domain.Multiplayer;

using Rampastring.Tools;

namespace ClientLogic.Skirmish;

/// <summary>
/// A skirmish lobby without a user interface: the game options (from the theme's SkirmishLobby layout), the players,
/// the selected map, the saved skirmish settings and the launch. It follows the XNA client's skirmish lobby step by
/// step, so both launch the same game from the same saved settings.
/// </summary>
public sealed partial class SkirmishSession : ObservableObject
{
    public const int MAX_PLAYER_COUNT = 8;
    public const string WINDOW_NAME = "SkirmishLobby";
    private const string SETTINGS_PATH = "Client/SkirmishSettings.ini";

    private readonly MapLoader mapLoader;
    private readonly GameProcessService gameProcess;
    private readonly IDialogService dialogs;
    private readonly Random random;

    public SkirmishSession(MapLoader mapLoader, GameProcessService gameProcess, IDialogService dialogs, Random random)
    {
        this.mapLoader = mapLoader;
        this.gameProcess = gameProcess;
        this.dialogs = dialogs;
        this.random = random;

        GameOptionsIni = new IniFile(SafePath.CombineFilePath(ProgramConstants.GetBaseResourcePath(), ClientConfiguration.GAME_OPTIONS));

        Sides = ClientConfiguration.Instance.Sides.Split(',');
        LoadRandomSelectors();
        SlotIndices = new SlotIndexMapper(Sides.Count, RandomSelectors.Count + 1);
        MPColors = MultiplayerColor.LoadColors();

        foreach (GameOptionDefinition definition in GameOptionCatalog.Load(new CCIniFile(FindLayoutIni(WINDOW_NAME)), WINDOW_NAME))
            Options.Add(new GameOption(definition, definition.DefaultValue));

        // As the XNA lobby does from its Initialize on: record the host's choices
        Options.OptionChanged += (_, _) => NormalisePlayers();

        Slots = new PlayerSlotsState(Players, AIPlayers);
        LobbyState = new GameLobbyState(Options, Slots, ExtraOptions);
        RandomSeed = random.Next();
    }

    public IniFile GameOptionsIni { get; }

    public GameOptionSet Options { get; } = new();

    public List<PlayerInfo> Players { get; } = [];

    public List<PlayerInfo> AIPlayers { get; } = [];

    public PlayerSlotsState Slots { get; }

    public PlayerExtraOptionsState ExtraOptions { get; } = new();

    public GameLobbyState LobbyState { get; }

    /// <summary>The real sides (without the random selectors and Spectator).</summary>
    public IReadOnlyList<string> Sides { get; }

    /// <summary>The custom random selectors: name and real sides.</summary>
    public List<string> RandomSelectorNames { get; } = [];

    public List<int[]> RandomSelectors { get; } = [];

    public SlotIndexMapper SlotIndices { get; }

    public List<MultiplayerColor> MPColors { get; }

    public int RandomSeed { get; private set; }

    public GameModeMap GameModeMap => LobbyState.GameModeMap;

    /// <summary>Raised when the players, map or options change, so views can refresh.</summary>
    public event EventHandler Changed;

    /// <summary>The theme's copy of a layout INI, or the base one (as the XNA client finds window INIs).</summary>
    public static string FindLayoutIni(string windowName)
    {
        FileInfo themeFile = SafePath.GetFile(ProgramConstants.GetResourcePath(), windowName + ".ini");
        return themeFile.Exists ? themeFile.FullName : SafePath.CombineFilePath(ProgramConstants.GetBaseResourcePath(), windowName + ".ini");
    }

    private void LoadRandomSelectors()
    {
        List<string> keys = GameOptionsIni.GetSectionKeys("RandomSelectors");
        if (keys == null)
            return;

        foreach (string randomSelector in keys)
        {
            List<int> randomSides = [];
            try
            {
                string[] tmp = GameOptionsIni.GetStringListValue("RandomSelectors", randomSelector, string.Empty);
                randomSides = Array.ConvertAll(tmp, int.Parse).ToList();
                randomSides.RemoveAll(x => x >= Sides.Count || x < 0);
            }
            catch (FormatException)
            {
            }

            if (randomSides.Count > 1)
            {
                RandomSelectorNames.Add(randomSelector);
                RandomSelectors.Add(randomSides.ToArray());
            }
        }
    }

    /// <summary>
    /// Selects a map: applies the forced options of the game mode and map and the map's player rules, as the XNA
    /// lobby's ChangeMap does.
    /// </summary>
    public void ChangeMap(GameModeMap gameModeMap)
    {
        Logger.Log("SkirmishSession: map " + (gameModeMap == null ? "none" : gameModeMap.Map.SHA1 + " " + gameModeMap.Map.UntranslatedName));
        LobbyState.GameModeMap = gameModeMap;

        if (gameModeMap == null)
        {
            Changed?.Invoke(this, EventArgs.Empty);
            return;
        }

        Options.BeginUpdate();
        Options.ApplyGameModeMap(gameModeMap.GameMode, gameModeMap.Map);

        MapSlotRules mapRules = MapSlotRules.FromGameModeMap(gameModeMap);
        PlayerSlotRules.NormaliseForMap(Players, AIPlayers, mapRules, MPColors.Count);
        ExtraOptions.SetTeamOptionsAllowed(!mapRules.HasCoopInfo);

        NormalisePlayers();
        Options.EndUpdate();

        Changed?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>Numbers the players by row and applies the side rules (the XNA lobby's refresh).</summary>
    public void NormalisePlayers()
    {
        for (int i = 0; i < Players.Count; i++)
            Players[i].Index = i;

        for (int i = 0; i < AIPlayers.Count; i++)
            AIPlayers[i].Index = Players.Count + i;

        foreach (bool forHumanPlayers in new[] { false, true })
        {
            SideAvailability availability = PlayerSlotRules.ComputeSideAvailability(
                GameLaunchBuilder.GetDisallowedSidesForGroup(Sides.Count, GameModeMap, Options.CheckBoxes, forHumanPlayers),
                RandomSelectors, SlotIndices, hasCoopInfo: GameModeMap?.CoopInfo != null);
            PlayerSlotRules.NormaliseSides(forHumanPlayers ? Players : AIPlayers, availability);
        }
    }

    /// <summary>Applies a change to a player row (see <see cref="PlayerSlotsState.ApplyChange"/>).</summary>
    public void ChangeSlot(int row, SlotField field, int index)
    {
        Slots.ApplyChange(row, field, index, SlotIndices, isCoop: GameModeMap != null && GameModeMap.IsCoop);
        NormalisePlayers();
        Changed?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>Loads the saved skirmish settings, or the defaults (the XNA lobby's LoadSettings).</summary>
    public void LoadSettings()
    {
        Players.Clear();
        AIPlayers.Clear();

        FileInfo settingsFile = SafePath.GetFile(ProgramConstants.GamePath, SETTINGS_PATH);
        if (!settingsFile.Exists)
        {
            InitDefaultSettings();
            return;
        }

        var skirmishSettingsIni = new IniFile(settingsFile.FullName);

        string gameModeMapFilterName = skirmishSettingsIni.GetStringValue("Settings", "GameModeMapFilter", string.Empty);
        if (string.IsNullOrEmpty(gameModeMapFilterName))
            gameModeMapFilterName = skirmishSettingsIni.GetStringValue("Settings", "GameMode", string.Empty); // legacy

        List<GameModeMap> filterMaps = GameModeMapsOf(gameModeMapFilterName);
        if (filterMaps.Count == 0)
            filterMaps = GameModeMapsOf(DefaultGameMode()?.UIName);

        GameModeMap gameModeMap = filterMaps.FirstOrDefault();
        if (gameModeMap != null)
        {
            // The XNA lobby looks the saved map up in its map list, which holds every game mode's maps when
            // "search all game modes" is on (sorting by name keeps the first match the same)
            string mapSHA1 = skirmishSettingsIni.GetStringValue("Settings", "Map", string.Empty);
            List<GameModeMap> candidates = UserINISettings.Instance.SearchAllGameModes.Value ? mapLoader.GameModeMaps.ToList() : filterMaps;
            ChangeMap(candidates.Find(gmm => gmm.Map.SHA1 == mapSHA1) ?? gameModeMap);
        }
        else
        {
            ChangeMap(GameModeMapsOf(DefaultGameMode()?.UIName).FirstOrDefault());
        }

        PlayerInfo player = PlayerInfo.FromString(skirmishSettingsIni.GetStringValue("Player", "Info", string.Empty));
        if (player == null)
        {
            Logger.Log("Failed to load human player information from skirmish settings!");
            InitDefaultSettings();
            return;
        }

        CheckLoadedPlayerVariableBounds(player);
        player.Name = ProgramConstants.PLAYERNAME;
        Players.Add(player);

        List<string> keys = skirmishSettingsIni.GetSectionKeys("AIPlayers") ?? [];
        bool aiAllowed = GameModeMap != null && !GameModeMap.HumanPlayersOnly;

        foreach (string key in keys)
        {
            if (!aiAllowed)
                break;

            PlayerInfo aiPlayer = PlayerInfo.FromString(skirmishSettingsIni.GetStringValue("AIPlayers", key, string.Empty));
            if (aiPlayer == null)
            {
                Logger.Log("Failed to load AI player information from skirmish settings!");
                InitDefaultSettings();
                return;
            }

            CheckLoadedPlayerVariableBounds(aiPlayer, isAIPlayer: true);

            if (AIPlayers.Count < MAX_PLAYER_COUNT - 1)
                AIPlayers.Add(aiPlayer);
        }

        if (ClientConfiguration.Instance.SaveSkirmishGameOptions)
            Options.ReadSettings(skirmishSettingsIni, "GameOptions", GameModeMap?.GameMode, GameModeMap?.Map);

        NormalisePlayers();
        Changed?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>The maps of the game mode with this display name, in the map list's order.</summary>
    public List<GameModeMap> GameModeMapsOf(string gameModeUIName) =>
        mapLoader.GameModeMaps.Where(gmm => gmm.GameMode.UIName == gameModeUIName).ToList();

    private GameMode DefaultGameMode() => mapLoader.GameModes.FirstOrDefault(gm => gm.Maps.Count > 0);

    private void InitDefaultSettings()
    {
        Players.Clear();
        AIPlayers.Clear();

        Players.Add(new PlayerInfo(ProgramConstants.PLAYERNAME, 0, 0, 0, 0));
        AIPlayers.Add(new PlayerInfo(ProgramConstants.AI_PLAYER_NAMES[0], 0, 0, 0, 0) { IsAI = true, AILevel = 0 });

        ChangeMap(GameModeMapsOf(DefaultGameMode()?.UIName).FirstOrDefault());
    }

    private void CheckLoadedPlayerVariableBounds(PlayerInfo pInfo, bool isAIPlayer = false)
    {
        int sideCount = Sides.Count + SlotIndices.RandomSelectorCount;
        if (isAIPlayer)
            sideCount--;

        if (pInfo.SideId < 0 || pInfo.SideId > sideCount)
            pInfo.SideId = 0;

        if (pInfo.ColorId < 0 || pInfo.ColorId > MPColors.Count)
            pInfo.ColorId = 0;

        if (pInfo.TeamId < 0 || pInfo.TeamId >= ProgramConstants.TEAMS.Count + 1 ||
            (!(GameModeMap?.IsCoop ?? false) && (GameModeMap?.ForceNoTeams ?? false)))
        {
            pInfo.TeamId = 0;
        }

        if (pInfo.StartingLocation < 0 || pInfo.StartingLocation > MAX_PLAYER_COUNT ||
            (GameModeMap?.ForceRandomStartLocations ?? false))
        {
            pInfo.StartingLocation = 0;
        }
    }

    /// <summary>Saves the skirmish settings (the XNA lobby's SaveSettings).</summary>
    public void SaveSettings()
    {
        try
        {
            FileInfo settingsFileInfo = SafePath.GetFile(ProgramConstants.GamePath, SETTINGS_PATH);

            // Delete the file so we don't keep potential extra AI players that already exist in the file
            settingsFileInfo.Delete();

            var skirmishSettingsIni = new IniFile(settingsFileInfo.FullName);
            skirmishSettingsIni.SetStringValue("Player", "Info", Players[0].ToString());

            for (int i = 0; i < AIPlayers.Count; i++)
                skirmishSettingsIni.SetStringValue("AIPlayers", i.ToString(), AIPlayers[i].ToString());

            skirmishSettingsIni.SetStringValue("Settings", "Map", GameModeMap?.Map?.SHA1 ?? string.Empty);
            skirmishSettingsIni.SetStringValue("Settings", "GameModeMapFilter", GameModeMap?.GameMode.UIName);

            if (ClientConfiguration.Instance.SaveSkirmishGameOptions)
                Options.WriteSettings(skirmishSettingsIni, "GameOptions");

            skirmishSettingsIni.WriteIniFile();
        }
        catch (Exception ex)
        {
            Logger.Log("Saving skirmish settings failed! Reason: " + ex);
        }
    }

    /// <summary>Why the game can't be launched, or null (the XNA skirmish lobby's CheckGameValidity).</summary>
    public string CheckGameValidity()
    {
        if (GameModeMap == null)
            return "No map selected.";

        int totalPlayerCount = Players.Count(p => p.SideId < SlotIndices.SpectatorSide) + AIPlayers.Count;

        if (GameModeMap.MultiplayerOnly)
        {
            return string.Format("{0} can only be played on CnCNet and LAN.".L10N("Client:Main:GameModeMultiplayerOnly"),
                GameModeMap.ToString());
        }

        if (GameModeMap.EnforceMinPlayers && totalPlayerCount < GameModeMap.MinPlayers)
        {
            return string.Format("{0} cannot be played with less than {1} players.".L10N("Client:Main:GameModeInsufficientPlayers"),
                GameModeMap.ToString(), GameModeMap.MinPlayers);
        }

        if (GameModeMap.EnforceMaxPlayers)
        {
            if (totalPlayerCount > GameModeMap.MaxPlayers)
            {
                return string.Format("{0} cannot be played with more than {1} players.".L10N("Client:Main:TooManyPlayers"),
                    GameModeMap.ToString(), GameModeMap.MaxPlayers);
            }

            List<PlayerInfo> all = Players.Concat(AIPlayers).ToList();
            if (all.Any(pInfo => pInfo.StartingLocation != 0 && all.Count(p => p.StartingLocation == pInfo.StartingLocation) > 1))
                return "Multiple players cannot share the same starting location on the selected map.".L10N("Client:Main:StartLocationOccupied");
        }

        if (GameModeMap.IsCoop && Players[0].SideId == SlotIndices.SpectatorSide)
            return "Co-op missions cannot be spectated. You'll have to show a bit more effort to cheat here.".L10N("Client:Main:CoOpMissionSpectatorPrompt");

        return ExtraOptions.ToPlayerExtraOptions().GetTeamMappingsError();
    }

    /// <summary>
    /// Checks the game, saves the settings, writes spawn.ini and the map file, and starts the game.
    /// </summary>
    /// <returns>Why the game can't be launched, or null if it was started.</returns>
    public string Launch()
    {
        string error = CheckGameValidity();
        if (error != null)
            return error;

        SaveSettings();

        if (LaunchCapture.ForcedSkirmishSeed is int forcedSeed)
        {
            Logger.Log($"Using the random seed {forcedSeed} from {LaunchCapture.SEED_ENVIRONMENT_VARIABLE}.");
            RandomSeed = forcedSeed;
        }

        LaunchCapture launchCapture = LaunchCapture.Begin(GetLaunchCaptureInputs);

        FileInfo spawnerSettingsFile = SafePath.GetFile(ProgramConstants.GamePath, ProgramConstants.SPAWNER_SETTINGS);
        FileInfo spawnMapIniFile = SafePath.GetFile(ProgramConstants.GamePath, ProgramConstants.SPAWNMAP_INI);
        spawnerSettingsFile.Delete();
        DeleteSupplementalMapFiles();
        spawnMapIniFile.Delete();

        LaunchArtifacts launch = GameLaunchBuilder.Build(CreateLaunchRequest(), ProgramConstants.GamePath);

        launch.SpawnIni.WriteIniFile(spawnerSettingsFile.FullName);
        CopySupplementalMapFiles(launch);
        launch.MapIni.WriteIniFile(spawnMapIniFile.FullName);

        launchCapture?.Complete(ProgramConstants.PLAYERNAME);

        gameProcess.GameProcessExited += GameProcess_Exited;
        gameProcess.Start(dialogs);
        return null;
    }

    private void GameProcess_Exited()
    {
        gameProcess.GameProcessExited -= GameProcess_Exited;
        RandomSeed = random.Next();
    }

    private LaunchRequest CreateLaunchRequest()
    {
        var forcedSpawnIniOptions = new List<KeyValuePair<string, string>>();
        List<string> forcedKeys = GameOptionsIni.GetSectionKeys("ForcedSpawnIniOptions");
        if (forcedKeys != null)
        {
            foreach (string key in forcedKeys)
                forcedSpawnIniOptions.Add(new KeyValuePair<string, string>(key, GameOptionsIni.GetStringValue("ForcedSpawnIniOptions", key, string.Empty)));
        }

        return new LaunchRequest
        {
            Players = Players,
            AIPlayers = AIPlayers,
            LocalPlayerName = ProgramConstants.PLAYERNAME,
            PlayerIPAddresses = Players.Select(_ => (string)null).ToList(),
            RandomSeed = RandomSeed,
            GameModeMap = GameModeMap,
            IsMultiplayer = false,
            CheckBoxes = Options.CheckBoxes,
            DropDowns = Options.DropDowns,
            MPColors = MPColors,
            SideCount = Sides.Count,
            RandomSelectors = RandomSelectors,
            RandomSelectorCount = SlotIndices.RandomSelectorCount,
            TeamStartMappings = ExtraOptions.ToPlayerExtraOptions().TeamStartMappings,
            RemoveStartingLocations = false,
            ForcedSpawnIniOptions = forcedSpawnIniOptions,
            SpawnIniAdditions = new IniFile(),
            BroadcastedGameOptionValues = BroadcastedGameOptionValues.Encode(
                Options.CheckBoxes.Where(o => o.BroadcastToLobby).Select(o => o.IsChecked).ToList(),
                Options.DropDowns.Where(o => o.BroadcastToLobby).Select(o => o.Value).ToList()),
        };
    }

    private IDictionary<string, object> GetLaunchCaptureInputs()
    {
        PlayerExtraOptions extraOptions = ExtraOptions.ToPlayerExtraOptions();

        return new Dictionary<string, object>
        {
            ["LobbyType"] = "SkirmishLobby",
            ["LocalPlayerName"] = ProgramConstants.PLAYERNAME,
            ["Seed"] = RandomSeed,
            ["GameMode"] = GameModeMap.GameMode.Name,
            ["GameModeUIName"] = GameModeMap.GameMode.UntranslatedUIName,
            ["MapName"] = GameModeMap.Map.UntranslatedName,
            ["MapSHA1"] = GameModeMap.Map.SHA1,
            ["MapPath"] = GameModeMap.Map.CompleteFilePath,
            ["MapOfficial"] = GameModeMap.Map.Official,
            ["IsCoop"] = GameModeMap.IsCoop,
            ["SideCount"] = Sides.Count,
            ["RandomSelectorCount"] = SlotIndices.RandomSelectorCount,
            ["RemoveStartingLocations"] = false,
            ["Players"] = Players.Select(LaunchCapture.DescribePlayer).ToList(),
            ["AIPlayers"] = AIPlayers.Select(LaunchCapture.DescribePlayer).ToList(),
            ["CheckBoxes"] = Options.CheckBoxes.Select(o => new { o.Name, Checked = o.IsChecked }).ToList(),
            ["DropDowns"] = Options.DropDowns.Select(o => new
            {
                o.Name,
                SelectedIndex = o.Value,
                SelectedItem = o.Value >= 0 && o.Value < o.Definition.Items.Count ? o.Definition.Items[o.Value].Label : null,
            }).ToList(),
            ["PlayerExtraOptions"] = new
            {
                extraOptions.IsForceRandomSides,
                extraOptions.IsForceRandomColors,
                extraOptions.IsForceNoTeams,
                extraOptions.IsForceRandomStarts,
                extraOptions.IsUseTeamStartMappings,
            },
            ["TeamStartMappings"] = extraOptions.TeamStartMappings.Select(m => (object)new { m.Team, m.Start }).ToList(),
        };
    }

    private void DeleteSupplementalMapFiles()
    {
        foreach (string supplementalMapFilename in GameLaunchBuilder.GetSupplementalMapFiles(ProgramConstants.GamePath, "spawnmap").ToList())
        {
            try
            {
                File.Delete(supplementalMapFilename);
            }
            catch (Exception ex)
            {
                string errorMessage = "Unable to delete supplemental map file".L10N("Client:Main:SupplementalFileDeleteError") + $" {supplementalMapFilename}";
                Logger.Log(errorMessage);
                Logger.Log(ex.ToString());
                dialogs.ShowMessage("Error".L10N("Client:Main:Error"), errorMessage);
            }
        }
    }

    private void CopySupplementalMapFiles(LaunchArtifacts launch)
    {
        if (!launch.SupplementalMapFiles.Any())
            return;

        List<string> supplementalFileNames = [];
        foreach (SupplementalMapFile supplementalMapFile in launch.SupplementalMapFiles)
        {
            string file = supplementalMapFile.SourcePath;
            try
            {
                string supplementalFileName = supplementalMapFile.TargetFileName;
                File.Copy(file, SafePath.CombineFilePath(ProgramConstants.GamePath, supplementalFileName), true);
                supplementalFileNames.Add(supplementalFileName);
            }
            catch (Exception ex)
            {
                string errorMessage = "Unable to copy supplemental map file".L10N("Client:Main:SupplementalFileCopyError") + $" {file}";
                Logger.Log(errorMessage);
                Logger.Log(ex.ToString());
                dialogs.ShowMessage("Error".L10N("Client:Main:Error"), errorMessage);
            }
        }

        // Write the supplemental map files to the INI (eventual spawnmap.ini)
        launch.MapIni.SetStringValue("Basic", "SupplementalFiles", string.Join(",", supplementalFileNames));
    }
}
