#nullable enable
using System.Collections.Generic;

using ClientCore.Extensions;

using DTAClient.Domain.Multiplayer.CnCNet;

namespace ClientLogic.Tunnels;

/// <summary>A pair of players in the negotiation status list.</summary>
/// <param name="Text">The status or ping text.</param>
/// <param name="TextTier">The text's colour, as a ping tier (Unknown gray, Fair yellow, Good green, Bad red).</param>
/// <param name="BarPing">The ping to draw as a bar, or null for no bar.</param>
public sealed record NegotiationPairRow(string Player1, string Player2, NegotiationStatus Status, PingValue? Ping,
    string Text, PingQualityTier TextTier, int? BarPing);

/// <summary>
/// The contents of the tunnel negotiation status panel (the XNA TunnelNegotiationStatusPanel): each pair's status
/// and ping, worst first.
/// </summary>
public static class NegotiationStatusRows
{
    /// <summary>The ping for a full-width ping bar.</summary>
    public const int BAR_MAX_PING = 500;

    /// <summary>The status to show: while the lobby waits, pairs that haven't started count as in progress.</summary>
    public static NegotiationStatus DisplayStatus(NegotiationStatus status, bool inferInProgress) =>
        inferInProgress && status == NegotiationStatus.NotStarted ? NegotiationStatus.InProgress : status;

    /// <summary>A pair's text and its colour tier.</summary>
    public static (string Text, PingQualityTier Tier) GetLabel(NegotiationStatus status, PingValue? ping) => status switch
    {
        NegotiationStatus.NotStarted => ("-", PingQualityTier.Unknown),
        NegotiationStatus.InProgress => ("...", PingQualityTier.Fair),
        NegotiationStatus.Succeeded when ping.HasValue => (ping.Value.ToString(), PingQualityRules.GetV3Tier(ping.Value)),
        NegotiationStatus.Succeeded => ("OK".L10N("Client:Main:NegStatusOK"), PingQualityTier.Good),
        NegotiationStatus.Failed => ("FAIL".L10N("Client:Main:NegStatusFail"), PingQualityTier.Bad),
        _ => ("?", PingQualityTier.Unknown)
    };

    /// <summary>The player pairs, worst first: failed pairs, then negotiated pairs from the highest ping.</summary>
    public static List<NegotiationPairRow> ListPairs(IReadOnlyList<string> players, NegotiationDataManager negotiationData,
        bool inferInProgress)
    {
        var pairs = new List<(string p1, string p2, NegotiationStatus status, PingValue? ping)>();
        foreach (var (p1, p2) in negotiationData.GetPlayerPairs(players))
        {
            NegotiationStatus status = DisplayStatus(negotiationData.GetNegotiationStatus(p1, p2), inferInProgress);
            pairs.Add((p1, p2, status, negotiationData.GetPing(p1, p2)));
        }

        // Worst first, so problems are visible at a glance without scrolling:
        // failed pairs on top, then negotiated pairs from highest to lowest ping.
        pairs.Sort((a, b) =>
        {
            int rankA = GetSortRank(a.status, a.ping);
            int rankB = GetSortRank(b.status, b.ping);
            if (rankA != rankB)
                return rankA.CompareTo(rankB);

            if (a.ping.HasValue && b.ping.HasValue)
                return b.ping.Value.Milliseconds.CompareTo(a.ping.Value.Milliseconds);

            return 0;
        });

        var rows = new List<NegotiationPairRow>(pairs.Count);
        foreach (var (p1, p2, status, ping) in pairs)
        {
            (string text, PingQualityTier tier) = GetLabel(status, ping);
            int? barPing = status == NegotiationStatus.Succeeded && ping.HasValue && ping.Value.IsValid()
                ? ping.Value.Milliseconds
                : null;
            rows.Add(new NegotiationPairRow(p1, p2, status, ping, text, tier, barPing));
        }

        return rows;
    }

    private static int GetSortRank(NegotiationStatus status, PingValue? ping) => status switch
    {
        NegotiationStatus.Failed => 0,
        NegotiationStatus.Succeeded when ping.HasValue && ping.Value.IsValid() => 1,
        NegotiationStatus.Succeeded => 2,
        NegotiationStatus.InProgress => 3,
        NegotiationStatus.NotStarted => 4,
        _ => 5
    };
}
