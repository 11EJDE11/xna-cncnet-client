#nullable enable

using Rampastring.Tools;

namespace ClientGUI.Options;

/// <summary>
/// An effect that changes spawn.ini when a game is launched.
/// </summary>
public interface ISpawnIniEffect
{
    void ApplyToSpawnIni(IniFile spawnIni);
}
