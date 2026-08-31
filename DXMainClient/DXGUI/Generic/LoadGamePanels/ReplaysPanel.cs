#nullable enable

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;

using ClientCore;
using ClientCore.Extensions;
using ClientCore.PlatformShim;

using ClientGUI;

using DTAClient.Domain;

using Microsoft.Xna.Framework;

using Rampastring.Tools;
using Rampastring.XNAUI;
using Rampastring.XNAUI.XNAControls;

namespace DTAClient.DXGUI.Generic.LoadGamePanels;

/// <summary>
/// Lists recorded replays and launches playback.
/// </summary>
public class ReplaysPanel : XNAPanel
{
    private const int MAX_LISTED_MISMATCHES = 4;

    private const int LIST_HEIGHT = 240;
    private const int DETAILS_HEIGHT = 100;
    private const int ROW_SPACING = 24;
    private const int CHECK_BOX_HEIGHT = 20;

    private const int PERSPECTIVE_X = 350;
    private const int PERSPECTIVE_WIDTH = 200;

    private const int FIXED_COLUMNS_WIDTH = 320;

    /// <summary>
    /// Frame counts offered for the spawner's ReplayKeyframeInterval. Playback drops a savegame
    /// keyframe this often as it watches, and rewinding restarts from the newest one at or before
    /// where you asked for - so a shorter interval is a more precise rewind at the cost of more
    /// scratch disk and a brief pause each time one is taken. Zero takes none.
    /// </summary>
    private static readonly int[] KeyframeIntervals = { 0, 375, 750, 1500 };

    public ReplaysPanel(WindowManager windowManager, DiscordHandler discordHandler) : base(windowManager)
    {
        this.discordHandler = discordHandler;
    }

    private readonly DiscordHandler discordHandler;

    private XNAMultiColumnListBox lbReplayList = null!;
    private XNATextBlock tbDetails = null!;

    private XNAClientCheckBox chkSpectator = null!;
    private XNAClientCheckBox chkShroudEnabled = null!;
    private XNAClientCheckBox chkLockedViewport = null!;
    private XNAClientCheckBox chkSelectUnits = null!;
    private XNAClientCheckBox chkShowChatAndBeacons = null!;
    private XNAClientCheckBox chkControlBar = null!;
    private XNAClientDropDown ddGameSpeed = null!;
    private XNAClientDropDown ddRewindPoints = null!;
    private XNAClientDropDown ddPerspective = null!;

    private List<ReplayGame> replays = new List<ReplayGame>();

    private IReadOnlyList<ReplayPlayer> perspectivePlayers = Array.Empty<ReplayPlayer>();

    public event EventHandler? SelectionChanged;

    public event EventHandler? LaunchRequested;

    public string LaunchButtonText => "Watch".L10N("Client:Main:ButtonWatchReplay");

    public bool CanLaunch => SelectedReplay?.IsPlayable == true;

    public bool CanDelete => lbReplayList.SelectedIndex > -1;

    private ReplayGame? SelectedReplay
        => lbReplayList.SelectedIndex > -1 && lbReplayList.SelectedIndex < replays.Count
            ? replays[lbReplayList.SelectedIndex]
            : null;

    private ReplayPlayer? SelectedPerspective
        => ddPerspective.SelectedIndex > -1 && ddPerspective.SelectedIndex < perspectivePlayers.Count
            ? perspectivePlayers[ddPerspective.SelectedIndex]
            : null;

