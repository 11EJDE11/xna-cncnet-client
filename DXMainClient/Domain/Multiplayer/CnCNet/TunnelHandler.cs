using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Text;
using System.Threading.Tasks;

using ClientCore;

using DTAClient.Online;

using Microsoft.Xna.Framework;

using Rampastring.Tools;
using Rampastring.XNAUI;

namespace DTAClient.Domain.Multiplayer.CnCNet
{
    public class TunnelHandler : GameComponent
    {
        /// <summary>
        /// Determines the time between pinging the current tunnel (if it's set).
        /// </summary>
        private const double CURRENT_TUNNEL_PING_INTERVAL = 20.0;

        /// <summary>
        /// A reciprocal to the value which determines how frequent the full tunnel
        /// refresh would be done instead of just pinging the current tunnel (1/N of 
        /// current tunnel ping refreshes would be substituted by a full list refresh).
        /// Multiply by <see cref="CURRENT_TUNNEL_PING_INTERVAL"/> to get the interval 
        /// between full list refreshes.
        /// </summary>
        private const uint CYCLES_PER_TUNNEL_LIST_REFRESH = 6;

        private static readonly int[] SUPPORTED_TUNNEL_VERSIONS = [2, 3];
        private static readonly TimeSpan tunnelRefreshInterval = TimeSpan.FromSeconds(CURRENT_TUNNEL_PING_INTERVAL);

        private readonly object _refreshLock = new();
        private bool _refreshInProgress = false;
        private readonly V3TunnelCommunicator _tunnelCommunicator;

        public TunnelHandler(WindowManager wm, CnCNetManager connectionManager) : base(wm.Game)
        {
            this.wm = wm;
            this.connectionManager = connectionManager;

            wm.Game.Components.Add(this);

            Enabled = false;

            connectionManager.Connected += ConnectionManager_Connected;
            connectionManager.Disconnected += ConnectionManager_Disconnected;
            connectionManager.ConnectionLost += ConnectionManager_ConnectionLost;

            _tunnelCommunicator = new V3TunnelCommunicator();
        }

        public List<CnCNetTunnel> Tunnels { get; private set; } = [];
        public CnCNetTunnel CurrentTunnel { get; set; } = null;
        public V3GameTunnelBridge GameTunnelBridge;

        private IPEndPoint _cachedP2PEndpoint;
        private Task<IPEndPoint> _p2pDiscoveryTask;
        private readonly object _p2pDiscoveryLock = new object();

        public event EventHandler TunnelsRefreshed;
        public event EventHandler CurrentTunnelPinged;
        public event EventHandler<CnCNetTunnel> TunnelFailed;
        public event Action<string, int> TunnelPinged; //address, port

        private WindowManager wm;
        private CnCNetManager connectionManager;

        private readonly Stopwatch refreshTimer = Stopwatch.StartNew();
        private TimeSpan? lastTunnelRefreshTimestamp;
        private uint skipCount = 0;
        private const int TUNNEL_FAILED_PING_AMOUNT = 2000;

        private void DoTunnelPinged(string address, int port)
        {
            if (TunnelPinged != null)
                wm.AddCallback(TunnelPinged, address, port);
        }

        private void DoCurrentTunnelPinged()
        {
            if (CurrentTunnelPinged != null)
                wm.AddCallback(CurrentTunnelPinged, this, EventArgs.Empty);
        }

        private void DoTunnelFailed(CnCNetTunnel tunnel)
        {
            if (TunnelFailed != null)
                wm.AddCallback(TunnelFailed, this, tunnel);
        }

        private void ConnectionManager_Connected(object sender, EventArgs e)
        {
            InitializeTunnelCommunicator();
            Enabled = true;
        }

        private void ConnectionManager_ConnectionLost(object sender, Online.EventArguments.ConnectionLostEventArgs e)
        {
            Enabled = false;
            _tunnelCommunicator.Shutdown();
            _cachedP2PEndpoint = null;
        }

        private void ConnectionManager_Disconnected(object sender, EventArgs e)
        {
            Enabled = false;
            _tunnelCommunicator.Shutdown();
            _cachedP2PEndpoint = null;
        }

