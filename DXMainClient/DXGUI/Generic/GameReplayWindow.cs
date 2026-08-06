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
using System.Diagnostics;
using System.IO;
using System.Linq;
using ClientUpdater;

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
        private XNAClientCheckBox chkSpectator;
        private XNAClientCheckBox chkLockedViewport;
        private XNAClientCheckBox chkSelectUnits;

        private const string SPAWNER_BINARY_NAME = "CnCNet-Spawner.dll";
        private const string PHOBOS_BINARY_NAME = "phobos.dll";

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
            lbReplayGameList.AddColumn("REPLAY GAME NAME".L10N("Client:Main:ReplayGameNameColumnHeader"), 360);
            lbReplayGameList.AddColumn("DATE / TIME".L10N("Client:Main:ReplayGameDateTimeColumnHeader"), 154);
            lbReplayGameList.AddColumn("SPAWNER VER", 105);
            lbReplayGameList.AddColumn("PHOBOS VER", 105);
            lbReplayGameList.BackgroundTexture = AssetLoader.CreateTexture(new Color(0, 0, 0, 128), 1, 1);
            lbReplayGameList.PanelBackgroundDrawMode = PanelBackgroundImageDrawMode.STRETCHED;
            lbReplayGameList.SelectedIndexChanged += ListBox_SelectedIndexChanged;
            lbReplayGameList.AllowKeyboardInput = true;

            // Game speed dropdown
            lblGameSpeed = new XNALabel(WindowManager);
            lblGameSpeed.Name = nameof(lblGameSpeed);
            lblGameSpeed.ClientRectangle = new Rectangle(13, 305, 100, 20);
            lblGameSpeed.Text = "Game speed:".L10N("Client:Main:GameSpeed");

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

            chkSpectator = new XNAClientCheckBox(WindowManager);
            chkSpectator.Name = nameof(chkSpectator);
            chkSpectator.ClientRectangle = new Rectangle(checkboxX + 260, checkboxY + checkboxSpacing, 200, 20);
            chkSpectator.Text = "Spectator view".L10N("Client:Main:SpectatorView");
            chkSpectator.Checked = false;
            chkSpectator.ToolTipText = ("Watch as an observer: reveals the whole map and shows cloaked and " +
                "disguised units. Leave off to watch from the recording player's point of view.")
                .L10N("Client:Main:ReplaySpectatorTooltip");

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
            AddChild(chkSpectator);
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

            if (!ValidateReplayVersions(replay))
                return;

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

            // A replay only plays back correctly against the game files it was recorded with.
            // Mismatched files do not error - the replay loads with the correct players and then
            // nothing happens, because unit orders reference object IDs that no longer exist. Stop
            // here instead and say which files are wrong.
            List<string> fileMismatches = ReplayFileHashes.FindMismatches(spawnIni);
            if (fileMismatches.Count > 0)
            {
                foreach (string mismatch in fileMismatches)
                    Logger.Log("Replay file mismatch: " + mismatch);

                const int maxListed = 6;
                string details = string.Join(Environment.NewLine,
                    fileMismatches.Take(maxListed).Select(m => "  - " + m));

                if (fileMismatches.Count > maxListed)
                    details += Environment.NewLine + $"  ...and {fileMismatches.Count - maxListed} more";

                // Versions are more use than hashes for tracking down the right build, so lead with
                // them where the replay carries them. Replays recorded by the Quick Match client do
                // not currently store versions, in which case only the hashes above are available.
                string versions = BuildVersionComparison(replay);
                if (!string.IsNullOrEmpty(versions))
                    details = versions + Environment.NewLine + Environment.NewLine + details;

                // Offer to continue rather than refusing outright. The mismatch is a strong signal
                // but not a certainty - the differing file may not affect this particular replay -
                // and a tester who knows what changed should not be blocked from looking.
                var msgBox = new XNAMessageBox(WindowManager,
                    "Replay File Mismatch".L10N("Client:Main:ReplayFileMismatchTitle"),
                    string.Format(("This replay was recorded with different game files, so it will " +
                        "most likely not play back correctly - it may load and then do nothing.\n\n{0}\n\n" +
                        "Full hashes are in the client log. Updating to the same game version as the " +
                        "recording should resolve this.\n\nPlay it anyway?")
                        .L10N("Client:Main:ReplayFileMismatchText"),
                        details),
                    XNAMessageBoxButtons.YesNo);

                msgBox.YesClickedAction = _ => LaunchReplay(replay, spawnIni, spawnMapContent, replayPath);
                msgBox.Show();

                // Nothing else to do on this path - either the callback launches, or the viewer
                // declines and the window stays open (it is only disabled once the game starts).
                return;
            }

            LaunchReplay(replay, spawnIni, spawnMapContent, replayPath);
        }

        /// <summary>
        /// Finishes writing the spawn files and starts the game. Split out from the launch handler
        /// so it can also be reached from the "play anyway" answer to the file mismatch prompt.
        /// </summary>
        private void LaunchReplay(ReplayGame replay, IniFile spawnIni, string spawnMapContent, string replayPath)
        {
            spawnIni.SetStringValue("Settings", "ReplayFile", replayPath);

            int selectedSpeed = ddGameSpeed.SelectedIndex;
            spawnIni.SetIntValue("Settings", "GameSpeed", selectedSpeed);

            spawnIni.SetIntValue("Settings", "ReplayShroudEnabled", chkShroudEnabled.Checked ? 1 : 0);
            spawnIni.SetIntValue("Settings", "ReplayLockedViewport", chkLockedViewport.Checked ? 1 : 0);
            spawnIni.SetIntValue("Settings", "ReplaySelectUnits", chkSelectUnits.Checked ? 1 : 0);
            spawnIni.SetIntValue("Settings", "ReplaySpectator", chkSpectator.Checked ? 1 : 0);
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
                    sg.SpawnerVersion ?? "Unknown",
                    string.IsNullOrWhiteSpace(sg.PhobosVersion) ? "Unknown" : sg.PhobosVersion };
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

        private bool ValidateReplayVersions(ReplayGame replay)
        {
            string replaySpawnerVersion = replay.SpawnerVersion;
            string currentSpawnerVersion = GetCurrentSpawnerVersion();

            //if (!string.IsNullOrWhiteSpace(replaySpawnerVersion)
            //    && !string.IsNullOrWhiteSpace(currentSpawnerVersion)
            //    && !AreVersionStringsEquivalent(replaySpawnerVersion, currentSpawnerVersion))
            //{
            //    var msgBox = new XNAMessageBox(WindowManager, "Version Mismatch",
            //        $"Replay spawner version ({replaySpawnerVersion}) does not match local spawner version ({currentSpawnerVersion}).\n\nPlayback is blocked to prevent desync.",
            //        XNAMessageBoxButtons.OK);
            //    msgBox.Show();
            //    return false;
            //}

            //if (!string.IsNullOrWhiteSpace(replay.PhobosVersion))
            //{
            //    string currentPhobosVersion = GetCurrentPhobosVersion();
            //    if (!string.IsNullOrWhiteSpace(currentPhobosVersion)
            //        && !AreVersionStringsEquivalent(replay.PhobosVersion, currentPhobosVersion))
            //    {
            //        var msgBox = new XNAMessageBox(WindowManager, "Version Mismatch",
            //            $"Replay Phobos version ({replay.PhobosVersion}) does not match local Phobos version ({currentPhobosVersion}).\n\nPlayback is blocked to prevent desync.",
            //            XNAMessageBoxButtons.OK);
            //        msgBox.Show();
            //        return false;
            //    }
            //}

            //if (!string.IsNullOrWhiteSpace(replay.GameClientVersion))
            //{
            //    string currentGameClientVersion = GetCurrentGameClientVersion();
            //    if (!string.IsNullOrWhiteSpace(currentGameClientVersion)
            //        && !AreComparableTextEquivalent(replay.GameClientVersion, currentGameClientVersion))
            //    {
            //        var msgBox = new XNAMessageBox(WindowManager, "Version Mismatch",
            //            $"Replay game client version ({replay.GameClientVersion}) does not match local game client version ({currentGameClientVersion}).\n\nPlayback is blocked to prevent desync.",
            //            XNAMessageBoxButtons.OK);
            //        msgBox.Show();
            //        return false;
            //    }
            //}

            return true;
        }

        private static bool AreComparableTextEquivalent(string left, string right)
        {
            string normalizedLeft = NormalizeComparableText(left);
            string normalizedRight = NormalizeComparableText(right);
            return normalizedLeft.Equals(normalizedRight, StringComparison.OrdinalIgnoreCase);
        }

        private static string NormalizeComparableText(string text)
        {
            if (string.IsNullOrWhiteSpace(text))
                return string.Empty;

            string[] parts = text
                .Split(new[] { ' ', '\t', '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);
            return string.Join(" ", parts);
        }

        private static bool AreVersionStringsEquivalent(string left, string right)
        {
            if (left.Equals(right, StringComparison.OrdinalIgnoreCase))
                return true;

            if (!TryParseVersionComponents(left, out int[] leftParts)
                || !TryParseVersionComponents(right, out int[] rightParts))
            {
                return false;
            }

            int maxLength = Math.Max(leftParts.Length, rightParts.Length);
            for (int i = 0; i < maxLength; i++)
            {
                int leftValue = i < leftParts.Length ? leftParts[i] : 0;
                int rightValue = i < rightParts.Length ? rightParts[i] : 0;
                if (leftValue != rightValue)
                    return false;
            }

            return true;
        }

        private static bool TryParseVersionComponents(string versionText, out int[] components)
        {
            components = Array.Empty<int>();

            if (string.IsNullOrWhiteSpace(versionText))
                return false;

            string[] rawParts = versionText.Split('.');
            if (rawParts.Length == 0)
                return false;

            var parsed = new List<int>(rawParts.Length);
            foreach (string rawPart in rawParts)
            {
                if (!int.TryParse(rawPart.Trim(), out int value) || value < 0)
                    return false;

                parsed.Add(value);
            }

            while (parsed.Count > 0 && parsed[parsed.Count - 1] == 0)
                parsed.RemoveAt(parsed.Count - 1);

            if (parsed.Count == 0)
                parsed.Add(0);

            components = parsed.ToArray();
            return true;
        }

        /// <summary>
        /// "recorded with X, you have Y" lines for whichever versions the replay actually carries.
        /// Returns an empty string when the replay stores none of them, so the caller can fall back
        /// to showing file hashes alone.
        /// </summary>
        private string BuildVersionComparison(ReplayGame replay)
        {
            var lines = new List<string>();

            AddVersionLine(lines, "Spawner", replay.SpawnerVersion, GetCurrentSpawnerVersion());
            AddVersionLine(lines, "Phobos", replay.PhobosVersion, GetCurrentPhobosVersion());
            AddVersionLine(lines, "Game client", replay.GameClientVersion, GetCurrentGameClientVersion());

            return lines.Count == 0 ? string.Empty : string.Join(Environment.NewLine, lines);
        }

        private static void AddVersionLine(List<string> lines, string label, string recorded, string local)
        {
            if (string.IsNullOrWhiteSpace(recorded))
                return;

            string localText = string.IsNullOrWhiteSpace(local) ? "unknown" : local;
            lines.Add($"  {label}: replay {recorded.Trim()}, yours {localText.Trim()}");
        }

        private string GetCurrentSpawnerVersion()
        {
            return GetLocalBinaryVersion(SPAWNER_BINARY_NAME);
        }

        private string GetCurrentPhobosVersion()
        {
            return GetLocalBinaryVersion(PHOBOS_BINARY_NAME);
        }

        private static string GetCurrentGameClientVersion()
        {
            if (string.IsNullOrWhiteSpace(Updater.GameVersion))
                return string.Empty;

            return $"{ClientConfiguration.Instance.LocalGame} {Updater.GameVersion}".Trim();
        }

        private string GetLocalBinaryVersion(string fileName)
        {
            string filePath = Path.Combine(ProgramConstants.GamePath, fileName);
            if (!File.Exists(filePath))
                return string.Empty;

            try
            {
                FileVersionInfo fileVersionInfo = FileVersionInfo.GetVersionInfo(filePath);

                if (!string.IsNullOrWhiteSpace(fileVersionInfo.FileVersion))
                    return fileVersionInfo.FileVersion;

                if (!string.IsNullOrWhiteSpace(fileVersionInfo.ProductVersion))
                    return fileVersionInfo.ProductVersion;
            }
            catch (Exception ex)
            {
                Logger.Log($"Failed to read version from {fileName}: {ex.Message}");
            }

            return string.Empty;
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