    public override void Initialize()
    {
        Name = nameof(ReplaysPanel);
        DrawBorders = false;

        lbReplayList = new XNAMultiColumnListBox(WindowManager);
        lbReplayList.Name = nameof(lbReplayList);
        lbReplayList.ClientRectangle = new Rectangle(0, 0, Width, LIST_HEIGHT);
        lbReplayList.AddColumn("REPLAY".L10N("Client:Main:ReplayNameColumnHeader"), Width - FIXED_COLUMNS_WIDTH);
        lbReplayList.AddColumn("DATE / TIME".L10N("Client:Main:ReplayDateTimeColumnHeader"), 140);
        lbReplayList.AddColumn("LENGTH".L10N("Client:Main:ReplayLengthColumnHeader"), 70);
        lbReplayList.AddColumn("PLAYERS".L10N("Client:Main:ReplayPlayersColumnHeader"), 110);
        lbReplayList.BackgroundTexture = AssetLoader.CreateTexture(new Color(0, 0, 0, 128), 1, 1);
        lbReplayList.PanelBackgroundDrawMode = PanelBackgroundImageDrawMode.STRETCHED;
        lbReplayList.SelectedIndexChanged += LbReplayList_SelectedIndexChanged;
        lbReplayList.AllowKeyboardInput = true;

        tbDetails = new XNATextBlock(WindowManager);
        tbDetails.Name = nameof(tbDetails);
        tbDetails.ClientRectangle = new Rectangle(0, lbReplayList.Bottom + 6, Width, DETAILS_HEIGHT);
        tbDetails.BackgroundTexture = AssetLoader.CreateTexture(new Color(0, 0, 0, 128), 1, 1);
        tbDetails.PanelBackgroundDrawMode = PanelBackgroundImageDrawMode.STRETCHED;

        var lblPlayback = new XNALabel(WindowManager);
        lblPlayback.Name = nameof(lblPlayback);
        lblPlayback.ClientRectangle = new Rectangle(0, tbDetails.Bottom + 10, 0, 0);
        lblPlayback.Text = "Playback:".L10N("Client:Main:ReplayPlaybackSettings");

        int firstRowY = lblPlayback.Bottom + 8;

        chkSpectator = CreateCheckBox(nameof(chkSpectator), 0, firstRowY, 160,
            "Spectator view".L10N("Client:Main:ReplaySpectator"),
            ("Watch from an observer's seat instead of a player's: the whole map is visible, " +
             "the scoreboard replaces the sidebar, and EVA stays quiet.")
                .L10N("Client:Main:ReplaySpectatorTooltip"),
            UserINISettings.Instance.ReplayPlaybackSpectator);

        chkShroudEnabled = CreateCheckBox(nameof(chkShroudEnabled), 170, firstRowY, 150,
            "Enable shroud".L10N("Client:Main:ReplayShroud"),
            "Enables or disables the fog of war shrouding.".L10N("Client:Main:ReplayShroudTooltip"),
            UserINISettings.Instance.ReplayPlaybackShroud);

        chkLockedViewport = CreateCheckBox(nameof(chkLockedViewport), 330, firstRowY, 150,
            "Lock viewport".L10N("Client:Main:ReplayLockViewport"),
            "Locks the viewport to what the recording player was seeing.".L10N("Client:Main:ReplayLockViewportTooltip"),
            UserINISettings.Instance.ReplayPlaybackLockedViewport);

        chkSelectUnits = CreateCheckBox(nameof(chkSelectUnits), 490, firstRowY, 150,
            "Select units".L10N("Client:Main:ReplaySelectUnits"),
            "Shows which units were selected by the recording player.".L10N("Client:Main:ReplaySelectUnitsTooltip"),
            UserINISettings.Instance.ReplayPlaybackSelectUnits);

        int secondRowY = firstRowY + ROW_SPACING;

        chkShowChatAndBeacons = CreateCheckBox(nameof(chkShowChatAndBeacons), 0, secondRowY, 165,
            "Show chat and beacons".L10N("Client:Main:ReplayShowChatAndBeacons"),
            "Replays chat messages, beacons and taunts.".L10N("Client:Main:ReplayShowChatAndBeaconsTooltip"),
            UserINISettings.Instance.ReplayPlaybackShowChatAndBeacons);

        int thirdRowY = secondRowY + ROW_SPACING;

        chkControlBar = CreateCheckBox(nameof(chkControlBar), 0, thirdRowY, 165,
            "Playback controls".L10N("Client:Main:ReplayControlBar"),
            ("Shows a seek bar and transport buttons over the game while watching. " +
             "The same actions are also available as keyboard shortcuts.")
                .L10N("Client:Main:ReplayControlBarTooltip"),
            UserINISettings.Instance.ReplayPlaybackControlBar);

        var lblGameSpeed = new XNALabel(WindowManager);
        lblGameSpeed.Name = nameof(lblGameSpeed);
        lblGameSpeed.Text = "Speed:".L10N("Client:Main:ReplayPlaybackSpeed");
        lblGameSpeed.ClientRectangle = new Rectangle(chkShroudEnabled.X, secondRowY + 3,
            lblGameSpeed.Width, lblGameSpeed.Height);

        ddGameSpeed = new XNAClientDropDown(WindowManager);
        ddGameSpeed.Name = nameof(ddGameSpeed);
        ddGameSpeed.ClientRectangle = new Rectangle(lblGameSpeed.Right + 8, secondRowY, 110, 21);
        ddGameSpeed.ToolTipText = ("How fast the replay is played back.").L10N("Client:Main:ReplayPlaybackSpeedTooltip");
        PopulateGameSpeedDropDown();

        var lblPerspective = new XNALabel(WindowManager);
        lblPerspective.Name = nameof(lblPerspective);
        lblPerspective.Text = "Watch as:".L10N("Client:Main:ReplayPerspective");
        lblPerspective.ClientRectangle = new Rectangle(PERSPECTIVE_X, secondRowY + 3,
            lblPerspective.Width, lblPerspective.Height);

        ddPerspective = new XNAClientDropDown(WindowManager);
        ddPerspective.Name = nameof(ddPerspective);
        ddPerspective.ClientRectangle = new Rectangle(lblPerspective.Right + 8, secondRowY,
            PERSPECTIVE_WIDTH, 21);
        ddPerspective.ToolTipText = ("Whose screen the replay is watched from - their fog of war, " +
            "sidebar, beacons and starting view, but no team chat messages.")
            .L10N("Client:Main:ReplayPerspectiveTooltip");
        ddPerspective.SelectedIndexChanged += DdPerspective_SelectedIndexChanged;

        var lblRewindPoints = new XNALabel(WindowManager);
        lblRewindPoints.Name = nameof(lblRewindPoints);
        lblRewindPoints.Text = "Rewind:".L10N("Client:Main:ReplayRewindPoints");
        lblRewindPoints.ClientRectangle = new Rectangle(chkShroudEnabled.X, thirdRowY + 3,
            lblRewindPoints.Width, lblRewindPoints.Height);

        ddRewindPoints = new XNAClientDropDown(WindowManager);
        ddRewindPoints.Name = nameof(ddRewindPoints);
        ddRewindPoints.ClientRectangle = new Rectangle(lblRewindPoints.Right + 8, thirdRowY, 170, 21);
        ddRewindPoints.ToolTipText = ("How often playback saves a point it can rewind to. " +
            "Closer together rewinds more precisely; further apart uses less disk and " +
            "pauses less often. Off leaves seeking forward-only.")
            .L10N("Client:Main:ReplayRewindPointsTooltip");
        PopulateRewindPointsDropDown();

        AddChild(lbReplayList);
        AddChild(tbDetails);
        AddChild(lblPlayback);
        AddChild(chkSpectator);
        AddChild(chkShroudEnabled);
        AddChild(chkLockedViewport);
        AddChild(chkSelectUnits);
        AddChild(chkShowChatAndBeacons);
        AddChild(lblGameSpeed);
        AddChild(ddGameSpeed);
        AddChild(lblPerspective);
        AddChild(ddPerspective);
        AddChild(chkControlBar);
        AddChild(lblRewindPoints);
        AddChild(ddRewindPoints);

        base.Initialize();
    }

