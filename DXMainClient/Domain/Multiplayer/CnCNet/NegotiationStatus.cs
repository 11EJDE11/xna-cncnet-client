namespace DTAClient.Domain.Multiplayer.CnCNet;

/// <summary>
/// The status of connection/negotiation between two players.
/// </summary>
public enum NegotiationStatus
{
    NotStarted,
    InProgress,
    Succeeded,
    Failed
}
