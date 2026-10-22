using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using ClientCore;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Microsoft.Xna.Framework.Input;
using Rampastring.Tools;
using Rampastring.XNAUI;
using Rampastring.XNAUI.XNAControls;

namespace DTAClient.DXGUI.Designer
{
    /// <summary>
    /// The designer's inspector and editor, drawn over the whole client while open (F6).
    /// Hovering outlines a control; clicking selects it and lists its INI keys with the file each
    /// comes from. Ctrl+click adds controls to the selection. Selected controls can be dragged, resized
    /// (a group is scaled) from the corner handle, nudged with the arrow keys, or given new key values;
    /// while typing, the keys the control can read are suggested. Changes are previewed live, controls
    /// with unsaved changes are marked, and changes are saved after review (Ctrl+S).
    /// Dragging snaps to the edges and centers of neighboring controls (Alt turns it off), and an axis
    /// only changes once the drag moved more than a few pixels along it. The Files tab edits theme-wide
    /// files such as DTACnCNetClient.ini and GenericWindow.ini; Add control creates new controls.
    /// </summary>
    internal sealed class DesignerOverlay : XNAControl
    {
        private const int PANEL_WIDTH = 440;
        private const int MARGIN = 10;
        private const int LINE_HEIGHT = 18;
        private const int INFO_LINES = 6;
        private const int HELP_LINES = 3;
        private const int HANDLE_SIZE = 9;
        private const int DRAG_THRESHOLD = 3;
        private const int FONT = 0;
        private const int SUGGESTION_LINES = 10;
        private const int TAB_HEIGHT = 22;
        private const int AXIS_TOLERANCE = 4;
        private const int SNAP_DISTANCE = 6;

        private static readonly Color HoverColor = new Color(80, 220, 255);
        private static readonly Color SelectionColor = new Color(255, 214, 64);
        private static readonly Color SecondarySelectionColor = new Color(255, 150, 60);
        private static readonly Color WindowColor = new Color(220, 110, 255);
        private static readonly Color TextColor = new Color(230, 230, 230);
        private static readonly Color DimTextColor = new Color(150, 156, 162);
        private static readonly Color PendingColor = new Color(255, 214, 64);
        private static readonly Color ThemeKeyColor = new Color(150, 230, 150);
        private static readonly Color WarningColor = new Color(255, 140, 90);
        private static readonly Color UnsetKeyColor = new Color(170, 196, 220);
        private static readonly Color GuideColor = new Color(255, 80, 200);

        private enum PanelMode { Inspector, Files, AddControl }

        // Theme-wide files the Files tab offers, looked up in the theme folder first
        private static readonly (string File, string Description)[] GlobalFiles =
        {
            ("DTACnCNetClient.ini", "theme colors and settings, reloads live"),
            ("GlobalThemeSettings.ini", "colors and constants themes share, reloads live"),
            ("GenericWindow.ini", "windows without an INI of their own, reloads live"),
            ("Fonts.ini", "fonts, needs a restart"),
            ("ClientDefinitions.ini", "client settings, most need a restart"),
        };

        private sealed class NewControlType
        {
            public NewControlType(string typeName, string prefix, string description, params string[] keys)
            {
                TypeName = typeName;
                Prefix = prefix;
                Description = description;
                Keys = keys;
            }

            public string TypeName { get; }
            public string Prefix { get; }
            public string Description { get; }

            /// <summary>
            /// Keys the new control starts with, as Key=Value. Size=$BUTTON is a button size the theme has an image for,
            /// with its images: a button in ExtraControls is initialized before its size is read, so it can't find them itself.
            /// </summary>
            public string[] Keys { get; }
        }

        // Client buttons use the {width}pxbtn.png images, so pick a width the theme has images for
        private static IEnumerable<string> GetButtonKeys()
        {
            int width = new[] { 133, 160, 121, 110, 92, 75 }.FirstOrDefault(w => AssetLoader.AssetExists(w + "pxbtn.png"));
            if (width == 0)
                return new[] { "Size=133,23" };

            return new[] { $"Size={width},23", $"IdleTexture={width}pxbtn.png", $"HoverTexture={width}pxbtn_c.png" };
        }

        private static readonly NewControlType[] NewControlTypes =
        {
            new NewControlType("XNALabel", "lbl", "text", "Text=New label"),
            new NewControlType("XNAClientButton", "btn", "button", "Size=$BUTTON", "Text=New button"),
            new NewControlType("XNALinkButton", "btn", "button that opens a web page", "Size=$BUTTON", "Text=Link", "URL=https://cncnet.org"),
            new NewControlType("XNAClientLinkLabel", "lnk", "text that opens a web page", "Text=Link", "URL=https://cncnet.org"),
            new NewControlType("XNAPanel", "pnl", "panel", "Size=160,100", "SolidColorBackgroundTexture=0,0,0,160", "DrawBorders=true"),
            new NewControlType("XNAExtraPanel", "img", "image or decoration, ignores the mouse", "Size=64,64", "SolidColorBackgroundTexture=255,255,255,96"),
            new NewControlType("XNAClientCheckBox", "chk", "check box", "Text=New check box"),
            new NewControlType("XNAClientDropDown", "dd", "drop-down", "Size=140,21"),
            new NewControlType("XNASuggestionTextBox", "tb", "text box", "Size=160,21", "Suggestion=Type here"),
            new NewControlType("XNAListBox", "lb", "list", "Size=200,120", "SolidColorBackgroundTexture=0,0,0,160"),
        };

        private enum DragMode { None, Move, Resize }

        private readonly DesignerTools tools;

        private XNAPanel panel;
        private XNAListBox lbKeys;
        private XNAListBox lbSuggestions;
        private DesignerEditTextBox tbEdit;
        private XNAButton btnSave;
        private XNAButton btnUndo;
        private XNAButton btnDiscard;
        private XNAButton btnReload;
        private XNAButton btnWrite;
        private XNAButton btnBack;
        private XNAButton btnTabInspector;
        private XNAButton btnTabFiles;
        private XNAButton btnTabAdd;
        private DesignerColorPicker colorPicker;
        private Texture2D tabTexture;
        private Texture2D activeTabTexture;

        private PanelMode mode = PanelMode.Inspector;

        // Files tab: 0 lists the files, 1 a file's sections, 2 a section's keys
        private int fileLevel;
        private string filePath;
        private string fileSection;
        private CCIniFile fileIni;

        private XNAControl addContainer;
        private string suggestedControlName;
        private string pickerText;

        private XNAControl hovered;

        // The selected control whose keys the panel shows: the last one in the selection
        private XNAControl selected;
        private readonly List<XNAControl> selection = new List<XNAControl>();
        private bool dockLeft;
        private bool reviewing;

        private DragMode drag = DragMode.None;
        private Point dragStartCursor;
        private bool dragMoved;
        private bool dragAxisX;
        private bool dragAxisY;
        private readonly List<int> guideXs = new List<int>();
        private readonly List<int> guideYs = new List<int>();
        private Rectangle dragStartBounds;
        private readonly Dictionary<XNAControl, Rectangle> dragStartRectangles = new Dictionary<XNAControl, Rectangle>();
        private readonly Dictionary<XNAControl, Rectangle> dragStartScreenRectangles = new Dictionary<XNAControl, Rectangle>();

        // Pressing on a control of a multiple selection drags them all; releasing without dragging selects only it
        private XNAControl clickedSelectedControl;

