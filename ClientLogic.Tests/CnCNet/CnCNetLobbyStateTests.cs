using System;

using ClientLogic.CnCNet;
using ClientLogic.UI;

using DTAClient.Domain.Multiplayer.CnCNet;
using DTAClient.Online;

using SixLabors.ImageSharp;

using Xunit;

namespace ClientLogic.Tests.CnCNet;

public class CnCNetLobbyStateTests
{
    private sealed class InlineDispatcher : IUiDispatcher
    {
        public void Post(Action action) => action();

        public bool CheckAccess() => true;
    }

    private sealed class TestCnCNetGame : CnCNetGame
    {
        protected override Image LoadImage() => null;
    }

    private static CnCNetLobbyState Create()
    {
        TestGame.EnsureInitialized();
        var games = new GameCollection();
        var manager = new CnCNetManager(new InlineDispatcher(), games, new CnCNetUserData(), new Random(1));
        return new CnCNetLobbyState(manager, games, "yr");
    }

    private static HostedCnCNetGame Game(string host) =>
        new("#" + host, "B", "1.0", 8, "Room", false, true, [host], host, "Map", "Battle", "hash")
        {
            Game = new TestCnCNetGame { InternalName = "yr" },
        };

    [Fact]
    public void JoiningBlocksFurtherJoins()
    {
        CnCNetLobbyState state = Create();
        HostedCnCNetGame game = Game("Host");
        state.GameList.AddOrUpdate(game);

        Assert.Null(state.JoinErrorByIndex(0));
        Assert.NotNull(state.JoinErrorByIndex(1));
        Assert.Null(state.JoinError(game, disallowJoiningIncompatibleGames: false));

        state.BeginJoin(game);

        Assert.True(state.IsJoiningGame);
        Assert.Same(game, state.GameOfLastJoinAttempt);
        Assert.NotNull(state.JoinErrorByIndex(0));
        Assert.NotNull(state.JoinError(game, false));
    }
}
