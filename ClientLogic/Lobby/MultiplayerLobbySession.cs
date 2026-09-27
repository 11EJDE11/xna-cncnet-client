using System;
using System.Collections.Generic;
using System.Linq;

using ClientCore;
using ClientCore.Extensions;
using ClientCore.Statistics;

using ClientLogic.Launch;
using ClientLogic.MapSharing;
using ClientLogic.Protocol;
using ClientLogic.UI;

using DTAClient.Domain.Multiplayer;
using DTAClient.Online;

using Rampastring.Tools;

namespace ClientLogic.Lobby;

/// <summary>
/// The multiplayer part of a game lobby without a user interface, shared by LAN and CnCNet: host and player roles,
/// ready status, room lock, the game options and player options the host sends and the players apply, option
/// requests, dice, launch checks and the launch. It follows the XNA client's MultiplayerGameLobby. Messages go out
/// through <see cref="LobbyState"/>'s session; the transport feeds incoming messages to the Apply/Handle methods.
/// </summary>
public abstract class MultiplayerLobbySession : LobbySession, IMapSharingLobby, IMapSharingTransport, INoticeSink
{
    protected MultiplayerLobbySession(string windowName, MapLoader mapLoader, GameProcessService gameProcess,
        IDialogService dialogs, ISoundService sounds, IUiDispatcher uiDispatcher, Random random, string layoutIniName = null)
        : base(windowName, mapLoader, gameProcess, dialogs, random, layoutIniName)
    {
        Sounds = sounds;
        UiDispatcher = uiDispatcher;
        LobbyState.IsHost = false;
        FrameSendRate = ClientConfiguration.Instance.DefaultFrameSendRate;
        ProtocolVersion = ClientConfiguration.Instance.DefaultProtocolVersion;
        MaxAhead = ClientConfiguration.Instance.DefaultMaxAhead;

        MapSharing = new MapSharingService(uiDispatcher, this, this, this, ClientConfiguration.Instance.LocalGame);
        MapSharing.DownloadFailed += () =>
        {
            MapSharingState = MapSharingPanelState.Failed;
            RaiseChanged();
        };
    }

    /// <summary>Custom maps shared through the CnCNet map database (the rooms start and stop it).</summary>
    public MapSharingService MapSharing { get; }

    /// <summary>The map sharing confirmation panel over the map preview (MapSharingConfirmationPanel).</summary>
    public MapSharingPanelState MapSharingState { get; private set; }

    /// <summary>The name the host's map sharing messages come from.</summary>
    protected abstract string MapSharingHostName { get; }

    string IMapSharingLobby.HostName => MapSharingHostName;

    Map IMapSharingLobby.Map => GameModeMap?.Map;

    Map IMapSharingLobby.FindMap(string sha1)
    {
        foreach (GameMode gm in MapLoader.GameModes)
        {
            Map map = gm.Maps.Find(m => m.SHA1 == sha1);

            if (map != null)
                return map;
        }

        return null;
    }

    /// <summary>Sends a map sharing message to the other players (CnCNet: a CTCP; LAN: to the host or all).</summary>
    public abstract void SendMapSharingMessage(string message);

    void INoticeSink.AddNotice(string message, NoticeSeverity severity) => AddNotice(message, severity switch
    {
        NoticeSeverity.Success => new ChatColor(144, 238, 144),
        NoticeSeverity.Warning => new ChatColor(255, 255, 0),
        NoticeSeverity.Degraded => new ChatColor(255, 165, 0),
        NoticeSeverity.Error => ChatColor.Red,
        _ => ChatColor.White,
    });

    /// <summary>The panel's Download button: downloads the host's map.</summary>
    public void ConfirmMapDownload()
    {
        MapSharingState = MapSharingPanelState.Downloading;
        RaiseChanged();
        MapSharing.DownloadHostMap();
    }