        private readonly string[] infoLines = new string[INFO_LINES];
        private string keySourceLine = string.Empty;
        private string editHintLine = string.Empty;
        private string editHintKey;
        private string suggestionState;
        private bool suggestionsDismissed;
        private string pendingLine = string.Empty;
        private string status = "F5 reload, F6 close. Click a control to inspect it.";
        private Color statusColor = DimTextColor;

        public DesignerOverlay(WindowManager windowManager, DesignerTools tools) : base(windowManager)
        {
            this.tools = tools;
        }

        public bool IsOpen => Enabled;

        public override void Initialize()
        {
            Name = nameof(DesignerOverlay);
            UpdateOrder = int.MaxValue;
            DrawOrder = int.MaxValue;
            ClientRectangle = new Rectangle(0, 0, WindowManager.RenderResolutionX, WindowManager.RenderResolutionY);

            panel = new XNAPanel(WindowManager);
            panel.Name = "designerPanel";
            panel.BackgroundTexture = AssetLoader.CreateTexture(new Color(12, 14, 16, 238), 1, 1);
            panel.PanelBackgroundDrawMode = PanelBackgroundImageDrawMode.STRETCHED;
            panel.DrawBorders = true;
            panel.BorderColor = new Color(90, 96, 104);

            int listY = MARGIN + TAB_HEIGHT + 6 + LINE_HEIGHT * (INFO_LINES + 1) + 6;

            lbKeys = new XNAListBox(WindowManager);
            lbKeys.Name = "designerKeys";
            lbKeys.FontIndex = FONT;
            lbKeys.LineHeight = 16;
            lbKeys.BackgroundTexture = AssetLoader.CreateTexture(new Color(4, 5, 6, 255), 1, 1);
            lbKeys.PanelBackgroundDrawMode = PanelBackgroundImageDrawMode.STRETCHED;
            lbKeys.SelectedIndexChanged += LbKeys_SelectedIndexChanged;
            lbKeys.LeftClick += LbKeys_LeftClick;
            lbKeys.ClientRectangle = new Rectangle(MARGIN, listY, PANEL_WIDTH - MARGIN * 2, 100);

            tbEdit = new DesignerEditTextBox(WindowManager);
            tbEdit.Name = "designerEdit";
            tbEdit.FontIndex = FONT;
            tbEdit.MaximumTextLength = 1000;
            tbEdit.EnterPressed += TbEdit_EnterPressed;
            tbEdit.InputReceived += (s, e) => suggestionsDismissed = false;
            tbEdit.PreviewKeyPress = TbEdit_PreviewKeyPress;
            tbEdit.ClientRectangle = new Rectangle(MARGIN, 0, PANEL_WIDTH - MARGIN * 2, 22);

            lbSuggestions = new XNAListBox(WindowManager);
            lbSuggestions.Name = "designerSuggestions";
            lbSuggestions.FontIndex = FONT;
            lbSuggestions.LineHeight = 16;
            lbSuggestions.AllowMultiLineItems = false;
            lbSuggestions.BackgroundTexture = AssetLoader.CreateTexture(new Color(22, 26, 32, 255), 1, 1);
            lbSuggestions.PanelBackgroundDrawMode = PanelBackgroundImageDrawMode.STRETCHED;
            lbSuggestions.BorderColor = HoverColor;
            lbSuggestions.LeftClick += (s, e) => AcceptSuggestion();
            lbSuggestions.ClientRectangle = new Rectangle(MARGIN, 0, PANEL_WIDTH - MARGIN * 2, SUGGESTION_LINES * 16 + 4);

            btnSave = CreateButton("Review && save", (s, e) => StartReview());
            btnUndo = CreateButton("Undo", (s, e) => SetStatus(tools.Undo()));
            btnDiscard = CreateButton("Discard", (s, e) => SetStatus(tools.Discard()));
            btnReload = CreateButton("Reload (F5)", (s, e) => SetStatus(tools.ReloadAll("Reloaded")));
            btnWrite = CreateButton("Write files", (s, e) => SaveReviewed());
            btnBack = CreateButton("Back", (s, e) => EndReview());

            tabTexture = AssetLoader.CreateTexture(new Color(30, 34, 40), 1, 1);
            activeTabTexture = AssetLoader.CreateTexture(new Color(70, 90, 120), 1, 1);
            btnTabInspector = CreateButton("Inspector", (s, e) => SetMode(PanelMode.Inspector));
            btnTabFiles = CreateButton("Files", (s, e) => SetMode(PanelMode.Files));
            btnTabAdd = CreateButton("Add control", (s, e) => SetMode(PanelMode.AddControl));

            colorPicker = new DesignerColorPicker(WindowManager);
            colorPicker.Name = "designerColorPicker";
            colorPicker.ColorChanged += ColorPicker_ColorChanged;
            colorPicker.ColorCommitted += (s, value) => ApplyEditText();

            panel.AddChild(lbKeys);
            panel.AddChild(tbEdit);
            panel.AddChild(btnSave);
            panel.AddChild(btnUndo);
            panel.AddChild(btnDiscard);
            panel.AddChild(btnReload);
            panel.AddChild(btnWrite);
            panel.AddChild(btnBack);
            panel.AddChild(btnTabInspector);
            panel.AddChild(btnTabFiles);
            panel.AddChild(btnTabAdd);
            panel.AddChild(colorPicker);
            panel.AddChild(lbSuggestions);
            AddChild(panel);

            base.Initialize();

            LayoutPanel();
            Disable();
        }

        private XNAButton CreateButton(string text, EventHandler<InputEventArgs> leftClick)
        {
            var button = new XNAButton(WindowManager);
            button.Name = "designerButton";
            button.IdleTexture = AssetLoader.CreateTexture(new Color(46, 50, 56), 1, 1);
            button.HoverTexture = AssetLoader.CreateTexture(new Color(74, 80, 90), 1, 1);
            button.FontIndex = FONT;
            button.TextColorIdle = TextColor;
            button.TextColorHover = Color.White;
            button.Text = text.Replace("&&", "&");
            button.ClientRectangle = new Rectangle(0, 0, (PANEL_WIDTH - MARGIN * 2 - 18) / 4, 24);
            button.LeftClick += leftClick;
            return button;
        }

        private void LayoutPanel()
        {
            ClientRectangle = new Rectangle(0, 0, WindowManager.RenderResolutionX, WindowManager.RenderResolutionY);
            panel.ClientRectangle = new Rectangle(dockLeft ? 0 : Width - PANEL_WIDTH, 0, PANEL_WIDTH, Height);

            int bottomBlock = LINE_HEIGHT * (4 + HELP_LINES) + 24 + 22 + MARGIN * 3;
            lbKeys.ClientRectangle = new Rectangle(lbKeys.X, lbKeys.Y, lbKeys.Width, Math.Max(60, Height - lbKeys.Y - bottomBlock));

            int y = lbKeys.Bottom + LINE_HEIGHT + 4;
            tbEdit.ClientRectangle = new Rectangle(MARGIN, y, tbEdit.Width, tbEdit.Height);

            // The suggestions and the color picker cover the bottom of the key list, leaving the hint line free
            lbSuggestions.ClientRectangle = new Rectangle(MARGIN, lbKeys.Bottom - lbSuggestions.Height, lbSuggestions.Width, lbSuggestions.Height);
            colorPicker.ClientRectangle = new Rectangle(MARGIN, lbKeys.Bottom - DesignerColorPicker.PreferredHeight, lbKeys.Width,
                DesignerColorPicker.PreferredHeight);

            int tabWidth = (PANEL_WIDTH - MARGIN * 2 - 12) / 3;
            XNAButton[] tabs = { btnTabInspector, btnTabFiles, btnTabAdd };
            for (int i = 0; i < tabs.Length; i++)
                tabs[i].ClientRectangle = new Rectangle(MARGIN + i * (tabWidth + 6), MARGIN, tabWidth, TAB_HEIGHT);

            y = tbEdit.Bottom + 6;
            XNAButton[] row = { btnSave, btnUndo, btnDiscard, btnReload };
            for (int i = 0; i < row.Length; i++)
                row[i].ClientRectangle = new Rectangle(MARGIN + i * (row[i].Width + 6), y, row[i].Width, row[i].Height);

            btnWrite.ClientRectangle = btnSave.ClientRectangle;
            btnBack.ClientRectangle = btnUndo.ClientRectangle;
        }

