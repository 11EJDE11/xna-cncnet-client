using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using ClientCore;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Input;
using Rampastring.Tools;
using Rampastring.XNAUI;
using Rampastring.XNAUI.XNAControls;

namespace DTAClient.DXGUI.Designer
{
    /// <summary>
    /// The designer's panel: the title bar, the tabs, what is selected, the list of keys (or controls, files,
    /// changes to review), the edit box with its suggestions and pickers, and the save and undo buttons.
    /// </summary>
    internal sealed partial class DesignerOverlay
    {
        private enum PanelMode { Inspector, Controls, AddControl, Files }

        // Theme-wide files the Files tab offers, looked up in the theme folder first
        private static readonly (string File, string Description)[] GlobalFiles =
        {
            ("DTACnCNetClient.ini", "theme colors and settings, reloads live"),
            ("GlobalThemeSettings.ini", "colors and constants themes share, reloads live"),
            ("GenericWindow.ini", "windows without an INI of their own, reloads live"),
            ("Fonts.ini", "fonts, needs a restart"),
            ("ClientDefinitions.ini", "client settings, most need a restart"),
        };

        private static readonly (string Keys, string Action)[] Shortcuts =
        {
            ("Click", "select a control; Shift+click selects a part of a list"),
            ("Ctrl+click", "add to or remove from the selection"),
            ("Drag", "move the selection; the corner handle resizes"),
            ("Alt while dragging", "don't snap to neighbors"),
            ("Arrows", "move 1 pixel, 10 with Shift"),
            ("Ctrl+arrows", "resize"),
            ("PageUp / PageDown", "select the parent / the controls inside"),
            ("Del", "delete the selection"),
            ("Ctrl+D", "duplicate the selected control"),
            ("Ctrl+F", "find a control by name"),
            ("Ctrl+Z / Ctrl+Y", "undo / redo"),
            ("Ctrl+S", "review and save"),
            ("Esc", "close the edit box, go back, deselect"),
            ("F5", "reload images and INI files"),
            ("F6", "open or close the designer"),
            ("F7", "dock the panel to the other side"),
            ("F8 or middle click", "hide or show the panel"),
            ("F9", "outline every control"),
            ("F1", "these shortcuts"),
        };

        private DesignerPanel panel;
        private DesignerListBox lbKeys;
        private DesignerListBox lbSuggestions;
        private DesignerEditTextBox tbEdit;
        private DesignerColorPicker colorPicker;
        private DesignerResourcePicker resourcePicker;

        private DesignerButton btnHelp;
        private DesignerButton btnDock;
        private DesignerButton btnCollapse;
        private DesignerButton btnClose;
        private DesignerButton btnTabInspector;
        private DesignerButton btnTabControls;
        private DesignerButton btnTabAdd;
        private DesignerButton btnTabFiles;
        private DesignerButton btnDelete;
        private DesignerButton btnDuplicate;
        private DesignerButton btnChildren;
        private DesignerButton btnOpenIni;
        private DesignerButton btnOutlines;
        private DesignerButton btnUnlink;
        private DesignerButton btnSave;
        private DesignerButton btnUndo;
        private DesignerButton btnRedo;
        private DesignerButton btnDiscard;
        private DesignerButton btnReload;
        private DesignerButton btnWrite;
        private DesignerButton btnBack;

        // Everything under the title bar, hidden while the panel is collapsed
        private readonly List<XNAControl> panelBody = new List<XNAControl>();
        private readonly List<DesignerButton> actionButtons = new List<DesignerButton>();

        private PanelMode mode = PanelMode.Inspector;
        private bool reviewing;
        private bool showingHelp;

        // Files tab: 0 lists the files, 1 a file's sections, 2 a section's keys
        private int fileLevel;
        private string filePath;
        private string fileSection;
        private CCIniFile fileIni;

        private string controlsFilter;

        // The links of the selected control to other controls, both ways
        private List<ControlLink> selectedLinks = new List<ControlLink>();

        private readonly string[] infoLines = new string[INFO_LINES];
        private string infoType = string.Empty;
        private readonly List<(Rectangle Area, XNAControl Control)> breadcrumbs = new List<(Rectangle Area, XNAControl Control)>();
        private readonly List<(string Text, XNAControl Control)> breadcrumbParts = new List<(string Text, XNAControl Control)>();
        private string listCaption = string.Empty;
        private string keySourceLine = string.Empty;
        private string editHintLine = string.Empty;
        private string editHintKey;
        private string suggestionState;
        private bool suggestionsDismissed;
        private string colorPickerText;
        private string resourcePickerText;
        private bool resourcePickerReset;
        private string pendingLine = string.Empty;
        private string status = "Click a control to inspect it. F1 lists the shortcuts.";
        private Color statusColor = DesignerStyle.DimText;

        private int infoTop;
        private int listCaptionY;
        private int statusTop;

        private void InitializePanel()
        {
            panel = new DesignerPanel(WindowManager);
            panel.Name = "designerPanel";
            panel.DrawBehindChildren = DrawPanelHeader;
            panel.DrawOverChildren = DrawPanelText;

            btnHelp = CreateHeaderButton("?", "Shortcuts (F1)", ToggleHelp);
            btnDock = CreateHeaderButton("<", "Dock to the other side (F7); drag the title bar to move", DockToOtherSide);
            btnCollapse = CreateHeaderButton("_", "Collapse to the title bar", ToggleCollapsed);
            btnClose = CreateHeaderButton("x", "Close the designer (F6)", Toggle);

            btnTabInspector = CreateTab("Inspect", "The keys of the selected control", PanelMode.Inspector);
            btnTabControls = CreateTab("Controls", "Every control of the window (Ctrl+F)", PanelMode.Controls);
            btnTabAdd = CreateTab("Add", "Add a control to the selected panel or window", PanelMode.AddControl);
            btnTabFiles = CreateTab("Theme files", "DTACnCNetClient.ini, GenericWindow.ini and other shared files", PanelMode.Files);

            btnDuplicate = CreateBodyButton("Duplicate", "Copy the control with its keys (Ctrl+D)", DuplicateSelected);
            btnChildren = CreateBodyButton("Inside", "Select the controls inside it (PageDown)", SelectChildren);
            btnOpenIni = CreateBodyButton("Open INI", "Open the file of the selected key or section in your editor", OpenIniFile);
            btnUnlink = CreateBodyButton("Unlink", "Replace the selected link to another control, or all of them, with a number", UnlinkSelected);
            btnOutlines = CreateBodyButton("Outlines", "Outline every control (F9)", ToggleOutlines, DesignerButtonStyle.Tab);
            btnDelete = CreateBodyButton("Delete", "Delete the selection (Del)", DeleteSelection, DesignerButtonStyle.Danger);
            actionButtons.AddRange(new[] { btnDuplicate, btnChildren, btnOpenIni, btnUnlink, btnOutlines, btnDelete });

            lbKeys = new DesignerListBox(WindowManager);
            lbKeys.Name = "designerKeys";
            lbKeys.SelectedIndexChanged += LbKeys_SelectedIndexChanged;
            lbKeys.MouseLeftDown += LbKeys_MouseLeftDown;
            lbKeys.LeftClick += LbKeys_LeftClick;
            lbKeys.DoubleLeftClick += LbKeys_DoubleLeftClick;
            lbKeys.RemoveClicked += LbKeys_RemoveClicked;
            AddToBody(lbKeys);

            tbEdit = new DesignerEditTextBox(WindowManager);
            tbEdit.Name = "designerEdit";
            tbEdit.FontIndex = FONT;
            tbEdit.MaximumTextLength = 1000;
            tbEdit.TextColor = DesignerStyle.Text;
            tbEdit.BackColor = DesignerStyle.Surface;
            tbEdit.IdleBorderColor = DesignerStyle.Border;
            tbEdit.ActiveBorderColor = DesignerStyle.Accent;
            tbEdit.EnterPressed += TbEdit_EnterPressed;
            tbEdit.InputReceived += (s, e) => suggestionsDismissed = false;
            tbEdit.PreviewKeyPress = TbEdit_PreviewKeyPress;
            AddToBody(tbEdit);

            lbSuggestions = new DesignerListBox(WindowManager);
            lbSuggestions.Name = "designerSuggestions";
            lbSuggestions.BorderColor = DesignerStyle.Accent;
            lbSuggestions.DrawBorders = true;
            // A list takes the focus when pressed, which would close the suggestions before the click
            lbSuggestions.MouseLeftDown += (s, e) => WindowManager.SelectedControl = tbEdit;
            lbSuggestions.LeftClick += (s, e) => AcceptSuggestion();

            btnSave = CreateBodyButton("Review & save", "See every change, then write the files (Ctrl+S)", StartReview, DesignerButtonStyle.Primary);
            btnUndo = CreateBodyButton("Undo", "Ctrl+Z", () => SetStatus(tools.Undo()));
            btnRedo = CreateBodyButton("Redo", "Ctrl+Y", () => SetStatus(tools.Redo()));
            btnDiscard = CreateBodyButton("Discard", "Drop every unsaved change", () => SetStatus(tools.Discard()));
            btnReload = CreateBodyButton("Reload", "Reload images and INI files from disk (F5)", () => SetStatus(tools.ReloadAll("Reloaded")));
            btnWrite = CreateBodyButton("Write files", "Save the changes listed above", SaveReviewed, DesignerButtonStyle.Primary);
            btnBack = CreateBodyButton("Back", "Esc", EndReview);

            colorPicker = new DesignerColorPicker(WindowManager);
            colorPicker.Name = "designerColorPicker";
            colorPicker.ColorChanged += ColorPicker_ColorChanged;
            colorPicker.ColorCommitted += (s, value) => ApplyEditText();
            AddToBody(colorPicker);

            resourcePicker = new DesignerResourcePicker(WindowManager);
            resourcePicker.ResourceChosen += ResourcePicker_ResourceChosen;
            AddToBody(resourcePicker);

            AddToBody(lbSuggestions);
            AddChild(panel);
        }

