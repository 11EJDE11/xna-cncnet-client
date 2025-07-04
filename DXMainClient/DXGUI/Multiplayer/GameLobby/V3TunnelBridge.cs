using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;
using System.Diagnostics;

using ClientCore;
using ClientCore.CnCNet5;
using ClientGUI;
using DTAClient.Domain.Multiplayer;
using DTAClient.Domain;
using DTAClient.DXGUI.Generic;
using DTAClient.DXGUI.Multiplayer.CnCNet;
using DTAClient.DXGUI.Multiplayer.GameLobby.CommandHandlers;
using DTAClient.Online;
using DTAClient.Online.EventArguments;
using Microsoft.Xna.Framework;
using Rampastring.Tools;
using Rampastring.XNAUI;
using Rampastring.XNAUI.XNAControls;
using DTAClient.Domain.Multiplayer.CnCNet;
using ClientCore.Extensions;

namespace DTAClient.DXGUI.Multiplayer.GameLobby
{
    public class V3TunnelBridge
    {
        private readonly UdpClient _localServer;  // fake tunnel for the game
        private readonly UdpClient _tunnelClient; // communicates with real tunnel
        private readonly List<V3PlayerInfo> _otherPlayers;
        private readonly uint _localId;
        private readonly int _localPort;
        private readonly string _tunnelIp;
        private readonly int _tunnelPort;
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
            _tunnelIp = tunnelHandler.CurrentTunnel.Address;
            _tunnelPort = tunnelHandler.CurrentTunnel.Port;

            // act as a server on the local port - the game will connect to us
            _localServer = new UdpClient(new IPEndPoint(IPAddress.Loopback, _localPort));

            // create tunnel client for communication with real tunnel
            _tunnelClient = new UdpClient(0);

            _otherPlayers = allPlayers
                .Where(p => p.Id != _localId)
                .ToList();

            Debug.Print($"V3TunnelBridge: Local ID={_localId}, Local Server Port={_localPort}");
            Debug.Print($"V3TunnelBridge: Tunnel Client Port={((IPEndPoint)_tunnelClient.Client.LocalEndPoint).Port}");
            Debug.Print($"V3TunnelBridge: Will forward to {_otherPlayers.Count} other players via tunnel {_tunnelIp}:{_tunnelPort}");
        }

        public void Start()
        {
            Task.Run(() => LocalServerLoopAsync());
            Task.Run(() => TunnelReceiveLoopAsync());
        }

        public void Stop()
        {
            _cts.Cancel();
            _localServer?.Close();
            _tunnelClient?.Close();
        }

        //receives from game, forwards to tunnel
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

                    //send to each other player through the real tunnel
                    foreach (var recipient in _otherPlayers)
                    {
                        await SendWrappedPacketAsync(gameData, recipient);
                    }
                }
            }
            catch (Exception ex)
            {
                Debug.Print($"V3TunnelBridge local server error: {ex}");
            }
        }

        //receives from tunnel, forwards to game
        private async Task TunnelReceiveLoopAsync()
        {
            try
            {
                Debug.Print($"V3TunnelBridge: Listening for tunnel data");

                while (!_cts.IsCancellationRequested)
                {
                    //receive data from tunnel
                    UdpReceiveResult result = await _tunnelClient.ReceiveAsync();
                    byte[] data = result.Buffer;

                    Debug.Print($"Received {data.Length} bytes from tunnel {result.RemoteEndPoint}");

                    if (data.Length <= 8)
                    {
                        Debug.Print("Ignoring too small packet.");
                        continue;
                    }

                    //extract V3 header
                    uint senderId = BitConverter.ToUInt32(data, 0);
                    uint receiverId = BitConverter.ToUInt32(data, 4);

                    Debug.Print($"V3 packet: sender={senderId}, receiver={receiverId}");

                    //check if this packet is for us
                    if (receiverId != _localId)
                    {
                        Debug.Print($"Packet not for us (our ID: {_localId}), but was for: {receiverId} - ignoring");
                        continue;
                    }

                    //skip the 8-byte V3 header
                    byte[] payload = new byte[data.Length - 8];
                    Array.Copy(data, 8, payload, 0, payload.Length);

                    //forward data to game
                    if (_gameEndpoint != null)
                    {
                        await _localServer.SendAsync(payload, payload.Length, _gameEndpoint);
                        Debug.Print($"Forwarded {payload.Length} bytes to game at {_gameEndpoint}");
                    }
                    else
                    {
                        Debug.Print("Warning: No game endpoint captured yet");
                    }
                }
            }
            catch (ObjectDisposedException)
            {
                Debug.Print("V3TunnelBridge: Tunnel client shutdown");
            }
            catch (Exception ex)
            {
                Debug.Print($"V3TunnelBridge tunnel client error: {ex}");
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
                //send to tunnel server
                await _tunnelClient.SendAsync(wrapped, wrapped.Length, _tunnelIp, _tunnelPort);

                Debug.Print($"Sent V3 packet to tunnel: sender={_localId}, receiver={recipient.Id}, size={wrapped.Length}");
            }
            catch (Exception ex)
            {
                Debug.Print($"Send error to tunnel {_tunnelIp}:{_tunnelPort} for recipient {recipient.Id} - {ex.Message}");
            }
        }
    }
}