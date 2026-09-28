using System;
using System.Collections.Generic;

using ClientLogic.Lobby;
using ClientLogic.Options;
using ClientLogic.Protocol;
using ClientLogic.UI;

using DTAClient.Domain.Multiplayer;

using Xunit;

namespace ClientLogic.Tests.Lobby;

public class GameLobbyStateTests
{
    private sealed class RecordingSession : ILobbySession
    {
        public List<string> Sent { get; } = [];

        public void SendChatMessage(string message) => Sent.Add("chat " + message);
        public void SendDiceRoll(int dieSides, int[] results) => Sent.Add($"dice {dieSides}:{string.Join(",", results)}");
        public void RequestPlayerOptions(PackedPlayerOptions options) => Sent.Add("options request");
        public void RequestReady(int readyState) => Sent.Add("ready " + readyState);
        public void SendPlayerOptions(PlayerOptionsMessage message) => Sent.Add("player options");
        public void SendPlayerExtraOptions(PlayerExtraOptions options) => Sent.Add("extra options");
        public void SendGameOptions(GameOptionsMessage message) => Sent.Add("game options");
    }

    private sealed class RecordingDialogs(bool answerYes) : IDialogService
    {
        public List<string> Shown { get; } = [];

        public void ShowMessage(string title, string text) => Shown.Add(text);
        public void ShowMessage(string title, string text, Action onOk) => ShowMessage(title, text);

        public void Confirm(string title, string text, Action onYes)
        {
            Shown.Add(text);
            if (answerYes)
                onYes();
        }

        public void Confirm(string title, string text, Action onYes, Action onNo)
        {
            Confirm(title, text, onYes);
            if (!answerYes)
                onNo();
        }
    }

    private static GameLobbyState Create(List<PlayerInfo> players = null)
    {
        TestGame.EnsureInitialized();
        return new GameLobbyState(new GameOptionSet(), new PlayerSlotsState(players ?? [], []), new PlayerExtraOptionsState());
    }

    [Fact]
    public void ClearingReadyNeverClearsTheHostAndKeepsAutoReady()
    {
        var players = new List<PlayerInfo>
        {
            new("Host") { Ready = true },
            new("A") { Ready = true, AutoReady = true },
            new("B") { Ready = true },
            new("C") { Ready = true, AutoReady = true, IsInGame = true },
        };
        GameLobbyState state = Create(players);

        state.ClearReadyStatuses();
        Assert.Equal([true, true, false, false], players.ConvertAll(p => p.Ready));

        state.ClearReadyStatuses(resetAutoReady: true);
        Assert.Equal([true, false, false, false], players.ConvertAll(p => p.Ready));
    }

    [Fact]
    public void DiceRollsAreSentThroughTheSession()
    {
        GameLobbyState state = Create();
        var session = new RecordingSession();
        state.Session = session;

        int[] results = state.RollDice("2d6", new Random(1), out int sides, out string error);

        Assert.Null(error);
        Assert.Equal(6, sides);
        Assert.Equal([$"dice 6:{string.Join(",", results)}"], session.Sent);

        Assert.Null(state.RollDice("0d6", new Random(1), out _, out error));
        Assert.NotNull(error);
        Assert.Single(session.Sent);
    }

    [Fact]
    public void TheLockButtonUsesThePlayerCount()
    {
        GameLobbyState state = Create([new("Host"), new("A")]);

        Assert.Equal(LockButtonAction.Lock, state.LockButtonAction(playerLimit: 2));

        state.Locked = true;
        Assert.Equal(LockButtonAction.RefuseUnlock, state.LockButtonAction(2));
        Assert.Equal(LockButtonAction.Unlock, state.LockButtonAction(3));
    }

    [Fact]
    public void DeletingAMapNeedsAMap()
    {
        GameLobbyState state = Create();
        var dialogs = new RecordingDialogs(answerYes: true);
        bool deleted = false;

        state.DeleteMap(dialogs, () => deleted = true);

        Assert.False(deleted);
        Assert.Empty(dialogs.Shown);
        Assert.False(state.CanDeleteMap(isMultiplayer: false));
    }
}
