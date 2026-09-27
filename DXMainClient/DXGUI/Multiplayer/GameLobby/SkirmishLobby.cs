using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;

using ClientCore;
using ClientCore.Extensions;
using ClientCore.Statistics;

using ClientGUI;

using DTAClient.Domain;
using DTAClient.Domain.Multiplayer;
using DTAClient.DXGUI.Generic;

using Microsoft.Xna.Framework;

using Rampastring.Tools;
using Rampastring.XNAUI;

namespace DTAClient.DXGUI.Multiplayer.GameLobby
{
    public class SkirmishLobby : GameLobbyBase, ISwitchable
    {
        private const string SETTINGS_PATH = "Client/SkirmishSettings.ini";

        public SkirmishLobby(WindowManager windowManager, TopBar topBar, MapLoader mapLoader, DiscordHandler discordHandler, Random random)
            : base(windowManager, "SkirmishLobby", mapLoader, false, discordHandler, random)
        {
            this.topBar = topBar;
            this.random = random;
        }

        public event EventHandler Exited;

        private Random random;

        TopBar topBar;

        public override void Initialize()
        {
            base.Initialize();

            RandomSeed = random.Next();

            //InitPlayerOptionDropdowns(128, 98, 90, 48, 55, new Point(6, 24));
            InitPlayerOptionDropdowns();

            btnLeaveGame.Text = "Main Menu".L10N("Client:Main:MainMenu");

            //MapPreviewBox.EnableContextMenu = true;

            const string spectatorName = "Spectator";
            AddSideToDropDown(ddPlayerSides[0], spectatorName, spectatorName.L10N("Client:Sides:SpectatorSide"), AssetLoader.LoadTexture("spectatoricon.png"));

            MapPreviewBox.LocalStartingLocationSelected += MapPreviewBox_LocalStartingLocationSelected;

            WindowManager.CenterControlOnScreen(this);

            LoadSettings();

            RefreshPlayerSlots();

            ProgramConstants.PlayerNameChanged += ProgramConstants_PlayerNameChanged;
            ddPlayerSides[0].SelectedIndexChanged += PlayerSideChanged;

            PlayerExtraOptionsPanel?.SetIsHost(true);
        }

        protected override void ToggleFavoriteMap()
        {
            base.ToggleFavoriteMap();

            if (GameModeMap != null && GameModeMap.IsFavorite)
                return;

            RefreshForFavoriteMapRemoved();
        }

        protected override void AddNotice(string message, Color color)
        {
            Dialogs.ShowMessage("Message".L10N("Client:Main:MessageTitle"), message);
        }

        protected override void OnEnabledChanged(object sender, EventArgs args)
        {
            base.OnEnabledChanged(sender, args);
            if (Enabled)
                UpdateDiscordPresence(true);
            else
                ResetDiscordPresence();
        }

        private void ProgramConstants_PlayerNameChanged(object sender, EventArgs e)
        {
            Players[0].Name = ProgramConstants.PLAYERNAME;
            RefreshPlayerSlots();
        }

        private void MapPreviewBox_LocalStartingLocationSelected(object sender, LocalStartingLocationEventArgs e)
        {
            // The index is already the start drop-down index: the waypoint + 1, or 0 when cleared
            Players[0].StartingLocation = e.StartingLocationIndex;
            RefreshPlayerSlots();
        }

        private string CheckGameValidity()
        {
            int totalPlayerCount = Players.Count(p => p.SideId < SlotIndices.SpectatorSide)
                + AIPlayers.Count;

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

                IEnumerable<PlayerInfo> concatList = Players.Concat(AIPlayers);

                foreach (PlayerInfo pInfo in concatList)
                {
                    if (pInfo.StartingLocation == 0)
                        continue;

                    if (concatList.Count(p => p.StartingLocation == pInfo.StartingLocation) > 1)
                    {
                        return "Multiple players cannot share the same starting location on the selected map.".L10N("Client:Main:StartLocationOccupied");
                    }
                }
            }

            if (GameModeMap.IsCoop && Players[0].SideId == SlotIndices.SpectatorSide)
            {
                return "Co-op missions cannot be spectated. You'll have to show a bit more effort to cheat here.".L10N("Client:Main:CoOpMissionSpectatorPrompt");
            }

            var teamMappingsError = GetTeamMappingsError();
            if (!string.IsNullOrEmpty(teamMappingsError))
                return teamMappingsError;

            return null;
        }

        protected override void BtnLaunchGame_LeftClick(object sender, EventArgs e)
        {
            string error = CheckGameValidity();

            if (error == null)
            {
                SaveSettings();
                StartGame();
                return;
            }

            Dialogs.ShowMessage("Cannot launch game".L10N("Client:Main:LaunchGameErrorTitle"), error);
        }

