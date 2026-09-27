using System;
using System.Collections.Generic;

using ClientLogic.CnCNet;
using ClientLogic.Lan;
using ClientLogic.Launch;
using ClientLogic.UI;

using DTAClient.Domain.Multiplayer;
using DTAClient.Domain.Multiplayer.CnCNet;
using DTAClient.Online;

using Xunit;

namespace ClientLogic.Tests.Lobby;

/// <summary>
/// The LAN and CnCNet sessions are built the way the front ends' service containers build them, without a network
/// (nothing connects until the player hosts, joins or logs in).
/// </summary>
public class MultiplayerSessionConstructionTests
{
    private sealed class NullDialogs : IDialogService
    {
        public void ShowMessage(string title, string text) { }
        public void ShowMessage(string title, string text, Action onOk) => ShowMessage(title, text);
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

    public MultiplayerSessionConstructionTests() => TestGame.EnsureInitialized();

    [Fact]
    public void TheLanSessionsCanBeCreated()
    {
        var dispatcher = new ImmediateDispatcher();
        var room = new LanGameRoom(new MapLoader(), new GameProcessService(), new NullDialogs(), new NullSounds(), dispatcher, new Random(1));
        using var lobby = new LanLobby(new GameCollection(), dispatcher, new Random(1));

        Assert.False(room.IsHost);
        Assert.Empty(lobby.Players);
        Assert.Contains(room.ChatCommands, c => c.Command == "ROLL");
    }

    [Fact]
    public void TheCnCNetSessionsCanBeCreatedAndSetUpTheirChannels()
    {
        var dispatcher = new ImmediateDispatcher();
        var random = new Random(1);
        var gameCollection = new GameCollection();
        var userData = new CnCNetUserData();
        var connectionManager = new CnCNetManager(dispatcher, gameCollection, userData, random);
        var tunnelHandler = new TunnelHandler(dispatcher);
        var gameProcess = new GameProcessService();

        var room = new CnCNetGameRoom(connectionManager, tunnelHandler, dispatcher, gameCollection, userData, new MapLoader(),
            gameProcess, new NullDialogs(), new NullSounds(), random);
        var lobby = new CnCNetLobbyService(connectionManager, tunnelHandler, gameCollection, userData, room, gameProcess, random);

        lobby.Initialize("test");

        Assert.False(lobby.IsConnected);
        Assert.NotEmpty(lobby.ChatChannelNames);
        Assert.NotNull(lobby.CurrentChannel);
        Assert.Contains(room.ChatCommands, c => c.Command == "TUNNELINFO");
        Assert.Empty(lobby.Games);
    }

    [Fact]
    public void ACnCNetPlayerSeesReturnMessagesInsteadOfTakingThemAsReadyRequests()
    {
        var dispatcher = new ImmediateDispatcher();
        var random = new Random(1);
        var gameCollection = new GameCollection();
        var userData = new CnCNetUserData();
        var connectionManager = new CnCNetManager(dispatcher, gameCollection, userData, random);
        var room = new CnCNetGameRoom(connectionManager, new TunnelHandler(dispatcher), dispatcher, gameCollection, userData,
            new MapLoader(), new GameProcessService(), new NullDialogs(), new NullSounds(), random);

        var channel = new Channel("Room", "#cncnet-test-game1234567", false, true, "password", null);
        room.SetUp(channel, isHost: false, playerLimit: 8, tunnel: null, hostName: "Host", isCustomPassword: false, skillLevel: 0);

        var notices = new List<string>();
        room.MessageAdded += (_, message) => notices.Add(message.Message);

        channel.OnCTCPReceived("Guest", "RETURN");

        Assert.Contains(notices, n => n.Contains("Guest") && n.Contains("returned"));
    }
}
