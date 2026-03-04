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

namespace DTAClient.DXGUI.Generic
{
    /// <summary>
    /// A window for loading replays.
    /// </summary>
    public class GameReplayWindow : XNAWindow
    {
        private const string REPLAY_GAMES_DIRECTORY = "replays";

        public GameReplayWindow(WindowManager windowManager, DiscordHandler discordHandler) : base(windowManager)
        {
            this.discordHandler = discordHandler;
        }

        private DiscordHandler discordHandler;

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
        private XNAClientCheckBox chkDebugLog;

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
            lbReplayGameList.AddColumn("REPLAY GAME NAME".L10N("Client:Main:ReplayGameNameColumnHeader"), 450);
            lbReplayGameList.AddColumn("DATE / TIME".L10N("Client:Main:ReplayGameDateTimeColumnHeader"), 174);
            lbReplayGameList.AddColumn("PHOBOS VER".L10N("Client:Main:ReplayPhobosVersionColumnHeader"), 100);
            lbReplayGameList.BackgroundTexture = AssetLoader.CreateTexture(new Color(0, 0, 0, 128), 1, 1);
            lbReplayGameList.PanelBackgroundDrawMode = PanelBackgroundImageDrawMode.STRETCHED;
            lbReplayGameList.SelectedIndexChanged += ListBox_SelectedIndexChanged;
            lbReplayGameList.AllowKeyboardInput = true;

            // Game speed dropdown
            lblGameSpeed = new XNALabel(WindowManager);
            lblGameSpeed.Name = nameof(lblGameSpeed);
            lblGameSpeed.ClientRectangle = new Rectangle(13, 305, 100, 20);
            lblGameSpeed.Text = "Game Speed (kicks in late):".L10N("Client:Main:GameSpeed");

            ddGameSpeed = new XNAClientDropDown(WindowManager);
            ddGameSpeed.Name = nameof(ddGameSpeed);
            ddGameSpeed.ClientRectangle = new Rectangle(120, 303, 150, 21);
            PopulateGameSpeedDropdown(5);

            // Playback settings checkboxes
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

            chkLockedViewport = new XNAClientCheckBox(WindowManager);
            chkLockedViewport.Name = nameof(chkLockedViewport);
            chkLockedViewport.ClientRectangle = new Rectangle(checkboxX + 260, checkboxY, 200, 20);
            chkLockedViewport.Text = "Lock viewport".L10N("Client:Main:LockViewport");
            chkLockedViewport.Checked = false;

            chkSelectUnits = new XNAClientCheckBox(WindowManager);
            chkSelectUnits.Name = nameof(chkSelectUnits);
            chkSelectUnits.ClientRectangle = new Rectangle(checkboxX, checkboxY + checkboxSpacing, 250, 20);
            chkSelectUnits.Text = "Select units".L10N("Client:Main:SelectUnits");
            chkSelectUnits.Checked = false;

            chkDebugLog = new XNAClientCheckBox(WindowManager);
            chkDebugLog.Name = nameof(chkDebugLog);
            chkDebugLog.ClientRectangle = new Rectangle(checkboxX + 260, checkboxY + checkboxSpacing, 200, 20);
            chkDebugLog.Text = "Write playbackLog.dat".L10N("Client:Main:WriteDebugLog");
            chkDebugLog.Checked = true;

            btnLaunch = new XNAClientButton(WindowManager);
            btnLaunch.Name = nameof(btnLaunch);
            btnLaunch.ClientRectangle = new Rectangle(200, 445, 110, 23);
            btnLaunch.Text = "Load".L10N("Client:Main:ButtonLoad");
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
            AddChild(chkDebugLog);
            AddChild(btnLaunch);
            AddChild(btnDelete);
            AddChild(btnCancel);

            base.Initialize();

            ListReplays();
        }

        private void ListBox_SelectedIndexChanged(object sender, EventArgs e)
        {
            if (lbReplayGameList.SelectedIndex == -1)
            {
                btnLaunch.AllowClick = false;
                btnDelete.AllowClick = false;
            }
            else
            {
                btnLaunch.AllowClick = true;
                btnDelete.AllowClick = true;

                ReplayGame replay = replays[lbReplayGameList.SelectedIndex];
                PopulateGameSpeedDropdown(replay.GameMode);
            }
        }

        private void BtnCancel_LeftClick(object sender, EventArgs e)
        {
            Disable();
        }

