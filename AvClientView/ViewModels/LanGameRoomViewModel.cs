using ClientLogic.Lan;

using DTAClient.Domain.Multiplayer;

namespace AvClientView.ViewModels;

/// <summary>The LAN game room screen, over a <see cref="LanGameRoom"/>.</summary>
public sealed class LanGameRoomViewModel : MultiplayerRoomViewModel
{
    private readonly LanGameRoom room;

    public LanGameRoomViewModel(LanGameRoom room, MapLoader mapLoader)
        : base(room, mapLoader)
    {
        this.room = room;
    }

    protected override void ToggleRoomLock() => room.ToggleLock();
}
