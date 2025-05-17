using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Input;
using System;
using System.Collections.Generic;

namespace Rampastring.XNAUI.XNAControls;

/// <summary>
/// A panel that allows for vertical scrolling.
/// </summary>
public class XNAClientScrollPanel : XNAPanel
{
    private const int MARGIN = 2;
    private const double SCROLL_REPEAT_TIME = 0.03;
    private const double FAST_SCROLL_TRIGGER_TIME = 0.4;
    protected XNAScrollBar ScrollBar;
    private TimeSpan _scrollKeyTime = TimeSpan.Zero;
    private TimeSpan _timeSinceLastScroll = TimeSpan.Zero;
    private bool _isScrollingQuickly = false;

    private Dictionary<XNAControl, Point> _originalPositions = new Dictionary<XNAControl, Point>();

    public event EventHandler ViewPositionChanged;

    public XNAClientScrollPanel(WindowManager windowManager) : base(windowManager)
    {
        DrawMode = ControlDrawMode.UNIQUE_RENDER_TARGET; //so controls can be clipped
        ScrollBar = new XNAScrollBar(WindowManager);
        ScrollBar.Name = "XNASPSB";
        ClientRectangleUpdated += XNAClientScrollPanel_ClientRectangleUpdated;
    }

    public override void Initialize()
    {
        base.Initialize();

        ScrollBar.ClientRectangle = new Rectangle(Width - ScrollBar.ScrollWidth - 1,
            1, ScrollBar.ScrollWidth, Height - 2);
        ScrollBar.Scrolled += ScrollBar_Scrolled;
        AddChild(ScrollBar);
        ScrollBar.Refresh();

        ParentChanged += Parent_ClientRectangleUpdated;

        if (Parent != null)
            Parent.ClientRectangleUpdated += Parent_ClientRectangleUpdated;
    }

    public int ScrollStep
    {
        get => ScrollBar.ScrollStep;
        set => ScrollBar.ScrollStep = value;
    }

    private int _viewTop;

    public int ViewTop
    {
        get => _viewTop;
        set
        {
            if (value != _viewTop)
            {
                _viewTop = Math.Max(0, value);
                ViewPositionChanged?.Invoke(this, EventArgs.Empty);
                ScrollBar.RefreshButtonY(_viewTop);

                UpdateChildPositions();
            }
        }
    }

    public int ContentHeight { get; set; } // optional - we automatically do this via RecalculateContentHeight 

    public bool AllowKeyboardInput { get; set; } = true;

    private bool _enableScrollbar = true;

    public bool EnableScrollbar
    {
        get => _enableScrollbar;
        set
        {
            _enableScrollbar = value;
            ScrollBar.Visible = _enableScrollbar;
            ScrollBar.Enabled = _enableScrollbar;
        }
    }

    public override void AddChild(XNAControl child)
    {
        if (child != null && child != ScrollBar)
        {
            _originalPositions[child] = new Point(child.X, child.Y);
        }

        base.AddChild(child);

        RecalculateContentHeight();
        RefreshScrollbar();
        UpdateChildPositions();
    }

    public void RecalculateContentHeight()
    {
        int maxHeight = 0;

        foreach (XNAControl child in Children)
        {
            if (child == ScrollBar)
                continue;

            int childBottom = child.Y + child.Height;
            if (childBottom > maxHeight)
                maxHeight = childBottom;
        }

        ContentHeight = maxHeight + MARGIN;
    }

    public void RefreshScrollbar()
    {
        ScrollBar.Length = ContentHeight;
        ScrollBar.DisplayedPixelCount = Height - MARGIN * 2;
        ScrollBar.Refresh();
    }

    public void ScrollToTop()
    {
        ViewTop = 0;
    }

    public void ScrollToBottom()
    {
        if (ContentHeight <= Height - MARGIN * 2)
        {
            ViewTop = 0;
            return;
        }

        ViewTop = ContentHeight - Height + MARGIN * 2;
    }

    public void ScrollToControl(XNAControl control)
    {
        if (!Children.Contains(control) || control == ScrollBar)
            return;

        Point originalPos = _originalPositions.ContainsKey(control)
            ? _originalPositions[control]
            : new Point(control.X, control.Y);

        // scroll up
        if (ViewTop > originalPos.Y)
            ViewTop = originalPos.Y;
        // scroll down
        else if (ViewTop + Height <= (originalPos.Y + control.Height))
            ViewTop = Math.Min((originalPos.Y + control.Height) - Height + MARGIN, ContentHeight - Height + MARGIN * 2);
    }

