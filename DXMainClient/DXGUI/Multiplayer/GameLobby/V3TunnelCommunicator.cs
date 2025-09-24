using ClientCore;

using Rampastring.Tools;

using System;
using System.Buffers.Binary;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Net;
using System.Net.Sockets;
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
        GameData = 0x08,
        P2PEndpointExchange = 0x09
    }

    public delegate void PacketHandler(uint senderId, uint receiverId,
        TunnelPacketType packetType, byte[] payload, long receivedTime, CnCNetTunnel tunnel);

    public class V3TunnelCommunicator : IDisposable
    {
        private static readonly byte[] MAGIC_BYTES = { 0x45, 0x4A, 0x45, 0x4A, 0x45, 0x4A }; //EJEJEJ

        private UdpClient _udpClient;
        private Thread _receiveThread;
        private CancellationTokenSource _receiveCts;
        private readonly ConcurrentDictionary<IPEndPoint, CnCNetTunnel> _endpointToTunnel = new();
        private readonly ConcurrentDictionary<(uint localId, uint remoteId), PacketHandler> _handlers = new();
        private readonly object _initLock = new();
        private UdpClient _p2pClient;
        private Thread _p2pReceiveThread;
        private CancellationTokenSource _p2pReceiveCts;
        private TaskCompletionSource<IPEndPoint> _stunTcs;
        public bool IsInitialized => _udpClient != null;

        /// <summary>
        /// Initializes the communicator with the provided V3-compatible tunnels,
        /// sets up UDP socket, and starts the background receive thread.
        /// </summary>
        public void Initialize(List<CnCNetTunnel> tunnels)
        {
            lock (_initLock)
            {
                if (IsInitialized)
                    return;

                var v3Tunnels = tunnels.Where(t => t.Version == 3 &&
                    (UserINISettings.Instance.PingUnofficialCnCNetTunnels || t.Official || t.Recommended))
                    .ToList();

                if (v3Tunnels.Count == 0)
                {
                    Logger.Log("No V3 tunnels available.");
                    return;
                }

                InitializeConnection(v3Tunnels);
                Logger.Log($"V3 tunnel communicator initialized with {v3Tunnels.Count} tunnels");
            }
        }

        /// <summary>
        /// Registers a handler for packets between the specified local and remote IDs.
        /// Only one handler per ID pair is kept at a time.
        /// </summary>
        public void RegisterHandler(uint localId, uint remoteId, PacketHandler handler)
        {
            _handlers[(localId, remoteId)] = handler;
            Debug.Print($"Registered handler for {localId} <-> {remoteId}");
        }

        /// <summary>
        /// Removes the handler for the specified local/remote ID pair.
        /// </summary>
        public void UnregisterHandler(uint localId, uint remoteId)
        {
            _handlers.TryRemove((localId, remoteId), out _);
            Debug.Print($"Unregistered handler for {localId} <-> {remoteId}");
        }

        /// <summary>
        /// Builds a properly formatted packet for sending through a V3 tunnel.
        /// </summary>
        public byte[] CreatePacket(uint senderId, uint receiverId, TunnelPacketType packetType, byte[] payload = null)
        {
            const int HeaderSize = 8;

            payload ??= Array.Empty<byte>();

            int extraLength = packetType switch
            {
                TunnelPacketType.Register => 0,
                TunnelPacketType.GameData => 0,
                _ => MAGIC_BYTES.Length + 1
            };

            var packet = new byte[HeaderSize + extraLength + payload.Length];
            var span = packet.AsSpan();

            BinaryPrimitives.WriteUInt32LittleEndian(span, senderId);
            BinaryPrimitives.WriteUInt32LittleEndian(span[4..], receiverId);

            if (packetType == TunnelPacketType.Register)
                return packet;

            if (packetType != TunnelPacketType.GameData)
            {
                MAGIC_BYTES.CopyTo(span[HeaderSize..]);
                span[HeaderSize + MAGIC_BYTES.Length] = (byte)packetType;
                payload.CopyTo(span[(HeaderSize + sizeof(TunnelPacketType) + MAGIC_BYTES.Length)..]);
            }
            else
            {
                payload.CopyTo(span[HeaderSize..]);
            }

            return packet;
        }

        /// <summary>
        /// Sends a registration packet to all known V3 tunnels, 
        /// or to a provided subset of tunnels.
        /// </summary>
        public void SendRegistrationToAllTunnels(uint localId, List<CnCNetTunnel> tunnels = null)
        {
            if (!IsInitialized)
                return;

            var targetTunnels = tunnels?.Where(t => t.Version == 3).ToList() ??
                               _endpointToTunnel.Values.ToList();

            var packet = CreatePacket(localId, 0u, TunnelPacketType.Register);
            foreach (var tunnel in targetTunnels)
            {
                try
                {
                    _udpClient.Send(packet, packet.Length, tunnel.Address, tunnel.Port);
                    Debug.Print($"[REGISTRATION] Sent to {tunnel.Name}");
                }
                catch (Exception ex)
                {
                    Debug.Print($"[REGISTRATION ERROR] {tunnel.Name}: {ex.Message}");
                }
            }
        }

        /// <summary>
        /// Sends a packet to the specified tunnel. 
        /// </summary>
        public void SendPacket(CnCNetTunnel tunnel, uint senderId, uint receiverId,
            TunnelPacketType packetType, byte[] payload = null)
        {
            if (!IsInitialized || tunnel == null)
            {
                Debug.Print($"[SEND ERROR] Cannot send packet - communicator not initialized or tunnel is null");
                return;
            }

            try
            {
                var packet = CreatePacket(senderId, receiverId, packetType, payload);
                _udpClient.Send(packet, packet.Length, tunnel.Address, tunnel.Port);
            }
            catch (Exception ex)
            {
                Debug.Print($"[SEND ERROR] Failed to send {packetType} packet to {tunnel.Name}: {ex.Message}");
            }
        }

        private void InitializeConnection(List<CnCNetTunnel> tunnels)
        {
            _udpClient = new UdpClient(0);
            _udpClient.Client.ReceiveBufferSize = 65536;
            _udpClient.Client.SendBufferSize = 65536;

            _endpointToTunnel.Clear();
            foreach (var tunnel in tunnels)
            {
                var endpoint = new IPEndPoint(IPAddress.Parse(tunnel.Address), tunnel.Port);
                _endpointToTunnel[endpoint] = tunnel;
                Debug.Print($"Added tunnel mapping: {endpoint} -> {tunnel.Name}");
            }

            _receiveCts = new CancellationTokenSource();
            _receiveThread = new Thread(ReceivePackets)
            {
                IsBackground = true,
                Name = "V3TunnelReceive"
            };
            _receiveThread.Start();

            Debug.Print($"Initialized V3 tunnel connection with {_endpointToTunnel.Count} tunnels on local port {((IPEndPoint)_udpClient.Client.LocalEndPoint).Port}");
        }

        private void ProcessReceivedPacket(byte[] data, long receivedTime, CnCNetTunnel tunnel)
        {
            try
            {
                var parsed = ParsePacket(data.AsSpan());
                if (parsed.Payload.Length == 0 && !parsed.NegotiationType.HasValue)
                    return;

                PacketHandler handler = null;

                if (parsed.NegotiationType.HasValue)
                    _handlers.TryGetValue((parsed.ReceiverId, parsed.SenderId), out handler);
                else if (parsed.Payload.Length > 0)
                    _handlers.TryGetValue((parsed.ReceiverId, 0), out handler);

                if (handler != null)
                {
                    handler(parsed.SenderId, parsed.ReceiverId,
                        parsed.NegotiationType ?? TunnelPacketType.GameData,
                        parsed.Payload.ToArray(), receivedTime, tunnel);
                }
            }
            catch (Exception ex)
            {
                Debug.Print($"Packet processing error from {tunnel.Name}: {ex.Message}");
            }
        }

        /// <summary>
        /// Parses an incoming raw UDP packet into a <see cref="ParsedPacket"/>.
        /// Detects negotiation vs. game data based on presence of magic bytes.
        /// </summary>
        private ParsedPacket ParsePacket(ReadOnlySpan<byte> data)
        {
            const int HeaderSize = 8;

            if (data.Length < HeaderSize)
                return new ParsedPacket();

            uint senderId = BinaryPrimitives.ReadUInt32LittleEndian(data);
            uint receiverId = BinaryPrimitives.ReadUInt32LittleEndian(data.Slice(4));

            if (data.Length >= HeaderSize + MAGIC_BYTES.Length + sizeof(TunnelPacketType) &&
                data.Slice(HeaderSize, MAGIC_BYTES.Length).SequenceEqual(MAGIC_BYTES))
            {
                var negotiationType = (TunnelPacketType)data[HeaderSize + MAGIC_BYTES.Length];
                var payload = data[(HeaderSize + sizeof(TunnelPacketType) + MAGIC_BYTES.Length)..];
                return new ParsedPacket
                {
                    SenderId = senderId,
                    ReceiverId = receiverId,
                    NegotiationType = negotiationType,
                    Payload = payload
                };
            }

            var gamePayload = data.Length > HeaderSize ? data[HeaderSize..] : ReadOnlySpan<byte>.Empty;
            return new ParsedPacket
            {
                SenderId = senderId,
                ReceiverId = receiverId,
                NegotiationType = null,
                Payload = gamePayload
            };
        }

        /// <summary>
        /// Main loop for receiving packets on the UDP socket.
        /// Dispatches packets to the correct handler based on endpoint.
        /// Runs on a background thread.
        /// </summary>
        private void ReceivePackets()
        {
            try
            {
                IPEndPoint remoteEndpoint = new IPEndPoint(IPAddress.Any, 0);
                while (!_receiveCts.Token.IsCancellationRequested)
                {
                    try
                    {
                        byte[] data = _udpClient.Receive(ref remoteEndpoint);
                        var receivedTime = Stopwatch.GetTimestamp();

                        if (_endpointToTunnel.TryGetValue(remoteEndpoint, out var tunnel))
                            ProcessReceivedPacket(data, receivedTime, tunnel);
                        else
                            Debug.Print($"[RECV] Got packet from unknown endpoint: {remoteEndpoint}");
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
                        Debug.Print($"Socket error in receive thread: {ex.SocketErrorCode} - {ex.Message}");

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

        public async Task<IPEndPoint> InitializeP2PConnectionAsync(int timeoutMs = 5000)
        {
            var officialTunnels = _endpointToTunnel.Values
                .Where(t => t.Official && t.Version == 3)
                .ToList();

            if (officialTunnels.Count == 0)
                return null;

            if (_p2pClient == null)
                InitializeP2PClient();

            const int STUN_ID = 26262;
            var stunPacket = new byte[48];
            new Random().NextBytes(stunPacket);

            // STUN ID at offset 6
            var stunIdBytes = BitConverter.GetBytes(IPAddress.HostToNetworkOrder((short)STUN_ID));
            Array.Copy(stunIdBytes, 0, stunPacket, 0, 2);

            _stunTcs = new TaskCompletionSource<IPEndPoint>();

            try
            {
                foreach (var tunnel in officialTunnels)
                {
                    try
                    {
                        await _p2pClient.SendAsync(stunPacket, stunPacket.Length, tunnel.Address, 8054);
                        Debug.Print($"[STUN] Sent request to {tunnel.Name}");
                    }
                    catch (Exception ex)
                    {
                        Debug.Print($"[STUN] Failed to send to {tunnel.Name}: {ex.Message}");
                    }
                }

                using var cts = new CancellationTokenSource(timeoutMs);
                try
                {
                    cts.Token.Register(() => _stunTcs.TrySetResult(null));
                    return await _stunTcs.Task;
                }
                catch (OperationCanceledException)
                {
                    Debug.Print("[STUN] Request timed out");
                    return null;
                }
            }
            finally
            {
                _stunTcs = null;
            }
        }

        private void InitializeP2PClient()
        {
            if (_p2pClient != null)
                return;

            _p2pClient = new UdpClient(0);
            _p2pClient.Client.ReceiveBufferSize = 65536;
            _p2pClient.Client.SendBufferSize = 65536;

            _p2pReceiveCts = new CancellationTokenSource();
            _p2pReceiveThread = new Thread(P2PReceivePackets)
            {
                IsBackground = true,
                Name = "P2PReceive"
            };
            _p2pReceiveThread.Start();

            var localPort = ((IPEndPoint)_p2pClient.Client.LocalEndPoint).Port;
            Debug.Print($"[P2P] Initialized P2P client on local port {localPort}");
        }

        private void P2PReceivePackets()
        {
            try
            {
                IPEndPoint remoteEndpoint = new IPEndPoint(IPAddress.Any, 0);
                while (!_p2pReceiveCts.Token.IsCancellationRequested)
                {
                    try
                    {
                        byte[] data = _p2pClient.Receive(ref remoteEndpoint);
                        ProcessP2PPacket(data, remoteEndpoint);
                    }
                    catch (SocketException ex) when (ex.SocketErrorCode == SocketError.TimedOut)
                    {
                        continue;
                    }
                    catch (SocketException ex) when (ex.SocketErrorCode == SocketError.Interrupted ||
                                                   ex.SocketErrorCode == SocketError.OperationAborted)
                    {
                        Debug.Print("[P2P] Socket closed, exiting receive thread");
                        break;
                    }
                    catch (SocketException ex)
                    {
                        Debug.Print($"[P2P] Socket error: {ex.SocketErrorCode} - {ex.Message}");
                    }
                }
            }
            catch (ObjectDisposedException)
            {
                Debug.Print("[P2P] Socket disposed");
            }
            catch (Exception ex)
            {
                Debug.Print($"[P2P] Unexpected error in receive thread: {ex.Message}");
            }
            finally
            {
                Debug.Print("[P2P] Receive thread exiting");
            }
        }

        private void ProcessP2PPacket(byte[] data, IPEndPoint remoteEndpoint)
        {
            try
            {
                if (data.Length == 40 && _stunTcs != null)
                {
                    var tunnel = FindTunnelByAddress(remoteEndpoint.Address);
                    if (tunnel != null)
                    {
                        var result = ParseStunResponse(data);
                        if (result != null)
                        {
                            Debug.Print($"[STUN] Got response from {tunnel.Name}: {result}");
                            _stunTcs?.TrySetResult(result);
                            return;
                        }
                    }
                }

                // we wil handle other P2P packets here in future


                Debug.Print($"[P2P] Received {data.Length} bytes from {remoteEndpoint}");
            }
            catch (Exception ex)
            {
                Debug.Print($"[P2P] Error processing packet from {remoteEndpoint}: {ex.Message}");
            }
        }

        private IPEndPoint ParseStunResponse(byte[] response)
        {
            if (response == null || response.Length != 40)
                return null;

            try
            {
                // Deobfuscate
                byte b0 = (byte)(response[0] ^ 0x20);
                byte b1 = (byte)(response[1] ^ 0x20);
                byte b2 = (byte)(response[2] ^ 0x20);
                byte b3 = (byte)(response[3] ^ 0x20);
                byte p0 = (byte)(response[4] ^ 0x20);
                byte p1 = (byte)(response[5] ^ 0x20);

                // IP address
                var ip = new IPAddress(new byte[] { b0, b1, b2, b3 });

                // port (unsigned big-endian)
                int port = (p0 << 8) | p1;

                return new IPEndPoint(ip, port);
            }
            catch (Exception ex)
            {
                Debug.Print($"[STUN] Failed to parse response: {ex.Message}");
                return null;
            }
        }

        private CnCNetTunnel FindTunnelByAddress(IPAddress address)
        {
            return _endpointToTunnel.Values.FirstOrDefault(tunnel =>
                IPAddress.Parse(tunnel.Address).Equals(address));
        }

        public IPEndPoint GetP2PLocalEndPoint()
        {
            return _p2pClient?.Client?.LocalEndPoint as IPEndPoint;
        }

        public void Dispose()
        {
            Dispose(true);
            GC.SuppressFinalize(this);
        }

        protected virtual void Dispose(bool disposing)
        {
            if (!disposing)
                return;

            lock (_initLock)
            {
                if (!IsInitialized)
                    return;

                _receiveCts?.Cancel();
                _udpClient?.Dispose();

                _endpointToTunnel.Clear();
                _handlers.Clear();
                _receiveCts?.Dispose();

                _udpClient = null;
                _receiveThread = null;
                _receiveCts = null;

                if (_p2pClient != null)
                {
                    _p2pReceiveCts?.Cancel();
                    _p2pClient?.Dispose();
                    _p2pReceiveCts?.Dispose();
                    _p2pClient = null;
                    _p2pReceiveThread = null;
                    _p2pReceiveCts = null;
                }
                Debug.Print("V3 tunnel communicator disposed.");
            }
        }
    }
}