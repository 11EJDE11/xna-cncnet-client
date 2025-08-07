using ClientCore;
using DTAClient.Online;
using Microsoft.Xna.Framework;
using Rampastring.Tools;
using Rampastring.XNAUI;
using System;
using System.Buffers.Binary;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace DTAClient.Domain.Multiplayer.CnCNet
{
    public class IncomingPacket
    {
        public byte[] Data { get; set; }
        public DateTime ReceivedTime { get; set; } //log this for accurate pings as the packets will enter a queue
        public CnCNetTunnel Tunnel { get; set; }
    }

    public ref struct ParsedPacket
    {
        public uint SenderId { get; init; }
        public uint ReceiverId { get; init; }
        public NegotiationPacketType? NegotiationType { get; init; }
        public ReadOnlySpan<byte> Payload { get; init; }
    }

    public enum NegotiationPacketType : byte
    {
        Connected = 0x01,
        PingRequest = 0x02,
        PingResponse = 0x03,
        TunnelChoice = 0x04,
        TunnelAck = 0x05,
        NegotiationFailed = 0x06
    }

    public class TunnelHandler : GameComponent, IDisposable
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

        private static readonly int[] SUPPORTED_TUNNEL_VERSIONS = { 2, 3 };

        private static readonly byte[] MAGIC_BYTES = { 0x45, 0x4A, 0x45, 0x4A, 0x45, 0x4A }; //EJEJEJ

        private const int PACKET_QUEUE_CAPACITY = 1000;
        private const int PACKET_PROCESSOR_COUNT = 4;
        private readonly BlockingCollection<IncomingPacket> _packetQueue;
        private readonly Task[] _packetProcessorTasks;
        private readonly CancellationTokenSource _processingCts = new CancellationTokenSource();

        public class TunnelConnection
        {
            public CnCNetTunnel Tunnel { get; set; }
            public UdpClient Client { get; set; }
            public CancellationTokenSource ReceiveCts { get; set; }
            public bool IsActive { get; set; } = true;
            public DateTime LastRegistration { get; set; }
            public Task ReceiveTask { get; set; }
        }

        public TunnelHandler(WindowManager wm, CnCNetManager connectionManager) : base(wm.Game)
        {
            this.wm = wm;
            this.connectionManager = connectionManager;

            _packetQueue = new BlockingCollection<IncomingPacket>(new ConcurrentQueue<IncomingPacket>(), PACKET_QUEUE_CAPACITY);

            _packetProcessorTasks = new Task[PACKET_PROCESSOR_COUNT];
            for (int i = 0; i < PACKET_PROCESSOR_COUNT; i++)
            {
                _packetProcessorTasks[i] = Task.Factory.StartNew(
                () => ProcessPacketsWorker(_processingCts.Token),
                _processingCts.Token,
                TaskCreationOptions.LongRunning,
                TaskScheduler.Default);
            }

            wm.Game.Components.Add(this);

            Enabled = false;

            connectionManager.Connected += ConnectionManager_Connected;
            connectionManager.Disconnected += ConnectionManager_Disconnected;
            connectionManager.ConnectionLost += ConnectionManager_ConnectionLost;
        }

        public List<CnCNetTunnel> Tunnels { get; private set; } = new List<CnCNetTunnel>();
        public CnCNetTunnel CurrentTunnel { get; set; } = null;

        public event EventHandler TunnelsRefreshed;
        public event EventHandler CurrentTunnelPinged;
        public event Action<int> TunnelPinged;

        public event Action<byte[], DateTime, CnCNetTunnel> PacketReceived;

        private WindowManager wm;
        private readonly CnCNetManager connectionManager;

        private TimeSpan timeSinceTunnelRefresh = TimeSpan.MaxValue;
        private uint skipCount = 0;

        //V3

        //we'll connect to V3 tunnels in the TunneHandler so both the negotiator and bridge
        //can use the same endpoint and not get tripped up by the tunnel's mapping
        private readonly ConcurrentDictionary<string, TunnelConnection> _v3Connections = new ConcurrentDictionary<string, TunnelConnection>();
        private readonly CancellationTokenSource _globalCts = new CancellationTokenSource();
        private bool _v3CommunicatorInitialized = false;
        
        //when packets come in, we'll parse it and dish out the details to the appropriate negotiator.
        public delegate void PacketHandler(uint senderId, uint receiverId,
                NegotiationPacketType packetType, byte[] payload, DateTime receivedTime, CnCNetTunnel tunnel);

        private readonly ConcurrentDictionary<(uint localId, uint remoteId), PacketHandler> _negotiationHandlers
                = new ConcurrentDictionary<(uint localId, uint remoteId), PacketHandler>();

        private void DoTunnelPinged(int index)
        {
            if (TunnelPinged != null)
                wm.AddCallback(TunnelPinged, index);
        }

        private void DoCurrentTunnelPinged()
        {
            if (CurrentTunnelPinged != null)
                wm.AddCallback(CurrentTunnelPinged, this, EventArgs.Empty);
        }

        private void ConnectionManager_Connected(object sender, EventArgs e) => Enabled = true;

        private void ConnectionManager_ConnectionLost(object sender, Online.EventArguments.ConnectionLostEventArgs e)
        {
            Enabled = false;
            DisposeV3Communicator();
        }

        private void ConnectionManager_Disconnected(object sender, EventArgs e)
        {
            Enabled = false;
            DisposeV3Communicator();
        }

        private void RefreshTunnelsAsync()
        {
            Task.Factory.StartNew(() =>
            {
                List<CnCNetTunnel> tunnels = RefreshTunnels();
                wm.AddCallback(new Action<List<CnCNetTunnel>>(HandleRefreshedTunnels), tunnels);
            });
        }

        private void HandleRefreshedTunnels(List<CnCNetTunnel> tunnels)
        {
            if (tunnels.Count > 0)
                Tunnels = tunnels;

            TunnelsRefreshed?.Invoke(this, EventArgs.Empty);

            Task[] pingTasks = new Task[Tunnels.Count];

            for (int i = 0; i < Tunnels.Count; i++)
            {
                if (UserINISettings.Instance.PingUnofficialCnCNetTunnels || Tunnels[i].Official || Tunnels[i].Recommended)
                    pingTasks[i] = PingListTunnelAsync(i);
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

            // Refresh V3 connections when tunnel list updates
            // (adds/removes tunnels, keeping same UDP deets)
            if (!_v3CommunicatorInitialized)
                _ = InitializeV3CommunicatorAsync();
            else
                _ = RefreshV3ConnectionsAsync();
        }

        private Task PingListTunnelAsync(int index)
        {
            return Task.Factory.StartNew(() =>
            {
                Tunnels[index].UpdatePing();
                DoTunnelPinged(index);
            });
        }

        private Task PingCurrentTunnelAsync(bool checkTunnelList = false)
        {
            return Task.Factory.StartNew(() =>
            {
                var tunnel = CurrentTunnel;
                if (tunnel == null) return;

                tunnel.UpdatePing();
                DoCurrentTunnelPinged();

                if (checkTunnelList)
                {
                    int tunnelIndex = Tunnels.FindIndex(t => t.Address == tunnel.Address && t.Port == tunnel.Port);
                    if (tunnelIndex > -1)
                        DoTunnelPinged(tunnelIndex);
                }
            });
        }

        private bool OnlineTunnelDataAvailable => !string.IsNullOrWhiteSpace(ClientConfiguration.Instance.CnCNetTunnelListURL);
        private bool OfflineTunnelDataAvailable => SafePath.GetFile(ProgramConstants.ClientUserFilesPath, "tunnel_cache").Exists;

        private byte[] GetRawTunnelDataOnline()
        {
            WebClient client = new ExtendedWebClient();
            return client.DownloadData(ClientConfiguration.Instance.CnCNetTunnelListURL);
        }

        private byte[] GetRawTunnelDataOffline()
        {
            FileInfo tunnelCacheFile = SafePath.GetFile(ProgramConstants.ClientUserFilesPath, "tunnel_cache");
            return File.ReadAllBytes(tunnelCacheFile.FullName);
        }

        private byte[] GetRawTunnelData(int retryCount = 2)
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
        private List<CnCNetTunnel> RefreshTunnels()
        {
            List<CnCNetTunnel> returnValue = new List<CnCNetTunnel>();

            FileInfo tunnelCacheFile = SafePath.GetFile(ProgramConstants.ClientUserFilesPath, "tunnel_cache");

            byte[] data = GetRawTunnelData();
            if (data is null)
                return returnValue;

            string convertedData = Encoding.Default.GetString(data);

            string[] serverList = convertedData.Split(new string[] { "\r\n", "\n" }, StringSplitOptions.RemoveEmptyEntries);

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
            if (timeSinceTunnelRefresh > TimeSpan.FromSeconds(CURRENT_TUNNEL_PING_INTERVAL))
            {
                if (skipCount % CYCLES_PER_TUNNEL_LIST_REFRESH == 0)
                {
                    skipCount = 0;
                    RefreshTunnelsAsync();
                }
                else if (CurrentTunnel != null)
                {
                    PingCurrentTunnelAsync(true);
                }

                timeSinceTunnelRefresh = TimeSpan.Zero;
                skipCount++;
            }
            else
                timeSinceTunnelRefresh += gameTime.ElapsedGameTime;

            base.Update(gameTime);
        }

        #region V3 Tunnel Communication Methods

        /// <summary>
        /// Initialize V3 tunnel communicator with available V3 tunnels
        /// </summary>
        public async Task InitializeV3CommunicatorAsync()
        {
            if (_v3CommunicatorInitialized)
                return;

            var v3Tunnels = Tunnels.Where(t => t.Version == 3 &&
                    (UserINISettings.Instance.PingUnofficialCnCNetTunnels || t.Official || t.Recommended))
                    .ToList();

            if (v3Tunnels.Count == 0)
            {
                Logger.Log("No V3 tunnels available.");
                return;
            }

            try
            {
                await InitializeV3ConnectionsAsync(v3Tunnels);
                _v3CommunicatorInitialized = true;
                Logger.Log($"V3 tunnel communicator initialized with {v3Tunnels.Count} tunnels");
            }
            catch (Exception ex)
            {
                Logger.Log($"Failed to initialize V3 tunnel communicator: {ex.Message}");
            }
        }

        public void RegisterNegotiationHandler(uint localId, uint remoteId, PacketHandler handler)
        {
            _negotiationHandlers.AddOrUpdate((localId, remoteId), handler, (key, existingVal) => handler);
            Debug.Print($"Registered negotiation handler for {localId} <-> {remoteId}");
        }

        public void UnregisterNegotiationHandler(uint localId, uint remoteId)
        {
            _negotiationHandlers.TryRemove((localId, remoteId), out _);
            Debug.Print($"Unregistered negotiation handler for {localId} <-> {remoteId}");
        }

        public async Task SendPacketAsync(string address, int port, byte[] packet) => await SendPacketAsync(GetTunnelConnection($"{address}:{port}"), packet);

        private async Task SendPacketAsync(CnCNetTunnel tunnel, byte[] packet) => await SendPacketAsync(GetTunnelConnection(GetTunnelKey(tunnel)), packet);

        private async Task SendPacketAsync(TunnelConnection connection, byte[] packet)
        {
            if (connection?.IsActive != true)
            {
                Debug.Print($"[SEND ERROR] No active connection");
                return;
            }

            try
            {
                await connection.Client.SendAsync(packet, packet.Length);
                //Debug.Print($"[SEND] {GetPacketTypeForDebug(packet)} to {connection.Tunnel.Name} ({packet.Length} bytes)");
            }
            catch (Exception ex)
            {
                Debug.Print($"[SEND ERROR] {connection.Tunnel.Name}: {ex.Message}");
                connection.IsActive = false;
            }
        }

        private TunnelConnection GetTunnelConnection(string key)
        {
            return _v3Connections.TryGetValue(key, out var conn) && conn.IsActive ? conn : null;
        }

        private async Task InitializeV3ConnectionsAsync(List<CnCNetTunnel> tunnels)
        {
            var tasks = tunnels.Select(async tunnel =>
            {
                try
                {
                    var client = new UdpClient(0);
                    client.Connect(tunnel.Address, tunnel.Port);

                    var connection = new TunnelConnection
                    {
                        Tunnel = tunnel,
                        Client = client,
                        ReceiveCts = new CancellationTokenSource()
                    };

                    var key = GetTunnelKey(tunnel);
                    _v3Connections.AddOrUpdate(key, connection, (k, existingConnection) =>
                    {
                        return connection;
                    });

                    connection.ReceiveTask = ReceivePacketsAsync(connection);
                    return connection;
                }
                catch (Exception ex)
                {
                    Debug.Print($"Failed to initialize tunnel {tunnel.Name}: {ex.Message}");
                    return null;
                }
            });

            await Task.WhenAll(tasks);
            Debug.Print($"Initialized {_v3Connections.Count} V3 tunnel connections");
        }

        private async Task RefreshV3ConnectionsAsync()
        {
            if (!_v3CommunicatorInitialized)
                return;

            var v3Tunnels = Tunnels.Where(t => t.Version == 3 &&
            (UserINISettings.Instance.PingUnofficialCnCNetTunnels || t.Official || t.Recommended))
            .ToList();

            var currentKeys = v3Tunnels.Select(GetTunnelKey).ToHashSet();

            var keysToRemove = _v3Connections.Keys.Where(key => !currentKeys.Contains(key)).ToList();

            foreach (var key in keysToRemove)
            {
                if (_v3Connections.TryRemove(key, out var kvpValue))
                {
                    Debug.Print($"[V3] Closing connection to removed tunnel {kvpValue.Tunnel.Name}");
                    kvpValue.ReceiveCts?.Cancel();
                    kvpValue.Client?.Close();
                }
            }

            // Update existing connections with refreshed tunnel info
            //but keep the same UDP connection to preserve port mapping
            foreach (var tunnel in v3Tunnels)
            {
                var key = GetTunnelKey(tunnel);
                if (_v3Connections.TryGetValue(key, out var existingConnection))
                {
                    existingConnection.Tunnel = tunnel;
                    Debug.Print($"[V3] Updated tunnel info for {tunnel.Name} (keep same UDP port)");
                }
            }

            //add connections for new tunnels only
            var newTunnels = v3Tunnels.Where(t => !_v3Connections.ContainsKey(GetTunnelKey(t))).ToList();
            if (newTunnels.Count > 0)
            {
                Debug.Print($"[V3] Adding connections to {newTunnels.Count} new tunnels");
                await InitializeV3ConnectionsAsync(newTunnels);
            }
        }

        /// <summary>
        /// Sends registration packet to create/maintain tunnel's mapping
        /// </summary>
        public async Task SendRegistrationAsync(uint localId, List<CnCNetTunnel> tunnels = null)
        {
            var packet = new byte[9];
            BitConverter.GetBytes(localId).CopyTo(packet, 0);
            BitConverter.GetBytes(0u).CopyTo(packet, 4);
            packet[8] = 0xFF;

            var connections = (tunnels ?? Enumerable.Empty<CnCNetTunnel>())
                .Select(tunnel => _v3Connections.TryGetValue(GetTunnelKey(tunnel), out var conn) && conn.IsActive ? conn : null)
                .Where(conn => conn != null)
                .ToList();

            if (connections.Count == 0)
            {
                Debug.Print("[REGISTRATION ERROR] No active connections available!");
                return;
            }

            var tasks = connections.Select(async c =>
            {
                Debug.Print($"[REGISTRATION] Sending to {c.Tunnel.Name}");
                await SendPacketAsync(c.Tunnel, packet);
                c.LastRegistration = DateTime.UtcNow;
            });

            await Task.WhenAll(tasks);
            Debug.Print($"[REGISTRATION] Registration complete for ID {localId}");
        }

        private void ProcessPacketsWorker(CancellationToken cancellationToken)
        {
            try
            {
                while (!cancellationToken.IsCancellationRequested)
                {
                    IncomingPacket packet;
                    try
                    {
                        packet = _packetQueue.Take(cancellationToken);
                    }
                    catch (OperationCanceledException)
                    {
                        break;
                    }
                    catch (InvalidOperationException)
                    {
                        break;
                    }

                    try
                    {
                        ProcessPacket(packet);
                    }
                    catch (Exception ex)
                    {
                        Debug.Print($"Error processing packet from {packet.Tunnel.Name}: {ex.Message}");
                    }
                }
            }
            catch (Exception ex)
            {
                Debug.Print($"Packet processor worker failed: {ex.Message}");
            }

            Debug.Print("Packet processor worker stopped");
        }

        private void ProcessPacket(IncomingPacket packet)
        {
            try
            {
                var data = packet.Data.AsSpan();
                var parsed = ParsePacket(data);

                if (_negotiationHandlers.TryGetValue((parsed.ReceiverId, parsed.SenderId), out var handler))
                {
                    try
                    {
                        byte[] payloadArray = parsed.Payload.ToArray();
                        handler(parsed.SenderId, parsed.ReceiverId, parsed.NegotiationType.Value,
                        payloadArray, packet.ReceivedTime, packet.Tunnel);
                    }
                    catch (Exception ex)
                    {
                        Debug.Print($"Negotiation packet handler error: {ex.Message}");
                    }
                }
            }
            catch (Exception ex)
            {
                Debug.Print($"Packet processing error: {ex.Message}");
            }
        }

        private ParsedPacket ParsePacket(ReadOnlySpan<byte> data)
        {
            uint senderId = BinaryPrimitives.ReadUInt32LittleEndian(data);
            uint receiverId = BinaryPrimitives.ReadUInt32LittleEndian(data.Slice(4));
            
            if (data.Length >= 8 + MAGIC_BYTES.Length + 1)
            {
                var magicSpan = data.Slice(8, MAGIC_BYTES.Length);
                if (magicSpan.SequenceEqual(MAGIC_BYTES))
                {
                    var negotiationType = (NegotiationPacketType)data[8 + MAGIC_BYTES.Length];
                    var payloadStart = 8 + MAGIC_BYTES.Length + 1;
                    var payload = payloadStart < data.Length ? data[payloadStart..] : ReadOnlySpan<byte>.Empty;

                    return new ParsedPacket
                    {
                        SenderId = senderId,
                        ReceiverId = receiverId,
                        NegotiationType = negotiationType,
                        Payload = payload
                    };
                }
            }

            return new ParsedPacket();
        }

        private async Task ReceivePacketsAsync(TunnelConnection connection)
        {
            Debug.Print($"[RECEIVE] Started listening on {connection.Tunnel.Name}");

            while (!connection.ReceiveCts.Token.IsCancellationRequested &&
            !_globalCts.Token.IsCancellationRequested &&
            !_processingCts.Token.IsCancellationRequested &&
            connection.IsActive)
            {
                try
                {
                    var result = await connection.Client.ReceiveAsync();
                    //Debug.Print($"[RECEIVE] Got {result.Buffer.Length} bytes from {connection.Tunnel.Name}");

                    var incomingPacket = new IncomingPacket
                    {
                        Data = result.Buffer,
                        ReceivedTime = DateTime.UtcNow,
                        Tunnel = connection.Tunnel
                    };

                    try
                    {
                        if (!_packetQueue.TryAdd(incomingPacket, 100, _processingCts.Token))
                            Debug.Print($"[RECEIVE] Packet queue full, dropping packet from {connection.Tunnel.Name}");
                    }
                    catch (OperationCanceledException)
                    {
                        Debug.Print($"[RECEIVE] Queue add cancelled for {connection.Tunnel.Name}");
                        break;
                    }
                    catch (InvalidOperationException)
                    {
                        Debug.Print($"[RECEIVE] Queue completed for {connection.Tunnel.Name}");
                        break;
                    }
                }
                catch (ObjectDisposedException)
                {
                    Debug.Print($"[RECEIVE] ObjectDisposed on {connection.Tunnel.Name}");
                    break;
                }
                catch (Exception ex)
                {
                    Debug.Print($"[RECEIVE ERROR] {connection.Tunnel.Name}: {ex.Message}");
                    connection.IsActive = false;
                    break;
                }
            }

            Debug.Print($"[RECEIVE] Stopped listening on {connection.Tunnel.Name}");
        }

        public TunnelConnection GetTunnelConnection(string address, int port)
        {
            return _v3Connections.TryGetValue($"{address}:{port}", out var connection) ? connection : null;
        }

        private string GetTunnelKey(CnCNetTunnel tunnel) => $"{tunnel.Address}:{tunnel.Port}";

        public void Dispose()
        {
            Dispose(true);
            GC.SuppressFinalize(this);
        }

        protected virtual void Dispose(bool disposing)
        {
            if (disposing)
            {
                connectionManager.Connected -= ConnectionManager_Connected;
                connectionManager.Disconnected -= ConnectionManager_Disconnected;
                connectionManager.ConnectionLost -= ConnectionManager_ConnectionLost;

                DisposeV3Communicator();
            }
        }
        private void DisposeV3Communicator()
        {
            if (!_v3CommunicatorInitialized)
                return;

            _processingCts.Cancel();

            _packetQueue.CompleteAdding();

            try
            {
                Task.WaitAll(_packetProcessorTasks, TimeSpan.FromSeconds(5));
            }
            catch (AggregateException ex)
            {
                Debug.Print($"Some packet processor tasks didn't complete cleanly: {ex.Message}");
            }

            var receiveTasks = _v3Connections.Values
                .Where(c => c.ReceiveTask != null)
                .Select(c => c.ReceiveTask)
                .ToArray();

            try
            {
                Task.WaitAll(receiveTasks, TimeSpan.FromSeconds(2));
            }
            catch (AggregateException)
            {
                // Some tasks didn't complete - they'll be abandoned
            }
            _packetQueue.Dispose();

            _globalCts.Cancel();
            _globalCts.Dispose();
            _processingCts.Dispose();

            foreach (var kvp in _v3Connections)
            {
                kvp.Value.ReceiveCts?.Cancel();
                kvp.Value.ReceiveCts?.Dispose();
                kvp.Value.Client?.Close();
                kvp.Value.Client?.Dispose();
            }
            _v3Connections.Clear();

            _negotiationHandlers.Clear();

            _v3CommunicatorInitialized = false;
            Debug.Print("V3 tunnel communicator disposed.");
        }

        #endregion
    }
}