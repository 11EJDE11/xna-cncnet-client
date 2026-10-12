#nullable enable

using System.Collections.Generic;

using ClientCore;
using ClientCore.Extensions;

using Rampastring.Tools;

namespace ClientGUI.Options;

/// <summary>
/// A single change that an option makes while it is in a specific state,
/// such as writing a spawn.ini key or copying a file.
/// </summary>
/// <remarks>
/// Each effect is defined in its own INI section and reads its own properties
/// from it. The section's <c>Type</c> key selects the kind of effect.
/// Each kind of effect overrides only the methods for what it changes; the rest do nothing.
/// </remarks>
public abstract class OptionEffect
{
    private const string TYPE_KEY = "Type";

    /// <summary>
    /// Changes spawn.ini when a game is launched.
    /// </summary>
    public virtual void ApplyToSpawnIni(IniFile spawnIni) { }

    /// <summary>
    /// Changes the map (spawnmap.ini) when a game is launched.
    /// </summary>
    /// <param name="mapIni">The map INI file.</param>
    /// <param name="gameModeName">Name of the selected game mode, if there is one.</param>
    public virtual void ApplyToMap(IniFile mapIni, string? gameModeName) { }

    /// <summary>
    /// Gets the command line argument added to the game executable when a game is launched, if any.
    /// </summary>
    public virtual string? LaunchArgument => null;

    /// <summary>
    /// Whether everything the effect needs to be committed, such as a source file, exists.
    /// </summary>
    public virtual bool IsAvailable => true;

    /// <summary>
    /// Makes a lasting change, such as copying a file, which stays in place until the option
    /// switches to another state and <see cref="Revert"/> is called. Called when the option's
    /// state is committed: when user settings are saved, or when a game is launched for game options.
    /// </summary>
    public virtual void Commit() { }

    /// <summary>
    /// Undoes the lasting change made by <see cref="Commit"/>.
    /// </summary>
    public virtual void Revert() { }

    /// <summary>
    /// Reads an effect from the INI section with the given name.
    /// </summary>
    /// <param name="iniFile">The INI file that contains the effect section.</param>
    /// <param name="sectionName">The name of the effect section.</param>
    /// <param name="ownerName">The name of the option that uses the effect, for error messages.</param>
    public static OptionEffect FromIniSection(IniFile iniFile, string sectionName, string ownerName)
    {
        IniSection section = iniFile.GetSection(sectionName)
            ?? throw new ClientConfigurationException($"Option effect section [{sectionName}] used by {ownerName} does not exist.");

        string type = section.GetStringValue(TYPE_KEY, string.Empty);

        return type switch
        {
            SpawnIniEffect.TYPE_NAME => SpawnIniEffect.FromIniSection(section),
            MapCodeEffect.TYPE_NAME => MapCodeEffect.FromIniSection(section),
            FileEffect.TYPE_NAME => FileEffect.FromIniSection(section),
            LaunchArgumentEffect.TYPE_NAME => LaunchArgumentEffect.FromIniSection(section),
            _ => throw new ClientConfigurationException($"Option effect section [{sectionName}] used by {ownerName} has an invalid {TYPE_KEY} \"{type}\".")
        };
    }

    /// <summary>
    /// Reads a comma-separated list of effect section names into effects.
    /// </summary>
    /// <param name="iniFile">The INI file that contains the effect sections.</param>
    /// <param name="sectionNames">Comma-separated effect section names.</param>
    /// <param name="ownerName">The name of the option that uses the effects, for error messages.</param>
    public static List<OptionEffect> FromIniSections(IniFile iniFile, string sectionNames, string ownerName)
    {
        var effects = new List<OptionEffect>();

        foreach (string sectionName in sectionNames.SplitWithCleanup())
            effects.Add(FromIniSection(iniFile, sectionName, ownerName));

        return effects;
    }

    /// <summary>
    /// Gets a value of a key that an effect section must define.
    /// </summary>
    protected static string GetRequiredValue(IniSection section, string key)
    {
        string value = section.GetStringValue(key, string.Empty);

        if (string.IsNullOrWhiteSpace(value))
            throw new ClientConfigurationException($"Option effect section [{section.SectionName}] is missing the required key {key}.");

        return value;
    }
}
