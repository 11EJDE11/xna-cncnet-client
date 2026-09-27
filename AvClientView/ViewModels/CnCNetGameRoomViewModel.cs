using ClientLogic.CnCNet;

using DTAClient.Domain.Multiplayer;
using DTAClient.Domain.Multiplayer.CnCNet;

namespace AvClientView.ViewModels;

/// <summary>The CnCNet game room screen, over a <see cref="CnCNetGameRoom"/>.</summary>
public sealed class CnCNetGameRoomViewModel : MultiplayerRoomViewModel
{
    private readonly CnCNetGameRoom room;

    public CnCNetGameRoomViewModel(CnCNetGameRoom room, MapLoader mapLoader, TunnelHandler tunnelHandler, ClientLogic.UI.IDialogService dialogs)
        : base(room, mapLoader)
    {
        this.room = room;
        Settings = new GameLobbySettingsViewModel(room, dialogs);
        Tunnels = new TunnelSelectionViewModel(room, tunnelHandler);
    }

    /// <summary>The host's game lobby settings window (btnGameLobbySettings).</summary>
    public GameLobbySettingsViewModel Settings { get; }

    /// <summary>The host's tunnel selection window (btnChangeTunnel, /CHANGETUNNEL).</summary>
    public TunnelSelectionViewModel Tunnels { get; }

    public override string RoomInfo => string.Format("{0} ({1} players max){2}", room.RoomSettings.RoomName, room.PlayerLimit,
        room.TunnelSession.Mode == TunnelMode.V3Dynamic ? ", dynamic tunnels" : string.Empty);

    protected override int PlayerLimit => room.PlayerLimit;

    protected override string PlayerLaunchText => (room.FindLocalPlayer()?.Ready ?? false) ? "Not Ready" : "I'm Ready";

    protected override string GetPlayerStatus(PlayerInfo pInfo)
    {
        string status = base.GetPlayerStatus(pInfo);
        string ping = pInfo.Ping.IsValid() ? pInfo.Ping.ToString() : string.Empty;
        return string.IsNullOrEmpty(ping) ? status : string.IsNullOrEmpty(status) ? ping : status + "  " + ping;
    }

    public override string LayoutIniName => CnCNetGameRoom.LAYOUT_INI_NAME;

    protected override void ToggleRoomLock() => room.ToggleLock();
}
