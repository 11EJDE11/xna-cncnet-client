using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Net.Sockets;
using System.Threading.Tasks;
using DTAClient.Domain.Multiplayer.CnCNet;
using Rampastring.Tools;

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

    //stores information about another player for use with V3 tunnels.
    public class V3PlayerInfo
    {
        public uint Id { get; set; }
        public string Name { get; set; }
        public string IpAddress { get; set; }
        public int Port { get; set; }
        public int PlayerIndex { get; set; }
        public ushort PlayerGameId { get; set; }
        public bool HasNegotiated { get; set; }
        public bool IsNegotiating { get; set; }

        // per player and tunnel test results. Tunnels are recreated on each update, so don't store the object.
        public Dictionary<string, TunnelTestResult> TunnelResults { get; } = new Dictionary<string, TunnelTestResult>();

        public V3PlayerInfo(uint id, string name, string ipAddress, int port, int playerIndex, ushort playerGameID)
        {
            Id = id;
            Name = name;
            IpAddress = ipAddress;
            Port = port;
            PlayerIndex = playerIndex;
            PlayerGameId = playerGameID;
        }

        public UdpClient TunnelClient { get; set; }

        public void InitializeTunnelResults(List<CnCNetTunnel> tunnels)
        {
            TunnelResults.Clear();
            foreach (var tunnel in tunnels)
            {
                TunnelResults[$"{tunnel.Address}:{tunnel.Port}"] = new TunnelTestResult();
            }
        }
        public TunnelTestResult GetTunnelResult(CnCNetTunnel tunnel)
        {
            return TunnelResults.TryGetValue($"{tunnel.Address}:{tunnel.Port}", out var result) ? result : null;
        }

        public CnCNetTunnel SelectBestTunnel(List<CnCNetTunnel> availableTunnels)
        {
            var bestKey = TunnelResults
                .Where(kvp => kvp.Value.PingResults.Any(p => p.RoundTripTime.HasValue))
                .OrderBy(kvp => kvp.Value.AverageRtt + kvp.Value.PacketLoss * 10)
                .Select(kvp => kvp.Key)
                .FirstOrDefault();

            if (bestKey == null)
                return null;

            var parts = bestKey.Split(':');
            if (parts.Length != 2)
                return null;

            string address = parts[0];
            if (!int.TryParse(parts[1], out int port))
                return null;

            return availableTunnels.FirstOrDefault(t => t.Address == address && t.Port == port);
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

        public void ClearTunnelResults()
        {
            TunnelResults.Clear();
        }
    }
}