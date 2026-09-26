using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using ClientGUI;
using ClientLogic.Lobby;
using DTAClient.Domain.Multiplayer;
using ClientCore.Extensions;
using Microsoft.Xna.Framework;
using Rampastring.XNAUI;
using Rampastring.XNAUI.XNAControls;

namespace DTAClient.DXGUI.Multiplayer
{
    /// <summary>
    /// Shows and edits a lobby's <see cref="PlayerExtraOptionsState"/>.
    /// </summary>
    public class PlayerExtraOptionsPanel : XNAPanel
    {
        private const int maxStartCount = 8;
        private const int defaultX = 24;
        private const int defaultTeamStartMappingX = UIDesignConstants.EMPTY_SPACE_SIDES;
        private const int teamMappingPanelWidth = 50;
        private const int teamMappingPanelHeight = 22;
        private readonly string customPresetName = "Custom".L10N("Client:Main:CustomPresetName");

        private XNAClientCheckBox chkBoxForceRandomSides;
        private XNAClientCheckBox chkBoxForceNoTeams;
        private XNAClientCheckBox chkBoxForceRandomColors;
        private XNAClientCheckBox chkBoxForceRandomStarts;
        private XNAClientCheckBox chkBoxUseTeamStartMappings;
        private XNAClientDropDown ddTeamStartMappingPreset;
        private TeamStartMappingsPanel teamStartMappingsPanel;
        private bool _isHost;
        private bool ignoreMappingChanges;

        public EventHandler OnClose;

        private GameModeMap _gameModeMap;
        private PlayerExtraOptionsState state;
        private bool updatingMappingsFromControls;

        public PlayerExtraOptionsPanel(WindowManager windowManager) : base(windowManager)
        {
        }

        /// <summary>Shows the given options; call after <see cref="Initialize"/>.</summary>
        public void Bind(PlayerExtraOptionsState extraOptions)
        {
            state = extraOptions;
            state.PropertyChanged += State_PropertyChanged;

            chkBoxForceRandomSides.Checked = state.ForceRandomSides;
            chkBoxForceRandomColors.Checked = state.ForceRandomColors;
            chkBoxForceNoTeams.Checked = state.ForceNoTeams;
            chkBoxForceRandomStarts.Checked = state.ForceRandomStarts;
            chkBoxUseTeamStartMappings.Checked = state.UseTeamStartMappings;
            RefreshAllowChecking();
        }

        private void State_PropertyChanged(object sender, PropertyChangedEventArgs e)
        {
            switch (e.PropertyName)
            {
                case nameof(PlayerExtraOptionsState.ForceRandomSides):
                    chkBoxForceRandomSides.Checked = state.ForceRandomSides;
                    break;
                case nameof(PlayerExtraOptionsState.ForceRandomColors):
                    chkBoxForceRandomColors.Checked = state.ForceRandomColors;
                    break;
                case nameof(PlayerExtraOptionsState.ForceNoTeams):
                    chkBoxForceNoTeams.Checked = state.ForceNoTeams;
                    break;
                case nameof(PlayerExtraOptionsState.ForceRandomStarts):
                    chkBoxForceRandomStarts.Checked = state.ForceRandomStarts;
                    break;
                case nameof(PlayerExtraOptionsState.UseTeamStartMappings):
                    chkBoxUseTeamStartMappings.Checked = state.UseTeamStartMappings;
                    RefreshTeamStartMappingsPanel();
                    RefreshPresetDropdown();
                    break;
                case nameof(PlayerExtraOptionsState.TeamStartMappings):
                    if (!updatingMappingsFromControls)
                        teamStartMappingsPanel.SetTeamStartMappings([.. state.TeamStartMappings]);
                    break;
                case nameof(PlayerExtraOptionsState.CanChangeForceNoTeams):
                case nameof(PlayerExtraOptionsState.CanChangeUseTeamStartMappings):
                    RefreshAllowChecking();
                    break;
            }
        }

        private void RefreshAllowChecking()
        {
            chkBoxForceNoTeams.AllowChecking = state.CanChangeForceNoTeams;
            chkBoxUseTeamStartMappings.AllowChecking = state.CanChangeUseTeamStartMappings;
        }

