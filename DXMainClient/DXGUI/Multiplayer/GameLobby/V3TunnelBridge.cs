using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;
using System.Diagnostics;
using System.Timers;
using DTAClient.Domain.Multiplayer.CnCNet;

namespace DTAClient.DXGUI.Multiplayer.GameLobby
{
    public enum CustomPacketType : byte
    {
        PingRequest = 0x01,
        PingResponse = 0x02
    }

    public class V3TunnelBridge
    {
        private readonly UdpClient _localServer;  // fake tunnel for the game
        private readonly Dictionary<uint, (UdpClient client, string ip, int port)> _tunnelClients; // connections to players' tunnels
        private readonly List<V3PlayerInfo> _otherPlayers;
        private readonly uint _localId;
        private readonly int _localPort;
        private readonly CancellationTokenSource _cts = new();
        private readonly System.Timers.Timer _keepAliveTimer;

        private IPEndPoint _gameEndpoint;

        private static readonly byte[] CUSTOM_PACKET_MAGIC = { 0x45, 0x4A, 0x45, 0x4A, 0x45, 0x4A };
        private const int KEEP_ALIVE_INTERVAL = 30000; // 30 secs

        public V3TunnelBridge(
            uint localId,
            int localPort,
            List<V3PlayerInfo> allPlayers,
            TunnelHandler tunnelHandler)
        {
            _localId = localId;
            _localPort = localPort;

            // act as a server on the local port - the game will connect to us
            _localServer = new UdpClient(new IPEndPoint(IPAddress.Loopback, _localPort));
            Debug.Print($"V3TunnelBridge: Local server created on {((IPEndPoint)_localServer.Client.LocalEndPoint).Address}:{((IPEndPoint)_localServer.Client.LocalEndPoint).Port}");

            _tunnelClients = new Dictionary<uint, (UdpClient client, string ip, int port)>();

            //create tunnel clients for all players
            foreach (var player in allPlayers)
            {
                var client = new UdpClient(0);
                client.Connect(player.IpAddress, player.Port); // probably not needed anymore

                var localEndPoint = (IPEndPoint)client.Client.LocalEndPoint;
                _tunnelClients[player.Id] = (client, player.IpAddress, player.Port);
                Debug.Print($"V3TunnelBridge: Player {player.Name} (ID={player.Id}) - Local: {localEndPoint.Address}:{localEndPoint.Port} -> Remote: {player.IpAddress}:{player.Port}");
            }

            // send registration packets for each tunnelClient
            foreach (var item in _tunnelClients)
            {
                SendRegistrationPacket(item.Value.client, item.Value.ip, item.Value.port);
            }

            _otherPlayers = allPlayers.Where(p => p.Id != _localId).ToList();

            _keepAliveTimer = new System.Timers.Timer(KEEP_ALIVE_INTERVAL);
            _keepAliveTimer.Elapsed += OnKeepAliveTimer;
            _keepAliveTimer.AutoReset = true;

            Debug.Print($"V3TunnelBridge: Local ID={_localId}, Local Server Port={_localPort}");
            Debug.Print($"V3TunnelBridge: Will forward to {_otherPlayers.Count} other players");
        }

        public void Start()
        {
            Debug.Print("=== V3TunnelBridge Connections ===");
            Debug.Print($"Local Server: 127.0.0.1:{_localPort}");
            Debug.Print("Tunnel Connections:");
            foreach (var item in _tunnelClients)
            {
                var localEndPoint = (IPEndPoint)item.Value.client.Client.LocalEndPoint;
                Debug.Print($"  Player {item.Key}: {localEndPoint.Address}:{localEndPoint.Port} -> {item.Value.ip}:{item.Value.port}");
            }
            Debug.Print("==========================================");

            Task.Run(() => LocalServerLoopAsync());

            //start a receive loop for each tunnelClient
            foreach (var item in _tunnelClients)
            {
                Task.Run(() => TunnelReceiveLoopAsync(item.Key, item.Value.client));
            }

            _keepAliveTimer.Start();
        }

        public void Stop()
        {
            _cts.Cancel();
            _keepAliveTimer?.Stop();
            _keepAliveTimer?.Dispose();
            _localServer?.Close();

            if (_tunnelClients != null)
            {
                foreach (var kvp in _tunnelClients)
                {
                    kvp.Value.client?.Close();
                }
                _tunnelClients.Clear();
            }
        }

        // Send reg packets to every tunnel every 30 sec
        private void OnKeepAliveTimer(object sender, ElapsedEventArgs e)
        {
            foreach (var item in _tunnelClients)
            {
                SendRegistrationPacket(item.Value.client, item.Value.ip, item.Value.port);
            }
            Debug.Print($"V3TunnelBridge: Sent keep-alive packets to {_tunnelClients.Count} tunnel servers");
        }

