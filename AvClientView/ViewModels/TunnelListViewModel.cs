using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;

using Avalonia.Threading;

using ClientCore.Extensions;

using ClientLogic.Tunnels;

using CommunityToolkit.Mvvm.ComponentModel;

using DTAClient.Domain.Multiplayer.CnCNet;

using Rampastring.Tools;

namespace AvClientView.ViewModels;

/// <summary>A tunnel list row: the flag's offset in flags16.png (null for none), name, official, ping and players.</summary>
public sealed record TunnelRow(int? FlagOffset, string Name, string Official, string Ping, string Players);

/// <summary>
/// The XNA TunnelListBox with its tunnel mode drop-down: the tunnels of the mode's version, kept up to date with the
/// tunnel handler, the best one selected until the user picks one (<see cref="TunnelListState"/>).
/// </summary>
public sealed partial class TunnelListViewModel : ObservableObject
{
    public static readonly TunnelMode[] Modes = [TunnelMode.V3Dynamic, TunnelMode.V3Static, TunnelMode.V2Legacy];

    private readonly TunnelHandler tunnelHandler;
    private bool updatingSelection;

    public TunnelListViewModel(TunnelHandler tunnelHandler)
    {
        this.tunnelHandler = tunnelHandler;
        State = new TunnelListState(() => tunnelHandler.Tunnels);

        tunnelHandler.TunnelsRefreshed += (_, _) => Dispatcher.UIThread.Post(ListTunnels);
        tunnelHandler.TunnelPinged += (address, port) => Dispatcher.UIThread.Post(() => TunnelPinged(address, port));
    }

    public TunnelListState State { get; }

    public IReadOnlyList<string> ModeItems { get; } =
    [
        "Dynamic (V3)".L10N("Client:Main:TunnelSelModeDynamic"),
        "Static (V3)".L10N("Client:Main:TunnelSelModeStatic"),
        "Legacy (V2)".L10N("Client:Main:TunnelSelModeLegacy"),
    ];

    public ObservableCollection<TunnelRow> Tunnels { get; } = [];

    [ObservableProperty]
    private int selectedTunnelIndex = -1;

    /// <summary>The list can be used (not in dynamic mode, where it's covered).</summary>
    [ObservableProperty]
    private bool isListEnabled;

    /// <summary>The list or its selection changed.</summary>
    public event EventHandler Changed;

    public static TunnelMode ModeAt(int index) => index >= 0 && index < Modes.Length ? Modes[index] : TunnelMode.V3Dynamic;

    public static int IndexOf(TunnelMode mode) => Math.Max(0, Array.IndexOf(Modes, mode));

    /// <summary>Lists the tunnels if the list hasn't been filled yet (the XNA list has been since the lobby started).</summary>
    public void EnsureListed()
    {
        if (Tunnels.Count == 0 && tunnelHandler.Tunnels.Count > 0)
            ListTunnels();
    }

    private void ListTunnels()
    {
        State.Refresh();

        updatingSelection = true;
        Tunnels.Clear();
        foreach (CnCNetTunnel tunnel in State.Tunnels)
        {
            Tunnels.Add(new TunnelRow(TunnelFlags.GetFlagOffset(tunnel.CountryCode), tunnel.Name,
                Conversions.BooleanToString(tunnel.Official, BooleanStringStyle.YESNO), tunnel.Ping.ToString(),
                tunnel.Clients + " / " + tunnel.MaxClients));
        }

        SelectedTunnelIndex = State.SelectedIndex;
        updatingSelection = false;
        Changed?.Invoke(this, EventArgs.Empty);
    }

    private void TunnelPinged(string address, int port)
    {
        int index = State.OnTunnelPinged(address, port);
        if (index < 0 || index >= Tunnels.Count)
            return;

        CnCNetTunnel tunnel = State.Tunnels[index];
        updatingSelection = true;
        Tunnels[index] = Tunnels[index] with { Ping = tunnel.Ping.ToString() };
        SelectedTunnelIndex = State.SelectedIndex;
        updatingSelection = false;
        Changed?.Invoke(this, EventArgs.Empty);
    }

    partial void OnSelectedTunnelIndexChanged(int value)
    {
        if (updatingSelection)
            return;

        State.Select(value);
        Changed?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>The mode drop-down changed: the list is covered in dynamic mode and lists the mode's version.</summary>
    public void SetMode(TunnelMode mode)
    {
        IsListEnabled = mode != TunnelMode.V3Dynamic;

        int version = mode == TunnelMode.V2Legacy ? 2 : 3;
        if (State.TargetVersion != version)
        {
            State.TargetVersion = version;
            if (Tunnels.Count > 0)
                ListTunnels();
        }

        Changed?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>Selects a tunnel, or nothing for null.</summary>
    public void SelectTunnel(CnCNetTunnel tunnel)
    {
        updatingSelection = true;
        if (tunnel != null)
            State.SelectTunnel(tunnel.Address, tunnel.Port);
        else
            State.Select(-1);

        SelectedTunnelIndex = State.SelectedIndex;
        updatingSelection = false;
        Changed?.Invoke(this, EventArgs.Empty);
    }
}