    protected override void HandleMapAdded(Map addedMap)
    {
        // If this is a map we downloaded, select it
        if (!MapSharing.IsExpectedDownload(addedMap.SHA1))
        {
            base.HandleMapAdded(addedMap);
            return;
        }

        AddNotice($"Map {addedMap.Name} loaded successfully.");
        RaiseMapsChanged();

        GameModeMap gameModeMap = GameModeMaps.FirstOrDefault(gmm => gmm.Map.SHA1 == addedMap.SHA1);
        if (gameModeMap != null)
            ChangeMap(gameModeMap);

        MapSharing.OnDownloadedMapInstalled(addedMap.SHA1);
    }

    protected override void PostToUi(Action action) => UiDispatcher.Post(action);

    protected ISoundService Sounds { get; }

    protected IUiDispatcher UiDispatcher { get; }

    protected override bool IsMultiplayer => true;

    public bool IsHost => LobbyState.IsHost;

    public bool Locked => LobbyState.Locked;

    public ILobbySession Session
    {
        get => LobbyState.Session;
        protected set => LobbyState.Session = value;
    }

    public int FrameSendRate { get; protected set; }

    public int MaxAhead { get; protected set; }

    public int ProtocolVersion { get; protected set; }

    public int UniqueGameID { get; protected set; }

    protected override int StatisticsGameId => UniqueGameID;

    /// <summary>The last map change was to no map (or an unknown one).</summary>
    public bool LastMapChangeWasInvalid { get; protected set; }

    /// <summary>The local player wants to be ready again automatically after each change.</summary>
    public bool AutoReady { get; set; }

    /// <summary>A chat message or notice to show in the lobby's chat.</summary>
    public event EventHandler<ChatMessage> MessageAdded;

    /// <summary>The local player left the room; the argument is why, or null.</summary>
    public event EventHandler<string> Left;

    public void AddNotice(string message) => AddNotice(message, ChatColor.White);

    public void AddNotice(string message, ChatColor color) => MessageAdded?.Invoke(this, new ChatMessage(color, message));

    protected void AddChatMessage(ChatMessage message) => MessageAdded?.Invoke(this, message);

    protected void RaiseLeft(string message) => Left?.Invoke(this, message);

    public PlayerInfo FindLocalPlayer() => Players.Find(p => p.Name == ProgramConstants.PLAYERNAME);

    /// <summary>
    /// Sets the room up for the host or a player (the XNA lobby's Refresh): unlocks it, restores the host's own
    /// options, generates the game ID and selects the default map.
    /// </summary>
    protected void SetUp(bool isHost)
    {
        LobbyState.IsHost = isHost;
        LobbyState.Locked = false;
        NormalisePlayers();

        if (isHost)
        {
            // Restore the local player's own choices, and send them once
            if (Options.RestoreUserValues())
                OnGameOptionChanged();

            GenerateGameID();
        }

        ChangeMap(MapLoader.GameModeMaps.FirstOrDefault(gmm => gmm.GameMode.Maps.Count > 0));
        RaiseChanged();
    }

    private void GenerateGameID()
    {
        int i = 0;

        while (i < 20)
        {
            string s = DateTime.Now.Day.ToString() +
                DateTime.Now.Month.ToString() +
                DateTime.Now.Hour.ToString() +
                DateTime.Now.Minute.ToString();

            UniqueGameID = int.Parse(i.ToString() + s);

            if (StatisticsManager.Instance.GetMatchWithGameID(UniqueGameID) == null)
                break;

            i++;
        }
    }

    public override void ChangeMap(GameModeMap gameModeMap)
    {
        MapSharingState = MapSharingPanelState.Hidden;
        base.ChangeMap(gameModeMap);

        bool resetAutoReady = gameModeMap?.GameMode == null || gameModeMap?.Map == null;

        LobbyState.ClearReadyStatuses(resetAutoReady);

        if ((LastMapChangeWasInvalid || resetAutoReady) && AutoReady)
            RequestReady();

        LastMapChangeWasInvalid = resetAutoReady;
        RaiseChanged();
    }

