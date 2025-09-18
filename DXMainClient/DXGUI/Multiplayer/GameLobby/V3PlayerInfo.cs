using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading.Tasks;

using DTAClient.Domain.Multiplayer.CnCNet;

namespace DTAClient.DXGUI.Multiplayer.GameLobby
{
    public class PingResult
    {
        public int ID { get; set; }
        public long SentTimeTicks { get; set; }
        public long? ReceivedTimeTicks { get; set; }

        public double? RoundTripTime
        {
            get
            {
                if (ReceivedTimeTicks.HasValue)
                    return (double)(ReceivedTimeTicks.Value - SentTimeTicks) / Stopwatch.Frequency * 1000.0;
                return null;
            }
        }

        public TaskCompletionSource<bool> CompletionSource { get; set; } = new TaskCompletionSource<bool>();
    }

    public class TunnelTestResult
    {
        // How long the non-decider will keep sending Connected packets to a single tunnel
        // while waiting for a Ping Request from the decider. After this, the tunnel is skipped.
        // Note that the existing players in the lobby will begin negotiating when
        // Channel_UserAdded is called, while the joining player will begin negotiation
        // when ApplyPlayerOptions is sent by the host. The timeout should be long enough for
        // the joining player to receive that IRC message + attempt connections to each tunnel.
        private const int CONNECTED_TIMEOUT_MS = 10000;

        public List<PingResult> PingResults { get; } = new List<PingResult>();
        public bool ConnectedReceived { get; set; }
        public TaskCompletionSource<bool> ConnectedTcs { get; } = new TaskCompletionSource<bool>();
        public TaskCompletionSource<bool> PingsCompletedTcs { get; } = new TaskCompletionSource<bool>();

        public double AverageRtt => PingResults
            .Where(p => p.RoundTripTime.HasValue)
            .Select(p => p.RoundTripTime.Value)
            .DefaultIfEmpty(-1)
            .Average();

        public double PacketLoss => PingResults.Count == 0 ? 100 :
            PingResults.Count(p => !p.RoundTripTime.HasValue) * 100.0 / PingResults.Count;

        public DateTime? FirstConnectedSentTime { get; set; }
        public bool ConnectedTimedOut => FirstConnectedSentTime.HasValue &&
            (DateTime.UtcNow - FirstConnectedSentTime.Value).TotalMilliseconds > CONNECTED_TIMEOUT_MS;

        public bool PingRequestReceived { get; set; }
    }

    public class V3PlayerInfo
    {
        public uint Id { get; set; }
        public string Name { get; set; }
        public int PlayerIndex { get; set; }
        public ushort PlayerGameId { get; set; }
        public bool HasNegotiated { get; set; }
        public bool IsNegotiating { get; set; }

        public CnCNetTunnel Tunnel { get; set; }
        public V3PlayerNegotiator Negotiator { get; set; }

        public Dictionary<CnCNetTunnel, TunnelTestResult> TunnelResults { get; } = new Dictionary<CnCNetTunnel, TunnelTestResult>();

        public V3PlayerInfo(uint id, string name, int playerIndex, ushort playerGameID, CnCNetTunnel tunnel = null)
        {
            Id = id;
            Name = name;
            PlayerIndex = playerIndex;
            PlayerGameId = playerGameID;
            Tunnel = tunnel;
        }

        public void InitializeTunnelResults(List<CnCNetTunnel> tunnels)
        {
            TunnelResults.Clear();
            foreach (var tunnel in tunnels)
            {
                TunnelResults[tunnel] = new TunnelTestResult();
            }
        }

        public TunnelTestResult GetTunnelResult(CnCNetTunnel tunnel)
        {
            return TunnelResults.TryGetValue(tunnel, out var result) ? result : null;
        }

        public CnCNetTunnel SelectBestTunnel(List<CnCNetTunnel> availableTunnels)
        {
            var bestTunnel = TunnelResults
                .Where(kvp => kvp.Value.PingResults.Any(p => p.RoundTripTime.HasValue))
                .OrderBy(kvp => kvp.Value.AverageRtt + kvp.Value.PacketLoss * 10)
                .Select(kvp => kvp.Key)
                .FirstOrDefault();

            if (bestTunnel != null)
                Tunnel = bestTunnel;

            return bestTunnel;
        }

        public double GetBestPing()
        {
            var bestPing = TunnelResults
                .Where(kvp => kvp.Value.PingResults.Any(p => p.RoundTripTime.HasValue))
                .Select(kvp => kvp.Value.AverageRtt)
                .DefaultIfEmpty(double.NaN)
                .Min();
            return bestPing;
        }

        public void SetNegotiator(V3PlayerNegotiator negotiator)
        {
            StopNegotiation();
            Negotiator = negotiator;
        }

        public void StopNegotiation()
        {
            if (Negotiator != null)
            {
                Negotiator.Dispose();
                Negotiator = null;
            }
        }
    }
}