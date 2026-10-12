#nullable enable

namespace ClientGUI.Options;

/// <summary>
/// An effect that adds a command line argument to the game executable when a game is launched.
/// </summary>
public interface ILaunchArgumentEffect
{
    string Argument { get; }
}
