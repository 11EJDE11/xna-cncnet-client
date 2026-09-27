using System;
using System.Collections.Generic;
using System.Linq;

using ClientCore;

using ClientLogic.Launch;
using ClientLogic.Lobby;
using ClientLogic.Protocol;
using ClientLogic.UI;

using DTAClient.Domain.Multiplayer;

using Xunit;

namespace ClientLogic.Tests.Lobby;

public class MultiplayerLobbySessionTests
{
    private const string GUEST = "Guest";

    private sealed class RecordingSession : ILobbySession
    {
        public List<string> Sent { get; } = [];
        public PackedPlayerOptions? LastRequest { get; private set; }

        public void SendChatMessage(string message) => Sent.Add("chat " + message);
        public void SendDiceRoll(int dieSides, int[] results) => Sent.Add("dice");
        public void RequestPlayerOptions(PackedPlayerOptions options)
        {
            LastRequest = options;
            Sent.Add("options request");
        }
        public void RequestReady(int readyState) => Sent.Add("ready " + readyState);
        public void SendPlayerOptions(PlayerOptionsMessage message) => Sent.Add("player options");
        public void SendPlayerExtraOptions(PlayerExtraOptions options) => Sent.Add("extra options");
        public void SendGameOptions(GameOptionsMessage message) => Sent.Add("game options");
    }

    private sealed class NullDialogs : IDialogService
    {
        public List<string> Shown { get; } = [];
        public void ShowMessage(string title, string text) => Shown.Add(text);
        public void Confirm(string title, string text, Action onYes) { }
    }

    private sealed class NullSounds : ISoundService
    {
        public void Play(LobbySound sound) { }
        public void SetEnabled(LobbySound sound, bool enabled) { }
    }

    private sealed class ImmediateDispatcher : IUiDispatcher
    {
        public void Post(Action action) => action();
        public bool CheckAccess() => true;
    }

    /// <summary>A room without a network: messages go to a <see cref="RecordingSession"/>.</summary>
    private sealed class TestRoom : MultiplayerLobbySession
    {
        public TestRoom(bool isHost)
            : base("TestMultiplayerLobby", new MapLoader(), new GameProcessService(), new NullDialogs(), new NullSounds(),
                new ImmediateDispatcher(), new Random(1))
        {
            Session = Recorder;
            LobbyState.IsHost = isHost;
            Players.Add(new PlayerInfo(isHost ? ProgramConstants.PLAYERNAME : "Host") { Ready = true });
            Players.Add(new PlayerInfo(isHost ? GUEST : ProgramConstants.PLAYERNAME));
            ChangeMap(TestGame.LoadGameModeMap("Maps/Test/four", "Battle"));
            Recorder.Sent.Clear();
            Notices.Clear();
            MessageAdded += (_, m) => Notices.Add(m.Message);
        }

        public RecordingSession Recorder { get; } = new();
        public List<string> Notices { get; } = [];
        public bool Launched { get; private set; }

        protected override bool IsMultiplayer => true;
        protected override string LobbyTypeName => "TestLobby";

        public override void BroadcastPlayerOptions()
        {
            if (IsHost)
                Session.SendPlayerOptions(new PlayerOptionsMessage([]));
        }

        public override void RequestReady() => Session.RequestReady(AutoReady ? 2 : 1);
        protected override void HostLaunchGame() => Launched = true;
        public override void Leave() { }
    }

    public MultiplayerLobbySessionTests() => TestGame.EnsureInitialized();

    [Fact]
    public void TheHostAppliesItsOwnSlotChangeAndSendsThePlayers()
    {
        var room = new TestRoom(isHost: true);

        room.ChangeSlot(0, SlotField.Color, 2);

        Assert.Equal(2, room.Players[0].ColorId);
        Assert.Contains("player options", room.Recorder.Sent);
    }

    [Fact]
    public void APlayerAsksTheHostInsteadOfChangingItsSlot()
    {
        var room = new TestRoom(isHost: false);
        int side = room.Players[1].SideId;

        room.ChangeSlot(1, SlotField.Color, 3);

        Assert.Equal(new PackedPlayerOptions(side, 3, 0, 0), room.Recorder.LastRequest);
        Assert.Equal(0, room.Players[1].ColorId);
        Assert.DoesNotContain("player options", room.Recorder.Sent);
    }

    [Fact]
    public void TheHostAppliesAnAllowedOptionsRequest()
    {
        var room = new TestRoom(isHost: true);

        room.HandlePlayerOptionsRequest(GUEST, new PackedPlayerOptions(0, 4, 0, 1));

        Assert.Equal(4, room.Players[1].ColorId);
        Assert.Equal(1, room.Players[1].TeamId);
        Assert.Contains("player options", room.Recorder.Sent);
    }

    [Fact]
    public void AnOptionsRequestIsIgnoredByPlayersAndForUnknownSenders()
    {
        var player = new TestRoom(isHost: false);
        player.HandlePlayerOptionsRequest("Host", new PackedPlayerOptions(0, 4, 0, 1));
        Assert.Empty(player.Recorder.Sent);

        var host = new TestRoom(isHost: true);
        host.HandlePlayerOptionsRequest("Nobody", new PackedPlayerOptions(0, 4, 0, 1));
        Assert.Empty(host.Recorder.Sent);
    }