        private void ChkBox_CheckedChanged(object sender, EventArgs e)
        {
            if (state == null)
                return;

            if (sender == chkBoxForceRandomSides)
                state.ForceRandomSides = chkBoxForceRandomSides.Checked;
            else if (sender == chkBoxForceRandomColors)
                state.ForceRandomColors = chkBoxForceRandomColors.Checked;
            else if (sender == chkBoxForceNoTeams)
                state.ForceNoTeams = chkBoxForceNoTeams.Checked;
            else if (sender == chkBoxForceRandomStarts)
                state.ForceRandomStarts = chkBoxForceRandomStarts.Checked;
            else if (sender == chkBoxUseTeamStartMappings)
                state.UseTeamStartMappings = chkBoxUseTeamStartMappings.Checked;
        }

        private void Mapping_Changed(object sender, EventArgs e)
        {
            if (state != null)
            {
                updatingMappingsFromControls = true;
                state.SetTeamStartMappings(teamStartMappingsPanel.GetTeamStartMappings());
                updatingMappingsFromControls = false;
            }

            if (ignoreMappingChanges)
                return;

            ddTeamStartMappingPreset.SelectedIndex = 0;
        }

        private void RefreshTeamStartMappingsPanel()
        {
            teamStartMappingsPanel.EnableControls(_isHost && UseTeamStartMappings);

            RefreshTeamStartMappingPanels();
        }

        private void AddLocationAssignments()
        {
            for (int i = 0; i < maxStartCount; i++)
            {
                var teamStartMappingPanel = new TeamStartMappingPanel(WindowManager, i + 1);
                teamStartMappingPanel.ClientRectangle = GetTeamMappingPanelRectangle(i);

                teamStartMappingsPanel.AddMappingPanel(teamStartMappingPanel);
            }

            teamStartMappingsPanel.MappingChanged += Mapping_Changed;
        }

        private Rectangle GetTeamMappingPanelRectangle(int index)
        {
            const int maxColumnCount = 2;
            const int mappingPanelDefaultX = 4;
            const int mappingPanelDefaultY = 0;
            if (index > 0 && index % maxColumnCount == 0) // need to start a new column
                return new Rectangle(((index / maxColumnCount) * (teamMappingPanelWidth + mappingPanelDefaultX)) + 3, mappingPanelDefaultY, teamMappingPanelWidth, teamMappingPanelHeight);

            var lastControl = index > 0 ? teamStartMappingsPanel.GetTeamStartMappingPanels()[index - 1] : null;
            return new Rectangle(lastControl?.X ?? mappingPanelDefaultX, lastControl?.Bottom + 4 ?? mappingPanelDefaultY, teamMappingPanelWidth, teamMappingPanelHeight);
        }

        private void ClearTeamStartMappingSelections()
            => teamStartMappingsPanel.GetTeamStartMappingPanels().ForEach(panel => panel.ClearSelections());

        private void RefreshTeamStartMappingPanels()
        {
            ClearTeamStartMappingSelections();
            var teamStartMappingPanels = teamStartMappingsPanel.GetTeamStartMappingPanels();
            for (int i = 0; i < teamStartMappingPanels.Count; i++)
            {
                var teamStartMappingPanel = teamStartMappingPanels[i];
                teamStartMappingPanel.ClearSelections();
                if (!UseTeamStartMappings)
                    continue;

                teamStartMappingPanel.EnableControls(_isHost && UseTeamStartMappings && _gameModeMap != null && _gameModeMap.AllowedStartingLocations.Contains(i + 1));
                RefreshTeamStartMappingPresets(_gameModeMap?.Map?.TeamStartMappingPresets);
            }
        }

        private void RefreshTeamStartMappingPresets(List<TeamStartMappingPreset> teamStartMappingPresets)
        {
            ddTeamStartMappingPreset.Items.Clear();
            ddTeamStartMappingPreset.AddItem(new XNADropDownItem
            {
                Text = customPresetName,
                Tag = new List<TeamStartMapping>()
            });
            ddTeamStartMappingPreset.SelectedIndex = 0;

            if (!(teamStartMappingPresets?.Any() ?? false)) return;

            teamStartMappingPresets.ForEach(preset => ddTeamStartMappingPreset.AddItem(new XNADropDownItem
            {
                Text = preset.Name,
                Tag = preset.TeamStartMappings
            }));
            ddTeamStartMappingPreset.SelectedIndex = 1;
        }

