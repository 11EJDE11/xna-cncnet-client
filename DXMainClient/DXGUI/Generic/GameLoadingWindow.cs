#nullable enable

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

using ClientCore;
using ClientCore.Extensions;

using ClientGUI;

using DTAClient.Domain;
using DTAClient.DXGUI.Campaign;
using DTAClient.DXGUI.Generic.LoadGamePanels;

using Microsoft.Xna.Framework;

using Rampastring.Tools;
using Rampastring.XNAUI;
using Rampastring.XNAUI.XNAControls;

namespace DTAClient.DXGUI.Generic
{
    /// <summary>
    /// A window for loading saved singleplayer games and replays.
    /// </summary>
    public class GameLoadingWindow : XNAWindow
    {
        private const string SAVED_GAMES_DIRECTORY = "Saved Games";

        private const int REPLAY_WINDOW_WIDTH = 700;
        private const int REPLAY_WINDOW_HEIGHT = 540;
        private const int BUTTON_WIDTH = 110;
        private const int BUTTON_HEIGHT = 23;
        private const int BUTTON_SPACING = 10;
        private const int MARGIN = 12;
        private const int REPLAYS_TAB_INDEX = 1;
        private const int DATE_COLUMN_WIDTH = 174;

        public GameLoadingWindow(WindowManager windowManager, DiscordHandler discordHandler,
            CampaignTagSelector campaignTagSelector) : base(windowManager)
        {
            this.discordHandler = discordHandler;
            this.campaignTagSelector = campaignTagSelector;
        }

        private readonly DiscordHandler discordHandler;
        private readonly CampaignTagSelector campaignTagSelector;

        private XNAClientTabControl? tabControl;
        private ReplaysPanel? replaysPanel;

        private XNAMultiColumnListBox lbSaveGameList = null!;
        private XNAClientButton btnLaunch = null!;
        private XNAClientButton btnDelete = null!;
        private XNAClientButton btnCancel = null!;
        private XNAClientButton? btnOpenReplayFolder;

        private List<SavedGame> savedGames = new List<SavedGame>();
        private bool initialized;

        private bool IsReplayTabSelected => tabControl?.SelectedTab == REPLAYS_TAB_INDEX;

        public override void Initialize()
        {
            Name = "GameLoadingWindow";
            BackgroundTexture = AssetLoader.LoadTexture("loadmissionbg.png");

            bool replaySupport = ReplayManager.IsSupported;

            ClientRectangle = replaySupport
                ? new Rectangle(0, 0, REPLAY_WINDOW_WIDTH, REPLAY_WINDOW_HEIGHT)
                : new Rectangle(0, 0, 600, 380);
            CenterOnParent();

            if (replaySupport)
                CreateReplayControls();

            CreateSaveGameList(replaySupport);
            CreateButtons(replaySupport);

            if (tabControl != null)
                AddChild(tabControl);

            AddChild(lbSaveGameList);

            if (replaysPanel != null)
            {
                AddChild(replaysPanel);
                replaysPanel.Disable();
            }

            AddChild(btnLaunch);
            AddChild(btnDelete);
            AddChild(btnCancel);

            if (btnOpenReplayFolder != null)
            {
                AddChild(btnOpenReplayFolder);
                btnOpenReplayFolder.Disable();
            }

            base.Initialize();

            initialized = true;
            ListSaves();
            UpdateButtonStates();
        }

        private void CreateReplayControls()
        {
            tabControl = new XNAClientTabControl(WindowManager);
            tabControl.Name = nameof(tabControl);
            tabControl.ClientRectangle = new Rectangle(MARGIN, MARGIN, 0, BUTTON_HEIGHT);
            tabControl.FontIndex = 1;
            tabControl.ClickSound = new EnhancedSoundEffect("button.wav");
            tabControl.AddTab("Saved Games".L10N("Client:Main:TabSavedGames"), UIDesignConstants.BUTTON_WIDTH_133);
            tabControl.AddTab("Replays".L10N("Client:Main:TabReplays"), UIDesignConstants.BUTTON_WIDTH_133);
            tabControl.SelectedIndexChanged += TabControl_SelectedIndexChanged;

            replaysPanel = new ReplaysPanel(WindowManager, discordHandler);
            replaysPanel.ClientRectangle = GetReplayContentRectangle();
            replaysPanel.SelectionChanged += (_, _) => UpdateButtonStates();
            replaysPanel.LaunchRequested += (_, _) => Disable();
        }

