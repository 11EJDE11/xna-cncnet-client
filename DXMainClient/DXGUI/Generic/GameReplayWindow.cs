using ClientCore;
using ClientGUI;
using DTAClient.Domain;
using ClientCore.Extensions;
using Microsoft.Xna.Framework;
using Rampastring.Tools;
using Rampastring.XNAUI;
using Rampastring.XNAUI.XNAControls;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using ClientUpdater;

namespace DTAClient.DXGUI.Generic
{
    public class GameReplayWindow : XNAWindow
    {
        private const string REPLAY_DIR = "replays";
        private const string CURRENT_DIR = "replays/current";

        public GameReplayWindow(WindowManager windowManager, DiscordHandler discordHandler) : base(windowManager)
        {
            this.discordHandler = discordHandler;
        }

        private readonly DiscordHandler discordHandler;

        private XNAMultiColumnListBox lbReplayGameList;
        private XNAClientButton btnLaunch;
        private XNAClientButton btnDelete;
        private XNAClientButton btnCancel;

        private XNALabel lblGameSpeed;
        private XNAClientDropDown ddGameSpeed;

        private XNALabel lblPlaybackSettings;
        private XNAClientCheckBox chkShroudEnabled;
        private XNAClientCheckBox chkLockedViewport;
        private XNAClientCheckBox chkSelectUnits;

        private List<ReplayGame> replays = new List<ReplayGame>();
        private bool wasEnabled = false;

