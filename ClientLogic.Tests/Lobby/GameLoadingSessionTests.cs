using System;
using System.Collections.Generic;
using System.IO;

using ClientCore;

using ClientLogic.Launch;
using ClientLogic.Lobby;
using ClientLogic.UI;

using DTAClient.Domain.Multiplayer;

using Xunit;

namespace ClientLogic.Tests.Lobby;

[Collection("Saved games")]
public class GameLoadingSessionTests : IDisposable
{
    private readonly string savedGamesDirectory;

    public GameLoadingSessionTests()
    {
        TestGame.EnsureInitialized();
        savedGamesDirectory = Path.Combine(ProgramConstants.GamePath, "Saved Games");
        Directory.CreateDirectory(savedGamesDirectory);

        File.WriteAllText(Path.Combine(savedGamesDirectory, "spawnSG.ini"),
            "[Settings]\nName=" + ProgramConstants.PLAYERNAME + "\nGameID=1234\nUIMapName=Test Map\nUIGameMode=Battle\nPlayerCount=2\nColor=0\n" +
            "[Other1]\nName=Friend\nColor=1\n");
        File.WriteAllText(Path.Combine(savedGamesDirectory, "SVGM_000.NET"), "save");
    }

    public void Dispose()
    {
        File.Delete(Path.Combine(savedGamesDirectory, "spawnSG.ini"));
        File.Delete(Path.Combine(savedGamesDirectory, "SVGM_000.NET"));
    }

    private sealed class Dispatcher : IUiDispatcher
    {
        public void Post(Action action) => action();
        public bool CheckAccess() => true;
    }

    private sealed class NullSounds : ISoundService
    {
        public void Play(LobbySound sound) { }
        public void SetEnabled(LobbySound sound, bool enabled) { }
    }

    private sealed class NullDialogs : IDialogService
    {
        public void ShowMessage(string title, string text) { }
        public void ShowMessage(string title, string text, Action onOk) { }
        public void Confirm(string title, string text, Action onYes) { }
        public void Confirm(string title, string text, Action onYes, Action onNo) { }
    }

    private sealed class TestRoom : GameLoadingSession
    {
        public TestRoom() : base(new GameProcessService(), new NullDialogs(), new NullSounds(), new Dispatcher())
        {
            MessageAdded += (_, m) => Notices.Add(m.Message);
        }

        public List<string> Notices { get; } = [];
        public bool Started { get; private set; }
        public bool ReadyRequested { get; private set; }
        public int Broadcasts { get; private set; }

        public void Open(bool isHost) => Refresh(isHost);

        public override void SendChatMessage(string message) { }
        protected override void RequestReadyStatus() => ReadyRequested = true;
        protected override void HostStartGame() => Started = true;
        protected override void BroadcastOptions() => Broadcasts++;
    }

    [Fact]
    public void Refresh_reads_the_saved_games_players_and_map()
    {
        var room = new TestRoom();
        room.Open(isHost: true);

        Assert.Equal([ProgramConstants.PLAYERNAME, "Friend"], room.SGPlayers.ConvertAll(p => p.Name));
        Assert.Equal("Test Map", room.MapName);
        Assert.Equal("Test Map", room.MapNameText);
        Assert.Equal("Battle", room.GameMode);
        Assert.Single(room.SavedGames);
        Assert.Equal(0, room.SelectedSavedGameIndex);
        Assert.All(room.PlayerRows(), row => Assert.False(row.IsPresent));
    }

    [Fact]
    public void The_host_can_load_only_when_everyone_is_present_and_ready()
    {
        var room = new TestRoom();
        room.Open(isHost: true);
        room.Players.Add(new PlayerInfo(ProgramConstants.PLAYERNAME) { Ready = true });

        room.LoadButtonClicked();
        Assert.False(room.Started);
        Assert.Contains(room.Notices, n => n.Contains("all players are present"));

        var friend = new PlayerInfo("Friend");
        room.Players.Add(friend);
        room.LoadButtonClicked();
        Assert.False(room.Started);
        Assert.Contains(room.Notices, n => n.Contains("not all players are ready"));
        Assert.Contains(room.PlayerRows(), row => row.Text.Contains("Not Ready"));

        friend.Ready = true;
        room.LoadButtonClicked();
        Assert.True(room.Started);
    }

    [Fact]
    public void A_player_asks_to_be_ready()
    {
        var room = new TestRoom();
        room.Open(isHost: false);

        room.LoadButtonClicked();

        Assert.True(room.ReadyRequested);
        Assert.Equal("I'm Ready", room.LoadButtonText);
    }
}
