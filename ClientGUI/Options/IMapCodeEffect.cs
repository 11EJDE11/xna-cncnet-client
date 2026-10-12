#nullable enable

using Rampastring.Tools;

namespace ClientGUI.Options;

/// <summary>
/// An effect that changes the map (spawnmap.ini) when a game is launched.
/// </summary>
public interface IMapCodeEffect
{
    /// <param name="mapIni">The map INI file.</param>
    /// <param name="gameModeName">Name of the selected game mode, if there is one.</param>
    void ApplyToMap(IniFile mapIni, string? gameModeName);
}