        public void Toggle()
        {
            if (IsOpen)
            {
                drag = DragMode.None;
                reviewing = false;
                hovered = null;
                Disable();
                return;
            }

            // Keep typing and arrow keys from going to whatever text box was focused underneath
            WindowManager.SelectedControl = null;
            Enable();
            LayoutPanel();
            RefreshPanel();
        }

        public void SetStatus(string text) => SetStatus(text, DimTextColor);

        private void SetStatus(string text, Color color)
        {
            status = text;
            statusColor = color;
            RefreshPanel();
        }

        /// <summary>
        /// Handles a key press while the overlay is open.
        /// </summary>
        /// <returns>True if the key was used.</returns>
        public bool HandleKey(Keys key)
        {
            if (WindowManager.SelectedControl == tbEdit && key != Keys.Escape)
                return false;

            bool ctrl = WindowManager.Keyboard.IsCtrlHeldDown();
            int step = WindowManager.Keyboard.IsShiftHeldDown() ? 10 : 1;

            switch (key)
            {
                case Keys.Escape:
                    if (WindowManager.SelectedControl == tbEdit)
                    {
                        WindowManager.SelectedControl = null;
                    }
                    else if (reviewing)
                    {
                        EndReview();
                    }
                    else if (mode == PanelMode.Files && fileLevel > 0)
                    {
                        fileLevel--;
                        tbEdit.Text = string.Empty;
                        RefreshPanel();
                    }
                    else if (mode != PanelMode.Inspector)
                    {
                        SetMode(PanelMode.Inspector);
                    }
                    else
                    {
                        Select(null);
                    }

                    return true;
                case Keys.Left:
                    Nudge(-step, 0, ctrl);
                    return true;
                case Keys.Right:
                    Nudge(step, 0, ctrl);
                    return true;
                case Keys.Up:
                    Nudge(0, -step, ctrl);
                    return true;
                case Keys.Down:
                    Nudge(0, step, ctrl);
                    return true;
                case Keys.PageUp:
                    if (selected?.Parent != null)
                        Select(selected.Parent);
                    return true;
                case Keys.PageDown:
                    SelectChildren();
                    return true;
                case Keys.F7:
                    dockLeft = !dockLeft;
                    LayoutPanel();
                    return true;
                case Keys.S when ctrl:
                    StartReview();
                    return true;
                case Keys.Z when ctrl:
                    SetStatus(tools.Undo());
                    return true;
            }

            return false;
        }

        public override void Update(GameTime gameTime)
        {
            if (Width != WindowManager.RenderResolutionX || Height != WindowManager.RenderResolutionY)
                LayoutPanel();

            base.Update(gameTime);

            if (selection.RemoveAll(c => !IsAttached(c)) > 0)
                SelectionChanged();

            UpdateSuggestions();
            UpdateColorPicker();

            Point cursorPoint = WindowManager.Cursor.Location;
            var cursor = WindowManager.Cursor;

            if (drag != DragMode.None)
            {
                if (cursor.LeftDown)
                    UpdateDrag(cursorPoint);
                else
                    EndDrag(cursorPoint);

                return;
            }

            bool overPanel = panel.GetWindowRectangle().Contains(cursorPoint);
            hovered = overPanel ? null : FindControlAt(cursorPoint);

            if (overPanel || !cursor.LeftPressedDown)
                return;

            WindowManager.SelectedControl = null;
            bool ctrl = WindowManager.Keyboard.IsCtrlHeldDown();

            if (selection.Count > 0 && GetHandleRectangle().Contains(cursorPoint))
            {
                StartDrag(DragMode.Resize, cursorPoint);
            }
            else if (hovered != null && ctrl)
            {
                ToggleSelection(hovered);
            }
            else if (hovered != null)
            {
                if (selection.Contains(hovered))
                    clickedSelectedControl = hovered;
                else
                    Select(hovered);

                StartDrag(DragMode.Move, cursorPoint);
            }
            else if (!ctrl)
            {
                Select(null);
            }
        }

        private XNAControl FindControlAt(Point point)
        {
            IReadOnlyList<XNAControl> controls = WindowManager.TopLevelControls;

            for (int i = controls.Count - 1; i >= 0; i--)
            {
                XNAControl control = controls[i];
                if (control == this || !control.Visible)
                    continue;

                if (control.GetWindowRectangle().Contains(point))
                    return FindDeepestControlAt(control, point);
            }

            return null;
        }

        private static XNAControl FindDeepestControlAt(XNAControl control, Point point)
        {
            // Children are drawn in DrawOrder, so the last one containing the point is on top
            List<XNAControl> children = control.Children.Where(c => c.Visible).OrderBy(c => c.DrawOrder).ToList();

            for (int i = children.Count - 1; i >= 0; i--)
            {
                if (children[i].GetWindowRectangle().Contains(point))
                    return FindDeepestControlAt(children[i], point);
            }

            return control;
        }

        private void Select(XNAControl control)
        {
            selection.Clear();
            if (control != null)
                selection.Add(control);

            SelectionChanged();
        }

        private void ToggleSelection(XNAControl control)
        {
            if (!selection.Remove(control))
                selection.Add(control);

            SelectionChanged();
        }

        private void SelectChildren()
        {
            if (selected == null)
                return;

            List<XNAControl> children = selected.Children.Where(c => c.Visible && !string.IsNullOrEmpty(c.Name)).ToList();
            if (children.Count == 0)
            {
                SetStatus($"{Describe(selected)} has no visible named controls inside it.", WarningColor);
                return;
            }

            string parentName = selected.Name;
            selection.Clear();
            selection.AddRange(children);
            SelectionChanged();
            SetStatus($"Selected the {children.Count} controls inside {parentName}. Drag the corner handle to scale them together.");
        }

        private void SelectionChanged()
        {
            selected = selection.LastOrDefault();

            if (reviewing)
                EndReview();
            else
                RefreshPanel();
        }

        private Rectangle GetSelectionBounds()
        {
            Rectangle bounds = selection[0].GetWindowRectangle();
            foreach (XNAControl control in selection.Skip(1))
                bounds = Rectangle.Union(bounds, control.GetWindowRectangle());

            return bounds;
        }

        private Rectangle GetHandleRectangle()
        {
            Rectangle bounds = GetSelectionBounds();
            return new Rectangle(bounds.Right - HANDLE_SIZE / 2 - 1, bounds.Bottom - HANDLE_SIZE / 2 - 1, HANDLE_SIZE, HANDLE_SIZE);
        }

        private static int GetDepth(XNAControl control)
        {
            int depth = 0;
            for (XNAControl parent = control.Parent; parent != null; parent = parent.Parent)
                depth++;

            return depth;
        }

        // Selected controls inside another selected control move along with it
        private List<XNAControl> GetOutermostSelected() =>
            selection.Where(c => !selection.Any(other => other != c && other.IsParentOf(c))).ToList();

