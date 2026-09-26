using System.Collections.Generic;

using DTAClient.Domain.Multiplayer;

using Rampastring.Tools;

namespace ClientLogic.Launch;

/// <summary>
/// The result of <see cref="GameLaunchBuilder.Build"/>. Nothing is written to disk yet.
/// </summary>
public sealed class LaunchArtifacts
{
    public LaunchArtifacts(IniFile spawnIni, IniFile mapIni, PlayerHouseInfo[] houseInfos,
        IReadOnlyList<SupplementalMapFile> supplementalMapFiles)
    {
        SpawnIni = spawnIni;
        MapIni = mapIni;
        HouseInfos = houseInfos;
        SupplementalMapFiles = supplementalMapFiles;
    }

    /// <summary>The contents of spawn.ini.</summary>
    public IniFile SpawnIni { get; }

    /// <summary>
    /// The contents of spawnmap.ini. If <see cref="SupplementalMapFiles"/> is not empty, its
    /// [Basic] SupplementalFiles key lists all of them; update it if some can't be copied.
    /// </summary>
    public IniFile MapIni { get; }

    /// <summary>The randomized side, colour and start of every human player, then every AI player.</summary>
    public PlayerHouseInfo[] HouseInfos { get; }

    /// <summary>Files next to the map that must be copied to the game directory with the game.</summary>
    public IReadOnlyList<SupplementalMapFile> SupplementalMapFiles { get; }
}

/// <param name="SourcePath">The full path of the file next to the map.</param>
/// <param name="TargetFileName">The file name to copy it to in the game directory.</param>
public sealed record SupplementalMapFile(string SourcePath, string TargetFileName);