        private void CreateSaveGameList(bool replaySupport)
        {
            Rectangle listRectangle = replaySupport
                ? GetReplayContentRectangle()
                : new Rectangle(13, 13, 574, 317);

            lbSaveGameList = new XNAMultiColumnListBox(WindowManager);
            lbSaveGameList.Name = nameof(lbSaveGameList);
            lbSaveGameList.ClientRectangle = listRectangle;
            lbSaveGameList.AddColumn("SAVED GAME NAME".L10N("Client:Main:SavedGameNameColumnHeader"),
                listRectangle.Width - DATE_COLUMN_WIDTH);
            lbSaveGameList.AddColumn("DATE / TIME".L10N("Client:Main:SavedGameDateTimeColumnHeader"), DATE_COLUMN_WIDTH);
            lbSaveGameList.BackgroundTexture = AssetLoader.CreateTexture(new Color(0, 0, 0, 128), 1, 1);
            lbSaveGameList.PanelBackgroundDrawMode = PanelBackgroundImageDrawMode.STRETCHED;
            lbSaveGameList.SelectedIndexChanged += (_, _) => UpdateButtonStates();
            lbSaveGameList.AllowKeyboardInput = true;
        }

        private void CreateButtons(bool replaySupport)
        {
            int buttonY = replaySupport ? Height - BUTTON_HEIGHT - MARGIN : 345;
            int launchX = replaySupport
                ? (Width - ((BUTTON_WIDTH * 3) + (BUTTON_SPACING * 2))) / 2
                : 125;

            btnLaunch = new XNAClientButton(WindowManager);
            btnLaunch.Name = nameof(btnLaunch);
            btnLaunch.ClientRectangle = new Rectangle(launchX, buttonY, BUTTON_WIDTH, BUTTON_HEIGHT);
            btnLaunch.Text = "Load".L10N("Client:Main:ButtonLoad");
            btnLaunch.AllowClick = false;
            btnLaunch.LeftClick += BtnLaunch_LeftClick;

            btnDelete = new XNAClientButton(WindowManager);
            btnDelete.Name = nameof(btnDelete);
            btnDelete.ClientRectangle = new Rectangle(btnLaunch.Right + BUTTON_SPACING, buttonY,
                BUTTON_WIDTH, BUTTON_HEIGHT);
            btnDelete.Text = "Delete".L10N("Client:Main:ButtonDelete");
            btnDelete.AllowClick = false;
            btnDelete.LeftClick += BtnDelete_LeftClick;

            btnCancel = new XNAClientButton(WindowManager);
            btnCancel.Name = nameof(btnCancel);
            btnCancel.ClientRectangle = new Rectangle(btnDelete.Right + BUTTON_SPACING, buttonY,
                BUTTON_WIDTH, BUTTON_HEIGHT);
            btnCancel.Text = "Cancel".L10N("Client:Main:ButtonCancel");
            btnCancel.LeftClick += (_, _) => Disable();

            if (!replaySupport)
                return;

            btnOpenReplayFolder = new XNAClientButton(WindowManager);
            btnOpenReplayFolder.Name = nameof(btnOpenReplayFolder);
            btnOpenReplayFolder.ClientRectangle = new Rectangle(MARGIN, buttonY, BUTTON_WIDTH, BUTTON_HEIGHT);
            btnOpenReplayFolder.Text = "Open Folder".L10N("Client:Main:ReplayOpenFolder");
            btnOpenReplayFolder.LeftClick += (_, _) => ReplayManager.OpenDirectory();
        }

        private Rectangle GetReplayContentRectangle()
        {
            int contentTop = tabControl!.Bottom + MARGIN;
            int contentHeight = Height - BUTTON_HEIGHT - (MARGIN * 2) - contentTop;

            return new Rectangle(MARGIN, contentTop, Width - (MARGIN * 2), contentHeight);
        }

