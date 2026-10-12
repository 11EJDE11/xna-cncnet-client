#nullable enable

using System.Collections.Generic;
using System.Linq;

using Rampastring.Tools;

namespace ClientGUI.Options;

public static class OptionExtensions
{
    /// <summary>
    /// Gets the effects of the option's current state.
    /// </summary>
    public static IEnumerable<OptionEffect> GetCurrentEffects(this IOption option)
        => option.GetStateEffects(option.CurrentState);

    /// <summary>
    /// Checks whether any state of the option has an effect of the given kind.
    /// </summary>
    public static bool HasEffect<TEffect>(this IOption option) where TEffect : OptionEffect
        => Enumerable.Range(0, option.StateCount).Any(state => option.GetStateEffects(state).OfType<TEffect>().Any());

    /// <summary>
    /// Checks whether all effects of the given state can be committed.
    /// </summary>
    public static bool IsStateAvailable(this IOption option, int state)
        => option.GetStateEffects(state).All(effect => effect.IsAvailable);

    /// <summary>
    /// Commits the effects of the given state, after reverting the effects of all other states.
    /// </summary>
    public static void CommitPersistentEffects(this IOption option, int state)
    {
        for (int i = 0; i < option.StateCount; i++)
        {
            if (i == state)
                continue;

            foreach (OptionEffect effect in option.GetStateEffects(i))
                effect.Revert();
        }

        foreach (OptionEffect effect in option.GetStateEffects(state))
            effect.Commit();
    }

    public static void ApplyToSpawnIni(this IEnumerable<OptionEffect> effects, IniFile spawnIni)
    {
        foreach (OptionEffect effect in effects)
            effect.ApplyToSpawnIni(spawnIni);
    }

    public static void ApplyToMap(this IEnumerable<OptionEffect> effects, IniFile mapIni, string? gameModeName)
    {
        foreach (OptionEffect effect in effects)
            effect.ApplyToMap(mapIni, gameModeName);
    }

    public static IEnumerable<string> GetLaunchArguments(this IEnumerable<OptionEffect> effects)
        => effects.Select(effect => effect.LaunchArgument).OfType<string>();
}
