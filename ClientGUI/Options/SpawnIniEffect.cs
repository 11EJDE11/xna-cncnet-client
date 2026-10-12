#nullable enable

using Rampastring.Tools;

namespace ClientGUI.Options;

/// <summary>
/// Writes a key into spawn.ini.
/// </summary>
public class SpawnIniEffect : OptionEffect
{
    public const string TYPE_NAME = "SpawnIni";

    private const string DEFAULT_SECTION = "Settings";

    public SpawnIniEffect(string key, string value)
        : this(DEFAULT_SECTION, key, value)
    {
    }

    public SpawnIniEffect(string section, string key, string value)
    {
        Section = section;
        Key = key;
        Value = value;
    }

    public string Section { get; }

    public string Key { get; }

    public string Value { get; }

    public override void ApplyToSpawnIni(IniFile spawnIni) => spawnIni.SetStringValue(Section, Key, Value);

    public static SpawnIniEffect FromIniSection(IniSection section)
        => new(section.GetStringValue("Section", DEFAULT_SECTION),
            GetRequiredValue(section, "Key"),
            section.GetStringValue("Value", string.Empty));
}