        public void Open() => Enable();

        public void ListSaves()
        {
            savedGames.Clear();
            lbSaveGameList.ClearItems();
            lbSaveGameList.SelectedIndex = -1;

            DirectoryInfo savedGamesDirectoryInfo = SafePath.GetDirectory(ProgramConstants.GamePath, SAVED_GAMES_DIRECTORY);

            if (!savedGamesDirectoryInfo.Exists)
            {
                Logger.Log("Saved Games directory not found!");
                return;
            }

            IEnumerable<FileInfo> files = savedGamesDirectoryInfo.EnumerateFiles("*.SAV", SearchOption.TopDirectoryOnly);

            foreach (FileInfo file in files)
                ParseSaveGame(file.FullName);

            savedGames = savedGames.OrderByDescending(savedGame => savedGame.LastModified.Ticks).ToList();

            foreach (SavedGame savedGame in savedGames)
            {
                string[] item =
                {
                    Renderer.GetSafeString(savedGame.GUIName, lbSaveGameList.FontIndex),
                    savedGame.LastModified.ToString()
                };
                lbSaveGameList.AddItem(item, true);
            }
        }

        protected override void OnEnabledChanged(object sender, EventArgs args)
        {
            base.OnEnabledChanged(sender, args);

            if (Enabled && initialized)
                RefreshSelectedTab();
        }

        private void TabControl_SelectedIndexChanged(object sender, EventArgs e)
        {
            if (IsReplayTabSelected)
            {
                lbSaveGameList.Disable();
                replaysPanel!.Enable();
                btnOpenReplayFolder!.Enable();
            }
            else
            {
                replaysPanel!.Disable();
                btnOpenReplayFolder!.Disable();
                lbSaveGameList.Enable();
            }

            RefreshSelectedTab();
        }

        private void RefreshSelectedTab()
        {
            if (IsReplayTabSelected)
                replaysPanel!.Refresh();
            else
                ListSaves();

            UpdateButtonStates();
        }

        private void UpdateButtonStates()
        {
            if (!initialized)
                return;

            if (IsReplayTabSelected)
            {
                btnLaunch.Text = replaysPanel!.LaunchButtonText;
                btnLaunch.AllowClick = replaysPanel.CanLaunch;
                btnDelete.AllowClick = replaysPanel.CanDelete;
                return;
            }

            btnLaunch.Text = "Load".L10N("Client:Main:ButtonLoad");
            btnLaunch.AllowClick = lbSaveGameList.SelectedIndex > -1;
            btnDelete.AllowClick = lbSaveGameList.SelectedIndex > -1;
        }

        private void BtnLaunch_LeftClick(object? sender, InputEventArgs e)
        {
            if (IsReplayTabSelected)
            {
                replaysPanel!.Launch();
                return;
            }

            LaunchSavedGame();
        }

        private void BtnDelete_LeftClick(object? sender, InputEventArgs e)
        {
            if (IsReplayTabSelected)
            {
                replaysPanel!.Delete();
                return;
            }

            DeleteSavedGame();
        }

