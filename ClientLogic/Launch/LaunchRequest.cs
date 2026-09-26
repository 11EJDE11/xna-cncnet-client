using System.Collections.Generic;

using DTAClient.Domain.Multiplayer;
using DTAClient.DXGUI;

using Rampastring.Tools;

namespace ClientLogic.Launch;

/// <summary>
/// Everything a game lobby knows that the spawn files of a game launch depend on.
/// See <see cref="GameLaunchBuilder"/>.
/// </summary>
public sealed class LaunchRequest
{
    /// <summary>The human players, in lobby order. Co-op games set their <see cref="PlayerInfo.TeamId"/> to 1.</summary>
    public List<PlayerInfo> Players { get; init; }

    /// <summary>The AI players, in lobby order.</summary>
    public List<PlayerInfo> AIPlayers { get; init; }

    /// <summary>The name of the player whose spawn.ini is built.</summary>
    public string LocalPlayerName { get; init; }

    /// <summary>
    /// For each entry of <see cref="Players"/>, the address written to its spawn.ini section.
    /// The local player's entry is not used.
    /// </summary>
    public IReadOnlyList<string> PlayerIPAddresses { get; init; }

    public int RandomSeed { get; init; }

    public GameModeMap GameModeMap { get; init; }

    public bool IsMultiplayer { get; init; }

    /// <summary>The check-box game options, in lobby order.</summary>
    public IReadOnlyList<IGameSessionSetting> CheckBoxes { get; init; }

    /// <summary>The drop-down game options, in lobby order.</summary>
    public IReadOnlyList<IGameSessionSetting> DropDowns { get; init; }

    public List<MultiplayerColor> MPColors { get; init; }

    public int SideCount { get; init; }

    public List<int[]> RandomSelectors { get; init; }

    public int RandomSelectorCount { get; init; }

    public List<TeamStartMapping> TeamStartMappings { get; init; }

    public bool RemoveStartingLocations { get; init; }

    /// <summary>The [ForcedSpawnIniOptions] of GameOptions.ini, in file order.</summary>
    public IReadOnlyList<KeyValuePair<string, string>> ForcedSpawnIniOptions { get; init; }

    /// <summary>
    /// Lobby-specific spawn.ini values (frame send rate, tunnel, game ID, ...).
    /// They are set in spawn.ini, in this file's order, right after the [Settings] section is created.
    /// </summary>
    public IniFile SpawnIniAdditions { get; init; }

    /// <summary>The packed values of the broadcast game options, shown by the game loading lobby.</summary>
    public string BroadcastedGameOptionValues { get; init; }
}
