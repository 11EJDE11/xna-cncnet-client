using System.Collections.Generic;
using System.Linq;

using ClientLogic.Lobby;

using DTAClient.Domain.Multiplayer;

using Xunit;

namespace ClientLogic.Tests.Lobby;

public class MapSearchTests
{
    private static readonly List<GameModeMap> Maps =
    [
        TestGame.LoadGameModeMap("Maps/Test/four", "Battle"),       // [4] Four Corners
        TestGame.LoadGameModeMap("Maps/Test/open", "Battle"),       // [4] Open Field
        TestGame.LoadGameModeMap("Maps/Test/supplemental", "Battle"), // [2] Supplemental
    ];

    private static List<string> Names(string search) => MapSearch.Filter(Maps, search).Select(m => m.Map.Name).ToList();

    [Fact]
    public void AnEmptySearchKeepsEveryMap() => Assert.Equal(3, Names("  ").Count);

    [Fact]
    public void SubstringsMatchIgnoringCase() => Assert.Equal(["[4] Open Field"], Names("open"));

    [Fact]
    public void ExactMatchesComeBeforeSubstringMatches()
    {
        var maps = new List<GameModeMap> { Maps[1], Maps[0] };
        Assert.Equal("[4] Four Corners", MapSearch.Filter(maps, "[4] Four Corners").First().Map.Name);
    }

    [Fact]
    public void EveryWordMustMatch()
    {
        Assert.Equal(["[4] Four Corners"], Names("corners four"));
        Assert.Empty(Names("four field"));
    }
}
