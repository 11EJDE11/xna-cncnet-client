using System;

using ClientLogic.Lobby;
using ClientLogic.Protocol;

using DTAClient.Domain.Multiplayer;

namespace DTAClient.Online
{
    /// <summary>
    /// Sends a CnCNet game lobby's messages: chat as channel messages, everything else as CTCP messages to the
    /// game channel.
    /// </summary>
    public sealed class CnCNetLobbySession : ILobbySession
    {
        public const string DICE_ROLL_MESSAGE = "DR";

        private readonly Func<Channel> channel;
        private readonly Func<IRCColor> chatColor;

        /// <param name="channel">The current game channel.</param>
        /// <param name="chatColor">The local player's chat colour.</param>
        public CnCNetLobbySession(Func<Channel> channel, Func<IRCColor> chatColor)
        {
            this.channel = channel;
            this.chatColor = chatColor;
        }

        public void SendChatMessage(string message) => channel().SendChatMessage(message, chatColor());

        public void SendDiceRoll(int dieSides, int[] results) =>
            channel().SendCTCPMessage($"{DICE_ROLL_MESSAGE} {dieSides},{string.Join(",", results)}", QueuedMessageType.CHAT_MESSAGE, 0);

        public void RequestPlayerOptions(PackedPlayerOptions options) =>
            channel().SendCTCPMessage("OR " + new PlayerOptionsRequestMessage(options).Encode(), QueuedMessageType.GAME_SETTINGS_MESSAGE, 6);

        public void RequestReady(int readyState) =>
            channel().SendCTCPMessage("R " + new ReadyRequestMessage(readyState).Encode(), QueuedMessageType.GAME_PLAYERS_READY_STATUS_MESSAGE, 5);

        public void SendPlayerOptions(PlayerOptionsMessage message) =>
            channel().SendCTCPMessage("PO " + message.Encode(), QueuedMessageType.GAME_PLAYERS_MESSAGE, 11);

        public void SendPlayerExtraOptions(PlayerExtraOptions options) =>
            channel().SendCTCPMessage(options.ToCncnetMessage(), QueuedMessageType.GAME_PLAYERS_EXTRA_MESSAGE, 11, true);

        public void SendGameOptions(GameOptionsMessage message) =>
            channel().SendCTCPMessage("GO " + message.Encode(), QueuedMessageType.GAME_SETTINGS_MESSAGE, 11);
    }
}
