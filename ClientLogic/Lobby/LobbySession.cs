using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

using ClientCore;
using ClientCore.Extensions;
using ClientCore.Statistics;

using ClientLogic.Launch;
using ClientLogic.Options;
using ClientLogic.Protocol;
using ClientLogic.Statistics;
using ClientLogic.UI;

using CommunityToolkit.Mvvm.ComponentModel;

using DTAClient.Domain.Multiplayer;

using Rampastring.Tools;

namespace ClientLogic.Lobby;

/// <summary>
/// The core of a game lobby without a user interface (skirmish, LAN or CnCNet): the game options from the lobby's
/// layout, the players, the selected map with its forced options and player rules, and writing the launch files.
/// It follows the XNA client's GameLobbyBase step by step.
/// </summary>
public abstract partial class LobbySession : ObservableObject
{
    public const int MAX_PLAYER_COUNT = 8;

    /// <param name="windowName">The layout INI's window section the game options are read from.</param>
    /// <param name="layoutIniName">The layout INI file (without .ini), if not named after the window (the XNA lobby's IniNameOverride).</param>
    protected LobbySession(string windowName, MapLoader mapLoader, GameProcessService gameProcess, IDialogService dialogs, Random random,
        string layoutIniName = null)
    {
        MapLoader = mapLoader;
        GameProcess = gameProcess;
        Dialogs = dialogs;
        Random = random;

        GameOptionsIni = new IniFile(SafePath.CombineFilePath(ProgramConstants.GetBaseResourcePath(), ClientConfiguration.GAME_OPTIONS));

        Sides = ClientConfiguration.Instance.Sides.Split(',');
        LoadRandomSelectors();
        SlotIndices = new SlotIndexMapper(Sides.Count, RandomSelectors.Count + 1);
        MPColors = MultiplayerColor.LoadColors();

        foreach (GameOptionDefinition definition in GameOptionCatalog.Load(new CCIniFile(FindLayoutIni(layoutIniName ?? windowName)), windowName))
            Options.Add(new GameOption(definition, definition.DefaultValue));

        // As the XNA lobby does from its Initialize on: record the host's choices
        Options.OptionChanged += (_, _) => OnGameOptionChanged();

        Slots = new PlayerSlotsState(Players, AIPlayers);
        LobbyState = new GameLobbyState(Options, Slots, ExtraOptions);
        RandomSeed = random.Next();
    }

    protected MapLoader MapLoader { get; }

    protected GameProcessService GameProcess { get; }

    protected IDialogService Dialogs { get; }

    protected Random Random { get; }

    public IniFile GameOptionsIni { get; }

    public GameOptionSet Options { get; } = new();

    public List<PlayerInfo> Players { get; } = [];

    public List<PlayerInfo> AIPlayers { get; } = [];

    public PlayerSlotsState Slots { get; }

    public PlayerExtraOptionsState ExtraOptions { get; } = new();

    /// <summary>
    /// Whether the lobby shows the extra player options (the theme has btnPlayerExtraOptionsOpen). Only then does
    /// the XNA lobby react to extra option changes and apply the host's extra options.
    /// </summary>
    public bool HasExtraOptionsPanel { get; private set; }

    /// <summary>The lobby shows the extra player options panel: extra option changes now apply to the players.</summary>
    public void EnableExtraOptionsPanel()
    {
        if (HasExtraOptionsPanel)
            return;

        HasExtraOptionsPanel = true;
        ExtraOptions.Changed += (_, _) => OnExtraOptionsChanged();
    }

    /// <summary>
    /// The extra player options changed (GameLobbyBase.PlayerExtraOptions_OptionsChanged): forced random sides,
    /// colours and starts and forced no teams reset those choices of every player and AI.
    /// </summary>
    protected virtual void OnExtraOptionsChanged()
    {
        PlayerExtraOptions extraOptions = ExtraOptions.ToPlayerExtraOptions();

        foreach (PlayerInfo pInfo in Players.Concat(AIPlayers))
        {
            if (extraOptions.IsForceRandomSides)
                pInfo.SideId = 0;

            if (extraOptions.IsForceNoTeams)
                pInfo.TeamId = 0;

            if (extraOptions.IsForceRandomColors)
                pInfo.ColorId = 0;

            if (extraOptions.IsForceRandomStarts)
                pInfo.StartingLocation = 0;
        }

        RaiseChanged();
    }

    public GameLobbyState LobbyState { get; }

    /// <summary>The real sides (without the random selectors and Spectator).</summary>
    public IReadOnlyList<string> Sides { get; }

    /// <summary>The custom random selectors: name and real sides.</summary>
    public List<string> RandomSelectorNames { get; } = [];

