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
public class ReplaysPanel : LoadGamePanel
{
    /// <summary>Files named in a mismatch dialog before it starts summarising.</summary>
    private const int MAX_LISTED_MISMATCHES = 4;

    private const int LIST_HEIGHT = 240;
    private const int DETAILS_HEIGHT = 100;
    private const int ROW_SPACING = 24;
    private const int CHECK_BOX_HEIGHT = 20;

    /// <summary>Combined width of the fixed-width columns; the replay name gets the rest.</summary>
    private const int FIXED_COLUMNS_WIDTH = 320;

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
    private XNAClientDropDown ddGameSpeed = null!;

    private LoadGamePanelAction[]? extraActions;

    private List<ReplayGame> replays = new List<ReplayGame>();

    public override string TabTitle => "Replays".L10N("Client:Main:TabReplays");

    public override string LaunchButtonText => "Watch".L10N("Client:Main:ButtonWatchReplay");

    public override bool CanLaunch => lbReplayList.SelectedIndex > -1;

    public override bool CanDelete => lbReplayList.SelectedIndex > -1;

    public override IReadOnlyList<LoadGamePanelAction> ExtraActions => extraActions ??= new[]
    {
        new LoadGamePanelAction("btnOpenReplayFolder",
            "Open Folder".L10N("Client:Main:ReplayOpenFolder"), ReplayManager.OpenDirectory),
        new LoadGamePanelAction("btnDeleteAllReplays",
            "Delete All".L10N("Client:Main:ReplayDeleteAll"), DeleteAll, () => replays.Count > 0)
    };