        private void RefreshTunnelsAsync()
        {
            lock (_refreshLock)
            {
                if (_refreshInProgress)
                    return;
                _refreshInProgress = true;
            }

            Task.Run(() =>
            {
                try
                {
                    List<CnCNetTunnel> tunnels = RefreshTunnels();
                    wm.AddCallback(new Action<List<CnCNetTunnel>>(HandleRefreshedTunnels), tunnels);
                }
                finally
                {
                    lock (_refreshLock)
                    {
                        _refreshInProgress = false;
                    }
                }
            });
        }

        private void HandleRefreshedTunnels(List<CnCNetTunnel> newTunnels)
        {
            if (newTunnels.Count == 0)
            {
                TunnelsRefreshed?.Invoke(this, EventArgs.Empty);
                return;
            }

            var existingTunnels = Tunnels.ToDictionary(t => $"{t.Address}:{t.Port}");
            var updatedTunnels = new List<CnCNetTunnel>();

            foreach (var newTunnel in newTunnels)
            {
                string key = $"{newTunnel.Address}:{newTunnel.Port}";
                if (existingTunnels.TryGetValue(key, out var existingTunnel))
                {
                    // update existing tunnels
                    existingTunnel.UpdateFrom(newTunnel);
                    updatedTunnels.Add(existingTunnel);
                }
                else
                {
                    // add new tunnels
                    updatedTunnels.Add(newTunnel);
                }
            }

            // remove old tunnels
            Tunnels = updatedTunnels;
            TunnelsRefreshed?.Invoke(this, EventArgs.Empty);

            // Group tunnels by IP address and ping each unique address
            var tunnelsByAddress = Tunnels
                .Where(t => UserINISettings.Instance.PingUnofficialCnCNetTunnels || t.Official || t.Recommended)
                .GroupBy(t => t.Address)
                .ToList();

            foreach (var group in tunnelsByAddress)
            {
                _ = PingAddressAndUpdateTunnelsAsync(group.Key, group.ToList());
            }

            if (CurrentTunnel != null)
            {
                var updatedTunnel = Tunnels.Find(t => t.Address == CurrentTunnel.Address && t.Port == CurrentTunnel.Port);
                if (updatedTunnel != null)
                {
                    // don't re-ping if the tunnel still exists in list, just update the tunnel instance and
                    // fire the event handler (the tunnel was already pinged when traversing the tunnel list)
                    CurrentTunnel = updatedTunnel;
                    DoCurrentTunnelPinged();
                }
                else
                {
                    // tunnel is not in the list anymore so it's not updated with a list instance and pinged
                    PingCurrentTunnelAsync();
                }
            }

            InitializeTunnelCommunicator();
            _tunnelCommunicator.AddTunnels(Tunnels);
        }

        /// <summary>
        /// Pings a single IP address and updates all tunnels sharing that address with the same ping result.
        /// This prevents redundant pings for tunnels on the same IP but different ports (e.g., V2 and V3 versions).
        /// </summary>
        private Task PingAddressAndUpdateTunnelsAsync(string address, List<CnCNetTunnel> tunnelsWithSameAddress)
        {
            if (tunnelsWithSameAddress.Count == 0)
                return Task.CompletedTask;

            return Task.Run(() =>
            {
                PingValue pingResult = PingValue.Unknown;

                for (int i = 0; i < tunnelsWithSameAddress.Count; i++)
                {
                    var tunnel = tunnelsWithSameAddress[i];
                    PingValue previousPing = tunnel.Ping;

                    if (i == 0)
                    {
                        tunnel.UpdatePing();
                        pingResult = tunnel.Ping;
                    }
                    else
                    {
                        tunnel.Ping = pingResult;
                    }

                    if (previousPing.IsValid() && (tunnel.Ping.IsUnknown() || tunnel.Ping.Milliseconds > TUNNEL_FAILED_PING_AMOUNT))
                    {
                        if (CurrentTunnel == null || tunnel == CurrentTunnel)
                            DoTunnelFailed(tunnel);
                    }

                    DoTunnelPinged(tunnel.Address, tunnel.Port);
                }
            });
        }

