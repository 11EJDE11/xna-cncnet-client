using ClientCore;
using DTAClient.Online;
using Microsoft.Xna.Framework;
using Rampastring.Tools;
using Rampastring.XNAUI;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Text;
using System.Threading.Tasks;
using DTAClient.DXGUI.Multiplayer.GameLobby;

namespace DTAClient.Domain.Multiplayer.CnCNet
{
    public class TunnelHandler : GameComponent, IDisposable
    {
        /// <summary>
        /// Determines the time between pinging the current tunnel (if it's set).
        /// </summary>
        private const double CURRENT_TUNNEL_PING_INTERVAL = 20.0;

        /// <summary>
        /// A reciprocal to the value which determines how frequent the full tunnel
        /// refresh would be done instead of just pinging the current tunnel (1/N of 
        /// current tunnel ping refreshes would be substituted by a full list refresh).
        /// Multiply by <see cref="CURRENT_TUNNEL_PING_INTERVAL"/> to get the interval 
        /// between full list refreshes.
        /// </summary>
        private const uint CYCLES_PER_TUNNEL_LIST_REFRESH = 6;
        private static readonly int[] SUPPORTED_TUNNEL_VERSIONS = { 2, 3 };

        public TunnelHandler(WindowManager wm, CnCNetManager connectionManager) : base(wm.Game)
        {
            this.wm = wm;
            this.connectionManager = connectionManager;
            wm.Game.Components.Add(this);
            Enabled = false;

            connectionManager.Connected += ConnectionManager_Connected;
            connectionManager.Disconnected += ConnectionManager_Disconnected;
            connectionManager.ConnectionLost += ConnectionManager_ConnectionLost;

            _v3Communicator = new V3TunnelCommunicator();
        }

        public List<CnCNetTunnel> Tunnels { get; private set; } = new List<CnCNetTunnel>();
        public CnCNetTunnel CurrentTunnel { get; set; } = null;
        public V3TunnelCommunicator V3Communicator => _v3Communicator;
        public V3GameTunnelBridge GameTunnelBridge;

        public event EventHandler TunnelsRefreshed;
        public event EventHandler CurrentTunnelPinged;
        public event EventHandler<CnCNetTunnel> TunnelFailed;
        public event Action<int> TunnelPinged;

        private WindowManager wm;
        private readonly CnCNetManager connectionManager;
        private TimeSpan timeSinceTunnelRefresh = TimeSpan.MaxValue;
        private uint skipCount = 0;
        private readonly V3TunnelCommunicator _v3Communicator;

        private void DoTunnelPinged(int index)
        {
            if (TunnelPinged != null)
                wm.AddCallback(TunnelPinged, index);
        }

        private void DoCurrentTunnelPinged()
        {
            if (CurrentTunnelPinged != null)
                wm.AddCallback(CurrentTunnelPinged, this, EventArgs.Empty);
        }

        private void ConnectionManager_Connected(object sender, EventArgs e)
        {
            if (!_v3Communicator.IsInitialized)
                InitializeV3Communicator();
            Enabled = true;
        }

        private void ConnectionManager_ConnectionLost(object sender, Online.EventArguments.ConnectionLostEventArgs e)
        {
            Enabled = false;
        }

        private void ConnectionManager_Disconnected(object sender, EventArgs e)
        {
            Enabled = false;
        }

        private void RefreshTunnelsAsync()
        {
            Task.Factory.StartNew(() =>
            {
                List<CnCNetTunnel> tunnels = RefreshTunnels();
                wm.AddCallback(new Action<List<CnCNetTunnel>>(HandleRefreshedTunnels), tunnels);
            });
        }

        private void HandleRefreshedTunnels(List<CnCNetTunnel> newTunnels)
        {
            if (newTunnels.Count == 0)
            {
                TunnelsRefreshed?.Invoke(this, EventArgs.Empty);
                return;
            }

            var existingTunnels = Tunnels.ToDictionary(t => $"{t.Address}:{t.Port}");
            var updatedTunnels = new List<CnCNetTunnel>();

            foreach (var newTunnel in newTunnels)
            {
                string key = $"{newTunnel.Address}:{newTunnel.Port}";
                if (existingTunnels.TryGetValue(key, out var existingTunnel))
                {
                    existingTunnel.UpdateFrom(newTunnel);
                    updatedTunnels.Add(existingTunnel);
                }
                else
                {
                    updatedTunnels.Add(newTunnel);
                }
            }

            Tunnels = updatedTunnels;
            TunnelsRefreshed?.Invoke(this, EventArgs.Empty);

            for (int i = 0; i < Tunnels.Count; i++)
            {
                if (UserINISettings.Instance.PingUnofficialCnCNetTunnels || Tunnels[i].Official || Tunnels[i].Recommended)
                    _ = PingListTunnelAsync(i);
            }

            if (CurrentTunnel != null)
            {
                var updatedTunnel = Tunnels.Find(t => t.Address == CurrentTunnel.Address && t.Port == CurrentTunnel.Port);
                if (updatedTunnel != null)
                {
                    // don't re-ping if the tunnel still exists in list, just update the tunnel instance and
                    // fire the event handler (the tunnel was already pinged when traversing the tunnel list)
                    CurrentTunnel = updatedTunnel;
                    DoCurrentTunnelPinged();
                }
                else
                {
                    // tunnel is not in the list anymore so it's not updated with a list instance and pinged
                    PingCurrentTunnelAsync();
                }
            }

            InitializeV3Communicator();
        }

