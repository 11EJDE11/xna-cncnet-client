using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;
using System.Diagnostics;
using DTAClient.Domain.Multiplayer.CnCNet;

namespace DTAClient.DXGUI.Multiplayer.GameLobby
{
    public class V3TunnelBridge
    {
        private readonly UdpClient _localServer;  // fake tunnel for the game
        private readonly Dictionary<uint, (UdpClient client, string ip, int port)> _tunnelClients; // connections to players' tunnels
        private readonly List<V3PlayerInfo> _otherPlayers;
        private readonly uint _localId;
        private readonly int _localPort;
        private readonly CancellationTokenSource _cts = new();

        private IPEndPoint _gameEndpoint;

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
                var client = item.Value.client;

                byte[] registrationPacket = new byte[9];
                Array.Copy(BitConverter.GetBytes(_localId), 0, registrationPacket, 0, 4); // senderId = _localId
                Array.Copy(BitConverter.GetBytes(0u), 0, registrationPacket, 4, 4); // receiverId = 0
                registrationPacket[8] = 0xFF; //junk

                try
                {
                    client.Send(registrationPacket, registrationPacket.Length);
                    Debug.Print($"V3TunnelBridge: Sent registration packet to {item.Value.ip}:{item.Value.port} from {_localId}");
                }
                catch (Exception ex)
                {
                    Debug.Print($"V3TunnelBridge: Failed to send registration packet to {item.Value.ip}:{item.Value.port} - {ex.Message}");
                }
            }

            _otherPlayers = allPlayers.Where(p => p.Id != _localId).ToList();

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
        }

        public void Stop()
        {
            _cts.Cancel();
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

                    Debug.Print($"Received {gameData.Length} bytes from game at {_gameEndpoint} -> forwarding to {_otherPlayers.Count} players");

                    //send to each other player through their respective tunnel
                    foreach (var recipient in _otherPlayers)
                    {
                        await SendWrappedPacketAsync(gameData, recipient);
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
                    var localEndPoint = (IPEndPoint)tunnelInfo.client.Client.LocalEndPoint;
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