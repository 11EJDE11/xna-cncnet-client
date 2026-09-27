using System;
using System.Collections.Generic;
using System.IO;

using ClientCore;
using ClientCore.Extensions;
using ClientCore.Statistics;

using ClientLogic.Launch;
using ClientLogic.UI;

using DTAClient.Domain;
using DTAClient.Domain.Multiplayer;
using DTAClient.Online;

using Rampastring.Tools;

namespace ClientLogic.Lobby;

/// <summary>A saved game player's row: the text (with "(Not present)" / "(Not Ready)") and colour.</summary>
/// <param name="IsPresent">The player is in the room (absent players are drawn gray).</param>
public sealed record SavedGamePlayerRow(string Text, ChatColor Color, bool IsPresent);

/// <summary>
/// A room for loading a saved multiplayer game, as the XNA GameLoadingLobbyBase without its user interface: the saved
/// game's players (from spawnSG.ini), the players present and ready, the saved game to load, the chat, and loading
/// the game (spawn.ini from spawnSG.ini). LAN and CnCNet rooms add the transport.
/// </summary>
public abstract class GameLoadingSession
{
    private readonly FileSystemWatcher savedGameWatcher;
    private bool isSettingUp;
    private int uniqueGameId;
    private DateTime gameLoadTime;

    protected GameLoadingSession(GameProcessService gameProcess, IDialogService dialogs, ISoundService sounds, IUiDispatcher uiDispatcher)
    {
        GameProcess = gameProcess;
        Dialogs = dialogs;
        Sounds = sounds;
        UiDispatcher = uiDispatcher;
        MPColors = MultiplayerColor.LoadColors();

        if (SavedGameManager.AreSavedGamesAvailable())
        {
            savedGameWatcher = new FileSystemWatcher(SafePath.CombineDirectoryPath(ProgramConstants.GamePath, "Saved Games"), "*.NET");
            savedGameWatcher.EnableRaisingEvents = false;
            savedGameWatcher.Created += SavedGameWatcher_Event;
            savedGameWatcher.Changed += SavedGameWatcher_Event;
        }
    }

    protected GameProcessService GameProcess { get; }

    protected IDialogService Dialogs { get; }

    protected ISoundService Sounds { get; }

    protected IUiDispatcher UiDispatcher { get; }

    protected List<MultiplayerColor> MPColors { get; }

    /// <summary>Discord rich presence (the front end sets it; null for none).</summary>
    public DTAClient.Domain.DiscordHandler DiscordHandler { get; set; }

    /// <summary>The players of the saved game.</summary>
    public List<SavedGamePlayer> SGPlayers { get; } = [];

    /// <summary>The players in the room.</summary>
    public List<PlayerInfo> Players { get; } = [];

    public bool IsHost { get; private set; }

    /// <summary>The saved games' timestamps, most recent first (the saved game drop-down).</summary>
    public List<string> SavedGames { get; } = [];

    /// <summary>The saved game to load (an index into <see cref="SavedGames"/>).</summary>
    public int SelectedSavedGameIndex { get; private set; } = -1;

    /// <summary>The saved game's map name (UIMapName, as broadcast).</summary>
    public string MapName { get; private set; } = string.Empty;

    /// <summary>The map name to show (translated).</summary>
    public string MapNameText { get; private set; } = string.Empty;

    /// <summary>The saved game's game mode (UIGameMode, as broadcast).</summary>
    public string GameMode { get; private set; } = string.Empty;

    /// <summary>The game mode to show (translated).</summary>
    public string GameModeText { get; private set; } = string.Empty;

    protected string SavedMapSHA1 { get; private set; } = string.Empty;

    protected string SavedBroadcastOptionValues { get; private set; } = string.Empty;

    /// <summary>The Load Game button's text: "Load Game" for the host, "I'm Ready" for players.</summary>
    public string LoadButtonText => IsHost ? "Load Game".L10N("Client:Main:ButtonLoadGame") : "I'm Ready".L10N("Client:Main:ButtonGetReady");

    /// <summary>Something shown changed.</summary>
    public event EventHandler Changed;

    /// <summary>A chat message or notice.</summary>
    public event EventHandler<ChatMessage> MessageAdded;

    /// <summary>The room was left (GameLeft).</summary>
    public event EventHandler Left;

    protected void RaiseChanged() => Changed?.Invoke(this, EventArgs.Empty);

    protected void AddMessage(ChatMessage message) => MessageAdded?.Invoke(this, message);

    public void AddNotice(string message) => AddNotice(message, ChatColor.White);

    public virtual void AddNotice(string message, ChatColor color) => AddMessage(new ChatMessage(color, message));

