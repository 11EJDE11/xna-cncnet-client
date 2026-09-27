using System.Collections.Generic;

using ClientLogic.Tunnels;

using DTAClient.Domain.Multiplayer.CnCNet;

using Xunit;

namespace ClientLogic.Tests.Tunnels;

public class TunnelListStateTests
{
    public TunnelListStateTests() => TestGame.EnsureInitialized();

    /// <param name="status">2 official, 1 recommended, 0 neither.</param>
    private static CnCNetTunnel Tunnel(string name, int port, int status, int ping, int version = 3, int clients = 10, int maxClients = 100)
    {
        CnCNetTunnel tunnel = CnCNetTunnel.Parse($"1.2.3.4:{port};Country;nz;{name};0;{clients};{maxClients};{status};0;0;{version};0");
        tunnel.Ping = ping < 0 ? PingValue.Unknown : PingValue.FromMs(ping);
        return tunnel;
    }

    private readonly List<CnCNetTunnel> tunnels =
    [
        Tunnel("Slow official", 1, 2, 200),
        Tunnel("Fast unofficial", 2, 0, 10),
        Tunnel("Fast recommended", 3, 1, 50),
        Tunnel("V2", 4, 2, 5, version: 2),
        Tunnel("Unpinged official", 5, 2, -1),
    ];

    private TunnelListState State() => new(() => tunnels) { TargetVersion = 3 };

    [Fact]
    public void Refresh_lists_the_target_version_and_selects_the_best_official_or_recommended_tunnel()
    {
        TunnelListState state = State();
        state.Refresh();

        Assert.Equal(4, state.Tunnels.Count);
        Assert.Equal("Fast recommended", state.GetSelectedTunnel().Name);
    }

    [Fact]
    public void The_users_pick_survives_a_refresh_and_pings()
    {
        TunnelListState state = State();
        state.Refresh();
        state.Select(0);

        tunnels[0].Ping = PingValue.FromMs(500);
        state.Refresh();
        Assert.Equal("Slow official", state.GetSelectedTunnel().Name);

        tunnels[4].Ping = PingValue.FromMs(1);
        Assert.Equal(3, state.OnTunnelPinged("1.2.3.4", 5));
        Assert.Equal("Slow official", state.GetSelectedTunnel().Name);
    }

    [Fact]
    public void A_better_ping_moves_the_automatic_selection()
    {
        TunnelListState state = State();
        state.Refresh();

        tunnels[4].Ping = PingValue.FromMs(1);
        state.OnTunnelPinged("1.2.3.4", 5);

        Assert.Equal("Unpinged official", state.GetSelectedTunnel().Name);
    }

    [Fact]
    public void Changing_the_version_forgets_the_pick()
    {
        TunnelListState state = State();
        state.Refresh();
        state.SelectTunnel("1.2.3.4", 1);
        Assert.True(state.IsTunnelSelected("1.2.3.4", 1));

        state.TargetVersion = 2;
        state.Refresh();
        Assert.Equal("V2", state.GetSelectedTunnel().Name);

        state.TargetVersion = 3;
        state.Refresh();
        Assert.Equal("Fast recommended", state.GetSelectedTunnel().Name);
    }

    [Fact]
    public void Rating_is_ping_squared_times_usage_percent()
    {
        Assert.Equal(2500 * 10, TunnelListState.GetTunnelRating(Tunnel("t", 1, 2, 50, clients: 10, maxClients: 100)));

        // An empty tunnel's usage ratio counts as 0.1 (10 %)
        Assert.Equal(2500 * 10, TunnelListState.GetTunnelRating(Tunnel("t", 1, 2, 50, clients: 0, maxClients: 100)));
    }
}
