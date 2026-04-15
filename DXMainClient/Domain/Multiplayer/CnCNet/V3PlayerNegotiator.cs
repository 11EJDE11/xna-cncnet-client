using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Rampastring.Tools;
using System.Text;

#nullable enable

namespace DTAClient.Domain.Multiplayer.CnCNet;

/// <summary>
/// Handles negotiating best tunnel with a single other player.
/// </summary>
public class V3PlayerNegotiator : IDisposable
{
    private readonly V3PlayerInfo _localPlayer; //our V3PlayerInfo ID
    private readonly V3PlayerInfo _remotePlayer;
    private readonly List<CnCNetTunnel> _tunnels; //list of tunnels to test with
    private readonly TunnelHandler _tunnelHandler;

    // If true, you send ping requests and measure latency.
    // If false, you reply to ping requests
    // This is set based on the ID (player1ID < player2ID)
    // As a negotiator runs for each other player, you may be a decider for
    // some, and a non-decider for others.
    private readonly bool _isDecider;
    private readonly CancellationTokenSource _negotiationCts = new();

    // Signals negotiation complete. Deciders = set when tunnel choice is made.
    // Non-deciders = set when tunnel choice is received from decider.
    private TaskCompletionSource<bool> _negotiationCompletionSource = new();

    // How long the non-decider will keep sending Connected packets overall.
    private const int NON_DECIDER_TOTAL_TIMEOUT_MS = 20000;

    // How long the decider will wait to receive a Ping Request from the non-decider.
    // If none are received in time, the tunnel is skipped.
    private static readonly TimeSpan DECIDER_CONNECTED_PHASE_TIMEOUT = TimeSpan.FromSeconds(15);

    // How long the decider will wait for pings to complete. If it takes this long, 
    // pick the best one from the results that have come in.
    private static readonly TimeSpan DECIDER_PING_PHASE_TIMEOUT = TimeSpan.FromSeconds(15);
    private const int PINGS_PER_TUNNEL = 5;
    private const int PING_TIMEOUT_MS = 2000; //consider it dropped, move on to the next ping
    private const int NON_DECIDER_CONNECTED_INTERVAL_MS = 500; //delay Connected packets a bit to avoid overloading

    // When the decider has picked a tunnel, they need to inform the non-decider.
    // As it's UDP and not guaranteed to make it, we need an acknowledgement.
    private const int TUNNEL_CHOICE_RETRY_INTERVAL_MS = 1000;
    private const int TUNNEL_CHOICE_MAX_RETRIES = 10;

    // Pick a tunnel early if we have 50% of the results. The remaining tunnels
    // will be high ping or timing out.
    private const double EARLY_SELECTION_THRESHOLD = 0.5;

    private TaskCompletionSource<bool> _tunnelAckReceived = new(); //true when tunnel choice ack'd

    public V3PlayerInfo RemotePlayer => _remotePlayer;

    public event EventHandler<TunnelChosenEventArgs>? NegotiationResult;
    public event EventHandler? NegotiationComplete;

    private static readonly byte[] SINGLE_BYTE_TRUE = [0x01];

    public V3PlayerNegotiator(V3PlayerInfo localPlayer, V3PlayerInfo remotePlayer, List<CnCNetTunnel> tunnels,
        TunnelHandler tunnelHandler)
    {
        _localPlayer = localPlayer;
        _remotePlayer = remotePlayer;
        _tunnels = tunnels;
        _tunnelHandler = tunnelHandler;
        _isDecider = localPlayer.Id < remotePlayer.Id;

        _remotePlayer.InitializeTunnelResults(tunnels);

        _tunnelHandler.RegisterV3PacketHandler(_localPlayer.Id, _remotePlayer.Id, OnPacketReceived);
    }

