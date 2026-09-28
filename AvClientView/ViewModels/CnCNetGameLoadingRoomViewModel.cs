using ClientLogic.CnCNet;

using DTAClient.Domain.Multiplayer.CnCNet;

namespace AvClientView.ViewModels;

/// <summary>The CnCNet saved game room (CnCNetGameLoadingLobby), with the host's tunnel selection.</summary>
public sealed class CnCNetGameLoadingRoomViewModel : GameLoadingRoomViewModel
{
    private readonly CnCNetGameLoadingRoom room;

    public CnCNetGameLoadingRoomViewModel(CnCNetGameLoadingRoom room, TunnelHandler tunnelHandler)
        : base(room)
    {
        this.room = room;
        Tunnels = new TunnelSelectionViewModel(room, tunnelHandler);
    }

    public override bool HasChangeTunnelButton => true;

    /// <summary>The Load Game button can be used (not while the host's tunnel is invalid).</summary>
    public bool CanLoad => room.CanLoad;

    public Avalonia.Media.IBrush ChatInputBrush =>
        new Avalonia.Media.SolidColorBrush(AvClientView.Theme.ThemeAssets.ToColor(room.IrcChatColor.Color));

    /// <summary>The host's tunnel selection window (btnChangeTunnel).</summary>
    public TunnelSelectionViewModel Tunnels { get; }

    protected override void OnRefreshed()
    {
        OnPropertyChanged(nameof(CanLoad));
        OnPropertyChanged(nameof(ChatInputBrush));
    }
}
