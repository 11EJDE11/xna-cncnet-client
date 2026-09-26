using ClientLogic.UI;

using Microsoft.Xna.Framework;

namespace DTAClient.DXGUI
{
    public static class NoticeSeverityColors
    {
        public static Color ToXnaColor(this NoticeSeverity severity) => severity switch
        {
            NoticeSeverity.Success => Color.LightGreen,
            NoticeSeverity.Warning => Color.Yellow,
            NoticeSeverity.Degraded => Color.Orange,
            NoticeSeverity.Error => Color.Red,
            _ => Color.White,
        };
    }
}
