#nullable enable

using System;
using System.Collections.Generic;
using System.Linq;

using ClientCore.Extensions;

using ClientGUI;

using DTAClient.Domain;
using DTAClient.DXGUI.Campaign;
using DTAClient.DXGUI.Generic.LoadGamePanels;

using Microsoft.Xna.Framework;

using Rampastring.XNAUI;

namespace DTAClient.DXGUI.Generic
{
    /// <summary>
    /// Loads saved games and, when enabled, replays.
    /// </summary>
    public class GameLoadingWindow : XNAWindow
    {
        private const int BUTTON_WIDTH = 110;
        private const int BUTTON_HEIGHT = 23;
        private const int BUTTON_SPACING = 10;
        private const int MARGIN = 12;

        public GameLoadingWindow(WindowManager windowManager, DiscordHandler discordHandler,
            CampaignTagSelector campaignTagSelector) : base(windowManager)
        {
            this.discordHandler = discordHandler;
            this.campaignTagSelector = campaignTagSelector;
        }

        private readonly DiscordHandler discordHandler;
        private readonly CampaignTagSelector campaignTagSelector;

        private XNAClientTabControl? tabControl;

        // Empty until Initialize builds them - OnEnabledChanged can fire before it runs.
        private LoadGamePanel[] panels = Array.Empty<LoadGamePanel>();
        private SavedGamesPanel savedGamesPanel = null!;

        private XNAClientButton btnLaunch = null!;
        private XNAClientButton btnDelete = null!;
        private XNAClientButton btnCancel = null!;

        /// <summary>
        /// Extra action buttons keyed by the panel that owns them.
        /// </summary>
        private readonly Dictionary<LoadGamePanel, XNAClientButton[]> extraButtons = new();

        // Cleared in Initialize for whatever the theme INI laid out itself, so that laying the
        // same controls out from code does not silently undo the package's own positioning.
        private bool layOutPanelsFromCode = true;
        private bool layOutButtonRowFromCode = true;
        private bool setLaunchButtonTextFromCode = true;

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

            ClientRectangle = showTabs
                ? new Rectangle(0, 0, 700, 540)
                : new Rectangle(0, 0, 600, 380);

            if (showTabs)
            {
                tabControl = new XNAClientTabControl(WindowManager);
                tabControl.Name = nameof(tabControl);
                tabControl.ClientRectangle = new Rectangle(MARGIN, MARGIN, 0, BUTTON_HEIGHT);
                tabControl.FontIndex = 1;
                tabControl.ClickSound = new EnhancedSoundEffect("button.wav");

                foreach (LoadGamePanel panel in panels)
                    tabControl.AddTab(panel.TabTitle, UIDesignConstants.BUTTON_WIDTH_133);

                tabControl.SelectedIndexChanged += TabControl_SelectedIndexChanged;

                AddChild(tabControl);
            }

            foreach (LoadGamePanel panel in panels)
            {
                // Sized before AddChild so the panel's own controls initialize at a sane size.
                panel.ClientRectangle = GetPanelRectangle();

                AddChild(panel);
                panel.Disable();
            }

            panels[0].Enable();

            btnLaunch = new XNAClientButton(WindowManager);
            btnLaunch.Name = nameof(btnLaunch);
            btnLaunch.ClientRectangle = new Rectangle(0, 0, BUTTON_WIDTH, BUTTON_HEIGHT);
            btnLaunch.Text = ActivePanel.LaunchButtonText;
            btnLaunch.AllowClick = false;
            btnLaunch.LeftClick += (_, _) => ActivePanel.Launch();

            btnDelete = new XNAClientButton(WindowManager);
            btnDelete.Name = nameof(btnDelete);
            btnDelete.ClientRectangle = new Rectangle(0, 0, BUTTON_WIDTH, BUTTON_HEIGHT);
            btnDelete.Text = "Delete".L10N("Client:Main:ButtonDelete");
            btnDelete.AllowClick = false;
            btnDelete.LeftClick += (_, _) => ActivePanel.Delete();

            btnCancel = new XNAClientButton(WindowManager);
            btnCancel.Name = nameof(btnCancel);
            btnCancel.ClientRectangle = new Rectangle(0, 0, BUTTON_WIDTH, BUTTON_HEIGHT);
            btnCancel.Text = "Cancel".L10N("Client:Main:ButtonCancel");
            btnCancel.LeftClick += (_, _) => Disable();

            AddChild(btnLaunch);
            AddChild(btnDelete);
            AddChild(btnCancel);

            CreateExtraButtons();

            foreach (LoadGamePanel panel in panels)
            {
                panel.SelectionChanged += (_, _) => UpdateButtonStates();
                panel.LaunchRequested += (_, _) => Disable();
            }

            // Lay out for the size set above before the theme INI is read, so that the INI - which
            // base.Initialize() applies on top of this
            UpdateExtraButtonVisibility();
            LayOutPanels();
            LayOutButtonRow();

            Rectangle[] panelRectangles = GetPanelRectangles();
            Rectangle[] buttonRectangles = GetButtonRectangles();
            string launchButtonText = btnLaunch.Text;

            base.Initialize();

            CenterOnParent();