    protected override void OnGameOptionChanged()
    {
        base.OnGameOptionChanged();
        LobbyState.ClearReadyStatuses();

        if (IsHost)
            Session?.SendGameOptions(CreateGameOptionsMessage());
    }

    /// <summary>The game options message the host sends (the transport can add its own fields).</summary>
    protected virtual GameOptionsMessage CreateGameOptionsMessage() => new()
    {
        CheckBoxValues = Options.CheckBoxes.Select(o => o.IsChecked).ToList(),
        DropDownIndices = Options.DropDowns.Select(o => o.Value).ToList(),
        IsMapOfficial = GameModeMap?.Map?.Official ?? false,
        MapSHA1 = GameModeMap?.Map?.SHA1 ?? string.Empty,
        GameModeName = GameModeMap?.GameMode?.Name ?? string.Empty,
        FrameSendRate = FrameSendRate,
        MaxAhead = MaxAhead,
        ProtocolVersion = ProtocolVersion,
        RandomSeed = RandomSeed,
        RemoveStartingLocations = RemoveStartingLocations,
        MapName = GameModeMap?.Map?.UntranslatedName ?? string.Empty,
    };

    /// <summary>Host: sends the players' options.</summary>
    public abstract void BroadcastPlayerOptions();

    /// <summary>Host: sends the extra player options.</summary>
    public void BroadcastPlayerExtraOptions()
    {
        if (IsHost)
            Session?.SendPlayerExtraOptions(ExtraOptions.ToPlayerExtraOptions());
    }

    /// <summary>
    /// The local player changed a player row: the host applies it and sends the players' options; a player asks the
    /// host to change their own options.
    /// </summary>
    /// <summary>The host changed starts in the map preview: the others must ready up again and get the players' options.</summary>
    protected override void OnStartsChangedFromMapPreview()
    {
        LobbyState.ClearReadyStatuses();
        RaiseChanged();
        BroadcastPlayerOptions();
    }

    public override SlotChangeResult ChangeSlot(int row, SlotField field, int index)
    {
        if (IsHost)
        {
            SlotChangeResult result = base.ChangeSlot(row, field, index);
            if (result.ClearsReady)
                LobbyState.ClearReadyStatuses();

            if (result.Command == SlotCommand.Kick)
                KickPlayer(row);
            else if (result.Command == SlotCommand.Ban)
                BanPlayer(row);

            BroadcastPlayerOptions();
            return result;
        }

        PlayerInfo me = FindLocalPlayer();
        if (me == null)
            return new SlotChangeResult(false);

        int side = me.SideId, color = me.ColorId, start = me.StartingLocation, team = me.TeamId;
        if (row == Players.IndexOf(me))
        {
            switch (field)
            {
                case SlotField.Side:
                    side = index;
                    break;
                case SlotField.Color:
                    color = index;
                    break;
                case SlotField.Start:
                    start = index;
                    break;
                case SlotField.Team:
                    team = index;
                    break;
            }
        }

        Session?.RequestPlayerOptions(new PackedPlayerOptions(side, color, start, team));
        return new SlotChangeResult(false);
    }

    /// <summary>Host: kicks a player picked from a name drop-down (LAN rooms can't).</summary>
    protected virtual void KickPlayer(int playerIndex)
    {
    }

    /// <summary>Host: bans a player picked from a name drop-down (LAN rooms can't).</summary>
    protected virtual void BanPlayer(int playerIndex)
    {
    }

    /// <summary>Host: changes the map and sends it (through the options message).</summary>
    public void HostChangeMap(GameModeMap gameModeMap)
    {
        if (IsHost)
            ChangeMap(gameModeMap);
    }

    /// <summary>Asks the host to change the local player's ready state.</summary>
    public abstract void RequestReady();

