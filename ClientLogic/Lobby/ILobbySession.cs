using ClientLogic.Protocol;

using DTAClient.Domain.Multiplayer;

namespace ClientLogic.Lobby;

/// <summary>
/// Sends a multiplayer game lobby's messages over its network (a CnCNet channel or a LAN connection).
/// The lobby builds the messages; the session only knows how each network carries them.
/// </summary>
public interface ILobbySession
{
    void SendChatMessage(string message);

    /// <summary>Sends a dice roll to the other players.</summary>
    void SendDiceRoll(int dieSides, int[] results);

    /// <summary>Asks the game host to change the local player's options.</summary>
    void RequestPlayerOptions(PackedPlayerOptions options);

    /// <summary>Tells the game host the local player's ready state (0 = not ready, 1 = ready, 2 = auto-ready).</summary>
    void RequestReady(int readyState);

    /// <summary>Game host: sends the players' options.</summary>
    void SendPlayerOptions(PlayerOptionsMessage message);

    /// <summary>Game host: sends the extra player options.</summary>
    void SendPlayerExtraOptions(PlayerExtraOptions options);

    /// <summary>Game host: sends the game options.</summary>
    void SendGameOptions(GameOptionsMessage message);
}
