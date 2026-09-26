using System;
using System.Collections.Generic;

using ClientCore.Extensions;

using ClientLogic.Options;
using ClientLogic.UI;

using CommunityToolkit.Mvvm.ComponentModel;

using DTAClient.Domain.Multiplayer;

namespace ClientLogic.Lobby;

/// <summary>
/// The state of a game lobby (skirmish, CnCNet or LAN): its game options, player slots and extra player options, the
/// selected map, the room settings and lock (CnCNet), and the network session (multiplayer). Its commands apply the
/// lobby rules; the lobby's view shows the state and forwards input.
/// </summary>
public sealed partial class GameLobbyState : ObservableObject
{
    public GameLobbyState(GameOptionSet options, PlayerSlotsState slots, PlayerExtraOptionsState extraOptions)
    {
        Options = options;
        Slots = slots;
        ExtraOptions = extraOptions;
    }

    public GameOptionSet Options { get; }

    public PlayerSlotsState Slots { get; }

    public PlayerExtraOptionsState ExtraOptions { get; }

    /// <summary>The CnCNet game room's settings; null in other lobbies.</summary>
    public GameRoomSettings RoomSettings { get; set; }

    /// <summary>Sends the lobby's messages; null in skirmish.</summary>
    public ILobbySession Session { get; set; }

    [ObservableProperty]
    private GameModeMap gameModeMap;

    /// <summary>Whether the local player is the game host (always true in skirmish).</summary>
    [ObservableProperty]
    private bool isHost = true;

    /// <summary>Whether the game room is locked, so no one else can join.</summary>
    [ObservableProperty]
    private bool locked;

    /// <summary>
    /// Clears the ready status of the players other than the game host. Players with auto-ready keep it unless
    /// <paramref name="resetAutoReady"/> is set or they're still in a game.
    /// </summary>
    public void ClearReadyStatuses(bool resetAutoReady = false)
    {
        List<PlayerInfo> players = Slots.Players;

        for (int i = 1; i < players.Count; i++)
        {
            if (resetAutoReady || !players[i].AutoReady || players[i].IsInGame)
                players[i].Ready = false;
        }
    }

    /// <summary>What the game host's Lock/Unlock button does now.</summary>
    public LockButtonAction LockButtonAction(int playerLimit) =>
        RoomLock.OnLockButton(Locked, Slots.Players.Count, playerLimit);

    /// <summary>
    /// Everything that keeps the game host from launching, in the order the lobby reports them.
    /// </summary>
    public IReadOnlyList<LaunchBlocker> CheckLaunch(string localPlayerName, int spectatorSide) =>
        LaunchValidation.Validate(new LaunchCheck(
            Locked, ExtraOptions.ToPlayerExtraOptions().GetTeamMappingsError(), Slots.Players, Slots.AIPlayers,
            localPlayerName, spectatorSide,
            GameModeMap.EnforceMinPlayers, GameModeMap.MinPlayers, GameModeMap.EnforceMaxPlayers, GameModeMap.MaxPlayers));

    /// <summary>
    /// The /roll command: rolls the dice a spec such as "3d6" describes and sends the result.
    /// </summary>
    /// <param name="error">The notice to show when the spec is invalid.</param>
    /// <returns>The results, or null if the spec is invalid.</returns>
    public int[] RollDice(string spec, Random random, out int dieSides, out string error)
    {
        if (!DiceRoll.TryParse(spec, out int dieCount, out dieSides, out error))
            return null;

        int[] results = DiceRoll.Roll(dieCount, dieSides, random);
        Session?.SendDiceRoll(dieSides, results);
        return results;
    }

    /// <summary>Whether the selected map is a custom map that can be deleted (skirmish only).</summary>
    public bool CanDeleteMap(bool isMultiplayer) => GameModeMap?.Map != null && !GameModeMap.Map.Official && !isMultiplayer;

    /// <summary>Asks the user to confirm deleting the selected custom map and runs <paramref name="delete"/> if they do.</summary>
    public void DeleteMap(IDialogService dialogs, Action delete)
    {
        Map map = GameModeMap?.Map;
        if (map == null)
            return;

        dialogs.Confirm("Delete Confirmation".L10N("Client:Main:DeleteMapConfirmTitle"),
            string.Format("Are you sure you wish to delete the custom map {0}?".L10N("Client:Main:DeleteMapConfirmText"), map.Name),
            delete);
    }
}
