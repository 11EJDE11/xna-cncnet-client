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
    public static bool HasEffect<TEffect>(this IOption option)
        => Enumerable.Range(0, option.StateCount).Any(state => option.GetStateEffects(state).OfType<TEffect>().Any());

    /// <summary>
    /// Checks whether all persistent effects of the given state can be applied.
    /// </summary>
    public static bool IsStateAvailable(this IOption option, int state)
        => option.GetStateEffects(state).OfType<IPersistentEffect>().All(effect => effect.IsAvailable);

    /// <summary>
    /// Applies the persistent effects of the given state, after reverting
    /// the persistent effects of all other states.
    /// </summary>
    public static void CommitPersistentEffects(this IOption option, int state)
    {
        for (int i = 0; i < option.StateCount; i++)
        {
            if (i == state)
                continue;

            foreach (IPersistentEffect effect in option.GetStateEffects(i).OfType<IPersistentEffect>())
                effect.Revert();
        }

        foreach (IPersistentEffect effect in option.GetStateEffects(state).OfType<IPersistentEffect>())
            effect.Apply();
    }

    public static void ApplyToSpawnIni(this IEnumerable<OptionEffect> effects, IniFile spawnIni)
    {
        foreach (ISpawnIniEffect effect in effects.OfType<ISpawnIniEffect>())
            effect.ApplyToSpawnIni(spawnIni);
    }

    public static void ApplyToMap(this IEnumerable<OptionEffect> effects, IniFile mapIni, string? gameModeName)
    {
        foreach (IMapCodeEffect effect in effects.OfType<IMapCodeEffect>())
            effect.ApplyToMap(mapIni, gameModeName);
    }

    public static IEnumerable<string> GetLaunchArguments(this IEnumerable<OptionEffect> effects)
        => effects.OfType<ILaunchArgumentEffect>().Select(effect => effect.Argument);
}
