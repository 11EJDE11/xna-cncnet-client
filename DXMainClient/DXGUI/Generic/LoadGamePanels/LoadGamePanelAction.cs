#nullable enable

using System;

namespace DTAClient.DXGUI.Generic.LoadGamePanels;

/// <summary>
/// A panel-specific button in the Load Game window's button row.
/// </summary>
public class LoadGamePanelAction
{
    /// <param name="name">Control name for the button, so a theme INI can reposition it.</param>
    /// <param name="text">Button caption.</param>
    /// <param name="action">Runs when the button is clicked.</param>
    /// <param name="isEnabled">Optional; the button is always clickable when this is null.</param>
    public LoadGamePanelAction(string name, string text, Action action, Func<bool>? isEnabled = null)
    {
        Name = name;
        Text = text;
        Action = action;
        IsEnabled = isEnabled;
    }

    public string Name { get; }

    public string Text { get; }

    public Action Action { get; }

    public Func<bool>? IsEnabled { get; }
}
