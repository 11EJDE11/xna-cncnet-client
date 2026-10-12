using ClientGUI.Options;

namespace DTAClient.DXGUI;

// TODO split the logic between campaign/mp and clean up
/// <summary>
/// A game option of a game lobby or the campaign selector. What it changes in the
/// launched game is defined by the effects of its states (see <see cref="IOption"/>).
/// </summary>
public interface IGameSessionSetting : IOption
{
    /// <summary>Indicates whether this setting in its current state allows the game to be scored.</summary>
    bool AllowScoring { get; }

    /// <summary>Indicates whether this setting should be broadcast to the lobby.</summary>
    bool BroadcastToLobby { get; }

    /// <summary>
    /// Gets or sets the value of this setting.
    /// For checkboxes: 0 = unchecked/off, 1 = checked/on.
    /// For dropdowns: the selected index.
    /// </summary>
    int Value { get; set; }
}