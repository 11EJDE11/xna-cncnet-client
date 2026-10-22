using System;
using Microsoft.Xna.Framework.Input;
using Rampastring.XNAUI;
using Rampastring.XNAUI.XNAControls;

namespace DTAClient.DXGUI.Designer
{
    /// <summary>
    /// The designer's Key=Value box. Lets the key suggestion list handle keys such as Up, Down,
    /// Tab and Enter before the text box does.
    /// </summary>
    internal sealed class DesignerEditTextBox : XNATextBox
    {
        public DesignerEditTextBox(WindowManager windowManager) : base(windowManager)
        {
        }

        /// <summary>
        /// Called with every key press while the box is focused; returns true if it handled the key.
        /// </summary>
        public Func<Keys, bool> PreviewKeyPress { get; set; }

        protected override bool HandleKeyPress(Keys key)
        {
            if (PreviewKeyPress != null && PreviewKeyPress(key))
                return true;

            return base.HandleKeyPress(key);
        }
    }
}
