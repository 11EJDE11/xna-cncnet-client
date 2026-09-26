using System.Collections.Generic;

using ClientLogic.Lobby;
using ClientLogic.Protocol;

using DTAClient.Domain.Multiplayer;

using Xunit;

namespace ClientLogic.Tests.Lobby;

public class GameOptionsApplierTests
{
    private static readonly GameModeMap fourBattle = TestGame.LoadGameModeMap("Maps/Test/four", "Battle");
    private static readonly GameModeMap openBattle = TestGame.LoadGameModeMap("Maps/Test/open", "Battle");

    private static readonly LobbyGameSettings current = new(FrameSendRate: 7, MaxAhead: 0, ProtocolVersion: 2, GameModeMap: fourBattle);

    private static GameModeMap Find(string gameMode, string sha1)
    {
        foreach (GameModeMap gmm in new[] { fourBattle, openBattle })
        {
            if (gmm.GameMode.Name == gameMode && gmm.Map.SHA1 == sha1)
                return gmm;
        }

        return null;
    }

    private static GameOptionsMessage Message(GameModeMap gameModeMap = null, string sha1 = null, bool official = true) => new()
    {
        CheckBoxValues = [true, false],
        DropDownIndices = [1],
        IsMapOfficial = official,
        MapSHA1 = sha1 ?? gameModeMap?.Map.SHA1 ?? string.Empty,
        GameModeName = gameModeMap?.GameMode.Name ?? "Battle",
        FrameSendRate = 7,
        MaxAhead = 0,
        ProtocolVersion = 2,
        RandomSeed = 99,
        RemoveStartingLocations = true,
        MapName = "Some map",
        TunnelMode = 1,
    };

    [Fact]
    public void SameMapAndSettingsChangeNothingButTheOptions()
    {
        GameOptionsUpdate update = GameOptionsApplier.Plan(Message(fourBattle), current, Find, isMapSharingEnabled: true);

        Assert.Equal(GameOptionsMapAction.None, update.MapAction);
        Assert.Null(update.GameModeMap);
        Assert.Empty(update.SettingNotices);
        Assert.Equal(new[] { true, false }, update.CheckBoxValues);
        Assert.Equal(new[] { 1 }, update.DropDownIndices);
        Assert.Equal(99, update.RandomSeed);
        Assert.True(update.RemoveStartingLocations);
        Assert.Equal(1, update.TunnelMode);
    }

    [Fact]
    public void AnotherInstalledMapIsSelected()
    {
        GameOptionsUpdate update = GameOptionsApplier.Plan(Message(openBattle), current, Find, isMapSharingEnabled: true);

        Assert.Equal(GameOptionsMapAction.Change, update.MapAction);
        Assert.Same(openBattle, update.GameModeMap);
    }

    [Theory]
    [InlineData("", true, true, GameOptionsMapAction.Clear)]
    [InlineData("0000000000000000000000000000000000000000", true, true, GameOptionsMapAction.ClearAndReportOfficialMapMissing)]
    [InlineData("0000000000000000000000000000000000000000", false, true, GameOptionsMapAction.ClearAndRequestDownload)]
    [InlineData("0000000000000000000000000000000000000000", false, false, GameOptionsMapAction.ClearAndReportMapSharingDisabled)]
    public void MissingMaps(string sha1, bool official, bool mapSharing, GameOptionsMapAction expected)
    {
        GameOptionsUpdate update = GameOptionsApplier.Plan(Message(sha1: sha1, official: official), current, Find, mapSharing);

        Assert.Equal(expected, update.MapAction);
        Assert.Null(update.GameModeMap);
        Assert.Equal(sha1, update.MapSHA1);
        Assert.Equal("Some map", update.MapName);
    }

    [Fact]
    public void ChangedSettingsGetNoticesInOrder()
    {
        GameOptionsMessage message = Message(fourBattle) with { FrameSendRate = 3, MaxAhead = 20, ProtocolVersion = 0 };

        GameOptionsUpdate update = GameOptionsApplier.Plan(message, current, Find, isMapSharingEnabled: true);

        Assert.Equal(3, update.SettingNotices.Count);
        Assert.Contains("FrameSendRate", update.SettingNotices[0]);
        Assert.Contains("MaxAhead", update.SettingNotices[1]);
        Assert.Contains("ProtocolVersion", update.SettingNotices[2]);
        Assert.Equal((3, 20, 0), (update.FrameSendRate, update.MaxAhead, update.ProtocolVersion));
    }

    [Fact]
    public void NoticeTexts()
    {
        Assert.Equal("The game host has enabled Short Game", GameOptionNotices.CheckBoxChanged("Short Game", true));
        Assert.Equal("The game host has disabled Short Game", GameOptionNotices.CheckBoxChanged("Short Game", false));
        Assert.Equal("The game host has set Credits to 10000", GameOptionNotices.DropDownChanged("Credits", "10000"));
    }
}