    /// <summary>The rows of the saved game's players (CopyPlayerDataToUI).</summary>
    public List<SavedGamePlayerRow> PlayerRows()
    {
        var rows = new List<SavedGamePlayerRow>();

        foreach (SavedGamePlayer sgPlayer in SGPlayers)
        {
            PlayerInfo pInfo = Players.Find(p => p.Name == sgPlayer.Name);

            if (pInfo == null)
            {
                rows.Add(new SavedGamePlayerRow(sgPlayer.Name + " " + "(Not present)".L10N("Client:Main:NotPresentSuffix"),
                    ChatColor.Gray, false));
                continue;
            }

            ChatColor color = sgPlayer.ColorIndex > -1
                ? new ChatColor((byte)MPColors[sgPlayer.ColorIndex].R, (byte)MPColors[sgPlayer.ColorIndex].G, (byte)MPColors[sgPlayer.ColorIndex].B)
                : ChatColor.White;
            rows.Add(new SavedGamePlayerRow(pInfo.Ready ? sgPlayer.Name : sgPlayer.Name + " " + "(Not Ready)".L10N("Client:Main:NotReadySuffix"),
                color, true));
        }

        return rows;
    }

    /// <summary>
    /// Reads the saved game (spawnSG.ini and the saves) for a new room (the XNA lobby's Refresh).
    /// </summary>
    protected virtual void Refresh(bool isHost)
    {
        isSettingUp = true;
        IsHost = isHost;

        SGPlayers.Clear();
        Players.Clear();
        SavedGames.Clear();

        var spawnSGIni = new IniFile(SafePath.CombineFilePath(ProgramConstants.GamePath, "Saved Games", "spawnSG.ini"));

        SavedMapSHA1 = spawnSGIni.GetStringValue("Settings", "MapSHA1", string.Empty);
        SavedBroadcastOptionValues = spawnSGIni.GetStringValue("Settings", "BroadcastedGameOptionValues", string.Empty);
        MapName = spawnSGIni.GetStringValue("Settings", "UIMapName", string.Empty);

        // The XNA lobby passes the previous game mode as the default text here (a bug); the map's name is meant
        MapNameText = MapName.L10N($"INI:Maps:{spawnSGIni.GetStringValue("Settings", "MapID", string.Empty)}:Description");
        GameMode = spawnSGIni.GetStringValue("Settings", "UIGameMode", string.Empty);
        GameModeText = GameMode.L10N($"INI:GameModes:{GameMode}:UIName");

        uniqueGameId = spawnSGIni.GetIntValue("Settings", "GameID", -1);

        int playerCount = spawnSGIni.GetIntValue("Settings", "PlayerCount", 0);

        SGPlayers.Add(new SavedGamePlayer
        {
            Name = ProgramConstants.PLAYERNAME,
            ColorIndex = MPColors.FindIndex(c => c.GameColorIndex == spawnSGIni.GetIntValue("Settings", "Color", 0)),
        });

        for (int i = 1; i < playerCount; i++)
        {
            string sectionName = "Other" + i;

            SGPlayers.Add(new SavedGamePlayer
            {
                Name = spawnSGIni.GetStringValue(sectionName, "Name", "Unknown player".L10N("Client:Main:UnknownPlayer")),
                ColorIndex = MPColors.FindIndex(c => c.GameColorIndex == spawnSGIni.GetIntValue(sectionName, "Color", 0)),
            });
        }

        List<string> timestamps = SavedGameManager.GetSaveGameTimestamps();
        timestamps.Reverse(); // Most recent saved game first
        SavedGames.AddRange(timestamps);

        SelectedSavedGameIndex = SavedGames.Count > 0 ? 0 : -1;

        isSettingUp = false;
        RaiseChanged();
    }

    /// <summary>
    /// The host picks a saved game (DdSavedGame_SelectedIndexChanged): the players' ready statuses are cleared and the
    /// choice sent.
    /// </summary>
    public void SelectSavedGame(int index)
    {
        if (!IsHost || index < 0 || index >= SavedGames.Count || index == SelectedSavedGameIndex)
            return;

        SelectedSavedGameIndex = index;

        for (int i = 1; i < Players.Count; i++)
            Players[i].Ready = false;

        if (!isSettingUp)
            BroadcastOptions();

        UpdateDiscordPresence();
        RaiseChanged();
    }

    /// <summary>A player's copy of the host's choice: the saved game at <paramref name="index"/>.</summary>
    protected void SetSavedGameIndex(int index)
    {
        SelectedSavedGameIndex = index;
        RaiseChanged();
    }

    /// <summary>The Load Game / I'm Ready button (BtnLoadGame_LeftClick).</summary>
    public void LoadButtonClicked()
    {
        if (!IsHost)
        {
            RequestReadyStatus();
            return;
        }

        if (Players.Find(p => !p.Ready) != null)
        {
            GetReadyNotification();
            return;
        }

        if (Players.Count != SGPlayers.Count)
        {
            NotAllPresentNotification();
            return;
        }

        HostStartGame();
    }

    public abstract void SendChatMessage(string message);

    public virtual void Leave()
    {
        Left?.Invoke(this, EventArgs.Empty);
        ResetDiscordPresence();
    }

