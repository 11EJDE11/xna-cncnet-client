using System;

using ClientLogic.UI;

using Rampastring.XNAUI;

namespace ClientGUI
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

        public void ShowMessage(string title, string text, Action onOk)
        {
            var messageBox = new XNAMessageBox(windowManager, title, text, XNAMessageBoxButtons.OK);
            messageBox.OKClickedAction = _ => onOk();
            messageBox.Show();
        }

        public void Confirm(string title, string text, Action onYes)
        {
            var messageBox = XNAMessageBox.ShowYesNoDialog(windowManager, title, text);
            messageBox.YesClickedAction = _ => onYes();
        }

        public void Confirm(string title, string text, Action onYes, Action onNo)
        {
            var messageBox = XNAMessageBox.ShowYesNoDialog(windowManager, title, text);
            messageBox.YesClickedAction = _ => onYes();
            messageBox.NoClickedAction = _ => onNo();
        }
    }
}
