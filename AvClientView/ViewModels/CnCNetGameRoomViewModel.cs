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

    public override Avalonia.Media.IBrush ChatInputBrush =>
        new Avalonia.Media.SolidColorBrush(AvClientView.Theme.ThemeAssets.ToColor(room.IrcChatColor.Color));

    protected override void ToggleRoomLock() => room.ToggleLock();

    /// <summary>The tunnel negotiation status panel is shown (dynamic tunnels only).</summary>
    public bool ShowNegotiationStatus => room.ShowNegotiationStatus && room.TunnelMode == TunnelMode.V3Dynamic;

    /// <summary>The players, in the room's order.</summary>
    public System.Collections.Generic.List<string> NegotiationPlayers => room.Players.ConvertAll(p => p.Name);

    public ClientLogic.Tunnels.NegotiationPairRow NegotiationPair(string player1, string player2)
    {
        var status = ClientLogic.Tunnels.NegotiationStatusRows.DisplayStatus(room.NegotiationData.GetNegotiationStatus(player1, player2), true);
        var ping = room.NegotiationData.GetPing(player1, player2);
        (string text, PingQualityTier tier) = ClientLogic.Tunnels.NegotiationStatusRows.GetLabel(status, ping);
        return new(player1, player2, status, ping, text, tier, null);
    }

    public System.Collections.Generic.List<ClientLogic.Tunnels.NegotiationPairRow> NegotiationRows =>
        ClientLogic.Tunnels.NegotiationStatusRows.ListPairs(NegotiationPlayers, room.NegotiationData, inferInProgress: true);

    /// <summary>The room uses dynamic tunnels: the negotiation status button can be used.</summary>
    public bool IsDynamicTunnel => room.TunnelMode == TunnelMode.V3Dynamic;

    /// <summary>The theme's btnNegotiationStatus (ToggleNegotiationStatus).</summary>
    public void ToggleNegotiationStatus() => room.ToggleNegotiationStatus();

    public void CloseNegotiationStatus()
    {
        if (room.ShowNegotiationStatus)
            room.ToggleNegotiationStatus();
    }

    public void RenegotiateAll() => room.TriggerRenegotiateAll();

    /// <summary>The mouse moved over the room: the host isn't inactive.</summary>
    public void ResetInactivity() => room.ResetInactivity();
}
