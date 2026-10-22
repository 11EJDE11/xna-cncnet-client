#nullable enable

using Microsoft.Xna.Framework;
using Rampastring.XNAUI;
using Rampastring.XNAUI.XNAControls;

namespace DTAClient.DXGUI.Designer
{
    internal enum DesignerButtonStyle
    {
        Normal,
        Primary,
        Danger,

        /// <summary>
        /// A tab or toggle: highlighted while <see cref="DesignerButton.Checked"/>.
        /// </summary>
        Tab,

        /// <summary>
        /// Text only until hovered, for the header and the selection toolbar.
        /// </summary>
        Flat,
    }

    /// <summary>
    /// A button of the designer's own interface, drawn with plain colors so it looks the same in every theme.
    /// </summary>
    internal sealed class DesignerButton : XNAControl
    {
        public DesignerButton(WindowManager windowManager, string text, DesignerButtonStyle style = DesignerButtonStyle.Normal) : base(windowManager)
        {
            Text = text;
            Style = style;
            Name = "designerButton";
        }

        public DesignerButtonStyle Style { get; set; }

        public bool Checked { get; set; }

        /// <summary>
        /// Drawn faded and ignores clicks. Unlike Enabled, it stays drawn and shows its tool tip.
        /// </summary>
        public bool IsDisabled { get; set; }

        /// <summary>
        /// Shown while the cursor is on the button.
        /// </summary>
        public string? ToolTipText { get; set; }

        public override void OnLeftClick(InputEventArgs inputEventArgs)
        {
            if (IsDisabled)
            {
                inputEventArgs.Handled = true;
                return;
            }

            base.OnLeftClick(inputEventArgs);
        }

        public override void Draw(GameTime gameTime)
        {
            bool hover = IsActive && !IsDisabled;
            bool pressed = hover && WindowManager.Cursor.LeftDown;
            Color? background;
            Color border = DesignerStyle.Border;
            Color text = DesignerStyle.Text;

            switch (Style)
            {
                case DesignerButtonStyle.Primary:
                    background = pressed ? DesignerStyle.Accent : hover ? DesignerStyle.AccentHover : DesignerStyle.Accent;
                    border = background.Value;
                    text = Color.White;
                    break;
                case DesignerButtonStyle.Danger:
                    background = hover ? DesignerStyle.Danger : DesignerStyle.Button;
                    border = hover ? DesignerStyle.Danger : DesignerStyle.Border;
                    text = hover ? Color.White : DesignerStyle.Removed;
                    break;
                case DesignerButtonStyle.Tab:
                    background = Checked ? DesignerStyle.AccentMuted : hover ? DesignerStyle.ButtonHover : DesignerStyle.Button;
                    border = Checked ? DesignerStyle.Accent : DesignerStyle.Border;
                    text = Checked ? Color.White : DesignerStyle.Text;
                    break;
                case DesignerButtonStyle.Flat:
                    background = hover ? DesignerStyle.ButtonHover : Checked ? DesignerStyle.AccentMuted : (Color?)null;
                    border = Color.Transparent;
                    text = Checked ? Color.White : DesignerStyle.Text;
                    break;
                default:
                    background = pressed ? DesignerStyle.ButtonPressed : hover ? DesignerStyle.ButtonHover : DesignerStyle.Button;
                    break;
            }

            if (IsDisabled)
            {
                text = DesignerStyle.FaintText;
                background = Style == DesignerButtonStyle.Flat ? null : DesignerStyle.Button * 0.6f;
                border = Style == DesignerButtonStyle.Flat ? Color.Transparent : DesignerStyle.Separator;
            }

            var area = new Rectangle(0, 0, Width, Height);
            if (background.HasValue)
                FillRectangle(area, background.Value);

            if (border.A > 0)
                DrawRectangle(area, border);

            Vector2 size = Renderer.GetTextDimensions(Text, DesignerStyle.FONT);
            DrawString(Text, DesignerStyle.FONT, new Vector2((Width - (int)size.X) / 2, (Height - (int)size.Y) / 2 + (pressed ? 1 : 0)), text);

            base.Draw(gameTime);
        }

        /// <summary>
        /// The width the button needs for its text with some padding.
        /// </summary>
        public static int GetWidthFor(string text, int padding = 16) =>
            (int)Renderer.GetTextDimensions(text, DesignerStyle.FONT).X + padding;
    }
}
