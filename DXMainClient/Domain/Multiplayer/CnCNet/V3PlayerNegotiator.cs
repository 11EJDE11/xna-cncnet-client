using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Net;
using System.Net.Sockets;
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
    // Tunnels to test with. Mutated when the P2P upgrade round adds direct paths, while other
    // tasks enumerate it (Connected sends, result printing), so all access goes through
    // _tunnelsLock; enumerators take a snapshot via TunnelsSnapshot().
    private readonly List<CnCNetTunnel> _tunnels; //list of tunnels to test with
    private readonly object _tunnelsLock = new();
    private readonly TunnelHandler _tunnelHandler;

    /// <summary>
    /// If true, you send ping requests and measure latency.
    /// If false, you reply to ping requests.
    ///
    /// This is set based on the ID (player1ID < player2ID).
    /// As a negotiator runs for each other player, you may be a decider for
    /// some, and a non-decider for others.
    /// </summary>
    private readonly bool _isDecider;
    private readonly bool _p2pEnabled;
    private readonly TaskCompletionSource<IReadOnlyList<IPEndPoint>?> _p2pPeerEndpointTcs = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly CancellationTokenSource _negotiationCts = new();
    // Cached because Dispose() disposes _negotiationCts, and fire-and-forget tasks
    // (e.g. PerformPingsAsync) may still be running; reading Token off a disposed
    // CTS throws, while a cached token stays safe to poll after cancellation.
    private readonly CancellationToken _negotiationToken;
    private int _disposeState;
    private int _completionRaised;

    // Signals negotiation complete. Deciders = set when tunnel choice is made.
    // Non-deciders = set when tunnel choice is received from decider.
    // volatile: reassigned for the P2P upgrade round on the negotiation task while the
    // communicator receive thread reads it in OnPacketReceived, so both must observe the
    // latest instance.
    private volatile TaskCompletionSource<bool> _negotiationCompletionSource = new(TaskCreationOptions.RunContinuationsAsynchronously);

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

    // P2P paths respond in ~1-3ms on a LAN and up to ~150ms across networks. Use a tighter
    // ping budget than relay negotiation so a doomed candidate (e.g. the reflexive address
    // between same-NAT peers, which needs NAT hairpinning) doesn't stall the upgrade decision.
    private const int P2P_PINGS_PER_TUNNEL = 4;
    private const int P2P_PING_TIMEOUT_MS = 1000;
    private const int NON_DECIDER_CONNECTED_INTERVAL_MS = 500; //delay Connected packets a bit to avoid overloading

    // P2P upgrade round: how long to wait for the peer's candidate addresses, how long the
    // non-decider waits for the upgrade tunnel choice, and how long the decider waits for the
    // peer to start punching the direct paths before falling back to the relay.
    private const int P2P_CANDIDATE_EXCHANGE_TIMEOUT_MS = 3000;
    private const int P2P_UPGRADE_NONDECIDER_TIMEOUT_MS = 10000;
    private static readonly TimeSpan P2P_UPGRADE_CONNECTED_TIMEOUT = TimeSpan.FromSeconds(3);

    // When the decider has picked a tunnel, they need to inform the non-decider.
    // As it's UDP and not guaranteed to make it, we need an acknowledgement.
    private const int TUNNEL_CHOICE_RETRY_INTERVAL_MS = 1000;
    private const int TUNNEL_CHOICE_MAX_RETRIES = 10;

    // Pick a tunnel early if we have 50% of the results. The remaining tunnels
    // will be high ping or timing out.
    private const double EARLY_SELECTION_THRESHOLD = 0.5;

    // volatile: reassigned for the P2P upgrade round (see _negotiationCompletionSource) and
    // read by the receive thread when a TunnelAck arrives.
    private volatile TaskCompletionSource<bool> _tunnelAckReceived = new(TaskCreationOptions.RunContinuationsAsynchronously); //true when tunnel choice ack'd

    // The tunnel the decider's outstanding TunnelChoice was sent through. An ack only counts
    // if it arrives via the same path: TunnelAck doesn't identify which choice it acknowledges,
    // so a late ack for an abandoned direct-path choice must not be mistaken for an ack of the
    // relay fallback (or vice versa). The non-decider always acks through the tunnel the choice
    // arrived on, so a matching ack implies the peer saw this specific choice.
    private volatile CnCNetTunnel? _pendingChoiceTunnel;
    private bool _loggedIgnoredFailureNotification;

    public V3PlayerInfo RemotePlayer => _remotePlayer;

    public event EventHandler<TunnelChosenEventArgs>? NegotiationResult;
    public event EventHandler? NegotiationComplete;

    public V3PlayerNegotiator(V3PlayerInfo localPlayer, V3PlayerInfo remotePlayer, List<CnCNetTunnel> tunnels,
        TunnelHandler tunnelHandler, bool p2pEnabled = false)
    {
        _localPlayer = localPlayer;
        _remotePlayer = remotePlayer;
        _tunnels = new List<CnCNetTunnel>(tunnels);
        _tunnelHandler = tunnelHandler;
        _p2pEnabled = p2pEnabled;
        _negotiationToken = _negotiationCts.Token;
        // The decider drives tunnel selection; the other peer waits for its choice.
        // Use the ID ordering, but fall back to player name ordering if the IDs
        // collide so exactly one side still becomes decider (otherwise both peers
        // would take the non-decider role and negotiation would deadlock).
        _isDecider = localPlayer.Id != remotePlayer.Id
            ? localPlayer.Id < remotePlayer.Id
            : string.CompareOrdinal(localPlayer.Name, remotePlayer.Name) < 0;

        if (localPlayer.Id == remotePlayer.Id)
            Logger.Log($"V3PlayerNegotiator: WARNING - player ID collision between {localPlayer.Name} and {remotePlayer.Name} (ID: {localPlayer.Id}). Falling back to name ordering to pick the decider.");

        _remotePlayer.InitializeTunnelResults(tunnels);

        _tunnelHandler.RegisterV3PacketHandler(_localPlayer.Id, _remotePlayer.Id, OnPacketReceived);
    }

    // A snapshot of the current tunnel set, safe to enumerate without holding the lock.
    private List<CnCNetTunnel> TunnelsSnapshot()
    {
        lock (_tunnelsLock)
            return new List<CnCNetTunnel>(_tunnels);
    }

    public async Task<bool> NegotiateAsync()
    {
        try
        {
            Logger.Log($"V3PlayerNegotiator: Starting negotiation with player {_remotePlayer.Name} (ID: {_remotePlayer.Id}, Decider: {_isDecider})");

            _negotiationCompletionSource = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            _tunnelAckReceived = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);

            _tunnelHandler.SendRegistrationToTunnels(_localPlayer.Id, TunnelsSnapshot());

            if (_isDecider)
                await PerformDeciderNegotiationAsync();
            else
                await PerformNonDeciderNegotiationAsync();

            bool negotiationSucceeded = await _negotiationCompletionSource.Task;

            // P2P upgrade round: now that a relay tunnel is agreed (and can carry the exchange),
            // offer direct candidate addresses through it and re-run the same negotiation over
            // the direct paths, which may now win.
            if (negotiationSucceeded && _p2pEnabled && _remotePlayer.P2PEnabled && _remotePlayer.Tunnel != null)
            {
                try
                {
                    await PerformP2PUpgradeRoundAsync(_remotePlayer.Tunnel);
                }
                catch (OperationCanceledException) { }
                catch (Exception ex)
                {
                    Logger.Log($"V3PlayerNegotiator: P2P upgrade error with {_remotePlayer.Name}: {ex.Message}");
                }
            }

            PrintNegotiationResults();

            // Skip the completion event if we were disposed mid-negotiation (the upgrade round
            // swallows the cancellation) — whoever cancelled has already replaced or removed
            // this negotiator, and the event could clobber the replacement's state.
            if (!_negotiationToken.IsCancellationRequested)
                RaiseNegotiationComplete();

            return negotiationSucceeded;
        }
        catch (Exception ex)
        {
            Logger.Log($"V3PlayerNegotiator: Negotiation failed with {_remotePlayer.Name}: {ex.Message}");
            PrintNegotiationResults();
            _negotiationCompletionSource.TrySetResult(false);

            // A cancellation means we were disposed (player left, renegotiation restart,
            // lobby teardown). Raising failure events for it would show spurious errors and
            // could clobber the state of a freshly restarted negotiation for this player.
            if (!_negotiationToken.IsCancellationRequested)
            {
                RaiseNegotiationResult(null, 0, ex.Message);
                RaiseNegotiationComplete();
            }

            return false;
        }
    }

    private void RaiseNegotiationResult(CnCNetTunnel? tunnel, int negotiationPing = 0, string? failureReason = null,
        bool isRelayFallback = false)
    {
        var args = new TunnelChosenEventArgs
        {
            PlayerId = _remotePlayer.Id,
            PlayerName = _remotePlayer.Name,
            ChosenTunnel = tunnel,
            IsLocalDecision = _isDecider,
            FailureReason = failureReason,
            NegotiationPing = negotiationPing,
            IsRelayFallback = isRelayFallback
        };
        NegotiationResult?.Invoke(this, args);
    }

    // Deciders wait for a Connected packet to be received. When received, they begin 
    // sending Ping Requests. When all tunnels are pinged/timed out, pick the best tunnel
    // and inform the other player.
    // <paramref name="tunnelsToAwait"/> limits which tunnels we wait for results on (defaults to
    // all). The P2P upgrade round passes just the direct paths so the already-completed relay
    // results don't trip the early-selection threshold; SelectBestTunnel still picks the global
    // best across relay and direct, so the choice is sent through whichever tunnel wins.
    private async Task PerformDeciderNegotiationAsync(
        IReadOnlyCollection<CnCNetTunnel>? tunnelsToAwait = null,
        TimeSpan? connectedTimeout = null,
        bool raiseFailureOnNoAck = true)
    {
        var awaitTunnels = tunnelsToAwait ?? _remotePlayer.TunnelResults.Keys.ToList();
        int totalTunnels = awaitTunnels.Count;
        if (totalTunnels == 0)
        {
            Logger.Log($"V3PlayerNegotiator: No tunnels available for decider negotiation with {_remotePlayer.Name}");
            _negotiationCompletionSource.TrySetResult(false);
            RaiseNegotiationResult(null, 0, "No tunnels available");
            return;
        }

        int completedTunnels = 0;
        bool selectionMade = false;
        var completionLock = new object();
        var selectionTcs = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);

        foreach (var tunnel in awaitTunnels)
        {
            var result = _remotePlayer.GetTunnelResult(tunnel);
            if (result == null)
                continue;

            _ = WaitForTunnelResultsAsync(result, _negotiationToken,
                connectedTimeout ?? DECIDER_CONNECTED_PHASE_TIMEOUT, () => {
                lock (completionLock)
                {
                    completedTunnels++;
                    if (!selectionMade && (completedTunnels >= totalTunnels ||
                        completedTunnels >= Math.Max(1, totalTunnels * EARLY_SELECTION_THRESHOLD)))
                    {
                        selectionMade = true;
                        selectionTcs.TrySetResult(true);
                    }
                }
            }, $"{_remotePlayer.Name} via {tunnel.Name}");
        }

        // Wait for early selection or all completion
        await selectionTcs.Task;

        var bestTunnel = _remotePlayer.SelectBestTunnel();
        if (bestTunnel != null)
        {
            var bestResult = _remotePlayer.GetTunnelResult(bestTunnel);
            if (bestResult != null && bestResult.AverageRtt.HasValue)
            {
                // Report the full round-trip time between the two players. This is the value
                // shown as the pair's ping, and the ping-tier thresholds (icons, status panel)
                // are calibrated for RTTs — halving it (as an approximation of a V2-style
                // per-leg tunnel ping) made pair pings look better than they are.
                int negotiatedPing = (int)Math.Round(bestResult.AverageRtt.Value);
                double packetLoss = bestResult.PacketLoss;
                _remotePlayer.NegotiatedPacketLoss = packetLoss;
                bool acknowledged = await SendTunnelChoiceAsync(bestTunnel, negotiatedPing, packetLoss);
                if (!acknowledged)
                {
                    // Distinguish exhausted ack retries (completion source untouched) from a
                    // remote NegotiationFailed / cancellation, which already raised a result.
                    bool alreadySignaled = _negotiationCompletionSource.Task.IsCompleted;
                    _negotiationCompletionSource.TrySetResult(false);

                    if (raiseFailureOnNoAck && !alreadySignaled && !_negotiationToken.IsCancellationRequested)
                        RaiseNegotiationResult(null, 0, $"No acknowledgment of tunnel choice after {TUNNEL_CHOICE_MAX_RETRIES} attempts");

                    return;
                }

                _negotiationCompletionSource.TrySetResult(true);
                RaiseNegotiationResult(bestTunnel, negotiatedPing);
            }
        }
        else
        {
            Logger.Log("V3PlayerNegotiator: No tunnels had any ping responses");
            _negotiationCompletionSource.TrySetResult(false);
            RaiseNegotiationResult(null, 0, "No viable tunnel found");
        }
    }

    private static async Task WaitForTunnelResultsAsync(TunnelTestResult result, CancellationToken cancellationToken,
        TimeSpan connectedTimeout, Action onComplete, string tunnelDescription = "")
    {
        try
        {
            // Link the phase timeouts to the negotiation token so they don't keep running
            // for up to 30s after the negotiator has been disposed/cancelled.
            using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            var connectedTask = result.ConnectedTcs.Task;
            var connectedTimeoutTask = Task.Delay(connectedTimeout, timeoutCts.Token);
            var completedTask = await Task.WhenAny(connectedTask, connectedTimeoutTask);

            if (completedTask == connectedTask)
            {
                // Connected phase completed successfully, cancel timeout
                timeoutCts.Cancel();

                // Now wait for pings
                using var pingTimeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
                var pingsTask = result.PingsCompletedTcs.Task;
                var pingsTimeoutTask = Task.Delay(DECIDER_PING_PHASE_TIMEOUT, pingTimeoutCts.Token);
                var pingCompletedTask = await Task.WhenAny(pingsTask, pingsTimeoutTask);

                if (pingCompletedTask == pingsTask)
                    pingTimeoutCts.Cancel();
            }
            else if (tunnelDescription.Length > 0)
            {
                Logger.Log($"V3PlayerNegotiator: No Connected from {tunnelDescription} within {connectedTimeout.TotalSeconds:F0}s, skipping tunnel");
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
    // In the P2P upgrade round (<paramref name="isUpgradeRound"/>) a timeout is benign: the relay
    // tunnel from round 1 is already agreed, so we just keep it rather than reporting a failure.
    private async Task PerformNonDeciderNegotiationAsync(
        bool isUpgradeRound = false, int totalTimeoutMs = NON_DECIDER_TOTAL_TIMEOUT_MS)
    {
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(_negotiationToken);
        Task connectedPacketsTask = SendConnectedPacketsAsync(cts.Token);

        try
        {
            //wait for tunnel choice or negotiation timeout
            var negotiationTimeout = Task.Delay(totalTimeoutMs, cts.Token);
            var completed = await Task.WhenAny(_negotiationCompletionSource.Task, negotiationTimeout);

            if (completed == negotiationTimeout && !_negotiationCompletionSource.Task.IsCompleted)
            {
                if (isUpgradeRound)
                {
                    Logger.Log($"V3PlayerNegotiator: No P2P upgrade choice from {_remotePlayer.Name}; keeping relay {_remotePlayer.Tunnel?.Name}");
                    _negotiationCompletionSource.TrySetResult(true);
                    cts.Cancel();
                    return;
                }

                Logger.Log($"V3PlayerNegotiator: Timeout waiting for tunnel selection from {_remotePlayer.Name} after {totalTimeoutMs / 1000} seconds.");
                _negotiationCompletionSource.TrySetResult(false);
                cts.Cancel();

                // Notify the decider so it stops retrying TunnelChoice packets
                foreach (var tunnel in TunnelsSnapshot())
                    _tunnelHandler.SendPacket(tunnel, _localPlayer.Id, _remotePlayer.Id, TunnelPacketType.NegotiationFailed, null);

                RaiseNegotiationResult(null, 0, "Timeout waiting for tunnel selection");
            }
        }
        catch (OperationCanceledException)
        {
            Logger.Log($"V3PlayerNegotiator: Cancelled negotiation with {_remotePlayer.Name}.");
        }
        finally
        {
            cts.Cancel();

            try
            {
                await connectedPacketsTask.ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
            }
        }
    }

    // Send Connected packets every 500ms to tunnels we haven't yet had a ping request from.
    private async Task SendConnectedPacketsAsync(CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested)
        {
            foreach (var tunnel in TunnelsSnapshot())
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
    private async Task PerformPingsAsync(CnCNetTunnel tunnel, TunnelTestResult result,
        int pingCount = PINGS_PER_TUNNEL, int pingTimeoutMs = PING_TIMEOUT_MS)
    {
        int timedOutPings = 0;

        for (int i = 0; i < pingCount && !_negotiationToken.IsCancellationRequested; i++)
        {
            var ping = result.AddPing(i, Stopwatch.GetTimestamp());

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
                var timeoutTask = Task.Delay(pingTimeoutMs, _negotiationToken);
                var completedTask = await Task.WhenAny(ping.CompletionSource.Task, timeoutTask);

                if (completedTask == timeoutTask)
                    timedOutPings++;
            }
            catch (OperationCanceledException)
            {
                Logger.Log($"V3PlayerNegotiator: Ping cancelled: ID {i} to {_remotePlayer.Name} on {tunnel.Name}");
                break;
            }
        }

        if (timedOutPings > 0 && !_negotiationToken.IsCancellationRequested)
            Logger.Log($"V3PlayerNegotiator: {timedOutPings}/{pingCount} pings timed out to {_remotePlayer.Name} on {tunnel.Name}");

        result.PingsCompletedTcs.TrySetResult(true);
    }

    private void OnPacketReceived(uint senderId, uint receiverId, TunnelPacketType packetType,
        ReadOnlyMemory<byte> payload, long receivedTime, CnCNetTunnel tunnel)
    {
        var result = _remotePlayer.GetTunnelResult(tunnel);
        if (result == null)
        {
            // Auto-learned P2P path: the peer sent from an endpoint not in their original
            // candidate list (e.g. a phone-hotspot that NATs to a different local IP).
            // Register it on-the-fly so we can ping it and include it in SelectBestTunnel.
            if (tunnel is not P2PTunnel)
                return;

            result = _remotePlayer.TunnelResults.GetOrAdd(tunnel, static _ => new TunnelTestResult());

            bool firstSeen;
            lock (_tunnelsLock)
            {
                firstSeen = !_tunnels.Contains(tunnel);
                if (firstSeen)
                    _tunnels.Add(tunnel);
            }

            if (firstSeen)
            {
                // Send Connected back so the NAT on the peer's side (e.g. phone hotspot)
                // has a return mapping and can forward our pings/choice to them.
                _tunnelHandler.SendPacket(tunnel, _localPlayer.Id, _remotePlayer.Id,
                    TunnelPacketType.Connected, null);
                Logger.Log($"V3PlayerNegotiator: Auto-learned P2P path {tunnel.Name} from {_remotePlayer.Name}, sending Connected back");
            }
        }

        switch (packetType)
        {
            case TunnelPacketType.Connected:
                //if we receive a connected packet, move on to the pinging phase.
                //Direct P2P paths use a tighter ping budget so a doomed candidate doesn't stall.
                if (_isDecider && !result.ConnectedReceived)
                {
                    result.ConnectedReceived = true;
                    result.ConnectedTcs.TrySetResult(true);
                    Logger.Log($"V3PlayerNegotiator: Connected received from {_remotePlayer.Name} on {tunnel.Name}, starting pings");
                    if (tunnel.IsDirect)
                        _ = PerformPingsAsync(tunnel, result, P2P_PINGS_PER_TUNNEL, P2P_PING_TIMEOUT_MS);
                    else
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
                        TunnelPacketType.PingResponse, payload.ToArray());
                }
                break;

            case TunnelPacketType.PingResponse:
                //if we receive a ping response, note down the received time and complete the ping.
                if (_isDecider && payload.Length >= 4)
                {
                    int id = BinaryPrimitives.ReadInt32LittleEndian(payload.Span);
                    result.CompletePing(id, receivedTime);
                }
                break;

            case TunnelPacketType.TunnelChoice:
                if (!_isDecider)
                {
                    // A legitimate choice can only arrive on a tunnel whose PingRequests *this*
                    // negotiator answered — the decider only picks tunnels that returned ping
                    // responses, and sends the choice through the winner. A choice on a tunnel
                    // that never pinged us is a stale packet from a previous, torn-down round
                    // (e.g. renegotiating while the peer's old decider was still retrying its
                    // choice). Accepting it would complete this round instantly with a tunnel
                    // the peer's *current* round never agreed to, then this negotiator gets
                    // disposed and the peer's real pings go unanswered — a phantom success on
                    // our side and a total ping timeout on theirs.
                    if (!result.PingRequestReceived)
                    {
                        Logger.Log($"V3PlayerNegotiator: Ignoring tunnel choice from {_remotePlayer.Name} via {tunnel.Name}: no ping request was received on that tunnel this round (likely stale)");
                        break;
                    }

                    // The chosen tunnel is the one this packet came through
                    int ping = -1;
                    if (payload.Length >= 4)
                        ping = BinaryPrimitives.ReadInt32LittleEndian(payload.Span);

                    // Packet loss (tenths of a percent) so we can display the same stats as the decider.
                    if (payload.Length >= 8)
                        _remotePlayer.NegotiatedPacketLoss = BinaryPrimitives.ReadInt32LittleEndian(payload.Span[4..]) / 10.0;

                    // P2P capability flag — whether the decider has P2P enabled.
                    if (payload.Length >= 9)
                        _remotePlayer.P2PEnabled = payload.Span[8] != 0;

                    Logger.Log($"V3PlayerNegotiator: {_remotePlayer.Name} chose {tunnel.Name} (Ping: {ping}ms, P2P: {_remotePlayer.P2PEnabled})");

                    _remotePlayer.Tunnel = tunnel;

                    // TunnelAck carries our own P2P flag so the decider knows whether to upgrade.
                    _tunnelHandler.SendPacket(tunnel, _localPlayer.Id, _remotePlayer.Id,
                        TunnelPacketType.TunnelAck, [0x01, _p2pEnabled ? (byte)0x01 : (byte)0x00]);

                    _negotiationCompletionSource.TrySetResult(true);
                    RaiseNegotiationResult(tunnel, ping);
                }
                break;

            case TunnelPacketType.TunnelAck:
                if (_isDecider)
                {
                    // P2P capability flag — whether the non-decider has P2P enabled.
                    if (payload.Length >= 2)
                        _remotePlayer.P2PEnabled = payload.Span[1] != 0;

                    if (!ReferenceEquals(tunnel, _pendingChoiceTunnel))
                    {
                        Logger.Log($"V3PlayerNegotiator: Ignoring acknowledgment from {_remotePlayer.Name} via {tunnel.Name}; it is not the tunnel of the outstanding choice");
                        break;
                    }

                    Logger.Log($"V3PlayerNegotiator: Received acknowledgment from {_remotePlayer.Name} for tunnel {tunnel.Name} (P2P: {_remotePlayer.P2PEnabled})");
                    _tunnelAckReceived.TrySetResult(true);
                }
                break;

            case TunnelPacketType.NegotiationFailed:
                // This packet exists solely to stop the decider's TunnelChoice retries, so it
                // is only meaningful to a decider with an outstanding choice. Anything else is
                // a stale packet from an earlier, torn-down negotiation round — e.g. after
                // renegotiating while the peer's previous round was still timing out — and must
                // not fail the current round. (If the peer really has given up, our choice will
                // go unacknowledged and the round fails through the retry path anyway.)
                if (!_isDecider || _pendingChoiceTunnel == null)
                {
                    // Logged once per negotiator: the sender broadcasts this packet to every
                    // tunnel in the list, so a single stale timeout produces dozens of copies.
                    if (!_loggedIgnoredFailureNotification)
                    {
                        _loggedIgnoredFailureNotification = true;
                        Logger.Log($"V3PlayerNegotiator: Ignoring failure notification from {_remotePlayer.Name} (no outstanding tunnel choice; likely stale)");
                    }

                    break;
                }

                Logger.Log($"V3PlayerNegotiator: Received failure notification from {_remotePlayer.Name}");
                _negotiationCompletionSource.TrySetResult(false);
                RaiseNegotiationResult(null, 0, "Remote player reported negotiation failure");
                break;

            case TunnelPacketType.P2PInfo:
                if (payload.Length >= 6 && payload.Length % 6 == 0)
                {
                    var peerEps = DecodeP2PEndpoints(payload);
                    Logger.Log($"V3PlayerNegotiator: Received {peerEps.Count} P2P candidate(s) from {_remotePlayer.Name}: {string.Join(", ", peerEps)}");
                    _p2pPeerEndpointTcs.TrySetResult(peerEps);
                }
                break;

            case TunnelPacketType.P2PDecline:
                Logger.Log($"V3PlayerNegotiator: Received P2PDecline from {_remotePlayer.Name}");
                _p2pPeerEndpointTcs.TrySetResult(null);
                break;
        }
    }

    // Informs the other player of the tunnel to use.
    // Returns true if an acknowledgment was received, false if all retries are exhausted.
    private async Task<bool> SendTunnelChoiceAsync(CnCNetTunnel tunnel, int ping, double packetLoss)
    {
        Logger.Log($"V3PlayerNegotiator: Sending tunnel choice to {_remotePlayer.Name}: {tunnel.Name} (Ping: {ping}ms, Loss: {packetLoss:F1}%)");

        _pendingChoiceTunnel = tunnel;

        // Payload: ping (int32) + packet loss in tenths of a percent (int32) + P2P flag (byte).
        // The non-decider reads these so it can show the same stats and knows whether to expect
        // a P2P upgrade round.
        var pingBytes = new byte[9];
        BinaryPrimitives.WriteInt32LittleEndian(pingBytes, ping);
        BinaryPrimitives.WriteInt32LittleEndian(pingBytes.AsSpan(4), (int)Math.Round(packetLoss * 10));
        pingBytes[8] = _p2pEnabled ? (byte)0x01 : (byte)0x00;

        for (int attempt = 0; attempt < TUNNEL_CHOICE_MAX_RETRIES; attempt++)
        {
            // Bail if NegotiationFailed (or any other completion signal) has already arrived.
            if (_negotiationCompletionSource.Task.IsCompleted)
            {
                Logger.Log($"V3PlayerNegotiator: Negotiation completion signaled before tunnel choice ack from {_remotePlayer.Name}, aborting retries");
                return false;
            }

            _tunnelHandler.SendPacket(tunnel, _localPlayer.Id, _remotePlayer.Id,
                TunnelPacketType.TunnelChoice, pingBytes);

            Logger.Log($"V3PlayerNegotiator: Attempt {attempt + 1} sent to {_remotePlayer.Name} via {tunnel.Name}");

            try
            {
                //wait for acknowledgment, negotiation failure, or timeout
                var timeoutTask = Task.Delay(TUNNEL_CHOICE_RETRY_INTERVAL_MS, _negotiationToken);
                var completedTask = await Task.WhenAny(_tunnelAckReceived.Task, _negotiationCompletionSource.Task, timeoutTask);

                if (completedTask == _tunnelAckReceived.Task)
                {
                    Logger.Log($"V3PlayerNegotiator: Acknowledgment received from {_remotePlayer.Name} for {tunnel.Name}");
                    return true;
                }
                if (completedTask == _negotiationCompletionSource.Task)
                {
                    Logger.Log($"V3PlayerNegotiator: Negotiation completion signaled while waiting for ack from {_remotePlayer.Name}, aborting retries");
                    return false;
                }
                Logger.Log($"V3PlayerNegotiator: No acknowledgment received, retrying... (attempt {attempt + 1}/{TUNNEL_CHOICE_MAX_RETRIES})");
            }
            catch (OperationCanceledException)
            {
                Logger.Log($"V3PlayerNegotiator: Cancelled while waiting for acknowledgment from {_remotePlayer.Name}");
                return false;
            }
        }

        Logger.Log($"V3PlayerNegotiator: Failed to receive tunnel acknowledgment from {_remotePlayer.Name} after {TUNNEL_CHOICE_MAX_RETRIES} goes");
        return false;
    }

    private void RaiseNegotiationComplete()
    {
        if (Interlocked.Exchange(ref _completionRaised, 1) == 0)
            NegotiationComplete?.Invoke(this, EventArgs.Empty);
    }

    private void PrintNegotiationResults()
    {
        var sb = new StringBuilder();

        if (_isDecider)
        {
            sb.AppendLine($"=== Decider Results for {_remotePlayer.Name} (ID: {_remotePlayer.Id}) ===");

            foreach (var tunnel in TunnelsSnapshot())
            {
                var result = _remotePlayer.GetTunnelResult(tunnel);
                if (result != null)
                {
                    var (successfulPings, totalPings) = result.GetPingCounts();

                    sb.AppendLine(
                        $"Player: {_remotePlayer.Name} | " +
                        $"Tunnel: {tunnel.Name} | " +
                        $"Avg RTT: {(result.AverageRtt.HasValue ? $"{result.AverageRtt.Value:F1}ms" : "N/A")} | " +
                        $"Real ping: {(tunnel.Ping.IsValid() ? $"{tunnel.Ping.Milliseconds:F1}ms" : "N/A")} | " +
                        $"Real ping*2: {(tunnel.Ping.IsValid() ? $"{tunnel.Ping.Milliseconds * 2:F1}ms" : "N/A")} | " +
                        $"Difference: {(tunnel.Ping.IsValid() && result.AverageRtt.HasValue ? $"{result.AverageRtt.Value - (tunnel.Ping.Milliseconds * 2):F1}ms" : "N/A")} | " +
                        $"Packet Loss: {result.PacketLoss:F1}% | " +
                        $"Pings: {successfulPings}/{totalPings} | " +
                        $"Connected: {result.ConnectedReceived}"
                    );
                }
            }

            var bestTunnel = _remotePlayer.Tunnel;
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

            sb.AppendLine($"=== End Decider Results for {_remotePlayer.Name} ===");
        }
        else
        {
            // Non-decider: log what we could observe — which tunnels the decider reached us on.
            // No RTT data (the decider measures that); this shows bidirectional connectivity per tunnel.
            sb.AppendLine($"=== Non-Decider Results for {_remotePlayer.Name} (ID: {_remotePlayer.Id}) ===");

            foreach (var tunnel in TunnelsSnapshot())
            {
                var result = _remotePlayer.GetTunnelResult(tunnel);
                if (result != null)
                {
                    sb.AppendLine(
                        $"Tunnel: {tunnel.Name} | " +
                        $"PingRequest received: {result.PingRequestReceived} | " +
                        $"Connected timed out: {result.ConnectedTimedOut}"
                    );
                }
            }

            sb.AppendLine($"Agreed tunnel: {_remotePlayer.Tunnel?.Name ?? "None"}");
            sb.AppendLine($"=== End Non-Decider Results for {_remotePlayer.Name} ===");
        }

        Logger.Log(sb.ToString());
    }

    /// <summary>
    /// Second negotiation round: exchange direct candidate addresses over the agreed relay
    /// tunnel, then re-run the same Connected → Ping → Choice → Ack negotiation over the direct
    /// paths. The decider pings them and, via SelectBestTunnel, picks the global best (relay vs
    /// direct), sending its choice through the winning tunnel; the non-decider adopts whatever
    /// tunnel that choice arrives on. If P2P doesn't pan out, both simply stay on the relay.
    /// </summary>
    private async Task PerformP2PUpgradeRoundAsync(CnCNetTunnel relayTunnel)
    {
        var localEps = await GatherLocalCandidatesAsync();
        if (localEps.Count == 0)
        {
            Logger.Log($"V3PlayerNegotiator: P2P upgrade skipped for {_remotePlayer.Name} — no local candidates gathered");
            return;
        }

        var peerEps = await ExchangeCandidatesAsync(localEps, relayTunnel);
        if (peerEps.Count == 0)
        {
            Logger.Log($"V3PlayerNegotiator: No P2P candidates from {_remotePlayer.Name}; keeping relay");
            return;
        }

        var p2pTunnels = BuildP2PTunnels(peerEps);
        Logger.Log($"V3PlayerNegotiator: P2P upgrade round with {_remotePlayer.Name} over {p2pTunnels.Count} direct path(s)");

        // Reset the per-round signals before any round-two packet can arrive, so a fast
        // TunnelChoice lands on this round's completion source rather than round one's.
        _negotiationCompletionSource = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        _tunnelAckReceived = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);

        // Punch from both sides so each NAT opens before pinging. The relay protocol only has
        // the non-decider send Connected, which is enough to reach a public relay server but not
        // to traverse a direct path where both peers' NATs need a mapping. Sent a few times since
        // the very first datagrams open the mapping and may themselves be dropped.
        for (int i = 0; i < 3; i++)
        {
            foreach (var tunnel in p2pTunnels)
                _tunnelHandler.SendPacket(tunnel, _localPlayer.Id, _remotePlayer.Id,
                    TunnelPacketType.Connected, null);

            await Task.Delay(150, _negotiationToken);
        }

        if (_isDecider)
            await PerformDeciderNegotiationAsync(p2pTunnels, P2P_UPGRADE_CONNECTED_TIMEOUT, raiseFailureOnNoAck: false);
        else
            await PerformNonDeciderNegotiationAsync(isUpgradeRound: true, totalTimeoutMs: P2P_UPGRADE_NONDECIDER_TIMEOUT_MS);

        bool upgradeAgreed = await _negotiationCompletionSource.Task;

        // If round two wasn't agreed (e.g. the decider couldn't get its choice acknowledged),
        // SelectBestTunnel may have optimistically pointed us at a direct path the peer never
        // committed to. Fall back to the relay both sides agreed on in round one — and, as the
        // decider, tell the peer so through a fresh TunnelChoice over the relay, so a peer that
        // *did* adopt the direct path converges back with us instead of split-braining.
        if (!upgradeAgreed && !_negotiationToken.IsCancellationRequested)
        {
            _remotePlayer.Tunnel = relayTunnel;

            if (!_isDecider)
            {
                Logger.Log($"V3PlayerNegotiator: P2P upgrade with {_remotePlayer.Name} not agreed; staying on relay {relayTunnel.Name}");
                return;
            }

            Logger.Log($"V3PlayerNegotiator: P2P upgrade with {_remotePlayer.Name} not agreed; re-offering relay {relayTunnel.Name}");

            _negotiationCompletionSource = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            _tunnelAckReceived = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);

            var relayResult = _remotePlayer.GetTunnelResult(relayTunnel);
            double? relayRtt = relayResult?.AverageRtt;
            int relayPing = relayRtt.HasValue ? (int)Math.Round(relayRtt.Value) : 0;
            double relayLoss = relayResult?.PacketLoss ?? 0;
            _remotePlayer.NegotiatedPacketLoss = relayLoss;

            bool reverted = await SendTunnelChoiceAsync(relayTunnel, relayPing, relayLoss);
            if (!reverted)
            {
                // No ack for the revert either. The peer's own upgrade timeout keeps it on the
                // relay (its negotiator may simply have finished already), so treat the relay
                // as converged rather than failing a pair whose round-one relay works.
                Logger.Log($"V3PlayerNegotiator: Relay fallback to {_remotePlayer.Name} was not acknowledged; assuming peer kept relay {relayTunnel.Name}");
            }

            _negotiationCompletionSource.TrySetResult(true);
            RaiseNegotiationResult(relayTunnel, relayPing, isRelayFallback: true);
        }
    }

    /// <summary>
    /// Gathers this peer's P2P candidates: every local LAN endpoint (which lets peers
    /// behind the same NAT connect without hairpinning) plus the STUN reflexive endpoint
    /// (which covers peers on different networks). Returns an empty list if P2P is disabled
    /// or no candidates could be gathered.
    /// </summary>
    private async Task<List<IPEndPoint>> GatherLocalCandidatesAsync()
    {
        var eps = new List<IPEndPoint>();
        if (!_p2pEnabled)
            return eps;

        eps.AddRange(_tunnelHandler.GetLocalP2PEndpoints());

        var reflexive = await _tunnelHandler.GetOrDiscoverP2PEndpointAsync();
        if (reflexive != null)
            eps.Add(reflexive);
        else if (eps.Count == 0)
            Logger.Log("V3PlayerNegotiator: STUN failed and no local candidates — P2P unavailable");
        else
            Logger.Log("V3PlayerNegotiator: STUN failed; proceeding with LAN-only P2P candidates");

        // De-duplicate (the reflexive address can coincide with a public LAN address).
        var candidates = eps.GroupBy(e => e.ToString()).Select(g => g.First()).ToList();

        if (candidates.Count > 0)
            Logger.Log($"V3PlayerNegotiator: Gathered {candidates.Count} local P2P candidate(s): {string.Join(", ", candidates)}");

        return candidates;
    }

    /// <summary>
    /// Advertises our candidate addresses through the established relay tunnel and waits for the
    /// peer's. Sent a few times since it's a single UDP packet on a path that, while just
    /// validated, can still drop a datagram. Returns the peer's candidates, or empty on timeout.
    /// </summary>
    private async Task<IReadOnlyList<IPEndPoint>> ExchangeCandidatesAsync(
        IReadOnlyList<IPEndPoint> localEps, CnCNetTunnel relayTunnel)
    {
        var payload = EncodeP2PEndpoints(localEps);

        using var cts = CancellationTokenSource.CreateLinkedTokenSource(_negotiationToken);
        cts.CancelAfter(P2P_CANDIDATE_EXCHANGE_TIMEOUT_MS);
        try
        {
            // Always send at least once even if the peer's candidates already arrived early
            // (they can arrive before STUN finishes, completing the TCS before we even enter
            // this loop — if we skip the send, the peer never gets our candidates).
            for (int attempt = 0; attempt < 3; attempt++)
            {
                _tunnelHandler.SendPacket(relayTunnel, _localPlayer.Id, _remotePlayer.Id,
                    TunnelPacketType.P2PInfo, payload);

                if (_p2pPeerEndpointTcs.Task.IsCompleted)
                    break;

                var completed = await Task.WhenAny(_p2pPeerEndpointTcs.Task, Task.Delay(150, cts.Token));
                if (completed == _p2pPeerEndpointTcs.Task)
                    break;
            }

            var peerEps = await _p2pPeerEndpointTcs.Task.WaitAsync(cts.Token);
            return peerEps ?? (IReadOnlyList<IPEndPoint>)Array.Empty<IPEndPoint>();
        }
        catch (OperationCanceledException)
        {
            Logger.Log($"V3PlayerNegotiator: No P2P candidates received from {_remotePlayer.Name} (timeout)");
            return Array.Empty<IPEndPoint>();
        }
    }

    /// <summary>
    /// Turns the peer's advertised candidates into <see cref="P2PTunnel"/>s, registers them so
    /// their packets are dispatched, and adds them to the negotiation tunnel set for round two.
    /// </summary>
    private List<CnCNetTunnel> BuildP2PTunnels(IReadOnlyList<IPEndPoint> peerEps)
    {
        var tunnels = new List<CnCNetTunnel>();
        foreach (var ep in peerEps)
        {
            var p2pTunnel = new P2PTunnel(ep, _remotePlayer.Name);
            _tunnelHandler.AddP2PTunnel(p2pTunnel, _localPlayer.Id, _remotePlayer.Id);
            _remotePlayer.AddTunnelResult(p2pTunnel);
            lock (_tunnelsLock)
                _tunnels.Add(p2pTunnel);
            tunnels.Add(p2pTunnel);
        }
        return tunnels;
    }

    private static byte[] EncodeP2PEndpoints(IReadOnlyList<IPEndPoint> eps)
    {
        var ipv4 = eps.Where(e => e.Address.AddressFamily == AddressFamily.InterNetwork).ToList();
        var buf = new byte[ipv4.Count * 6];
        for (int i = 0; i < ipv4.Count; i++)
        {
            ipv4[i].Address.GetAddressBytes().CopyTo(buf, i * 6);
            BinaryPrimitives.WriteUInt16BigEndian(buf.AsSpan(i * 6 + 4), (ushort)ipv4[i].Port);
        }
        return buf;
    }

    private static List<IPEndPoint> DecodeP2PEndpoints(ReadOnlyMemory<byte> payload)
    {
        var eps = new List<IPEndPoint>();
        var span = payload.Span;
        for (int i = 0; i + 6 <= span.Length; i += 6)
        {
            var ip = new IPAddress(span.Slice(i, 4).ToArray());
            ushort port = BinaryPrimitives.ReadUInt16BigEndian(span.Slice(i + 4, 2));
            eps.Add(new IPEndPoint(ip, port));
        }
        return eps;
    }

    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposeState, 1) != 0)
            return;

        _negotiationCts.Cancel();
        _tunnelAckReceived.TrySetCanceled();
        _negotiationCompletionSource.TrySetCanceled();
        _p2pPeerEndpointTcs.TrySetCanceled();
        _tunnelHandler.UnregisterV3PacketHandler(_localPlayer.Id, _remotePlayer.Id);
        _negotiationCts.Dispose();
    }
}

