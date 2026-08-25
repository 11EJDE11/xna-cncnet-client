#nullable enable

using System;
using System.Collections.Generic;

using ClientCore.Extensions;

using Rampastring.XNAUI;
using Rampastring.XNAUI.XNAControls;

namespace DTAClient.DXGUI.Generic.LoadGamePanels;

/// <summary>
/// One loadable list inside the Load Game window.
/// </summary>
public abstract class LoadGamePanel : XNAPanel
{
    protected LoadGamePanel(WindowManager windowManager) : base(windowManager) { }

    /// <summary>
    /// Raised when the selection changes, so the window can enable or disable its buttons.
    /// </summary>
    public event EventHandler? SelectionChanged;

    /// <summary>
    /// Raised just before a game process is started, so the window can close itself.
    /// </summary>
    public event EventHandler? LaunchRequested;

    /// <summary>Label for this panel's tab.</summary>
    public abstract string TabTitle { get; }

    /// <summary>Text for the window's launch button while this panel is showing.</summary>
    public virtual string LaunchButtonText => "Load".L10N("Client:Main:ButtonLoad");

    public abstract bool CanLaunch { get; }

    public abstract bool CanDelete { get; }

    /// <summary>
    /// Extra buttons shown while this panel is active. The window builds one button per action
    /// once and then pairs them up by index, so this has to return the same actions every time.
    /// </summary>
    public virtual IReadOnlyList<LoadGamePanelAction> ExtraActions => Array.Empty<LoadGamePanelAction>();

    /// <summary>Starts the selected item. Only called when <see cref="CanLaunch"/> is true.</summary>
    public abstract void Launch();

    /// <summary>Deletes the selected item, confirming first.</summary>
    public abstract void Delete();

    /// <summary>Re-reads whatever this panel lists. Called whenever the panel becomes visible.</summary>
    public abstract void Refresh();

    protected void OnSelectionChanged() => SelectionChanged?.Invoke(this, EventArgs.Empty);

    protected void OnLaunchRequested() => LaunchRequested?.Invoke(this, EventArgs.Empty);
}