    /// <summary>Sends a chat message.</summary>
    public void SendChatMessage(string message) => Session?.SendChatMessage(message);

    /// <summary>The chat box commands this lobby supports.</summary>
    public IReadOnlyList<ChatBoxCommand> ChatCommands => chatCommands ??= CreateChatCommands();

    private List<ChatBoxCommand> chatCommands;

    /// <summary>
    /// The chat commands (the XNA lobby's list, without the map list, custom map and preset commands, which need the
    /// front end).
    /// </summary>
    protected virtual List<ChatBoxCommand> CreateChatCommands() =>
    [
        new ChatBoxCommand("FRAMESENDRATE", string.Format("Change order lag / FrameSendRate (default {0}) (game host only)".L10N("Client:Main:ChatboxCommandFrameSendRateHelpV2"), ClientConfiguration.Instance.DefaultFrameSendRate), true,
            SetFrameSendRate),
        new ChatBoxCommand("MAXAHEAD", string.Format("Change MaxAhead (default {0}) (game host only)".L10N("Client:Main:ChatboxCommandMaxAheadHelpV2"), ClientConfiguration.Instance.DefaultMaxAhead), true,
            SetMaxAhead),
        new ChatBoxCommand("PROTOCOLVERSION", string.Format("Change ProtocolVersion (default {0}) (game host only)".L10N("Client:Main:ChatboxCommandProtocolVersionHelpV2"), ClientConfiguration.Instance.DefaultProtocolVersion), true,
            SetProtocolVersion),
        new ChatBoxCommand("LOADMAP", "Load a custom map with given filename from /Maps/Custom/ folder.".L10N("Client:Main:ChatboxCommandLoadMapHelp"), true, LoadCustomMap),
        new ChatBoxCommand("RANDOMSTARTS", "Enables completely random starting locations (Tiberian Sun based games only).".L10N("Client:Main:ChatboxCommandRandomStartsHelp"), true,
            SetStartingLocationClearance),
        new ChatBoxCommand("ROLL", "Roll dice, for example /roll 3d6".L10N("Client:Main:ChatboxCommandRollHelp"), false, RollDice),
        new ChatBoxCommand("SAVEOPTIONS", "Save game option preset so it can be loaded later".L10N("Client:Main:ChatboxCommandSaveOptionsHelp"), false, HandleGameOptionPresetSaveCommand),
        new ChatBoxCommand("LOADOPTIONS", "Load game option preset".L10N("Client:Main:ChatboxCommandLoadOptionsHelp"), true, HandleGameOptionPresetLoadCommand),
    ];

    protected override bool CanChangeGameOptions => IsHost;

    /// <summary>The /LOADMAP command: loads a map from Maps/Custom.</summary>
    private void LoadCustomMap(string mapName)
    {
        Map map = MapLoader.LoadCustomMap($"Maps/Custom/{mapName}", out string resultMessage);
        if (map != null)
        {
            AddNotice(resultMessage);
            RaiseMapsChanged();
        }
        else
        {
            AddNotice(resultMessage, ChatColor.Red);
        }
    }

    public void HandleGameOptionPresetSaveCommand(string presetName)
    {
        string error = SaveGameOptionPreset(presetName);
        if (!string.IsNullOrEmpty(error))
            AddNotice(error);
    }

    public void HandleGameOptionPresetLoadCommand(string presetName)
    {
        if (LoadGameOptionPreset(presetName))
            AddNotice("Game option preset loaded succesfully.".L10N("Client:Main:PresetLoaded"));
        else
            AddNotice(string.Format("Preset {0} not found!".L10N("Client:Main:PresetNotFound"), presetName));
    }

