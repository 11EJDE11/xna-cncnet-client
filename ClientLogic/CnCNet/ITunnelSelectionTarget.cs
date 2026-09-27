using System;

using DTAClient.Domain.Multiplayer.CnCNet;

namespace ClientLogic.CnCNet;

/// <summary>A room whose host can pick the tunnel mode and server (the XNA TunnelSelectionWindow's lobby).</summary>
public interface ITunnelSelectionTarget
{
    bool IsHost { get; }

    /// <summary>The tunnel the room uses (null in dynamic V3 mode until one is negotiated).</summary>
    CnCNetTunnel CurrentTunnel { get; }

    TunnelMode TunnelMode { get; }

    /// <summary>The room asks the front end to open the tunnel selection with this description.</summary>
    event EventHandler<string> TunnelSelectionRequested;

    /// <summary>Host: switches to a tunnel mode and, for the static modes, a tunnel server.</summary>
    void SelectTunnel(TunnelMode mode, CnCNetTunnel tunnel);
}
