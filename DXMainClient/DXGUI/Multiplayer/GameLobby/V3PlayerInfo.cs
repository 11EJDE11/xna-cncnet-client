namespace DTAClient.DXGUI.Multiplayer.GameLobby
{
    public class V3PlayerInfo
    {
        public uint Id { get; set; }
        public string Name { get; set; }
        public string IpAddress { get; set; }
        public int Port { get; set; }
        public int PlayerIndex { get; set; }
        public ushort PlayerGameId { get; set; }
        public V3PlayerInfo(uint id, string name, string ipAddress, int port, int playerIndex, ushort playerGameID)
        {
            Id = id;
            Name = name;
            IpAddress = ipAddress;
            Port = port;
            PlayerIndex = playerIndex;
            PlayerGameId = playerGameID;
        }
    }
}