        protected override void BtnLeaveGame_LeftClick(object sender, EventArgs e)
        {
            Exited?.Invoke(this, EventArgs.Empty);

            PlayerExtraOptionsPanel?.Disable();
            Disable();

            topBar.RemovePrimarySwitchable(this);
            ResetDiscordPresence();
        }

        private void PlayerSideChanged(object sender, EventArgs e)
        {
            UpdateDiscordPresence();
        }

        protected override void UpdateDiscordPresence(bool resetTimer = false)
        {
            if (discordHandler == null || Map == null || GameMode == null || !Initialized)
                return;

            int playerIndex = Players.FindIndex(p => p.Name == ProgramConstants.PLAYERNAME);
            if (playerIndex >= MAX_PLAYER_COUNT || playerIndex < 0)
                return;

            XNAClientDropDown sideDropDown = ddPlayerSides[playerIndex];
            if (sideDropDown.SelectedItem == null)
                return;

            string side = (string)sideDropDown.SelectedItem.Tag;
            string currentState = ProgramConstants.IsInGame ? "In Game" : "Setting Up";

            discordHandler.UpdatePresence(
                Map.UntranslatedName, GameMode.UntranslatedUIName, currentState, side, resetTimer);
        }

        protected override bool AllowPlayerOptionsChange()
        {
            return true;
        }

        protected override void GameProcessExited()
        {
            base.GameProcessExited();

            DdGameModeMapFilter_SelectedIndexChanged(null, EventArgs.Empty); // Refresh ranks

            RandomSeed = random.Next();
        }

        public void Open()
        {
            topBar.AddPrimarySwitchable(this);
            Enable();
        }

        public void SwitchOn()
        {
            Enable();
        }

        public void SwitchOff()
        {
            Disable();
        }

        public string GetSwitchName()
        {
            return "Skirmish Lobby".L10N("Client:Main:SkirmishLobby");
        }

        /// <summary>
        /// Saves skirmish settings to an INI file on the file system.
        /// </summary>
        private void SaveSettings()
        {
            try
            {
                FileInfo settingsFileInfo = SafePath.GetFile(ProgramConstants.GamePath, SETTINGS_PATH);

                // Delete the file so we don't keep potential extra AI players that already exist in the file
                settingsFileInfo.Delete();

                var skirmishSettingsIni = new IniFile(settingsFileInfo.FullName);

                skirmishSettingsIni.SetStringValue("Player", "Info", Players[0].ToString());

                for (int i = 0; i < AIPlayers.Count; i++)
                {
                    skirmishSettingsIni.SetStringValue("AIPlayers", i.ToString(), AIPlayers[i].ToString());
                }

                skirmishSettingsIni.SetStringValue("Settings", "Map", Map?.SHA1 ?? string.Empty);
                skirmishSettingsIni.SetStringValue("Settings", "GameModeMapFilter", ddGameModeMapFilter.SelectedItem?.Text);

                if (ClientConfiguration.Instance.SaveSkirmishGameOptions)
                {
                    GameOptions.WriteSettings(skirmishSettingsIni, "GameOptions");
                }

                skirmishSettingsIni.WriteIniFile();
            }
            catch (Exception ex)
            {
                Logger.Log("Saving skirmish settings failed! Reason: " + ex.ToString());

#if DEBUG
                Debugger.Break();
#endif
            }
        }

