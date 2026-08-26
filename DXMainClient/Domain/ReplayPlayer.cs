#nullable enable

namespace DTAClient.Domain;

/// <summary>
/// One human player of a recorded game, as the replay's embedded spawn.ini describes them.
/// AI players have no spawn.ini section of their own and so are not represented here.
/// </summary>
public sealed class ReplayPlayer
{
    public ReplayPlayer(int spawnIniIndex, string name, int sideIndex, bool isSpectator)
    {
        SpawnIniIndex = spawnIniIndex;
        Name = name;
        SideIndex = sideIndex;
        IsSpectator = isSpectator;
    }

    /// <summary>
    /// The player's slot in spawn.ini: 0 is <c>[Settings]</c>, N is <c>[OtherN]</c>. This is the
    /// index the spawner's <c>ReplayViewPlayer</c> key takes.
    /// </summary>
    public int SpawnIniIndex { get; }

    public string Name { get; }

    /// <summary>
    /// The player's spawn.ini <c>Side</c>, used to pick their loading screen. -1 when the replay
    /// records no side for them, which includes spectators.
    /// </summary>
    public int SideIndex { get; }

    public bool IsSpectator { get; }

    /// <summary>
    /// True for the player whose game produced the recording. Always slot 0: the recording client
    /// embeds its own spawn.ini, in which it is <c>[Settings]</c>.
    /// </summary>
    public bool IsRecorder => SpawnIniIndex == 0;
}
