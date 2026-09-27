using System;

using ClientCore;

using ClientLogic.Lobby;
using ClientLogic.Protocol;

using Rampastring.Tools;

namespace DTAClient.Domain.Multiplayer.LAN
{
    /// <summary>
    /// Sends a LAN game lobby's messages. Players send everything to the game host, which relays it; the host
    /// broadcasts to every player.
    /// </summary>
    public sealed class LanLobbySession : ILobbySession
    {
        public const string CHAT_COMMAND = "GLCHAT";
        public const string PLAYER_OPTIONS_REQUEST_COMMAND = "POREQ";
        public const string PLAYER_OPTIONS_BROADCAST_COMMAND = "POPTS";
        public const string GAME_OPTIONS_COMMAND = "OPTS";
        public const string PLAYER_READY_REQUEST = "READY";
        public const string DICE_ROLL_COMMAND = "DR";

        private readonly Action<string> sendToHost;
        private readonly Action<string, bool> broadcast;
        private readonly Func<int> chatColorIndex;

        /// <param name="sendToHost">Sends a message to the game host.</param>
        /// <param name="broadcast">Game host: sends a message to all players (or, if true, all but itself).</param>
        /// <param name="chatColorIndex">The local player's chat colour.</param>
        public LanLobbySession(Action<string> sendToHost, Action<string, bool> broadcast, Func<int> chatColorIndex)
        {
            this.sendToHost = sendToHost;
            this.broadcast = broadcast;
            this.chatColorIndex = chatColorIndex;
        }

        public void SendChatMessage(string message)
        {
            var sb = new ExtendedStringBuilder(CHAT_COMMAND + " ", true);
            sb.Separator = ProgramConstants.LAN_DATA_SEPARATOR;
            sb.Append(chatColorIndex());
            sb.Append(message);
            sendToHost(sb.ToString());
        }

        public void SendDiceRoll(int dieSides, int[] results) =>
            sendToHost($"{DICE_ROLL_COMMAND} {dieSides},{string.Join(",", results)}");

        public void RequestPlayerOptions(PackedPlayerOptions options) =>
            sendToHost(PLAYER_OPTIONS_REQUEST_COMMAND + " " + new PlayerOptionsRequestMessage(options).Encode());

        public void RequestReady(int readyState) =>
            sendToHost(PLAYER_READY_REQUEST + " " + new ReadyRequestMessage(readyState).Encode());

        public void SendPlayerOptions(PlayerOptionsMessage message) =>
            broadcast(PLAYER_OPTIONS_BROADCAST_COMMAND + " " + message.Encode(), false);

        public void SendPlayerExtraOptions(PlayerExtraOptions options) =>
            broadcast(options.ToLanMessage(), true);

        public void SendGameOptions(GameOptionsMessage message) =>
            broadcast(GAME_OPTIONS_COMMAND + " " + message.Encode(), false);
    }
}
