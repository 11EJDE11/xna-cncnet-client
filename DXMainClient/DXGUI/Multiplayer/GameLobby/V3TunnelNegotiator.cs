using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using DTAClient.Domain.Multiplayer.CnCNet;
using ClientCore;
using Rampastring.Tools;

namespace DTAClient.DXGUI.Multiplayer.GameLobby
{

    public class TunnelChosenEventArgs : EventArgs
    {
        public uint PlayerId { get; set; }
        public string PlayerName { get; set; }
        public CnCNetTunnel ChosenTunnel { get; set; }
        public bool IsLocalDecision { get; set; }
    }

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

        // Used to cancel SendConnectedPacketsAsync and PerformPingsAsync.
        private readonly CancellationTokenSource _negotiationCts = new CancellationTokenSource();

        // Signals negotiation complete. Deciders = set when tunnel choice is made.
        // Non-deciders = set when tunnel choice is received from decider.
        private TaskCompletionSource<bool> _negotiationCompletionSource = new TaskCompletionSource<bool>();

        // Something to differentiate game data from client data.
        private static readonly byte[] MAGIC_DATA = { 0x45, 0x4A, 0x45, 0x4A, 0x45, 0x4A }; //EJEJEJ

        // How long the non-decider will keep sending Connected packets to a single tunnel
        // while waiting for a Ping Request from the decider. After this, the tunnel is skipped.
        // Note that the existing players in the lobby will begin negotiating when
        // Channel_UserAdded is called, while the joining player will begin negotiation
        // when ApplyPlayerOptions is sent by the host. The timeout should be long enough for
        // the joining player to receive that IRC message + attempt connections to each tunnel.
        private const int NON_DECIDER_CONNECTED_TIMEOUT_MS = 10000;

        // How long the non-decider will keep sending Connected packets overall.
        private const int NON_DECIDER_TOTAL_TIMEOUT_MS = 20000;

        // How long the decider will wait to receive a Ping Request from the non-decider.
        // If none are received in time, the tunnel is skipped.
        private static readonly TimeSpan DECIDER_CONNECTED_PHASE_TIMEOUT = TimeSpan.FromSeconds(10);

        // How long the decider will wait for pings to complete. If it takes this long, 
        // pick the best one from the results that have come in.
        private static readonly TimeSpan DECIDER_PING_PHASE_TIMEOUT = TimeSpan.FromSeconds(15);
        private const int PINGS_PER_TUNNEL = 5;
        private const int PING_TIMEOUT_MS = 2000; //consider it dropped, move on to the next ping

        private const int NON_DECIDER_CONNECTED_INTERVAL_MS = 500; //delay Connected packets a bit to avoid overloading

        // When the decider has picked a tunnel, they need to inform the non-decider.
        // As it's UDP and not garaunteed to make ti, we need an acknowledgement.
        private const int TUNNEL_CHOICE_RETRY_INTERVAL_MS = 1000;
        private const int TUNNEL_CHOICE_MAX_RETRIES = 10;
        private TaskCompletionSource<bool> _tunnelAckReceived = new TaskCompletionSource<bool>(); //true when tunnel choice ack'd

        public V3PlayerInfo RemotePlayer => _remotePlayer;

        public event EventHandler<CnCNetTunnel> TunnelChosen;
        public event EventHandler NegotiationComplete;
        public event EventHandler<string> NegotiationFailed; //todo: merge with TunnelChosen

        public V3PlayerNegotiator(V3PlayerInfo localPlayer, V3PlayerInfo remotePlayer, List<CnCNetTunnel> tunnels,
            TunnelHandler tunnelHandler)
        {
            _localPlayer = localPlayer;
            _remotePlayer = remotePlayer;
            _tunnels = tunnels;
            _tunnelHandler = tunnelHandler;
            _isDecider = localPlayer.Id < remotePlayer.Id;

            _remotePlayer.InitializeTunnelResults(tunnels);

            _tunnelHandler.RegisterNegotiationHandler(_localPlayer.Id, _remotePlayer.Id, OnPacketReceived);
        }