        private Task PingCurrentTunnelAsync(bool checkTunnelList = false)
        {
            return Task.Run(() =>
            {
                var tunnel = CurrentTunnel;
                if (tunnel == null) return;

                PingValue previousPing = tunnel.Ping;
                tunnel.UpdatePing();
                PingValue pingResult = tunnel.Ping;

                if (previousPing.IsValid() && (pingResult.IsUnknown() || pingResult.Milliseconds > TUNNEL_FAILED_PING_AMOUNT))
                    DoTunnelFailed(tunnel);

                DoCurrentTunnelPinged();

                if (checkTunnelList)
                {
                    DoTunnelPinged(tunnel.Address, tunnel.Port);

                    // Update all other tunnels with the same IP address
                    var otherTunnelsWithSameAddress = Tunnels.Where(t => t.Address == tunnel.Address && t != tunnel).ToList();
                    foreach (var otherTunnel in otherTunnelsWithSameAddress)
                    {
                        PingValue otherPreviousPing = otherTunnel.Ping;
                        otherTunnel.Ping = pingResult;

                        if (CurrentTunnel == null && otherPreviousPing.IsValid() &&
                            (pingResult.IsUnknown() || pingResult.Milliseconds > TUNNEL_FAILED_PING_AMOUNT))
                        {
                            DoTunnelFailed(otherTunnel);
                        }

                        DoTunnelPinged(otherTunnel.Address, otherTunnel.Port);
                    }
                }
            });
        }

        private static bool OnlineTunnelDataAvailable => !string.IsNullOrWhiteSpace(ClientConfiguration.Instance.CnCNetTunnelListURL);
        private static bool OfflineTunnelDataAvailable => SafePath.GetFile(ProgramConstants.ClientUserFilesPath, "tunnel_cache").Exists;

        private static byte[] GetRawTunnelDataOnline()
        {
            return new TimedHttpClient(10000).GetBytes(ClientConfiguration.Instance.CnCNetTunnelListURL);
        }

        private static byte[] GetRawTunnelDataOffline()
        {
            FileInfo tunnelCacheFile = SafePath.GetFile(ProgramConstants.ClientUserFilesPath, "tunnel_cache");
            return File.ReadAllBytes(tunnelCacheFile.FullName);
        }

        private static byte[] GetRawTunnelData(int retryCount = 2)
        {
            Logger.Log("Fetching tunnel server info.");

            if (OnlineTunnelDataAvailable)
            {
                for (int i = 0; i < retryCount; i++)
                {
                    try
                    {
                        byte[] data = GetRawTunnelDataOnline();
                        return data;
                    }
                    catch (Exception ex)
                    {
                        Logger.Log("Error when downloading tunnel server info: " + ex.Message);
                        if (i < retryCount - 1)
                            Logger.Log("Retrying.");
                        else
                            Logger.Log("Fetching tunnel server list failed.");
                    }
                }
            }
            else
            {
                // Don't fetch the latest tunnel list if it is explicitly disabled
                // For example, the official CnCNet server might be unavailable/unstable in a country with Internet censorship,
                // where players might either establish a substitute server or manually distribute the tunnel cache file
                Logger.Log("Fetching tunnel server list online is disabled.");
            }

            if (OfflineTunnelDataAvailable)
            {
                Logger.Log("Using cached tunnel data.");
                byte[] data = GetRawTunnelDataOffline();
                return data;
            }
            else
                Logger.Log("Tunnel cache file doesn't exist!");

            return null;
        }