        private void DdTeamMappingPreset_SelectedIndexChanged(object sender, EventArgs e)
        {
            var selectedItem = ddTeamStartMappingPreset.SelectedItem;
            if (selectedItem?.Text == customPresetName)
                return;

            var teamStartMappings = selectedItem?.Tag as List<TeamStartMapping>;

            // One change for the whole preset
            state?.BeginUpdate();
            ignoreMappingChanges = true;
            teamStartMappingsPanel.SetTeamStartMappings(teamStartMappings);
            ignoreMappingChanges = false;
            state?.EndUpdate();
        }

        private bool UseTeamStartMappings => state?.UseTeamStartMappings ?? false;

        private void RefreshPresetDropdown() => ddTeamStartMappingPreset.AllowDropDown = _isHost && UseTeamStartMappings;

        public override void Initialize()
        {
            Name = nameof(PlayerExtraOptionsPanel);
            BackgroundTexture = AssetLoader.CreateTexture(new Color(0, 0, 0, 255), 1, 1);
            Visible = false;

            var btnClose = new XNAClientButton(WindowManager);
            btnClose.Name = "btnClose";
            btnClose.ClientRectangle = new Rectangle(0, 0, 0, 0);
            btnClose.IdleTexture = AssetLoader.LoadTexture("optionsButtonClose.png");
            btnClose.HoverTexture = AssetLoader.LoadTexture("optionsButtonClose_c.png");
            btnClose.LeftClick += (sender, args) => Disable();
            AddChild(btnClose);

            var lblHeader = new XNALabel(WindowManager);
            lblHeader.Name = "lblHeader";
            lblHeader.Text = "Extra Player Options".L10N("Client:Main:ExtraPlayerOptions");
            lblHeader.ClientRectangle = new Rectangle(defaultX, 4, 0, 18);
            AddChild(lblHeader);

            chkBoxForceRandomSides = new XNAClientCheckBox(WindowManager);
            chkBoxForceRandomSides.Name = "chkBoxForceRandomSides";
            chkBoxForceRandomSides.Text = "Force Random Sides".L10N("Client:Main:ForceRandomSides");
            chkBoxForceRandomSides.ClientRectangle = new Rectangle(defaultX, lblHeader.Bottom + 4, 0, 0);
            chkBoxForceRandomSides.CheckedChanged += ChkBox_CheckedChanged;
            AddChild(chkBoxForceRandomSides);

            chkBoxForceRandomColors = new XNAClientCheckBox(WindowManager);
            chkBoxForceRandomColors.Name = "chkBoxForceRandomColors";
            chkBoxForceRandomColors.Text = "Force Random Colors".L10N("Client:Main:ForceRandomColors");
            chkBoxForceRandomColors.ClientRectangle = new Rectangle(defaultX, chkBoxForceRandomSides.Bottom + 4, 0, 0);
            chkBoxForceRandomColors.CheckedChanged += ChkBox_CheckedChanged;
            AddChild(chkBoxForceRandomColors);

            chkBoxForceNoTeams = new XNAClientCheckBox(WindowManager);
            chkBoxForceNoTeams.Name = "chkBoxForceNoTeams";
            chkBoxForceNoTeams.Text = "Force No Teams".L10N("Client:Main:ForceNoTeams");
            chkBoxForceNoTeams.ClientRectangle = new Rectangle(defaultX, chkBoxForceRandomColors.Bottom + 4, 0, 0);
            chkBoxForceNoTeams.CheckedChanged += ChkBox_CheckedChanged;
            AddChild(chkBoxForceNoTeams);

            chkBoxForceRandomStarts = new XNAClientCheckBox(WindowManager);
            chkBoxForceRandomStarts.Name = "chkBoxForceRandomStarts";
            chkBoxForceRandomStarts.Text = "Force Random Starts".L10N("Client:Main:ForceRandomStarts");
            chkBoxForceRandomStarts.ClientRectangle = new Rectangle(defaultX, chkBoxForceNoTeams.Bottom + 4, 0, 0);
            chkBoxForceRandomStarts.CheckedChanged += ChkBox_CheckedChanged;
            AddChild(chkBoxForceRandomStarts);

            /////////////////////////////

            chkBoxUseTeamStartMappings = new XNAClientCheckBox(WindowManager);
            chkBoxUseTeamStartMappings.Name = "chkBoxUseTeamStartMappings";
            chkBoxUseTeamStartMappings.Text = "Enable Auto Allying:".L10N("Client:Main:EnableAutoAllying");
            chkBoxUseTeamStartMappings.ClientRectangle = new Rectangle(chkBoxForceRandomSides.X, chkBoxForceRandomStarts.Bottom + 20, 0, 0);
            chkBoxUseTeamStartMappings.CheckedChanged += ChkBox_CheckedChanged;
            AddChild(chkBoxUseTeamStartMappings);

            var btnHelp = new XNAClientButton(WindowManager);
            btnHelp.Name = "btnHelp";
            btnHelp.IdleTexture = AssetLoader.LoadTexture("questionMark.png");
            btnHelp.HoverTexture = AssetLoader.LoadTexture("questionMark_c.png");
            btnHelp.LeftClick += BtnHelp_LeftClick;
            btnHelp.ClientRectangle = new Rectangle(chkBoxUseTeamStartMappings.Right + 4, chkBoxUseTeamStartMappings.Y - 1, 0, 0);
            AddChild(btnHelp);

            var lblPreset = new XNALabel(WindowManager);
            lblPreset.Name = "lblPreset";
            lblPreset.Text = "Presets:".L10N("Client:Main:Presets");
            lblPreset.ClientRectangle = new Rectangle(chkBoxUseTeamStartMappings.X, chkBoxUseTeamStartMappings.Bottom + 8, 0, 0);
            AddChild(lblPreset);

            ddTeamStartMappingPreset = new XNAClientDropDown(WindowManager);
            ddTeamStartMappingPreset.Name = "ddTeamStartMappingPreset";
            ddTeamStartMappingPreset.ClientRectangle = new Rectangle(lblPreset.X + 50, lblPreset.Y - 2, 160, 0);
            ddTeamStartMappingPreset.SelectedIndexChanged += DdTeamMappingPreset_SelectedIndexChanged;
            ddTeamStartMappingPreset.AllowDropDown = true;
            AddChild(ddTeamStartMappingPreset);

            teamStartMappingsPanel = new TeamStartMappingsPanel(WindowManager);
            teamStartMappingsPanel.Name = "teamStartMappingsPanel";
            teamStartMappingsPanel.ClientRectangle = new Rectangle(lblPreset.X, ddTeamStartMappingPreset.Bottom + 8, Width, Height - ddTeamStartMappingPreset.Bottom + 4);
            AddChild(teamStartMappingsPanel);

            AddLocationAssignments();

            base.Initialize();

            RefreshTeamStartMappingsPanel();
        }

