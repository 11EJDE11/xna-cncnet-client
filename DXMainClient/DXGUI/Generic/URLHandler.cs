#nullable enable
using ClientGUI;

using ClientLogic.UI;

using Rampastring.XNAUI;

namespace DTAClient.DXGUI.Generic
{
    public static class URLHandler
    {
        /// <summary>
        /// Checks whether a URL is safe before opening it, asking for confirmation in an XNAMessageBox otherwise.
        /// </summary>
        public static void OpenLink(WindowManager wm, string url) => LinkOpener.OpenLink(url, new XnaDialogService(wm));
    }
}