        private void BtnLaunch_LeftClick(object sender, EventArgs e)
        {
            ReplayGame replay = replays[lbReplayGameList.SelectedIndex];
            Logger.Log("Loading replay " + replay.FileName);

            // Validate Phobos version before launching
            // Would be good to prompt user to auto download correct Phobos version for the replay from Github.
            string currentPhobosVersion = GetCurrentPhobosVersion();
            if (replay.PhobosVersion != currentPhobosVersion)
            {
                var msgBox = new XNAMessageBox(WindowManager, "Version Mismatch".L10N("Client:Main:VersionMismatchTitle"),
                    string.Format(("Replay Phobos version ({0}) does not match current version ({1}).\n\n" +
                                 "Playback is blocked to prevent desync.").L10N("Client:Main:VersionMismatchText"),
                                 replay.PhobosVersion, currentPhobosVersion),
                    XNAMessageBoxButtons.OK);
                msgBox.Show();
                return;
            }

            // Extract spawn.ini and spawnmap.ini from the replay file
            string spawnIniContent = replay.ExtractSpawnIni();
            string spawnMapContent = replay.ExtractSpawnMap();

            if (string.IsNullOrEmpty(spawnIniContent) || string.IsNullOrEmpty(spawnMapContent))
            {
                Logger.Log("ERROR: Replay file is missing spawn file data!");
                return;
            }

            string replayPath = Path.Combine(ProgramConstants.GamePath, REPLAY_GAMES_DIRECTORY, replay.FileName);

            // Write spawn.ini from replay
            FileInfo spawnerSettingsFile = SafePath.GetFile(ProgramConstants.GamePath, ProgramConstants.SPAWNER_SETTINGS);
            if (spawnerSettingsFile.Exists)
                spawnerSettingsFile.Delete();
            File.WriteAllText(spawnerSettingsFile.FullName, spawnIniContent);

            // Update the settings fore play support
            IniFile spawnIni = new IniFile(spawnerSettingsFile.FullName);

            spawnIni.SetStringValue("Settings", "ReplayFile", replayPath);

            int selectedSpeed = ddGameSpeed.SelectedIndex;
            spawnIni.SetIntValue("Settings", "GameSpeed", selectedSpeed);

            spawnIni.SetIntValue("Settings", "ReplayShroudEnabled", chkShroudEnabled.Checked ? 1 : 0);
            spawnIni.SetIntValue("Settings", "ReplayLockedViewport", chkLockedViewport.Checked ? 1 : 0);
            spawnIni.SetIntValue("Settings", "ReplaySelectUnits", chkSelectUnits.Checked ? 1 : 0);
            spawnIni.SetIntValue("Settings", "ReplayDebugLog", chkDebugLog.Checked ? 1 : 0);
            spawnIni.SetBooleanValue("Settings", "EnableReplayRecording", false);

            spawnIni.WriteIniFile();

            // Write spawnmap.ini from replay
            FileInfo spawnMapIniFile = SafePath.GetFile(ProgramConstants.GamePath, "spawnmap.ini");
            if (spawnMapIniFile.Exists)
                spawnMapIniFile.Delete();

            File.WriteAllText(spawnMapIniFile.FullName, spawnMapContent);

            Logger.Log("Extracted spawn files from replay successfully");

            discordHandler.UpdatePresence(replay.GUIName, true);

            Disable();

            GameProcessLogic.GameProcessExited += GameProcessExited_Callback;

            // so GameLobbyBase knows not to save replay.dat
            GameProcessLogic.IsReplayPlayback = true;

            GameProcessLogic.StartGameProcess(WindowManager);
        }

        private void BtnDelete_LeftClick(object sender, EventArgs e)
        {
            ReplayGame replay = replays[lbReplayGameList.SelectedIndex];
            var msgBox = new XNAMessageBox(WindowManager, "Delete Confirmation".L10N("Client:Main:DeleteConfirmationTitle"),
                string.Format(("The following replay will be deleted permanently:\n\n" +
                    "Filename: {0}\n" +
                    "Replay game name: {1}\n" +
                    "Date and time: {2}\n\n" +
                    "Are you sure you want to proceed?").L10N("Client:Main:DeleteReplayConfirmationText"),
                    replay.FileName, Renderer.GetSafeString(replay.GUIName, lbReplayGameList.FontIndex), replay.LastModified.ToString()),
                XNAMessageBoxButtons.YesNo);
            msgBox.Show();
            msgBox.YesClickedAction = DeleteMsgBox_YesClicked;
        }

        private void DeleteMsgBox_YesClicked(XNAMessageBox obj)
        {
            ReplayGame replay = replays[lbReplayGameList.SelectedIndex];

            Logger.Log("Deleting replay " + replay.FileName);
            SafePath.DeleteFileIfExists(ProgramConstants.GamePath, REPLAY_GAMES_DIRECTORY, replay.FileName);
            ListReplays();
        }

