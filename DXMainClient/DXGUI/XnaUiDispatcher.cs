using System;
using System.Threading;

using ClientLogic.UI;

using Rampastring.XNAUI;

namespace DTAClient.DXGUI
{
    /// <summary>
    /// Runs actions on the XNA game thread through <see cref="WindowManager.AddCallback"/>.
    /// </summary>
    public sealed class XnaUiDispatcher : IUiDispatcher
    {
        private readonly WindowManager windowManager;
        private readonly int uiThreadId;

        /// <summary>Must be created on the game thread.</summary>
        public XnaUiDispatcher(WindowManager windowManager)
        {
            this.windowManager = windowManager;
            uiThreadId = Environment.CurrentManagedThreadId;
        }

        public void Post(Action action) => windowManager.AddCallback(action);

        public bool CheckAccess() => Environment.CurrentManagedThreadId == uiThreadId;
    }
}