        private void LaunchSavedGame()
        {
            if (lbSaveGameList.SelectedIndex < 0 || lbSaveGameList.SelectedIndex >= savedGames.Count)
                return;

            SavedGame savedGame = savedGames[lbSaveGameList.SelectedIndex];
            Logger.Log("Loading saved game " + savedGame.FileName);

            Mission? mission = campaignTagSelector.UniqueIDToMissions.GetValueOrDefault(savedGame.CustomMissionID, null);

            CustomMissionHelper.DeleteSupplementalMissionFiles();

            if (mission != null)
                CustomMissionHelper.CopySupplementalMissionFiles(mission);

            FileInfo spawnerSettingsFile = SafePath.GetFile(ProgramConstants.GamePath, ProgramConstants.SPAWNER_SETTINGS);

            if (spawnerSettingsFile.Exists)
                spawnerSettingsFile.Delete();

            IniFile spawnIni = new()
            {
                Comment = "Generated by CnCNet Client"
            };

            IniSection spawnIniSettings = new("Settings");
            spawnIniSettings.AddKey("Scenario", "spawnmap.ini");
            spawnIniSettings.AddKey("SaveGameName", savedGame.FileName);
            spawnIniSettings.AddKey("LoadSaveGame", "Yes");
            spawnIniSettings.AddKey("SidebarHack", ClientConfiguration.Instance.SidebarHack.ToString());
            spawnIniSettings.AddKey("CustomLoadScreen", LoadingScreenController.GetLoadScreenName("g"));
            spawnIniSettings.AddKey("Firestorm", "No");
            spawnIniSettings.AddKey("GameSpeed", UserINISettings.Instance.GameSpeed.ToString());

            spawnIni.AddSection(spawnIniSettings);

            if (mission != null)
            {
                spawnIniSettings.AddKey("CustomMissionID", savedGame.CustomMissionID.ToString());
                CampaignSelector.WriteMissionSectionToSpawnIni(spawnIni, mission);
            }

            spawnIni.WriteIniFile(spawnerSettingsFile.FullName);

            FileInfo spawnMapIniFile = SafePath.GetFile(ProgramConstants.GamePath, "spawnmap.ini");

            if (spawnMapIniFile.Exists)
                spawnMapIniFile.Delete();

            using (var spawnMapStreamWriter = new StreamWriter(spawnMapIniFile.FullName))
            {
                spawnMapStreamWriter.WriteLine("[Map]");
                spawnMapStreamWriter.WriteLine("Size=0,0,50,50");
                spawnMapStreamWriter.WriteLine("LocalSize=0,0,50,50");
                spawnMapStreamWriter.WriteLine();
            }

            discordHandler.UpdatePresence(savedGame.GUIName, true);

            Disable();
            GameProcessLogic.GameProcessExited += GameProcessExited_Callback;
            GameProcessLogic.StartGameProcess(WindowManager);
        }

        private void DeleteSavedGame()
        {
            if (lbSaveGameList.SelectedIndex < 0 || lbSaveGameList.SelectedIndex >= savedGames.Count)
                return;

            SavedGame savedGame = savedGames[lbSaveGameList.SelectedIndex];
            var msgBox = new XNAMessageBox(WindowManager,
                "Delete Confirmation".L10N("Client:Main:DeleteConfirmationTitle"),
                string.Format(("The following saved game will be deleted permanently:\n\n" +
                    "Filename: {0}\n" +
                    "Saved game name: {1}\n" +
                    "Date and time: {2}\n\n" +
                    "Are you sure you want to proceed?").L10N("Client:Main:DeleteConfirmationText"),
                    savedGame.FileName, Renderer.GetSafeString(savedGame.GUIName, lbSaveGameList.FontIndex),
                    savedGame.LastModified.ToString()),
                XNAMessageBoxButtons.YesNo);
            msgBox.YesClickedAction = DeleteMsgBox_YesClicked;
            msgBox.Show();
        }

        private void DeleteMsgBox_YesClicked(XNAMessageBox messageBox)
        {
            SavedGame savedGame = savedGames[lbSaveGameList.SelectedIndex];

            Logger.Log("Deleting saved game " + savedGame.FileName);
            SafePath.DeleteFileIfExists(ProgramConstants.GamePath, SAVED_GAMES_DIRECTORY, savedGame.FileName);
            ListSaves();
        }

        private void GameProcessExited_Callback()
        {
            WindowManager.AddCallback(new Action(GameProcessExited), null);
        }

        protected virtual void GameProcessExited()
        {
            GameProcessLogic.GameProcessExited -= GameProcessExited_Callback;

            CustomMissionHelper.DeleteSupplementalMissionFiles();

            discordHandler.UpdatePresence();
        }

        private void ParseSaveGame(string fileName)
        {
            string shortName = Path.GetFileName(fileName);

            SavedGame savedGame = new SavedGame(shortName);
            if (savedGame.ParseInfo())
                savedGames.Add(savedGame);
        }
    }
}
