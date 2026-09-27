using System.Collections.Generic;
using System.Linq;

using ClientLogic.Tunnels;

using DTAClient.Domain.Multiplayer.CnCNet;

using Xunit;

namespace ClientLogic.Tests.Tunnels;

public class NegotiationStatusRowsTests
{
    public NegotiationStatusRowsTests() => TestGame.EnsureInitialized();

    private static void Succeed(NegotiationDataManager data, string a, string b, int ping)
    {
        data.UpdateStatus(a, b, NegotiationStatus.Succeeded);
        data.UpdateStatus(b, a, NegotiationStatus.Succeeded);
        data.UpdatePing(a, b, ping);
        data.UpdatePing(b, a, ping);
    }

    [Fact]
    public void Pairs_are_listed_worst_first()
    {
        var data = new NegotiationDataManager();
        List<string> players = ["A", "B", "C", "D"];
        Succeed(data, "A", "B", 40);
        Succeed(data, "A", "C", 200);
        data.UpdateStatus("B", "C", NegotiationStatus.Failed);

        List<NegotiationPairRow> rows = NegotiationStatusRows.ListPairs(players, data, inferInProgress: false);

        Assert.Equal(("B", "C"), (rows[0].Player1, rows[0].Player2));
        Assert.Equal(PingQualityTier.Bad, rows[0].TextTier);
        Assert.Equal(200, rows[1].BarPing);
        Assert.Equal(40, rows[2].BarPing);
        Assert.All(rows.Skip(3), row => Assert.Equal(NegotiationStatus.NotStarted, row.Status));
        Assert.Equal("-", rows[3].Text);
    }

    [Fact]
    public void Waiting_pairs_count_as_in_progress_when_inferred()
    {
        var data = new NegotiationDataManager();

        NegotiationPairRow row = NegotiationStatusRows.ListPairs(["A", "B"], data, inferInProgress: true).Single();

        Assert.Equal(NegotiationStatus.InProgress, row.Status);
        Assert.Equal("...", row.Text);
        Assert.Null(row.BarPing);
    }
}