    private ReplayGame? SelectedReplay
        => lbReplayList.SelectedIndex > -1 && lbReplayList.SelectedIndex < replays.Count
            ? replays[lbReplayList.SelectedIndex]
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
            ("Watch as an observer: reveals the whole map and shows cloaked and disguised " +
             "units. Leave off to watch from the recording player's point of view.")
                .L10N("Client:Main:ReplaySpectatorTooltip"),
            UserINISettings.Instance.ReplayPlaybackSpectator);

        chkShroudEnabled = CreateCheckBox(nameof(chkShroudEnabled), 170, firstRowY, 150,
            "Enable shroud".L10N("Client:Main:ReplayShroud"),
            "Fog of war will be enabled for the recording player.".L10N("Client:Main:ReplayShroudTooltip"),
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

        // XNALabel sizes itself when Text is set.
        var lblGameSpeed = new XNALabel(WindowManager);
        lblGameSpeed.Name = nameof(lblGameSpeed);
        lblGameSpeed.Text = "Speed:".L10N("Client:Main:ReplayPlaybackSpeed");
        lblGameSpeed.ClientRectangle = new Rectangle(chkShroudEnabled.X, secondRowY + 3,
            lblGameSpeed.Width, lblGameSpeed.Height);

        ddGameSpeed = new XNAClientDropDown(WindowManager);
        ddGameSpeed.Name = nameof(ddGameSpeed);
        ddGameSpeed.ClientRectangle = new Rectangle(lblGameSpeed.Right + 8, secondRowY, 110, 21);
        ddGameSpeed.ToolTipText = ("How fast the replay is played back. The recorded game's own " +
            "speed is unaffected.").L10N("Client:Main:ReplayPlaybackSpeedTooltip");
        PopulateGameSpeedDropDown();

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

        base.Initialize();
    }

    public override void LayOutControls()
    {
        lbReplayList.ClientRectangle = new Rectangle(0, 0, Width, LIST_HEIGHT);
        lbReplayList.ChangeColumnWidth(0, Width - FIXED_COLUMNS_WIDTH);

        tbDetails.ClientRectangle = new Rectangle(0, lbReplayList.Bottom + 6, Width, DETAILS_HEIGHT);
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

    /// <summary>
    /// Adds the playback speed values accepted by the spawner.
    /// </summary>
    private void PopulateGameSpeedDropDown()
    {
        for (int i = 0; i <= ReplayGame.MAX_GAME_SPEED_INDEX; i++)
            ddGameSpeed.AddItem($"{ReplayGame.GetFramesPerSecond(i)} FPS");

        int stored = UserINISettings.Instance.ReplayPlaybackGameSpeed;
        ddGameSpeed.SelectedIndex = stored >= 0 && stored <= ReplayGame.MAX_GAME_SPEED_INDEX ? stored : 0;
        ddGameSpeed.SelectedIndexChanged += PlaybackSetting_Changed;
    }

    private void PlaybackSetting_Changed(object? sender, EventArgs e)
    {
        UserINISettings.Instance.ReplayPlaybackSpectator.Value = chkSpectator.Checked;
        UserINISettings.Instance.ReplayPlaybackShroud.Value = chkShroudEnabled.Checked;
        UserINISettings.Instance.ReplayPlaybackLockedViewport.Value = chkLockedViewport.Checked;
        UserINISettings.Instance.ReplayPlaybackSelectUnits.Value = chkSelectUnits.Checked;
        UserINISettings.Instance.ReplayPlaybackShowChatAndBeacons.Value = chkShowChatAndBeacons.Checked;
        UserINISettings.Instance.ReplayPlaybackGameSpeed.Value = ddGameSpeed.SelectedIndex;

        // Persist immediately; this panel has no Apply button.
        UserINISettings.Instance.SaveSettings();
    }

    private void LbReplayList_SelectedIndexChanged(object? sender, EventArgs e)
    {
        UpdateDetails();
        OnSelectionChanged();
    }

    public override void Refresh()
    {
        int previouslySelected = lbReplayList.SelectedIndex;

        replays = ReplayManager.List();

        lbReplayList.ClearItems();
        lbReplayList.SelectedIndex = -1;

        foreach (ReplayGame replay in replays)
        {
            lbReplayList.AddItem(new[]
            {
                Renderer.GetSafeString(replay.GUIName, lbReplayList.FontIndex),
                replay.RecordedAt.ToString("g"),
                FormatDuration(replay),
                replay.PlayerNames.Count.ToString()
            }, true);
        }

        if (replays.Count > 0)
            lbReplayList.SelectedIndex = Math.Min(Math.Max(previouslySelected, 0), replays.Count - 1);

        UpdateDetails();
    }

    private void UpdateDetails()
    {
        ReplayGame? replay = SelectedReplay;

        if (replay == null)
        {
            tbDetails.Text = "Select a replay to see its details.".L10N("Client:Main:ReplayNoSelection");
            return;
        }

        var details = new StringBuilder();

        details.Append(replay.GUIName);
        if (!string.IsNullOrWhiteSpace(replay.UIGameMode))
            details.Append(" - ").Append(replay.UIGameMode);
        details.AppendLine();

        if (replay.PlayerNames.Count > 0)
        {
            details.Append(string.Format("Players: {0}".L10N("Client:Main:ReplayDetailPlayers"),
                string.Join(", ", replay.PlayerNames)));
            details.AppendLine();
        }

        details.Append(string.Format("Recorded {0}".L10N("Client:Main:ReplayDetailRecorded"),
            replay.RecordedAt.ToString("f")));
        details.AppendLine();

        // An incomplete recording has no frame count to show - the header still reads 0, which is
        // how it was recognised as incomplete in the first place.
        if (replay.IsComplete)
        {
            details.Append(string.Format("Length: {0} - {1} frames at {2} FPS"
                .L10N("Client:Main:ReplayDetailLength"),
                FormatDuration(replay), replay.TotalFrames.ToString("N0"), replay.FramesPerSecond));
            details.AppendLine();
        }

        details.Append(string.Format("Version: {0}".L10N("Client:Main:ReplayDetailVersion"),
            GetDisplayVersion(replay)));

        if (!replay.IsComplete)
        {
            details.AppendLine();
            details.Append("This recording did not finish cleanly, so it stops early."
                .L10N("Client:Main:ReplayIncomplete"));
        }

        tbDetails.Text = details.ToString();
    }

    public override void Launch()
    {
        ReplayGame? replay = SelectedReplay;
        if (replay == null)
            return;

        Logger.Log("Loading replay " + replay.FileName);

        if (!replay.TryReadSpawnFiles(out string spawnIniContent, out string spawnMapContent))
        {
            Logger.Log("Replay file is missing spawn file data: " + replay.FileName);
            XNAMessageBox.Show(WindowManager,
                "Replay Unreadable".L10N("Client:Main:ReplayUnreadableTitle"),
                ("This replay does not contain the game setup it was recorded with, so it " +
                 "cannot be played back.").L10N("Client:Main:ReplayUnreadableText"));
            return;
        }

        IniFile spawnIni = ReadReplaySpawnIni(spawnIniContent);

        // A replay only plays back correctly against the game files it was recorded with.
        List<string> fileMismatches = ReplayFileHashes.FindMismatches(spawnIni);
        if (fileMismatches.Count > 0)
        {
            ShowMismatchPrompt(replay, spawnIni, spawnMapContent, fileMismatches);
            return;
        }

        StartPlayback(replay, spawnIni, spawnMapContent);
    }

    private static IniFile ReadReplaySpawnIni(string spawnIniContent)
    {
        using MemoryStream stream = new MemoryStream(EncodingExt.UTF8NoBOM.GetBytes(spawnIniContent));
        return new IniFile(stream, EncodingExt.UTF8NoBOM, applyBaseIni: false);
    }

    private void ShowMismatchPrompt(ReplayGame replay, IniFile spawnIni, string spawnMapContent,
        List<string> fileMismatches)
    {
        foreach (string mismatch in fileMismatches)
            Logger.Log("Replay file mismatch: " + mismatch);

        string details = string.Join("\n\n", fileMismatches.Take(MAX_LISTED_MISMATCHES));

        if (fileMismatches.Count > MAX_LISTED_MISMATCHES)
        {
            details += "\n  " + string.Format("...and {0} more".L10N("Client:Main:ReplayMoreMismatches"),
                fileMismatches.Count - MAX_LISTED_MISMATCHES);
        }

        // Warn but allow playback; the mismatch might not affect this replay.
        var msgBox = new XNAMessageBox(WindowManager,
            "Replay File Mismatch".L10N("Client:Main:ReplayFileMismatchTitle"),
            string.Format(("This replay was recorded with different game files, so it will " +
                "most likely not play back correctly - it may load and then do nothing.\n\n" +
                "Recorded with: {0}\n" +
                "You have: {1}\n\n" +
                "{2}\n\n" +
                "Play it anyway?").L10N("Client:Main:ReplayFileMismatchText"),
                GetDisplayVersion(replay), ReplayManager.GameClientVersion, details),
            XNAMessageBoxButtons.YesNo);

        msgBox.YesClickedAction = _ => StartPlayback(replay, spawnIni, spawnMapContent);
        msgBox.Show();
    }

    private void StartPlayback(ReplayGame replay, IniFile spawnIni, string spawnMapContent)
    {
        spawnIni.SetStringValue("Settings", "ReplayFile", ReplayManager.GetRelativePath(replay.FileName));

        spawnIni.SetIntValue("Settings", "GameSpeed", ddGameSpeed.SelectedIndex);

        spawnIni.SetBooleanValue("Settings", "ReplayShroudEnabled", chkShroudEnabled.Checked);
        spawnIni.SetBooleanValue("Settings", "ReplayLockedViewport", chkLockedViewport.Checked);
        spawnIni.SetBooleanValue("Settings", "ReplaySelectUnits", chkSelectUnits.Checked);
        spawnIni.SetBooleanValue("Settings", "ReplaySpectator", chkSpectator.Checked);
        spawnIni.SetBooleanValue("Settings", "ReplayShowChatAndBeacons", chkShowChatAndBeacons.Checked);

        // Watching a replay must never produce another one.
        spawnIni.SetBooleanValue("Settings", "EnableReplayRecording", false);

        FileInfo spawnerSettingsFile = SafePath.GetFile(ProgramConstants.GamePath, ProgramConstants.SPAWNER_SETTINGS);
        if (spawnerSettingsFile.Exists)
            spawnerSettingsFile.Delete();

        spawnIni.FileName = spawnerSettingsFile.FullName;
        spawnIni.Encoding = EncodingExt.UTF8NoBOM;
        spawnIni.WriteIniFile();

        FileInfo spawnMapIniFile = SafePath.GetFile(ProgramConstants.GamePath, ProgramConstants.SPAWNMAP_INI);
        if (spawnMapIniFile.Exists)
            spawnMapIniFile.Delete();

        File.WriteAllText(spawnMapIniFile.FullName, spawnMapContent, EncodingExt.UTF8NoBOM);

        Logger.Log("Extracted spawn files from replay successfully");

        discordHandler.UpdatePresence(replay.GUIName, true);

        OnLaunchRequested();

        GameProcessLogic.GameProcessExited += GameProcessExited_Callback;
        GameProcessLogic.StartGameProcess(WindowManager);
    }

    private void GameProcessExited_Callback()
    {
        WindowManager.AddCallback(new Action(GameProcessExited), null);
    }

    private void GameProcessExited()
    {
        GameProcessLogic.GameProcessExited -= GameProcessExited_Callback;

        discordHandler.UpdatePresence();
        Refresh();
    }

    public override void Delete()
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
                replay.FileName, Renderer.GetSafeString(replay.GUIName, lbReplayList.FontIndex),
                replay.RecordedAt.ToString()),
            XNAMessageBoxButtons.YesNo);

        msgBox.YesClickedAction = _ =>
        {
            ReplayManager.Delete(replay);
            Refresh();
        };
        msgBox.Show();
    }

    private void DeleteAll()
    {
        if (replays.Count == 0)
            return;

        var msgBox = new XNAMessageBox(WindowManager, "Delete Confirmation".L10N("Client:Main:DeleteConfirmationTitle"),
            string.Format(("All {0} replays will be deleted permanently.\n\n" +
                "Are you sure you want to proceed?").L10N("Client:Main:DeleteAllReplaysConfirmationText"),
                replays.Count),
            XNAMessageBoxButtons.YesNo);

        msgBox.YesClickedAction = _ =>
        {
            foreach (ReplayGame replay in replays)
                ReplayManager.Delete(replay);

            Refresh();
        };
        msgBox.Show();
    }

    /// <summary>
    /// The game package a replay was recorded with, for display.
    /// </summary>
    private static string GetDisplayVersion(ReplayGame replay)
        => string.IsNullOrWhiteSpace(replay.GameClientVersion)
            ? "Unknown".L10N("Client:Main:ReplayUnknownVersion")
            : replay.GameClientVersion;

    private static string FormatDuration(ReplayGame replay)
    {
        if (!replay.IsComplete)
            return "?";

        TimeSpan duration = replay.Duration;
        return duration.TotalHours >= 1
            ? $"{(int)duration.TotalHours}:{duration.Minutes:D2}:{duration.Seconds:D2}"
            : $"{duration.Minutes}:{duration.Seconds:D2}";
    }
}