    public async Task<bool> NegotiateAsync()
    {
        try
        {
            Logger.Log($"V3TunnelNegotiator: Starting negotiation with player {_remotePlayer.Name} (ID: {_remotePlayer.Id}, Decider: {_isDecider})");

            _negotiationCompletionSource = new TaskCompletionSource<bool>();
            _tunnelAckReceived = new TaskCompletionSource<bool>();

            _tunnelHandler.SendRegistrationToTunnels(_localPlayer.Id, _tunnels);

            if (_isDecider)
                await PerformDeciderNegotiationAsync();
            else
                await PerformNonDeciderNegotiationAsync();

            PrintNegotiationResults();
            bool succeeded = _negotiationCompletionSource.Task.IsCompleted && _negotiationCompletionSource.Task.Result;
            _negotiationCompletionSource.TrySetResult(succeeded);
            NegotiationComplete?.Invoke(this, EventArgs.Empty);
            return succeeded;
        }
        catch (Exception ex)
        {
            Logger.Log($"V3TunnelNegotiator: Negotiation failed with {_remotePlayer.Name}: {ex.Message}");
            PrintNegotiationResults();
            _negotiationCompletionSource.TrySetResult(false);
            RaiseNegotiationResult(null, 0, ex.Message);
            NegotiationComplete?.Invoke(this, EventArgs.Empty);
            return false;
        }
    }

    private void RaiseNegotiationResult(CnCNetTunnel? tunnel, int negotiationPing = 0, string? failureReason = null)
    {
        var args = new TunnelChosenEventArgs
        {
            PlayerId = _remotePlayer.Id,
            PlayerName = _remotePlayer.Name,
            ChosenTunnel = tunnel,
            IsLocalDecision = _isDecider,
            FailureReason = failureReason,
            NegotiationPing = negotiationPing
        };
        NegotiationResult?.Invoke(this, args);
    }

    // Deciders wait for a Connected packet to be received. When received, they begin 
    // sending Ping Requests. When all tunnels are pinged/timed out, pick the best tunnel
    // and inform the other player.
    private async Task PerformDeciderNegotiationAsync()
    {
        var totalTunnels = _remotePlayer.TunnelResults.Count;
        var completedTunnels = 0;
        var selectionMade = false;
        var completionLock = new object();
        var selectionTcs = new TaskCompletionSource<bool>();

        foreach (var kvp in _remotePlayer.TunnelResults)
        {
            var tunnel = kvp.Key;
            var result = kvp.Value;
            _ = WaitForTunnelResultsAsync(result, () => {
                lock (completionLock)
                {
                    completedTunnels++;
                    if (!selectionMade && completedTunnels >= Math.Max(2, totalTunnels * EARLY_SELECTION_THRESHOLD))
                    {
                        selectionMade = true;
                        selectionTcs.TrySetResult(true);
                    }
                }
            });
        }

        // Wait for early selection or all completion
        await selectionTcs.Task;

        var bestTunnel = _remotePlayer.SelectBestTunnel();
        if (bestTunnel != null)
        {
            var bestResult = _remotePlayer.GetTunnelResult(bestTunnel);
            if (bestResult != null && bestResult.AverageRtt.HasValue)
            {
                int halvedPing = (int)Math.Round(bestResult.AverageRtt.Value / 2.0);
                bool ackReceived = await SendTunnelChoiceAsync(bestTunnel, halvedPing);

                if (ackReceived)
                    RaiseNegotiationResult(bestTunnel, halvedPing);
                else
                    RaiseNegotiationResult(null, 0, $"Failed to receive tunnel acknowledgment after {TUNNEL_CHOICE_MAX_RETRIES} attempts");
            }
        }
        else
        {
            Logger.Log("V3TunnelNegotiator: No tunnels had any ping responses");
            RaiseNegotiationResult(null, 0, "No viable tunnel found");
            throw new Exception("No viable tunnel");
        }
    }

    private static async Task WaitForTunnelResultsAsync(TunnelTestResult result, Action onComplete)
    {
        try
        {
            using var timeoutCts = new CancellationTokenSource();
            var connectedTask = result.ConnectedTcs.Task;
            var connectedTimeoutTask = Task.Delay(DECIDER_CONNECTED_PHASE_TIMEOUT, timeoutCts.Token);
            var completedTask = await Task.WhenAny(connectedTask, connectedTimeoutTask);

            if (completedTask == connectedTask)
            {
                // Connected phase completed successfully, cancel timeout
                timeoutCts.Cancel();

                // Now wait for pings
                using var pingTimeoutCts = new CancellationTokenSource();
                var pingsTask = result.PingsCompletedTcs.Task;
                var pingsTimeoutTask = Task.Delay(DECIDER_PING_PHASE_TIMEOUT, pingTimeoutCts.Token);
                var pingCompletedTask = await Task.WhenAny(pingsTask, pingsTimeoutTask);

                if (pingCompletedTask == pingsTask)
                    pingTimeoutCts.Cancel();
            }
        }
        catch (OperationCanceledException)
        {
        }
        finally
        {
            onComplete();
        }
    }