    /// <summary>
    /// Handles the chat box input: a /command, or a chat message.
    /// </summary>
    public void SubmitChatInput(string text)
    {
        if (string.IsNullOrEmpty(text))
            return;

        if (text.StartsWith("/"))
        {
            ChatCommandResult result = ChatBoxCommands.Execute(text, ChatCommands, IsHost);
            if (result.Outcome == ChatCommandOutcome.HostOnly)
                AddNotice(result.Notice);
            else if (result.Outcome == ChatCommandOutcome.Unknown)
                Dialogs.ShowMessage(ChatBoxCommands.HelpTitle, ChatBoxCommands.HelpText(ChatCommands));

            return;
        }

        SendChatMessage(text);
    }

    private void SetFrameSendRate(string value)
    {
        if (!int.TryParse(value, out int intValue))
        {
            AddNotice("Command syntax: /FrameSendRate <number>".L10N("Client:Main:ChatboxCommandFrameSendRateSyntax"));
            return;
        }

        FrameSendRate = intValue;
        AddNotice(string.Format("FrameSendRate has been changed to {0}".L10N("Client:Main:FrameSendRateChanged"), intValue));

        OnGameOptionChanged();
        LobbyState.ClearReadyStatuses();
        RaiseChanged();
    }

    private void SetMaxAhead(string value)
    {
        if (!int.TryParse(value, out int intValue))
        {
            AddNotice("Command syntax: /MaxAhead <number>".L10N("Client:Main:ChatboxCommandMaxAheadSyntax"));
            return;
        }

        MaxAhead = intValue;
        AddNotice(string.Format("MaxAhead has been changed to {0}".L10N("Client:Main:MaxAheadChanged"), intValue));

        OnGameOptionChanged();
        LobbyState.ClearReadyStatuses();
        RaiseChanged();
    }

    private void SetProtocolVersion(string value)
    {
        if (!int.TryParse(value, out int intValue))
        {
            AddNotice("Command syntax: /ProtocolVersion <number>.".L10N("Client:Main:ChatboxCommandProtocolVersionSyntax"));
            return;
        }

        if (!(intValue == 0 || intValue == 2))
        {
            AddNotice("ProtocolVersion only allows values 0 and 2.".L10N("Client:Main:ChatboxCommandProtocolVersionInvalid"));
            return;
        }

        ProtocolVersion = intValue;
        AddNotice(string.Format("ProtocolVersion has been changed to {0}".L10N("Client:Main:ProtocolVersionChanged"), intValue));

        OnGameOptionChanged();
        LobbyState.ClearReadyStatuses();
        RaiseChanged();
    }

    private void SetStartingLocationClearance(string value)
    {
        bool removeStartingLocations = Conversions.BooleanFromString(value, RemoveStartingLocations);

        if (removeStartingLocations != RemoveStartingLocations)
        {
            RemoveStartingLocations = removeStartingLocations;
            AddNotice(GameOptionNotices.RemoveStartingLocationsChanged(RemoveStartingLocations));
        }

        OnGameOptionChanged();
        LobbyState.ClearReadyStatuses();
        RaiseChanged();
    }

    /// <summary>The /roll command; the result comes back to everyone (and is shown by the transport).</summary>
    public void RollDice(string spec)
    {
        int[] results = LobbyState.RollDice(spec, Random, out int dieSides, out string error);
        if (results == null)
            AddNotice(error);
        else
            OnLocalDiceRoll(dieSides, results);
    }

    protected virtual void OnLocalDiceRoll(int dieSides, int[] results)
    {
    }

    /// <summary>Shows a dice roll that was received ("sides,result,result...").</summary>
    public void HandleDiceRollResult(string senderName, string result)
    {
        if (DiceRoll.TryParseResult(result, out int dieSides, out int[] results))
            AddNotice(DiceRoll.FormatResult(senderName, dieSides, results));
    }

