using System;
using System.Collections.Generic;

using ClientCore.Extensions;

using Rampastring.XNAUI;
using Rampastring.XNAUI.XNAControls;

namespace DTAClient.DXGUI.Generic.LoadGamePanels
{
    /// <summary>
    /// One tab of the Load Game window. Each panel owns a list of things that can be loaded and
    /// knows how to start one; the window owns the Load / Delete / Cancel buttons and drives the
    /// active panel through this contract, so the button row stays put as the user changes tabs.
    /// </summary>
    public abstract class LoadGamePanel : XNAPanel
    {
        protected LoadGamePanel(WindowManager windowManager) : base(windowManager)
        {
        }

        /// <summary>
        /// Raised when the selection changes, so the window can enable or disable its buttons.
        /// </summary>
        public event EventHandler SelectionChanged;

        /// <summary>
        /// Raised just before a game process is started, so the window can close itself.
        /// </summary>
        public event EventHandler LaunchRequested;

        /// <summary>Label for this panel's tab.</summary>
        public abstract string TabTitle { get; }

        /// <summary>Text for the window's launch button while this panel is showing.</summary>
        public virtual string LaunchButtonText => "Load".L10N("Client:Main:ButtonLoad");

        public abstract bool CanLaunch { get; }

        public abstract bool CanDelete { get; }

        /// <summary>Starts the selected item. Only called when <see cref="CanLaunch"/> is true.</summary>
        public abstract void Launch();

        /// <summary>Deletes the selected item, confirming first.</summary>
        public abstract void Delete();

        /// <summary>Re-reads whatever this panel lists. Called whenever the panel becomes visible.</summary>
        public abstract void Refresh();

        /// <summary>
        /// Extra buttons this panel wants in the window's button row, shown only while the panel
        /// is the active tab. They live on the window rather than inside the panel so every button
        /// in the dialog sits on one row.
        /// </summary>
        public virtual IReadOnlyList<LoadGamePanelAction> ExtraActions => EmptyActions;

        private static readonly LoadGamePanelAction[] EmptyActions = new LoadGamePanelAction[0];

        protected void OnSelectionChanged() => SelectionChanged?.Invoke(this, EventArgs.Empty);

        protected void OnLaunchRequested() => LaunchRequested?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>
    /// A panel-specific button in the Load Game window's button row.
    /// </summary>
    public class LoadGamePanelAction
    {
        public LoadGamePanelAction(string text, Action action, Func<bool> isEnabled = null)
        {
            Text = text;
            Action = action;
            IsEnabled = isEnabled;
        }

        public string Text { get; }

        public Action Action { get; }

        /// <summary>Optional; the button is always clickable when this is null.</summary>
        public Func<bool> IsEnabled { get; }
    }
}
