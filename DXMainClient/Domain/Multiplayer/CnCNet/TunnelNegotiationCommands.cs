#nullable enable

namespace DTAClient.Domain.Multiplayer.CnCNet;

/// <summary>
/// CTCP command names used to coordinate V3 tunnel negotiation between lobby clients.
/// Shared by CnCNetGameLobby and CnCNetGameLoadingLobby so the wire protocol can't drift
/// between the two lobbies.
/// </summary>
public static class TunnelNegotiationCommands
{
    /// <summary>Full negotiation state report: all known local→peer statuses packed into one coalescing message.</summary>
    public const string NegotiationReport = "NEGRPT";

    /// <summary>Host broadcasts this to ask all clients to restart all their tunnel negotiations.</summary>
    public const string RenegotiateAll = "RENEGALL";

    /// <summary>Asks peers to renegotiate the tunnel they share with the sender.</summary>
    public const string TunnelRenegotiate = "TNLRENEG";

    /// <summary>Notifies the host that the sender can no longer reach a tunnel.</summary>
    public const string TunnelFailed = "TNLFAIL";

    /// <summary>Announces a host-selected tunnel server change.</summary>
    public const string ChangeTunnelServer = "CHTNL";

    /// <summary>
    /// Reports the sender's reserved local game-relay port to the host, for inclusion as
    /// that player's in-game ID in the next STARTV3 payload.
    /// </summary>
    public const string GamePortReport = "GPRT";
}
