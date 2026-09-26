using System;
using System.Collections.Generic;
using System.Linq;

using ClientCore.Enums;

using ClientLogic.GameList;

using DTAClient.Domain.Multiplayer;
using DTAClient.Domain.Multiplayer.CnCNet;

using SixLabors.ImageSharp;

using Xunit;

namespace ClientLogic.Tests.GameList;

public class GameListTests
{
    private sealed class TestCnCNetGame(string internalName) : CnCNetGame
    {
        public string Name { get; } = internalName;

        protected override Image LoadImage() => null;
    }

    private static readonly CnCNetGame Yr = new TestCnCNetGame("yr") { InternalName = "yr" };
    private static readonly CnCNetGame Ts = new TestCnCNetGame("ts") { InternalName = "ts" };

    private static HostedCnCNetGame Game(string host, string room = null, CnCNetGame game = null, bool locked = false,
        bool passworded = false, string version = "1.0", int maxPlayers = 8, string[] players = null, int[] options = null) =>
        new("#" + host, "B", version, maxPlayers, room ?? host + "'s game", passworded, true, players ?? [host], host, "Map", "Battle", "hash")
        {
            Game = game ?? Yr,
            Locked = locked,
            BroadcastedGameOptionValues = options,
            LastRefreshTime = new DateTime(2026, 9, 27, 12, 0, 0),
        };

    [Fact]
    public void GamesAreKeyedByHostAndExpire()
    {
        var state = new GameListState();

        Assert.True(state.AddOrUpdate(Game("A")));
        Assert.True(state.AddOrUpdate(Game("B")));
        HostedCnCNetGame newer = Game("A", room: "renamed");
        newer.LastRefreshTime = newer.LastRefreshTime.AddSeconds(30);
        Assert.False(state.AddOrUpdate(newer));
        Assert.Equal(["renamed", "B's game"], state.Games.Select(g => g.RoomName));

        state.RemoveExpired(new DateTime(2026, 9, 27, 12, 0, 40));
        Assert.Equal(["renamed"], state.Games.Select(g => g.RoomName));

        Assert.True(state.RemoveByHost("A"));
        Assert.False(state.RemoveByHost("A"));
        Assert.Empty(state.Games);
    }

    [Fact]
    public void SortingKeepsTheClientsOrder()
    {
        var games = new List<GenericHostedGame>
        {
            Game("locked", room: "a", locked: true),
            Game("yr-b", room: "b"),
            Game("ts", room: "c", game: Ts),
            Game("old", room: "a", version: "0.9", game: Ts),
            Game("pw", room: "a", passworded: true, game: Ts),
            Game("yr-a", room: "a"),
        };

        Assert.Equal(["ts", "pw", "old", "yr-a", "yr-b", "locked"],
            GameListState.Sort(games, "yr", "1.0", SortDirection.Asc).Select(g => g.HostName));
        Assert.Equal(["ts", "pw", "old", "yr-b", "yr-a", "locked"],
            GameListState.Sort(games, "yr", "1.0", SortDirection.Desc).Select(g => g.HostName));
    }

    private static GameListFilterSettings Settings(bool friendsOnly = false, bool hideLocked = false, bool hidePassworded = false,
        int maxPlayers = 8, string search = null, IReadOnlyList<int?> options = null) =>
        new(friendsOnly, hideLocked, false, hidePassworded, maxPlayers, search, options ?? []);

    private static bool Matches(GenericHostedGame game, GameListFilterSettings settings) =>
        GameListFilter.Matches(game, settings, name => name == "Friend", mode => "Schlacht", map => map == "Map" ? "Karte" : null);

    [Fact]
    public void FiltersFollowTheSettings()
    {
        Assert.True(Matches(Game("A"), Settings()));
        Assert.False(Matches(Game("A", locked: true), Settings(hideLocked: true)));
        Assert.False(Matches(Game("A", passworded: true), Settings(hidePassworded: true)));
        Assert.False(Matches(Game("A", maxPlayers: 8), Settings(maxPlayers: 4)));

        // Friends only overrides the rest
        Assert.True(Matches(Game("A", locked: true, players: ["A", "Friend"]), Settings(friendsOnly: true, hideLocked: true)));
        Assert.False(Matches(Game("A"), Settings(friendsOnly: true)));

        // Broadcast option filters; null means any value
        Assert.True(Matches(Game("A", options: [1, 3]), Settings(options: [1, null])));
        Assert.False(Matches(Game("A", options: [1, 3]), Settings(options: [null, 2])));
        Assert.True(Matches(Game("A"), Settings(options: [5])));
    }

    [Theory]
    [InlineData("a's", true)]       // room name contains
    [InlineData("battle", true)]    // game mode equals
    [InlineData("schlacht", true)]  // translated game mode equals
    [InlineData("bat", false)]      // game mode must match whole
    [InlineData("kart", true)]      // translated map contains
    [InlineData("a", true)]         // player equals (and room contains)
    [InlineData("zzz", false)]
    public void SearchLooksAtRoomModeMapAndPlayers(string search, bool expected) =>
        Assert.Equal(expected, Matches(Game("A"), Settings(search: search)));
}