    // Non-deciders continuously send "Connected" packets to the other player
    // until they receive a Ping Request. Then they reply with Ping Responses
    // and await the tunnel choice from the Decider.
    private async Task PerformNonDeciderNegotiationAsync()
    {
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(_negotiationCts.Token);
        _ = SendConnectedPacketsAsync(cts.Token);

        try
        {
            //wait for tuennel choice or negotiation timeout
            var negotiationTimeout = Task.Delay(NON_DECIDER_TOTAL_TIMEOUT_MS, cts.Token);
            var completed = await Task.WhenAny(_negotiationCompletionSource.Task, negotiationTimeout);

            if (completed == negotiationTimeout && !_negotiationCompletionSource.Task.IsCompleted)
            {
                Logger.Log($"V3TunnelNegotiator: Timeout waiting for tunnel selection from {_remotePlayer.Name} after {NON_DECIDER_TOTAL_TIMEOUT_MS / 1000} seconds.");
                _negotiationCompletionSource.TrySetResult(false);
                cts.Cancel();

                // Notify the decider so it stops retrying TunnelChoice packets
                foreach (var tunnel in _tunnels)
                    _tunnelHandler.SendPacket(tunnel, _localPlayer.Id, _remotePlayer.Id, TunnelPacketType.NegotiationFailed, null);

                RaiseNegotiationResult(null, 0, "Timeout waiting for tunnel selection");
            }
        }
        catch (OperationCanceledException)
        {
            Logger.Log($"V3TunnelNegotiator: Cancelled negotiation with {_remotePlayer.Name}.");
        }
    }