            // Hand over to the theme INI whatever it laid out itself.
            layOutPanelsFromCode = GetPanelRectangles().SequenceEqual(panelRectangles);
            layOutButtonRowFromCode = GetButtonRectangles().SequenceEqual(buttonRectangles);
            setLaunchButtonTextFromCode = btnLaunch.Text == launchButtonText;

            LayOutPanels();
            LayOutButtonRow();

            RefreshActivePanel();
        }

        /// <summary>
        /// Builds the panel-specific action buttons.
        /// </summary>
        private void CreateExtraButtons()
        {
            extraButtons.Clear();

            foreach (LoadGamePanel panel in panels)
            {
                var buttons = new List<XNAClientButton>();

                foreach (LoadGamePanelAction action in panel.ExtraActions)
                {
                    var button = new XNAClientButton(WindowManager);
                    button.Name = action.Name;
                    button.ClientRectangle = new Rectangle(0, 0, BUTTON_WIDTH, BUTTON_HEIGHT);
                    button.Text = action.Text;
                    button.LeftClick += (_, _) => action.Action();

                    AddChild(button);
                    button.Disable();

                    buttons.Add(button);
                }

                extraButtons[panel] = buttons.ToArray();
            }
        }

        /// <summary>
        /// The area the panels fill, between the tab control and the button row.
        /// </summary>
        private Rectangle GetPanelRectangle()
        {
            int panelTop = tabControl == null ? MARGIN : tabControl.Bottom + MARGIN;
            int panelHeight = Height - BUTTON_HEIGHT - (MARGIN * 2) - panelTop;

            return new Rectangle(MARGIN, panelTop, Width - (MARGIN * 2), panelHeight);
        }

        /// <summary>
        /// Every button the automatic layout owns, in a stable order.
        /// </summary>
        private IEnumerable<XNAClientButton> EnumerateButtons()
        {
            yield return btnLaunch;
            yield return btnDelete;
            yield return btnCancel;

            foreach (LoadGamePanel panel in panels)
            {
                foreach (XNAClientButton button in extraButtons[panel])
                    yield return button;
            }
        }

        private Rectangle[] GetPanelRectangles()
            => panels.Select(panel => panel.ClientRectangle).ToArray();

        private Rectangle[] GetButtonRectangles()
            => EnumerateButtons().Select(button => button.ClientRectangle).ToArray();

        /// <summary>
        /// Sizes the panels to fit the window.
        /// </summary>
        private void LayOutPanels()
        {
            if (!layOutPanelsFromCode)
                return;

            Rectangle panelRectangle = GetPanelRectangle();

            foreach (LoadGamePanel panel in panels)
            {
                if (panel.ClientRectangle == panelRectangle)
                    continue;

                panel.ClientRectangle = panelRectangle;
                panel.LayOutControls();
            }
        }

        /// <summary>
        /// Shows the active panel's extra buttons and hides the other panels'.
        /// </summary>
        private void UpdateExtraButtonVisibility()
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
        }

        /// <summary>
        /// Centers the active panel's button row, which is as wide as that panel has buttons.
        /// </summary>
        private void LayOutButtonRow()
        {
            if (!layOutButtonRowFromCode)
                return;

            var row = new List<XNAClientButton> { btnLaunch, btnDelete };
            row.AddRange(extraButtons[ActivePanel]);
            row.Add(btnCancel);

            int rowWidth = (BUTTON_WIDTH * row.Count) + (BUTTON_SPACING * (row.Count - 1));
            int x = (Width - rowWidth) / 2;
            int y = Height - BUTTON_HEIGHT - MARGIN;

            foreach (XNAClientButton button in row)
            {
                button.ClientRectangle = new Rectangle(x, y, BUTTON_WIDTH, BUTTON_HEIGHT);
                x += BUTTON_WIDTH + BUTTON_SPACING;
            }
        }

        public void Open() => Enable();

        /// <summary>
        /// Refresh hook used by the main menu after returning from a game.
        /// </summary>
        public void ListSaves() => savedGamesPanel.ListSaves();

        protected override void OnEnabledChanged(object sender, EventArgs args)
        {
            base.OnEnabledChanged(sender, args);

            // Files can change while the window is closed. Nothing to refresh before Initialize.
            if (Enabled && panels.Length > 0)
                RefreshActivePanel();
        }

        private void TabControl_SelectedIndexChanged(object sender, EventArgs e)
        {
            foreach (LoadGamePanel panel in panels)
                panel.Disable();

            ActivePanel.Enable();

            UpdateExtraButtonVisibility();
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
            LoadGamePanel panel = ActivePanel;

            if (setLaunchButtonTextFromCode)
                btnLaunch.Text = panel.LaunchButtonText;

            btnLaunch.AllowClick = panel.CanLaunch;
            btnDelete.AllowClick = panel.CanDelete;

            XNAClientButton[] buttons = extraButtons[panel];
            IReadOnlyList<LoadGamePanelAction> actions = panel.ExtraActions;

            for (int i = 0; i < buttons.Length; i++)
            {
                Func<bool>? isEnabled = actions[i].IsEnabled;
                buttons[i].AllowClick = isEnabled == null || isEnabled();
            }
        }
    }
}