        private void AddToBody(XNAControl control)
        {
            panelBody.Add(control);
            panel.AddChild(control);
        }

        private DesignerButton CreateHeaderButton(string text, string toolTip, Action action)
        {
            var button = new DesignerButton(WindowManager, text, DesignerButtonStyle.Flat);
            button.ToolTipText = toolTip;
            button.LeftClick += (s, e) => action();
            allButtons.Add(button);
            panel.AddChild(button);
            return button;
        }

        private DesignerButton CreateTab(string text, string toolTip, PanelMode tabMode)
        {
            var button = new DesignerButton(WindowManager, text, DesignerButtonStyle.Tab);
            button.ToolTipText = toolTip;
            button.LeftClick += (s, e) => SetMode(tabMode);
            allButtons.Add(button);
            AddToBody(button);
            return button;
        }

        private DesignerButton CreateBodyButton(string text, string toolTip, Action action, DesignerButtonStyle style = DesignerButtonStyle.Normal)
        {
            var button = new DesignerButton(WindowManager, text, style);
            button.ToolTipText = toolTip;
            button.LeftClick += (s, e) => action();
            allButtons.Add(button);
            AddToBody(button);
            return button;
        }

        private void LayoutPanel()
        {
            ClientRectangle = new Rectangle(0, 0, WindowManager.RenderResolutionX, WindowManager.RenderResolutionY);
            PlacePanel();

            int inner = PANEL_WIDTH - MARGIN * 2;

            XNAControl[] header = { btnClose, btnCollapse, btnDock, btnHelp };
            int headerX = PANEL_WIDTH - 4;
            foreach (XNAControl button in header)
            {
                headerX -= 26;
                button.ClientRectangle = new Rectangle(headerX, 4, 24, HEADER_HEIGHT - 8);
            }

            btnCollapse.Text = panelCollapsed ? "+" : "_";
            btnDock.Text = panel.X + PANEL_WIDTH / 2 < Width / 2 ? ">" : "<";

            foreach (XNAControl control in panelBody)
                control.Visible = control.Enabled = !panelCollapsed;

            int y = HEADER_HEIGHT + 8;
            LayoutRow(new[] { btnTabInspector, btnTabControls, btnTabAdd, btnTabFiles }, y, TAB_HEIGHT, 4);

            infoTop = y + TAB_HEIGHT + 8;
            y = infoTop + LINE_HEIGHT * INFO_LINES + 4;
            LayoutRow(actionButtons, y, ACTION_HEIGHT, 4);

            listCaptionY = y + ACTION_HEIGHT + 10;
            int listY = listCaptionY + LINE_HEIGHT + 2;

            // From the bottom up: the status, the unsaved changes line, the buttons, the edit box and the line above it
            statusTop = Height - MARGIN - LINE_HEIGHT * STATUS_LINES;
            int buttonsY = statusTop - LINE_HEIGHT - 8 - BUTTON_HEIGHT;
            int editY = buttonsY - 8 - EDIT_HEIGHT;
            int listBottom = editY - LINE_HEIGHT - 4;

            lbKeys.ClientRectangle = new Rectangle(MARGIN, listY, inner, Math.Max(60, listBottom - listY));
            tbEdit.ClientRectangle = new Rectangle(MARGIN, editY, inner, EDIT_HEIGHT);

            LayoutRow(new[] { btnSave, btnUndo, btnRedo, btnDiscard, btnReload }, buttonsY, BUTTON_HEIGHT, 4, 1.6f);
            btnWrite.ClientRectangle = btnSave.ClientRectangle;
            btnBack.ClientRectangle = new Rectangle(btnUndo.X, btnUndo.Y, btnRedo.Right - btnUndo.X, BUTTON_HEIGHT);

            // The suggestions and the pickers cover the bottom of the list, leaving the hint line free
            int suggestionsHeight = SUGGESTION_LINES * lbSuggestions.LineHeight + 4;
            lbSuggestions.ClientRectangle = new Rectangle(MARGIN, lbKeys.Bottom - suggestionsHeight, inner, suggestionsHeight);
            colorPicker.ClientRectangle = new Rectangle(MARGIN, lbKeys.Bottom - DesignerColorPicker.PreferredHeight, inner,
                DesignerColorPicker.PreferredHeight);
            int pickerHeight = Math.Min(DesignerResourcePicker.PreferredHeight, lbKeys.Height);
            resourcePicker.ClientRectangle = new Rectangle(MARGIN, lbKeys.Bottom - pickerHeight, inner, pickerHeight);

            RefreshPanelVisibility();
        }

        // Lays buttons out side by side across the panel; the first one can be wider than the others
        private static void LayoutRow(IReadOnlyList<XNAControl> buttons, int y, int height, int gap, float firstWeight = 1f)
        {
            int inner = PANEL_WIDTH - MARGIN * 2 - gap * (buttons.Count - 1);
            float unit = inner / (buttons.Count - 1 + firstWeight);
            int x = MARGIN;

            for (int i = 0; i < buttons.Count; i++)
            {
                int width = i == buttons.Count - 1 ? PANEL_WIDTH - MARGIN - x : (int)(unit * (i == 0 ? firstWeight : 1f));
                buttons[i].ClientRectangle = new Rectangle(x, y, width, height);
                x += width + gap;
            }
        }