    /// <summary>Host: accepts or refuses a player's options request.</summary>
    public void HandlePlayerOptionsRequest(string sender, PackedPlayerOptions options)
    {
        if (!IsHost)
            return;

        PlayerInfo pInfo = Players.Find(p => p.Name == sender);
        if (pInfo == null || !IsPlayerOptionsRequestAllowed(options.Side, options.Color, options.Start, options.Team))
            return;

        if (Slots.ApplyOptionsRequest(pInfo, options.Side, options.Color, options.Start, options.Team).ClearsReady)
            LobbyState.ClearReadyStatuses();

        NormalisePlayers();
        RaiseChanged();
        BroadcastPlayerOptions();
    }

    /// <summary>Whether the host accepts a player's options request (the lobby's drop-down rules).</summary>
    protected bool IsPlayerOptionsRequestAllowed(int side, int color, int start, int team)
    {
        SideAvailability humanSides = PlayerSlotRules.ComputeSideAvailability(
            GameLaunchBuilder.GetDisallowedSidesForGroup(Sides.Count, GameModeMap, Options.CheckBoxes, true),
            RandomSelectors, SlotIndices, hasCoopInfo: GameModeMap?.CoopInfo != null);
        bool[] colors = PlayerSlotRules.ComputeColorSelectable(MPColors.Count + 1,
            GameModeMap?.CoopInfo?.DisallowedPlayerColors, MPColors.Count);

        return PlayerSlotRules.IsOptionsRequestAllowed(side, color, start, team, humanSides, SlotIndices, colors,
            s => GameModeMap?.AllowedStartingLocations?.Contains(s) ?? true);
    }

    /// <summary>Host: applies a player's ready request.</summary>
    public void HandleReadyRequest(string sender, int readyState)
    {
        PlayerInfo pInfo = Players.Find(p => p.Name == sender);
        if (pInfo == null)
            return;

        pInfo.Ready = readyState > 0;
        pInfo.AutoReady = readyState > 1;
        RaiseChanged();
        BroadcastPlayerOptions();
    }

    /// <summary>The LAN and CnCNet lobbies also send the extra options (the host) after a change.</summary>
    protected override void OnExtraOptionsChanged()
    {
        base.OnExtraOptionsChanged();
        BroadcastPlayerExtraOptions();
    }

    /// <summary>Player: applies the extra player options the host sent, with notices (only with the panel, as XNA).</summary>
    public void ApplyPlayerExtraOptions(string message)
    {
        if (HasExtraOptionsPanel)
        {
            foreach (string notice in ExtraOptions.ApplyFromHost(PlayerExtraOptions.FromMessage(message)))
                AddNotice(notice);
        }

        RaiseChanged();
    }

    /// <summary>
    /// Called after the map was cleared because the host's map isn't installed
    /// (<see cref="GameOptionsUpdate.MapAction"/> says why).
    /// </summary>
    private void HandleMissingHostMap(GameOptionsUpdate update)
    {
        switch (update.MapAction)
        {
            case GameOptionsMapAction.ClearAndRequestDownload:
                AddNotice("The game host has selected a map that doesn't exist on your installation.".L10N("Client:Main:MapNotExist"));
                MapSharingState = MapSharingPanelState.Request;
                break;
            case GameOptionsMapAction.ClearAndReportMapSharingDisabled:
                MapSharing.ReportMapSharingDisabled();
                break;
            case GameOptionsMapAction.ClearAndReportOfficialMapMissing:
                MapSharing.ReportOfficialMapMissing(update.MapSHA1);
                break;
        }
    }