    private XNAClientCheckBox CreateCheckBox(string name, int x, int y, int width, string text,
        string toolTip, bool isChecked)
    {
        var checkBox = new XNAClientCheckBox(WindowManager);
        checkBox.Name = name;
        checkBox.ClientRectangle = new Rectangle(x, y, width, CHECK_BOX_HEIGHT);
        checkBox.Text = text;
        checkBox.ToolTipText = toolTip;
        checkBox.Checked = isChecked;
        checkBox.CheckedChanged += PlaybackSetting_Changed;
        return checkBox;
    }

    private void PopulateGameSpeedDropDown()
    {
        ddGameSpeed.AddItem("As recorded".L10N("Client:Main:ReplayPlaybackSpeedAsRecorded"));

        foreach (int fps in ReplayGame.PlaybackSpeedLadder)
        {
            ddGameSpeed.AddItem(string.Format(
                "{0} FPS".L10N("Client:Main:ReplayPlaybackSpeedItem"), fps));
        }

        // Store FPS instead of an index so ladder changes preserve the selection.
        int storedFPS = UserINISettings.Instance.ReplayPlaybackGameSpeed;
        int ladderIndex = Array.IndexOf(ReplayGame.PlaybackSpeedLadder, storedFPS);
        ddGameSpeed.SelectedIndex = ladderIndex >= 0 ? ladderIndex + 1 : 0;
        ddGameSpeed.SelectedIndexChanged += PlaybackSetting_Changed;
    }