        public void SetStatus(string text) => SetStatus(text, DesignerStyle.DimText);

        private void SetStatus(string text, Color color)
        {
            status = text;
            statusColor = color;
            RefreshPanel();
        }

        private void UpdateButtonStates()
        {
            bool hasPending = tools.PendingEdits.Count > 0;
            btnSave.IsDisabled = !hasPending;
            btnDiscard.IsDisabled = !hasPending;
            btnUndo.IsDisabled = !tools.CanUndo;
            btnRedo.IsDisabled = !tools.CanRedo;

            bool canDelete = selection.Count > 0 && !selection.Any(IsWindow);
            btnDelete.IsDisabled = !canDelete;
            btnDuplicate.IsDisabled = selection.Count != 1 || !canDelete;
            btnChildren.IsDisabled = selected == null || !GetSelectableChildren(selected).Any();
            btnOpenIni.IsDisabled = mode == PanelMode.Files ? filePath == null : selected == null || IniWindow.OwnerOf(selected) == null;
            btnUnlink.IsDisabled = selectedLinks.Count == 0;
            btnHelp.Checked = showingHelp;
            btnOutlines.Checked = showOutlines;
        }

        // Which panel controls are shown for the mode, while the panel isn't collapsed
        private void RefreshPanelVisibility()
        {
            if (panelCollapsed)
                return;

            btnTabInspector.Checked = mode == PanelMode.Inspector;
            btnTabControls.Checked = mode == PanelMode.Controls;
            btnTabAdd.Checked = mode == PanelMode.AddControl;
            btnTabFiles.Checked = mode == PanelMode.Files;

            SetShown(btnSave, !reviewing);
            SetShown(btnUndo, !reviewing);
            SetShown(btnRedo, !reviewing);
            SetShown(btnDiscard, !reviewing);
            SetShown(btnWrite, reviewing);
            SetShown(btnBack, reviewing);
            SetShown(tbEdit, !reviewing && !showingHelp);

            bool actions = !reviewing && (mode == PanelMode.Inspector || mode == PanelMode.Controls);
            foreach (DesignerButton button in actionButtons)
                SetShown(button, actions);

            SetShown(btnOpenIni, actions || (!reviewing && mode == PanelMode.Files));
        }

        private static void SetShown(XNAControl control, bool shown) => control.Visible = control.Enabled = shown;

        private void SetMode(PanelMode newMode)
        {
            if (newMode == PanelMode.AddControl && !PrepareAddControl())
                return;

            if (newMode == PanelMode.Files)
                fileLevel = 0;

            if (newMode != mode || newMode == PanelMode.AddControl)
                tbEdit.Text = string.Empty;

            mode = newMode;
            reviewing = false;
            showingHelp = false;
            controlsFilter = null;
            RefreshPanel();

            if (mode == PanelMode.AddControl && lbKeys.Items.Count > 0)
            {
                lbKeys.SelectedIndex = 0;
                WindowManager.SelectedControl = tbEdit;
            }
        }

        private void ToggleHelp()
        {
            showingHelp = !showingHelp;
            reviewing = false;
            RefreshPanel();
        }

        private void RefreshPanel()
        {
            if (panel == null)
                return;

            RefreshPanelVisibility();

            int pending = tools.PendingEdits.Count;
            pendingLine = pending == 0 ? "No unsaved changes" :
                $"{pending} unsaved change(s) in {string.Join(", ", tools.PendingEdits.Select(p => Path.GetFileName(p.Edit.FilePath)).Distinct(StringComparer.OrdinalIgnoreCase))}";

            selectedLinks = selected != null && IniWindow.OwnerOf(selected) is IniWindow window ?
                ControlLinks.Find(window).Where(l => l.Follower == selected || l.Source == selected).ToList() : new List<ControlLink>();

            string previousKey = lbKeys.SelectedItem?.Tag as string;
            lbKeys.Clear();
            lbKeys.SelectedIndex = -1;
            lbKeys.ValueColumn = 0.42f;

            if (showingHelp)
                FillHelpList();
            else if (reviewing)
                FillReviewList();
            else if (mode == PanelMode.Files)
                FillFileList();
            else if (mode == PanelMode.AddControl)
                FillControlTypeList();
            else if (mode == PanelMode.Controls)
                FillControlTree();
            else
                FillKeyList(previousKey);

            FillInfoLines();
        }

        #region Lists

        private void FillHelpList()
        {
            listCaption = "Shortcuts";
            lbKeys.ValueColumn = 0.36f;

            foreach (var (keys, action) in Shortcuts)
                lbKeys.AddItem(new DesignerListItem(keys, DesignerStyle.Text, action) { Selectable = false });
        }

        private void FillKeyList(string previousKey)
        {
            listCaption = "Keys";
            IniWindow window = selected == null ? null : IniWindow.OwnerOf(selected);
            string section = window?.SectionFor(selected);
            var iniSection = section == null ? null : window.Ini?.GetSection(section);

            if (iniSection == null)
            {
                keySourceLine = string.Empty;

                if (selected != null && section != null)
                    lbKeys.AddItem(new DesignerListItem("No keys yet. Type Key=Value below to add one.", DesignerStyle.DimText) { Selectable = false });

                AddLinkRows();
                return;
            }

            string themeDirectory = Path.GetFullPath(ProgramConstants.GetResourcePath());

            foreach (var kvp in iniSection.Keys)
            {
                IniKeySource source = window.Ini.GetKeySource(section, kvp.Key);
                bool isPending = source != null && IniOverlay.GetEdits(source.FilePath).Any(e => e.Section == source.Section && e.Key == kvp.Key);
                bool isTheme = source != null && Path.GetFullPath(source.FilePath).StartsWith(themeDirectory, StringComparison.OrdinalIgnoreCase);
                Color marker = isPending ? DesignerStyle.PendingKey : isTheme ? DesignerStyle.ThemeKey : DesignerStyle.InheritedKey;

                var item = new DesignerListItem(kvp.Key, isPending ? DesignerStyle.PendingKey : isTheme ? DesignerStyle.Text : DesignerStyle.DimText, kvp.Value)
                {
                    Marker = marker,
                    ValueColor = isPending ? DesignerStyle.PendingKey : isTheme ? DesignerStyle.ThemeKey : DesignerStyle.DimText,
                    Tag = kvp.Key,
                    Removable = IniEditTargets.ResolveRemoval(window, section, kvp.Key) != null,
                };
                lbKeys.AddItem(item);

                if (kvp.Key == previousKey)
                    lbKeys.SelectedIndex = lbKeys.Items.Count - 1;
            }

            AddLinkRows();
        }

        /// <summary>
        /// A row of the key list for another control the selected one is linked to.
        /// </summary>
        private sealed class LinkRow
        {
            public LinkRow(XNAControl other, List<ControlLink> links)
            {
                Other = other;
                Links = links;
            }

            public XNAControl Other { get; }
            public List<ControlLink> Links { get; }
        }

        // The controls the selected one is positioned from, and those positioned from it
        private void AddLinkRows()
        {
            AddLinkRows("Positioned from", selectedLinks.Where(l => l.Follower == selected).GroupBy(l => l.Source), SourceLinkColor);
            AddLinkRows("Followed by", selectedLinks.Where(l => l.Source == selected).GroupBy(l => l.Follower), FollowerLinkColor);
        }

