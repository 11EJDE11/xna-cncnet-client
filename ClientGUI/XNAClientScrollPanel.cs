using Rampastring.XNAUI;
using Rampastring.XNAUI.XNAControls;

namespace ClientGUI
{
    /// <summary>
    /// A scroll panel that can be created from INI. Exposes its content panel so that
    /// INI-defined controls can be placed inside it.
    /// </summary>
    public class XNAClientScrollPanel : XNAScrollPanel
    {
        public XNAClientScrollPanel(WindowManager windowManager) : base(windowManager)
        {
        }

        /// <summary>
        /// The panel that holds the scrolled content.
        /// </summary>
        public XNAPanel Content => ContentPanel;
    }
}
