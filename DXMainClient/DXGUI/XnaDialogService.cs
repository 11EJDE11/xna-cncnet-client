using System;

using ClientGUI;

using ClientLogic.UI;

using Rampastring.XNAUI;

namespace DTAClient.DXGUI
{
    /// <summary>
    /// Shows message boxes with <see cref="XNAMessageBox"/>.
    /// </summary>
    public sealed class XnaDialogService : IDialogService
    {
        private readonly WindowManager windowManager;

        public XnaDialogService(WindowManager windowManager)
        {
            this.windowManager = windowManager;
        }

        public void ShowMessage(string title, string text) => XNAMessageBox.Show(windowManager, title, text);

        public void Confirm(string title, string text, Action onYes)
        {
            var messageBox = XNAMessageBox.ShowYesNoDialog(windowManager, title, text);
            messageBox.YesClickedAction = _ => onYes();
        }
    }
}
