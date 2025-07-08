using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace DTAClient.DXGUI.Multiplayer.GameLobby
{
    public class V3PlayerInfo
    {
        public uint Id { get; set; }
        public string Name { get; set; }
        public string IpAddress { get; set; }
        public int Port { get; set; }
        public int PlayerIndex { get; set; }
        public V3PlayerInfo(uint id, string name, string ipAddress, int port, int playerIndex)
        {
            Id = id;
            Name = name;
            IpAddress = ipAddress;
            Port = port;
            PlayerIndex = playerIndex;
        }
    }
}