        private void StartDrag(DragMode mode, Point cursorPoint)
        {
            drag = mode;
            dragStartCursor = cursorPoint;
            dragMoved = false;
            dragAxisX = false;
            dragAxisY = false;
            guideXs.Clear();
            guideYs.Clear();
            dragStartBounds = GetSelectionBounds();
            dragStartRectangles.Clear();
            dragStartScreenRectangles.Clear();

            foreach (XNAControl control in selection)
            {
                dragStartRectangles[control] = control.ClientRectangle;
                dragStartScreenRectangles[control] = control.GetWindowRectangle();
            }
        }

        private void UpdateDrag(Point cursorPoint)
        {
            int dx = cursorPoint.X - dragStartCursor.X;
            int dy = cursorPoint.Y - dragStartCursor.Y;

            if (!dragMoved && Math.Abs(dx) < DRAG_THRESHOLD && Math.Abs(dy) < DRAG_THRESHOLD)
                return;

            dragMoved = true;

            // An axis only changes once the drag went past the tolerance along it,
            // so dragging along one axis doesn't nudge the other
            dragAxisX |= Math.Abs(dx) >= AXIS_TOLERANCE;
            dragAxisY |= Math.Abs(dy) >= AXIS_TOLERANCE;
            dx = dragAxisX ? dx : 0;
            dy = dragAxisY ? dy : 0;

            Snap(ref dx, ref dy, drag == DragMode.Resize);

            if (drag == DragMode.Move)
            {
                foreach (XNAControl control in GetOutermostSelected())
                {
                    Rectangle r = dragStartRectangles[control];
                    control.ClientRectangle = new Rectangle(r.X + dx, r.Y + dy, r.Width, r.Height);
                }

                return;
            }

            // A single control is resized; a group is scaled from its top left corner, positions included
            Rectangle bounds = dragStartBounds;
            float scaleX = Math.Max(4, bounds.Width + dx) / (float)Math.Max(1, bounds.Width);
            float scaleY = Math.Max(4, bounds.Height + dy) / (float)Math.Max(1, bounds.Height);
            bool single = selection.Count == 1;

            // Parents first, so children are placed relative to their parent's new position
            foreach (XNAControl control in selection.OrderBy(GetDepth))
            {
                Rectangle a = dragStartScreenRectangles[control];
                Rectangle target = single
                    ? new Rectangle(a.X, a.Y, Math.Max(4, a.Width + dx), Math.Max(4, a.Height + dy))
                    : new Rectangle(bounds.X + (int)Math.Round((a.X - bounds.X) * scaleX), bounds.Y + (int)Math.Round((a.Y - bounds.Y) * scaleY),
                        Math.Max(1, (int)Math.Round(a.Width * scaleX)), Math.Max(1, (int)Math.Round(a.Height * scaleY)));

                Point parentOrigin = control.Parent?.GetWindowRectangle().Location ?? Point.Zero;
                control.ClientRectangle = new Rectangle(target.X - parentOrigin.X, target.Y - parentOrigin.Y, target.Width, target.Height);
            }
        }

        /// <summary>
        /// Adjusts a drag so the edges or center of the selection line up with those of neighboring
        /// controls and the parent, when they are within a few pixels. Alt turns snapping off.
        /// </summary>
        private void Snap(ref int dx, ref int dy, bool resizing)
        {
            guideXs.Clear();
            guideYs.Clear();

            XNAControl parent = selected?.Parent;
            if (parent == null || WindowManager.Keyboard.IsAltHeldDown())
                return;

            Rectangle parentRectangle = parent.GetWindowRectangle();
            var lineXs = new List<int> { parentRectangle.X, parentRectangle.Right, parentRectangle.Center.X };
            var lineYs = new List<int> { parentRectangle.Y, parentRectangle.Bottom, parentRectangle.Center.Y };

            foreach (XNAControl sibling in parent.Children)
            {
                if (!sibling.Visible || selection.Contains(sibling) || selection.Any(c => c.IsParentOf(sibling) || sibling.IsParentOf(c)))
                    continue;

                Rectangle r = sibling.GetWindowRectangle();
                if (r.Width <= 0 || r.Height <= 0)
                    continue;

                lineXs.AddRange(new[] { r.X, r.Right, r.Center.X });
                lineYs.AddRange(new[] { r.Y, r.Bottom, r.Center.Y });
            }

            Rectangle b = dragStartBounds;
            int[] edgeXs = resizing ? new[] { b.Right + dx } : new[] { b.X + dx, b.Right + dx, b.Center.X + dx };
            int[] edgeYs = resizing ? new[] { b.Bottom + dy } : new[] { b.Y + dy, b.Bottom + dy, b.Center.Y + dy };

            if (dragAxisX)
                dx += FindSnap(edgeXs, lineXs, guideXs);

            if (dragAxisY)
                dy += FindSnap(edgeYs, lineYs, guideYs);
        }

        // Returns the smallest offset that puts one of the edges on one of the lines, or 0 if none is close
        private static int FindSnap(int[] edges, List<int> lines, List<int> guides)
        {
            int bestOffset = 0;
            int bestDistance = SNAP_DISTANCE + 1;
            int guide = 0;

            foreach (int edge in edges)
            {
                foreach (int line in lines)
                {
                    int offset = line - edge;
                    if (Math.Abs(offset) < bestDistance)
                    {
                        bestDistance = Math.Abs(offset);
                        bestOffset = offset;
                        guide = line;
                    }
                }
            }

            if (bestDistance > SNAP_DISTANCE)
                return 0;

            guides.Add(guide);
            return bestOffset;
        }

        private void EndDrag(Point cursorPoint)
        {
            drag = DragMode.None;
            guideXs.Clear();
            guideYs.Clear();
            XNAControl clicked = clickedSelectedControl;
            clickedSelectedControl = null;

            if (!dragMoved)
            {
                if (clicked != null)
                    Select(clicked);

                return;
            }

            var after = selection.ToDictionary(c => c, c => c.ClientRectangle);
            CommitGeometry(new Dictionary<XNAControl, Rectangle>(dragStartRectangles), after);
        }

        private void Nudge(int dx, int dy, bool resize)
        {
            if (selection.Count == 0)
                return;

            var before = selection.ToDictionary(c => c, c => c.ClientRectangle);
            var after = new Dictionary<XNAControl, Rectangle>(before);

            if (resize)
            {
                foreach (XNAControl control in selection)
                {
                    Rectangle r = before[control];
                    after[control] = new Rectangle(r.X, r.Y, Math.Max(1, r.Width + dx), Math.Max(1, r.Height + dy));
                }
            }
            else
            {
                foreach (XNAControl control in GetOutermostSelected())
                {
                    Rectangle r = before[control];
                    after[control] = new Rectangle(r.X + dx, r.Y + dy, r.Width, r.Height);
                }
            }

            CommitGeometry(before, after);
        }

