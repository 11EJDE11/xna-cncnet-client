using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.Linq;

using ClientLogic.Protocol;

using Rampastring.Tools;

using Xunit;

namespace ClientLogic.Tests.Protocol;

public class LobbyMessageTests
{
    private static GameOptionsMessage CreateGameOptions(int checkBoxCount, int dropDownCount) => new()
    {
        CheckBoxValues = Enumerable.Range(0, checkBoxCount).Select(i => i % 3 == 0 || i == checkBoxCount - 1).ToList(),
        DropDownIndices = Enumerable.Range(0, dropDownCount).Select(i => i * 2).ToList(),
        IsMapOfficial = true,
        MapSHA1 = "0123456789abcdef0123456789abcdef01234567",
        GameModeName = "Free For All",
        FrameSendRate = 7,
        MaxAhead = 40,
        ProtocolVersion = 2,
        RandomSeed = -123456789,
        RemoveStartingLocations = true,
        MapName = "[4] Map; with ; semicolons 100%",
        TunnelMode = 2,
    };

    [Theory]
    [InlineData(0, 0)]
    [InlineData(1, 3)]
    [InlineData(31, 5)]
    [InlineData(32, 5)]
    [InlineData(33, 5)]
    [InlineData(64, 1)]
    [InlineData(65, 0)]
    public void GameOptionsRoundTrip(int checkBoxCount, int dropDownCount)
    {
        GameOptionsMessage sent = CreateGameOptions(checkBoxCount, dropDownCount);

        Assert.True(GameOptionsMessage.TryDecode(sent.Encode(), checkBoxCount, dropDownCount, out GameOptionsMessage received));

        Assert.Equal(sent.CheckBoxValues, received.CheckBoxValues);
        Assert.Equal(sent.DropDownIndices, received.DropDownIndices);
        Assert.Equal(sent with { CheckBoxValues = received.CheckBoxValues, DropDownIndices = received.DropDownIndices }, received);
    }

    [Fact]
    public void GameOptionsFromDifferentOptionCountsAreRejected()
    {
        string payload = CreateGameOptions(32, 5).Encode();

        Assert.False(GameOptionsMessage.TryDecode(payload, 33, 5, out _));
        Assert.False(GameOptionsMessage.TryDecode(payload, 32, 4, out _));
    }

    [Fact]
    public void MalformedGameOptionsAreRejected()
    {
        string payload = CreateGameOptions(3, 2).Encode();
        string[] fields = payload.Split(';');

        // Truncated, extra field, bad random seed (field 10 counting from 0: count, packed, count, 2 dd, official, sha, mode, fsr, maxahead, protocol, seed)
        Assert.False(GameOptionsMessage.TryDecode(string.Join(";", fields.Take(fields.Length - 1)), 3, 2, out _));
        Assert.False(GameOptionsMessage.TryDecode(payload + ";1", 3, 2, out _));

        int seedIndex = 11;
        Assert.Equal("-123456789", fields[seedIndex]);
        fields[seedIndex] = "not-a-seed";
        Assert.False(GameOptionsMessage.TryDecode(string.Join(";", fields), 3, 2, out _));
    }

    [Fact]
    public void PlayerOptionsRoundTrip()
    {
        var sent = new PlayerOptionsMessage(
        [
            new PlayerOptionsEntry("Host;Name%", -1, new PackedPlayerOptions(3, 2, 1, 1), 1, "192.168.0.10"),
            new PlayerOptionsEntry("Guest", -1, new PackedPlayerOptions(0, 0, 0, 0), 2, string.Empty),
            new PlayerOptionsEntry(string.Empty, 2, new PackedPlayerOptions(7, 8, 4, 2), 1, string.Empty),
        ]);

        Assert.True(PlayerOptionsMessage.TryDecode(sent.Encode(), out PlayerOptionsMessage received));
        Assert.Equal(sent.Players, received.Players);
        Assert.True(received.Players[2].IsAI);
        Assert.False(received.Players[0].IsAI);
    }

    [Theory]
    [InlineData("1;Alice;-1;0;1")]               // missing address
    [InlineData("2;Alice;-1;0;1;")]              // second player missing
    [InlineData("1;;-1;0;1;")]                   // human without a name
    [InlineData("1;Alice;3;0;1;")]               // AI level out of range
    [InlineData("1;Alice;-1;0;3;")]              // ready state out of range
    [InlineData("1;Alice;-1;x;1;")]              // options not a number
    [InlineData("1;Alice;-1;0;1;;extra")]        // trailing data
    public void MalformedPlayerOptionsAreRejected(string payload)
    {
        Assert.False(PlayerOptionsMessage.TryDecode(payload, out _));
    }

