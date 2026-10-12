#nullable enable

namespace ClientGUI.Options;

/// <summary>
/// An effect that makes a lasting change, such as copying a file, which stays in place
/// until the option switches to another state and the change is reverted.
/// </summary>
/// <remarks>
/// Persistent effects are applied when the option's state is committed:
/// when user settings are saved, or when a game is launched for game options.
/// </remarks>
public interface IPersistentEffect
{
    /// <summary>
    /// Whether everything the effect needs, such as its source file, exists.
    /// </summary>
    bool IsAvailable { get; }

    void Apply();

    void Revert();
}
