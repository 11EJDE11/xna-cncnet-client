using System.Collections.Generic;
using System.Linq;

using ClientCore;
using ClientCore.Extensions;

using ClientLogic.CnCNet;
using ClientLogic.UI;

using CommunityToolkit.Mvvm.ComponentModel;

using DTAClient.Domain.Multiplayer.CnCNet;

namespace AvClientView.ViewModels;

/// <summary>The XNA GameLobbySettingsWindow: the host changes the room's name, password, player limit and skill level.</summary>
public sealed partial class GameLobbySettingsViewModel : ObservableObject
{
    private readonly CnCNetGameRoom room;
    private readonly IDialogService dialogs;

    public GameLobbySettingsViewModel(CnCNetGameRoom room, IDialogService dialogs)
    {
        this.room = room;
        this.dialogs = dialogs;

        SkillLevels = ClientConfiguration.Instance.GetSkillLevelOptions()
            .Select((level, i) => level.L10N($"INI:ClientDefinitions:SkillLevel:{i}")).ToList();
        skillLevel = ClientConfiguration.Instance.DefaultSkillLevelIndex;
    }

    /// <summary>8 down to 2.</summary>
    public IReadOnlyList<string> MaxPlayerItems { get; } = Enumerable.Range(2, 7).Reverse().Select(i => i.ToString()).ToList();

    public IReadOnlyList<string> SkillLevels { get; }

    [ObservableProperty]
    private bool isOpen;

    [ObservableProperty]
    private string gameName = string.Empty;

    [ObservableProperty]
    private string password = string.Empty;

    [ObservableProperty]
    private int maxPlayersIndex;

    [ObservableProperty]
    private int skillLevel;

    /// <summary>BtnGameLobbySettings_LeftClick: the host opens the window with the room's settings.</summary>
    public void Open()
    {
        if (!room.IsHost)
            return;

        GameName = room.RoomSettings.RoomName;
        Password = room.CustomPassword ?? string.Empty;
        MaxPlayersIndex = 8 - room.RoomSettings.PlayerLimit;
        SkillLevel = room.RoomSettings.SkillLevel;
        IsOpen = true;
    }

    public void Save()
    {
        string gameName = NameValidator.GetSanitizedGameName(GameName ?? string.Empty);

        NameValidationError validationError = NameValidator.IsGameNameValid(gameName, out string errorMessage);
        if (validationError != NameValidationError.None)
        {
            dialogs.ShowMessage("Invalid game name".L10N("Client:Main:InvalidGameName"), errorMessage);
            return;
        }

        int maxPlayers = int.Parse(MaxPlayerItems[System.Math.Max(0, MaxPlayersIndex)]);

        if (room.IsHost)
            room.UpdateGameLobbySettings(gameName, maxPlayers, SkillLevel, Password ?? string.Empty);

        IsOpen = false;
    }

    public void Cancel() => IsOpen = false;
}
