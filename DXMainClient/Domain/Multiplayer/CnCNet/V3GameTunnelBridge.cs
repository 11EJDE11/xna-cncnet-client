using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Runtime.InteropServices;
using System.Threading;
using System.Buffers.Binary;
using Rampastring.Tools;

#nullable enable

namespace DTAClient.Domain.Multiplayer.CnCNet;

/// <summary>
/// Bridges UDP traffic between the local game and remote players
/// using V3 tunnels
/// </summary>
public class V3GameTunnelBridge
{
    private readonly uint _localId;
    private readonly int _localPort;
    private readonly List<V3PlayerInfo> _otherPlayers;
    private readonly TunnelHandler _tunnelHandler;
    private readonly Thread _bridgeThread;
    private readonly UdpClient _localGameClient; // game will connect to this
    private volatile IPEndPoint? _gameEndpoint;
    private volatile bool _isRunning = false;
    private bool _loggedTransientReceiveError;
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
        _localGameClient.Client.ReceiveTimeout = 500;
        V3TunnelCommunicator.DisableIcmpPortUnreachableExceptions(_localGameClient.Client);
        _otherPlayers = allPlayers.Where(p => p.Id != _localId).ToList();

        Logger.Log($"V3GameTunnelBridge: Local ID={_localId}, Local Port={_localPort}");
        Logger.Log($"V3GameTunnelBridge: Will forward to {_otherPlayers.Count} other players");