        /// <summary>
        /// Turns new positions and sizes of the selected controls into INI edits, previews them as one
        /// undo step and reports controls that ended up elsewhere or can't be configured from an INI file.
        /// </summary>
        private void CommitGeometry(Dictionary<XNAControl, Rectangle> before, Dictionary<XNAControl, Rectangle> after)
        {
            // Let the INI decide where the controls end up
            foreach (var entry in before)
                entry.Key.ClientRectangle = entry.Value;

            var edits = new List<ControlEdit>();
            var notConfigurable = new List<string>();

            // Parents first, then in the order the INI creates and reads controls, so controls positioned
            // relative to an earlier one are planned from where that one moved them
            List<XNAControl> ordered = selection.OrderBy(GetDepth).ThenBy(c => c.Parent?.Children.IndexOf(c) ?? 0).ToList();

            string error = tools.ApplyEditsInOrder(ordered, control =>
            {
                bool parentResized = control.Parent != null && before.TryGetValue(control.Parent, out Rectangle parentBefore) &&
                    (after[control.Parent].Width != parentBefore.Width || after[control.Parent].Height != parentBefore.Height);

                Rectangle current = control.ClientRectangle;
                if (current == after[control] && !parentResized)
                    return null;

                IniWindow window = IniWindow.OwnerOf(control);
                string section = window?.SectionFor(control);
                if (section == null)
                {
                    notConfigurable.Add(Describe(control));
                    return null;
                }

                Point? parentSize = control.Parent == null ? null : new Point(control.Parent.Width, control.Parent.Height);
                List<KeyValuePair<string, string>> changes = GeometryEdits.Apply(GetOrderedKeys(window, section), current, after[control],
                    parentSize, parentResized);

                return changes.Count == 0 ? null : new ControlEdit(window, control, section, changes);
            }, DependsOn, edits);

            if (error != null)
            {
                SetStatus(error, WarningColor);
                return;
            }

            // Scaling rounds to whole pixels, so allow a pixel either way
            List<string> misplaced = selection.Where(c => !IsWithinPixel(c.ClientRectangle, after[c]) && !notConfigurable.Contains(Describe(c)))
                .Select(c => c.Name).ToList();
            string message = edits.Count == 0 ? "Nothing changed." :
                edits.Count == 1 ? $"Changed {string.Join(", ", edits[0].Keys.Select(k => k.Key + "=" + k.Value))} in [{edits[0].Section}]." :
                $"Changed {edits.Count} controls: {ListNames(edits.Select(e => e.Control.Name))}.";

            if (notConfigurable.Count > 0)
                message += $" Can't be configured from an INI file: {ListNames(notConfigurable)}.";

            if (misplaced.Count > 0)
                message += $" A later key or code places {ListNames(misplaced)} elsewhere.";

            SetStatus(message, notConfigurable.Count > 0 || misplaced.Count > 0 ? WarningColor : ThemeKeyColor);
        }

        /// <summary>
        /// Whether a control's position can change when the given controls change: when one of them
        /// contains it, or when its INI keys refer to one of them by name, as in getBottom(name).
        /// </summary>
        private static bool DependsOn(XNAControl control, IEnumerable<XNAControl> changed)
        {
            List<XNAControl> changedList = changed.ToList();
            if (changedList.Any(c => c.IsParentOf(control)))
                return true;

            IniWindow window = IniWindow.OwnerOf(control);
            string section = window?.SectionFor(control);
            if (section == null)
                return false;

            string values = string.Join(" ", GetKeys(window, section).Values);
            return changedList.Any(c => !string.IsNullOrEmpty(c.Name) && values.Contains("(" + c.Name + ")"));
        }

        private static bool IsWithinPixel(Rectangle a, Rectangle b) =>
            Math.Abs(a.X - b.X) <= 1 && Math.Abs(a.Y - b.Y) <= 1 && Math.Abs(a.Width - b.Width) <= 1 && Math.Abs(a.Height - b.Height) <= 1;

        private static string ListNames(IEnumerable<string> names, int max = 3)
        {
            List<string> list = names.ToList();
            return list.Count <= max ? string.Join(", ", list) : string.Join(", ", list.Take(max)) + $" and {list.Count - max} more";
        }

        private static List<KeyValuePair<string, string>> GetOrderedKeys(IniWindow window, string section) =>
            window.Ini?.GetSection(section)?.Keys.ToList() ?? new List<KeyValuePair<string, string>>();

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

        private void TbEdit_EnterPressed(object sender, EventArgs e) => ApplyEditText();

        /// <summary>
        /// Applies what the edit box holds: Key=Value for the selected control or the open file section,
        /// or the name of a control to add.
        /// </summary>
        private void ApplyEditText()
        {
            string text = tbEdit.Text ?? string.Empty;

            if (mode == PanelMode.AddControl)
            {
                AddControl(text.Trim());
                return;
            }

            int equals = text.IndexOf('=');
            if (equals < 1 || (mode == PanelMode.Inspector && selected == null) || (mode == PanelMode.Files && fileLevel != 2))
            {
                SetStatus(mode == PanelMode.Files ? "Open a section and type Key=Value." : "Select a control and type Key=Value.", WarningColor);
                return;
            }

            string key = text.Substring(0, equals).Trim();
            string value = text.Substring(equals + 1).Trim();

            if (value.Contains(";"))
            {
                SetStatus("Values can't contain ';', INI files read it as the start of a comment.", WarningColor);
                return;
            }

            if (mode == PanelMode.Files)
            {
                string fileError = tools.ApplyFileEdit(filePath, fileSection, key, value);
                if (fileError != null)
                    SetStatus(fileError, WarningColor);
                else
                    SetStatus($"Set {key}={value} in [{fileSection}] of {Path.GetFileName(filePath)}.", ThemeKeyColor);

                return;
            }

            IniWindow window = IniWindow.OwnerOf(selected);
            string section = window?.SectionFor(selected);
            if (window == null || section == null)
            {
                SetStatus($"{Describe(selected)} can't be configured from an INI file.", WarningColor);
                return;
            }

            string error = tools.ApplyEdits(window, selected, section, new[] { new KeyValuePair<string, string>(key, value) });
            if (error != null)
                SetStatus(error, WarningColor);
            else
                SetStatus($"Set {key}={value} in [{section}].", ThemeKeyColor);
        }

