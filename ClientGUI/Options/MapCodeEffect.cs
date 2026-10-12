#nullable enable

using ClientCore;

using Rampastring.Tools;

namespace ClientGUI.Options;

/// <summary>
/// Applies a map code INI file to the map.
/// </summary>
public class MapCodeEffect : OptionEffect
{
    public const string TYPE_NAME = "MapCode";

    public MapCodeEffect(string path)
    {
        Path = path;
    }

    /// <summary>
    /// Path of the map code INI file, relative to the game directory.
    /// </summary>
    public string Path { get; }

    public override void ApplyToMap(IniFile mapIni, string? gameModeName) => MapCodeHelper.ApplyMapCode(mapIni, Path, gameModeName);

    public static MapCodeEffect FromIniSection(IniSection section) => new(GetRequiredValue(section, "Path"));
}
