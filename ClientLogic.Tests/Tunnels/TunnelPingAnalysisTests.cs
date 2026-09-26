using System.Collections.Generic;

using ClientLogic.Tunnels;

using DTAClient.Domain.Multiplayer.CnCNet;

using Xunit;

namespace ClientLogic.Tests.Tunnels;

public class TunnelPingAnalysisTests
{
    private static NegotiationDataManager Pings(params (string, string, int)[] pairs)
    {
        var data = new NegotiationDataManager();
        foreach (var (p1, p2, ping) in pairs)
            data.UpdatePing(p1, p2, ping);

        return data;
    }

    [Fact]
    public void SuggestsThePlayerWhoseRemovalHelpsMost()
    {
        // Carol is far from everyone; without her the worst ping drops from 900 to 80.
        var data = Pings(("Alice", "Bob", 80), ("Alice", "Carol", 900), ("Bob", "Carol", 850));

        KickSuggestion suggestion = TunnelPingAnalysis.SuggestKick(data, ["Alice", "Bob", "Carol"]);

        Assert.Equal(new KickSuggestion("Carol", 900, 80), suggestion);
    }

    [Fact]
    public void NoSuggestionForTwoPlayersUnknownPingsOrSmallGains()
    {
        Assert.Null(TunnelPingAnalysis.SuggestKick(Pings(("Alice", "Bob", 900)), ["Alice", "Bob"]));

        // Bob <-> Carol unknown
        Assert.Null(TunnelPingAnalysis.SuggestKick(Pings(("Alice", "Bob", 80), ("Alice", "Carol", 900)), ["Alice", "Bob", "Carol"]));

        // Worst ping below the threshold
        Assert.Null(TunnelPingAnalysis.SuggestKick(
            Pings(("Alice", "Bob", 80), ("Alice", "Carol", PingQualityRules.KickSuggestionMinWorstMs - 1), ("Bob", "Carol", 90)),
            ["Alice", "Bob", "Carol"]));

        // Everyone is equally far apart: removing anyone gains nothing
        Assert.Null(TunnelPingAnalysis.SuggestKick(
            Pings(("Alice", "Bob", 900), ("Alice", "Carol", 900), ("Bob", "Carol", 900)),
            ["Alice", "Bob", "Carol"]));
    }

    [Fact]
    public void HighPingPairsAreListed()
    {
        int high = PingQualityRules.HighPingWarningMs + 1;
        var data = Pings(("Alice", "Bob", 50), ("Alice", "Carol", high));

        List<(string, string, int)> pairs = TunnelPingAnalysis.GetHighPingPairs(data, ["Alice", "Bob", "Carol"]);

        var pair = Assert.Single(pairs);
        Assert.Equal(high, pair.Item3);
        Assert.Contains("Carol", new[] { pair.Item1, pair.Item2 });
    }
}
