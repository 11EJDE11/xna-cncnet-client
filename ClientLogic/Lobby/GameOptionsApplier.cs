using System;
using System.Collections.Generic;

using ClientCore.Extensions;

using ClientLogic.Protocol;

using DTAClient.Domain.Multiplayer;

namespace ClientLogic.Lobby;

/// <summary>What happens to the lobby's map when the host's game options arrive.</summary>
public enum GameOptionsMapAction
{
    /// <summary>The host's map is already selected.</summary>
    None,

    /// <summary>Select <see cref="GameOptionsUpdate.GameModeMap"/>.</summary>
    Change,

    /// <summary>The host has no map selected: clear the map.</summary>
    Clear,

    /// <summary>The host's custom map isn't installed: clear the map and offer to download it.</summary>
    ClearAndRequestDownload,

    /// <summary>The host's custom map isn't installed and map sharing is off: clear the map and tell the host.</summary>
    ClearAndReportMapSharingDisabled,

    /// <summary>The host's official map isn't installed: clear the map and tell the host.</summary>
    ClearAndReportOfficialMapMissing,
}

/// <summary>The lobby state that receiving the host's game options depends on.</summary>
public sealed record LobbyGameSettings(
    int FrameSendRate,
    int MaxAhead,
    int ProtocolVersion,
    GameModeMap GameModeMap);

/// <summary>
/// The result of <see cref="GameOptionsApplier.Plan"/>. A lobby applies it in this order:
/// the game settings (with <see cref="SettingNotices"/>), the map action, the check-box and drop-down values,
/// random starting locations, the random seed and (CnCNet only) the tunnel mode.
/// </summary>
public sealed record GameOptionsUpdate
{
    public int FrameSendRate { get; init; }

    public int MaxAhead { get; init; }

    public int ProtocolVersion { get; init; }

    /// <summary>Chat notices for changed game settings, in order.</summary>
    public IReadOnlyList<string> SettingNotices { get; init; } = [];

    public GameOptionsMapAction MapAction { get; init; }

    /// <summary>The map to select for <see cref="GameOptionsMapAction.Change"/>; otherwise null.</summary>
    public GameModeMap GameModeMap { get; init; }

    public string MapSHA1 { get; init; } = string.Empty;

    public string MapName { get; init; } = string.Empty;

    public string GameModeName { get; init; } = string.Empty;

    /// <summary>
    /// The check-box values to apply after the map action. Compare them with the check-boxes' values after the
    /// map action (the map can force options) to tell which ones the host changed.
    /// </summary>
    public IReadOnlyList<bool> CheckBoxValues { get; init; } = [];

    /// <summary>The drop-down indices to apply after the map action; ignore indices a drop-down doesn't have.</summary>
    public IReadOnlyList<int> DropDownIndices { get; init; } = [];

    public bool RemoveStartingLocations { get; init; }

    public int RandomSeed { get; init; }

    public int TunnelMode { get; init; }
}

/// <summary>
/// Decides how a lobby applies the host's game options, shared by the CnCNet and LAN lobbies.
/// </summary>
public static class GameOptionsApplier
{
    /// <param name="message">The host's game options.</param>
    /// <param name="current">The lobby's current settings.</param>
    /// <param name="findGameModeMap">Finds an installed map by game mode name and map SHA1, or returns null.</param>
    /// <param name="isMapSharingEnabled">Whether the user allows downloading maps from the host.</param>
    public static GameOptionsUpdate Plan(GameOptionsMessage message, LobbyGameSettings current,
        Func<string, string, GameModeMap> findGameModeMap, bool isMapSharingEnabled)
    {
        var notices = new List<string>();

        if (message.FrameSendRate != current.FrameSendRate)
            notices.Add(string.Format("The game host has changed FrameSendRate (order lag) to {0}".L10N("Client:Main:HostChangeFrameSendRate"), message.FrameSendRate));

        if (message.MaxAhead != current.MaxAhead)
            notices.Add(string.Format("The game host has changed MaxAhead to {0}".L10N("Client:Main:HostChangeMaxAhead"), message.MaxAhead));

        if (message.ProtocolVersion != current.ProtocolVersion)
            notices.Add(string.Format("The game host has changed ProtocolVersion to {0}".L10N("Client:Main:HostChangeProtocolVersion"), message.ProtocolVersion));

        GameModeMap gameModeMap = findGameModeMap(message.GameModeName, message.MapSHA1);

        GameOptionsMapAction mapAction;
        if (gameModeMap == null)
        {
            if (string.IsNullOrEmpty(message.MapSHA1))
                mapAction = GameOptionsMapAction.Clear;
            else if (message.IsMapOfficial)
                mapAction = GameOptionsMapAction.ClearAndReportOfficialMapMissing;
            else if (isMapSharingEnabled)
                mapAction = GameOptionsMapAction.ClearAndRequestDownload;
            else
                mapAction = GameOptionsMapAction.ClearAndReportMapSharingDisabled;
        }
        else
        {
            mapAction = gameModeMap != current.GameModeMap ? GameOptionsMapAction.Change : GameOptionsMapAction.None;
        }

        return new GameOptionsUpdate
        {
            FrameSendRate = message.FrameSendRate,
            MaxAhead = message.MaxAhead,
            ProtocolVersion = message.ProtocolVersion,
            SettingNotices = notices,
            MapAction = mapAction,
            GameModeMap = mapAction == GameOptionsMapAction.Change ? gameModeMap : null,
            MapSHA1 = message.MapSHA1,
            MapName = message.MapName,
            GameModeName = message.GameModeName,
            CheckBoxValues = message.CheckBoxValues,
            DropDownIndices = message.DropDownIndices,
            RemoveStartingLocations = message.RemoveStartingLocations,
            RandomSeed = message.RandomSeed,
            TunnelMode = message.TunnelMode,
        };
    }
}

/// <summary>Chat notices shown to players when the host changes game options.</summary>
public static class GameOptionNotices
{
    public static string CheckBoxChanged(string optionText, bool isChecked) => isChecked
        ? string.Format("The game host has enabled {0}".L10N("Client:Main:HostEnableOption"), optionText)
        : string.Format("The game host has disabled {0}".L10N("Client:Main:HostDisableOption"), optionText);

    public static string DropDownChanged(string optionName, string itemText) =>
        string.Format("The game host has set {0} to {1}".L10N("Client:Main:HostSetOption"), optionName, itemText);

    public static string RemoveStartingLocationsChanged(bool isEnabled) => isEnabled
        ? "The game host has enabled completely random starting locations (only works for regular maps).".L10N("Client:Main:HostEnabledRandomStartLocation")
        : "The game host has disabled completely random starting locations.".L10N("Client:Main:HostDisabledRandomStartLocation");
}
