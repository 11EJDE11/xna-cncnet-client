using System;

using Avalonia.Threading;

using ClientCore.Extensions;

using ClientLogic.CnCNet;

using CommunityToolkit.Mvvm.ComponentModel;

using DTAClient.Domain.Multiplayer.CnCNet;

namespace AvClientView.ViewModels;

/// <summary>
/// The XNA TunnelSelectionWindow: the host picks the tunnel mode and, for the static modes, the tunnel server from the
/// TunnelListBox.
/// </summary>
public sealed partial class TunnelSelectionViewModel : ObservableObject
{
    private readonly ITunnelSelectionTarget room;
    private CnCNetTunnel originalTunnel;
    private TunnelMode originalMode;

    public TunnelSelectionViewModel(ITunnelSelectionTarget room, TunnelHandler tunnelHandler)
    {
        this.room = room;
        List = new TunnelListViewModel(tunnelHandler);
        List.Changed += (_, _) => UpdateApplyButton();
        room.TunnelSelectionRequested += (_, description) => Dispatcher.UIThread.Post(() => Open(description));
    }

    /// <summary>The mode drop-down's items and the tunnel list.</summary>
    public TunnelListViewModel List { get; }

    [ObservableProperty]
    private bool isOpen;

    [ObservableProperty]
    private string description = string.Empty;

    [ObservableProperty]
    private int selectedModeIndex;

    [ObservableProperty]
    private bool canApply;

    private TunnelMode SelectedMode => TunnelListViewModel.ModeAt(SelectedModeIndex);

    partial void OnSelectedModeIndexChanged(int value) => List.SetMode(SelectedMode);

    private void UpdateApplyButton()
    {
        TunnelMode mode = SelectedMode;
        if (mode == TunnelMode.V3Dynamic)
        {
            CanApply = originalMode != TunnelMode.V3Dynamic;
            return;
        }

        bool modeChanged = mode != originalMode;
        bool tunnelChanged = originalTunnel == null || !List.State.IsTunnelSelected(originalTunnel.Address, originalTunnel.Port);
        CanApply = List.State.IsValidIndexSelected && (modeChanged || tunnelChanged);
    }

    /// <summary>The Change Tunnel button.</summary>
    public void Open() => Open("Select tunnel server:".L10N("Client:Main:SelectTunnelServer"));

    /// <summary>Opens the window with the current tunnel and mode selected (TunnelSelectionWindow.Open).</summary>
    public void Open(string description)
    {
        if (!room.IsHost)
            return;

        Description = description;
        originalTunnel = room.CurrentTunnel;
        originalMode = room.TunnelMode;

        List.EnsureListed();

        SelectedModeIndex = TunnelListViewModel.IndexOf(originalMode);
        List.SetMode(SelectedMode);

        List.SelectTunnel(SelectedMode != TunnelMode.V3Dynamic ? originalTunnel : null);

        CanApply = false;
        IsOpen = true;
    }

    public void Apply()
    {
        IsOpen = false;

        TunnelMode mode = SelectedMode;
        CnCNetTunnel tunnel = mode == TunnelMode.V3Dynamic ? null : List.State.GetSelectedTunnel();

        if (mode != TunnelMode.V3Dynamic && tunnel == null)
            return;

        room.SelectTunnel(mode, tunnel);
    }

    public void Cancel() => IsOpen = false;
}