        public override void Initialize()
        {
            Name = "GameReplayWindow";
            BackgroundTexture = AssetLoader.LoadTexture("loadmissionbg.png");

            ClientRectangle = new Rectangle(0, 0, 750, 480);
            CenterOnParent();

            lbReplayGameList = new XNAMultiColumnListBox(WindowManager);
            lbReplayGameList.Name = nameof(lbReplayGameList);
            lbReplayGameList.ClientRectangle = new Rectangle(13, 13, 724, 280);
            lbReplayGameList.AddColumn("REPLAY GAME NAME".L10N("Client:Main:ReplayGameNameColumnHeader"), 330);
            lbReplayGameList.AddColumn("DATE / TIME".L10N("Client:Main:ReplayGameDateTimeColumnHeader"), 140);
            lbReplayGameList.AddColumn("SPAWNER VER", 86);
            lbReplayGameList.AddColumn("ARES VER", 80);
            lbReplayGameList.AddColumn("PHOBOS VER", 88);
            lbReplayGameList.BackgroundTexture = AssetLoader.CreateTexture(new Color(0, 0, 0, 128), 1, 1);
            lbReplayGameList.PanelBackgroundDrawMode = PanelBackgroundImageDrawMode.STRETCHED;
            lbReplayGameList.SelectedIndexChanged += ListBox_SelectedIndexChanged;
            lbReplayGameList.AllowKeyboardInput = true;

            lblGameSpeed = new XNALabel(WindowManager);
            lblGameSpeed.Name = nameof(lblGameSpeed);
            lblGameSpeed.ClientRectangle = new Rectangle(13, 305, 100, 20);
            lblGameSpeed.Text = "Game speed:".L10N("Client:Main:GameSpeed");

            ddGameSpeed = new XNAClientDropDown(WindowManager);
            ddGameSpeed.Name = nameof(ddGameSpeed);
            ddGameSpeed.ClientRectangle = new Rectangle(120, 303, 150, 21);
            PopulateGameSpeedDropdown(5);

            lblPlaybackSettings = new XNALabel(WindowManager);
            lblPlaybackSettings.Name = nameof(lblPlaybackSettings);
            lblPlaybackSettings.ClientRectangle = new Rectangle(13, 335, 150, 20);
            lblPlaybackSettings.Text = "Playback Settings:".L10N("Client:Main:PlaybackSettings");

            int checkboxX = 13;
            int checkboxY = 360;
            int checkboxSpacing = 25;

            chkShroudEnabled = new XNAClientCheckBox(WindowManager);
            chkShroudEnabled.Name = nameof(chkShroudEnabled);
            chkShroudEnabled.ClientRectangle = new Rectangle(checkboxX, checkboxY, 250, 20);
            chkShroudEnabled.Text = "Enable shroud".L10N("Client:Main:EnableShroud");
            chkShroudEnabled.Checked = false;
            chkShroudEnabled.ToolTipText = "Fog of war will be enabled for the recording player.".L10N("Client:Main:ReplayShroudTooltip");

            chkLockedViewport = new XNAClientCheckBox(WindowManager);
            chkLockedViewport.Name = nameof(chkLockedViewport);
            chkLockedViewport.ClientRectangle = new Rectangle(checkboxX + 260, checkboxY, 200, 20);
            chkLockedViewport.Text = "Lock viewport".L10N("Client:Main:LockViewport");
            chkLockedViewport.Checked = false;
            chkLockedViewport.ToolTipText = "Locks the viewport to what the recording player was seeing.".L10N("Client:Main:ReplayLockViewportTooltip");

            chkSelectUnits = new XNAClientCheckBox(WindowManager);
            chkSelectUnits.Name = nameof(chkSelectUnits);
            chkSelectUnits.ClientRectangle = new Rectangle(checkboxX, checkboxY + checkboxSpacing, 250, 20);
            chkSelectUnits.Text = "Select units".L10N("Client:Main:SelectUnits");
            chkSelectUnits.Checked = false;
            chkSelectUnits.ToolTipText = "Shows which units were selected by the recording player.".L10N("Client:Main:ReplaySelectUnitsTooltip");

            btnLaunch = new XNAClientButton(WindowManager);
            btnLaunch.Name = nameof(btnLaunch);
            btnLaunch.ClientRectangle = new Rectangle(200, 445, 110, 23);
            btnLaunch.Text = "Watch".L10N("Client:Main:ButtonWatchReplay");
            btnLaunch.AllowClick = false;
            btnLaunch.LeftClick += BtnLaunch_LeftClick;

            btnDelete = new XNAClientButton(WindowManager);
            btnDelete.Name = nameof(btnDelete);
            btnDelete.ClientRectangle = new Rectangle(btnLaunch.Right + 10, btnLaunch.Y, 110, 23);
            btnDelete.Text = "Delete".L10N("Client:Main:ButtonDelete");
            btnDelete.AllowClick = false;
            btnDelete.LeftClick += BtnDelete_LeftClick;

            btnCancel = new XNAClientButton(WindowManager);
            btnCancel.Name = nameof(btnCancel);
            btnCancel.ClientRectangle = new Rectangle(btnDelete.Right + 10, btnLaunch.Y, 110, 23);
            btnCancel.Text = "Cancel".L10N("Client:Main:ButtonCancel");
            btnCancel.LeftClick += BtnCancel_LeftClick;

            AddChild(lbReplayGameList);
            AddChild(lblGameSpeed);
            AddChild(ddGameSpeed);
            AddChild(lblPlaybackSettings);
            AddChild(chkShroudEnabled);
            AddChild(chkLockedViewport);
            AddChild(chkSelectUnits);
            AddChild(btnLaunch);
            AddChild(btnDelete);
            AddChild(btnCancel);

            base.Initialize();
        }

        private void ListBox_SelectedIndexChanged(object sender, EventArgs e)
        {
            bool hasSelection = lbReplayGameList.SelectedIndex >= 0;
            btnLaunch.AllowClick = hasSelection;
            btnDelete.AllowClick = hasSelection;

            if (hasSelection)
                PopulateGameSpeedDropdown(replays[lbReplayGameList.SelectedIndex].GameMode);
        }

        private void BtnCancel_LeftClick(object sender, EventArgs e) => Disable();