    private void PopulateRewindPointsDropDown()
    {
        foreach (int interval in KeyframeIntervals)
        {
            ddRewindPoints.AddItem(interval <= 0
                ? "Off".L10N("Client:Main:ReplayRewindPointsOff")
                : string.Format("Every {0} frames".L10N("Client:Main:ReplayRewindPointsItem"), interval));
        }

        // Store the interval instead of an index, so changing the table preserves the selection.
        int storedInterval = UserINISettings.Instance.ReplayPlaybackKeyframeInterval;
        int index = Array.IndexOf(KeyframeIntervals, storedInterval);
        ddRewindPoints.SelectedIndex = index >= 0 ? index : Array.IndexOf(KeyframeIntervals, 750);
        ddRewindPoints.SelectedIndexChanged += PlaybackSetting_Changed;
    }

    /// <summary>Frames between rewind points, or 0 for none.</summary>
    private int SelectedKeyframeInterval
    {
        get
        {
            int index = ddRewindPoints.SelectedIndex;
            return index >= 0 && index < KeyframeIntervals.Length ? KeyframeIntervals[index] : 750;
        }
    }

    /// <summary>Selected playback rate in FPS, or 0 for the recorded speed.</summary>
    private int SelectedPlaybackFPS
    {
        get
        {
            int index = ddGameSpeed.SelectedIndex - 1;
            return index >= 0 && index < ReplayGame.PlaybackSpeedLadder.Length
                ? ReplayGame.PlaybackSpeedLadder[index]
                : 0;
        }
    }

    private void PlaybackSetting_Changed(object? sender, EventArgs e)
    {
        UserINISettings.Instance.ReplayPlaybackSpectator.Value = chkSpectator.Checked;
        UserINISettings.Instance.ReplayPlaybackShroud.Value = chkShroudEnabled.Checked;
        UserINISettings.Instance.ReplayPlaybackLockedViewport.Value = chkLockedViewport.Checked;
        UserINISettings.Instance.ReplayPlaybackSelectUnits.Value = chkSelectUnits.Checked;
        UserINISettings.Instance.ReplayPlaybackShowChatAndBeacons.Value = chkShowChatAndBeacons.Checked;
        UserINISettings.Instance.ReplayPlaybackGameSpeed.Value = SelectedPlaybackFPS;
        UserINISettings.Instance.ReplayPlaybackControlBar.Value = chkControlBar.Checked;
        UserINISettings.Instance.ReplayPlaybackKeyframeInterval.Value = SelectedKeyframeInterval;

        UserINISettings.Instance.SaveSettings();

        UpdatePlaybackOptionAvailability();
    }