    /// <summary>
    /// Player: applies the game options the host sent (planned by <see cref="GameOptionsApplier.Plan"/>), in the
    /// XNA lobby's order: settings, map, then options with notices, starting locations and seed.
    /// </summary>
    public void ApplyGameOptionsUpdate(GameOptionsUpdate update)
    {
        MapSharing.SetHostMap(update.MapSHA1, update.MapName);

        FrameSendRate = update.FrameSendRate;
        MaxAhead = update.MaxAhead;
        ProtocolVersion = update.ProtocolVersion;

        foreach (string notice in update.SettingNotices)
            AddNotice(notice);

        switch (update.MapAction)
        {
            case GameOptionsMapAction.None:
                break;
            case GameOptionsMapAction.Change:
                ChangeMap(update.GameModeMap);
                break;
            default:
                ChangeMap(null);
                HandleMissingHostMap(update);
                break;
        }

        for (int i = 0; i < Options.CheckBoxes.Count; i++)
        {
            var option = Options.CheckBoxes[i];
            bool isChecked = update.CheckBoxValues[i];

            if (option.IsChecked != isChecked)
                AddNotice(GameOptionNotices.CheckBoxChanged(option.Definition.Label, isChecked));

            option.Value = isChecked ? 1 : 0;
        }

        for (int i = 0; i < Options.DropDowns.Count; i++)
        {
            var option = Options.DropDowns[i];
            int selectedIndex = update.DropDownIndices[i];

            if (selectedIndex < 0 || selectedIndex >= option.Definition.Items.Count)
                continue;

            if (option.Value != selectedIndex)
                AddNotice(GameOptionNotices.DropDownChanged(option.Definition.OptionName ?? option.Name, option.Definition.Items[selectedIndex].Label));

            option.Value = selectedIndex;
        }

        if (update.RemoveStartingLocations != RemoveStartingLocations)
        {
            RemoveStartingLocations = update.RemoveStartingLocations;
            AddNotice(GameOptionNotices.RemoveStartingLocationsChanged(RemoveStartingLocations));
        }

        RandomSeed = update.RandomSeed;
        RaiseChanged();
    }

    /// <summary>
    /// The local player pressed Launch: the host launches if nothing blocks it (otherwise shows why); a player
    /// toggles ready.
    /// </summary>
    public void Launch()
    {
        if (!IsHost)
        {
            RequestReady();
            return;
        }

        IReadOnlyList<LaunchBlocker> blockers = LobbyState.CheckLaunch(ProgramConstants.PLAYERNAME, SlotIndices.SpectatorSide);
        if (blockers.Count > 0)
        {
            ShowLaunchBlocker(blockers[0]);
            return;
        }

        HostLaunchGame();
    }

    /// <summary>Host: tells the players to start and starts the game.</summary>
    protected abstract void HostLaunchGame();

    /// <summary>Shows why the host can't launch (the XNA lobby's notifications).</summary>
    protected virtual void ShowLaunchBlocker(LaunchBlocker blocker)
    {
        switch (blocker.Kind)
        {
            case LaunchBlockerKind.RoomNotLocked:
                AddNotice("The host needs to lock the game room before launching the game.".L10N("Client:Main:LockGameNotificationV2"));
                break;
            case LaunchBlockerKind.TeamMappings:
                AddNotice(blocker.Message);
                break;
            case LaunchBlockerKind.SharedColors:
                AddNotice("Multiple human players cannot share the same color.".L10N("Client:Main:SharedColorsNotification"));
                break;
            case LaunchBlockerKind.AiSpectators:
                AddNotice("AI players don't enjoy spectating matches. They want some action!".L10N("Client:Main:AISpectatorsNotification"));
                break;
            case LaunchBlockerKind.SharedStartingLocation:
                AddNotice("Multiple players cannot share the same starting location on this map.".L10N("Client:Main:SharedStartingLocationNotification"));
                break;
            case LaunchBlockerKind.InsufficientPlayers when GameModeMap != null:
                AddNotice(string.Format("Unable to launch game: {0} cannot be played with fewer than {1} players".L10N("Client:Main:InsufficientPlayersNotificationV2"),
                    GameModeMap.ToString(), GameModeMap.MinPlayers));
                break;
            case LaunchBlockerKind.TooManyPlayers when GameModeMap != null:
                AddNotice(string.Format("Unable to launch game: {0} cannot be played with more than {1} players.".L10N("Client:Main:TooManyPlayersNotificationV2"),
                    GameModeMap.ToString(), GameModeMap.MaxPlayers));
                break;
            case LaunchBlockerKind.NotVerified:
                if (blocker.PlayerIndex > -1 && blocker.PlayerIndex < Players.Count)
                    AddNotice(string.Format("Unable to launch game. Player {0} hasn't been verified.".L10N("Client:Main:NotVerifiedNotification"), Players[blocker.PlayerIndex].Name));
                break;
            case LaunchBlockerKind.StillInGame:
                if (blocker.PlayerIndex > -1 && blocker.PlayerIndex < Players.Count)
                    AddNotice(string.Format("Unable to launch game. Player {0} is still playing the game you started previously.".L10N("Client:Main:StillInGameNotification"), Players[blocker.PlayerIndex].Name));
                break;
            case LaunchBlockerKind.NotReady:
                GetReadyNotification();
                break;
        }
    }

