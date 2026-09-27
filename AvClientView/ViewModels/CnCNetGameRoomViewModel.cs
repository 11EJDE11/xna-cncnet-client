using ClientLogic.CnCNet;

using DTAClient.Domain.Multiplayer;
using DTAClient.Domain.Multiplayer.CnCNet;

namespace AvClientView.ViewModels;

/// <summary>The CnCNet game room screen, over a <see cref="CnCNetGameRoom"/>.</summary>
public sealed class CnCNetGameRoomViewModel : MultiplayerRoomViewModel
{
    private readonly CnCNetGameRoom room;

    public CnCNetGameRoomViewModel(CnCNetGameRoom room, MapLoader mapLoader)
        : base(room, mapLoader)
    {
        this.room = room;
    }

    public override string RoomInfo => string.Format("{0} ({1} players max){2}", room.RoomSettings.RoomName, room.PlayerLimit,
        room.TunnelSession.Mode == TunnelMode.V3Dynamic ? ", dynamic tunnels" : string.Empty);

    protected override string PlayerLaunchText => (room.FindLocalPlayer()?.Ready ?? false) ? "Not Ready" : "I'm Ready";

    protected override string GetPlayerStatus(PlayerInfo pInfo)
    {
        string status = base.GetPlayerStatus(pInfo);
        string ping = pInfo.Ping.IsValid() ? pInfo.Ping.ToString() : string.Empty;
        return string.IsNullOrEmpty(ping) ? status : string.IsNullOrEmpty(status) ? ping : status + "  " + ping;
    }

    protected override void ToggleRoomLock() => room.ToggleLock();
}
