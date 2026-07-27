using Rampastring.Tools;

using System;
using System.Collections.Generic;
using System.Globalization;
using System.Net;
using System.Net.NetworkInformation;

namespace DTAClient.Domain.Multiplayer.CnCNet
{
    /// <summary>
    /// A CnCNet tunnel server.
    /// </summary>
    /// <remarks>
    /// Equality is by <see cref="Address"/> and <see cref="Port"/>, not by reference. The same
    /// endpoint can be represented by more than one instance — a relay list refresh, or a
    /// renegotiation rebuilding its <see cref="P2PTunnel"/> candidates — and every consumer
    /// (routing maps, per-tunnel test results, keep-alive trackers) must treat those as one
    /// path. Both properties are immutable after construction, so instances are safe as
    /// dictionary keys.
    /// </remarks>
    public class CnCNetTunnel : IEquatable<CnCNetTunnel>
    {
        private const int REQUEST_TIMEOUT = 10000; // In milliseconds
        private const int PING_TIMEOUT = 1000;

        public CnCNetTunnel() { }

        protected CnCNetTunnel(string address, int port, string name, int version)
        {
            Address = address;
            Port = port;
            Name = name;
            Version = version;
            Official = false;
            Recommended = true;
        }

        /// <summary>
        /// Parses a formatted string that contains the tunnel server's 
        /// information into a CnCNetTunnel instance.
        /// </summary>
        /// <param name="str">The string that contains the tunnel server's information.</param>
        /// <returns>A CnCNetTunnel instance parsed from the given string.</returns>
        public static CnCNetTunnel Parse(string str)
        {
            // For the format, check http://cncnet.org/master-list

            try
            {
                var tunnel = new CnCNetTunnel();
                string[] parts = str.Split(';');

                string address = parts[0];
                string[] detailedAddress = address.Split(new char[] { ':' });

                tunnel.Address = detailedAddress[0];
                tunnel.Port = int.Parse(detailedAddress[1]);
                tunnel.Country = parts[1];
                tunnel.CountryCode = parts[2];
                tunnel.Name = parts[3];
                tunnel.RequiresPassword = parts[4] != "0";
                tunnel.Clients = int.Parse(parts[5]);
                tunnel.MaxClients = int.Parse(parts[6]);
                int status = int.Parse(parts[7]);
                tunnel.Official = status == 2;
                if (!tunnel.Official)
                    tunnel.Recommended = status == 1;

                CultureInfo cultureInfo = CultureInfo.InvariantCulture;

                tunnel.Latitude = double.Parse(parts[8], cultureInfo);
                tunnel.Longitude = double.Parse(parts[9], cultureInfo);
                tunnel.Version = int.Parse(parts[10]);
                tunnel.Distance = double.Parse(parts[11], cultureInfo);

                return tunnel;
            }
            catch (Exception ex)
            {
                if (ex is FormatException || ex is OverflowException || ex is IndexOutOfRangeException)
                {
                    Logger.Log("Parsing tunnel information failed: " + ex.ToString() + Environment.NewLine + "Parsed string: " + str);
                    return null;
                }

                throw;
            }
        }

        public string Address { get; private set; }
        public int Port { get; private set; }
        public string Country { get; private set; }
        public string CountryCode { get; private set; }
        public string Name { get; private set; }
        public bool RequiresPassword { get; private set; }
        public int Clients { get; private set; }
        public int MaxClients { get; private set; }
        public bool Official { get; private set; }
        public bool Recommended { get; private set; }
        public double Latitude { get; private set; }
        public double Longitude { get; private set; }
        public int Version { get; private set; }
        public double Distance { get; private set; }
        public PingValue Ping { get; set; } = PingValue.Unknown;

        /// <summary>
        /// How many ping attempts in a row have failed (unknown result or excessive latency).
        /// Maintained by TunnelHandler so a single dropped ICMP echo doesn't declare the
        /// tunnel failed and trigger renegotiations.
        /// </summary>
        internal int ConsecutivePingFailures { get; set; }

