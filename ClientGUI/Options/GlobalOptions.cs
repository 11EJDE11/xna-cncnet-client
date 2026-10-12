#nullable enable

using System.Collections.Generic;
using System.Linq;

using ClientGUI.Settings;

namespace ClientGUI.Options;

/// <summary>
/// Keeps track of the user settings in the options window, whose effects
/// apply to every game the client launches.
/// </summary>
/// <remarks>
/// These settings are local to each player. Their spawn.ini and map code effects
/// must not change anything that affects the game simulation, or multiplayer
/// games would desync.
/// </remarks>
public static class GlobalOptions
{
    private static readonly List<IUserSetting> settings = [];

    public static void Register(IUserSetting setting)
    {
        if (!settings.Contains(setting))
            settings.Add(setting);
    }

    /// <summary>
    /// Gets the effects of all registered settings, based on their saved values
    /// so that unsaved changes in the options window are ignored.
    /// </summary>
    public static IEnumerable<OptionEffect> GetEffects()
        => settings.SelectMany(setting => setting.GetStateEffects(setting.SavedState)).ToList();
}
