using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

using ClientCore;
using ClientCore.Extensions;

using ClientLogic.Launch;
using ClientLogic.Lobby;
using ClientLogic.Options;
using ClientLogic.UI;

using DTAClient.Domain.Multiplayer;

using Rampastring.Tools;

namespace ClientLogic.Skirmish;

/// <summary>
/// A skirmish lobby without a user interface: the game options (from the theme's SkirmishLobby layout), the players,
/// the selected map, the saved skirmish settings and the launch. It follows the XNA client's skirmish lobby step by
/// step, so both launch the same game from the same saved settings.
/// </summary>
public sealed class SkirmishSession : LobbySession
{
    public const string WINDOW_NAME = "SkirmishLobby";
    private const string SETTINGS_PATH = "Client/SkirmishSettings.ini";

    private readonly IUiDispatcher uiDispatcher;

    public SkirmishSession(MapLoader mapLoader, GameProcessService gameProcess, IDialogService dialogs, IUiDispatcher uiDispatcher, Random random)
        : base(WINDOW_NAME, mapLoader, gameProcess, dialogs, random)
    {
        this.uiDispatcher = uiDispatcher;
    }

    protected override void PostToUi(Action action) => uiDispatcher.Post(action);

    private bool open;

    /// <summary>The lobby is shown (the XNA lobby's Enabled): Discord shows it.</summary>
    public void Opened()
    {
        open = true;
        UpdateDiscordPresence(true);
    }

    /// <summary>The lobby was left: Discord shows the client again.</summary>
    public void Closed()
    {
        open = false;
        ResetDiscordPresence();
    }

    protected override void UpdateDiscordPresence(bool resetTimer = false)
    {
        if (!open || GameModeMap?.Map == null || GameModeMap.GameMode == null)
            return;

        if (!Players.Any(p => p.Name == ProgramConstants.PLAYERNAME))
            return;

        string map = GameModeMap.Map.UntranslatedName;
        string mode = GameModeMap.GameMode.UntranslatedUIName;
        string side = LocalSideName();
        string currentState = ProgramConstants.IsInGame ? "In Game" : "Setting Up";

        SetDiscordPresence(string.Join("|", map, mode, currentState, side), resetTimer,
            discord => discord.UpdatePresence(map, mode, currentState, side, resetTimer));
    }

    /// <summary>The game exited: the match is recorded on the UI thread and the lobby refreshes (its ranks).</summary>
    protected override void OnGameProcessExited() => uiDispatcher.Post(() =>
    {
        base.OnGameProcessExited();
        RaiseChanged();
    });

    protected override bool IsMultiplayer => false;

    protected override string LobbyTypeName => "SkirmishLobby";

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
            List<GameModeMap> candidates = UserINISettings.Instance.SearchAllGameModes.Value ? MapLoader.GameModeMaps.ToList() : filterMaps;
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
        RaiseChanged();
    }

    private GameMode DefaultGameMode() => MapLoader.GameModes.FirstOrDefault(gm => gm.Maps.Count > 0);

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
        StartGame();
        return null;
    }
}