        _bridgeThread = new Thread(BridgeWorker)
        {
            Name = "CnCNetV3GameTunnelBridge",
            IsBackground = true
        };
    }

    /// <summary>
    /// Starts the game tunnel bridge, registers tunnel packet handler, launches
    /// the worker thread to forward game traffic between the game and other players.
    /// </summary>
    public void Start()
    {
        if (_isRunning)
            return;

        Logger.Log("=== V3GameTunnelBridge Starting ===");

        var localEP = (IPEndPoint)_localGameClient.Client.LocalEndPoint!;
        Logger.Log($"Local Server: {localEP}");

        Logger.Log("Player mappings:");
        foreach (var player in _otherPlayers)
        {
            if (player.Tunnel != null)
                Logger.Log($" Player {player.Name}: {player.Tunnel.Address}:{player.Tunnel.Port}");
        }
        Logger.Log("=============================================");

        _tunnelHandler.RegisterV3PacketHandler(_localId, 0, OnTunnelPacketReceived);

        _isRunning = true;
        _bridgeThread.Start();
        Logger.Log("V3GameTunnelBridge: Started successfully");
    }

    /// <summary>
    /// Stops the game tunnel bridge, unregisters packet handlers,
    /// and closes the local/game UDP socket.
    /// </summary>
    public void Stop()
    {
        if (!_isRunning)
            return;

        _isRunning = false;
        _localGameClient?.Close();
        _tunnelHandler.UnregisterV3PacketHandler(_localId, 0);
        if (Thread.CurrentThread != _bridgeThread)
            _bridgeThread.Join(1000);
        CleanupP2PRoutes();

        Logger.Log("V3GameTunnelBridge: Stopped");
    }

    private void CleanupP2PRoutes()
    {
        foreach (var player in _otherPlayers)
        {
            if (player.Tunnel is P2PTunnel)
                _tunnelHandler.CleanupP2PPair(_localId, player.Id);
        }
    }

    /// <summary>
    /// Handles packets received from remote players via the tunnels.
    /// Forwards the received payload to the locally running game once its endpoint is known.
    /// </summary>
    /// <param name="senderId">The ID of the player who sent the packet.</param>
    /// <param name="receiverId">The ID of the recipient player.</param>
    /// <param name="packetType">The type of received tunnel packet.</param>
    /// <param name="payload">The raw data payload to forward to the game.</param>
    /// <param name="receivedTime">The timestamp when the packet was received.</param>
    /// <param name="tunnel">The tunnel through which the packet arrived.</param>
    private void OnTunnelPacketReceived(uint senderId, uint receiverId,
        TunnelPacketType packetType, ReadOnlyMemory<byte> payload, long receivedTime, CnCNetTunnel tunnel)
    {
        var player = _otherPlayers.FirstOrDefault(p => p.Id == senderId && p.Tunnel == tunnel);
        if (player == null)
            return;

        if (_gameEndpoint != null)
        {
            try
            {
                // Forward straight from the received buffer to avoid copying every game packet.
                if (MemoryMarshal.TryGetArray(payload, out ArraySegment<byte> segment))
                    _localGameClient.Client.SendTo(segment.Array!, segment.Offset, segment.Count, SocketFlags.None, _gameEndpoint);
                else
                    _localGameClient.Send(payload.ToArray(), payload.Length, _gameEndpoint);
            }
            catch (Exception ex)
            {
                Logger.Log($"V3GameTunnelBridge: Error sending to game: {ex.Message}");
            }
        }
    }

    /// <summary>
    /// The background worker that receives data from the local game client
    /// and forwards it through the appropriate tunnel to remote players.
    /// Also captures the game's UDP endpoint once the first packet is received.
    /// </summary>
    private void BridgeWorker()
    {
        try
        {
            IPEndPoint remoteEndPoint = new(IPAddress.Any, 0);
            while (_isRunning)
            {
                try
                {
                    if (_localGameClient.Client.Poll(500_000, SelectMode.SelectRead)) // 500ms
                    {
                        byte[] gameData = _localGameClient.Receive(ref remoteEndPoint);
                        _gameEndpoint = remoteEndPoint;

                        if (gameData.Length < 4)
                        {
                            Logger.Log($"V3GameTunnelBridge: Ignoring too-short game packet (length={gameData.Length})");
                            continue;
                        }

                        ushort receiverId = BinaryPrimitives.ReadUInt16BigEndian(gameData.AsSpan(2));
                        var recipient = _otherPlayers.FirstOrDefault(p => p.PlayerGameId == receiverId);

                        if (recipient != null)
                        {
                            if (recipient.Tunnel == null)
                            {
                                Logger.Log($"V3GameTunnelBridge: Cannot send to {recipient.Name} - no tunnel assigned");
                                continue;
                            }

                            _tunnelHandler.SendPacket(recipient.Tunnel, _localId, recipient.Id,
                                TunnelPacketType.GameData, gameData);
                        }
                        else
                        {
                            Logger.Log($"V3GameTunnelBridge: No matching recipient found for receiverId={receiverId}");
                        }
                    }
                }
                catch (SocketException ex) when (ex.SocketErrorCode == SocketError.TimedOut)
                {
                    continue;
                }
                catch (SocketException ex) when (ex.SocketErrorCode == SocketError.Interrupted ||
                                                 ex.SocketErrorCode == SocketError.OperationAborted)
                {
                    Logger.Log("V3GameTunnelBridge: Local server socket closed, exiting");
                    break;
                }
                catch (SocketException ex)
                {
                    // Notably ConnectionReset on Windows (ICMP port-unreachable from a send to the
                    // game's endpoint). The bridge must outlive transient socket errors — exiting
                    // here would silently disconnect the player for the rest of the match.
                    if (!_loggedTransientReceiveError)
                    {
                        _loggedTransientReceiveError = true;
                        Logger.Log($"V3GameTunnelBridge: Transient receive error, continuing: {ex.SocketErrorCode} - {ex.Message}");
                    }
                }
            }
        }
        catch (ObjectDisposedException)
        {
            Logger.Log("V3GameTunnelBridge: Local server shutdown");
        }
        catch (Exception ex)
        {
            Logger.Log($"V3GameTunnelBridge: Local server receive error: {ex.Message}");
        }

        Logger.Log("V3GameTunnelBridge: Bridge worker thread stopped");
    }
}