        /// <summary>
        /// Loads skirmish settings from an INI file on the file system.
        /// </summary>
        private void LoadSettings()
        {
            if (!SafePath.GetFile(ProgramConstants.GamePath, SETTINGS_PATH).Exists)
            {
                InitDefaultSettings();
                return;
            }

            var skirmishSettingsIni = new IniFile(SafePath.CombineFilePath(ProgramConstants.GamePath, SETTINGS_PATH));

            string gameModeMapFilterName = skirmishSettingsIni.GetStringValue("Settings", "GameModeMapFilter", string.Empty);
            if (string.IsNullOrEmpty(gameModeMapFilterName))
                gameModeMapFilterName = skirmishSettingsIni.GetStringValue("Settings", "GameMode", string.Empty); // legacy

            var gameModeMapFilter = ddGameModeMapFilter.Items.Find(i => i.Text == gameModeMapFilterName)?.Tag as GameModeMapFilter;
            if (gameModeMapFilter == null || !gameModeMapFilter.Any())
                gameModeMapFilter = GetDefaultGameModeMapFilter();

            var gameModeMap = gameModeMapFilter.GetGameModeMaps().FirstOrDefault();

            if (gameModeMap != null)
            {
                GameModeMap = gameModeMap;

                ddGameModeMapFilter.SelectedIndex = ddGameModeMapFilter.Items.FindIndex(i => i.Tag == gameModeMapFilter);

                string mapSHA1 = skirmishSettingsIni.GetStringValue("Settings", "Map", string.Empty);

                int gameModeMapIndex = GetSortedGameModeMaps().FindIndex(gmm => gmm.Map.SHA1 == mapSHA1);

                if (gameModeMapIndex > -1)
                {
                    lbGameModeMapList.SelectedIndex = gameModeMapIndex;

                    while (gameModeMapIndex > lbGameModeMapList.LastIndex)
                        lbGameModeMapList.TopIndex++;
                }
            }
            else
                LoadDefaultGameModeMap();

            var player = PlayerInfo.FromString(skirmishSettingsIni.GetStringValue("Player", "Info", string.Empty));

            if (player == null)
            {
                Logger.Log("Failed to load human player information from skirmish settings!");
                InitDefaultSettings();
                return;
            }

            CheckLoadedPlayerVariableBounds(player);

            player.Name = ProgramConstants.PLAYERNAME;
            Players.Add(player);

            List<string> keys = skirmishSettingsIni.GetSectionKeys("AIPlayers");

            if (keys == null)
            {
                keys = new List<string>(); // No point skip parsing all settings if only AI info is missing.
                //Logger.Log("AI player information doesn't exist in skirmish settings!");
                //InitDefaultSettings();
                //return;
            }

            bool AIAllowed = GameModeMap != null && !GameModeMap.HumanPlayersOnly;
            foreach (string key in keys)
            {
                if (!AIAllowed) break;
                var aiPlayer = PlayerInfo.FromString(skirmishSettingsIni.GetStringValue("AIPlayers", key, string.Empty));

                CheckLoadedPlayerVariableBounds(aiPlayer, true);

                if (aiPlayer == null)
                {
                    Logger.Log("Failed to load AI player information from skirmish settings!");
                    InitDefaultSettings();
                    return;
                }

                if (AIPlayers.Count < MAX_PLAYER_COUNT - 1)
                    AIPlayers.Add(aiPlayer);
            }

            if (ClientConfiguration.Instance.SaveSkirmishGameOptions)
                GameOptions.ReadSettings(skirmishSettingsIni, "GameOptions", GameMode, Map);
        }

        /// <summary>
        /// Checks that a player's color, team and starting location
        /// don't exceed allowed bounds.
        /// </summary>
        /// <param name="pInfo">The PlayerInfo.</param>
        private void CheckLoadedPlayerVariableBounds(PlayerInfo pInfo, bool isAIPlayer = false)
        {
            int sideCount = SideCount + RandomSelectorCount;
            if (isAIPlayer) sideCount--;

            if (pInfo.SideId < 0 || pInfo.SideId > sideCount)
            {
                pInfo.SideId = 0;
            }

            if (pInfo.ColorId < 0 || pInfo.ColorId > MPColors.Count)
            {
                pInfo.ColorId = 0;
            }

            if (pInfo.TeamId < 0 || pInfo.TeamId >= ddPlayerTeams[0].Items.Count ||
                (!(GameModeMap?.IsCoop ?? false)) && (GameModeMap?.ForceNoTeams ?? false))
            {
                pInfo.TeamId = 0;
            }

            if (pInfo.StartingLocation < 0 || pInfo.StartingLocation > MAX_PLAYER_COUNT ||
                (GameModeMap?.ForceRandomStartLocations ?? false))
            {
                pInfo.StartingLocation = 0;
            }
        }

        private void InitDefaultSettings()
        {
            Players.Clear();
            AIPlayers.Clear();

            Players.Add(new PlayerInfo(ProgramConstants.PLAYERNAME, 0, 0, 0, 0));
            PlayerInfo aiPlayer = new PlayerInfo(ProgramConstants.AI_PLAYER_NAMES[0], 0, 0, 0, 0);
            aiPlayer.IsAI = true;
            aiPlayer.AILevel = 0;
            AIPlayers.Add(aiPlayer);

            LoadDefaultGameModeMap();
        }

        protected override void UpdateMapPreviewBoxEnabledStatus()
        {
            MapPreviewBox.EnableContextMenu = GameModeMap != null && !(GameModeMap.ForceRandomStartLocations || GetPlayerExtraOptions().IsForceRandomStarts);
            MapPreviewBox.EnableStartLocationSelection = MapPreviewBox.EnableContextMenu;
        }

        protected override bool UpdateLaunchGameButtonStatus()
        {
            btnLaunchGame.Enabled = base.UpdateLaunchGameButtonStatus() && GameMode != null && Map != null;
            return btnLaunchGame.Enabled;
        }
    }
}
