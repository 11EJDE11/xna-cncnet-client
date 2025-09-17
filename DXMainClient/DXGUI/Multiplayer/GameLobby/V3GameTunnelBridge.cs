using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Threading;
using System.Diagnostics;
using DTAClient.Domain.Multiplayer.CnCNet;

namespace DTAClient.DXGUI.Multiplayer.GameLobby
{
    public class V3GameTunnelBridge : IDisposable
    {
        private readonly uint _localId;
        private readonly int _localPort;
        private readonly List<V3PlayerInfo> _otherPlayers;
        private readonly TunnelHandler _tunnelHandler;
        private readonly Thread _bridgeThread;
        private readonly CancellationTokenSource _cts = new();
        private readonly UdpClient _localGameClient; // game will connect to this
        private IPEndPoint _gameEndpoint;
        private volatile bool _isRunning = false; 
        public bool IsRunning => _isRunning;

        public V3GameTunnelBridge(
            uint localId,
            int localPort,
            List<V3PlayerInfo> allPlayers,
            TunnelHandler tunnelHandler)
        {
            _localId = localId;
            _localPort = localPort;
            _tunnelHandler = tunnelHandler;
            _localGameClient = new UdpClient(new IPEndPoint(IPAddress.Loopback, _localPort));
            _otherPlayers = allPlayers.Where(p => p.Id != _localId).ToList();

            Debug.Print($"V3GameTunnelBridge: Local ID={_localId}, Local Port={_localPort}");
            Debug.Print($"V3GameTunnelBridge: Will forward to {_otherPlayers.Count} other players");

            _bridgeThread = new Thread(BridgeWorker)
            {
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
            {
                if (player.Tunnel != null)
                    Debug.Print($" Player {player.Name}: {player.Tunnel.Address}:{player.Tunnel.Port}");
            }
            Debug.Print("=============================================");

            _tunnelHandler.RegisterV3PacketHandler(_localId, 0, OnTunnelPacketReceived);

            _isRunning = true;
            _bridgeThread.Start();
            Debug.Print("V3GameTunnelBridge: Started successfully using TunnelHandler");
        }

        public void Stop()
        {
            if (!_isRunning)
                return;

            Debug.Print("V3GameTunnelBridge: Stopping...");

            _isRunning = false;
            _cts.Cancel();
            _tunnelHandler?.UnregisterV3PacketHandler(_localId, 0);
            _localGameClient?.Close();

            if (_bridgeThread.IsAlive)
            {
                if (!_bridgeThread.Join(TimeSpan.FromSeconds(5)))
                    Debug.Print("V3GameTunnelBridge: Bridge thread did not stop.");
            }

            Debug.Print("V3GameTunnelBridge: Stopped");
        }

        private void OnTunnelPacketReceived(uint senderId, uint receiverId,
            TunnelPacketType packetType, byte[] payload, long receivedTime, CnCNetTunnel tunnel)
        {
            var player = _otherPlayers.FirstOrDefault(p => p.Id == senderId && p.Tunnel == tunnel);
            if (player == null)
                return;

            if (_gameEndpoint != null)
            {
                try
                {
                    _localGameClient.Send(payload, payload.Length, _gameEndpoint);
                }
                catch (Exception ex)
                {
                    Debug.Print($"V3GameTunnelBridge: Error sending to game: {ex.Message}");
                }
            }
            else
            {
                Debug.Print("V3GameTunnelBridge: Warning - No game endpoint captured yet");
            }
        }

        private void BridgeWorker()
        {
            Debug.Print("V3GameTunnelBridge: Bridge worker thread started");
            Debug.Print($"V3GameTunnelBridge: Local server listening on 127.0.0.1:{_localPort}");

            try
            {
                IPEndPoint remoteEndPoint = new IPEndPoint(IPAddress.Any, 0);
                while (_isRunning && !_cts.Token.IsCancellationRequested)
                {
                    try
                    {
                        byte[] gameData = _localGameClient.Receive(ref remoteEndPoint);
                        _gameEndpoint = remoteEndPoint;

                        if (gameData.Length >= 8)
                        {
                            ushort receiverId = SwapBytes(BitConverter.ToUInt16(gameData, 2)); // [senderId][receiverId][payload]
                            var recipient = _otherPlayers.FirstOrDefault(p => p.PlayerGameId == receiverId);

                            if (recipient != null)
                                _tunnelHandler.SendPacket(recipient.Tunnel, _localId, recipient.Id,
                                    TunnelPacketType.GameData, gameData);
                            else
                                Debug.Print($"V3GameTunnelBridge: No matching recipient found for receiverId={receiverId}");
                        }
                        else
                        {
                            Debug.Print("V3GameTunnelBridge: Received too-short packet from game");
                        }
                    }
                    catch (SocketException ex) when (ex.SocketErrorCode == SocketError.TimedOut)
                    {
                        continue;
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

            Debug.Print("V3GameTunnelBridge: Bridge worker thread stopped");
        }

        private static ushort SwapBytes(ushort val) => (ushort)((val << 8) | (val >> 8));
        public void Dispose()
        {
            Stop();
            _localGameClient?.Close();
            _localGameClient?.Dispose();
            _cts?.Dispose();
        }
    }
}