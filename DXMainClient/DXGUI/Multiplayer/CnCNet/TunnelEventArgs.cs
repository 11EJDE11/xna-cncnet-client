using DTAClient.Domain.Multiplayer.CnCNet;
using System;

namespace DTAClient.DXGUI.Multiplayer.CnCNet;

class TunnelEventArgs : EventArgs
{
    public TunnelEventArgs(CnCNetTunnel tunnel, TunnelMode mode)
    {
        Tunnel = tunnel;
        Mode = mode;
    }

    public CnCNetTunnel Tunnel { get; }
    public TunnelMode Mode { get; }
}