        private void AddLinkRows(string heading, IEnumerable<IGrouping<XNAControl, ControlLink>> groups, Color color)
        {
            bool first = true;

            foreach (var group in groups)
            {
                if (first)
                {
                    lbKeys.AddItem(new DesignerListItem(heading, DesignerStyle.DimText) { IsGroupHeading = true, Selectable = false });
                    first = false;
                }

                lbKeys.AddItem(new DesignerListItem(group.Key.Name, DesignerStyle.Text, string.Join(", ", group.Select(l => l.Key).Distinct()))
                {
                    Marker = color,
                    ValueColor = color,
                    Tag = new LinkRow(group.Key, group.ToList()),
                });
            }
        }

        /// <summary>
        /// Lists the controls of the selected control's window, or of every open window, as a tree.
        /// With text in the edit box, lists the controls whose name contains it instead.
        /// </summary>
        private void FillControlTree()
        {
            controlsFilter = tbEdit.Text ?? string.Empty;
            string filter = controlsFilter.Trim();

            XNAControl root = selected == null ? null : (IniWindow.OwnerOf(selected)?.Control ?? GetRoot(selected));
            List<XNAControl> roots = root != null ? new List<XNAControl> { root } :
                WindowManager.TopLevelControls.Where(c => c != this && c.Visible).ToList();

            listCaption = root != null ? "Controls of " + Describe(root) : "Controls of every open window";
            lbKeys.ValueColumn = 0.6f;

            foreach (XNAControl control in roots)
                AddControlRows(control, 0, filter);

            if (lbKeys.Items.Count == 0)
                lbKeys.AddItem(new DesignerListItem("No control names contain \"" + filter + "\"", DesignerStyle.DimText) { Selectable = false });

            int index = lbKeys.Items.FindIndex(i => i.Tag == selected);
            if (index > -1)
            {
                lbKeys.SelectedIndex = index;
                lbKeys.ScrollToSelectedElement();
            }
        }

        private void AddControlRows(XNAControl control, int depth, string filter)
        {
            // Unnamed controls can't be configured, so only those with named controls inside are listed
            if (tools.IsDeleted(control) || (string.IsNullOrEmpty(control.Name) && !HasNamedDescendant(control)))
                return;

            bool part = IsInternalPart(control);
            bool matches = filter.Length == 0 || (control.Name ?? string.Empty).IndexOf(filter, StringComparison.OrdinalIgnoreCase) > -1;

            if (!part && matches)
            {
                bool shown = IsShown(control);
                string type = control.GetType().Name + (shown ? string.Empty : ", hidden");
                if (filter.Length > 0 && control.Parent != null)
                    type += " in " + control.Parent.Name;

                lbKeys.AddItem(new DesignerListItem(string.IsNullOrEmpty(control.Name) ? "(unnamed)" : control.Name,
                    shown ? DesignerStyle.Text : DesignerStyle.FaintText, type)
                {
                    Indent = filter.Length > 0 ? 0 : depth,
                    Marker = tools.EditedControls.Contains(control) ? DesignerStyle.PendingKey : (Color?)null,
                    ValueColor = shown ? DesignerStyle.DimText : DesignerStyle.FaintText,
                    Tag = control,
                });
            }

            // The parts of a multi-column list box aren't controls of their own
            if (control is XNAMultiColumnListBox)
                return;

            foreach (XNAControl child in control.Children)
                AddControlRows(child, part ? depth : depth + 1, filter);
        }

        private static bool HasNamedDescendant(XNAControl control) =>
            control.Children.Any(c => (!string.IsNullOrEmpty(c.Name) && !IsInternalPart(c)) || HasNamedDescendant(c));

        private static XNAControl GetRoot(XNAControl control)
        {
            while (control.Parent != null)
                control = control.Parent;

            return control;
        }

        private void FillFileList()
        {
            fileIni = null;

            if (fileLevel == 0)
            {
                listCaption = "Theme-wide files";
                lbKeys.ValueColumn = 0.45f;

                foreach (var (file, description) in GlobalFiles)
                {
                    string path = ResolveGlobalFile(file);
                    if (path == null)
                        continue;

                    lbKeys.AddItem(new DesignerListItem(file, DesignerStyle.Text, description) { Tag = path });
                }

                return;
            }

            lbKeys.AddItem(new DesignerListItem(".. back", DesignerStyle.DimText) { Tag = ".." });
            fileIni = new CCIniFile(filePath);
            string fullPath = Path.GetFullPath(filePath);

            if (fileLevel == 1)
            {
                listCaption = "Sections of " + Path.GetFileName(filePath);

                foreach (string section in fileIni.GetSections())
                {
                    bool inThisFile = fileIni.GetSectionKeys(section).Any(k =>
                        string.Equals(fileIni.GetKeySource(section, k)?.FilePath, fullPath, StringComparison.OrdinalIgnoreCase));

                    lbKeys.AddItem(new DesignerListItem("[" + section + "]", inThisFile ? DesignerStyle.Text : DesignerStyle.DimText,
                        fileIni.GetSectionKeys(section).Count + " keys")
                    {
                        Marker = inThisFile ? DesignerStyle.ThemeKey : DesignerStyle.InheritedKey,
                        Tag = section,
                    });
                }

                return;
            }

            listCaption = $"[{fileSection}] of {Path.GetFileName(filePath)}";
            IReadOnlyList<IniOverlayEdit> pending = IniOverlay.GetEdits(filePath);

            foreach (string key in fileIni.GetSectionKeys(fileSection) ?? new List<string>())
            {
                IniKeySource source = fileIni.GetKeySource(fileSection, key);
                bool isPending = pending.Any(e => e.Section == fileSection && e.Key == key);
                bool inThisFile = string.Equals(source?.FilePath, fullPath, StringComparison.OrdinalIgnoreCase);
                Color color = isPending ? DesignerStyle.PendingKey : inThisFile ? DesignerStyle.ThemeKey : DesignerStyle.InheritedKey;

                lbKeys.AddItem(new DesignerListItem(key, isPending ? DesignerStyle.PendingKey : DesignerStyle.Text,
                    fileIni.GetStringValue(fileSection, key, string.Empty))
                {
                    Marker = color,
                    ValueColor = isPending ? DesignerStyle.PendingKey : inThisFile ? DesignerStyle.ThemeKey : DesignerStyle.DimText,
                    Tag = key,
                });
            }
        }

        private static string ResolveGlobalFile(string fileName)
        {
            string themePath = SafePath.CombineFilePath(ProgramConstants.GetResourcePath(), fileName);
            if (IniOverlay.FileExists(themePath))
                return themePath;

            string basePath = SafePath.CombineFilePath(ProgramConstants.GetBaseResourcePath(), fileName);
            return File.Exists(basePath) ? basePath : null;
        }

        private void FillControlTypeList()
        {
            listCaption = "Type of the new control";
            lbKeys.ValueColumn = 0.4f;

            foreach (NewControlType type in NewControlTypes)
                lbKeys.AddItem(new DesignerListItem(type.TypeName, DesignerStyle.Text, type.Description) { Tag = type.TypeName });
        }

