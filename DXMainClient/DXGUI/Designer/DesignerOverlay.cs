using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Input;
using Rampastring.XNAUI;
using Rampastring.XNAUI.XNAControls;

namespace DTAClient.DXGUI.Designer
{
    /// <summary>
    /// The designer's inspector and editor, drawn over the whole client while open (F6).
    /// Hovering outlines a control; clicking selects it and lists its INI keys with the file each
    /// comes from. Ctrl+click adds controls to the selection. Selected controls can be dragged, resized
    /// (a group is scaled) from the corner handle, nudged with the arrow keys, duplicated, deleted or given
    /// new key values; while typing, the keys the control can read are suggested, and keys that take a
    /// color, image or sound get a picker. Changes are previewed live, controls with unsaved changes are
    /// marked, and changes are saved after review (Ctrl+S).
    /// Dragging snaps to the edges and centers of neighboring controls (Alt turns it off), and an axis
    /// only changes once the drag moved more than a few pixels along it. The panel can be moved, docked to
    /// either side, collapsed to its title bar or hidden (middle click). Its tabs list the controls of a
    /// window, add new controls and edit theme-wide files such as DTACnCNetClient.ini.
    /// </summary>
    internal sealed partial class DesignerOverlay : XNAControl
    {
        private const int PANEL_WIDTH = 460;
        private const int MARGIN = 10;
        private const int HEADER_HEIGHT = 30;
        private const int TAB_HEIGHT = 24;
        private const int LINE_HEIGHT = 18;
        private const int INFO_LINES = 5;
        private const int ACTION_HEIGHT = 22;
        private const int BUTTON_HEIGHT = 24;
        private const int EDIT_HEIGHT = 22;
        private const int STATUS_LINES = 3;
        private const int HANDLE_SIZE = 9;
        private const int DRAG_THRESHOLD = 3;
        private const int AXIS_TOLERANCE = 4;
        private const int SNAP_DISTANCE = 6;
        private const int DOCK_SNAP_DISTANCE = 24;
        private const int TOOLBAR_HEIGHT = 20;
        private const int SUGGESTION_LINES = 10;
        private const int FONT = DesignerStyle.FONT;

        private static readonly Color HoverColor = new Color(80, 220, 255);
        private static readonly Color SelectionColor = new Color(255, 214, 64);
        private static readonly Color SecondarySelectionColor = new Color(255, 150, 60);
        private static readonly Color WindowColor = new Color(220, 110, 255);
        private static readonly Color GuideColor = new Color(255, 80, 200);
        private static readonly Color OutlineColor = new Color(120, 200, 255) * 0.3f;
        private static readonly Color SourceLinkColor = new Color(90, 230, 170);
        private static readonly Color FollowerLinkColor = new Color(200, 150, 255);

        private enum DragMode { None, Move, Resize }

        private enum PanelAnchor { Left, Right, Free }

        private readonly DesignerTools tools;

        private XNAControl hovered;

        // The selected control whose keys the panel shows: the last one in the selection
        private XNAControl selected;
        private readonly List<XNAControl> selection = new List<XNAControl>();

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

        private PanelAnchor panelAnchor = PanelAnchor.Right;
        private Point panelLocation;
        private bool panelCollapsed;
        private bool panelHidden;
        private bool panelDragging;
        private Point panelDragOffset;

        private bool showOutlines;

        // The selection toolbar drawn next to the selected controls
        private readonly List<DesignerButton> toolbar = new List<DesignerButton>();

        // Every button of the panel and the toolbar, for their tool tips
        private readonly List<DesignerButton> allButtons = new List<DesignerButton>();
        private DesignerButton tbtnAdd;
        private DesignerButton tbtnDuplicate;
        private DesignerButton tbtnDelete;
        private DesignerButton tbtnParent;

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

            InitializePanel();

            tbtnAdd = CreateToolbarButton("+ Add", "Add a control inside it or next to it", () => SetMode(PanelMode.AddControl));
            tbtnDuplicate = CreateToolbarButton("Duplicate", "Ctrl+D", DuplicateSelected);
            tbtnParent = CreateToolbarButton("Parent", "PageUp", SelectParent);
            tbtnDelete = CreateToolbarButton("Delete", "Del", DeleteSelection);
            tbtnDelete.Style = DesignerButtonStyle.Danger;

            base.Initialize();

