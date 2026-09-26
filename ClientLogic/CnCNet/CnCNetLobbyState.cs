using System;

using ClientCore;
using ClientCore.Extensions;

using ClientLogic.GameList;

using CommunityToolkit.Mvvm.ComponentModel;

using DTAClient.Domain.Multiplayer.CnCNet;
using DTAClient.Online;

namespace ClientLogic.CnCNet;

/// <summary>
/// The state of the CnCNet lobby: the game list, open game invitations, the game being joined, whether the user is in
/// a game room, and the chosen chat colour. Its commands apply the join rules and send the join request.
/// </summary>
public sealed partial class CnCNetLobbyState : ObservableObject
{
    private readonly CnCNetManager connection;
    private readonly GameCollection gameCollection;

    public CnCNetLobbyState(CnCNetManager connection, GameCollection gameCollection, string localGameId)
    {
        this.connection = connection;
        this.gameCollection = gameCollection;
        LocalGameId = localGameId;
    }

    public string LocalGameId { get; }

    public GameListState GameList { get; } = new();

    /// <summary>The open invitations; the front end stores its notification for each.</summary>
    public GameInvitations<object> Invitations { get; } = new();

    /// <summary>A join request was sent and the channel hasn't been joined (or refused) yet.</summary>
    [ObservableProperty]
    private bool isJoiningGame;

    /// <summary>The user is in a game room (game lobby or loading lobby).</summary>
    [ObservableProperty]
    private bool isInGameRoom;

    /// <summary>The last game the user tried to join.</summary>
    [ObservableProperty]
    private HostedCnCNetGame gameOfLastJoinAttempt;

    [ObservableProperty]
    private IRCColor chatColor;

    /// <summary>Why no game can be joined right now, or null.</summary>
    public string JoinBaseError => JoinGameRules.BaseError(IsJoiningGame, ProgramConstants.IsInGame);

    /// <summary>Why the listed game at <paramref name="gameIndex"/> can't be joined, or null.</summary>
    public string JoinErrorByIndex(int gameIndex)
    {
        if (gameIndex < 0 || gameIndex >= GameList.Games.Count)
            return "Invalid game index".L10N("Client:Main:InvalidGameIndex");

        return JoinBaseError;
    }

    /// <summary>Why <paramref name="hg"/> can't be joined, or null.</summary>
    public string JoinError(HostedCnCNetGame hg, bool disallowJoiningIncompatibleGames) =>
        JoinGameRules.Error(hg, new JoinContext(LocalGameId, ProgramConstants.PLAYERNAME, disallowJoiningIncompatibleGames,
            IsJoiningGame, ProgramConstants.IsInGame, gameCollection.GetGameNameFromInternalName));

    /// <summary>Marks a join as started; the front end then sets up the game room and calls <see cref="SendJoin"/>.</summary>
    public void BeginJoin(HostedCnCNetGame hg)
    {
        IsJoiningGame = true;
        GameOfLastJoinAttempt = hg;
    }

    /// <summary>Sends the JOIN for a game's channel.</summary>
    public void SendJoin(HostedCnCNetGame hg, string password) =>
        connection.SendCustomMessage(new QueuedMessage("JOIN " + hg.ChannelName + " " + password, QueuedMessageType.INSTANT_MESSAGE, 0));
}