    // Send Connected packets every 500ms to tunnels we haven't yet had a ping request from.
    private async Task SendConnectedPacketsAsync(CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested)
        {
            foreach (var tunnel in _tunnels)
            {
                var result = _remotePlayer.GetTunnelResult(tunnel);
                if (result == null || result.ConnectedTimedOut || result.PingRequestReceived)
                    continue;

                _tunnelHandler.SendPacket(tunnel, _localPlayer.Id, _remotePlayer.Id,
                    TunnelPacketType.Connected, null);

                if (!result.FirstConnectedSentTime.HasValue)
                    result.FirstConnectedSentTime = DateTime.UtcNow;
            }

            await Task.Delay(NON_DECIDER_CONNECTED_INTERVAL_MS, cancellationToken);
        }
    }

    //send a ping, wait for response or timeout, next ping...
    private async Task PerformPingsAsync(CnCNetTunnel tunnel, TunnelTestResult result)
    {
        for (int i = 0; i < PINGS_PER_TUNNEL && !_negotiationCts.Token.IsCancellationRequested; i++)
        {
            var ping = new PingResult { ID = i, SentTimeTicks = Stopwatch.GetTimestamp() };

            result.PingResults.Add(ping);

            var pingIdBytes = new byte[4];
            BinaryPrimitives.WriteInt32LittleEndian(pingIdBytes, i);

            _tunnelHandler.SendPacket(
                tunnel,
                _localPlayer.Id,
                _remotePlayer.Id,
                TunnelPacketType.PingRequest,
                pingIdBytes
            );

            // Wait for a ping response or timeout
            try
            {
                var timeoutTask = Task.Delay(PING_TIMEOUT_MS, _negotiationCts.Token);
                var completedTask = await Task.WhenAny(ping.CompletionSource.Task, timeoutTask);

                if (completedTask == timeoutTask)
                    Logger.Log($"V3TunnelNegotiator: Ping timeout: ID {i} to {_remotePlayer.Name} on {tunnel.Name}");
            }
            catch (OperationCanceledException)
            {
                Logger.Log($"V3TunnelNegotiator: Ping cancelled: ID {i} to {_remotePlayer.Name} on {tunnel.Name}");
                break;
            }
        }

        result.PingsCompletedTcs.TrySetResult(true);
    }

    private void OnPacketReceived(uint senderId, uint receiverId, TunnelPacketType packetType,
        byte[] payload, long receivedTime, CnCNetTunnel tunnel)
    {
        var result = _remotePlayer.GetTunnelResult(tunnel);
        if (result == null)
            return;

        switch (packetType)
        {
            case TunnelPacketType.Connected:
                //if we receive a connected packet, move on to the pinging phase.
                if (_isDecider && !result.ConnectedReceived)
                {
                    result.ConnectedReceived = true;
                    result.ConnectedTcs.TrySetResult(true);
                    _ = PerformPingsAsync(tunnel, result);
                }
                break;

            case TunnelPacketType.PingRequest:
                //if we receive a ping request, reply with a ping response that contains the ping ID.
                if (!_isDecider)
                {
                    var tunnelResult = _remotePlayer.GetTunnelResult(tunnel);
                    if (tunnelResult != null)
                        tunnelResult.PingRequestReceived = true;

                    _tunnelHandler.SendPacket(tunnel, _localPlayer.Id, _remotePlayer.Id,
                        TunnelPacketType.PingResponse, payload);
                }
                break;

            case TunnelPacketType.PingResponse:
                //if we receive a ping response, note down the received time and complete the ping.
                if (_isDecider && payload.Length >= 4)
                {
                    int id = BinaryPrimitives.ReadInt32LittleEndian(payload);
                    var ping = result.PingResults.FirstOrDefault(p => p.ID == id);
                    if (ping != null && !ping.ReceivedTimeTicks.HasValue)
                    {
                        ping.ReceivedTimeTicks = Stopwatch.GetTimestamp();
                        ping.CompletionSource.TrySetResult(true); //ping complete
                    }
                }
                break;

            case TunnelPacketType.TunnelChoice:
                if (!_isDecider)
                {
                    // The chosen tunnel is the one this packet came through
                    int ping = -1;
                    if (payload != null && payload.Length >= 4)
                        ping = BinaryPrimitives.ReadInt32LittleEndian(payload);

                    Logger.Log($"V3TunnelNegotiator: {_remotePlayer.Name} chose {tunnel.Name} (Ping: {ping}ms)");

                    _remotePlayer.Tunnel = tunnel;

                    _tunnelHandler.SendPacket(tunnel, _localPlayer.Id, _remotePlayer.Id,
                        TunnelPacketType.TunnelAck, SINGLE_BYTE_TRUE);

                    RaiseNegotiationResult(tunnel, ping);
                    _negotiationCompletionSource.TrySetResult(true);
                }
                break;

            case TunnelPacketType.TunnelAck:
                if (_isDecider)
                {
                    Logger.Log($"V3TunnelNegotiator: Received acknowledgment from {_remotePlayer.Name} for tunnel {tunnel.Name}");
                    _tunnelAckReceived.TrySetResult(true);
                }
                break;

            case TunnelPacketType.NegotiationFailed:
                Logger.Log($"V3TunnelNegotiator: Received failure notification from {_remotePlayer.Name}");
                _negotiationCompletionSource.TrySetResult(false);
                RaiseNegotiationResult(null, 0, "Remote player reported negotiation failure");
                break;
        }
    }

    // Informs the other player of the tunnel to use.
    // Returns true if an acknowledgment was received, false if all retries were exhausted.
    private async Task<bool> SendTunnelChoiceAsync(CnCNetTunnel tunnel, int ping)
    {
        Logger.Log($"V3TunnelNegotiator: Sending tunnel choice to {_remotePlayer.Name}: {tunnel.Name} (Ping: {ping}ms)");

        var pingBytes = new byte[4];
        BinaryPrimitives.WriteInt32LittleEndian(pingBytes, ping);

        for (int attempt = 0; attempt < TUNNEL_CHOICE_MAX_RETRIES; attempt++)
        {
            _tunnelHandler.SendPacket(tunnel, _localPlayer.Id, _remotePlayer.Id,
                TunnelPacketType.TunnelChoice, pingBytes);

            Logger.Log($"V3TunnelNegotiator: Attempt {attempt + 1} sent to {_remotePlayer.Name} via {tunnel.Name}");

            try
            {
                //wait for acknowledgment or timeout
                var timeoutTask = Task.Delay(TUNNEL_CHOICE_RETRY_INTERVAL_MS, _negotiationCts.Token);
                var completedTask = await Task.WhenAny(_tunnelAckReceived.Task, timeoutTask);

                if (completedTask == _tunnelAckReceived.Task)
                {
                    Logger.Log($"V3TunnelNegotiator: Acknowledgment received from {_remotePlayer.Name} for {tunnel.Name}");
                    return true;
                }
                Logger.Log($"V3TunnelNegotiator: No acknowledgment received, retrying... (attempt {attempt + 1}/{TUNNEL_CHOICE_MAX_RETRIES})");
            }
            catch (OperationCanceledException)
            {
                Logger.Log($"V3TunnelNegotiator: Cancelled while waiting for acknowledgment from {_remotePlayer.Name}");
                return false;
            }
        }

        Logger.Log($"V3TunnelNegotiator: Failed to receive tunnel acknowledgment from {_remotePlayer.Name} after {TUNNEL_CHOICE_MAX_RETRIES} goes");
        return false;
    }

    private void PrintNegotiationResults()
    {
        if (!_isDecider)
            return;

        var sb = new StringBuilder();

        sb.AppendLine($"=== Negotiation Results for {_remotePlayer.Name} (ID: {_remotePlayer.Id}) ===");

        foreach (var tunnel in _tunnels)
        {
            var result = _remotePlayer.GetTunnelResult(tunnel);
            if (result != null)
            {
                var successfulPings = result.PingResults.Count(p => p.RoundTripTime.HasValue);

                sb.AppendLine(
                    $"Player: {_remotePlayer.Name} | " +
                    $"Tunnel: {tunnel.Name} | " +
                    $"Avg RTT: {(result.AverageRtt.HasValue ? $"{result.AverageRtt.Value:F1}ms" : "N/A")} | " +
                    $"Real ping: {(tunnel.Ping.IsValid() ? $"{tunnel.Ping.Milliseconds:F1}ms" : "N/A")} | " +
                    $"Real ping*2: {(tunnel.Ping.IsValid() ? $"{tunnel.Ping.Milliseconds * 2:F1}ms" : "N/A")} | " +
                    $"Difference: {(tunnel.Ping.IsValid() && result.AverageRtt.HasValue ? $"{result.AverageRtt.Value - (tunnel.Ping.Milliseconds * 2):F1}ms" : "N/A")} | " +
                    $"Packet Loss: {result.PacketLoss:F1}% | " +
                    $"Pings: {successfulPings}/{result.PingResults.Count} | " +
                    $"Connected: {result.ConnectedReceived}"
                );
            }
        }

        var bestTunnel = _remotePlayer.SelectBestTunnel();
        if (bestTunnel != null)
        {
            var bestResult = _remotePlayer.GetTunnelResult(bestTunnel);
            if (bestResult != null)
            {
                var rttDisplay = bestResult.AverageRtt.HasValue ? $"{bestResult.AverageRtt.Value:F1}ms" : "N/A";
                sb.AppendLine($"BEST TUNNEL for {_remotePlayer.Name}: {bestTunnel.Name} " +
                    $"(RTT: {rttDisplay}, Loss: {bestResult.PacketLoss:F1}%)");
            }
        }
        else
        {
            sb.AppendLine($"NO VIABLE TUNNEL found for {_remotePlayer.Name}");
        }

        sb.AppendLine($"=== End Results for {_remotePlayer.Name} ===");

        Logger.Log(sb.ToString());
    }

    public void Dispose()
    {
        _negotiationCts.Dispose();
        _tunnelAckReceived.TrySetCanceled();
        _negotiationCompletionSource.TrySetCanceled();
        _tunnelHandler.UnregisterV3PacketHandler(_localPlayer.Id, _remotePlayer.Id);
    }
}