        private void BtnHelp_LeftClick(object sender, EventArgs args)
        {
            XNAMessageBox.Show(WindowManager, "Auto Allying".L10N("Client:Main:AutoAllyingTitle"),
                ("Auto allying allows the host to assign starting locations to teams, not players.\n" +
                "When players are assigned to spawn locations, they will be auto assigned to teams based on these mappings.\n" +
                "This is best used with random teams and random starts. However, only random teams is required.\n" +
                "Manually specified starts will take precedence.").L10N("Client:Main:AutoAllyingText1") + "\n\n" +
                $"{TeamStartMapping.NO_PLAYER} : " + "Block this location from being randomly assigned to a player if there are spare locations.".L10N("Client:Main:AutoAllyingTextNoPlayerV2") + "\n" +
                $"{TeamStartMapping.NO_TEAM} : " + "Allow a player here, but don't assign a team.".L10N("Client:Main:AutoAllyingTextNoTeamV2")
            );
        }

        public void UpdateForGameModeMap(GameModeMap gameModeMap)
        {
            if (_gameModeMap == gameModeMap)
                return;

            _gameModeMap = gameModeMap;

            RefreshTeamStartMappingPanels();
        }

        public void EnableControls(bool enable)
        {
            chkBoxForceRandomSides.InputEnabled = enable;
            chkBoxForceRandomColors.InputEnabled = enable;
            chkBoxForceRandomStarts.InputEnabled = enable;
            chkBoxForceNoTeams.InputEnabled = enable;
            chkBoxUseTeamStartMappings.InputEnabled = enable;

            teamStartMappingsPanel.EnableControls(enable && UseTeamStartMappings);
        }

        public void SetIsHost(bool isHost)
        {
            _isHost = isHost;
            RefreshPresetDropdown();
            EnableControls(_isHost);
        }
    }
}