    private void DdPerspective_SelectedIndexChanged(object? sender, EventArgs e)
        => UpdatePlaybackOptionAvailability();

    private void LbReplayList_SelectedIndexChanged(object? sender, EventArgs e)
    {
        UpdateForSelection();
        SelectionChanged?.Invoke(this, EventArgs.Empty);
    }

    private void UpdateForSelection()
    {
        UpdateDetails();
        UpdatePerspectiveDropDown();
    }

    private void UpdatePerspectiveDropDown()
    {
        // Perspective indices are replay-specific, so reset to the recorder.
        ddPerspective.SelectedIndexChanged -= DdPerspective_SelectedIndexChanged;

        perspectivePlayers = SelectedReplay?.Players ?? (IReadOnlyList<ReplayPlayer>)Array.Empty<ReplayPlayer>();

        ddPerspective.Items.Clear();
        ddPerspective.SelectedIndex = -1;

        foreach (ReplayPlayer player in perspectivePlayers)
        {
            ddPerspective.AddItem(Renderer.GetSafeString(DescribePerspective(player),
                ddPerspective.FontIndex));
        }

        if (perspectivePlayers.Count > 0)
            ddPerspective.SelectedIndex = 0;

        ddPerspective.SelectedIndexChanged += DdPerspective_SelectedIndexChanged;

        UpdatePlaybackOptionAvailability();
    }

    private void UpdatePlaybackOptionAvailability()
    {
        bool spectating = chkSpectator.Checked;
        bool watchingRecordingPlayer = spectating || SelectedPerspective?.IsRecorder != false;

        // The spawner ignores these options for spectator or alternate-player views.
        chkShroudEnabled.AllowChecking = !spectating;
        ddPerspective.AllowDropDown = !spectating && perspectivePlayers.Count > 1;

        chkLockedViewport.AllowChecking = watchingRecordingPlayer;
        chkSelectUnits.AllowChecking = watchingRecordingPlayer;
    }

    private static string DescribePerspective(ReplayPlayer player)
    {
        if (player.IsRecorder)
        {
            return string.Format("{0} (recorded)".L10N("Client:Main:ReplayPerspectiveRecorder"),
                player.Name);
        }

        return player.IsSpectator
            ? string.Format("{0} (spectator)".L10N("Client:Main:ReplayPerspectiveSpectator"), player.Name)
            : player.Name;
    }

    public void Refresh()
    {
        // Preserve selection by file name because deletion changes list indices.
        string? previouslySelected = SelectedReplay?.FileName;

        lbReplayList.ClearItems();
        lbReplayList.SelectedIndex = -1;

        replays = ReplayManager.List();

        foreach (ReplayGame replay in replays)
        {
            string[] columns =
            {
                Renderer.GetSafeString(replay.GUIName, lbReplayList.FontIndex),
                replay.RecordedAt.ToString("g"),
                FormatDuration(replay),
                replay.IsPlayable ? replay.Players.Count.ToString() : "-"
            };

            if (replay.IsPlayable)
            {
                lbReplayList.AddItem(columns, true);
                continue;
            }

            // Keep unsupported replays selectable so the details pane can explain them.
            lbReplayList.AddItem(Array.ConvertAll(columns,
                text => new XNAListBoxItem(text, UISettings.ActiveSettings.DisabledItemColor)));
        }

        if (replays.Count > 0)
        {
            int restored = previouslySelected == null
                ? 0
                : replays.FindIndex(replay =>
                    string.Equals(replay.FileName, previouslySelected, StringComparison.OrdinalIgnoreCase));

            lbReplayList.SelectedIndex = restored < 0 ? 0 : restored;
        }

        UpdateForSelection();
    }

