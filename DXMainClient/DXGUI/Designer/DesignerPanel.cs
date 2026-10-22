#nullable enable

using System;
using Microsoft.Xna.Framework;
using Rampastring.XNAUI;
using Rampastring.XNAUI.XNAControls;

namespace DTAClient.DXGUI.Designer
{
    /// <summary>
    /// The background of the designer's panel, which lets the overlay draw behind and over the panel's controls.
    /// </summary>
    internal sealed class DesignerPanel : XNAControl
    {
        public DesignerPanel(WindowManager windowManager) : base(windowManager)
        {
        }

        public Action? DrawBehindChildren { get; set; }

        public Action? DrawOverChildren { get; set; }

        public override void Draw(GameTime gameTime)
        {
            FillControlArea(DesignerStyle.PanelBackground);
            DrawBehindChildren?.Invoke();

            base.Draw(gameTime);

            DrawRectangle(new Rectangle(0, 0, Width, Height), DesignerStyle.Border);
            DrawOverChildren?.Invoke();
        }
    }
}
