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
    public ref struct ParsedPacket
    {
        public uint SenderId { get; init; }
        public uint ReceiverId { get; init; }
        public TunnelPacketType? NegotiationType { get; init; }
        public ReadOnlySpan<byte> Payload { get; init; }
    }

    public enum TunnelPacketType : byte
    {
        Connected = 0x01,
        PingRequest = 0x02,
        PingResponse = 0x03,
        TunnelChoice = 0x04,
        TunnelAck = 0x05,
        NegotiationFailed = 0x06,
        Register = 0x07,
        GameData = 0x08
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

        public TunnelHandler(WindowManager wm, CnCNetManager connectionManager) : base(wm.Game)
        {
            this.wm = wm;
            this.connectionManager = connectionManager;

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

        private WindowManager wm;
        private readonly CnCNetManager connectionManager;

        private TimeSpan timeSinceTunnelRefresh = TimeSpan.MaxValue;
        private uint skipCount = 0;

        //V3

        //we'll connect to V3 tunnels in the TunneHandler so both the negotiator and bridge
        //can use the same endpoint and not get tripped up by the tunnel's mapping
        private bool _v3CommunicatorInitialized = false;
        private UdpClient _v3UdpClient;
        private Thread _v3ReceiveThread;
        private CancellationTokenSource _v3ReceiveCts;
        private readonly ConcurrentDictionary<string, CnCNetTunnel> _endpointToTunnel = new ConcurrentDictionary<string, CnCNetTunnel>();
        private readonly object _v3InitLock = new object();

        //when packets come in, we'll parse it and dish out the details to the appropriate negotiator.
        public delegate void PacketHandler(uint senderId, uint receiverId,
            TunnelPacketType packetType, byte[] payload, long receivedTime, CnCNetTunnel tunnel);

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

        private void ConnectionManager_Connected(object sender, EventArgs e)
        {
            if (!_v3CommunicatorInitialized)
                InitializeV3Communicator();
            Enabled = true;
        }

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
                    // Update
                    existingTunnel.UpdateFrom(newTunnel);
                    updatedTunnels.Add(existingTunnel);
                }
                else
                {
                    // Add
                    updatedTunnels.Add(newTunnel);
                }
            }

            // Remove
            Tunnels = updatedTunnels;

            TunnelsRefreshed?.Invoke(this, EventArgs.Empty);

            // Ping tunnels
            for (int i = 0; i < Tunnels.Count; i++)
            {
                if (UserINISettings.Instance.PingUnofficialCnCNetTunnels || Tunnels[i].Official || Tunnels[i].Recommended)
                    _ = PingListTunnelAsync(i);
            }

            if (CurrentTunnel != null)
            {
                var updatedTunnel = Tunnels.Find(t => t.Address == CurrentTunnel.Address && t.Port == CurrentTunnel.Port);
                if (updatedTunnel != null)
                {
                    CurrentTunnel = updatedTunnel;
                    DoCurrentTunnelPinged();
                }
                else
                {
                    // Current tunnel no longer in list, ping it separately
                    PingCurrentTunnelAsync();
                }
            }

            if (!_v3CommunicatorInitialized)
                InitializeV3Communicator();
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
        public void InitializeV3Communicator()
        {
            lock (_v3InitLock)
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
                    InitializeV3Connection(v3Tunnels);
                    _v3CommunicatorInitialized = true;
                    Logger.Log($"V3 tunnel communicator initialized with {v3Tunnels.Count} tunnels");
                }
                catch (Exception ex)
                {
                    Logger.Log($"Failed to initialize V3 tunnel communicator: {ex.Message}");
                }
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

        // <summary>
        /// Creates a packet - adds magic bytes + packet type for negotiation packets, 
        /// or just sender/receiver for game data packets
        /// </summary>
        public byte[] CreatePacket(uint senderId, uint receiverId, TunnelPacketType packetType, byte[] payload = null)
        {
            payload = payload ?? Array.Empty<byte>();

            if (packetType == TunnelPacketType.GameData)
            {
                // Game data packet: [senderId][receiverId][payload]
                var packet = new byte[8 + payload.Length];

                Array.Copy(BitConverter.GetBytes(senderId), 0, packet, 0, 4);
                Array.Copy(BitConverter.GetBytes(receiverId), 0, packet, 4, 4);
                Array.Copy(payload, 0, packet, 8, payload.Length);

                return packet;
            }
            else
            {
                // Negotiation packet: [senderId][receiverId][EJEJEJ][packetType][payload]

                byte[] packet;
                if (packetType == TunnelPacketType.Register)
                    packet = new byte[4 + 4 + 1];
                else
                    packet = new byte[4 + 4 + MAGIC_BYTES.Length + 1 + payload.Length];

                int offset = 0;

                // Sender ID
                Array.Copy(BitConverter.GetBytes(senderId), 0, packet, offset, 4);
                offset += 4;

                // Receiver ID
                Array.Copy(BitConverter.GetBytes(receiverId), 0, packet, offset, 4);
                offset += 4;

                // Magic bytes (EJEJEJ)
                if (packetType != TunnelPacketType.Register)
                {
                    Array.Copy(MAGIC_BYTES, 0, packet, offset, MAGIC_BYTES.Length);
                    offset += MAGIC_BYTES.Length;
                }

                // Packet type
                packet[offset] = (byte)packetType;
                offset++;

                // Payload
                if (payload.Length > 0)
                    Array.Copy(payload, 0, packet, offset, payload.Length);

                return packet;
            }
        }

        /// <summary>
        /// Sends registration packet to create/maintain tunnel's mapping
        /// </summary>
        public void SendRegistrationToAllTunnels(uint localId, List<CnCNetTunnel> tunnels = null)
        {
            var targetTunnels = (tunnels ?? Tunnels.Where(t => t.Version == 3))
                .Where(t => t.Version == 3)
                .ToList();

            foreach (var tunnel in targetTunnels)
            {
                try
                {
                    SendPacket(tunnel,localId,0u,TunnelPacketType.Register);
                    Debug.Print($"[REGISTRATION] Sent to {tunnel.Name}");
                }
                catch (Exception ex)
                {
                    Debug.Print($"[REGISTRATION ERROR] {tunnel.Name}: {ex.Message}");
                }
            }

            Debug.Print($"[REGISTRATION] Registration complete for ID {localId}");
        }

        /// <summary>
        /// Sends a packet to the specified tunnel
        /// </summary>
        public void SendPacket(CnCNetTunnel tunnel, uint senderId, uint receiverId,
            TunnelPacketType packetType, byte[] payload = null)
        {
            if (tunnel == null)
            {
                Debug.Print($"[SEND ERROR] Cannot send packet - tunnel is null");
                return;
            }

            try
            {
                var packet = CreatePacket(senderId, receiverId, packetType, payload);

                if (_v3UdpClient == null || tunnel == null)
                    return;

                try
                {
                    _v3UdpClient.Send(packet, packet.Length, tunnel.Address, tunnel.Port);
                    //Debug.Print($"[SEND] Sent {packet.Length} bytes to {tunnel.Name} ({tunnel.Address}:{tunnel.Port})");
                }
                catch (Exception ex)
                {
                    Debug.Print($"[SEND ERROR] {tunnel.Name}: {ex.Message}");
                }
            }
            catch (Exception ex)
            {
                Debug.Print($"[SEND ERROR] Failed to send {packetType} packet to {tunnel.Name}: {ex.Message}");
            }
        }

        private void InitializeV3Connection(List<CnCNetTunnel> tunnels)
        {
            try
            {
                _v3UdpClient = new UdpClient(0);

                _v3UdpClient.Client.ReceiveBufferSize = 65536;
                _v3UdpClient.Client.SendBufferSize = 65536;

                _endpointToTunnel.Clear();
                foreach (var tunnel in tunnels)
                {
                    try
                    {
                        string endpointKey = $"{tunnel.Address}:{tunnel.Port}";
                        _endpointToTunnel[endpointKey] = tunnel;

                        Debug.Print($"Added tunnel mapping: {endpointKey} -> {tunnel.Name}");
                    }
                    catch (Exception ex)
                    {
                        Debug.Print($"Failed to add tunnel {tunnel.Name} to endpoint mapping: {ex.Message}");
                    }
                }

                _v3ReceiveCts = new CancellationTokenSource();
                _v3ReceiveThread = new Thread(ReceivePackets)
                {
                    IsBackground = true,
                    Name = "V3TunnelReceive"
                };
                _v3ReceiveThread.Start();

                Debug.Print($"Initialized V3 tunnel connection with {_endpointToTunnel.Count} tunnels on local port {((IPEndPoint)_v3UdpClient.Client.LocalEndPoint).Port}");
            }
            catch (Exception ex)
            {
                Debug.Print($"Failed to initialize V3 connection: {ex.Message}");
                throw;
            }
        }

        private void ProcessPacket(byte[] data, long receivedTime, CnCNetTunnel tunnel)
        {
            try
            {
                var parsed = ParsePacket(data.AsSpan());

                // Handle negotiation packets
                if (parsed.NegotiationType.HasValue &&
                    _negotiationHandlers.TryGetValue((parsed.ReceiverId, parsed.SenderId), out var handler))
                {
                    try
                    {
                        byte[] payloadArray = parsed.Payload.ToArray();
                        handler(parsed.SenderId, parsed.ReceiverId, parsed.NegotiationType.Value,
                            payloadArray, receivedTime, tunnel);
                    }
                    catch (Exception ex)
                    {
                        Debug.Print($"Negotiation packet handler error: {ex.Message}");
                    }
                }
                // Handle game packets
                else if (!parsed.NegotiationType.HasValue && parsed.Payload.Length > 0)
                {
                    if (_negotiationHandlers.TryGetValue((parsed.ReceiverId, 0), out var gameHandler))
                    {
                        try
                        {
                            byte[] payloadArray = parsed.Payload.ToArray();
                            gameHandler(parsed.SenderId, parsed.ReceiverId, TunnelPacketType.GameData,
                                payloadArray, receivedTime, tunnel);
                        }
                        catch (Exception ex)
                        {
                            Debug.Print($"Game packet handler error: {ex.Message}");
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                Debug.Print($"Packet processing error from {tunnel.Name}: {ex.Message}");
            }
        }

        private ParsedPacket ParsePacket(ReadOnlySpan<byte> data)
        {
            if (data.Length < 8)
                return new ParsedPacket();

            uint senderId = BinaryPrimitives.ReadUInt32LittleEndian(data);
            uint receiverId = BinaryPrimitives.ReadUInt32LittleEndian(data.Slice(4));

            // Check if this is a negotiation packet (has magic bytes)
            if (data.Length >= 8 + MAGIC_BYTES.Length + 1)
            {
                var magicSpan = data.Slice(8, MAGIC_BYTES.Length);
                if (magicSpan.SequenceEqual(MAGIC_BYTES))
                {
                    var negotiationType = (TunnelPacketType)data[8 + MAGIC_BYTES.Length];
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

            // This is a game packet (no magic bytes) - payload starts after the 8-byte header
            var gamePayload = data.Length > 8 ? data[8..] : ReadOnlySpan<byte>.Empty;

            return new ParsedPacket
            {
                SenderId = senderId,
                ReceiverId = receiverId,
                NegotiationType = null,
                Payload = gamePayload
            };
        }

        private void ReceivePackets()
        {
            byte[] buffer = new byte[4096];
            int receiveErrors = 0;
            const int MAX_CONSECUTIVE_ERRORS = 10;

            try
            {
                while (!_v3ReceiveCts.Token.IsCancellationRequested)
                {
                    try
                    {
                        IPEndPoint remoteEndpoint = new IPEndPoint(IPAddress.Any, 0);
                        byte[] data = _v3UdpClient.Receive(ref remoteEndpoint);
                        var receivedTime = Stopwatch.GetTimestamp();

                        receiveErrors = 0;

                        // Find which tunnel this packet came from
                        string endpointKey = $"{remoteEndpoint.Address}:{remoteEndpoint.Port}";

                        if (_endpointToTunnel.TryGetValue(endpointKey, out var tunnel))
                        {
                            //Debug.Print($"[RECV] Got {data.Length} bytes from {tunnel.Name} ({endpointKey})");
                            ProcessPacket(data, receivedTime, tunnel);
                        }
                        else
                        {
                            Debug.Print($"[RECV] Got packet from unknown endpoint: {endpointKey}");
                        }
                    }
                    catch (SocketException ex) when (ex.SocketErrorCode == SocketError.TimedOut)
                    {
                        continue;
                    }
                    catch (SocketException ex) when (ex.SocketErrorCode == SocketError.Interrupted ||
                                                     ex.SocketErrorCode == SocketError.OperationAborted)
                    {
                        Debug.Print("Receive thread: Socket closed, exiting");
                        break;
                    }
                    catch (SocketException ex)
                    {
                        receiveErrors++;
                        Debug.Print($"Socket error in receive thread: {ex.SocketErrorCode} - {ex.Message}");

                        if (receiveErrors >= MAX_CONSECUTIVE_ERRORS)
                        {
                            Debug.Print($"Too many consecutive receive errors ({receiveErrors}), exiting receive thread");
                            break;
                        }

                        Thread.Sleep(10);
                    }
                }
            }
            catch (ObjectDisposedException)
            {
                Debug.Print("Receive thread: Socket disposed");
            }
            catch (Exception ex)
            {
                Debug.Print($"Unexpected error in receive thread: {ex.Message}");
            }
            finally
            {
                Debug.Print("Receive thread exiting");
            }
        }

        public void Dispose()
        {
            Dispose(true);
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
            lock (_v3InitLock)
            {
                if (!_v3CommunicatorInitialized)
                    return;

                _v3CommunicatorInitialized = false;

                _v3ReceiveCts?.Cancel();

                if (_v3ReceiveThread != null && _v3ReceiveThread.IsAlive)
                {
                    if (!_v3ReceiveThread.Join(2000))
                    {
                        Debug.Print("V3 receive thread did not terminate gracefully");
                    }
                }

                try
                {
                    _v3UdpClient?.Close();
                    _v3UdpClient?.Dispose();
                }
                catch (Exception ex)
                {
                    Debug.Print($"Error disposing UDP client: {ex.Message}");
                }

                _endpointToTunnel.Clear();
                _negotiationHandlers.Clear();

                _v3ReceiveCts?.Dispose();

                _v3UdpClient = null;
                _v3ReceiveThread = null;
                _v3ReceiveCts = null;

                Debug.Print("V3 tunnel communicator disposed.");
            }
        }

        #endregion
    }
}