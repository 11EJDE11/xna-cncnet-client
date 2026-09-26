namespace ClientLogic.UI;

/// <summary>A sound a game lobby plays.</summary>
public enum LobbySound
{
    PlayerJoined,
    PlayerLeft,
    Message,

    /// <summary>The host wants to start but the local player isn't ready.</summary>
    GetReady,

    /// <summary>A player came back from a game.</summary>
    PlayerReturned,
}

/// <summary>Plays the lobby sounds on the front end.</summary>
public interface ISoundService
{
    void Play(LobbySound sound);

    /// <summary>Turns a sound on or off (e.g. chat messages while the game runs).</summary>
    void SetEnabled(LobbySound sound, bool enabled);
}
