#nullable enable

using System;
using System.Collections.Generic;

using ClientCore;
using ClientCore.Extensions;

using DTAClient.Domain.Multiplayer;
using DTAClient.DXGUI.Multiplayer.GameLobby;

using Rampastring.Tools;

namespace DTAClient.DXGUI.Generic;

/// <summary>
/// A set of effects that a game option control applies to the game session
/// while it is in a specific state (a check box being checked or unchecked,
/// or a specific drop-down item being selected).
/// </summary>
public class GameOptionEffects
{
    /// <summary>
    /// Prefix of keys in an effects section that are written into spawn.ini.
    /// For example, <c>SpawnIni.Crates=False</c> writes <c>Crates=False</c>.
    /// </summary>
    private const string SPAWN_INI_KEY_PREFIX = "SpawnIni.";

    private const string MAP_CODE_KEY = "MapCode";

    private const string SPAWN_INI_SECTION = "Settings";

    private readonly List<KeyValuePair<string, string>> spawnIniOptions = [];

    private readonly List<string> mapCodePaths = [];

    public bool AffectsSpawnIni => spawnIniOptions.Count > 0;

    public bool AffectsMapCode => mapCodePaths.Count > 0;

    /// <summary>
    /// Adds a key that is written into the [Settings] section of spawn.ini.
    /// </summary>
    public GameOptionEffects AddSpawnIniOption(string key, string value)
    {
        spawnIniOptions.Add(new KeyValuePair<string, string>(key, value));
        return this;
    }

    /// <summary>
    /// Adds a map code INI file that is applied to spawnmap.ini.
    /// </summary>
    public GameOptionEffects AddMapCode(string customIniPath)
    {
        mapCodePaths.Add(customIniPath);
        return this;
    }

    /// <summary>
    /// Appends all effects of another effect set to this one.
    /// </summary>
    public GameOptionEffects Add(GameOptionEffects? other)
    {
        if (other != null)
        {
            spawnIniOptions.AddRange(other.spawnIniOptions);
            mapCodePaths.AddRange(other.mapCodePaths);
        }

        return this;
    }

    public void ApplySpawnIniCode(IniFile spawnIni)
    {
        foreach (KeyValuePair<string, string> option in spawnIniOptions)
            spawnIni.SetStringValue(SPAWN_INI_SECTION, option.Key, option.Value);
    }

    public void ApplyMapCode(IniFile mapIni, GameMode? gameMode)
    {
        foreach (string customIniPath in mapCodePaths)
            MapCodeHelper.ApplyMapCode(mapIni, customIniPath, gameMode);
    }

    /// <summary>
    /// Reads an effect set from an INI section.
    /// </summary>
    /// <param name="iniFile">The INI file that contains the section.</param>
    /// <param name="sectionName">The name of the effects section. An empty name results in no effects.</param>
    /// <param name="ownerName">The name of the control that references the section, for error messages.</param>
    public static GameOptionEffects FromIniSection(IniFile iniFile, string? sectionName, string ownerName)
    {
        var effects = new GameOptionEffects();

        if (string.IsNullOrWhiteSpace(sectionName))
            return effects;

        IniSection? section = iniFile.GetSection(sectionName)
            ?? throw new ClientConfigurationException($"Game option effects section [{sectionName}] referenced by {ownerName} does not exist.");

        foreach (KeyValuePair<string, string> kvp in section.Keys)
        {
            if (kvp.Key.StartsWith(SPAWN_INI_KEY_PREFIX, StringComparison.Ordinal))
            {
                string spawnIniKey = kvp.Key.Substring(SPAWN_INI_KEY_PREFIX.Length);
                if (spawnIniKey.Length == 0)
                    throw new ClientConfigurationException($"Game option effects section [{sectionName}] has a {SPAWN_INI_KEY_PREFIX} key without a spawn.ini key name.");

                effects.AddSpawnIniOption(spawnIniKey, kvp.Value);
            }
            else if (kvp.Key == MAP_CODE_KEY)
            {
                foreach (string customIniPath in kvp.Value.SplitWithCleanup())
                    effects.AddMapCode(customIniPath);
            }
            else
            {
                Logger.Log($"GameOptionEffects: Unknown key {kvp.Key} in effects section [{sectionName}] referenced by {ownerName}.");
            }
        }

        return effects;
    }
}