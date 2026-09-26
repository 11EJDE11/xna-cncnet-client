using System;
using System.Collections.Generic;
using System.Linq;

using ClientCore;

using DTAClient.Domain.Multiplayer;

namespace ClientLogic.Lobby;

/// <summary>A player option a lobby row shows.</summary>
public enum SlotField
{
    /// <summary>The name drop-down: AI level on AI rows, Kick/Ban on human rows.</summary>
    Name,
    Side,
    Color,
    Start,
    Team,
}

/// <summary>A command picked from a human player's name drop-down.</summary>
public enum SlotCommand
{
    None,
    Kick,
    Ban,
}

/// <summary>The outcome of a change to a player slot.</summary>
/// <param name="ClearsReady">Whether the other players' ready status must be cleared.</param>
/// <param name="Command">A command the lobby must carry out.</param>
public sealed record SlotChangeResult(bool ClearsReady, SlotCommand Command = SlotCommand.None);

/// <summary>
/// The lobby's players and AI players, and the changes the game host (or the skirmish player) can make to them.
/// The lobby's drop-downs only show this data; their input comes back here as a change of one field of one row.
/// </summary>
/// <remarks>
/// Ready rules: a change of colour doesn't clear the other players' ready status, any other change does. The host
/// is never cleared (see the lobby's <c>ClearReadyStatuses</c>).
/// </remarks>
public sealed class PlayerSlotsState
{
    /// <param name="players">The human players; the lobby and map preview share this list.</param>
    /// <param name="aiPlayers">The AI players; the lobby and map preview share this list.</param>
    public PlayerSlotsState(List<PlayerInfo> players, List<PlayerInfo> aiPlayers)
    {
        Players = players;
        AIPlayers = aiPlayers;
    }

    public List<PlayerInfo> Players { get; }

    public List<PlayerInfo> AIPlayers { get; }

    /// <summary>Human players first, then AI players, in row order.</summary>
    public IEnumerable<PlayerInfo> AllPlayers => Players.Concat(AIPlayers);

    /// <summary>Whether changing the field clears the other players' ready status.</summary>
    public static bool ClearsReady(SlotField field) => field != SlotField.Color;

    /// <summary>
    /// Applies a change the game host or skirmish player made to a row: a human player's options or command, or an
    /// AI player's options. Picking an AI level on the first free row adds an AI; picking "-" on an AI row removes it.
    /// </summary>
    /// <param name="row">The row: human players first, then AI players, then free rows.</param>
    /// <param name="index">The drop-down index picked.</param>
    /// <param name="isCoop">The current map is a co-op map: AI players are always on team 1.</param>
    public SlotChangeResult ApplyChange(int row, SlotField field, int index, SlotIndexMapper map, bool isCoop)
    {
        SlotCommand command = SlotCommand.None;

        if (row < Players.Count)
            command = ApplyHumanChange(Players[row], field, index);
        else
            ApplyAiChange(row - Players.Count, field, index);

        // AI players are always on team 1 on co-op maps
        if (isCoop)
        {
            foreach (PlayerInfo aiInfo in AIPlayers)
                aiInfo.TeamId = 1;
        }

        // Spectators have no start location
        foreach (PlayerInfo pInfo in Players)
        {
            if (map.IsSpectator(pInfo.SideId))
                pInfo.StartingLocation = SlotIndexMapper.RandomStart;
        }

        return new SlotChangeResult(ClearsReady(field), command);
    }

    private static SlotCommand ApplyHumanChange(PlayerInfo pInfo, SlotField field, int index)
    {
        switch (field)
        {
            case SlotField.Name:
                return index switch
                {
                    SlotIndexMapper.KickNameItem => SlotCommand.Kick,
                    SlotIndexMapper.BanNameItem => SlotCommand.Ban,
                    _ => SlotCommand.None,
                };
            case SlotField.Side:
                pInfo.SideId = index;
                break;
            case SlotField.Color:
                pInfo.ColorId = index;
                break;
            case SlotField.Start:
                pInfo.StartingLocation = index;
                break;
            case SlotField.Team:
                pInfo.TeamId = index;
                break;
        }

        return SlotCommand.None;
    }

    private void ApplyAiChange(int aiIndex, SlotField field, int index)
    {
        if (aiIndex < AIPlayers.Count)
        {
            PlayerInfo aiInfo = AIPlayers[aiIndex];

            switch (field)
            {
                case SlotField.Name when index < SlotIndexMapper.FirstAiNameItem:
                    AIPlayers.RemoveAt(aiIndex);
                    break;
                case SlotField.Name:
                    SetAiLevel(aiInfo, index);
                    break;
                case SlotField.Side:
                    aiInfo.SideId = Math.Max(index, 0);
                    break;
                case SlotField.Color:
                    aiInfo.ColorId = Math.Max(index, 0);
                    break;
                case SlotField.Start:
                    aiInfo.StartingLocation = Math.Max(index, 0);
                    break;
                case SlotField.Team:
                    aiInfo.TeamId = Math.Max(index, 0);
                    break;
            }
        }
        else if (aiIndex == AIPlayers.Count && field == SlotField.Name && index >= SlotIndexMapper.FirstAiNameItem)
        {
            var aiInfo = new PlayerInfo { IsAI = true };
            SetAiLevel(aiInfo, index);
            AIPlayers.Add(aiInfo);
        }
    }

    private static void SetAiLevel(PlayerInfo aiInfo, int nameItem)
    {
        aiInfo.AILevel = nameItem - SlotIndexMapper.FirstAiNameItem;
        aiInfo.Name = ProgramConstants.AI_PLAYER_NAMES[aiInfo.AILevel];
    }

    /// <summary>
    /// Applies a player's accepted options request (the host has validated it).
    /// </summary>
    public SlotChangeResult ApplyOptionsRequest(PlayerInfo pInfo, int side, int color, int start, int team)
    {
        bool clearsReady = side != pInfo.SideId || start != pInfo.StartingLocation || team != pInfo.TeamId;

        pInfo.SideId = side;
        pInfo.ColorId = color;
        pInfo.StartingLocation = start;
        pInfo.TeamId = team;

        return new SlotChangeResult(clearsReady);
    }

    /// <summary>
    /// Puts a player (by row) on a start location from the map preview. If the map allows only one player per
    /// location, whoever was there moves to a random start.
    /// </summary>
    /// <returns>False if there is no player on that row.</returns>
    public bool AssignStart(int row, int start, bool onePlayerPerStart)
    {
        PlayerInfo player = AllPlayers.ElementAtOrDefault(row);
        if (player == null)
            return false;

        if (onePlayerPerStart)
            ClearStart(start);

        player.StartingLocation = start;
        return true;
    }

    /// <summary>Moves every player on a start location to a random start.</summary>
    public void ClearStart(int start)
    {
        foreach (PlayerInfo pInfo in AllPlayers)
        {
            if (pInfo.StartingLocation == start)
                pInfo.StartingLocation = SlotIndexMapper.RandomStart;
        }
    }
}