        public async Task<bool> NegotiateAsync()
        {
            try
            {
                Debug.Print($"Starting negotiation with player {_remotePlayer.Name} (ID: {_remotePlayer.Id}, Decider: {_isDecider})");

                _negotiationCompletionSource = new TaskCompletionSource<bool>();
                _tunnelAckReceived = new TaskCompletionSource<bool>();

                _tunnelHandler.SendRegistrationToAllTunnels(_localPlayer.Id, _tunnels);

                if (_isDecider)
                    await PerformDeciderNegotiationAsync();
                else
                    await PerformNonDeciderNegotiationAsync();

                PrintNegotiationResults();
                _negotiationCompletionSource.TrySetResult(true);
                NegotiationComplete?.Invoke(this, EventArgs.Empty);
                return true;
            }
            catch (Exception ex)
            {
                Debug.Print($"Negotiation failed with {_remotePlayer.Name}: {ex.Message}");
                PrintNegotiationResults();
                NotifyNegotiationFailure();
                _negotiationCompletionSource.TrySetResult(false);
                NegotiationFailed?.Invoke(this, ex.Message);
                NegotiationComplete?.Invoke(this, EventArgs.Empty);
                return false;
            }
        }

        //todo: should really be ctcp, not communicated over tunnels
        private void NotifyNegotiationFailure()
        {
            try
            {
                foreach (var tunnel in _tunnels)
                    _tunnelHandler.SendPacket(tunnel, _localPlayer.Id, _remotePlayer.Id,
                TunnelPacketType.NegotiationFailed);

                Debug.Print($"Sent negotiation failure notification to {_remotePlayer.Name}");
            }
            catch (Exception ex)
            {
                Debug.Print($"Failed to send negotiation failure notification: {ex.Message}");
            }
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

                _ = ProcessTunnelAsync(tunnel, result, () =>
                {
                    lock (completionLock)
                    {
                        completedTunnels++;
                        if (!selectionMade && (completedTunnels >= totalTunnels * 0.8 || completedTunnels == totalTunnels))
                        {
                            selectionMade = true;
                            selectionTcs.TrySetResult(true);
                        }
                    }
                });
            }

            // Wait for early selection or all completion
            await selectionTcs.Task;