        private Task PingListTunnelAsync(int index)
        {
            return Task.Factory.StartNew(() =>
            {
                var tunnel = Tunnels[index];
                int previousPing = tunnel.PingInMs;
                tunnel.UpdatePing();

                // Check for tunnel failure
                if (previousPing > 0 && (tunnel.PingInMs <= 0 || tunnel.PingInMs > 2000))
                    TunnelFailed?.Invoke(this, tunnel);

                DoTunnelPinged(index);
            });
        }

        private Task PingCurrentTunnelAsync(bool checkTunnelList = false)
        {
            return Task.Factory.StartNew(() =>
            {
                var tunnel = CurrentTunnel;
                if (tunnel == null) return;

                int previousPing = tunnel.PingInMs;
                tunnel.UpdatePing();

                if (previousPing > 0 && (tunnel.PingInMs <= 0 || tunnel.PingInMs > 2000))
                    TunnelFailed?.Invoke(this, tunnel);

                DoCurrentTunnelPinged();

                if (checkTunnelList)
                {
                    int tunnelIndex = Tunnels.FindIndex(t => t.Address == tunnel.Address && t.Port == tunnel.Port);
                    if (tunnelIndex > -1)
                        DoTunnelPinged(tunnelIndex);
                }
            });
        }

        private bool OnlineTunnelDataAvailable => !string.IsNullOrWhiteSpace(ClientConfiguration.Instance.CnCNetTunnelListURL);

        private bool OfflineTunnelDataAvailable => SafePath.GetFile(ProgramConstants.ClientUserFilesPath, "tunnel_cache").Exists;

        private byte[] GetRawTunnelDataOnline()
        {
            WebClient client = new ExtendedWebClient();
            return client.DownloadData(ClientConfiguration.Instance.CnCNetTunnelListURL);
        }

        private byte[] GetRawTunnelDataOffline()
        {
            FileInfo tunnelCacheFile = SafePath.GetFile(ProgramConstants.ClientUserFilesPath, "tunnel_cache");
            return File.ReadAllBytes(tunnelCacheFile.FullName);
        }

        private byte[] GetRawTunnelData(int retryCount = 2)
        {
            Logger.Log("Fetching tunnel server info.");

            if (OnlineTunnelDataAvailable)
            {
                for (int i = 0; i < retryCount; i++)
                {
                    try
                    {
                        byte[] data = GetRawTunnelDataOnline();
                        return data;
                    }
                    catch (Exception ex)
                    {
                        Logger.Log("Error when downloading tunnel server info: " + ex.Message);
                        if (i < retryCount - 1)
                            Logger.Log("Retrying.");
                        else
                            Logger.Log("Fetching tunnel server list failed.");
                    }
                }
            }
            else
            {
                // Don't fetch the latest tunnel list if it is explicitly disabled
                // For example, the official CnCNet server might be unavailable/unstable in a country with Internet censorship,
                // where players might either establish a substitute server or manually distribute the tunnel cache file
                Logger.Log("Fetching tunnel server list online is disabled.");
            }

            if (OfflineTunnelDataAvailable)
            {
                Logger.Log("Using cached tunnel data.");
                byte[] data = GetRawTunnelDataOffline();
                return data;
            }
            else
                Logger.Log("Tunnel cache file doesn't exist!");

            return null;
        }