        private void BtnLaunch_LeftClick(object sender, EventArgs e)
        {
            ReplayGame replay = replays[lbReplayGameList.SelectedIndex];

            VersionCheckResult versionResult = replay.CheckVersions(ProgramConstants.GamePath);
            if (versionResult.HasAnyMismatch)
            {
                string warningText =
                    "This replay was recorded with different DLL versions:\n\n"
                    + versionResult.ToWarningText()
                    + "\n\nPlayback may desync. Launch anyway?";

                var msgBox = new XNAMessageBox(WindowManager,
                    "Version Mismatch".L10N("Client:Main:VersionMismatchTitle"),
                    warningText,
                    XNAMessageBoxButtons.YesNo);
                msgBox.Show();
                msgBox.YesClickedAction = _ => LaunchReplay(replay);
            }
            else
            {
                LaunchReplay(replay);
            }
        }

        private void LaunchReplay(ReplayGame replay)
        {
            Logger.Log($"Launching replay: {replay.FileName}");

            string spawnIniContent = replay.GetSpawnIni();
            string spawnMapContent = replay.GetSpawnMap();
            byte[] eventData = replay.GetEventData();

            if (string.IsNullOrEmpty(spawnIniContent))
            {
                Logger.Log("ERROR: Replay file is missing spawn.ini data");
                XNAMessageBox.Show(WindowManager,
                    "Error".L10N("Client:Main:Error"),
                    "This replay file is corrupt or missing spawn configuration data.");
                return;
            }

            // Write event stream to temp extraction directory
            string currentDir = SafePath.GetDirectory(ProgramConstants.GamePath, CURRENT_DIR).FullName;
            Directory.CreateDirectory(currentDir);

            string eventsPath = Path.Combine(currentDir, "events.dat");
            if (eventData.Length > 0)
                File.WriteAllBytes(eventsPath, eventData);
            else
                Logger.Log("WARNING: Replay has no event data (eventDataSize=0)");

            // Write spawn.ini extracted from replay
            FileInfo spawnerSettingsFile = SafePath.GetFile(ProgramConstants.GamePath, ProgramConstants.SPAWNER_SETTINGS);
            if (spawnerSettingsFile.Exists)
                spawnerSettingsFile.Delete();
            File.WriteAllText(spawnerSettingsFile.FullName, spawnIniContent, System.Text.Encoding.UTF8);

            // Patch spawn.ini for playback
            IniFile spawnIni = new IniFile(spawnerSettingsFile.FullName);
            spawnIni.SetBooleanValue("Settings", "IsReplayPlayback", true);
            spawnIni.SetStringValue("Settings", "ReplayDataDir", currentDir);
            spawnIni.SetBooleanValue("Settings", "EnableReplayRecording", false);
            spawnIni.SetIntValue("Settings", "GameSpeed", ddGameSpeed.SelectedIndex);
            spawnIni.SetIntValue("Settings", "ReplayShroudEnabled", chkShroudEnabled.Checked ? 1 : 0);
            spawnIni.SetIntValue("Settings", "ReplayLockedViewport", chkLockedViewport.Checked ? 1 : 0);
            spawnIni.SetIntValue("Settings", "ReplaySelectUnits", chkSelectUnits.Checked ? 1 : 0);
            spawnIni.WriteIniFile();

            // Write spawnmap.ini extracted from replay
            if (!string.IsNullOrEmpty(spawnMapContent))
            {
                string spawnMapPath = Path.Combine(ProgramConstants.GamePath, "spawnmap.ini");
                File.WriteAllText(spawnMapPath, spawnMapContent, System.Text.Encoding.UTF8);
            }

            Logger.Log($"Replay extracted to {currentDir}");

            discordHandler.UpdatePresence(replay.GUIName, true);
            Disable();

            GameProcessLogic.GameProcessExited += GameProcessExited_Callback;
            GameProcessLogic.IsReplayPlayback = true;
            GameProcessLogic.StartGameProcess(WindowManager);
        }

