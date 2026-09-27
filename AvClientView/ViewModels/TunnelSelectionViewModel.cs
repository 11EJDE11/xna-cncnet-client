using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;

using Avalonia.Threading;

using ClientCore.Extensions;

using ClientLogic.CnCNet;
using ClientLogic.Tunnels;

using CommunityToolkit.Mvvm.ComponentModel;

using DTAClient.Domain.Multiplayer.CnCNet;

using Rampastring.Tools;

namespace AvClientView.ViewModels;

/// <summary>A tunnel list row: the flag's offset in flags16.png (null for none), name, official, ping and players.</summary>
public sealed record TunnelRow(int? FlagOffset, string Name, string Official, string Ping, string Players);

/// <summary>
/// The XNA TunnelSelectionWindow: the host picks the tunnel mode and, for the static modes, the tunnel server from the
/// TunnelListBox.
/// </summary>
public sealed partial class TunnelSelectionViewModel : ObservableObject
{
    private static readonly TunnelMode[] modes = [TunnelMode.V3Dynamic, TunnelMode.V3Static, TunnelMode.V2Legacy];

    private readonly CnCNetGameRoom room;
    private readonly TunnelHandler tunnelHandler;
    private readonly TunnelListState state;
    private CnCNetTunnel originalTunnel;
    private TunnelMode originalMode;
    private bool updatingSelection;

    public TunnelSelectionViewModel(CnCNetGameRoom room, TunnelHandler tunnelHandler)
    {
        this.room = room;
        this.tunnelHandler = tunnelHandler;
        state = new TunnelListState(() => tunnelHandler.Tunnels);

        tunnelHandler.TunnelsRefreshed += (_, _) => Dispatcher.UIThread.Post(ListTunnels);
        tunnelHandler.TunnelPinged += (address, port) => Dispatcher.UIThread.Post(() => TunnelPinged(address, port));
        room.TunnelSelectionRequested += (_, description) => Dispatcher.UIThread.Post(() => Open(description));
    }

    public IReadOnlyList<string> ModeItems { get; } =
    [
        "Dynamic (V3)".L10N("Client:Main:TunnelSelModeDynamic"),
        "Static (V3)".L10N("Client:Main:TunnelSelModeStatic"),
        "Legacy (V2)".L10N("Client:Main:TunnelSelModeLegacy"),
    ];

    public ObservableCollection<TunnelRow> Tunnels { get; } = [];

    [ObservableProperty]
    private bool isOpen;

    [ObservableProperty]
    private string description = string.Empty;

    [ObservableProperty]
    private int selectedModeIndex;

    [ObservableProperty]
    private int selectedTunnelIndex = -1;

    /// <summary>The list can be used (not in dynamic mode, where it's covered).</summary>
    [ObservableProperty]
    private bool isListEnabled;

    [ObservableProperty]
    private bool canApply;

    private TunnelMode SelectedMode => SelectedModeIndex >= 0 && SelectedModeIndex < modes.Length ? modes[SelectedModeIndex] : TunnelMode.V3Dynamic;

    private void ListTunnels()
    {
        state.Refresh();

        updatingSelection = true;
        Tunnels.Clear();
        foreach (CnCNetTunnel tunnel in state.Tunnels)
        {
            Tunnels.Add(new TunnelRow(TunnelFlags.GetFlagOffset(tunnel.CountryCode), tunnel.Name,
                Conversions.BooleanToString(tunnel.Official, BooleanStringStyle.YESNO), tunnel.Ping.ToString(),
                tunnel.Clients + " / " + tunnel.MaxClients));
        }

        SelectedTunnelIndex = state.SelectedIndex;
        updatingSelection = false;
        UpdateApplyButton();
    }

    private void TunnelPinged(string address, int port)
    {
        int index = state.OnTunnelPinged(address, port);
        if (index < 0 || index >= Tunnels.Count)
            return;

        CnCNetTunnel tunnel = state.Tunnels[index];
        updatingSelection = true;
        Tunnels[index] = Tunnels[index] with { Ping = tunnel.Ping.ToString() };
        SelectedTunnelIndex = state.SelectedIndex;
        updatingSelection = false;
        UpdateApplyButton();
    }

    partial void OnSelectedTunnelIndexChanged(int value)
    {
        if (updatingSelection)
            return;

        state.Select(value);
        UpdateApplyButton();
    }

    partial void OnSelectedModeIndexChanged(int value) => ModeChanged();

    /// <summary>DdMode_SelectedIndexChanged.</summary>
    private void ModeChanged()
    {
        TunnelMode mode = SelectedMode;
        IsListEnabled = mode != TunnelMode.V3Dynamic;

        int version = mode == TunnelMode.V2Legacy ? 2 : 3;
        if (state.TargetVersion != version)
        {
            state.TargetVersion = version;
            if (Tunnels.Count > 0)
                ListTunnels();
        }

        UpdateApplyButton();
    }

    private void UpdateApplyButton()
    {
        TunnelMode mode = SelectedMode;
        if (mode == TunnelMode.V3Dynamic)
        {
            CanApply = originalMode != TunnelMode.V3Dynamic;
            return;
        }

        bool modeChanged = mode != originalMode;
        bool tunnelChanged = originalTunnel == null || !state.IsTunnelSelected(originalTunnel.Address, originalTunnel.Port);
        CanApply = state.IsValidIndexSelected && (modeChanged || tunnelChanged);
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

        // The XNA window's list has been filled since the lobby started; this one may not have been yet
        if (Tunnels.Count == 0 && tunnelHandler.Tunnels.Count > 0)
            ListTunnels();

        SelectedModeIndex = Math.Max(0, Array.IndexOf(modes, originalMode));
        ModeChanged();

        updatingSelection = true;
        if (SelectedMode != TunnelMode.V3Dynamic && originalTunnel != null)
        {
            state.SelectTunnel(originalTunnel.Address, originalTunnel.Port);
            SelectedTunnelIndex = state.SelectedIndex;
        }
        else
        {
            state.Select(-1);
            SelectedTunnelIndex = -1;
        }

        updatingSelection = false;

        CanApply = false;
        IsOpen = true;
    }

    public void Apply()
    {
        IsOpen = false;

        TunnelMode mode = SelectedMode;
        CnCNetTunnel tunnel = mode == TunnelMode.V3Dynamic ? null : state.GetSelectedTunnel();

        if (mode != TunnelMode.V3Dynamic && tunnel == null)
            return;

        room.SelectTunnel(mode, tunnel);
    }

    public void Cancel() => IsOpen = false;
}
