#nullable enable

using System.Collections.Generic;

namespace ClientGUI.Options;

/// <summary>
/// An option that is in one of a fixed number of states, each with its own list of effects.
/// The control that implements it decides how the states are presented and switched,
/// for example a check box has an unchecked and a checked state, and a drop-down
/// has one state per item.
/// </summary>
public interface IOption
{
    /// <summary>
    /// Gets the name of this option.
    /// </summary>
    string Name { get; }

    /// <summary>
    /// Gets the number of states this option has.
    /// </summary>
    int StateCount { get; }

    /// <summary>
    /// Gets the index of the state the option is currently in, or -1 if it is in none.
    /// </summary>
    int CurrentState { get; }

    /// <summary>
    /// Gets the effects of the given state. An invalid state has no effects.
    /// </summary>
    IEnumerable<OptionEffect> GetStateEffects(int state);
}