    private void UpdateDetails()
    {
        ReplayGame? replay = SelectedReplay;

        if (replay == null)
        {
            tbDetails.Text = "Select a replay to see its details.".L10N("Client:Main:ReplayNoSelection");
            return;
        }

        if (!replay.IsPlayable)
        {
            tbDetails.Text = DescribeUnplayable(replay);
            return;
        }

        var details = new StringBuilder();

        details.Append(SafeForDetails(replay.GUIName));
        if (!string.IsNullOrWhiteSpace(replay.UIGameMode))
            details.Append(" - ").Append(SafeForDetails(replay.UIGameMode));
        details.AppendLine();

        if (replay.Players.Count > 0)
        {
            details.Append(string.Format("Players: {0}".L10N("Client:Main:ReplayDetailPlayers"),
                SafeForDetails(string.Join(", ", replay.Players.Select(player => player.Name)))));
            details.AppendLine();
        }

        details.Append(string.Format("Recorded {0}".L10N("Client:Main:ReplayDetailRecorded"),
            replay.RecordedAt.ToString("f")));
        details.AppendLine();

        if (replay.IsComplete)
        {
            details.Append(string.Format("Length: {0} - {1} frames at {2} FPS"
                .L10N("Client:Main:ReplayDetailLength"),
                FormatDuration(replay), replay.TotalFrames.ToString("N0"), replay.FramesPerSecond));
            details.AppendLine();
        }

        details.Append(string.Format("Version: {0}".L10N("Client:Main:ReplayDetailVersion"),
            SafeForDetails(GetDisplayVersion(replay))));

        if (!string.IsNullOrWhiteSpace(replay.SpawnerVersion))
        {
            details.Append(" - ");
            details.Append(string.Format("spawner {0}".L10N("Client:Main:ReplayDetailSpawnerVersion"),
                SafeForDetails(replay.SpawnerVersion)));
        }

        if (!replay.IsComplete)
        {
            details.AppendLine();
            details.Append("This recording did not finish cleanly, so it stops early."
                .L10N("Client:Main:ReplayIncomplete"));
        }

        tbDetails.Text = details.ToString();
    }

    private ReplayPlayer? LaunchPerspective => chkSpectator.Checked ? null : SelectedPerspective;

    public void Launch()
    {
        ReplayGame? replay = SelectedReplay;

        // Double-click can call Launch even when the button is disabled.
        if (replay == null || !replay.IsPlayable)
            return;

        Logger.Log("Loading replay " + replay.FileName);

        if (!replay.TryReadSpawnFiles(out string spawnIniContent, out byte[] spawnMapContent))
        {
            Logger.Log("Replay file is missing spawn file data: " + replay.FileName);
            XNAMessageBox.Show(WindowManager,
                "Replay Unreadable".L10N("Client:Main:ReplayUnreadableTitle"),
                ("This replay does not contain the game setup it was recorded with, so it " +
                 "cannot be played back.").L10N("Client:Main:ReplayUnreadableText"));
            return;
        }

        IniFile spawnIni = ReadReplaySpawnIni(spawnIniContent);

        List<string> fileMismatches = ReplayFileHashes.FindMismatches(spawnIni);
        if (fileMismatches.Count > 0)
        {
            ShowMismatchPrompt(replay, spawnIni, spawnMapContent, LaunchPerspective, fileMismatches);
            return;
        }

        StartPlayback(replay, spawnIni, spawnMapContent, LaunchPerspective);
    }

    private static IniFile ReadReplaySpawnIni(string spawnIniContent)
    {
        using MemoryStream stream = new MemoryStream(EncodingExt.UTF8NoBOM.GetBytes(spawnIniContent));
        return new IniFile(stream, EncodingExt.UTF8NoBOM, applyBaseIni: false);
    }

