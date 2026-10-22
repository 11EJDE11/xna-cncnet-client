#nullable enable

using System;
using Microsoft.Xna.Framework;
using Rampastring.XNAUI;
using Rampastring.XNAUI.XNAControls;

namespace DTAClient.DXGUI.Designer
{
    /// <summary>
    /// A row of a <see cref="DesignerListBox"/>: a name in the first column, an optional value in the
    /// second, a colored marker at the left edge and an indent for tree rows. The item's Tag stays free
    /// for what the row stands for.
    /// </summary>
    internal sealed class DesignerListItem : XNAListBoxItem
    {
        public DesignerListItem(string text, Color textColor, string? value = null) : base(text, textColor)
        {
            Value = value;
        }

        public string? Value { get; set; }

        public Color ValueColor { get; set; } = DesignerStyle.DimText;

        /// <summary>
        /// The color of the marker at the left edge, or null for none.
        /// </summary>
        public Color? Marker { get; set; }

        public int Indent { get; set; }

        /// <summary>
        /// A heading that groups the rows below it.
        /// </summary>
        public bool IsGroupHeading { get; set; }

        /// <summary>
        /// Shows a remove button at the end of the row while it is hovered or selected.
        /// </summary>
        public bool Removable { get; set; }
    }

    /// <summary>
    /// The designer's lists: rows of two columns with a hover highlight, drawn with plain colors.
    /// </summary>
    internal sealed class DesignerListBox : XNAListBox
    {
        private const int INDENT_WIDTH = 12;
        private const int MARKER_WIDTH = 3;
        private const int REMOVE_WIDTH = 18;

        public DesignerListBox(WindowManager windowManager) : base(windowManager)
        {
            FontIndex = DesignerStyle.FONT;
            LineHeight = 17;
            AllowMultiLineItems = false;
            FocusColor = DesignerStyle.RowSelected;
            BackgroundTexture = AssetLoader.CreateTexture(DesignerStyle.Surface, 1, 1);
            PanelBackgroundDrawMode = PanelBackgroundImageDrawMode.STRETCHED;
            BorderColor = DesignerStyle.Border;
        }

        /// <summary>
        /// Where the second column starts, as a share of the width.
        /// </summary>
        public float ValueColumn { get; set; } = 0.42f;

        /// <summary>
        /// Raised when the remove button of a <see cref="DesignerListItem.Removable"/> row is clicked.
        /// </summary>
        public event EventHandler<DesignerListItem>? RemoveClicked;

        private int RowWidth => Width - 2 - (ScrollBar.IsDrawn() && EnableScrollbar ? ScrollBar.Width : 0);

        // The scroll bar takes the clicks at the right edge even while it is hidden, so the button stays clear of it
        private int RemoveButtonRight => Width - 2 - (EnableScrollbar ? ScrollBar.Width : 0);

        private bool IsCursorOnRemoveButton()
        {
            Point cursor = GetCursorPoint();
            return HoveredIndex > -1 && HoveredIndex < Items.Count && Items[HoveredIndex] is DesignerListItem { Removable: true } &&
                cursor.X >= RemoveButtonRight - REMOVE_WIDTH && cursor.X < RemoveButtonRight;
        }

        // The click that follows a press on a remove button isn't a click on the row
        private bool pressedRemoveButton;

        public override void OnMouseLeftDown(InputEventArgs inputEventArgs)
        {
            pressedRemoveButton = IsCursorOnRemoveButton();
            if (!pressedRemoveButton)
            {
                base.OnMouseLeftDown(inputEventArgs);
                return;
            }

            inputEventArgs.Handled = true;
            RemoveClicked?.Invoke(this, (DesignerListItem)Items[HoveredIndex]);
        }

        public override void OnLeftClick(InputEventArgs inputEventArgs)
        {
            if (pressedRemoveButton)
            {
                inputEventArgs.Handled = true;
                return;
            }

            base.OnLeftClick(inputEventArgs);
        }

        public override void OnDoubleLeftClick(InputEventArgs inputEventArgs)
        {
            if (pressedRemoveButton)
            {
                inputEventArgs.Handled = true;
                return;
            }

            base.OnDoubleLeftClick(inputEventArgs);
        }

        protected override void DrawListBoxItem(int index, int y)
        {
            if (Items[index] is not DesignerListItem item)
            {
                base.DrawListBoxItem(index, y);
                return;
            }

            int width = RowWidth;
            var row = new Rectangle(1, y, width, LineHeight);

            if (index == SelectedIndex)
                FillRectangle(row, FocusColor);
            else if (index == HoveredIndex && item.Selectable)
                FillRectangle(row, DesignerStyle.RowHover);

            if (item.Marker.HasValue)
                FillRectangle(new Rectangle(2, y + 3, MARKER_WIDTH, LineHeight - 6), item.Marker.Value);

            int x = TextBorderDistance + MARKER_WIDTH + 4 + item.Indent * INDENT_WIDTH;
            int textY = y + item.TextYPadding;

            if (item.IsGroupHeading)
            {
                DrawString(item.Text, FontIndex, new Vector2(x, textY), item.TextColor);
                FillRectangle(new Rectangle(x, y + LineHeight - 1, width - x - 4, 1), DesignerStyle.Separator);
                return;
            }

            // The remove button's space is kept free on every removable row, so the values don't move when it shows
            int textRight = item.Removable ? RemoveButtonRight - REMOVE_WIDTH : width;
            int valueX = item.Value == null ? textRight : Math.Max(x + 40, (int)(width * ValueColumn));
            DrawString(Renderer.GetStringWithLimitedWidth(item.Text, FontIndex, valueX - x - 6), FontIndex, new Vector2(x, textY), item.TextColor);

            if (item.Value != null)
            {
                DrawString(Renderer.GetStringWithLimitedWidth(item.Value, FontIndex, textRight - valueX - 4), FontIndex,
                    new Vector2(valueX, textY), item.ValueColor);
            }

            if (item.Removable && (index == HoveredIndex || index == SelectedIndex))
            {
                var button = new Rectangle(RemoveButtonRight - REMOVE_WIDTH + 2, y + 2, REMOVE_WIDTH - 4, LineHeight - 4);
                bool onButton = index == HoveredIndex && IsCursorOnRemoveButton();
                if (onButton)
                    FillRectangle(button, DesignerStyle.Danger);

                Vector2 size = Renderer.GetTextDimensions("x", FontIndex);
                DrawString("x", FontIndex, new Vector2(button.X + (button.Width - (int)size.X) / 2, button.Y + (button.Height - (int)size.Y) / 2),
                    onButton ? Color.White : DesignerStyle.Removed);
            }
        }
    }
}
