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

    public delegate void PacketHandler(uint senderId, uint receiverId,
        TunnelPacketType packetType, byte[] payload, long receivedTime, CnCNetTunnel tunnel);

    public class V3TunnelCommunicator : IDisposable
    {
        private static readonly byte[] MAGIC_BYTES = { 0x45, 0x4A, 0x45, 0x4A, 0x45, 0x4A }; //EJEJEJ

        private bool _initialized = false;
        private UdpClient _udpClient;
        private Thread _receiveThread;
        private CancellationTokenSource _receiveCts;
        private readonly ConcurrentDictionary<IPEndPoint, CnCNetTunnel> _endpointToTunnel = new();
        private readonly ConcurrentDictionary<(uint localId, uint remoteId), PacketHandler> _handlers = new();
        private readonly object _initLock = new object();

        public bool IsInitialized => _initialized;

        /// <summary>
        /// Initializes the communicator with the provided V3-compatible tunnels,
        /// sets up UDP socket, and starts the background receive thread.
        /// </summary>
        public void Initialize(List<CnCNetTunnel> tunnels)
        {
            lock (_initLock)
            {
                if (_initialized)
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
                _initialized = true;
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
            if (!_initialized)
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
            if (!_initialized || tunnel == null)
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
            try
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
            catch (Exception ex)
            {
                Debug.Print($"Failed to initialize V3 connection: {ex.Message}");
                throw;
            }
        }

        private void ProcessReceivedPacket(byte[] data, long receivedTime, CnCNetTunnel tunnel)
        {
            try
            {
                var parsed = ParsePacket(data.AsSpan());
                if (parsed.Payload.Length == 0 && !parsed.NegotiationType.HasValue)
                    return;

                PacketHandler? handler = null;

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
                if (!_initialized)
                    return;

                _initialized = false;
                _receiveCts?.Cancel();

                _udpClient?.Dispose();

                if (_receiveThread != null && _receiveThread.IsAlive)
                    if (!_receiveThread.Join(2000))
                        Debug.Print("V3 receive thread did not terminate gracefully");

                _endpointToTunnel.Clear();
                _handlers.Clear();
                _receiveCts?.Dispose();

                _udpClient = null;
                _receiveThread = null;
                _receiveCts = null;

                Debug.Print("V3 tunnel communicator disposed.");
            }
        }
    }
}