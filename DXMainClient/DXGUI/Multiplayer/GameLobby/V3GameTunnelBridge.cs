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
    public class V3GameTunnelBridge : IDisposable
    {
        private readonly UdpClient _localServer; // fake tunnel for the game
        private readonly List<V3PlayerInfo> _otherPlayers;
        private readonly uint _localId;
        private readonly int _localPort;
        private readonly CancellationTokenSource _cts = new();
        private readonly TunnelHandler _tunnelHandler;

        private readonly Dictionary<string, UdpClient> _tunnelClients = new();
        private readonly Thread _bridgeThread;
        private IPEndPoint _gameEndpoint;
        private volatile bool _isRunning = false;

        public V3GameTunnelBridge(
            uint localId,
            int localPort,
            List<V3PlayerInfo> allPlayers,
            TunnelHandler tunnelHandler)
        {
            _localId = localId;
            _localPort = localPort;
            _tunnelHandler = tunnelHandler;

            // act as a server on the local port - the game will connect to us
            _localServer = new UdpClient(new IPEndPoint(IPAddress.Loopback, _localPort));
            Debug.Print($"V3GameTunnelBridge: Local server created on {((IPEndPoint)_localServer.Client.LocalEndPoint).Address}:{((IPEndPoint)_localServer.Client.LocalEndPoint).Port}");

            _otherPlayers = allPlayers.Where(p => p.Id != _localId).ToList();

            foreach (var player in _otherPlayers)
            {
                if (string.IsNullOrEmpty(player.IpAddress) || player.IpAddress == IPAddress.Any.ToString() || player.Port == 0)
                {
                    Debug.Print($"V3GameTunnelBridge: WARNING - Player {player.Name} (ID={player.Id}) has no valid tunnel");
                    continue;
                }
            }

            Debug.Print($"V3GameTunnelBridge: Local ID={_localId}, Local Server Port={_localPort}");
            Debug.Print($"V3GameTunnelBridge: Will forward to {_otherPlayers.Count} other players");

            _bridgeThread = new Thread(BridgeWorker)
            {
                //todo priority?
                Name = "V3GameTunnelBridge",
                IsBackground = true
            };
        }

        public void Start()
        {
            if (_isRunning)
                return;

            Debug.Print("=== V3GameTunnelBridge Starting ===");
            Debug.Print($"Local Server: 127.0.0.1:{_localPort}");
            Debug.Print("Player mappings:");
            foreach (var player in _otherPlayers)
                Debug.Print($" Player {player.Name}: {player.IpAddress}:{player.Port}");
            Debug.Print("=============================================");

            InitializeTunnelClients();

            _isRunning = true;
            _bridgeThread.Start();

            Debug.Print("V3GameTunnelBridge: Started successfully in independent mode");
        }

        public void Stop()
        {
            if (!_isRunning)
                return;

            Debug.Print("V3GameTunnelBridge: Stopping...");

            _isRunning = false;
            _cts.Cancel();

            _localServer?.Close();

            if (_bridgeThread.IsAlive)
            {
                if (!_bridgeThread.Join(TimeSpan.FromSeconds(5)))
                    Debug.Print("V3GameTunnelBridge: Bridge thread did not stop.");
            }

            foreach (var client in _tunnelClients.Values)
            {
                client?.Close();
                client?.Dispose();
            }
            _tunnelClients.Clear();

            Debug.Print("V3GameTunnelBridge: Stopped");
        }

        private void InitializeTunnelClients()
        {
            var usedTunnels = _otherPlayers
                .Where(p => !string.IsNullOrEmpty(p.IpAddress))
                .GroupBy(p => new { p.IpAddress, p.Port })
                .ToList();

            foreach (var tunnelGroup in usedTunnels)
            {
                var tunnel = tunnelGroup.Key;
                try
                {
                    var existingConnection = _tunnelHandler.GetTunnelConnection(tunnel.IpAddress, tunnel.Port);
                    if (existingConnection != null)
                    {
                        var localEndPoint = (IPEndPoint)existingConnection.Client.Client.LocalEndPoint;
                        var client = new UdpClient(localEndPoint);
                        client.Connect(tunnel.IpAddress, tunnel.Port);

                        foreach (var player in tunnelGroup)
                        {
                            player.TunnelClient = client;
                        }

                        _tunnelClients[$"{tunnel.IpAddress}:{tunnel.Port}"] = client;
                    }
                }
                catch (Exception ex)
                {
                    Debug.Print($"V3GameTunnelBridge: Failed to init tunnel client for {tunnel}: {ex.Message}");
                }
            }
        }

        private void BridgeWorker()
        {
            Debug.Print("V3GameTunnelBridge: Bridge worker thread started");

            var tunnelReceiveTasks = new List<Task>();
            foreach (var kvp in _tunnelClients)
            {
                var tunnelKey = kvp.Key;
                var client = kvp.Value;
                var task = Task.Run(() => TunnelReceiveLoop(tunnelKey, client));
                tunnelReceiveTasks.Add(task);
            }

            var localServerTask = Task.Run(() => LocalServerReceiveLoop());

            try
            {
                var allTasks = new List<Task>(tunnelReceiveTasks) { localServerTask };
                Task.WaitAny(allTasks.ToArray(), _cts.Token);
            }
            catch (OperationCanceledException)
            {
                Debug.Print("V3GameTunnelBridge: Bridge worker cancelled");
            }
            catch (Exception ex)
            {
                Debug.Print($"V3GameTunnelBridge: Bridge worker error: {ex.Message}");
            }

            Debug.Print("V3GameTunnelBridge: Bridge worker thread stopped");
        }

        private void TunnelReceiveLoop(string tunnelKey, UdpClient client)
        {
            Debug.Print($"V3GameTunnelBridge: Starting tunnel receive loop for {tunnelKey}");

            try
            {
                IPEndPoint remoteEndPoint = new IPEndPoint(IPAddress.Any, 0);
                while (_isRunning && !_cts.Token.IsCancellationRequested)
                {
                    var result = client.Receive(ref remoteEndPoint);

                    if (result.Length <= 8)
                    {
                        Debug.Print($"V3GameTunnelBridge: Ignoring too small packet from tunnel {tunnelKey}");
                        continue;
                    }

                    // Skip the 8-byte V3 header
                    byte[] payload = new byte[result.Length - 8];
                    Array.Copy(result, 8, payload, 0, payload.Length);

                    // Forward data to game
                    if (_gameEndpoint != null)
                    {
                        _localServer.Send(payload, payload.Length, _gameEndpoint);
                        // Debug.Print($"V3GameTunnelBridge: Forwarded {payload.Length} bytes to game at {_gameEndpoint}");
                    }
                    else
                    {
                        Debug.Print("V3GameTunnelBridge: Warning - No game endpoint captured yet");
                    }
                }
            }
            catch (ObjectDisposedException)
            {
                Debug.Print($"V3GameTunnelBridge: Tunnel {tunnelKey} client disposed");
            }
            catch (SocketException ex) when (ex.SocketErrorCode == SocketError.Interrupted)
            {
                Debug.Print($"V3GameTunnelBridge: Tunnel {tunnelKey} receive interrupted");
            }
            catch (Exception ex)
            {
                Debug.Print($"V3GameTunnelBridge: Tunnel {tunnelKey} receive error: {ex.Message}");
            }

            Debug.Print($"V3GameTunnelBridge: Tunnel receive loop stopped for {tunnelKey}");
        }

        private void LocalServerReceiveLoop()
        {
            Debug.Print($"V3GameTunnelBridge: Local server listening on 127.0.0.1:{_localPort}");

            try
            {
                IPEndPoint remoteEndPoint = new IPEndPoint(IPAddress.Any, 0);
                while (_isRunning && !_cts.Token.IsCancellationRequested)
                {
                    // Receive data from the game
                    byte[] gameData = _localServer.Receive(ref remoteEndPoint);

                    // Remember where the game is sending from
                    _gameEndpoint = remoteEndPoint;

                    // Debug.Print($"V3GameTunnelBridge: Received {gameData.Length} bytes from game at {_gameEndpoint}");

                    // Send to the recipient through their respective tunnel
                    if (gameData.Length >= 8)
                    {
                        ushort receiverId = SwapBytes(BitConverter.ToUInt16(gameData, 2)); // [senderId][receiverId][payload]
                        var recipient = _otherPlayers.FirstOrDefault(p => p.PlayerGameId == receiverId);
                        if (recipient != null)
                            SendWrappedPacket(gameData, recipient);
                        else
                            Debug.Print($"V3GameTunnelBridge: No matching recipient found for receiverId={receiverId}");
                    }
                    else
                    {
                        Debug.Print("V3GameTunnelBridge: Received too-short packet from game");
                    }
                }
            }
            catch (ObjectDisposedException)
            {
                Debug.Print("V3GameTunnelBridge: Local server shutdown");
            }
            catch (SocketException ex) when (ex.SocketErrorCode == SocketError.Interrupted)
            {
                Debug.Print("V3GameTunnelBridge: Local server receive interrupted");
            }
            catch (Exception ex)
            {
                Debug.Print($"V3GameTunnelBridge: Local server receive error: {ex.Message}");
            }

            Debug.Print("V3GameTunnelBridge: Local server receive loop stopped");
        }

        private static ushort SwapBytes(ushort val) => (ushort)((val << 8) | (val >> 8));

        private void SendWrappedPacket(byte[] data, V3PlayerInfo recipient)
        {
            if (recipient.TunnelClient == null)
            {
                Debug.Print($"V3GameTunnelBridge: No tunnel client for player {recipient.Name}");
                return;
            }

            // Create V3 packet: [SenderID(4)][ReceiverID(4)][Payload(n)]
            byte[] wrapped = new byte[8 + data.Length];
            Array.Copy(BitConverter.GetBytes(_localId), 0, wrapped, 0, 4); //todo: store bytes
            Array.Copy(BitConverter.GetBytes(recipient.Id), 0, wrapped, 4, 4);
            Array.Copy(data, 0, wrapped, 8, data.Length);

            try
            {
                recipient.TunnelClient.Send(wrapped, wrapped.Length);
            }
            catch (Exception ex)
            {
                Debug.Print($"V3GameTunnelBridge: Send error for recipient {recipient.Name} - {ex.Message}");
            }
        }

        public void Dispose()
        {
            Stop();
            _localServer?.Dispose();
            _cts?.Dispose();
        }
    }
}