        private void SendRegistrationPacket(UdpClient client, string ip, int port)
        {
            byte[] registrationPacket = new byte[9];
            Array.Copy(BitConverter.GetBytes(_localId), 0, registrationPacket, 0, 4); // senderId = _localId
            Array.Copy(BitConverter.GetBytes(0u), 0, registrationPacket, 4, 4); // receiverId = 0
            registrationPacket[8] = 0xFF;   // junk byte

            try
            {
                client.Send(registrationPacket, registrationPacket.Length);
                Debug.Print($"V3TunnelBridge: Sent registration packet to {ip}:{port} from {_localId}");
            }
            catch (Exception ex)
            {
                Debug.Print($"V3TunnelBridge: Failed to send registration packet to {ip}:{port} - {ex.Message}");
            }
        }

        // Send a custom packet to a specific player
        public async Task SendCustomPacketAsync(uint targetPlayerId, CustomPacketType packetType, byte[] data = null)
        {
            if (!_tunnelClients.TryGetValue(targetPlayerId, out var tunnelInfo))
            {
                Debug.Print($"ERROR: No tunnel client available for player {targetPlayerId}");
                return;
            }

            data = data ?? new byte[0];

            //Create custom packet: [SenderID(4)][ReceiverID(4)][Magic(4)][PacketType(1)][Data(n)]
            byte[] packet = new byte[4 + 4 + CUSTOM_PACKET_MAGIC.Length + 1 + data.Length];
            int offset = 0;

            //sender ID
            Array.Copy(BitConverter.GetBytes(_localId), 0, packet, offset, 4);
            offset += 4;

            //receiver ID
            Array.Copy(BitConverter.GetBytes(targetPlayerId), 0, packet, offset, 4);
            offset += 4;

            //magic bytes
            Array.Copy(CUSTOM_PACKET_MAGIC, 0, packet, offset, CUSTOM_PACKET_MAGIC.Length);
            offset += CUSTOM_PACKET_MAGIC.Length;

            //packet type
            packet[offset] = (byte)packetType;
            offset += 1;

            //data
            if (data.Length > 0)
                Array.Copy(data, 0, packet, offset, data.Length);

            try
            {
                await tunnelInfo.client.SendAsync(packet, packet.Length);
                Debug.Print($"Sent custom packet type {packetType} to player {targetPlayerId}, size={packet.Length}");
            }
            catch (Exception ex)
            {
                Debug.Print($"Error sending custom packet to player {targetPlayerId}: {ex.Message}");
            }
        }

        // Check if packet is a custom packet based on magic bytes
        private bool IsCustomPacket(byte[] data)
        {
            if (data.Length < 8 + CUSTOM_PACKET_MAGIC.Length)
                return false;

            // Check magic bytes at offset 8 (after senderID and receiverID)
            for (int i = 0; i < CUSTOM_PACKET_MAGIC.Length; i++)
            {
                if (data[8 + i] != CUSTOM_PACKET_MAGIC[i])
                    return false;
            }

            return true;
        }

        private async Task ProcessCustomPacket(byte[] data, IPEndPoint remoteEndPoint)
        {
            if (data.Length < 8 + CUSTOM_PACKET_MAGIC.Length + 1)
            {
                Debug.Print("Custom packet too small");
                return;
            }

            uint senderId = BitConverter.ToUInt32(data, 0);
            uint receiverId = BitConverter.ToUInt32(data, 4);

            // Check if this packet is for us
            if (receiverId != _localId)
            {
                Debug.Print($"Custom packet not for us (our ID: {_localId}), but was for: {receiverId} - ignoring");
                return;
            }

            // Skip senderID(4) + receiverID(4) + magic(4) to get to packet type
            int offset = 8 + CUSTOM_PACKET_MAGIC.Length;

            CustomPacketType packetType = (CustomPacketType)data[offset];
            offset += 1;

            byte[] payload = new byte[data.Length - offset];
            Array.Copy(data, offset, payload, 0, payload.Length);

            Debug.Print($"Received custom packet type {packetType} from player {senderId}");

            switch (packetType)
            {
                case CustomPacketType.PingRequest:
                    if (payload.Length >= 4)
                    {

                    }
                    break;

                case CustomPacketType.PingResponse:
                    if (payload.Length >= 4)
                    {

                    }
                    break;

                default:
                    Debug.Print($"Unknown custom packet type {packetType} from player {senderId}");
                    break;
            }
        }