        /// <summary>
        /// Downloads and parses the list of CnCNet tunnels.
        /// </summary>
        /// <returns>A list of tunnel servers.</returns>
        private static List<CnCNetTunnel> RefreshTunnels()
        {
            List<CnCNetTunnel> returnValue = [];
            var seenAddresses = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            FileInfo tunnelCacheFile = SafePath.GetFile(ProgramConstants.ClientUserFilesPath, "tunnel_cache");

            byte[] data = GetRawTunnelData();
            if (data is null)
                return returnValue;

            string convertedData = Encoding.Default.GetString(data);

            string[] serverList = convertedData.Split(["\r\n", "\n"], StringSplitOptions.RemoveEmptyEntries);

            // skip first header item ("address;country;countrycode;name;password;clients;maxclients;official;latitude;longitude;version;distance")
            foreach (string serverInfo in serverList.Skip(1))
            {
                try
                {
                    CnCNetTunnel tunnel = CnCNetTunnel.Parse(serverInfo);

                    if (tunnel == null)
                        continue;

                    if (tunnel.RequiresPassword)
                        continue;

                    if (!SUPPORTED_TUNNEL_VERSIONS.Contains(tunnel.Version))
                        continue;

                    if (!seenAddresses.Add($"{tunnel.Address}:{tunnel.Port}"))
                        continue;

                    returnValue.Add(tunnel);
                }
                catch (Exception ex)
                {
                    Logger.Log("Caught an exception when parsing a tunnel server: " + ex.ToString());
                }
            }

            if (returnValue.Count > 0)
            {
                try
                {
                    if (tunnelCacheFile.Exists)
                        tunnelCacheFile.Delete();

                    DirectoryInfo clientDirectoryInfo = SafePath.GetDirectory(ProgramConstants.ClientUserFilesPath);

                    if (!clientDirectoryInfo.Exists)
                        clientDirectoryInfo.Create();

                    File.WriteAllBytes(tunnelCacheFile.FullName, data);
                }
                catch (Exception ex)
                {
                    Logger.Log("Refreshing tunnel cache file failed! Returned error: " + ex.ToString());
                }
            }

            Logger.Log($"Successfully refreshed tunnel cache with {returnValue.Count} servers.");
            return returnValue;
        }

        public override void Update(GameTime gameTime)
        {
            TimeSpan currentTimestamp = refreshTimer.Elapsed;
            TimeSpan elapsedSinceLastRefresh = lastTunnelRefreshTimestamp.HasValue
                ? currentTimestamp - lastTunnelRefreshTimestamp.Value
                : TimeSpan.MaxValue;

            if (elapsedSinceLastRefresh > tunnelRefreshInterval)
            {
                if (skipCount % CYCLES_PER_TUNNEL_LIST_REFRESH == 0)
                {
                    skipCount = 0;
                    RefreshTunnelsAsync();
                }
                else if (CurrentTunnel != null)
                {
                    _ = PingCurrentTunnelAsync(true);
                }

                lastTunnelRefreshTimestamp = currentTimestamp;
                skipCount++;
            }

            base.Update(gameTime);
        }

        public V3GameTunnelBridge StartGameBridge(uint localId, int localPort, List<V3PlayerInfo> allPlayers)
        {
            StopGameBridge();

            GameTunnelBridge = new V3GameTunnelBridge(localId, localPort, allPlayers, this);
            GameTunnelBridge.Start();

            return GameTunnelBridge;
        }

        public void StopGameBridge()
        {
            if (GameTunnelBridge != null)
            {
                GameTunnelBridge.Stop();
                GameTunnelBridge = null;
            }
        }

        public void InitializeTunnelCommunicator()
        {
            if (!_tunnelCommunicator.IsInitialized && Tunnels.Count > 0)
                _tunnelCommunicator.Initialize(Tunnels);
        }

        /// <summary>
        /// Returns the cached STUN-discovered external endpoint for this session,
        /// or discovers it by querying official tunnel servers as STUN endpoints.
        /// Returns null if the NAT is symmetric or no STUN servers respond.
        /// </summary>
        public Task<IPEndPoint> GetOrDiscoverP2PEndpointAsync()
        {
            if (_cachedP2PEndpoint != null)
                return Task.FromResult(_cachedP2PEndpoint);

            // Single-flight: several player negotiations can run at once (3+ player games), so
            // share one in-flight discovery rather than racing STUN queries to the same servers
            // on the shared communicator socket (which would clobber each other's pending query).
            lock (_p2pDiscoveryLock)
            {
                if (_cachedP2PEndpoint != null)
                    return Task.FromResult(_cachedP2PEndpoint);

                return _p2pDiscoveryTask ??= DiscoverP2PEndpointAsync();
            }
        }