    private void ShowMismatchPrompt(ReplayGame replay, IniFile spawnIni, byte[] spawnMapContent,
        ReplayPlayer? perspective, List<string> fileMismatches)
    {
        foreach (string mismatch in fileMismatches)
            Logger.Log("Replay file mismatch: " + mismatch);

        string details = string.Join("\n\n", fileMismatches.Take(MAX_LISTED_MISMATCHES));

        if (fileMismatches.Count > MAX_LISTED_MISMATCHES)
        {
            details += "\n\n" + string.Format("...and {0} more".L10N("Client:Main:ReplayMoreMismatches"),
                fileMismatches.Count - MAX_LISTED_MISMATCHES);
        }

        var msgBox = new XNAMessageBox(WindowManager,
            "Replay File Mismatch".L10N("Client:Main:ReplayFileMismatchTitle"),
            string.Format(("This replay was recorded with different game files, so it will " +
                "most likely not play back correctly - it may load and then do nothing.\n\n" +
                "Recorded with: {0}\n" +
                "You have: {1}\n\n" +
                "{2}\n\n" +
                "Play it anyway?").L10N("Client:Main:ReplayFileMismatchText"),
                SafeForDialog(GetDisplayVersion(replay)), SafeForDialog(ReplayManager.GameClientVersion),
                SafeForDialog(details)),
            XNAMessageBoxButtons.YesNo);

        msgBox.YesClickedAction = _ => StartPlayback(replay, spawnIni, spawnMapContent, perspective);
        msgBox.Show();
    }

    private void StartPlayback(ReplayGame replay, IniFile spawnIni, byte[] spawnMapContent,
        ReplayPlayer? perspective)
    {
        spawnIni.SetStringValue("Settings", "ReplayFile", ReplayManager.GetRelativePath(replay.FileName));

        // ReplayViewPlayer is a spawn.ini player slot.
        spawnIni.SetIntValue("Settings", "ReplayViewPlayer", perspective?.SpawnIniIndex ?? 0);

        // Use the selected player's loading screen when available.
        if (perspective != null && !perspective.IsRecorder && perspective.SideIndex >= 0)
        {
            spawnIni.SetStringValue("Settings", "CustomLoadScreen",
                LoadingScreenController.GetLoadScreenName(perspective.SideIndex.ToString()));
        }

        // Playback speed changes pacing, not the recorded simulation speed.
        spawnIni.SetIntValue("Settings", "ReplayPlaybackSpeed", SelectedPlaybackFPS);

        spawnIni.SetBooleanValue("Settings", "ReplayShroudEnabled", chkShroudEnabled.Checked);
        spawnIni.SetBooleanValue("Settings", "ReplayLockedViewport", chkLockedViewport.Checked);
        spawnIni.SetBooleanValue("Settings", "ReplaySelectUnits", chkSelectUnits.Checked);
        spawnIni.SetBooleanValue("Settings", "ReplaySpectator", chkSpectator.Checked);
        spawnIni.SetBooleanValue("Settings", "ReplayShowChatAndBeacons", chkShowChatAndBeacons.Checked);

        spawnIni.SetBooleanValue("Settings", "ReplayControlBar", chkControlBar.Checked);

        // Keyframes are scratch savegames the spawner takes as it plays, so that rewinding does not
        // have to replay the game from the start. Nothing about them reaches the replay file.
        spawnIni.SetIntValue("Settings", "ReplayKeyframeInterval", SelectedKeyframeInterval);

        spawnIni.SetBooleanValue("Settings", "EnableReplayRecording", false);

        spawnIni.RemoveSection(ReplayFileHashes.SECTION);

        FileInfo spawnerSettingsFile = SafePath.GetFile(ProgramConstants.GamePath, ProgramConstants.SPAWNER_SETTINGS);
        if (spawnerSettingsFile.Exists)
            spawnerSettingsFile.Delete();

        spawnIni.FileName = spawnerSettingsFile.FullName;
        spawnIni.Encoding = EncodingExt.UTF8NoBOM;
        spawnIni.WriteIniFile();

        FileInfo spawnMapIniFile = SafePath.GetFile(ProgramConstants.GamePath, ProgramConstants.SPAWNMAP_INI);
        if (spawnMapIniFile.Exists)
            spawnMapIniFile.Delete();

        // Campaign missions can load from game archives without an embedded map.
        if (spawnMapContent.Length > 0)
            File.WriteAllBytes(spawnMapIniFile.FullName, spawnMapContent);

        Logger.Log("Extracted spawn files from replay successfully");

        discordHandler.UpdatePresence(replay.GUIName, true);

        LaunchRequested?.Invoke(this, EventArgs.Empty);

        GameProcessLogic.GameProcessExited += GameProcessExited_Callback;
        GameProcessLogic.StartGameProcess(WindowManager);
    }