    public List<int[]> RandomSelectors { get; } = [];

    public SlotIndexMapper SlotIndices { get; }

    public List<MultiplayerColor> MPColors { get; }

    public int RandomSeed { get; protected set; }

    public GameModeMap GameModeMap => LobbyState.GameModeMap;

    public IEnumerable<GameModeMap> GameModeMaps => MapLoader.GameModeMaps;

    /// <summary>Raised when the players, map or options change, so views can refresh.</summary>
    public event EventHandler Changed;

    private MatchStatistics matchStatistics;

    protected void RaiseChanged() => Changed?.Invoke(this, EventArgs.Empty);

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

    /// <summary>The maps of the game mode with this display name, in the map list's order.</summary>
    public List<GameModeMap> GameModeMapsOf(string gameModeUIName) =>
        MapLoader.GameModeMaps.Where(gmm => gmm.GameMode.UIName == gameModeUIName).ToList();

    /// <summary>
    /// Selects a map: applies the forced options of the game mode and map and the map's player rules, as the XNA
    /// lobby's ChangeMap does.
    /// </summary>
    public virtual void ChangeMap(GameModeMap gameModeMap)
    {
        Logger.Log(GetType().Name + ": map " + (gameModeMap == null ? "none" : gameModeMap.Map.SHA1 + " " + gameModeMap.Map.UntranslatedName));
        LobbyState.GameModeMap = gameModeMap;

        if (gameModeMap == null)
        {
            OnGameOptionChanged();
            RaiseChanged();
            return;
        }

        Options.BeginUpdate();
        Options.ApplyGameModeMap(gameModeMap.GameMode, gameModeMap.Map);

        MapSlotRules mapRules = MapSlotRules.FromGameModeMap(gameModeMap);
        PlayerSlotRules.NormaliseForMap(Players, AIPlayers, mapRules, MPColors.Count);
        ExtraOptions.SetTeamOptionsAllowed(!mapRules.HasCoopInfo);

        // The XNA lobby calls OnGameOptionChanged inside the batch, so a map change always sends the options
        OnGameOptionChanged();
        NormalisePlayers();
        Options.EndUpdate();

        RaiseChanged();
    }

    /// <summary>
    /// Called after a game option changed (and once per map change): applies the side rules. Multiplayer lobbies also
    /// send the options.
    /// </summary>
    protected virtual void OnGameOptionChanged() => NormalisePlayers();