        private void FillReviewList()
        {
            listCaption = "Unsaved changes";
            lbKeys.ValueColumn = 0.45f;

            foreach (var file in tools.PendingEdits.GroupBy(p => Path.GetFullPath(p.Edit.FilePath), StringComparer.OrdinalIgnoreCase))
            {
                bool createsFile = file.First().CreatesFile;
                lbKeys.AddItem(new DesignerListItem(DesignerTools.DisplayPath(file.Key) + (createsFile ? "  (new file)" : string.Empty),
                    DesignerStyle.Title) { IsGroupHeading = true, Selectable = false });

                foreach (PendingEdit edit in file)
                {
                    IniOverlayEdit e = edit.Edit;
                    DesignerListItem item;

                    if (e.RemovesSection)
                        item = new DesignerListItem($"[{e.Section}]", DesignerStyle.Removed, "removed with all its keys") { Marker = DesignerStyle.Removed };
                    else if (e.RemovesKey)
                        item = new DesignerListItem($"[{e.Section}] {e.Key}", DesignerStyle.Removed, "removed, was " + edit.OldValue) { Marker = DesignerStyle.Removed };
                    else if (edit.OldValue == null)
                        item = new DesignerListItem($"[{e.Section}] {e.Key}", DesignerStyle.Text, e.Value) { Marker = DesignerStyle.ThemeKey, ValueColor = DesignerStyle.ThemeKey };
                    else
                        item = new DesignerListItem($"[{e.Section}] {e.Key}", DesignerStyle.Text, edit.OldValue + "  >  " + e.Value) { Marker = DesignerStyle.PendingKey, ValueColor = DesignerStyle.PendingKey };

                    item.Selectable = false;
                    lbKeys.AddItem(item);
                }
            }
        }

        private void LbKeys_LeftClick(object sender, InputEventArgs e)
        {
            if (reviewing || showingHelp)
                return;

            if (mode == PanelMode.Controls)
            {
                if (lbKeys.SelectedItem?.Tag is XNAControl control && control != selected)
                    Select(control);

                return;
            }

            if (mode != PanelMode.Files || lbKeys.SelectedItem?.Tag is not string tag)
                return;

            if (tag == "..")
            {
                fileLevel--;
            }
            else if (fileLevel == 0)
            {
                filePath = tag;
                fileLevel = 1;
            }
            else if (fileLevel == 1)
            {
                fileSection = tag;
                fileLevel = 2;
            }
            else
            {
                // Clicking the selected key again loads it again
                LbKeys_SelectedIndexChanged(sender, EventArgs.Empty);
                return;
            }

            tbEdit.Text = string.Empty;
            RefreshPanel();
        }

        private void LbKeys_MouseLeftDown(object sender, InputEventArgs e)
        {
            // A key picked from the list goes to the edit box ready to be edited, but the list takes the focus when pressed
            if (!reviewing && !showingHelp && mode != PanelMode.Controls && lbKeys.SelectedItem?.Tag is string)
                WindowManager.SelectedControl = tbEdit;
        }

        private void LbKeys_DoubleLeftClick(object sender, InputEventArgs e)
        {
            if (reviewing || showingHelp)
                return;

            if (mode == PanelMode.Controls && lbKeys.SelectedItem?.Tag is XNAControl)
                SetMode(PanelMode.Inspector);
            else if (mode == PanelMode.AddControl && lbKeys.SelectedItem != null)
                ApplyEditText();
            else if (mode == PanelMode.Inspector && lbKeys.SelectedItem?.Tag is LinkRow row)
                Select(row.Other);
            else if (lbKeys.SelectedItem?.Tag is string key && key != ".." &&
                ((mode == PanelMode.Inspector && selected != null) || (mode == PanelMode.Files && fileLevel == 2)))
            {
                EditListedKey();
            }
        }

        /// <summary>
        /// Flips the double-clicked key if it is true or false, and otherwise selects its value in the edit box to be typed over.
        /// </summary>
        private void EditListedKey()
        {
            // Back to the listed value, in case the first click's text was changed since
            LbKeys_SelectedIndexChanged(lbKeys, EventArgs.Empty);

            string text = tbEdit.Text ?? string.Empty;
            int equals = text.IndexOf('=');
            if (equals < 1)
                return;

            string key = text.Substring(0, equals);
            string flipped = FlipBoolean(key, text.Substring(equals + 1).Trim());
            if (flipped != null)
            {
                SetEditText(key + "=" + flipped);
                ApplyEditText();
                return;
            }

            tbEdit.SetSelection(equals + 1, text.Length);
            tbEdit.InputPosition = text.Length;
        }

        /// <summary>
        /// Returns the opposite of a true/false value in the same style (True becomes False, yes becomes no),
        /// or null if the value isn't one.
        /// </summary>
        private static string FlipBoolean(string key, string value)
        {
            bool isBooleanKey = DesignerKeyCatalog.IsBooleanKey(key);
            string flipped = value.ToLowerInvariant() switch
            {
                "true" => "false",
                "false" => "true",
                "yes" => "no",
                "no" => "yes",
                "1" when isBooleanKey => "0",
                "0" when isBooleanKey => "1",
                _ => null,
            };

            if (flipped == null || !char.IsLetter(value[0]))
                return flipped;

            if (value.ToUpperInvariant() == value)
                return flipped.ToUpperInvariant();

            return char.IsUpper(value[0]) ? char.ToUpperInvariant(flipped[0]) + flipped.Substring(1) : flipped;
        }

        private void LbKeys_SelectedIndexChanged(object sender, EventArgs e)
        {
            keySourceLine = string.Empty;

            if (!reviewing && !showingHelp && lbKeys.SelectedItem?.Tag is LinkRow row)
            {
                ControlLink link = row.Links[0];
                keySourceLine = $"{link.Follower.Name}: {link.Key}={link.Value}. Double-click selects {row.Other.Name}.";
                return;
            }

            if (reviewing || showingHelp || lbKeys.SelectedItem?.Tag is not string key)
                return;

            if (mode == PanelMode.AddControl)
            {
                // Follow the chosen type with the suggested name, unless a name was typed
                if (string.IsNullOrEmpty(tbEdit.Text) || tbEdit.Text == suggestedControlName)
                {
                    suggestedControlName = SuggestControlName(GetChosenControlType());
                    tbEdit.Text = suggestedControlName ?? string.Empty;
                }

                // So Enter adds it
                tbEdit.InputPosition = tbEdit.Text.Length;
                WindowManager.SelectedControl = tbEdit;
                return;
            }

            if (mode == PanelMode.Files)
            {
                if (fileLevel != 2 || key == ".." || fileIni == null)
                    return;

                SetEditText(key + "=" + fileIni.GetStringValue(fileSection, key, string.Empty));

                IniKeySource fileSource = fileIni.GetKeySource(fileSection, key);
                if (fileSource != null)
                    keySourceLine = $"From {DesignerTools.DisplayPath(fileSource.FilePath)} [{fileSource.Section}]";

                return;
            }

            if (selected == null || mode != PanelMode.Inspector)
                return;

            IniWindow window = IniWindow.OwnerOf(selected);
            string section = window?.SectionFor(selected);
            if (window?.Ini == null || section == null)
                return;

            SetEditText(key + "=" + window.Ini.GetStringValue(section, key, string.Empty));

            IniKeySource source = window.Ini.GetKeySource(section, key);
            if (source != null)
            {
                keySourceLine = $"From {DesignerTools.DisplayPath(source.FilePath)} [{source.Section}]" +
                    (source.Section != section ? " via $BaseSection" : string.Empty);
            }
        }

        #endregion

        #region Info