        private void LbKeys_LeftClick(object sender, InputEventArgs e)
        {
            if (reviewing || mode != PanelMode.Files || lbKeys.SelectedItem?.Tag is not string tag)
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

        private void LbKeys_SelectedIndexChanged(object sender, EventArgs e)
        {
            keySourceLine = string.Empty;

            if (reviewing || lbKeys.SelectedItem?.Tag is not string key)
                return;

            if (mode == PanelMode.AddControl)
            {
                // Follow the chosen type with the suggested name, unless a name was typed
                if (string.IsNullOrEmpty(tbEdit.Text) || tbEdit.Text == suggestedControlName)
                {
                    suggestedControlName = SuggestControlName(GetChosenControlType());
                    tbEdit.Text = suggestedControlName ?? string.Empty;
                }

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

            if (selected == null)
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

        private void SetEditText(string text)
        {
            tbEdit.Text = text;
            tbEdit.InputPosition = text.Length;
            WindowManager.SelectedControl = tbEdit;
        }

        /// <summary>
        /// Lists the keys the selected control can read that match what is typed before the '=',
        /// and keeps the hint line under the list describing the value the current key expects.
        /// </summary>
        private void UpdateSuggestions()
        {
            bool editing = !reviewing && mode == PanelMode.Inspector && selected != null && WindowManager.SelectedControl == tbEdit;
            string text = tbEdit.Text ?? string.Empty;
            string state = editing ? selected.GetHashCode() + "|" + text : null;

            if (state != suggestionState)
            {
                suggestionState = state;
                FillSuggestions(editing && !text.Contains("=") ? text.Trim() : null);
            }

            bool show = editing && !suggestionsDismissed && lbSuggestions.Items.Count > 0;
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
                var item = new XNAListBoxItem(isSet ? key + " = " + value : key, isSet ? TextColor : UnsetKeyColor);
                item.Tag = key;
                lbSuggestions.AddItem(item);
            }

            lbSuggestions.TopIndex = 0;
            lbSuggestions.SelectedIndex = filter.Length > 0 && lbSuggestions.Items.Count > 0 ? 0 : -1;
        }

        private bool TbEdit_PreviewKeyPress(Keys key)
        {
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

        private void SetMode(PanelMode newMode)
        {
            if (newMode == PanelMode.AddControl)
            {
                if (selected == null)
                {
                    SetStatus("Select the window or panel to add a control to first.", WarningColor);
                    return;
                }

                // Add inside a selected window or plain panel, otherwise next to the selected control
                addContainer = selected is XNAPanel && (IniWindow.From(selected) != null || selected.GetType() == typeof(XNAPanel) ||
                    selected is ClientGUI.XNAClientScrollPanel) ? selected : selected.Parent;

                if (addContainer == null || IniWindow.OwnerOf(addContainer) == null)
                {
                    SetStatus($"{Describe(selected)} isn't inside a window that reads an INI file.", WarningColor);
                    return;
                }

                suggestedControlName = null;
                tbEdit.Text = string.Empty;
            }
            else if (newMode == PanelMode.Files)
            {
                fileLevel = 0;
            }

            if (newMode != mode)
                tbEdit.Text = string.Empty;

            mode = newMode;
            reviewing = false;
            RefreshPanel();

            if (mode == PanelMode.AddControl && lbKeys.Items.Count > 0)
            {
                lbKeys.SelectedIndex = 0;
                WindowManager.SelectedControl = tbEdit;
            }
        }

        private void ColorPicker_ColorChanged(object sender, string value)
        {
            string text = tbEdit.Text ?? string.Empty;
            int equals = text.IndexOf('=');
            if (equals < 1)
                return;

            tbEdit.Text = text.Substring(0, equals + 1) + value;
            tbEdit.InputPosition = tbEdit.Text.Length;
            pickerText = tbEdit.Text;
        }

        /// <summary>
        /// Shows the color picker while the edit box holds a key that takes a color.
        /// </summary>
        private void UpdateColorPicker()
        {
            string text = tbEdit.Text ?? string.Empty;
            int equals = text.IndexOf('=');
            string key = equals > 0 ? text.Substring(0, equals).Trim() : null;

            bool canEdit = !reviewing && ((mode == PanelMode.Inspector && selected != null) || (mode == PanelMode.Files && fileLevel == 2));
            bool show = canEdit && key != null && !lbSuggestions.Visible &&
                DesignerKeyCatalog.GetValueHint(key)?.StartsWith("color", StringComparison.Ordinal) == true;

            if (show && text != pickerText)
            {
                pickerText = text;
                colorPicker.SetValue(text.Substring(equals + 1).Trim());
            }

            colorPicker.Visible = colorPicker.Enabled = show;
        }

        private static string ResolveGlobalFile(string fileName)
        {
            string themePath = SafePath.CombineFilePath(ProgramConstants.GetResourcePath(), fileName);
            if (IniOverlay.FileExists(themePath))
                return themePath;

            string basePath = SafePath.CombineFilePath(ProgramConstants.GetBaseResourcePath(), fileName);
            return File.Exists(basePath) ? basePath : null;
        }

        private void FillFileList()
        {
            fileIni = null;

            if (fileLevel == 0)
            {
                foreach (var (file, description) in GlobalFiles)
                {
                    string path = ResolveGlobalFile(file);
                    if (path == null)
                        continue;

                    var item = new XNAListBoxItem($"{DesignerTools.DisplayPath(path)}: {description}", TextColor);
                    item.Tag = path;
                    lbKeys.AddItem(item);
                }

                return;
            }

            lbKeys.AddItem(new XNAListBoxItem(".. back", DimTextColor) { Tag = ".." });
            fileIni = new CCIniFile(filePath);
            string fullPath = Path.GetFullPath(filePath);

            if (fileLevel == 1)
            {
                foreach (string section in fileIni.GetSections())
                {
                    bool inThisFile = fileIni.GetSectionKeys(section).Any(k =>
                        string.Equals(fileIni.GetKeySource(section, k)?.FilePath, fullPath, StringComparison.OrdinalIgnoreCase));

                    lbKeys.AddItem(new XNAListBoxItem("[" + section + "]", inThisFile ? ThemeKeyColor : DimTextColor) { Tag = section });
                }

                return;
            }

            IReadOnlyList<IniOverlayEdit> pending = IniOverlay.GetEdits(filePath);
            foreach (string key in fileIni.GetSectionKeys(fileSection) ?? new List<string>())
            {
                IniKeySource source = fileIni.GetKeySource(fileSection, key);
                bool isPending = pending.Any(e => e.Section == fileSection && e.Key == key);
                bool inThisFile = string.Equals(source?.FilePath, fullPath, StringComparison.OrdinalIgnoreCase);

                lbKeys.AddItem(new XNAListBoxItem((isPending ? "* " : string.Empty) + key + " = " + fileIni.GetStringValue(fileSection, key, string.Empty),
                    isPending ? PendingColor : inThisFile ? ThemeKeyColor : DimTextColor) { Tag = key });
            }
        }

        private void FillControlTypeList()
        {
            foreach (NewControlType type in NewControlTypes)
                lbKeys.AddItem(new XNAListBoxItem($"{type.TypeName}: {type.Description}", TextColor) { Tag = type.TypeName });
        }

        private NewControlType GetChosenControlType() =>
            NewControlTypes.FirstOrDefault(t => t.TypeName == lbKeys.SelectedItem?.Tag as string) ?? NewControlTypes[0];

        private string SuggestControlName(NewControlType type)
        {
            IniWindow window = IniWindow.OwnerOf(addContainer);
            if (window == null)
                return null;

            for (int i = 1; ; i++)
            {
                string name = type.Prefix + "New" + i;
                if (IsControlNameFree(window, name))
                    return name;
            }
        }

        private static bool IsControlNameFree(IniWindow window, string name) =>
            DesignerTools.FindDescendant(window.Control, name) == null && window.Ini?.SectionExists(name) != true;

        // Whether a control is created from the window's INI, so sections of its own are read on every load
        private static bool IsCreatedFromIni(IniWindow window, XNAControl control)
        {
            if (window.Ini == null)
                return false;

            foreach (string section in window.Ini.GetSections())
            {
                foreach (string key in window.Ini.GetSectionKeys(section))
                {
                    if (key.StartsWith("$CC", StringComparison.Ordinal) &&
                        window.Ini.GetStringValue(section, key, string.Empty).StartsWith(control.Name + ":", StringComparison.Ordinal))
                    {
                        return true;
                    }
                }
            }

            return false;
        }

        /// <summary>
        /// Adds a control of the chosen type with the given name to the chosen container: an entry that
        /// declares it (ExtraControls, or a $CC key in an INI-configured window) and a section of its own.
        /// </summary>
        private void AddControl(string name)
        {
            IniWindow window = IniWindow.OwnerOf(addContainer);
            NewControlType type = GetChosenControlType();

            if (window == null)
            {
                SetStatus("The control to add to is no longer part of a window.", WarningColor);
                return;
            }

            if (!System.Text.RegularExpressions.Regex.IsMatch(name, "^[A-Za-z_][A-Za-z0-9_]*$"))
            {
                SetStatus("Names can only use letters, digits and underscores, and can't start with a digit.", WarningColor);
                return;
            }

            if (!IsControlNameFree(window, name))
            {
                SetStatus($"{window.Control.Name} already has a control or section named {name}.", WarningColor);
                return;
            }

            var edits = new List<IniOverlayEdit>();

            List<string> typeKeys = type.Keys.SelectMany(k => k == "Size=$BUTTON" ? GetButtonKeys() : new[] { k }).ToList();

            // In the middle of the container, so it isn't hidden in a corner
            string sizeKey = typeKeys.FirstOrDefault(k => k.StartsWith("Size=", StringComparison.Ordinal));
            string[] sizeParts = sizeKey?.Substring("Size=".Length).Split(',');
            Point size = sizeParts != null ? new Point(int.Parse(sizeParts[0], CultureInfo.InvariantCulture), int.Parse(sizeParts[1], CultureInfo.InvariantCulture)) : new Point(100, 20);
            Point location = new Point(Math.Max(0, (addContainer.Width - size.X) / 2), Math.Max(0, (addContainer.Height - size.Y) / 2));

            if (window.Control is ClientGUI.INItializableWindow)
            {
                // A $CC entry is only read when its section is; code-created containers may never be read
                XNAControl parent = addContainer == window.Control || IsCreatedFromIni(window, addContainer) ? addContainer : window.Control;
                if (parent != addContainer)
                {
                    Rectangle container = addContainer.GetWindowRectangle();
                    Rectangle windowRectangle = window.Control.GetWindowRectangle();
                    location = new Point(container.X - windowRectangle.X + location.X, container.Y - windowRectangle.Y + location.Y);
                }

                edits.Add(new IniOverlayEdit(null, window.SectionFor(parent), "$CC" + name, name + ":" + type.TypeName));
            }
            else
            {
                string parentPart = addContainer == window.Control ? string.Empty : ":" + addContainer.Name;
                edits.Add(new IniOverlayEdit(null, "ExtraControls", name, name + ":" + type.TypeName + parentPart));

                // ExtraControls are drawn behind the window's own controls, which suits decorations but not these
                if (type.TypeName != "XNAPanel" && type.TypeName != "XNAExtraPanel")
                    edits.Add(new IniOverlayEdit(null, name, "DrawOrder", "1"));
            }

            edits.Add(new IniOverlayEdit(null, name, "Location", location.X + "," + location.Y));
            foreach (string keyValue in typeKeys)
            {
                int equals = keyValue.IndexOf('=');
                edits.Add(new IniOverlayEdit(null, name, keyValue.Substring(0, equals), keyValue.Substring(equals + 1)));
            }

            XNAControl control = tools.AddControl(window, name, edits, out string error);
            if (control == null)
            {
                SetStatus(error, WarningColor);
                return;
            }

            mode = PanelMode.Inspector;
            tbEdit.Text = string.Empty;
            Select(control);
            SetStatus($"Added {name} to {addContainer.Name}. Drag it into place and set its keys; Ctrl+Z removes it again.", ThemeKeyColor);
        }

        private void StartReview()
        {
            if (tools.PendingEdits.Count == 0)
            {
                SetStatus("There are no unsaved changes.");
                return;
            }

            reviewing = true;
            SetStatus("Review the changes below, then Write files. Lines starting with + are new.");
        }

        private void EndReview()
        {
            reviewing = false;
            RefreshPanel();
        }

        private void SaveReviewed()
        {
            reviewing = false;
            string result = tools.Save();
            SetStatus(result, result.StartsWith("Saved", StringComparison.Ordinal) ? ThemeKeyColor : WarningColor);
        }

        private void RefreshPanel()
        {
            if (panel == null)
                return;

            btnTabInspector.IdleTexture = mode == PanelMode.Inspector ? activeTabTexture : tabTexture;
            btnTabFiles.IdleTexture = mode == PanelMode.Files ? activeTabTexture : tabTexture;
            btnTabAdd.IdleTexture = mode == PanelMode.AddControl ? activeTabTexture : tabTexture;

            btnSave.Visible = btnSave.Enabled = !reviewing;
            btnUndo.Visible = btnUndo.Enabled = !reviewing;
            btnDiscard.Visible = btnDiscard.Enabled = !reviewing;
            btnWrite.Visible = btnWrite.Enabled = reviewing;
            btnBack.Visible = btnBack.Enabled = reviewing;
            tbEdit.Visible = tbEdit.Enabled = !reviewing;

            int pending = tools.PendingEdits.Count;
            pendingLine = pending == 0 ? "No unsaved changes." :
                $"{pending} unsaved change(s) in {string.Join(", ", tools.PendingEdits.Select(p => Path.GetFileName(p.Edit.FilePath)).Distinct(StringComparer.OrdinalIgnoreCase))}. Ctrl+S to review.";

            string previousKey = lbKeys.SelectedItem?.Tag as string;
            lbKeys.Clear();
            lbKeys.SelectedIndex = -1;

            if (reviewing)
                FillReviewList();
            else if (mode == PanelMode.Files)
                FillFileList();
            else if (mode == PanelMode.AddControl)
                FillControlTypeList();
            else
                FillKeyList(previousKey);

            FillInfoLines();
        }

        private void FillInfoLines()
        {
            for (int i = 0; i < INFO_LINES; i++)
                infoLines[i] = string.Empty;

            if (reviewing)
            {
                infoLines[0] = "Unsaved changes";
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
                infoLines[1] = "Pick a type, type a name, press Enter";
                infoLines[2] = addWindow == null ? string.Empty : "Saved to " + string.Join(", ", GetTargetFiles(new[] { addContainer }));
                return;
            }

            if (selected == null)
            {
                infoLines[0] = "Nothing selected";
                infoLines[1] = "Click a control to inspect it.";
                return;
            }

            Rectangle screen = selected.GetWindowRectangle();
            infoLines[0] = Describe(selected);
            infoLines[3] = $"Position {Format(selected.ClientRectangle)} in parent, {screen.X},{screen.Y} on screen";

            if (selection.Count > 1)
            {
                Rectangle bounds = GetSelectionBounds();
                infoLines[0] += $" and {selection.Count - 1} more";
                infoLines[3] = "Also selected: " + ListNames(selection.Take(selection.Count - 1).Select(c => c.Name), 5);
                infoLines[5] = $"Together {bounds.Width}x{bounds.Height}, changes go to {string.Join(", ", GetTargetFiles(selection))}";
            }

            IniWindow window = IniWindow.OwnerOf(selected);
            if (window == null)
            {
                infoLines[1] = "Not inside a window that reads an INI file";
                return;
            }

            string themeFolder = DesignerTools.DisplayPath(ProgramConstants.GetResourcePath()).TrimEnd('\\', '/');
            string ownFile = themeFolder + "\\" + window.OwnIniFileName;
            infoLines[1] = $"Window {window.Control.Name} ({window.Control.GetType().Name})";

            if (window.IniPath == null)
                infoLines[2] = $"No INI file yet; the first edit creates {ownFile}";
            else if (window.UsesGenericWindowIni)
                infoLines[2] = $"Uses shared {DesignerTools.DisplayPath(window.IniPath)}; edits go to {ownFile}";
            else
                infoLines[2] = "INI " + DesignerTools.DisplayPath(window.IniPath);

            string section = window.SectionFor(selected);
            infoLines[4] = section == null ? "No name, so no INI section can refer to it" :
                window.Ini?.GetSection(section) == null ? $"[{section}] is not in the INI yet; edits add it" : $"Section [{section}]";
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

        private void FillKeyList(string previousKey)
        {
            IniWindow window = selected == null ? null : IniWindow.OwnerOf(selected);
            string section = window?.SectionFor(selected);
            var iniSection = section == null ? null : window.Ini?.GetSection(section);

            if (iniSection == null)
            {
                keySourceLine = string.Empty;
                return;
            }

            string themeDirectory = Path.GetFullPath(ProgramConstants.GetResourcePath());

            foreach (var kvp in iniSection.Keys)
            {
                IniKeySource source = window.Ini.GetKeySource(section, kvp.Key);
                bool isPending = source != null && IniOverlay.GetEdits(source.FilePath).Any(e => e.Section == source.Section && e.Key == kvp.Key);
                bool isTheme = source != null && Path.GetFullPath(source.FilePath).StartsWith(themeDirectory, StringComparison.OrdinalIgnoreCase);

                var item = new XNAListBoxItem((isPending ? "* " : string.Empty) + kvp.Key + " = " + kvp.Value,
                    isPending ? PendingColor : isTheme ? ThemeKeyColor : DimTextColor);
                item.Tag = kvp.Key;
                lbKeys.AddItem(item);

                if (kvp.Key == previousKey)
                    lbKeys.SelectedIndex = lbKeys.Items.Count - 1;
            }
        }

        private void FillReviewList()
        {
            foreach (var file in tools.PendingEdits.GroupBy(p => Path.GetFullPath(p.Edit.FilePath), StringComparer.OrdinalIgnoreCase))
            {
                bool createsFile = file.First().CreatesFile;
                lbKeys.AddItem(DesignerTools.DisplayPath(file.Key) + (createsFile ? " (new file)" : string.Empty), TextColor, false);

                foreach (PendingEdit edit in file)
                {
                    string line = edit.OldValue == null
                        ? $"  + [{edit.Edit.Section}] {edit.Edit.Key}={edit.Edit.Value}"
                        : $"    [{edit.Edit.Section}] {edit.Edit.Key}: {edit.OldValue} -> {edit.Edit.Value}";
                    lbKeys.AddItem(line, edit.OldValue == null ? ThemeKeyColor : PendingColor, false);
                }
            }
        }

        private static string Describe(XNAControl control) =>
            (string.IsNullOrEmpty(control.Name) ? "(unnamed)" : control.Name) + " (" + control.GetType().Name + ")";

        private static string Format(Rectangle r) => $"{r.X},{r.Y} {r.Width}x{r.Height}";

        public override void Draw(GameTime gameTime)
        {
            // Controls with unsaved changes: a faint outline and a corner marker
            foreach (XNAControl control in tools.EditedControls)
            {
                if (!IsShown(control))
                    continue;

                Rectangle rectangle = control.GetWindowRectangle();
                DrawRectangle(rectangle, PendingColor * 0.45f);
                FillRectangle(new Rectangle(rectangle.Right - 7, rectangle.Y, 7, 7), PendingColor);
            }

            if (selected != null && IniWindow.OwnerOf(selected) is IniWindow window && window.Control != selected)
                DrawRectangle(window.Control.GetWindowRectangle(), WindowColor * 0.6f);

            if (hovered != null && !selection.Contains(hovered))
            {
                Rectangle rectangle = hovered.GetWindowRectangle();
                DrawRectangle(rectangle, HoverColor);
                DrawTag(Describe(hovered), rectangle, HoverColor);
            }

            if (selection.Count > 0)
            {
                foreach (XNAControl control in selection)
                {
                    if (control == selected)
                        DrawRectangle(control.GetWindowRectangle(), SelectionColor, 2);
                    else
                        DrawRectangle(control.GetWindowRectangle(), SecondarySelectionColor);
                }

                Rectangle bounds = GetSelectionBounds();
                if (selection.Count > 1)
                    DrawRectangle(bounds, SelectionColor * 0.5f);

                FillRectangle(GetHandleRectangle(), SelectionColor);
                DrawTag(selection.Count > 1 ? $"{selection.Count} selected  {bounds.Width}x{bounds.Height}" :
                    $"{selected.Name}  {bounds.Width}x{bounds.Height}", bounds, SelectionColor);
            }

            if (drag != DragMode.None)
            {
                foreach (int x in guideXs)
                    FillRectangle(new Rectangle(x, 0, 1, Height), GuideColor);

                foreach (int y in guideYs)
                    FillRectangle(new Rectangle(0, y, Width, 1), GuideColor);
            }

            base.Draw(gameTime);

            DrawPanelText();
        }

        // Whether the control is still part of the UI
        private bool IsAttached(XNAControl control)
        {
            XNAControl root = control;
            while (root.Parent != null)
                root = root.Parent;

            return WindowManager.TopLevelControls.Contains(root);
        }

        // Whether the control is visible and still part of the UI
        private bool IsShown(XNAControl control)
        {
            XNAControl root = control;
            for (XNAControl current = control; current != null; current = current.Parent)
            {
                if (!current.Visible)
                    return false;

                root = current;
            }

            return WindowManager.TopLevelControls.Contains(root);
        }

        private void DrawTag(string text, Rectangle target, Color color)
        {
            Vector2 size = Renderer.GetTextDimensions(text, FONT);
            int y = target.Y - (int)size.Y - 4 >= 0 ? target.Y - (int)size.Y - 4 : target.Bottom + 2;
            var background = new Rectangle(target.X, y, (int)size.X + 8, (int)size.Y + 4);
            FillRectangle(background, new Color(0, 0, 0, 200));
            DrawString(text, FONT, new Vector2(background.X + 4, background.Y + 2), color);
        }

        private void DrawPanelText()
        {
            int x = panel.X + MARGIN;
            int width = PANEL_WIDTH - MARGIN * 2;
            int y = MARGIN + TAB_HEIGHT + 6;

            for (int i = 0; i < INFO_LINES; i++)
            {
                DrawPanelLine(infoLines[i], x, y, width, i == 0 ? SelectionColor : TextColor);
                y += LINE_HEIGHT;
            }

            string legend = reviewing || mode == PanelMode.AddControl ? string.Empty :
                mode == PanelMode.Files ? (fileLevel == 0 ? string.Empty : "Green in this file, grey inherited, yellow unsaved") :
                "Keys: green from this theme, grey inherited, yellow unsaved";
            DrawPanelLine(legend, x, y, width, DimTextColor);

            y = lbKeys.Bottom + 2;
            if (WindowManager.SelectedControl == tbEdit && editHintLine.Length > 0)
                DrawPanelLine(editHintLine, x, y, width, UnsetKeyColor);
            else
                DrawPanelLine(keySourceLine, x, y, width, DimTextColor);

            y = btnSave.Bottom + 8;
            DrawPanelLine(pendingLine, x, y, width, tools.PendingEdits.Count > 0 ? PendingColor : DimTextColor);
            y += LINE_HEIGHT;

            foreach (string line in WrapText(status, width, 2))
            {
                DrawPanelLine(line, x, y, width, statusColor);
                y += LINE_HEIGHT;
            }

            y = Height - LINE_HEIGHT * HELP_LINES - MARGIN;
            DrawPanelLine("Click select, Ctrl+click add/remove, PgUp parent, PgDn children", x, y, width, DimTextColor);
            DrawPanelLine("Drag move, corner resize (scales a group), Alt no snap, arrows nudge", x, y + LINE_HEIGHT, width, DimTextColor);
            DrawPanelLine("Ctrl+Z undo  Ctrl+S save  Esc deselect  F7 dock  F6 close", x, y + LINE_HEIGHT * 2, width, DimTextColor);
        }

        private void DrawPanelLine(string text, int x, int y, int width, Color color)
        {
            if (string.IsNullOrEmpty(text))
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
    }
}
