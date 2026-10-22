using System;
using System.Globalization;
using Microsoft.Xna.Framework;
using Rampastring.XNAUI;
using Rampastring.XNAUI.XNAControls;

namespace DTAClient.DXGUI.Designer
{
    /// <summary>
    /// A color picker for the designer's edit box: a palette of swatches and red, green, blue and alpha
    /// sliders. Reports changes while dragging and commits once the mouse button is released.
    /// </summary>
    internal sealed class DesignerColorPicker : XNAControl
    {
        private const int SWATCH_COLUMNS = 16;
        private const int SWATCH_ROWS = 4;
        private const int SWATCH_HEIGHT = 14;
        private const int SLIDER_HEIGHT = 12;
        private const int SLIDER_SPACING = 4;
        private const int LABEL_WIDTH = 14;
        private const int PREVIEW_WIDTH = 40;
        private const int GRADIENT_STEPS = 32;

        private static readonly string[] ChannelNames = { "R", "G", "B", "A" };

        private readonly Color[] swatches = CreateSwatches();
        private readonly int[] channels = { 255, 255, 255, 255 };
        private bool hasAlpha;
        private int draggedSlider = -1;
        private bool changedSinceCommit;

        public DesignerColorPicker(WindowManager windowManager) : base(windowManager)
        {
        }

        /// <summary>
        /// Raised while the color changes, with the value as INI text.
        /// </summary>
        public event EventHandler<string> ColorChanged;

        /// <summary>
        /// Raised when a change is finished: a swatch was clicked or a slider released.
        /// </summary>
        public event EventHandler<string> ColorCommitted;

        public static int PreferredHeight => SWATCH_ROWS * SWATCH_HEIGHT + 6 + 4 * (SLIDER_HEIGHT + SLIDER_SPACING) + 4;

        /// <summary>
        /// Shows a value in R,G,B or R,G,B,A form. Returns false if it isn't a color.
        /// </summary>
        public bool SetValue(string value)
        {
            string[] parts = (value ?? string.Empty).Split(',');
            if (parts.Length != 3 && parts.Length != 4)
                return false;

            var parsed = new int[4] { 0, 0, 0, 255 };
            for (int i = 0; i < parts.Length; i++)
            {
                if (!int.TryParse(parts[i].Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out parsed[i]))
                    return false;

                parsed[i] = MathHelper.Clamp(parsed[i], 0, 255);
            }

            Array.Copy(parsed, channels, 4);
            hasAlpha = parts.Length == 4;
            return true;
        }

        public string GetValue() => hasAlpha || channels[3] < 255
            ? string.Join(",", channels)
            : string.Join(",", channels[0], channels[1], channels[2]);

        private Color CurrentColor => new Color(channels[0], channels[1], channels[2], channels[3]);

        private static Color[] CreateSwatches()
        {
            var colors = new Color[SWATCH_COLUMNS * SWATCH_ROWS];

            for (int column = 0; column < SWATCH_COLUMNS; column++)
            {
                // Last column greys, the others hues at four brightness levels
                for (int row = 0; row < SWATCH_ROWS; row++)
                {
                    if (column == SWATCH_COLUMNS - 1)
                    {
                        int grey = 255 - row * 80;
                        colors[row * SWATCH_COLUMNS + column] = new Color(grey, grey, grey);
                        continue;
                    }

                    float hue = column / (float)(SWATCH_COLUMNS - 1);
                    float value = 1f - row * 0.22f;
                    float saturation = row == 0 ? 0.55f : 0.85f;
                    colors[row * SWATCH_COLUMNS + column] = FromHsv(hue, saturation, value);
                }
            }

            return colors;
        }

        private static Color FromHsv(float hue, float saturation, float value)
        {
            float h = hue * 6f;
            int sector = (int)Math.Floor(h) % 6;
            float f = h - (float)Math.Floor(h);
            float p = value * (1 - saturation);
            float q = value * (1 - f * saturation);
            float t = value * (1 - (1 - f) * saturation);

            switch (sector)
            {
                case 0: return new Color(value, t, p);
                case 1: return new Color(q, value, p);
                case 2: return new Color(p, value, t);
                case 3: return new Color(p, q, value);
                case 4: return new Color(t, p, value);
                default: return new Color(value, p, q);
            }
        }

        private int SwatchWidth => (Width - PREVIEW_WIDTH - 6) / SWATCH_COLUMNS;

        private Rectangle SwatchRectangle(int index) =>
            new Rectangle((index % SWATCH_COLUMNS) * SwatchWidth, (index / SWATCH_COLUMNS) * SWATCH_HEIGHT, SwatchWidth - 1, SWATCH_HEIGHT - 1);

