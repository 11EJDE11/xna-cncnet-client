using System.Collections.Generic;

using ClientCore;
using ClientCore.Extensions;

using ClientLogic.Protocol;

using CommunityToolkit.Mvvm.ComponentModel;

namespace ClientLogic.Lobby;

/// <summary>
/// The settings of a CnCNet game room that the host can change: name, player limit, skill level and whether it
/// has a custom password. Clients receive them in the GSETTINGS message.
/// </summary>
public sealed partial class GameRoomSettings : ObservableObject
{
    public GameRoomSettings()
    {
        roomName = string.Empty;
        skillLevel = ClientConfiguration.Instance.DefaultSkillLevelIndex;
    }

    [ObservableProperty]
    private string roomName;

    [ObservableProperty]
    private int playerLimit;

    [ObservableProperty]
    private int skillLevel;

    [ObservableProperty]
    private bool isCustomPassword;

    public GameRoomSettingsMessage ToMessage() => new(RoomName, PlayerLimit, SkillLevel, IsCustomPassword);

    /// <summary>
    /// Applies the settings the host sent and returns chat notices for what changed, in order.
    /// </summary>
    public IReadOnlyList<string> ApplyFromHost(GameRoomSettingsMessage message, string hostName)
    {
        int newSkillLevel = ClientConfiguration.Instance.NormalizeSkillLevel(message.SkillLevel);

        bool gameNameChanged = RoomName != message.RoomName;
        bool maxPlayersChanged = PlayerLimit != message.PlayerLimit;
        bool skillLevelChanged = SkillLevel != newSkillLevel;

        RoomName = message.RoomName;
        PlayerLimit = message.PlayerLimit;
        SkillLevel = newSkillLevel;
        IsCustomPassword = message.IsCustomPassword;

        var notices = new List<string>();

        if (gameNameChanged)
        {
            notices.Add(string.Format("{0} changed game room name to \"{1}\"."
                .L10N("Client:Main:HostChangedGameName"), hostName, RoomName));
        }

        if (maxPlayersChanged)
        {
            notices.Add(string.Format("{0} changed maximum players to {1}."
                .L10N("Client:Main:HostChangedMaxPlayers"), hostName, PlayerLimit));
        }

        if (skillLevelChanged)
        {
            notices.Add(string.Format("{0} changed skill level to {1}."
                .L10N("Client:Main:HostChangedSkillLevel"), hostName, GetSkillLevelName(SkillLevel)));
        }

        return notices;
    }

    /// <summary>The translated name of a skill level.</summary>
    public static string GetSkillLevelName(int skillLevel)
    {
        string[] skillLevelOptions = ClientConfiguration.Instance.GetSkillLevelOptions();
        string skillLevelName = skillLevelOptions[skillLevel];
        return skillLevelName.L10N($"INI:ClientDefinitions:SkillLevel:{skillLevel}");
    }
}
