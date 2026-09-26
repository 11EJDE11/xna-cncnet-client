using DTAClient.Domain.Multiplayer;
using Microsoft.Xna.Framework;

namespace DTAClient.DXGUI.Multiplayer
{
    public static class MultiplayerColorExtensions
    {
        public static Color ToXnaColor(this MultiplayerColor color) => new Color(color.R, color.G, color.B, 255);
    }
}