        // A whole number of gradient steps wide, so the drawn gradient and the clickable area match
        private Rectangle SliderRectangle(int channel) =>
            new Rectangle(LABEL_WIDTH, SWATCH_ROWS * SWATCH_HEIGHT + 6 + channel * (SLIDER_HEIGHT + SLIDER_SPACING),
                (Width - LABEL_WIDTH - 40) / GRADIENT_STEPS * GRADIENT_STEPS, SLIDER_HEIGHT);

        public override void Update(GameTime gameTime)
        {
            base.Update(gameTime);

            var cursor = WindowManager.Cursor;
            Point point = GetCursorPoint();

            if (draggedSlider > -1)
            {
                if (cursor.LeftDown)
                {
                    SetChannelFromCursor(draggedSlider, point.X);
                }
                else
                {
                    draggedSlider = -1;
                    Commit();
                }

                return;
            }

            if (!cursor.LeftPressedDown || !new Rectangle(0, 0, Width, Height).Contains(point))
                return;

            for (int i = 0; i < swatches.Length; i++)
            {
                if (SwatchRectangle(i).Contains(point))
                {
                    channels[0] = swatches[i].R;
                    channels[1] = swatches[i].G;
                    channels[2] = swatches[i].B;
                    changedSinceCommit = true;
                    ColorChanged?.Invoke(this, GetValue());
                    Commit();
                    return;
                }
            }

            for (int channel = 0; channel < 4; channel++)
            {
                Rectangle slider = SliderRectangle(channel);
                if (new Rectangle(slider.X, slider.Y - 2, slider.Width, slider.Height + 4).Contains(point))
                {
                    draggedSlider = channel;
                    SetChannelFromCursor(channel, point.X);
                    return;
                }
            }
        }

        private void SetChannelFromCursor(int channel, int x)
        {
            Rectangle slider = SliderRectangle(channel);
            int value = MathHelper.Clamp((int)Math.Round((x - slider.X) * 255.0 / Math.Max(1, slider.Width - 1)), 0, 255);
            if (value == channels[channel])
                return;

            channels[channel] = value;
            if (channel == 3)
                hasAlpha = true;

            changedSinceCommit = true;
            ColorChanged?.Invoke(this, GetValue());
        }

        private void Commit()
        {
            if (!changedSinceCommit)
                return;

            changedSinceCommit = false;
            ColorCommitted?.Invoke(this, GetValue());
        }

        public override void Draw(GameTime gameTime)
        {
            FillControlArea(new Color(16, 18, 22, 250));

            for (int i = 0; i < swatches.Length; i++)
                FillRectangle(SwatchRectangle(i), swatches[i]);

            // Preview: the color over a light and a dark half, so alpha shows
            var preview = new Rectangle(Width - PREVIEW_WIDTH, 0, PREVIEW_WIDTH, SWATCH_ROWS * SWATCH_HEIGHT - 1);
            FillRectangle(new Rectangle(preview.X, preview.Y, preview.Width, preview.Height / 2), Color.White);
            FillRectangle(new Rectangle(preview.X, preview.Y + preview.Height / 2, preview.Width, preview.Height - preview.Height / 2), Color.Black);
            FillRectangle(preview, CurrentColor);
            DrawRectangle(preview, Color.Gray);

            for (int channel = 0; channel < 4; channel++)
            {
                Rectangle slider = SliderRectangle(channel);
                DrawString(ChannelNames[channel], 0, new Vector2(0, slider.Y - 3), Color.LightGray);

                int stepWidth = slider.Width / GRADIENT_STEPS;
                for (int step = 0; step < GRADIENT_STEPS; step++)
                {
                    var color = new int[4];
                    Array.Copy(channels, color, 4);
                    color[channel] = step * 255 / (GRADIENT_STEPS - 1);
                    if (channel != 3)
                        color[3] = 255;

                    FillRectangle(new Rectangle(slider.X + step * stepWidth, slider.Y, stepWidth, slider.Height),
                        new Color(color[0], color[1], color[2], color[3]));
                }

                DrawRectangle(slider, Color.Gray);

                int markerX = slider.X + channels[channel] * (slider.Width - 1) / 255;
                FillRectangle(new Rectangle(markerX - 1, slider.Y - 2, 3, slider.Height + 4), Color.White);
                DrawString(channels[channel].ToString(CultureInfo.InvariantCulture), 0, new Vector2(Width - 34, slider.Y - 3), Color.LightGray);
            }

            base.Draw(gameTime);
        }
    }
}
