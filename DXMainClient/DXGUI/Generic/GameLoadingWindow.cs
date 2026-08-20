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
using System;
using System.Collections.Generic;

namespace DTAClient.DXGUI.Generic
{
    /// <summary>
    /// A window for loading saved singleplayer games and, where the game supports it, watching
    /// recorded replays.
    ///
    /// Structured as a tab shell around <see cref="LoadGamePanel"/>s, the same way OptionsWindow
    /// wraps its option panels. Games without replay support get a single panel and no tab strip,
    /// so the window looks exactly as it always has.
    /// </summary>
    public class GameLoadingWindow : XNAWindow
    {
        private const int BUTTON_WIDTH = 110;
        private const int BUTTON_HEIGHT = 23;
        private const int BUTTON_SPACING = 10;

        public GameLoadingWindow(WindowManager windowManager, DiscordHandler discordHandler,
            CampaignTagSelector campaignTagSelector) : base(windowManager)
        {
            this.discordHandler = discordHandler;
            this.campaignTagSelector = campaignTagSelector;
        }

        private readonly DiscordHandler discordHandler;
        private readonly CampaignTagSelector campaignTagSelector;

        private XNAClientTabControl tabControl;
        private LoadGamePanel[] panels;
        private SavedGamesPanel savedGamesPanel;

        private XNAClientButton btnLaunch;
        private XNAClientButton btnDelete;
        private XNAClientButton btnCancel;

        /// <summary>
        /// The buttons each panel contributes to the row, kept per panel so switching tabs just
        /// swaps which set is visible.
        /// </summary>
        private Dictionary<LoadGamePanel, XNAClientButton[]> extraButtons;

        private int buttonRowY;

        private LoadGamePanel ActivePanel => panels[tabControl == null ? 0 : tabControl.SelectedTab];

        public override void Initialize()
        {
            Name = "GameLoadingWindow";
            BackgroundTexture = AssetLoader.LoadTexture("loadmissionbg.png");

            savedGamesPanel = new SavedGamesPanel(WindowManager, discordHandler, campaignTagSelector);

            var panelList = new List<LoadGamePanel> { savedGamesPanel };

            if (ReplayManager.IsSupported)
                panelList.Add(new ReplaysPanel(WindowManager, discordHandler));

            panels = panelList.ToArray();

            bool showTabs = panels.Length > 1;

            // The replay panel needs the room; a lone saved-games list does not, and growing the
            // window for every game would change how it looks for the ones without replays.
            ClientRectangle = showTabs
                ? new Rectangle(0, 0, 700, 540)
                : new Rectangle(0, 0, 600, 380);

            int panelTop = 12;

            if (showTabs)
            {
                tabControl = new XNAClientTabControl(WindowManager);
                tabControl.Name = nameof(tabControl);
                tabControl.ClientRectangle = new Rectangle(12, 12, 0, 23);
                tabControl.FontIndex = 1;
                tabControl.ClickSound = new EnhancedSoundEffect("button.wav");

                foreach (LoadGamePanel panel in panels)
                    tabControl.AddTab(panel.TabTitle, UIDesignConstants.BUTTON_WIDTH_133);

                tabControl.SelectedIndexChanged += TabControl_SelectedIndexChanged;

                panelTop = tabControl.Bottom + 12;
            }

            int buttonY = Height - BUTTON_HEIGHT - 12;
            int panelHeight = buttonY - panelTop - 12;

            foreach (LoadGamePanel panel in panels)
            {
                panel.ClientRectangle = new Rectangle(12, panelTop, Width - 24, panelHeight);

                // AddChild initializes the panel straight away, so its rectangle has to be set
                // first and its events hooked up afterwards - a panel populates its list during
                // Initialize, which would otherwise reach the buttons before they exist.
                AddChild(panel);
                panel.Disable();
            }

            panels[0].Enable();

            buttonRowY = buttonY;

            btnLaunch = new XNAClientButton(WindowManager);
            btnLaunch.Name = nameof(btnLaunch);
            btnLaunch.ClientRectangle = new Rectangle(0, buttonY, BUTTON_WIDTH, BUTTON_HEIGHT);
            btnLaunch.Text = "Load".L10N("Client:Main:ButtonLoad");
            btnLaunch.AllowClick = false;
            btnLaunch.LeftClick += (_, _) => ActivePanel.Launch();

            btnDelete = new XNAClientButton(WindowManager);
            btnDelete.Name = nameof(btnDelete);
            btnDelete.ClientRectangle = new Rectangle(0, buttonY, BUTTON_WIDTH, BUTTON_HEIGHT);
            btnDelete.Text = "Delete".L10N("Client:Main:ButtonDelete");
            btnDelete.AllowClick = false;
            btnDelete.LeftClick += (_, _) => ActivePanel.Delete();

            btnCancel = new XNAClientButton(WindowManager);
            btnCancel.Name = nameof(btnCancel);
            btnCancel.ClientRectangle = new Rectangle(0, buttonY, BUTTON_WIDTH, BUTTON_HEIGHT);
            btnCancel.Text = "Cancel".L10N("Client:Main:ButtonCancel");
            btnCancel.LeftClick += (_, _) => Disable();

            CreateExtraButtons(buttonY);

            foreach (LoadGamePanel panel in panels)
            {
                panel.SelectionChanged += (_, _) => UpdateButtonStates();
                panel.LaunchRequested += (_, _) => Disable();
            }

            if (tabControl != null)
                AddChild(tabControl);

            AddChild(btnLaunch);
            AddChild(btnDelete);
            AddChild(btnCancel);

            base.Initialize();

            CenterOnParent();

            LayOutButtonRow();
            UpdateButtonStates();
        }