        public override void Update(GameTime gameTime)
        {
            base.Update(gameTime);

            // Check if the window was just enabled
            if (Enabled && !wasEnabled)
            {
                // Check for replay.dat and move it if it exists
                MoveReplayFileIfExists();

                // Refresh the list to show any new replays
                ListReplays();

                // Select the first item if there are any replays
                if (lbReplayGameList.ItemCount > 0)
                {
                    lbReplayGameList.SelectedIndex = 0;
                }
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

            // Clear replay playback flag
            GameProcessLogic.IsReplayPlayback = false;

            discordHandler.UpdatePresence();
            ListReplays();
        }

        /// <summary>
        /// Checks for replay.dat and moves it to the replays directory if it exists.
        /// </summary>
        private void MoveReplayFileIfExists()
        {
            try
            {
                string replaySource = Path.Combine(ProgramConstants.GamePath, "replay.dat");
                if (File.Exists(replaySource))
                {
                    string replayDir = Path.Combine(ProgramConstants.GamePath, REPLAY_GAMES_DIRECTORY);
                    Directory.CreateDirectory(replayDir);

                    string timestamp = DateTime.Now.ToString("yyyyMMdd_HHmmss");
                    string replayDest = Path.Combine(replayDir, $"Replay_{timestamp}.yrrp");

                    // If file already exists, add a counter
                    int counter = 1;
                    string baseName = replayDest;
                    while (File.Exists(replayDest))
                    {
                        replayDest = baseName.Replace(".yrrp", $"_{counter}.yrrp");
                        counter++;
                    }

                    File.Move(replaySource, replayDest);
                    Logger.Log($"Replay saved to: {replayDest}");
                }
            }
            catch (Exception ex)
            {
                Logger.Log($"Failed to move replay.dat: {ex.Message}");
            }
        }

        public void ListReplays()
        {
            replays.Clear();
            lbReplayGameList.ClearItems();
            lbReplayGameList.SelectedIndex = -1;

            DirectoryInfo replayDirectoryInfo = SafePath.GetDirectory(ProgramConstants.GamePath, REPLAY_GAMES_DIRECTORY);

            if (!replayDirectoryInfo.Exists)
            {
                Logger.Log("Replay directory not found!");
                return;
            }

            IEnumerable<FileInfo> files = replayDirectoryInfo.EnumerateFiles("*.yrrp", SearchOption.TopDirectoryOnly);

            foreach (FileInfo file in files)
            {
                ParseReplay(file.FullName);
            }

            replays = replays.OrderBy(sg => sg.LastModified.Ticks).ToList();
            replays.Reverse();

            foreach (ReplayGame sg in replays)
            {
                string[] item = new string[] {
                    Renderer.GetSafeString(sg.GUIName, lbReplayGameList.FontIndex),
                    sg.LastModified.ToString(),
                    sg.PhobosVersion ?? "Unknown" };
                lbReplayGameList.AddItem(item, true);
            }
        }

        private void ParseReplay(string fileName)
        {
            string shortName = Path.GetFileName(fileName);

            ReplayGame replay = new ReplayGame(shortName);
            if (replay.ParseInfo())
                replays.Add(replay);
        }

        /// <summary>
        /// Gets the current Phobos version installed.
        /// TODO: Read from phobos.dll or a version file.
        /// </summary>
        private string GetCurrentPhobosVersion()
        {
            // TODO
            return "0.3.0.1"; //Phobos.version.h
        }

        /// <summary>
        /// Populates the game speed dropdown based on game mode.
        /// GameMode values: Campaign=0, LAN=3, Internet=4, Skirmish=5
        /// </summary>
        private void PopulateGameSpeedDropdown(uint gameMode)
        {
            // Store the previously selected text
            string previouslySelected = ddGameSpeed.Items.Count > 0
                ? ddGameSpeed.Items[ddGameSpeed.SelectedIndex].Text
                : null;

            ddGameSpeed.Items.Clear();

            // Populate based on game mode ---
            string[] speeds;

            if (gameMode == 5) // Skirmish (7 options)
            {
                speeds = new[]
                {
                    "Fastest", "Faster", "Fast",
                    "Normal",
                    "Slow", "Slower", "Slowest"
                };
            }
            else // LAN / Internet / Campaign (6 options)
            {
                speeds = new[]
                {
                    "Fastest", "Faster", "Fast",
                    "Normal",
                    "Slow", "Slower"
                };
            }

            foreach (var s in speeds)
                ddGameSpeed.AddItem(s);

            // Restore selection by matching text
            int restoredIndex = -1;

            if (!string.IsNullOrEmpty(previouslySelected))
            {
                for (int i = 0; i < ddGameSpeed.Items.Count; i++)
                {
                    if (string.Equals(ddGameSpeed.Items[i].Text, previouslySelected,
                                      StringComparison.OrdinalIgnoreCase))
                    {
                        restoredIndex = i;
                        break;
                    }
                }
            }

            if (restoredIndex == -1)
            {
                restoredIndex = Array.IndexOf(speeds, "Fastest");
                if (restoredIndex < 0) restoredIndex = 0; // last fallback
            }

            ddGameSpeed.SelectedIndex = restoredIndex;
        }
    }
}
