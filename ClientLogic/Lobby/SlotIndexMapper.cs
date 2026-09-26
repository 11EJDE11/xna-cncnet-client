namespace ClientLogic.Lobby;

/// <summary>
/// The meaning of the indices a player slot stores (<c>PlayerInfo.SideId</c>, <c>ColorId</c>,
/// <c>StartingLocation</c>) and of the slot's name drop-down items.
/// </summary>
/// <remarks>
/// Side indices: 0 = Random, 1 .. <see cref="RandomSelectorCount"/> - 1 = the custom random selectors, then the real
/// sides, then Spectator.
/// </remarks>
public sealed class SlotIndexMapper
{
    public const int RandomSide = 0;
    public const int RandomColor = 0;
    public const int RandomStart = 0;

    /// <summary>Name item of the first AI level; AI level n is item 1 + n.</summary>
    public const int FirstAiNameItem = 1;

    /// <summary>The number of AI levels (name items 1-3).</summary>
    public const int AiNameItemCount = 3;

    /// <summary>On a human player's row, name item 2 kicks the player.</summary>
    public const int KickNameItem = 2;

    /// <summary>On a human player's row, name item 3 bans the player.</summary>
    public const int BanNameItem = 3;

    /// <param name="sideCount">The number of real sides.</param>
    /// <param name="randomSelectorCount">The number of random selectors, including the plain Random.</param>
    public SlotIndexMapper(int sideCount, int randomSelectorCount)
    {
        SideCount = sideCount;
        RandomSelectorCount = randomSelectorCount;
    }

    public int SideCount { get; }

    public int RandomSelectorCount { get; }

    public int SpectatorSide => RandomSelectorCount + SideCount;

    /// <summary>The number of side items including Spectator.</summary>
    public int SideItemCount => SpectatorSide + 1;

    /// <summary>The side index of a custom random selector (0-based, without the plain Random).</summary>
    public static int RandomSelectorSide(int selectorIndex) => 1 + selectorIndex;

    /// <summary>The side index of a real side (0-based as in the side list).</summary>
    public int RealSide(int sideIndex) => sideIndex + RandomSelectorCount;

    public bool IsSpectator(int sideId) => sideId == SpectatorSide;

    /// <summary>The colour index of a multiplayer colour (0-based); 0 is Random.</summary>
    public static int ColorId(int mpColorIndex) => mpColorIndex + 1;

    public static int AiLevelToNameItem(int aiLevel) => FirstAiNameItem + aiLevel;
}