        /// <summary>
        /// Builds the buttons each panel asked for. They belong to the window so the whole dialog
        /// keeps a single row of buttons instead of scattering actions across the panel.
        /// </summary>
        private void CreateExtraButtons(int buttonY)
        {
            extraButtons = new Dictionary<LoadGamePanel, XNAClientButton[]>();

            foreach (LoadGamePanel panel in panels)
            {
                var buttons = new List<XNAClientButton>();

                foreach (LoadGamePanelAction action in panel.ExtraActions)
                {
                    LoadGamePanelAction capturedAction = action;

                    var button = new XNAClientButton(WindowManager);
                    button.Name = "btn" + capturedAction.Text.Replace(" ", string.Empty);
                    button.ClientRectangle = new Rectangle(0, buttonY, BUTTON_WIDTH, BUTTON_HEIGHT);
                    button.Text = capturedAction.Text;
                    button.LeftClick += (_, _) => capturedAction.Action();

                    AddChild(button);
                    button.Disable();

                    buttons.Add(button);
                }

                extraButtons[panel] = buttons.ToArray();
            }
        }

        /// <summary>
        /// Shows the active panel's buttons and centres the whole row, which changes width as
        /// panels contribute a different number of buttons.
        /// </summary>
        private void LayOutButtonRow()
        {
            LoadGamePanel activePanel = ActivePanel;

            foreach (KeyValuePair<LoadGamePanel, XNAClientButton[]> entry in extraButtons)
            {
                bool visible = entry.Key == activePanel;

                foreach (XNAClientButton button in entry.Value)
                {
                    if (visible)
                        button.Enable();
                    else
                        button.Disable();
                }
            }

            XNAClientButton[] activeExtras = extraButtons[activePanel];

            // Panel actions sit between Delete and Cancel, so Cancel stays the rightmost button
            // and Delete is never adjacent to a Delete All.
            var row = new List<XNAClientButton> { btnLaunch, btnDelete };
            row.AddRange(activeExtras);
            row.Add(btnCancel);

            int rowWidth = (BUTTON_WIDTH * row.Count) + (BUTTON_SPACING * (row.Count - 1));
            int x = (Width - rowWidth) / 2;

            foreach (XNAClientButton button in row)
            {
                button.ClientRectangle = new Rectangle(x, buttonRowY, BUTTON_WIDTH, BUTTON_HEIGHT);
                x += BUTTON_WIDTH + BUTTON_SPACING;
            }
        }

        public void Open() => Enable();

        /// <summary>
        /// Kept for the main menu, which refreshes the window when returning from a game.
        /// </summary>
        public void ListSaves() => savedGamesPanel.ListSaves();

        protected override void OnEnabledChanged(object sender, EventArgs args)
        {
            base.OnEnabledChanged(sender, args);

            // Files can appear or vanish while the window is closed - a game just finished
            // recording, or the user deleted something in Explorer.
            if (Enabled && panels != null)
                RefreshActivePanel();
        }

        private void TabControl_SelectedIndexChanged(object sender, EventArgs e)
        {
            foreach (LoadGamePanel panel in panels)
                panel.Disable();

            ActivePanel.Enable();

            LayOutButtonRow();
            RefreshActivePanel();
        }

        private void RefreshActivePanel()
        {
            ActivePanel.Refresh();
            UpdateButtonStates();
        }

        private void UpdateButtonStates()
        {
            // Reachable before the buttons are built, e.g. if the window is enabled while the
            // theme INI is still being applied.
            if (btnLaunch == null)
                return;

            LoadGamePanel panel = ActivePanel;

            btnLaunch.Text = panel.LaunchButtonText;
            btnLaunch.AllowClick = panel.CanLaunch;
            btnDelete.AllowClick = panel.CanDelete;

            XNAClientButton[] buttons = extraButtons[panel];
            IReadOnlyList<LoadGamePanelAction> actions = panel.ExtraActions;

            for (int i = 0; i < buttons.Length && i < actions.Count; i++)
                buttons[i].AllowClick = actions[i].IsEnabled == null || actions[i].IsEnabled();
        }
    }
}