        private async Task<IPEndPoint> DiscoverP2PEndpointAsync()
        {
            try
            {
                var stunHosts = Tunnels
                    .Where(t => t.Official || t.Recommended)
                    .Select(t => t.Address)
                    .Distinct()
                    .Take(8)
                    .ToList();

                // Prepend any configured STUN hosts
                string configuredHosts = ClientConfiguration.Instance.P2PStunServers;
                if (!string.IsNullOrWhiteSpace(configuredHosts))
                {
                    var configured = configuredHosts.Split(';', StringSplitOptions.RemoveEmptyEntries);
                    stunHosts.InsertRange(0, configured);
                }

                var ep = await StunHelper.DiscoverExternalEndpointAsync(_tunnelCommunicator, stunHosts).ConfigureAwait(false);
                _cachedP2PEndpoint = ep;
                return ep;
            }
            finally
            {
                // Release the in-flight slot: a success is now served from _cachedP2PEndpoint,
                // and a failure (null) can be retried (serially) by a later negotiation.
                lock (_p2pDiscoveryLock)
                    _p2pDiscoveryTask = null;
            }
        }

        /// <summary>
        /// Returns this machine's local (LAN) endpoints — every non-loopback IPv4
        /// unicast address paired with the communicator's UDP port. These are offered as
        /// additional P2P candidates so peers behind the same NAT (e.g. on the same LAN)
        /// can connect directly without relying on NAT hairpinning of the reflexive address.
        /// </summary>
        public List<IPEndPoint> GetLocalP2PEndpoints()
        {
            var result = new List<IPEndPoint>();
            int port = _tunnelCommunicator.LocalPort;
            if (port == 0)
                return result;

            try
            {
                foreach (var ni in NetworkInterface.GetAllNetworkInterfaces())
                {
                    if (ni.OperationalStatus != OperationalStatus.Up ||
                        ni.NetworkInterfaceType == NetworkInterfaceType.Loopback)
                        continue;

                    foreach (var addr in ni.GetIPProperties().UnicastAddresses)
                    {
                        if (addr.Address.AddressFamily != AddressFamily.InterNetwork ||
                            IPAddress.IsLoopback(addr.Address))
                            continue;

                        result.Add(new IPEndPoint(addr.Address, port));
                    }
                }
            }
            catch (Exception ex)
            {
                Logger.Log($"TunnelHandler: Failed to enumerate local endpoints: {ex.Message}");
            }

            return result;
        }

        /// <summary>
        /// Registers a P2P peer's endpoint with the communicator so packets from
        /// that address are dispatched correctly.
        /// </summary>
        public void AddP2PTunnel(P2PTunnel tunnel) => _tunnelCommunicator.AddP2PTunnel(tunnel);

        /// <summary>
        /// Clears the cached STUN result so the next P2P negotiation re-queries.
        /// Call when P2P is enabled in options or after a network change.
        /// </summary>
        public void ClearP2PEndpointCache() => _cachedP2PEndpoint = null;

        public void RegisterV3PacketHandler(uint localId, uint remoteId, PacketHandler handler) => _tunnelCommunicator.RegisterHandler(localId, remoteId, handler);

        public void UnregisterV3PacketHandler(uint localId, uint remoteId) => _tunnelCommunicator.UnregisterHandler(localId, remoteId);

        public void SendRegistrationToTunnels(uint localId, List<CnCNetTunnel> tunnels = null) => _tunnelCommunicator.SendRegistrationToTunnels(localId, tunnels);

        public void SendPacket(CnCNetTunnel tunnel, uint senderId, uint receiverId,
            TunnelPacketType packetType, byte[] payload = null)  => _tunnelCommunicator.SendPacket(tunnel, senderId, receiverId, packetType, payload);
    }
}