    private void GameProcessExited_Callback()
    {
        WindowManager.AddCallback(new Action(GameProcessExited), null);
    }

    protected virtual void GameProcessExited()
    {
        GameProcessLogic.GameProcessExited -= GameProcessExited_Callback;

        discordHandler.UpdatePresence();
        Refresh();
    }

    public void Delete()
    {
        ReplayGame? replay = SelectedReplay;
        if (replay == null)
            return;

        var msgBox = new XNAMessageBox(WindowManager, "Delete Confirmation".L10N("Client:Main:DeleteConfirmationTitle"),
            string.Format(("The following replay will be deleted permanently:\n\n" +
                "Filename: {0}\n" +
                "Replay: {1}\n" +
                "Date and time: {2}\n\n" +
                "Are you sure you want to proceed?").L10N("Client:Main:DeleteReplayConfirmationText"),
                SafeForDialog(replay.FileName), SafeForDialog(replay.GUIName),
                replay.RecordedAt.ToString()),
            XNAMessageBoxButtons.YesNo);

        msgBox.YesClickedAction = _ =>
        {
            if (!ReplayManager.Delete(replay))
            {
                XNAMessageBox.Show(WindowManager,
                    "Deleting Replay Failed".L10N("Client:Main:ReplayDeleteFailedTitle"),
                    string.Format(("The replay {0} could not be deleted. It may still be open in " +
                        "the game or in another program.").L10N("Client:Main:ReplayDeleteFailedText"),
                        SafeForDialog(replay.FileName)));
            }

            Refresh();
        };
        msgBox.Show();
    }

    /// <summary>Makes replay metadata renderable with the details font.</summary>
    private string SafeForDetails(string text) => Renderer.GetSafeString(text, tbDetails.FontIndex);

    /// <summary>Makes replay metadata renderable in message boxes.</summary>
    private static string SafeForDialog(string text) => Renderer.GetSafeString(text, 0);

    private string DescribeUnplayable(ReplayGame replay)
    {
        var details = new StringBuilder();

        details.Append(SafeForDetails(replay.GUIName));
        details.AppendLine();
        details.Append(string.Format("Recorded {0}".L10N("Client:Main:ReplayDetailRecorded"),
            replay.RecordedAt.ToString("f")));
        details.AppendLine();

        details.Append(("This replay was recorded in a newer format than this version of the game " +
            "can read. Update the game to watch it.").L10N("Client:Main:ReplayUnsupportedVersion"));

        return details.ToString();
    }

    private static string GetDisplayVersion(ReplayGame replay)
        => string.IsNullOrWhiteSpace(replay.GameClientVersion)
            ? "Unknown".L10N("Client:Main:ReplayUnknownVersion")
            : replay.GameClientVersion;

    private static string FormatDuration(ReplayGame replay)
    {
        if (!replay.IsPlayable || !replay.IsComplete)
            return "?";

        TimeSpan duration = replay.Duration;
        return duration.TotalHours >= 1
            ? $"{(int)duration.TotalHours}:{duration.Minutes:D2}:{duration.Seconds:D2}"
            : $"{duration.Minutes}:{duration.Seconds:D2}";
    }
}