        private void FillInfoLines()
        {
            for (int i = 0; i < INFO_LINES; i++)
                infoLines[i] = string.Empty;

            infoType = string.Empty;
            breadcrumbParts.Clear();

            if (reviewing)
            {
                infoLines[0] = "Review";
                infoLines[1] = "These changes are written when you press Write files.";
                infoLines[2] = "Green lines are new keys, yellow changed ones, red removed ones.";
                return;
            }

            if (mode == PanelMode.Files)
            {
                infoLines[0] = "Theme-wide files";
                if (fileLevel == 0)
                {
                    infoLines[1] = "Pick a file. Edits go to that file after review.";
                    return;
                }

                infoLines[1] = "File " + DesignerTools.DisplayPath(filePath);
                infoLines[2] = GlobalFiles.FirstOrDefault(f => string.Equals(f.File, Path.GetFileName(filePath), StringComparison.OrdinalIgnoreCase)).Description;
                infoLines[3] = fileLevel == 2 ? $"Section [{fileSection}]" : "Pick a section";
                infoLines[4] = fileLevel == 2 ? "Type Key=Value to change or add a key" : string.Empty;
                return;
            }

            if (mode == PanelMode.AddControl)
            {
                IniWindow addWindow = IniWindow.OwnerOf(addContainer);
                infoLines[0] = "Add a control to " + Describe(addContainer);
                infoLines[1] = "Pick a type, type a name and press Enter.";
                infoLines[2] = "It appears in the middle of " + addContainer.Name + ", ready to be dragged into place.";
                infoLines[3] = addWindow == null ? string.Empty : "Saved to " + string.Join(", ", GetTargetFiles(new[] { addContainer }));
                return;
            }

            if (selected == null)
            {
                infoLines[0] = "Nothing selected";
                infoLines[1] = mode == PanelMode.Controls ? "Pick a control below, or type to find one by name." : "Click a control to inspect it.";
                infoLines[2] = "Ctrl+click selects several; Shift+click a part of a list.";
                return;
            }

            FillBreadcrumb();

            Rectangle screen = selected.GetWindowRectangle();
            infoLines[1] = string.IsNullOrEmpty(selected.Name) ? "(unnamed)" : selected.Name;
            infoType = selected.GetType().Name;
            infoLines[4] = $"{Format(selected.ClientRectangle)} in parent, at {screen.X},{screen.Y} on screen";

            if (selection.Count > 1)
            {
                Rectangle bounds = GetSelectionBounds();
                infoType += $" and {selection.Count - 1} more";
                infoLines[4] = $"Together {bounds.Width}x{bounds.Height}: also " + ListNames(selection.Take(selection.Count - 1).Select(c => c.Name), 4);
            }

            IniWindow window = IniWindow.OwnerOf(selected);
            if (window == null)
            {
                infoLines[2] = "Not inside a window that reads an INI file";
                return;
            }

            string themeFolder = DesignerTools.DisplayPath(ProgramConstants.GetResourcePath()).TrimEnd('\\', '/');
            string ownFile = themeFolder + "\\" + window.OwnIniFileName;

            if (window.IniPath == null)
                infoLines[2] = $"No INI file yet; the first edit creates {ownFile}";
            else if (window.UsesGenericWindowIni)
                infoLines[2] = $"Uses shared {DesignerTools.DisplayPath(window.IniPath)}; edits go to {ownFile}";
            else
                infoLines[2] = "INI " + DesignerTools.DisplayPath(window.IniPath);

            string section = window.SectionFor(selected);
            if (section == null)
            {
                infoLines[3] = "No name, so no INI section can refer to it";
                return;
            }

            ControlDeclaration declaration = selected == window.Control ? null : window.FindDeclaration(selected.Name);
            string createdBy = selected == window.Control ? "the window" :
                declaration == null ? "created by code" :
                declaration.Key.StartsWith("$CC", StringComparison.Ordinal) ? $"created by {declaration.Key} in [{declaration.Section}]" :
                $"created by [{declaration.Section}]";

            infoLines[3] = (window.Ini?.GetSection(section) == null ? $"[{section}] not in the INI yet" : $"Section [{section}]") + ", " + createdBy;

            if (selection.Count > 1)
                infoLines[3] = "Changes go to " + string.Join(", ", GetTargetFiles(selection));
        }

        private void FillBreadcrumb()
        {
            var chain = new List<XNAControl>();
            for (XNAControl control = selected; control != null; control = control.Parent)
            {
                if (!IsInternalPart(control))
                    chain.Insert(0, control);
            }

            // The nearest window and what is inside it, so long chains fit
            int windowIndex = chain.FindLastIndex(c => IniWindow.From(c) != null && c != selected);
            if (windowIndex > 0)
            {
                chain.RemoveRange(0, windowIndex);
                breadcrumbParts.Add(("...", chain[0].Parent));
            }

            foreach (XNAControl control in chain)
                breadcrumbParts.Add((string.IsNullOrEmpty(control.Name) ? "(unnamed)" : control.Name, control));
        }

        private XNAControl FindBreadcrumbAt(Point point)
        {
            Point local = new Point(point.X - panel.X, point.Y - panel.Y);

            foreach (var crumb in breadcrumbs)
            {
                if (crumb.Area.Contains(local) && crumb.Control != null && crumb.Control != selected)
                    return crumb.Control;
            }

            return null;
        }

        /// <summary>
        /// Returns the names of the files a position change of these controls would be saved to.
        /// </summary>
        private static IEnumerable<string> GetTargetFiles(IEnumerable<XNAControl> controls)
        {
            var files = new List<string>();

            foreach (XNAControl control in controls)
            {
                IniWindow window = IniWindow.OwnerOf(control);
                string section = window?.SectionFor(control);
                if (section == null)
                    continue;

                string file = Path.GetFileName(IniEditTargets.Resolve(window, section, "X", "0").Last().FilePath);
                if (!files.Contains(file))
                    files.Add(file);
            }

            return files;
        }

        private static string ListNames(IEnumerable<string> names, int max = 3)
        {
            List<string> list = names.ToList();
            return list.Count <= max ? string.Join(", ", list) : string.Join(", ", list.Take(max)) + $" and {list.Count - max} more";
        }

        private static string Format(Rectangle r) => $"{r.X},{r.Y} {r.Width}x{r.Height}";

        #endregion

        #region Edit box helpers

        private void UpdateEditHelpers()
        {
            if (mode == PanelMode.Controls && !reviewing && !showingHelp && controlsFilter != null && (tbEdit.Text ?? string.Empty) != controlsFilter)
                RefreshPanel();

            UpdateSuggestions();
            UpdateColorPicker();
            UpdateResourcePicker();
        }

        /// <summary>
        /// Lists the keys the selected control can read that match what is typed before the '=',
        /// and keeps the hint line under the list describing the value the current key expects.
        /// </summary>
        private void UpdateSuggestions()
        {
            bool editing = !reviewing && !showingHelp && mode == PanelMode.Inspector && selected != null && WindowManager.SelectedControl == tbEdit;
            string text = tbEdit.Text ?? string.Empty;
            string state = editing ? selected.GetHashCode() + "|" + text : null;

            if (state != suggestionState)
            {
                suggestionState = state;
                FillSuggestions(editing && !text.Contains("=") ? text.Trim() : null);
            }

            bool show = editing && !suggestionsDismissed && lbSuggestions.Items.Count > 0 && !panelCollapsed;
            lbSuggestions.Visible = lbSuggestions.Enabled = show;

            string hintKey = !editing ? null :
                show ? lbSuggestions.SelectedItem?.Tag as string :
                text.Contains("=") ? text.Substring(0, text.IndexOf('=')).Trim() : null;

            if (hintKey != editHintKey)
            {
                editHintKey = hintKey;
                string hint = hintKey == null ? null : DesignerKeyCatalog.GetValueHint(hintKey);
                editHintLine = hint == null ? string.Empty : $"{hintKey}: {hint}";
            }
        }

