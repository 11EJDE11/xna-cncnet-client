using ClientLogic.Lan;

using DTAClient.Domain.Multiplayer;

namespace AvClientView.ViewModels;

/// <summary>The LAN game room screen, over a <see cref="LanGameRoom"/>.</summary>
public sealed class LanGameRoomViewModel : MultiplayerRoomViewModel
{
    public override Avalonia.Media.IBrush ChatInputBrush =>
        new Avalonia.Media.SolidColorBrush(AvClientView.Theme.ThemeAssets.ToColor(LanChatColors.All[room.ChatColorIndex].Color));

    private readonly LanGameRoom room;

    public LanGameRoomViewModel(LanGameRoom room, MapLoader mapLoader)
        : base(room, mapLoader)
    {
        this.room = room;
    }

    public override string LayoutIniName => LanGameRoom.LAYOUT_INI_NAME;

    protected override void ToggleRoomLock() => room.ToggleLock();
}