    [Fact]
    public void RequestsAndSettingsRoundTrip()
    {
        var request = new PlayerOptionsRequestMessage(new PackedPlayerOptions(5, 6, 7, 3));
        Assert.True(PlayerOptionsRequestMessage.TryDecode(request.Encode(), out var decodedRequest));
        Assert.Equal(request, decodedRequest);

        for (int state = 0; state <= 2; state++)
        {
            Assert.True(ReadyRequestMessage.TryDecode(new ReadyRequestMessage(state).Encode(), out var ready));
            Assert.Equal(state, ready.ReadyState);
        }

        Assert.False(ReadyRequestMessage.TryDecode("3", out _));

        var settings = new GameRoomSettingsMessage("Room; with semicolons", 6, 2, true);
        Assert.True(GameRoomSettingsMessage.TryDecode(settings.Encode(), out var decodedSettings));
        Assert.Equal(settings, decodedSettings);
    }

    [Fact]
    public void StartMessagesRoundTrip()
    {
        var v2 = new StartV2Message(4242, [new StartV2Entry("Alice", "1.2.3.4", 50000), new StartV2Entry("B;ob", "1.2.3.4", -17618)]);
        Assert.True(StartV2Message.TryDecode(v2.Encode(), out var decodedV2));
        Assert.Equal(v2.GameId, decodedV2.GameId);
        Assert.Equal(v2.Players, decodedV2.Players);

        var v3 = new StartV3Message(7, [new StartV3Entry(uint.MaxValue, "Alice", "2001:db8::1", 50000), new StartV3Entry(1, "Bob", "0.0.0.0", 0)]);
        Assert.True(StartV3Message.TryDecode(v3.Encode(), out var decodedV3));
        Assert.Equal(v3.GameId, decodedV3.GameId);
        Assert.Equal(v3.Players, decodedV3.Players);

        Assert.True(LaunchMessage.TryDecode(new LaunchMessage(99).Encode(), out var launch));
        Assert.Equal(99, launch.GameId);

        Assert.False(StartV3Message.TryDecode("7;2;1;Alice;1.2.3.4;50000", out _));
        Assert.False(LaunchMessage.TryDecode("-1", out _));
    }

    [Theory]
    [InlineData(0, 0)]
    [InlineData(5, 0)]
    [InlineData(0, 3)]
    [InlineData(31, 2)]
    [InlineData(32, 2)]
    [InlineData(33, 2)]
    [InlineData(70, 4)]
    public void BroadcastedGameOptionValuesMatchTheOldPacking(int checkBoxCount, int dropDownCount)
    {
        var random = new Random(checkBoxCount * 100 + dropDownCount);
        bool[] checkBoxes = Enumerable.Range(0, checkBoxCount).Select(_ => random.Next(2) == 1).ToArray();
        int[] dropDowns = Enumerable.Range(0, dropDownCount).Select(_ => random.Next(10)).ToArray();

        string encoded = BroadcastedGameOptionValues.Encode(checkBoxes, dropDowns);

        Assert.Equal(OldPacking(checkBoxes, dropDowns), encoded);

        int[] decoded = BroadcastedGameOptionValues.Decode(encoded, checkBoxCount, dropDownCount);
        if (checkBoxCount + dropDownCount == 0)
        {
            Assert.Null(decoded);
        }
        else
        {
            Assert.Equal(checkBoxes.Select(c => c ? 1 : 0).Concat(dropDowns), decoded);
        }
    }

    /// <summary>GameLobbyBase.GetPackedGameOptionValuesString before the codec existed.</summary>
    private static string OldPacking(bool[] checkboxValues, int[] dropDowns)
    {
        var values = new List<int>();

        if (checkboxValues.Length > 0)
        {
            List<byte> byteList = Conversions.BoolArrayIntoBytes(checkboxValues).ToList();
            while (byteList.Count % 4 != 0)
                byteList.Add(0);
            byte[] byteArray = byteList.ToArray();

            for (int i = 0; i < byteArray.Length / 4; i++)
                values.Add(BinaryPrimitives.ReadInt32LittleEndian(byteArray.AsSpan(i * 4)));
        }

        values.AddRange(dropDowns);

        return values.Count > 0 ? string.Join(",", values) : string.Empty;
    }
}