    private void XNAClientScrollPanel_ClientRectangleUpdated(object sender, EventArgs e)
    {
        if (ScrollBar != null)
        {
            ScrollBar.ClientRectangle = new Rectangle(Width - ScrollBar.ScrollWidth - 1,
                                                    1, ScrollBar.ScrollWidth, Height - 2);
            ScrollBar.DisplayedPixelCount = Height - MARGIN * 2;
            ScrollBar.Refresh();
        }
    }

    private void Parent_ClientRectangleUpdated(object sender, EventArgs e)
    {
        ScrollBar.Refresh();
    }

    public int GetScrollBarWidth()
    {
        return ScrollBar.Width;
    }

    private void ScrollBar_Scrolled(object sender, EventArgs e)
    {
        ViewTop = ScrollBar.ViewTop;
    }

    private void UpdateChildPositions()
    {
        foreach (XNAControl child in Children)
        {
            if (child == ScrollBar)
                continue;

            if (!_originalPositions.ContainsKey(child))
            {
                _originalPositions[child] = new Point(child.X, child.Y);
            }

            // update the child's Y
            child.ClientRectangle = new Rectangle(_originalPositions[child].X, _originalPositions[child].Y - ViewTop,
                child.Width, child.Height);
        }
    }

    public override void Update(GameTime gameTime)
    {
        if (IsActive && AllowKeyboardInput)
        {
            if (Keyboard.IsKeyHeldDown(Keys.Up))
            {
                HandleScrollKeyDown(gameTime, ScrollUp);
            }
            else if (Keyboard.IsKeyHeldDown(Keys.Down))
            {
                HandleScrollKeyDown(gameTime, ScrollDown);
            }
            else
            {
                _isScrollingQuickly = false;
                _timeSinceLastScroll = TimeSpan.Zero;
                _scrollKeyTime = TimeSpan.Zero;
            }
        }

        base.Update(gameTime);
    }

    private void HandleScrollKeyDown(GameTime gameTime, Action action)
    {
        if (_scrollKeyTime.Equals(TimeSpan.Zero))
            action();

        WindowManager.SelectedControl = this;

        _scrollKeyTime += gameTime.ElapsedGameTime;

        if (_isScrollingQuickly)
        {
            _timeSinceLastScroll += gameTime.ElapsedGameTime;

            if (_timeSinceLastScroll > TimeSpan.FromSeconds(SCROLL_REPEAT_TIME))
            {
                _timeSinceLastScroll = TimeSpan.Zero;
                action();
            }
        }

        if (_scrollKeyTime > TimeSpan.FromSeconds(FAST_SCROLL_TRIGGER_TIME) && !_isScrollingQuickly)
        {
            _isScrollingQuickly = true;
            _timeSinceLastScroll = TimeSpan.Zero;
        }
    }

    private void ScrollUp()
    {
        ViewTop -= ScrollStep;
        if (ViewTop < 0)
            ViewTop = 0;

        ScrollBar.RefreshButtonY(ViewTop);
    }

    private void ScrollDown()
    {
        int maxScrollPos = Math.Max(0, ContentHeight - Height + MARGIN * 2);
        ViewTop += ScrollStep;
        if (ViewTop > maxScrollPos)
            ViewTop = maxScrollPos;

        ScrollBar.RefreshButtonY(ViewTop);
    }

    public override void OnMouseScrolled()
    {
        if (ContentHeight <= Height - MARGIN * 2)
        {
            ViewTop = 0;
            return;
        }

        ViewTop -= Cursor.ScrollWheelValue * ScrollStep;

        if (ViewTop < 0)
        {
            ViewTop = 0;
            return;
        }

        int maxScrollPos = ContentHeight - Height + MARGIN * 2;
        if (ViewTop > maxScrollPos)
            ViewTop = maxScrollPos;

        base.OnMouseScrolled();
    }

    public override void Draw(GameTime gameTime)
    {
        DrawPanel();

        DrawChildren(gameTime);

        if (DrawBorders)
            DrawPanelBorders();

        ScrollBar.Draw(gameTime);
    }
}