    /// <summary>The host can't start because not everyone is ready.</summary>
    public virtual void GetReadyNotification()
    {
        AddNotice("The host wants to start the game but cannot because not all players are ready!".L10N("Client:Main:GetReadyNotification"));
        if (!IsHost && !(FindLocalPlayer()?.Ready ?? true))
            Sounds.Play(LobbySound.GetReady);
    }

    /// <summary>A player came back from the game.</summary>
    public void ReturnNotification(string sender)
    {
        AddNotice(string.Format("{0} has returned from the game.".L10N("Client:Main:PlayerReturned"), sender));

        PlayerInfo pInfo = Players.Find(p => p.Name == sender);
        if (pInfo != null)
            pInfo.IsInGame = false;

        Sounds.Play(LobbySound.PlayerReturned);
        RaiseChanged();
    }

    /// <summary>Starts the game (the XNA multiplayer lobby's StartGame).</summary>
    protected void StartMultiplayerGame()
    {
        if (UserINISettings.Instance.StopGameLobbyMessageAudio)
            Sounds.SetEnabled(LobbySound.Message, false);

        StartGame();
    }

    // The game process exits on another thread
    protected sealed override void OnGameProcessExited() => UiDispatcher.Post(HandleGameProcessExited);

    /// <summary>The game exited (on the UI thread).</summary>
    protected virtual void HandleGameProcessExited()
    {
        base.OnGameProcessExited();

        PlayerInfo pInfo = FindLocalPlayer();
        if (pInfo != null)
            pInfo.IsInGame = false;

        if (UserINISettings.Instance.StopGameLobbyMessageAudio)
            Sounds.SetEnabled(LobbySound.Message, true);

        LobbyState.ClearReadyStatuses();

        if (IsHost)
            GenerateGameID();
        else if (AutoReady)
            RequestReady();

        RaiseChanged();
    }

    protected override void WriteSpawnIniAdditions(IniFile iniFile)
    {
        iniFile.SetIntValue("Settings", "FrameSendRate", FrameSendRate);
        if (MaxAhead > 0)
            iniFile.SetIntValue("Settings", "MaxAhead", MaxAhead);
        iniFile.SetIntValue("Settings", "Protocol", ProtocolVersion);
    }

    protected override void AddLaunchCaptureInputs(IDictionary<string, object> inputs)
    {
        base.AddLaunchCaptureInputs(inputs);
        inputs["IsHost"] = IsHost;
        inputs["FrameSendRate"] = FrameSendRate;
        inputs["MaxAhead"] = MaxAhead;
        inputs["ProtocolVersion"] = ProtocolVersion;
    }

    /// <summary>Leaves the room.</summary>
    public abstract void Leave();
}

/// <summary>What the map sharing confirmation panel shows.</summary>
public enum MapSharingPanelState
{
    Hidden,

    /// <summary>The host's map isn't installed: offer to download it.</summary>
    Request,

    Downloading,

    Failed,
}