        private void FillSuggestions(string filter)
        {
            lbSuggestions.Clear();

            if (filter == null)
                return;

            IniWindow window = IniWindow.OwnerOf(selected);
            string section = window?.SectionFor(selected);
            if (window == null || section == null)
                return;

            Dictionary<string, string> current = GetKeys(window, section);
            IEnumerable<string> keys = DesignerKeyCatalog.GetKeys(selected, window.Control is ClientGUI.INItializableWindow)
                .Union(current.Keys)
                .Where(k => filter.Length == 0 || k.IndexOf(filter, StringComparison.OrdinalIgnoreCase) > -1)
                .OrderBy(k => k.StartsWith(filter, StringComparison.OrdinalIgnoreCase) ? 0 : 1)
                .ThenBy(k => k, StringComparer.OrdinalIgnoreCase);

            foreach (string key in keys)
            {
                bool isSet = current.TryGetValue(key, out string value);
                lbSuggestions.AddItem(new DesignerListItem(key, isSet ? DesignerStyle.Text : DesignerStyle.UnsetKey, isSet ? value : "not set")
                {
                    ValueColor = isSet ? DesignerStyle.DimText : DesignerStyle.FaintText,
                    Tag = key,
                });
            }

            lbSuggestions.TopIndex = 0;
            lbSuggestions.SelectedIndex = filter.Length > 0 && lbSuggestions.Items.Count > 0 ? 0 : -1;
        }

        private bool TbEdit_PreviewKeyPress(Keys key)
        {
            if (resourcePicker.Visible)
            {
                switch (key)
                {
                    case Keys.Down:
                        resourcePicker.MoveSelection(1);
                        return true;
                    case Keys.Up:
                        resourcePicker.MoveSelection(-1);
                        return true;
                    case Keys.PageDown:
                        resourcePicker.MoveSelection(8);
                        return true;
                    case Keys.PageUp:
                        resourcePicker.MoveSelection(-8);
                        return true;
                }
            }

            if (!lbSuggestions.Visible)
                return false;

            switch (key)
            {
                case Keys.Down:
                    MoveSuggestion(1);
                    return true;
                case Keys.Up:
                    MoveSuggestion(-1);
                    return true;
                case Keys.PageDown:
                    MoveSuggestion(SUGGESTION_LINES - 1);
                    return true;
                case Keys.PageUp:
                    MoveSuggestion(1 - SUGGESTION_LINES);
                    return true;
                case Keys.Tab:
                    AcceptSuggestion();
                    return true;
                case Keys.Enter:
                    if (lbSuggestions.SelectedIndex < 0)
                        return false;

                    AcceptSuggestion();
                    return true;
                case Keys.Escape:
                    suggestionsDismissed = true;
                    return true;
            }

            return false;
        }

        private void MoveSuggestion(int delta)
        {
            int index = Math.Max(0, Math.Min(lbSuggestions.Items.Count - 1, lbSuggestions.SelectedIndex + delta));
            lbSuggestions.SelectedIndex = index;

            int visibleLines = lbSuggestions.Height / lbSuggestions.LineHeight;
            if (index < lbSuggestions.TopIndex)
                lbSuggestions.TopIndex = index;
            else if (index >= lbSuggestions.TopIndex + visibleLines)
                lbSuggestions.TopIndex = index - visibleLines + 1;
        }

        /// <summary>
        /// Puts the chosen key in the edit box, with its current value if it has one.
        /// </summary>
        private void AcceptSuggestion()
        {
            if (lbSuggestions.SelectedItem?.Tag is not string key)
                return;

            IniWindow window = IniWindow.OwnerOf(selected);
            string section = window?.SectionFor(selected);
            string value = window?.Ini != null && section != null ? window.Ini.GetStringValue(section, key, string.Empty) : string.Empty;
            SetEditText(key + "=" + value);
        }

        private void SetEditText(string text)
        {
            // A key's current value shows the whole list with it selected, rather than filtering it
            resourcePickerReset = true;
            tbEdit.Text = text;
            tbEdit.InputPosition = text.Length;
            WindowManager.SelectedControl = tbEdit;
        }

        // The key in the edit box, when the selected control or open file section can be edited
        private string GetEditedKey(out string value)
        {
            value = null;
            string text = tbEdit.Text ?? string.Empty;
            int equals = text.IndexOf('=');
            bool canEdit = !reviewing && !showingHelp && !panelCollapsed &&
                ((mode == PanelMode.Inspector && selected != null) || (mode == PanelMode.Files && fileLevel == 2));

            if (!canEdit || equals < 1)
                return null;

            value = text.Substring(equals + 1).Trim();
            return text.Substring(0, equals).Trim();
        }

        private void ColorPicker_ColorChanged(object sender, string value)
        {
            string text = tbEdit.Text ?? string.Empty;
            int equals = text.IndexOf('=');
            if (equals < 1)
                return;

            tbEdit.Text = text.Substring(0, equals + 1) + value;
            tbEdit.InputPosition = tbEdit.Text.Length;
            colorPickerText = tbEdit.Text;
        }

        /// <summary>
        /// Shows the color picker while the edit box holds a key that takes a color.
        /// </summary>
        private void UpdateColorPicker()
        {
            string key = GetEditedKey(out string value);
            bool show = key != null && !lbSuggestions.Visible && DesignerKeyCatalog.GetValueKind(key) == DesignerValueKind.Color;

            if (show && tbEdit.Text != colorPickerText)
            {
                colorPickerText = tbEdit.Text;
                colorPicker.SetValue(value);
            }

            colorPicker.Visible = colorPicker.Enabled = show;
        }

        /// <summary>
        /// Shows the image or sound list while the edit box holds a key that takes a file,
        /// filtered by what is typed after the '='.
        /// </summary>
        private void UpdateResourcePicker()
        {
            string key = GetEditedKey(out string value);
            DesignerValueKind kind = key == null || lbSuggestions.Visible ? DesignerValueKind.Other : DesignerKeyCatalog.GetValueKind(key);
            bool show = kind == DesignerValueKind.Image || kind == DesignerValueKind.Sound;

            if (show && (!resourcePicker.Visible || resourcePicker.Kind != kind || resourcePickerReset))
            {
                resourcePicker.Open(kind);
                resourcePicker.SetFilter(string.Empty);
                resourcePicker.SelectName(value);
                resourcePickerText = tbEdit.Text;
            }
            else if (show && tbEdit.Text != resourcePickerText)
            {
                // What is typed filters the list; what the picker put there doesn't
                resourcePickerText = tbEdit.Text;
                resourcePicker.SetFilter(value);
            }

            resourcePickerReset = false;

            resourcePicker.Visible = resourcePicker.Enabled = show;
        }

        private void ResourcePicker_ResourceChosen(object sender, ResourceChosenEventArgs e)
        {
            string text = tbEdit.Text ?? string.Empty;
            int equals = text.IndexOf('=');
            if (equals < 1)
                return;

            // Keep the slash the value used; the stock INI files use backslashes
            string value = text.Substring(equals + 1);
            string name = value.Contains("/") ? e.Name : e.Name.Replace('/', '\\');

            tbEdit.Text = text.Substring(0, equals + 1) + name;
            tbEdit.InputPosition = tbEdit.Text.Length;
            resourcePickerText = tbEdit.Text;
            WindowManager.SelectedControl = tbEdit;

            if (e.Clicked)
                ApplyEditText();
        }

        private static Dictionary<string, string> GetKeys(IniWindow window, string section)
        {
            var keys = new Dictionary<string, string>();
            var iniSection = window.Ini?.GetSection(section);

            if (iniSection != null)
            {
                foreach (var kvp in iniSection.Keys)
                    keys[kvp.Key] = kvp.Value;
            }

            return keys;
        }

