using ClientLogic.UI;

using Microsoft.Xna.Framework;

namespace DTAClient.DXGUI
{
    /// <summary>Converts between chat colours and XNA colours.</summary>
    public static class XnaChatColors
    {
        public static ChatColor ToChatColor(this Color color) => new(color.R, color.G, color.B, color.A);

        public static Color ToXnaColor(this ChatColor color) => new(color.R, color.G, color.B, color.A);
    }
}
