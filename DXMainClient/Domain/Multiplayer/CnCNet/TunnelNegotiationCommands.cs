namespace DTAClient.Domain.Multiplayer.CnCNet;

/// <summary>
/// CTCP command names used to coordinate V3 tunnel negotiation between lobby clients.
/// Shared by CnCNetGameLobby and CnCNetGameLoadingLobby so the wire protocol can't drift
/// between the two lobbies.
/// </summary>
public static class TunnelNegotiationCommands
{
    /// <summary>Reports a player's negotiation status/ping for a given peer.</summary>
    public const string NegotiationInfo = "NEGINFO";

    /// <summary>Asks peers to renegotiate the tunnel they share with the sender.</summary>
    public const string TunnelRenegotiate = "TNLRENEG";

    /// <summary>Notifies the host that the sender can no longer reach a tunnel.</summary>
    public const string TunnelFailed = "TNLFAIL";

    /// <summary>Announces a host-selected tunnel server change.</summary>
    public const string ChangeTunnelServer = "CHTNL";
}