        //receives from game, forwards to tunnel(s)
        private async Task LocalServerLoopAsync()
        {
            try
            {
                Debug.Print($"V3TunnelBridge: Local server listening on 127.0.0.1:{_localPort}");

                while (!_cts.IsCancellationRequested)
                {
                    //receive data from the game
                    UdpReceiveResult result = await _localServer.ReceiveAsync();
                    byte[] gameData = result.Buffer;

                    //remember where the game is sending from
                    _gameEndpoint = result.RemoteEndPoint;

                    Debug.Print($"Received {gameData.Length} bytes from game at {_gameEndpoint}");

                    //send to the recipient through their respective tunnel
                    if (gameData.Length >= 8)
                    {
                        ushort receiverId = SwapBytes(BitConverter.ToUInt16(gameData, 2)); //[senderId][receiverId][payload]
                        var recipient = _otherPlayers.FirstOrDefault(p => p.PlayerGameId == receiverId);
                        if (recipient != null)
                            await SendWrappedPacketAsync(gameData, recipient);
                        else
                            Debug.Print($"V3TunnelBridge: No matching recipient found for receiverId={receiverId}");
                    }
                    else
                    {
                        Debug.Print("V3TunnelBridge: Received too-short packet from game");
                    }
                }
            }
            catch (ObjectDisposedException)
            {
                Debug.Print("V3TunnelBridge: Local server shutdown");
            }
            catch (Exception ex)
            {
                Debug.Print($"V3TunnelBridge local server error: {ex}");
            }
        }

        private static ushort SwapBytes(ushort val)
        {
            return (ushort)((val << 8) | (val >> 8));
        }

        //receives from tunnel, forwards to game
        private async Task TunnelReceiveLoopAsync(uint playerId, UdpClient client)
        {
            try
            {
                Debug.Print($"V3TunnelBridge: Listening for tunnel data from player {playerId}'s tunnel");

                while (!_cts.IsCancellationRequested)
                {
                    //receive data from tunnel
                    UdpReceiveResult result = await client.ReceiveAsync();
                    await ProcessReceivedPacket(result.Buffer, result.RemoteEndPoint);
                }
            }
            catch (ObjectDisposedException)
            {
                Debug.Print($"V3TunnelBridge: Tunnel client for player {playerId} shutdown");
            }
            catch (Exception ex)
            {
                Debug.Print($"V3TunnelBridge: Tunnel client error for player {playerId}: {ex}");
            }
        }

        // Process received packets from tunnels
        private async Task ProcessReceivedPacket(byte[] data, IPEndPoint remoteEndPoint)
        {
            if (IsCustomPacket(data))
            {
                await ProcessCustomPacket(data, remoteEndPoint);
                return;
            }

            if (data.Length <= 8)
            {
                Debug.Print("Ignoring too small packet.");
                return;
            }

            //extract V3 header
            uint senderId = BitConverter.ToUInt32(data, 0);
            uint receiverId = BitConverter.ToUInt32(data, 4);
            Debug.Print($"Received {data.Length} bytes from tunnel {remoteEndPoint} (V3 packet: sender={senderId}, receiver={receiverId})");

            //check if this packet is for us
            if (receiverId != _localId)
            {
                Debug.Print($"Packet not for us (our ID: {_localId}), but was for: {receiverId} - ignoring");
                return;
            }

            //skip the 8-byte V3 header
            byte[] payload = new byte[data.Length - 8];
            Array.Copy(data, 8, payload, 0, payload.Length);

            //forward data to game
            if (_gameEndpoint != null)
            {
                await _localServer.SendAsync(payload, payload.Length, _gameEndpoint);
                var localServerEndPoint = (IPEndPoint)_localServer.Client.LocalEndPoint;
                Debug.Print($"Forwarded {payload.Length} bytes from {localServerEndPoint.Address}:{localServerEndPoint.Port} to game at {_gameEndpoint}");
            }
            else
            {
                Debug.Print("Warning: No game endpoint captured yet");
            }
        }

        private async Task SendWrappedPacketAsync(byte[] data, V3PlayerInfo recipient)
        {
            //create V3 packet: [SenderID(4)][ReceiverID(4)][Payload(n)]
            byte[] wrapped = new byte[8 + data.Length];

            //write sender ID (local player)
            Array.Copy(BitConverter.GetBytes(_localId), 0, wrapped, 0, 4);

            //write receiver ID (target player)
            Array.Copy(BitConverter.GetBytes(recipient.Id), 0, wrapped, 4, 4);

            //write game data
            Array.Copy(data, 0, wrapped, 8, data.Length);

            try
            {
                if (_tunnelClients.TryGetValue(recipient.Id, out var tunnelInfo))
                {
                    await tunnelInfo.client.SendAsync(wrapped, wrapped.Length);
                    var localEndPoint = (IPEndPoint)tunnelInfo.client?.Client?.LocalEndPoint;
                    if(localEndPoint != null)
                        Debug.Print($"Sent V3 packet to player {recipient.Id}'s tunnel from {localEndPoint.Address}:{localEndPoint.Port} to {tunnelInfo.ip}:{tunnelInfo.port}, size={wrapped.Length}");
                }
                else
                {
                    Debug.Print($"ERROR: No tunnel client available for recipient {recipient.Id}");
                }
            }
            catch (Exception ex)
            {
                Debug.Print($"Send error for recipient {recipient.Id} - {ex.Message}");
            }
        }
    }
}