            LayoutPanel();
            Disable();
        }

        private DesignerButton CreateToolbarButton(string text, string toolTip, Action action)
        {
            var button = new DesignerButton(WindowManager, text);
            button.ToolTipText = toolTip;
            button.ClientRectangle = new Rectangle(0, 0, DesignerButton.GetWidthFor(text, 14), TOOLBAR_HEIGHT);
            button.LeftClick += (s, e) => action();
            toolbar.Add(button);
            allButtons.Add(button);
            AddChild(button);
            return button;
        }

        public void Toggle()
        {
            if (IsOpen)
            {
                drag = DragMode.None;
                panelDragging = false;
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

        /// <summary>
        /// Handles a key press while the overlay is open.
        /// </summary>
        /// <returns>True if the key was used.</returns>
        public bool HandleKey(Keys key)
        {
            if (WindowManager.SelectedControl == tbEdit && key != Keys.Escape)
                return false;

            bool ctrl = WindowManager.Keyboard.IsCtrlHeldDown();
            bool shift = WindowManager.Keyboard.IsShiftHeldDown();
            int step = shift ? 10 : 1;

            switch (key)
            {
                case Keys.Escape:
                    HandleEscape();
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
                    SelectParent();
                    return true;
                case Keys.PageDown:
                    SelectChildren();
                    return true;
                case Keys.Delete:
                    DeleteSelection();
                    return true;
                case Keys.F1:
                    ToggleHelp();
                    return true;
                case Keys.F7:
                    DockToOtherSide();
                    return true;
                case Keys.F8:
                    TogglePanelHidden();
                    return true;
                case Keys.F9:
                    ToggleOutlines();
                    return true;
                case Keys.D when ctrl:
                    DuplicateSelected();
                    return true;
                case Keys.F when ctrl:
                    SetMode(PanelMode.Controls);
                    WindowManager.SelectedControl = tbEdit;
                    return true;
                case Keys.S when ctrl:
                    StartReview();
                    return true;
                case Keys.Z when ctrl && shift:
                case Keys.Y when ctrl:
                    SetStatus(tools.Redo());
                    return true;
                case Keys.Z when ctrl:
                    SetStatus(tools.Undo());
                    return true;
            }

            return false;
        }

        private void HandleEscape()
        {
            if (WindowManager.SelectedControl == tbEdit)
            {
                WindowManager.SelectedControl = null;
            }
            else if (showingHelp)
            {
                ToggleHelp();
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
            else if (mode == PanelMode.AddControl || mode == PanelMode.Files)
            {
                SetMode(PanelMode.Inspector);
            }
            else
            {
                Select(null);
            }
        }

        public override void Update(GameTime gameTime)
        {
            if (Width != WindowManager.RenderResolutionX || Height != WindowManager.RenderResolutionY)
                LayoutPanel();

            if (selection.RemoveAll(c => !IsAttached(c) || tools.IsDeleted(c)) > 0)
                SelectionChanged();

            UpdateButtonStates();
            LayoutToolbar();

            base.Update(gameTime);

            UpdateEditHelpers();

            Point cursorPoint = WindowManager.Cursor.Location;
            var cursor = WindowManager.Cursor;

            if (cursor.MiddlePressedDown)
                TogglePanelHidden();

            if (panelDragging)
            {
                if (cursor.LeftDown)
                    MovePanel(cursorPoint);
                else
                    panelDragging = false;

                return;
            }

            if (drag != DragMode.None)
            {
                if (cursor.LeftDown)
                    UpdateDrag(cursorPoint);
                else
                    EndDrag(cursorPoint);

                return;
            }

            bool overPanel = panel.Visible && panel.GetWindowRectangle().Contains(cursorPoint);
            bool overToolbar = toolbar.Any(b => b.Visible && b.GetWindowRectangle().Contains(cursorPoint));
            hovered = overPanel || overToolbar ? null : ResolvePart(FindControlAt(cursorPoint));

            if (!cursor.LeftPressedDown)
                return;

            if (overPanel)
            {
                HandlePanelPress(cursorPoint);
                return;
            }

            if (overToolbar)
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

        #region Panel position

        private void HandlePanelPress(Point cursorPoint)
        {
            XNAControl crumb = FindBreadcrumbAt(cursorPoint);
            if (crumb != null)
            {
                Select(crumb);
                return;
            }

            // Pressing on the panel itself rather than one of its controls moves it
            if (panel.IsDirectlyActive)
            {
                panelDragging = true;
                panelDragOffset = new Point(cursorPoint.X - panel.X, cursorPoint.Y - panel.Y);
            }
        }

        private void MovePanel(Point cursorPoint)
        {
            int x = Math.Max(0, Math.Min(Width - PANEL_WIDTH, cursorPoint.X - panelDragOffset.X));
            int y = panelCollapsed ? Math.Max(0, Math.Min(Height - HEADER_HEIGHT, cursorPoint.Y - panelDragOffset.Y)) : 0;

            panelAnchor = x <= DOCK_SNAP_DISTANCE ? PanelAnchor.Left :
                x >= Width - PANEL_WIDTH - DOCK_SNAP_DISTANCE ? PanelAnchor.Right : PanelAnchor.Free;

            panelLocation = new Point(x, y);
            PlacePanel();
        }

        private void PlacePanel()
        {
            int x = panelAnchor == PanelAnchor.Left ? 0 :
                panelAnchor == PanelAnchor.Right ? Width - PANEL_WIDTH : Math.Max(0, Math.Min(Width - PANEL_WIDTH, panelLocation.X));
            int y = panelCollapsed ? Math.Max(0, Math.Min(Height - HEADER_HEIGHT, panelLocation.Y)) : 0;

            panel.ClientRectangle = new Rectangle(x, y, PANEL_WIDTH, panelCollapsed ? HEADER_HEIGHT : Height);
            btnDock.Text = x + PANEL_WIDTH / 2 < Width / 2 ? ">" : "<";
        }

        private void DockToOtherSide()
        {
            panelAnchor = panel.X + PANEL_WIDTH / 2 < Width / 2 ? PanelAnchor.Right : PanelAnchor.Left;
            PlacePanel();
        }

        private void ToggleCollapsed()
        {
            panelCollapsed = !panelCollapsed;
            panelLocation = new Point(panel.X, panelCollapsed ? panelLocation.Y : 0);
            LayoutPanel();
        }

        private void TogglePanelHidden()
        {
            panelHidden = !panelHidden;
            panel.Visible = panel.Enabled = !panelHidden;

            if (panelHidden && WindowManager.SelectedControl == tbEdit)
                WindowManager.SelectedControl = null;
        }

        private void ToggleOutlines()
        {
            showOutlines = !showOutlines;
            btnOutlines.Checked = showOutlines;
            SetStatus(showOutlines ? "Outlining every control (F9 to stop)." : "Outlines off.");
        }

        #endregion

        #region Selection

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

        /// <summary>
        /// Returns the control a click on a part of a composite control selects: the columns and headers of
        /// a multi-column list box, scroll bars and a scroll panel's content panel select the control they
        /// belong to. With Shift held, the part itself is selected.
        /// </summary>
        private XNAControl ResolvePart(XNAControl control)
        {
            if (control == null || WindowManager.Keyboard.IsShiftHeldDown())
                return control;

            for (XNAControl ancestor = control.Parent; ancestor != null; ancestor = ancestor.Parent)
            {
                if (ancestor is XNAMultiColumnListBox)
                    return ancestor;
            }

            while (control.Parent != null && IsInternalPart(control))
                control = control.Parent;

            return control;
        }

        private static bool IsInternalPart(XNAControl control) =>
            control is XNAScrollBar || control is XNAHorizontalScrollBar ||
            (control.Parent is XNAScrollPanel && control.Name != null &&
            (control.Name.EndsWith("_ContentPanel", StringComparison.Ordinal) || control.Name.EndsWith("_CornerPanel", StringComparison.Ordinal)));

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

        private void SelectParent()
        {
            XNAControl parent = selected?.Parent;

            // A scroll panel's content panel is part of the scroll panel
            while (parent != null && IsInternalPart(parent) && parent.Parent != null)
                parent = parent.Parent;

            if (parent != null)
                Select(parent);
        }

        private void SelectChildren()
        {
            if (selected == null)
                return;

            List<XNAControl> children = GetSelectableChildren(selected).ToList();
            if (children.Count == 0)
            {
                SetStatus($"{Describe(selected)} has no visible named controls inside it.", DesignerStyle.Warning);
                return;
            }

            string parentName = selected.Name;
            selection.Clear();
            selection.AddRange(children);
            SelectionChanged();
            SetStatus($"Selected the {children.Count} controls inside {parentName}. Drag the corner handle to scale them together.");
        }

        // A scroll panel's controls are inside its content panel
        private static IEnumerable<XNAControl> GetSelectableChildren(XNAControl control) =>
            control.Children.SelectMany(c => IsInternalPart(c) ? c.Children : (IEnumerable<XNAControl>)new[] { c })
                .Where(c => c.Visible && !string.IsNullOrEmpty(c.Name) && !IsInternalPart(c));

        private void SelectionChanged()
        {
            selected = selection.LastOrDefault();
            showingHelp = false;

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

        // Windows and the controls that hold them can't be deleted or duplicated
        private static bool IsWindow(XNAControl control) => IniWindow.From(control) != null || control.Parent == null;

        // Selected controls inside another selected control move along with it
        private List<XNAControl> GetOutermostSelected() =>
            selection.Where(c => !selection.Any(other => other != c && other.IsParentOf(c))).ToList();

        /// <summary>
        /// Places the selection toolbar under the selection, or above it when there is no room below.
        /// </summary>
        private void LayoutToolbar()
        {
            bool show = selection.Count > 0 && drag == DragMode.None && !reviewing && selection.All(IsShown);

            foreach (DesignerButton button in toolbar)
                button.Visible = button.Enabled = show;

            if (!show)
                return;

            bool windows = selection.Any(IsWindow);
            tbtnDuplicate.Visible = tbtnDuplicate.Enabled = selection.Count == 1 && !windows;
            tbtnDelete.Visible = tbtnDelete.Enabled = !windows;

            Rectangle bounds = GetSelectionBounds();
            List<DesignerButton> shown = toolbar.Where(b => b.Visible).ToList();
            int width = shown.Sum(b => b.Width) + (shown.Count - 1) * 2;
            int x = Math.Max(0, Math.Min(Width - width, bounds.X));
            int y = bounds.Bottom + HANDLE_SIZE / 2 + 4;
            if (y + TOOLBAR_HEIGHT > Height)
                y = Math.Max(0, bounds.Y - TOOLBAR_HEIGHT - LINE_HEIGHT - 8);

            // Beside the panel rather than over it
            Rectangle panelRectangle = panel.Visible ? panel.ClientRectangle : Rectangle.Empty;
            if (panelRectangle.Intersects(new Rectangle(x, y, width, TOOLBAR_HEIGHT)))
                x = panelRectangle.X >= width ? panelRectangle.X - width - 2 : Math.Min(Width - width, panelRectangle.Right + 2);

            foreach (DesignerButton button in shown)
            {
                button.ClientRectangle = new Rectangle(x, y, button.Width, TOOLBAR_HEIGHT);
                x += button.Width + 2;
            }
        }

        #endregion

        #region Dragging

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

        #endregion

        #region Drawing

        public override void Draw(GameTime gameTime)
        {
            if (showOutlines)
                DrawAllOutlines();

            // Controls with unsaved changes: a faint outline and a corner marker
            foreach (XNAControl control in tools.EditedControls)
            {
                if (!IsShown(control))
                    continue;

                Rectangle rectangle = control.GetWindowRectangle();
                DrawRectangle(rectangle, SelectionColor * 0.45f);
                FillRectangle(new Rectangle(rectangle.Right - 7, rectangle.Y, 7, 7), SelectionColor);
            }

            if (selected != null && IniWindow.OwnerOf(selected) is IniWindow window && window.Control != selected)
                DrawRectangle(window.Control.GetWindowRectangle(), WindowColor * 0.6f);

            if (hovered != null && !selection.Contains(hovered))
            {
                Rectangle rectangle = hovered.GetWindowRectangle();
                DrawRectangle(rectangle, HoverColor);
                DrawTag(Describe(hovered), rectangle, HoverColor);
            }

            if (selected != null && IsShown(selected))
                DrawLinks();

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
                DrawRectangle(GetHandleRectangle(), Color.Black * 0.6f);

                Rectangle r = selected.ClientRectangle;
                DrawTag(selection.Count > 1 ? $"{selection.Count} selected  {bounds.Width}x{bounds.Height}" :
                    $"{selected.Name}  {r.X},{r.Y}  {r.Width}x{r.Height}", bounds, SelectionColor);
            }

            if (drag != DragMode.None)
            {
                foreach (int x in guideXs)
                    FillRectangle(new Rectangle(x, 0, 1, Height), GuideColor);

                foreach (int y in guideYs)
                    FillRectangle(new Rectangle(0, y, Width, 1), GuideColor);
            }

            // Draws the panel, which draws its text through DrawPanelHeader and DrawPanelText
            base.Draw(gameTime);

            if (!panel.Visible)
                DrawHiddenPanelHint();

            DrawToolTip();
        }

        // The controls the selected one is positioned from and those that follow it, joined to it by a line
        private void DrawLinks()
        {
            Rectangle selectedArea = selected.GetWindowRectangle();

            foreach (var group in selectedLinks.GroupBy(l => l.Follower == selected ? l.Source : l.Follower))
            {
                XNAControl other = group.Key;
                if (!IsShown(other))
                    continue;

                bool isSource = group.First().Follower == selected;
                Color color = isSource ? SourceLinkColor : FollowerLinkColor;
                Rectangle area = other.GetWindowRectangle();

                DrawLine(selectedArea.Center.ToVector2(), area.Center.ToVector2(), color * 0.7f);
                DrawRectangle(area, color);
                DrawTag((isSource ? "positions it: " : "follows it: ") + other.Name + " (" + string.Join(", ", group.Select(l => l.Key).Distinct()) + ")",
                    area, color);
            }
        }

        private void DrawAllOutlines()
        {
            foreach (XNAControl control in WindowManager.TopLevelControls)
            {
                if (control != this && control.Visible)
                    DrawOutlines(control);
            }
        }

        private void DrawOutlines(XNAControl control)
        {
            foreach (XNAControl child in control.Children)
            {
                if (!child.Visible)
                    continue;

                if (!string.IsNullOrEmpty(child.Name) && !IsInternalPart(child))
                    DrawRectangle(child.GetWindowRectangle(), OutlineColor);

                if (child is not XNAMultiColumnListBox)
                    DrawOutlines(child);
            }
        }

        private void DrawHiddenPanelHint()
        {
            const string hint = "Designer: middle click or F8 shows the panel";
            Vector2 size = Renderer.GetTextDimensions(hint, FONT);
            var box = new Rectangle((Width - (int)size.X) / 2 - 8, 4, (int)size.X + 16, (int)size.Y + 6);
            FillRectangle(box, new Color(0, 0, 0, 170));
            DrawString(hint, FONT, new Vector2(box.X + 8, box.Y + 3), DesignerStyle.DimText);
        }

        private void DrawToolTip()
        {
            DesignerButton button = allButtons.Find(b => b.IsDirectlyActive && b.Visible && b.Parent?.Visible == true);
            if (button == null || string.IsNullOrEmpty(button.ToolTipText))
                return;

            Vector2 size = Renderer.GetTextDimensions(button.ToolTipText, FONT);
            Rectangle target = button.GetWindowRectangle();
            int x = Math.Max(0, Math.Min(Width - (int)size.X - 10, target.X));
            int y = target.Bottom + 4 + (int)size.Y + 6 <= Height ? target.Bottom + 4 : target.Y - (int)size.Y - 10;
            var box = new Rectangle(x, y, (int)size.X + 10, (int)size.Y + 6);
            FillRectangle(box, new Color(4, 5, 7, 240));
            DrawRectangle(box, DesignerStyle.Border);
            DrawString(button.ToolTipText, FONT, new Vector2(box.X + 5, box.Y + 3), DesignerStyle.Text);
        }

        private void DrawTag(string text, Rectangle target, Color color)
        {
            Vector2 size = Renderer.GetTextDimensions(text, FONT);
            int y = target.Y - (int)size.Y - 4 >= 0 ? target.Y - (int)size.Y - 4 : target.Bottom + 2;
            var background = new Rectangle(target.X, y, (int)size.X + 8, (int)size.Y + 4);
            FillRectangle(background, new Color(0, 0, 0, 200));
            DrawString(text, FONT, new Vector2(background.X + 4, background.Y + 2), color);
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

        private static string Describe(XNAControl control) =>
            (string.IsNullOrEmpty(control.Name) ? "(unnamed)" : control.Name) + " (" + control.GetType().Name + ")";

        #endregion
    }
}