        /// <summary>
        /// Whether this tunnel has ever answered an ICMP echo. Unknown ping results only
        /// count as failures once this is true; some networks block ICMP entirely while
        /// UDP tunnel traffic works fine, and such tunnels must not be declared failed.
        /// </summary>
        internal bool HasRespondedToPing { get; set; }

        /// <summary>
        /// Whether this is a direct peer-to-peer path rather than a relay tunnel server.
        /// Relay tunnels are always false; <see cref="P2PTunnel"/> overrides this to true.
        /// Used to exclude synthetic P2P entries from relay-only operations (registration,
        /// endpoint mapping, ping refresh).
        /// </summary>
        public virtual bool IsDirect => false;

        /// <summary>
        /// Updates this tunnel's metadata from another tunnel instance, preserving Address, Port, and existing Ping.
        /// </summary>
        internal void UpdateFrom(CnCNetTunnel updatedTunnel)
        {
            Country = updatedTunnel.Country;
            CountryCode = updatedTunnel.CountryCode;
            Name = updatedTunnel.Name;
            Clients = updatedTunnel.Clients;
            MaxClients = updatedTunnel.MaxClients;
            Official = updatedTunnel.Official;
            Recommended = updatedTunnel.Recommended;
            Version = updatedTunnel.Version;

            RequiresPassword = updatedTunnel.RequiresPassword;
            Latitude = updatedTunnel.Latitude;
            Longitude = updatedTunnel.Longitude;
            Distance = updatedTunnel.Distance;
        }

        /// <summary>
        /// Gets a list of player ports to use from a specific V2 tunnel server.
        /// </summary>
        /// <returns>A list of player ports to use.</returns>
        public List<int> GetPlayerPortInfo(int playerCount)
        {
            try
            {
                Logger.Log($"Contacting tunnel at {Address}:{Port}");

                // Do not use https here as not supported by tunnels
                string addressString = $"http://{Address}:{Port}/request?clients={playerCount}";
                Logger.Log($"Downloading from {addressString}");

                string data = new TimedHttpClient(REQUEST_TIMEOUT).GetString(addressString);

                data = data.Replace("[", String.Empty);
                data = data.Replace("]", String.Empty);

                string[] portIDs = data.Split(',');
                List<int> playerPorts = new List<int>();

                foreach (string _port in portIDs)
                {
                    playerPorts.Add(Convert.ToInt32(_port));
                    Logger.Log($"Added port {_port}");
                }

                return playerPorts;
            }
            catch (Exception ex)
            {
                Logger.Log("Unable to connect to the specified tunnel server. Returned error message: " + ex.ToString());
            }

            return new List<int>();
        }

        public void UpdatePing()
        {
            using (Ping p = new Ping())
            {
                try
                {
                    PingReply reply = p.Send(IPAddress.Parse(Address), PING_TIMEOUT);
                    if (reply.Status == IPStatus.Success)
                        Ping = PingValue.FromMs(Convert.ToInt32(reply.RoundtripTime));
                    else
                        Ping = PingValue.Unknown;
                }
                catch (PingException ex)
                {
                    Logger.Log($"Caught an exception when pinging {Name} tunnel server: {ex.ToString()}");
                    Ping = PingValue.Unknown;
                }
            }
        }

        public bool Equals(CnCNetTunnel other)
        {
            if (other is null)
                return false;

            return Address == other.Address && Port == other.Port;
        }

        public override bool Equals(object obj) => Equals(obj as CnCNetTunnel);

        // Implemented with a ValueTuple rather than an anonymous type: SendPacket hashes the
        // tunnel to look up its endpoint for every outbound game packet, and an anonymous
        // type would allocate on that path.
        public override int GetHashCode() => (Address, Port).GetHashCode();

        public static bool operator ==(CnCNetTunnel left, CnCNetTunnel right)
        {
            if (left is null && right is null)
                return true;
            if (left is null || right is null)
                return false;

            return left.Equals(right);
        }

        public static bool operator !=(CnCNetTunnel left, CnCNetTunnel right)
        {
            return !(left == right);
        }
    }
}