        /// <summary>
        /// Downloads and parses the list of CnCNet tunnels.
        /// </summary>
        /// <returns>A list of tunnel servers.</returns>
        private List<CnCNetTunnel> RefreshTunnels()
        {
            List<CnCNetTunnel> returnValue = new List<CnCNetTunnel>();

            FileInfo tunnelCacheFile = SafePath.GetFile(ProgramConstants.ClientUserFilesPath, "tunnel_cache");

            byte[] data = GetRawTunnelData();
            if (data is null)
                return returnValue;

            string convertedData = Encoding.Default.GetString(data);
            string[] serverList = convertedData.Split(new string[] { "\r\n", "\n" }, StringSplitOptions.RemoveEmptyEntries);

            // skip first header item ("address;country;countrycode;name;password;clients;maxclients;official;latitude;longitude;version;distance")
            foreach (string serverInfo in serverList.Skip(1))
            {
                try
                {
                    CnCNetTunnel tunnel = CnCNetTunnel.Parse(serverInfo);

                    if (tunnel == null)
                        continue;

                    if (tunnel.RequiresPassword)
                        continue;

                    if (!SUPPORTED_TUNNEL_VERSIONS.Contains(tunnel.Version))
                        continue;

                    returnValue.Add(tunnel);
                }
                catch (Exception ex)
                {
                    Logger.Log("Caught an exception when parsing a tunnel server: " + ex.ToString());
                }
            }

            if (returnValue.Count > 0)
            {
                try
                {
                    if (tunnelCacheFile.Exists)
                        tunnelCacheFile.Delete();

                    DirectoryInfo clientDirectoryInfo = SafePath.GetDirectory(ProgramConstants.ClientUserFilesPath);
                    if (!clientDirectoryInfo.Exists)
                        clientDirectoryInfo.Create();

                    File.WriteAllBytes(tunnelCacheFile.FullName, data);
                }
                catch (Exception ex)
                {
                    Logger.Log("Refreshing tunnel cache file failed! Returned error: " + ex.ToString());
                }
            }

            Logger.Log($"Successfully refreshed tunnel cache with {returnValue.Count} servers.");
            return returnValue;
        }

        public override void Update(GameTime gameTime)
        {
            if (timeSinceTunnelRefresh > TimeSpan.FromSeconds(CURRENT_TUNNEL_PING_INTERVAL))
            {
                if (skipCount % CYCLES_PER_TUNNEL_LIST_REFRESH == 0)
                {
                    skipCount = 0;
                    RefreshTunnelsAsync();
                }
                else if (CurrentTunnel != null)
                {
                    PingCurrentTunnelAsync(true);
                }

                timeSinceTunnelRefresh = TimeSpan.Zero;
                skipCount++;
            }
            else
                timeSinceTunnelRefresh += gameTime.ElapsedGameTime;

            base.Update(gameTime);
        }

        #region V3 Tunnel Communication Methods

        public V3GameTunnelBridge StartGameBridge(uint localId, int localPort, List<V3PlayerInfo> allPlayers)
        {
            if (GameTunnelBridge != null)
            {
                Logger.Log("TunnelHandler: Stopping existing bridge before starting new one");
                StopGameBridge();
            }

            Logger.Log($"TunnelHandler: Starting game bridge for localId={localId}, port={localPort}");

            GameTunnelBridge = new V3GameTunnelBridge(localId, localPort, allPlayers, this);
            GameTunnelBridge.Start();

            Logger.Log("TunnelHandler: Game bridge started successfully");

            return GameTunnelBridge;
        }

        public void StopGameBridge()
        {
            if (GameTunnelBridge != null)
            {
                Logger.Log("TunnelHandler: Stopping game bridge");

                var bridge = GameTunnelBridge;
                GameTunnelBridge = null;

                bridge.Stop();
                bridge.Dispose();

                Logger.Log("TunnelHandler: Game bridge stopped");
            }
        }

        public void InitializeV3Communicator()
        {
            if (!_v3Communicator.IsInitialized && Tunnels.Count > 0)
            {
                _v3Communicator.Initialize(Tunnels);
            }
        }

        public void RegisterV3PacketHandler(uint localId, uint remoteId, PacketHandler handler)
        {
            _v3Communicator.RegisterHandler(localId, remoteId, handler);
        }

        public void UnregisterV3PacketHandler(uint localId, uint remoteId)
        {
            _v3Communicator.UnregisterHandler(localId, remoteId);
        }

        public void SendRegistrationToAllTunnels(uint localId, List<CnCNetTunnel> tunnels = null)
        {
            _v3Communicator.SendRegistrationToAllTunnels(localId, tunnels);
        }

        public void SendPacket(CnCNetTunnel tunnel, uint senderId, uint receiverId,
            TunnelPacketType packetType, byte[] payload = null)
        {
            _v3Communicator.SendPacket(tunnel, senderId, receiverId, packetType, payload);
        }

        #endregion

        protected override void Dispose(bool disposing)
        {
            StopGameBridge();
            _v3Communicator?.Dispose();
        }

    }
}