            var bestTunnel = _remotePlayer.SelectBestTunnel(_tunnels);
            if (bestTunnel != null)
            {
                await SendTunnelChoiceAsync(bestTunnel);
                TunnelChosen?.Invoke(this, bestTunnel);
            }
            else
            {
                Debug.Print("[FAILURE] No tunnels had any ping responses");
                NotifyNegotiationFailure();
                throw new Exception("No viable tunnel");
            }
        }

        private async Task ProcessTunnelAsync(CnCNetTunnel tunnel, TunnelTestResult result, Action onComplete)
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
                // expected when negotiation is cancelled
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
            using (var cts = CancellationTokenSource.CreateLinkedTokenSource(_negotiationCts.Token))
            {
                _ = SendConnectedPacketsAsync(cts.Token);

                try
                {
                    //wait for tuennel choice or negotiation timeout
                    var negotiationTimeout = Task.Delay(NON_DECIDER_TOTAL_TIMEOUT_MS, cts.Token);
                    var completed = await Task.WhenAny(_negotiationCompletionSource.Task, negotiationTimeout);

                    if (completed == negotiationTimeout && !_negotiationCompletionSource.Task.IsCompleted)
                    {
                        Debug.Print($"[TIMEOUT] No PingRequest received from decider {_remotePlayer.Name} within 20 seconds.");
                        _negotiationCompletionSource.TrySetResult(false);
                        cts.Cancel();
                        NegotiationComplete?.Invoke(this, EventArgs.Empty);
                    }
                }
                catch (OperationCanceledException)
                {
                    Debug.Print($"[CANCELLED] Negotiation with {_remotePlayer.Name} was cancelled.");
                }
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

                    //Debug.Print($"Sending Connected packet to player {_remotePlayer.Name} ({_remotePlayer.Id}) via {tunnel.Name}");

                    _tunnelHandler.SendPacket(tunnel, _localPlayer.Id, _remotePlayer.Id,
                        TunnelPacketType.Connected);

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

                _tunnelHandler.SendPacket(tunnel, _localPlayer.Id, _remotePlayer.Id,
                    TunnelPacketType.PingRequest, BitConverter.GetBytes(i));

                //Debug.Print($"[PING REQUEST] ID {i} sent to {_remotePlayer.Name} on {tunnel.Name}");

                // Wait for a ping response or timeout
                try
                {
                    var timeoutTask = Task.Delay(PING_TIMEOUT_MS, _negotiationCts.Token);
                    var completedTask = await Task.WhenAny(ping.CompletionSource.Task, timeoutTask);

                    if (completedTask == timeoutTask)
                        Debug.Print($"[PING TIMEOUT] ID {i} to {_remotePlayer.Name} on {tunnel.Name}");
                    //else
                        //Debug.Print($"[PING RESPONSE] ID {i} received from {_remotePlayer.Name} on {tunnel.Name}");
                }
                catch (OperationCanceledException)
                {
                    Debug.Print($"[PING CANCELLED] ID {i} to {_remotePlayer.Name} on {tunnel.Name}");
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
                        //Debug.Print($"[CONNECTED] Received from {_remotePlayer.Name} on tunnel {tunnel.Name}");
                        result.ConnectedReceived = true;
                        result.ConnectedTcs.TrySetResult(true);
                        _ = PerformPingsAsync(tunnel, result);
                    }
                    break;

                case TunnelPacketType.PingRequest:
                    //if we receive a ping request, reply with a ping response that contains the ping ID.
                    //Debug.Print($"[PING REQUEST] From {_remotePlayer.Name} on tunnel {tunnel.Name}");
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
                        int id = BitConverter.ToInt32(payload, 0);
                        var ping = result.PingResults.FirstOrDefault(p => p.ID == id);
                        if (ping != null && !ping.ReceivedTimeTicks.HasValue)
                        {
                            ping.ReceivedTimeTicks = Stopwatch.GetTimestamp();
                            ping.CompletionSource.TrySetResult(true); //ping complete
                            //Debug.Print($"[PING RESPONSE] Received ID {id} from {_remotePlayer.Name} on tunnel {tunnel.Name}, RTT: {ping.RoundTripTime:F1}ms");
                        }
                    }
                    break;

                case TunnelPacketType.TunnelChoice:
                    if (!_isDecider)
                    {
                        // The chosen tunnel is the one this packet came through
                        Debug.Print($"[TUNNEL CHOICE] {_remotePlayer.Name} chose {tunnel.Name}");

                        _remotePlayer.Tunnel = tunnel;

                        _tunnelHandler.SendPacket(tunnel, _localPlayer.Id, _remotePlayer.Id,
                            TunnelPacketType.TunnelAck, new byte[] { 0x01 });

                        TunnelChosen?.Invoke(this, tunnel);
                        _negotiationCompletionSource.TrySetResult(true);
                    }

                    break;

                case TunnelPacketType.TunnelAck:
                    if (_isDecider)
                    {
                        Debug.Print($"[TUNNEL ACK] Received acknowledgment from {_remotePlayer.Name} for tunnel {tunnel.Name}");
                        _tunnelAckReceived.TrySetResult(true);
                    }
                    break;
                case TunnelPacketType.NegotiationFailed:
                    Debug.Print($"[NEGOTIATION FAILED] Received failure notification from {_remotePlayer.Name}");
                    _negotiationCompletionSource.TrySetResult(false);
                    NegotiationFailed?.Invoke(this, "Remote player reported negotiation failure");
                    break;
            }
        }

        //informs other player of the tunnel to use.
        private async Task SendTunnelChoiceAsync(CnCNetTunnel tunnel)
        {
            Debug.Print($"[TUNNEL CHOICE] Sending tunnel choice to {_remotePlayer.Name}: {tunnel.Name}");

            for (int attempt = 0; attempt < TUNNEL_CHOICE_MAX_RETRIES; attempt++)
            {
                //send the tunnel choice
                _tunnelHandler.SendPacket(tunnel, _localPlayer.Id, _remotePlayer.Id,
                    TunnelPacketType.TunnelChoice, new byte[] { 0x01 });
                Debug.Print($"[TUNNEL CHOICE] Attempt {attempt + 1} sent to {_remotePlayer.Name} via {tunnel.Name}");

                try
                {
                    //wait for acknowledgment or timeout
                    var timeoutTask = Task.Delay(TUNNEL_CHOICE_RETRY_INTERVAL_MS, _negotiationCts.Token);
                    var completedTask = await Task.WhenAny(_tunnelAckReceived.Task, timeoutTask);

                    if (completedTask == _tunnelAckReceived.Task)
                    {
                        Debug.Print($"[TUNNEL CHOICE] Acknowledgment received from {_remotePlayer.Name} for {tunnel.Name}");
                        TunnelChosen?.Invoke(this, tunnel);
                        return; // success
                    }
                    else
                    {
                        Debug.Print($"[TUNNEL CHOICE] No acknowledgment received, retrying... (attempt {attempt + 1}/{TUNNEL_CHOICE_MAX_RETRIES})");
                    }
                }
                catch (OperationCanceledException)
                {
                    Debug.Print($"[TUNNEL CHOICE] Cancelled while waiting for acknowledgment from {_remotePlayer.Name}");
                    return;
                }
            }

            Debug.Print($"[TUNNEL CHOICE] Failed to recieve tunnel acknowledgment from {_remotePlayer.Name} after {TUNNEL_CHOICE_MAX_RETRIES} goes");
            //todo: what now it's failed to send? Maybe nothing as the ack is just a one-packet jobbie that might have got lost anyway. Is it hacky to just send 5 acks and hope one gets through?
        }

        private void PrintNegotiationResults()
        {
            if (!_isDecider)
            {
                Debug.Print($"We are non decider. No tunnel is chosen.");
                return;
            }

            Debug.Print($"=== Negotiation Results for {_remotePlayer.Name} (ID: {_remotePlayer.Id}) ===");
            Logger.Log($"=== Negotiation Results for {_remotePlayer.Name} (ID: {_remotePlayer.Id}) ===");

            foreach (var tunnel in _tunnels)
            {
                var result = _remotePlayer.GetTunnelResult(tunnel);
                if (result != null)
                {
                    var successfulPings = result.PingResults.Count(p => p.RoundTripTime.HasValue);

                    Debug.Print($"Player: {_remotePlayer.Name} | Tunnel: {tunnel.Name} | " +
                               $"Avg RTT: {(result.AverageRtt >= 0 ? $"{result.AverageRtt:F1}ms" : "N/A")} | " +
                               $"Real ping: {(tunnel.PingInMs >= 0 ? $"{tunnel.PingInMs:F1}ms" : "N/A")} | " +
                               $"Real ping*2: {(tunnel.PingInMs >= 0 ? $"{tunnel.PingInMs * 2:F1}ms" : "N/A")} | " +
                               $"Difference: {(tunnel.PingInMs >= 0 && result.AverageRtt > 0 ? $"{result.AverageRtt - (tunnel.PingInMs * 2):F1}ms" : "N/A")} | " +
                               $"Packet Loss: {result.PacketLoss:F1}% | " +
                               $"Pings: {successfulPings}/{result.PingResults.Count} | " +
                               $"Connected: {result.ConnectedReceived}");
                    Logger.Log($"Player: {_remotePlayer.Name} | Tunnel: {tunnel.Name} | " +
                               $"Avg RTT: {(result.AverageRtt >= 0 ? $"{result.AverageRtt:F1}ms" : "N/A")} | " +
                               $"Real ping: {(tunnel.PingInMs >= 0 ? $"{tunnel.PingInMs:F1}ms" : "N/A")} | " +
                               $"Real ping*2: {(tunnel.PingInMs >= 0 ? $"{tunnel.PingInMs * 2:F1}ms" : "N/A")} | " +
                               $"Difference: {(tunnel.PingInMs >= 0 && result.AverageRtt > 0 ? $"{result.AverageRtt- (tunnel.PingInMs * 2):F1}ms" : "N/A")} | " +
                               $"Packet Loss: {result.PacketLoss:F1}% | " +
                               $"Pings: {successfulPings}/{result.PingResults.Count} | " +
                               $"Connected: {result.ConnectedReceived}");
                }
            }

            var bestTunnel = _remotePlayer.SelectBestTunnel(_tunnels);
            if (bestTunnel != null)
            {
                var bestResult = _remotePlayer.GetTunnelResult(bestTunnel);
                Debug.Print($"BEST TUNNEL for {_remotePlayer.Name}: {bestTunnel.Name} " +
                            $"(RTT: {bestResult.AverageRtt:F1}ms, Loss: {bestResult.PacketLoss:F1}%)");
                Logger.Log($"BEST TUNNEL for {_remotePlayer.Name}: {bestTunnel.Name} " +
                            $"(RTT: {bestResult.AverageRtt:F1}ms, Loss: {bestResult.PacketLoss:F1}%)");
            }
            else
            {
                Debug.Print($"NO VIABLE TUNNEL found for {_remotePlayer.Name}");
                Logger.Log($"NO VIABLE TUNNEL found for {_remotePlayer.Name}");
            }

            Debug.Print($"=== End Results for {_remotePlayer.Name} ===");
            Logger.Log($"=== End Results for {_remotePlayer.Name} ===");
        }

        public void Dispose()
        {
            _negotiationCts?.Cancel();
            _negotiationCts?.Dispose();

            _tunnelAckReceived?.TrySetCanceled();
            _negotiationCompletionSource?.TrySetCanceled();

            if (_tunnelHandler != null)
                _tunnelHandler.UnregisterNegotiationHandler(_localPlayer.Id, _remotePlayer.Id);
        }
    }

    public class V3TunnelNegotiationManager : IDisposable
    {
        private readonly V3PlayerInfo _localPlayer;
        private readonly TunnelHandler _tunnelHandler;
        private readonly Dictionary<uint, V3PlayerNegotiator> _negotiators = new Dictionary<uint, V3PlayerNegotiator>(); //todo double check dict use
        private readonly object _lock = new object();

        public event EventHandler<TunnelChosenEventArgs> TunnelChosen;

        public V3TunnelNegotiationManager(V3PlayerInfo localPlayer, TunnelHandler tunnelHandler)
        {
            _localPlayer = localPlayer;
            _tunnelHandler = tunnelHandler;
        }
        
        private List<CnCNetTunnel> GetAvailableTunnels()
        {
            return _tunnelHandler.Tunnels
                .Where(t => t.Version == 3 &&
                    (UserINISettings.Instance.PingUnofficialCnCNetTunnels || t.Official || t.Recommended))
                .ToList();
        }

        public bool StartNegotiation(V3PlayerInfo player)
        {
            if (player == _localPlayer)
                return true;

            player.HasNegotiated = false;
            player.IsNegotiating = true;

            Debug.Print($"[ADD PLAYER] Adding player {player.Name} (ID: {player.Id})");

            lock (_lock)
            {
                if (_negotiators.ContainsKey(player.Id))
                    return true;
            }

            var availableTunnels = GetAvailableTunnels();

            if (availableTunnels.Count == 0)
            {
                Debug.Print($"[ADD PLAYER] No available V3 tunnels for negotiation with {player.Name}");
                player.HasNegotiated = true;
                player.IsNegotiating = false;
                return false;
            }

            var negotiator = new V3PlayerNegotiator(_localPlayer, player, availableTunnels, _tunnelHandler);

            negotiator.TunnelChosen += OnTunnelChosenHandler;
            negotiator.NegotiationComplete += OnNegotiationCompleteHandler;
            negotiator.NegotiationFailed += OnNegotiationFailedHandler;

            lock (_lock)
            {
                _negotiators[player.Id] = negotiator;
            }

            var negotiationTask = Task.Run(() => NegotiationWorkerAsync(negotiator, player), CancellationToken.None);

            return true;
        }
        private async Task NegotiationWorkerAsync(V3PlayerNegotiator negotiator, V3PlayerInfo player)
        {
            Debug.Print($"Negotiation task started for {player.Name}");
            try
            {
                bool success = await negotiator.NegotiateAsync().ConfigureAwait(false);
                if (!success)
                {
                    Debug.Print($"[NEGOTIATION FAILED] For player {player.Name}, cleaning up.");
                    await Task.Yield(); // allow state to settle (optional)
                    StopNegotiation(player.Id);
                }
            }
            catch (Exception ex)
            {
                Debug.Print($"[NEGOTIATION ERROR] For player {player.Name}: {ex.Message}");
                StopNegotiation(player.Id);
            }
            Debug.Print($"Negotiation task finished for {player.Name}");
        }

        public void StopNegotiation(uint playerId)
        {
            V3PlayerNegotiator negotiator;
            lock (_lock)
            {
                if (!_negotiators.TryGetValue(playerId, out negotiator))
                    return;

                _negotiators.Remove(playerId);
            }

            if (negotiator != null)
            {
                negotiator.TunnelChosen -= OnTunnelChosenHandler;
                negotiator.NegotiationComplete -= OnNegotiationCompleteHandler;
                negotiator.NegotiationFailed -= OnNegotiationFailedHandler;
                negotiator.Dispose();
            }
        }

        private void OnNegotiationFailedHandler(object sender, string reason)
        {
            var negotiator = (V3PlayerNegotiator)sender;
            var player = negotiator.RemotePlayer;
            if (player == null) return;

            Debug.Print($"[NEGOTIATION FAILED] Player {player.Name}: {reason}");

            TunnelChosen?.Invoke(this, new TunnelChosenEventArgs
            {
                PlayerId = player.Id,
                PlayerName = player.Name,
                ChosenTunnel = null,
                IsLocalDecision = _localPlayer.Id < player.Id
            });
        }

        private void OnTunnelChosenHandler(object sender, CnCNetTunnel tunnel)
        {
            var negotiator = (V3PlayerNegotiator)sender;
            var player = negotiator.RemotePlayer;
            if (player == null) return;

            player.HasNegotiated = true;
            player.IsNegotiating = false;

            TunnelChosen?.Invoke(this, new TunnelChosenEventArgs
            {
                PlayerId = player.Id,
                PlayerName = player.Name,
                ChosenTunnel = tunnel,
                IsLocalDecision = _localPlayer.Id < player.Id
            });

            StopNegotiation(player.Id);
        }

        private void OnNegotiationCompleteHandler(object sender, EventArgs e)
        {
            var negotiator = (V3PlayerNegotiator)sender;
            var player = negotiator.RemotePlayer;
            if (player == null) return;

            if (!player.HasNegotiated)
            {
                player.HasNegotiated = true;
                player.IsNegotiating = false;
                Debug.Print($"[NEGOTIATION COMPLETE] Cleaning up failed negotiator for {player.Name}");

                var failureEvent = new TunnelChosenEventArgs
                {
                    PlayerId = player.Id,
                    PlayerName = player.Name,
                    ChosenTunnel = null,
                    IsLocalDecision = false
                };

                // This will trigger the failure broadcast in the game lobby
                TunnelChosen?.Invoke(this, failureEvent);

                StopNegotiation(player.Id);
            }
        }

        public void Dispose()
        {
            List<V3PlayerNegotiator> negotiators;
            lock (_lock)
            {
                negotiators = _negotiators.Values.ToList();
                _negotiators.Clear();
            }

            foreach (var negotiator in negotiators)
                negotiator?.Dispose();
        }
    }
}