    protected abstract void RequestReadyStatus();

    protected abstract void HostStartGame();

    /// <summary>Sends the ready statuses and the selected saved game to the players (host).</summary>
    protected abstract void BroadcastOptions();

    protected virtual void GetReadyNotification()
    {
        AddNotice("The game host wants to load the game but cannot because not all players are ready!".L10N("Client:Main:GetReadyPlease"));

        if (!IsHost && !(Players.Find(p => p.Name == ProgramConstants.PLAYERNAME)?.Ready ?? true))
            Sounds.Play(LobbySound.GetReady);
    }

    protected virtual void NotAllPresentNotification() =>
        AddNotice("You cannot load the game before all players are present.".L10N("Client:Main:NotAllPresent"));

    private void SavedGameWatcher_Event(object sender, FileSystemEventArgs e) => UiDispatcher.Post(() =>
    {
        Logger.Log("FSW Event: " + e.FullPath);

        if (Path.GetFileName(e.FullPath) == "SAVEGAME.NET")
            SavedGameManager.RenameSavedGame();
    });

    /// <summary>Writes spawn.ini from spawnSG.ini for the selected save and the players, and starts the game (LoadGame).</summary>
    protected void LoadGame()
    {
        FileInfo spawnFileInfo = SafePath.GetFile(ProgramConstants.GamePath, "spawn.ini");

        spawnFileInfo.Delete();

        File.Copy(SafePath.CombineFilePath(ProgramConstants.GamePath, "Saved Games", "spawnSG.ini"), spawnFileInfo.FullName);

        var spawnIni = new IniFile(spawnFileInfo.FullName);

        int sgIndex = (SavedGames.Count - 1) - SelectedSavedGameIndex;

        spawnIni.SetStringValue("Settings", "SaveGameName", string.Format("SVGM_{0}.NET", sgIndex.ToString("D3")));
        spawnIni.SetBooleanValue("Settings", "LoadSaveGame", true);

        PlayerInfo localPlayer = Players.Find(p => p.Name == ProgramConstants.PLAYERNAME);

        if (localPlayer == null)
            return;

        spawnIni.SetIntValue("Settings", "Port", localPlayer.Port);

        for (int i = 1; i < Players.Count; i++)
        {
            string otherName = spawnIni.GetStringValue("Other" + i, "Name", string.Empty);

            if (string.IsNullOrEmpty(otherName))
                continue;

            PlayerInfo otherPlayer = Players.Find(p => p.Name == otherName);

            if (otherPlayer == null)
                continue;

            spawnIni.SetStringValue("Other" + i, "Ip", otherPlayer.IPAddress);
            spawnIni.SetIntValue("Other" + i, "Port", otherPlayer.Port);
        }

        WriteSpawnIniAdditions(spawnIni);
        spawnIni.WriteIniFile();

        FileInfo spawnMapFileInfo = SafePath.GetFile(ProgramConstants.GamePath, "spawnmap.ini");
        spawnMapFileInfo.Delete();
        using (var spawnMapStreamWriter = new StreamWriter(spawnMapFileInfo.FullName))
        {
            spawnMapStreamWriter.WriteLine("[Map]");
            spawnMapStreamWriter.WriteLine("Size=0,0,50,50");
            spawnMapStreamWriter.WriteLine("LocalSize=0,0,50,50");
            spawnMapStreamWriter.WriteLine();
        }

        gameLoadTime = DateTime.Now;

        GameProcess.GameProcessExited += GameProcess_Exited;
        GameProcess.Start(Dialogs);

        if (savedGameWatcher != null)
            savedGameWatcher.EnableRaisingEvents = true;

        UpdateDiscordPresence(true);
    }

    private void GameProcess_Exited() => UiDispatcher.Post(HandleGameProcessExited);

    /// <summary>The game exited: the match's statistics are updated with the loaded game's results.</summary>
    protected virtual void HandleGameProcessExited()
    {
        if (savedGameWatcher != null)
            savedGameWatcher.EnableRaisingEvents = false;

        GameProcess.GameProcessExited -= GameProcess_Exited;

        MatchStatistics matchStatistics = StatisticsManager.Instance.GetMatchWithGameID(uniqueGameId);

        if (matchStatistics != null)
        {
            int newLength = matchStatistics.LengthInSeconds + (int)(DateTime.Now - gameLoadTime).TotalSeconds;

            matchStatistics.ParseStatistics(ProgramConstants.GamePath, ClientConfiguration.Instance.LocalGame, true);

            matchStatistics.LengthInSeconds = newLength;

            StatisticsManager.Instance.SaveDatabase();
        }

        UpdateDiscordPresence(true);
    }

    protected virtual void WriteSpawnIniAdditions(IniFile spawnIni)
    {
        // Do nothing by default
    }

    protected virtual void UpdateDiscordPresence(bool resetTimer = false)
    {
    }

    protected void ResetDiscordPresence() => DiscordHandler?.UpdatePresence();
}
