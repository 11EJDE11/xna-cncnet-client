using System;

using ClientLogic.Protocol;

using Xunit;

namespace ClientLogic.Tests.Protocol;

public class GameBroadcastMessageTests
{
    // A real line from the YR broadcast channel (release client, revision A), with the names replaced
    private const string ReleaseLine =
        "A;9.3.4;8;#cncnet-yr-game1000001;Host1's Game;11000;Host1;[4] Distant Quasar;Battle;198.244.177.26:50000;0;0;" +
        "7f496ead3cc793c47c6ee3218d19953038582f16;129,0,2,3,0";

    [Fact]
    public void RealLinesDecodeAndEncodeUnchanged()
    {
        Assert.True(GameBroadcastMessage.TryDecode(ReleaseLine, out GameBroadcastMessage message));

        Assert.Equal(("A", "9.3.4", 8, "#cncnet-yr-game1000001", "Host1's Game"),
            (message.Revision, message.GameVersion, message.MaxPlayers, message.ChannelName, message.RoomName));
        Assert.Equal((true, true, false, false, false),
            (message.Locked, message.IsCustomPassword, message.IsClosed, message.IsLoadedGame, message.IsLadder));
        Assert.Equal(["Host1"], message.Players);
        Assert.Equal(("[4] Distant Quasar", "Battle", "198.244.177.26:50000", false),
            (message.MapName, message.GameMode, message.Tunnel, message.IsDynamicTunnels));
        Assert.Equal(("0", "0", "129,0,2,3,0"), (message.LoadedGameId, message.SkillLevel, message.GameOptionValues));

        Assert.Equal("GAME " + ReleaseLine, message.Encode());
    }

    [Fact]
    public void SeveralPlayersAndDynamicTunnels()
    {
        var message = new GameBroadcastMessage("B", "9.3.4", 4, "#game", "Room", false, false, false, true, false,
            ["A", "B"], "Map", "Mode", GameBroadcastMessage.DYNAMIC_TUNNELS, "0", "2", "hash", "");

        string encoded = message.Encode();
        Assert.Equal("GAME B;9.3.4;4;#game;Room;00010;A,B;Map;Mode;[DYN];0;2;hash;", encoded);

        Assert.True(GameBroadcastMessage.TryDecode(encoded.Substring(5), out GameBroadcastMessage decoded));
        Assert.True(decoded.IsDynamicTunnels);
        Assert.Equal(["A", "B"], decoded.Players);
        Assert.Equal(encoded, decoded.Encode());
    }

    [Fact]
    public void OtherFieldCountsAreRejectedAndBadFlagsThrow()
    {
        Assert.False(GameBroadcastMessage.TryDecode("A;9.3.4;8", out _));
        Assert.False(GameBroadcastMessage.TryDecode(ReleaseLine + ";extra", out _));
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            GameBroadcastMessage.TryDecode(ReleaseLine.Replace(";11000;", ";1;"), out _));
    }
}
