using System;

#nullable enable

namespace DTAClient.Domain.Multiplayer.CnCNet;

public class TunnelChosenEventArgs : EventArgs
{
    public uint PlayerId { get; set; }
    public string PlayerName { get; set; } = string.Empty;
    public CnCNetTunnel? ChosenTunnel { get; set; }
    public bool IsLocalDecision { get; set; }
    public string? FailureReason { get; set; }
    public int NegotiationPing { get; set; }
}