    [Fact]
    public void HostOnlyChatCommandsAreRefusedForPlayers()
    {
        var player = new TestRoom(isHost: false);
        int frameSendRate = player.FrameSendRate;

        player.SubmitChatInput("/framesendrate 9");

        Assert.Equal(frameSendRate, player.FrameSendRate);
        Assert.Single(player.Notices);
        Assert.Empty(player.Recorder.Sent);
    }

    [Fact]
    public void TheHostChangesFrameSendRateAndSendsTheOptions()
    {
        var host = new TestRoom(isHost: true);

        host.SubmitChatInput("/framesendrate 9");

        Assert.Equal(9, host.FrameSendRate);
        Assert.Contains("game options", host.Recorder.Sent);
    }

    [Fact]
    public void ChatTextIsSent()
    {
        var player = new TestRoom(isHost: false);

        player.SubmitChatInput("hello");

        Assert.Equal(["chat hello"], player.Recorder.Sent);
    }

    [Fact]
    public void TheHostCantLaunchAnUnlockedRoom()
    {
        var host = new TestRoom(isHost: true);
        host.Players[1].Ready = true;

        host.Launch();

        Assert.False(host.Launched);
        Assert.Single(host.Notices);
    }

    [Fact]
    public void PressingLaunchAsAPlayerRequestsReady()
    {
        var player = new TestRoom(isHost: false);

        player.Launch();

        Assert.Equal(["ready 1"], player.Recorder.Sent);
    }

    [Fact]
    public void AReadyRequestUpdatesThePlayerAndIsSent()
    {
        var host = new TestRoom(isHost: true);

        host.HandleReadyRequest(GUEST, 2);

        Assert.True(host.Players[1].Ready);
        Assert.True(host.Players[1].AutoReady);
        Assert.Contains("player options", host.Recorder.Sent);
    }

    [Fact]
    public void AGameOptionChangeClearsTheOtherPlayersReadyStatus()
    {
        var host = new TestRoom(isHost: true);
        host.Players[1].Ready = true;

        host.SubmitChatInput("/maxahead 3");

        Assert.False(host.Players[1].Ready);
        Assert.True(host.Players[0].Ready);
    }

    [Fact]
    public void AnInvalidMapChangeIsRemembered()
    {
        var player = new TestRoom(isHost: false);

        player.ChangeMap(null);

        Assert.True(player.LastMapChangeWasInvalid);
        Assert.Null(player.GameModeMap);
        Assert.True(player.Players.Where(p => p.Name != "Host").All(p => !p.Ready));
    }

    [Fact]
    public void WithThePanelForcedExtraOptionsResetThePlayersAndTheHostSendsThem()
    {
        var room = new TestRoom(isHost: true);
        room.Players[1].SideId = 3;
        room.Players[1].ColorId = 2;

        room.ExtraOptions.ForceRandomSides = true;
        Assert.Equal(3, room.Players[1].SideId);
        Assert.DoesNotContain("extra options", room.Recorder.Sent);

        room.EnableExtraOptionsPanel();
        room.ExtraOptions.ForceRandomColors = true;

        Assert.Equal((0, 0), (room.Players[1].SideId, room.Players[1].ColorId));
        Assert.Contains("extra options", room.Recorder.Sent);
    }

    [Fact]
    public void APlayerAppliesTheHostsExtraOptionsOnlyWithThePanel()
    {
        var room = new TestRoom(isHost: false);
        string message = new PlayerExtraOptions { IsForceRandomSides = true }.ToString();

        room.ApplyPlayerExtraOptions(message);
        Assert.False(room.ExtraOptions.ForceRandomSides);

        room.EnableExtraOptionsPanel();
        room.ApplyPlayerExtraOptions(message);
        Assert.True(room.ExtraOptions.ForceRandomSides);
        Assert.DoesNotContain("extra options", room.Recorder.Sent);
    }

    [Fact]
    public void TheHostAssignsStartsFromTheMapPreviewAndThePlayersMustReadyAgain()
    {
        var room = new TestRoom(isHost: true);
        room.Players[1].Ready = true;

        room.AssignStartFromMapPreview(1, 2);

        Assert.Equal(2, room.Players[1].StartingLocation);
        Assert.False(room.Players[1].Ready);
        Assert.Contains("player options", room.Recorder.Sent);

        room.ClearStartFromMapPreview(2);
        Assert.Equal(0, room.Players[1].StartingLocation);
    }

    [Fact]
    public void APlayerPicksTheirOwnStartFromTheMapPreviewByAskingTheHost()
    {
        var room = new TestRoom(isHost: false);

        room.SelectLocalStartFromMapPreview(3);

        Assert.Equal(3, room.Recorder.LastRequest?.Start);
        Assert.Contains("options request", room.Recorder.Sent);
    }
}
