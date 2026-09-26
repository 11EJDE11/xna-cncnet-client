using System.Collections.Generic;

using ClientLogic.Lobby;
using ClientLogic.Protocol;

using Xunit;

namespace ClientLogic.Tests.Lobby;

public class GameRoomSettingsTests
{
    private static GameRoomSettings Create()
    {
        TestGame.EnsureInitialized();
        return new GameRoomSettings { RoomName = "Room", PlayerLimit = 8, SkillLevel = 0, IsCustomPassword = false };
    }

    [Fact]
    public void HostSettingsAreAppliedWithNoticesAndChangeEvents()
    {
        GameRoomSettings settings = Create();
        var changed = new List<string>();
        settings.PropertyChanged += (_, e) => changed.Add(e.PropertyName);

        IReadOnlyList<string> notices = settings.ApplyFromHost(new GameRoomSettingsMessage("New room", 4, 1, true), "Host");

        Assert.Equal(("New room", 4, 1, true), (settings.RoomName, settings.PlayerLimit, settings.SkillLevel, settings.IsCustomPassword));
        Assert.Equal(3, notices.Count);
        Assert.Contains("New room", notices[0]);
        Assert.Contains("4", notices[1]);
        Assert.Equal(new[] { "RoomName", "PlayerLimit", "SkillLevel", "IsCustomPassword" }, changed);
    }

    [Fact]
    public void UnchangedSettingsGiveNoNoticesOrEvents()
    {
        GameRoomSettings settings = Create();
        var changed = new List<string>();
        settings.PropertyChanged += (_, e) => changed.Add(e.PropertyName);

        IReadOnlyList<string> notices = settings.ApplyFromHost(settings.ToMessage(), "Host");

        Assert.Empty(notices);
        Assert.Empty(changed);
    }

    [Fact]
    public void SkillLevelIsClampedToTheConfiguredOptions()
    {
        GameRoomSettings settings = Create();

        settings.ApplyFromHost(new GameRoomSettingsMessage("Room", 8, 99, false), "Host");

        // The test client definitions use the default options: Any, Beginner, Intermediate, Pro
        Assert.Equal(3, settings.SkillLevel);
    }
}