    /// <summary>Numbers the players by row and applies the side rules (the XNA lobby's refresh).</summary>
    public void NormalisePlayers()
    {
        if (Players.Count + AIPlayers.Count > MAX_PLAYER_COUNT)
            return;

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

    /// <summary>Applies a change the local player made to a player row (see <see cref="PlayerSlotsState.ApplyChange"/>).</summary>
    /// <summary>
    /// The map preview's start menu (the host or the skirmish player): puts the player on a row (humans first, then
    /// AIs) on a start; with EnforceMaxPlayers whoever was there moves to a random start.
    /// </summary>
    public void AssignStartFromMapPreview(int row, int start)
    {
        if (GameModeMap != null && Slots.AssignStart(row, start, onePlayerPerStart: GameModeMap.EnforceMaxPlayers))
            OnStartsChangedFromMapPreview();
    }

    /// <summary>The map preview's right click (the host or the skirmish player): moves everyone on a start to a random start.</summary>
    public void ClearStartFromMapPreview(int start)
    {
        Slots.ClearStart(start);
        OnStartsChangedFromMapPreview();
    }

    /// <summary>Called after starts were changed from the map preview.</summary>
    protected virtual void OnStartsChangedFromMapPreview() => RaiseChanged();

    /// <summary>
    /// A player picked their own start in the map preview (MapPreviewBox.LocalStartingLocationSelected): as picking
    /// it in their start drop-down; spectators can't. With EnforceMaxPlayers an occupied start can't be picked.
    /// </summary>
    /// <param name="start">The start (1-based), or 0 to go back to a random start.</param>
    public void SelectLocalStartFromMapPreview(int start)
    {
        PlayerInfo me = Players.Find(p => p.Name == ProgramConstants.PLAYERNAME);
        if (me == null || me.SideId == SlotIndices.SpectatorSide || me.StartingLocation == start)
            return;

        if (start > 0 && GameModeMap != null && GameModeMap.EnforceMaxPlayers &&
            Players.Concat(AIPlayers).Any(p => p.StartingLocation == start))
        {
            return;
        }

        ChangeSlot(Players.IndexOf(me), SlotField.Start, start);
    }

    public virtual SlotChangeResult ChangeSlot(int row, SlotField field, int index)
    {
        SlotChangeResult result = Slots.ApplyChange(row, field, index, SlotIndices, isCoop: GameModeMap != null && GameModeMap.IsCoop);
        NormalisePlayers();
        RaiseChanged();
        return result;
    }

    /// <summary>The packed values of the options sent in the CnCNet game list (<see cref="BroadcastedGameOptionValues"/>).</summary>
    public string GetPackedGameOptionValues() => BroadcastedGameOptionValues.Encode(
        Options.CheckBoxes.Where(o => o.BroadcastToLobby).Select(o => o.IsChecked).ToList(),
        Options.DropDowns.Where(o => o.BroadcastToLobby).Select(o => o.Value).ToList());

    /// <summary>The spawn.ini additions of this lobby type (the XNA lobby's WriteSpawnIniAdditions).</summary>
    protected virtual void WriteSpawnIniAdditions(IniFile iniFile)
    {
    }

    protected abstract bool IsMultiplayer { get; }

    /// <summary>Whether this is a LAN or CnCNet lobby (not skirmish).</summary>
    public bool IsMultiplayerLobby => IsMultiplayer;

    /// <summary>The game ID the match is recorded under (0 for skirmish, as the XNA lobby).</summary>
    protected virtual int StatisticsGameId => 0;

    /// <summary>No game option denies scoring (a match can then earn a rank).</summary>
    public bool AllOptionsAllowScoring =>
        Options.CheckBoxes.All(o => o.AllowScoring) && Options.DropDowns.All(o => o.AllowScoring);

    /// <summary>The rank (stars on the launch button) the current setup can earn: a <see cref="LobbyStatistics"/> RANK_ value.</summary>
    public int Rank => LobbyStatistics.GetRank(IsMultiplayer, GameModeMap, AllOptionsAllowScoring, Players, AIPlayers,
        ProgramConstants.PLAYERNAME, SlotIndices.SpectatorSide);

    /// <summary>The map list's rank icon for a map (an index into <see cref="LobbyStatistics.RankTextureNames"/>).</summary>
    public int MapListRankIndex(GameModeMap gameModeMap) =>
        LobbyStatistics.GetMapListRankIndex(StatisticsManager.Instance, gameModeMap, IsMultiplayer);

    public bool RemoveStartingLocations { get; protected set; }

    /// <summary>The IP address the launch uses for a human player; null for the local player.</summary>
    protected virtual string GetIPAddressForPlayer(PlayerInfo player) => "0.0.0.0";

    protected LaunchRequest CreateLaunchRequest()
    {
        var spawnIniAdditions = new IniFile();
        WriteSpawnIniAdditions(spawnIniAdditions);

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
            PlayerIPAddresses = Players.Select(p => p.Name == ProgramConstants.PLAYERNAME ? null : GetIPAddressForPlayer(p)).ToList(),
            RandomSeed = RandomSeed,
            GameModeMap = GameModeMap,
            IsMultiplayer = IsMultiplayer,
            CheckBoxes = Options.CheckBoxes,
            DropDowns = Options.DropDowns,
            MPColors = MPColors,
            SideCount = Sides.Count,
            RandomSelectors = RandomSelectors,
            RandomSelectorCount = SlotIndices.RandomSelectorCount,
            TeamStartMappings = ExtraOptions.ToPlayerExtraOptions().TeamStartMappings,
            RemoveStartingLocations = RemoveStartingLocations,
            ForcedSpawnIniOptions = forcedSpawnIniOptions,
            SpawnIniAdditions = spawnIniAdditions,
            BroadcastedGameOptionValues = GetPackedGameOptionValues(),
        };
    }

