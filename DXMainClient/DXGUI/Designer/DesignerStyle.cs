#nullable enable

using Microsoft.Xna.Framework;

namespace DTAClient.DXGUI.Designer
{
    /// <summary>
    /// The colors of the designer's own controls, which don't follow the theme being edited.
    /// </summary>
    internal static class DesignerStyle
    {
        public const int FONT = 0;

        public static readonly Color PanelBackground = new Color(15, 17, 21);
        public static readonly Color HeaderBackground = new Color(27, 31, 38);
        public static readonly Color Surface = new Color(9, 10, 13);
        public static readonly Color Border = new Color(54, 60, 70);
        public static readonly Color Separator = new Color(38, 43, 51);

        public static readonly Color Button = new Color(38, 43, 51);
        public static readonly Color ButtonHover = new Color(56, 63, 75);
        public static readonly Color ButtonPressed = new Color(30, 34, 41);
        public static readonly Color Accent = new Color(78, 148, 255);
        public static readonly Color AccentHover = new Color(104, 166, 255);
        public static readonly Color AccentMuted = new Color(36, 62, 104);
        public static readonly Color Danger = new Color(214, 84, 76);
        public static readonly Color DangerHover = new Color(234, 106, 98);

        public static readonly Color RowHover = new Color(30, 35, 43);
        public static readonly Color RowSelected = new Color(36, 62, 104);

        public static readonly Color Text = new Color(228, 231, 236);
        public static readonly Color DimText = new Color(138, 146, 158);
        public static readonly Color FaintText = new Color(92, 99, 110);
        public static readonly Color Title = new Color(255, 214, 64);

        public static readonly Color ThemeKey = new Color(132, 220, 140);
        public static readonly Color InheritedKey = new Color(150, 156, 164);
        public static readonly Color PendingKey = new Color(255, 214, 64);
        public static readonly Color UnsetKey = new Color(160, 190, 220);
        public static readonly Color Removed = new Color(240, 110, 100);
        public static readonly Color Warning = new Color(255, 150, 96);
    }
}