        private void BtnDelete_LeftClick(object sender, EventArgs e)
        {
            ReplayGame replay = replays[lbReplayGameList.SelectedIndex];
            var msgBox = new XNAMessageBox(WindowManager,
                "Delete Confirmation".L10N("Client:Main:DeleteConfirmationTitle"),
                string.Format(("The following replay will be deleted permanently:\n\n" +
                    "Filename: {0}\n" +
                    "Replay game name: {1}\n" +
                    "Date and time: {2}\n\n" +
                    "Are you sure you want to proceed?").L10N("Client:Main:DeleteReplayConfirmationText"),
                    replay.FileName,
                    Renderer.GetSafeString(replay.GUIName, lbReplayGameList.FontIndex),
                    replay.LastModified.ToString()),
                XNAMessageBoxButtons.YesNo);
            msgBox.Show();
            msgBox.YesClickedAction = DeleteMsgBox_YesClicked;
        }

        private void DeleteMsgBox_YesClicked(XNAMessageBox obj)
        {
            ReplayGame replay = replays[lbReplayGameList.SelectedIndex];
            Logger.Log("Deleting replay " + replay.FileName);
            SafePath.DeleteFileIfExists(ProgramConstants.GamePath, REPLAY_DIR, replay.FileName);
            ListReplays();
        }

        public override void Update(GameTime gameTime)
        {
            base.Update(gameTime);

            if (Enabled && !wasEnabled)
            {
                ListReplays();
                if (lbReplayGameList.ItemCount > 0)
                    lbReplayGameList.SelectedIndex = 0;
            }

            wasEnabled = Enabled;
        }

        private void GameProcessExited_Callback()
        {
            WindowManager.AddCallback(new Action(GameProcessExited), null);
        }

        protected virtual void GameProcessExited()
        {
            GameProcessLogic.GameProcessExited -= GameProcessExited_Callback;
            GameProcessLogic.IsReplayPlayback = false;
            discordHandler.UpdatePresence();
            ListReplays();
        }

        public void ListReplays()
        {
            replays.Clear();
            lbReplayGameList.ClearItems();
            lbReplayGameList.SelectedIndex = -1;

            DirectoryInfo replayDir = SafePath.GetDirectory(ProgramConstants.GamePath, REPLAY_DIR);
            if (!replayDir.Exists)
                return;

            foreach (FileInfo file in replayDir.EnumerateFiles("*.yrrp", SearchOption.TopDirectoryOnly))
            {
                var replay = new ReplayGame(file.Name);
                if (replay.ParseInfo())
                    replays.Add(replay);
            }

            replays = replays.OrderByDescending(r => r.LastModified.Ticks).ToList();

            foreach (ReplayGame r in replays)
            {
                lbReplayGameList.AddItem(new[]
                {
                    Renderer.GetSafeString(r.GUIName, lbReplayGameList.FontIndex),
                    r.LastModified.ToString(),
                    r.SpawnerVersion ?? "?",
                    string.IsNullOrWhiteSpace(r.AresVersion) || r.AresVersion == "N/A" ? "N/A" : r.AresVersion,
                    string.IsNullOrWhiteSpace(r.PhobosVersion) || r.PhobosVersion == "N/A" ? "N/A" : r.PhobosVersion
                }, true);
            }
        }

        private void PopulateGameSpeedDropdown(uint gameMode)
        {
            string previouslySelected = ddGameSpeed.Items.Count > 0
                ? ddGameSpeed.Items[ddGameSpeed.SelectedIndex].Text
                : null;

            ddGameSpeed.Items.Clear();

            string[] speeds = gameMode == 5  // Skirmish has 7 speed options
                ? new[] { "Fastest", "Faster", "Fast", "Normal", "Slow", "Slower", "Slowest" }
                : new[] { "Fastest", "Faster", "Fast", "Normal", "Slow", "Slower" };

            foreach (string s in speeds)
                ddGameSpeed.AddItem(s);

            int idx = 0; // default to Fastest
            if (!string.IsNullOrEmpty(previouslySelected))
            {
                for (int i = 0; i < speeds.Length; i++)
                {
                    if (string.Equals(speeds[i], previouslySelected, StringComparison.OrdinalIgnoreCase))
                    {
                        idx = i;
                        break;
                    }
                }
            }
            ddGameSpeed.SelectedIndex = idx;
        }
    }
}