    /// <summary>The lobby state recorded in a launch capture (the XNA lobby's AddLaunchCaptureInputs).</summary>
    protected virtual void AddLaunchCaptureInputs(IDictionary<string, object> inputs)
    {
        PlayerExtraOptions extraOptions = ExtraOptions.ToPlayerExtraOptions();

        inputs["LobbyType"] = LobbyTypeName;
        inputs["LocalPlayerName"] = ProgramConstants.PLAYERNAME;
        inputs["Seed"] = RandomSeed;
        inputs["GameMode"] = GameModeMap.GameMode.Name;
        inputs["GameModeUIName"] = GameModeMap.GameMode.UntranslatedUIName;
        inputs["MapName"] = GameModeMap.Map.UntranslatedName;
        inputs["MapSHA1"] = GameModeMap.Map.SHA1;
        inputs["MapPath"] = GameModeMap.Map.CompleteFilePath;
        inputs["MapOfficial"] = GameModeMap.Map.Official;
        inputs["IsCoop"] = GameModeMap.IsCoop;
        inputs["SideCount"] = Sides.Count;
        inputs["RandomSelectorCount"] = SlotIndices.RandomSelectorCount;
        inputs["RemoveStartingLocations"] = RemoveStartingLocations;
        inputs["Players"] = Players.Select(LaunchCapture.DescribePlayer).ToList();
        inputs["AIPlayers"] = AIPlayers.Select(LaunchCapture.DescribePlayer).ToList();
        inputs["CheckBoxes"] = Options.CheckBoxes.Select(o => new { o.Name, Checked = o.IsChecked }).ToList();
        inputs["DropDowns"] = Options.DropDowns.Select(o => new
        {
            o.Name,
            SelectedIndex = o.Value,
            SelectedItem = o.Value >= 0 && o.Value < o.Definition.Items.Count ? o.Definition.Items[o.Value].Label : null,
        }).ToList();
        inputs["PlayerExtraOptions"] = new
        {
            extraOptions.IsForceRandomSides,
            extraOptions.IsForceRandomColors,
            extraOptions.IsForceNoTeams,
            extraOptions.IsForceRandomStarts,
            extraOptions.IsUseTeamStartMappings,
        };
        inputs["TeamStartMappings"] = extraOptions.TeamStartMappings.Select(m => (object)new { m.Team, m.Start }).ToList();
    }

    /// <summary>The XNA lobby class name this session stands in for, as recorded in launch captures.</summary>
    protected abstract string LobbyTypeName { get; }

    /// <summary>
    /// Writes spawn.ini and the map file, starts the match statistics and starts the game (the XNA lobby's StartGame).
    /// </summary>
    protected void StartGame()
    {
        if (!IsMultiplayer && LaunchCapture.ForcedSkirmishSeed is int forcedSeed)
        {
            Logger.Log($"Using the random seed {forcedSeed} from {LaunchCapture.SEED_ENVIRONMENT_VARIABLE}.");
            RandomSeed = forcedSeed;
        }

        LaunchCapture launchCapture = LaunchCapture.Begin(() =>
        {
            var inputs = new Dictionary<string, object>();
            AddLaunchCaptureInputs(inputs);
            return inputs;
        });

        FileInfo spawnerSettingsFile = SafePath.GetFile(ProgramConstants.GamePath, ProgramConstants.SPAWNER_SETTINGS);
        FileInfo spawnMapIniFile = SafePath.GetFile(ProgramConstants.GamePath, ProgramConstants.SPAWNMAP_INI);
        spawnerSettingsFile.Delete();
        DeleteSupplementalMapFiles();
        spawnMapIniFile.Delete();

        LaunchArtifacts launch = GameLaunchBuilder.Build(CreateLaunchRequest(), ProgramConstants.GamePath);

        launch.SpawnIni.WriteIniFile(spawnerSettingsFile.FullName);
        CopySupplementalMapFiles(launch);
        launch.MapIni.WriteIniFile(spawnMapIniFile.FullName);

        matchStatistics = LobbyStatistics.CreateMatchStatistics(StatisticsGameId, GameModeMap, AllOptionsAllowScoring,
            Players, AIPlayers, launch.HouseInfos, MPColors, SlotIndices.SpectatorSide, ProgramConstants.PLAYERNAME);

        launchCapture?.Complete(ProgramConstants.PLAYERNAME);

        GameProcess.GameProcessExited += GameProcess_Exited;
        GameProcess.Start(Dialogs);
    }

    private void GameProcess_Exited()
    {
        GameProcess.GameProcessExited -= GameProcess_Exited;
        OnGameProcessExited();
    }

    /// <summary>
    /// Called when the game exits, on the game process's thread; subclasses move the work to the UI thread before
    /// calling this, since it records the match in the statistics.
    /// </summary>
    protected virtual void OnGameProcessExited()
    {
        Logger.Log("GameProcessExited: Parsing statistics.");

        matchStatistics?.ParseStatistics(ProgramConstants.GamePath, ClientConfiguration.Instance.LocalGame, false);

        Logger.Log("GameProcessExited: Adding match to statistics.");

        StatisticsManager.Instance.AddMatchAndSaveDatabase(true, matchStatistics);
        matchStatistics = null;

        RandomSeed = Random.Next();
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
                Dialogs.ShowMessage("Error".L10N("Client:Main:Error"), errorMessage);
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
                Dialogs.ShowMessage("Error".L10N("Client:Main:Error"), errorMessage);
            }
        }

        // Write the supplemental map files to the INI (eventual spawnmap.ini)
        launch.MapIni.SetStringValue("Basic", "SupplementalFiles", string.Join(",", supplementalFileNames));
    }
}