        #endregion

        #region Drawing

        private void DrawPanelHeader()
        {
            int x = panel.X + MARGIN;
            int y = panel.Y;

            FillRectangle(new Rectangle(panel.X + 1, y + 1, PANEL_WIDTH - 2, HEADER_HEIGHT - 1), DesignerStyle.HeaderBackground);
            FillRectangle(new Rectangle(panel.X + 1, y + HEADER_HEIGHT, PANEL_WIDTH - 2, 1), DesignerStyle.Border);
            FillRectangle(new Rectangle(panel.X + 1, y + 1, 3, HEADER_HEIGHT - 1), DesignerStyle.Accent);
            DrawString("Designer", FONT, new Vector2(x, y + 8), Color.White);

            int pending = tools.PendingEdits.Count;
            string summary = pending > 0 ? $"{pending} unsaved" : selection.Count > 1 ? $"{selection.Count} selected" : string.Empty;
            if (summary.Length > 0)
            {
                int titleWidth = (int)Renderer.GetTextDimensions("Designer", FONT).X;
                DrawString(summary, FONT, new Vector2(x + titleWidth + 12, y + 8), pending > 0 ? DesignerStyle.PendingKey : DesignerStyle.DimText);
            }
        }

        private void DrawPanelText()
        {
            if (panelCollapsed)
                return;

            int x = panel.X + MARGIN;
            int width = PANEL_WIDTH - MARGIN * 2;
            int pending = tools.PendingEdits.Count;

            int y = panel.Y + infoTop;
            DrawInfo(x, y, width);

            // List caption with the legend of the marker colors
            y = panel.Y + listCaptionY;
            bool legend = !reviewing && !showingHelp && (mode == PanelMode.Inspector || (mode == PanelMode.Files && fileLevel == 2));
            DrawPanelLine(listCaption, x, y, legend ? width / 2 : width, DesignerStyle.DimText);
            if (legend)
                DrawLegend(x + width, y);

            y = panel.Y + lbKeys.Bottom + 3;
            if (WindowManager.SelectedControl == tbEdit && editHintLine.Length > 0)
                DrawPanelLine(editHintLine, x, y, width, DesignerStyle.UnsetKey);
            else
                DrawPanelLine(keySourceLine, x, y, width, DesignerStyle.DimText);

            if (tbEdit.Visible && string.IsNullOrEmpty(tbEdit.Text) && WindowManager.SelectedControl != tbEdit)
                DrawPanelLine(GetEditPlaceholder(), x + 4, panel.Y + tbEdit.Y + 4, width - 8, DesignerStyle.FaintText);

            y = panel.Y + btnSave.Bottom + 6;
            DrawPanelLine(pendingLine + (pending > 0 && !reviewing ? ", Ctrl+S to review" : string.Empty), x, y, width,
                pending > 0 ? DesignerStyle.PendingKey : DesignerStyle.DimText);

            y = panel.Y + statusTop;
            FillRectangle(new Rectangle(panel.X + MARGIN - 6, y, 2, LINE_HEIGHT * STATUS_LINES - 2), statusColor);
            foreach (string line in WrapText(status, width, STATUS_LINES))
            {
                DrawPanelLine(line, x, y, width, statusColor);
                y += LINE_HEIGHT;
            }
        }

        private void DrawInfo(int x, int y, int width)
        {
            breadcrumbs.Clear();

            if (breadcrumbParts.Count > 0)
            {
                // Window > panel > control; the parts before the selected control can be clicked
                int crumbX = x;
                for (int i = 0; i < breadcrumbParts.Count; i++)
                {
                    var (text, control) = breadcrumbParts[i];
                    bool last = i == breadcrumbParts.Count - 1;
                    int textWidth = (int)Renderer.GetTextDimensions(text, FONT).X;
                    if (crumbX + textWidth > x + width)
                        break;

                    var area = new Rectangle(crumbX - panel.X, y - panel.Y, textWidth, LINE_HEIGHT);
                    bool hover = !last && area.Contains(new Point(WindowManager.Cursor.Location.X - panel.X, WindowManager.Cursor.Location.Y - panel.Y));
                    DrawString(text, FONT, new Vector2(crumbX, y), last ? DesignerStyle.Text : hover ? DesignerStyle.AccentHover : DesignerStyle.DimText);

                    if (!last)
                        breadcrumbs.Add((area, control));

                    crumbX += textWidth;
                    if (!last)
                    {
                        DrawString(" > ", FONT, new Vector2(crumbX, y), DesignerStyle.FaintText);
                        crumbX += (int)Renderer.GetTextDimensions(" > ", FONT).X;
                    }
                }
            }
            else
            {
                DrawPanelLine(infoLines[0], x, y, width, DesignerStyle.Title);
            }

            y += LINE_HEIGHT;
            DrawPanelLine(infoLines[1], x, y, width, breadcrumbParts.Count > 0 ? DesignerStyle.Title : DesignerStyle.Text);
            if (infoType.Length > 0)
            {
                int nameWidth = (int)Renderer.GetTextDimensions(infoLines[1], FONT).X;
                DrawPanelLine(infoType, x + nameWidth + 8, y, width - nameWidth - 8, DesignerStyle.DimText);
            }

            for (int i = 2; i < INFO_LINES; i++)
            {
                y += LINE_HEIGHT;
                DrawPanelLine(infoLines[i], x, y, width, DesignerStyle.DimText);
            }
        }

        private void DrawLegend(int right, int y)
        {
            (string Text, Color Color)[] entries =
            {
                ("theme", DesignerStyle.ThemeKey),
                ("inherited", DesignerStyle.InheritedKey),
                ("unsaved", DesignerStyle.PendingKey),
            };

            int x = right;
            for (int i = entries.Length - 1; i >= 0; i--)
            {
                int textWidth = (int)Renderer.GetTextDimensions(entries[i].Text, FONT).X;
                x -= textWidth;
                DrawString(entries[i].Text, FONT, new Vector2(x, y), DesignerStyle.DimText);
                x -= 10;
                FillRectangle(new Rectangle(x, y + 4, 6, 8), entries[i].Color);
                x -= 12;
            }
        }

        private string GetEditPlaceholder()
        {
            switch (mode)
            {
                case PanelMode.Controls:
                    return "Type to find a control by name";
                case PanelMode.AddControl:
                    return "Name of the new control, Enter adds it";
                case PanelMode.Files:
                    return fileLevel == 2 ? "Key=Value, Enter applies it" : string.Empty;
                default:
                    return selected == null ? string.Empty : "Key=Value, Enter applies it; type a key for suggestions";
            }
        }

        private void DrawPanelLine(string text, int x, int y, int width, Color color)
        {
            if (string.IsNullOrEmpty(text) || width <= 0)
                return;

            DrawString(Renderer.GetStringWithLimitedWidth(text, FONT, width), FONT, new Vector2(x, y), color);
        }

        private static IEnumerable<string> WrapText(string text, int width, int maxLines)
        {
            if (string.IsNullOrEmpty(text))
                yield break;

            string line = string.Empty;
            int lines = 0;

            foreach (string word in text.Split(' '))
            {
                string candidate = line.Length == 0 ? word : line + " " + word;
                if (Renderer.GetTextDimensions(candidate, FONT).X <= width || line.Length == 0)
                {
                    line = candidate;
                    continue;
                }

                if (++lines == maxLines)
                {
                    yield return line + " ...";
                    yield break;
                }

                yield return line;
                line = word;
            }

            yield return line;
        }

        #endregion
    }
}
