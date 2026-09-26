using System.Collections.Generic;
using System.Linq;

using ClientLogic.Launch;

using DTAClient.Domain.Multiplayer;
using DTAClient.DXGUI;

using Rampastring.Tools;

namespace ClientLogic.Tests;

/// <summary>
/// The lobby state a launch is built from, with the same index conventions as the game lobby:
/// side 0 = random, 1..2 = random selectors, 3..6 = sides, 7 = spectator; colour 0 = random;
/// start 0 = random; team 0 = no team.
/// </summary>
internal sealed class TestLobby
{
    public const int SIDE_COUNT = 4;
    public const int RANDOM = 0;
    public const int FIRST_SIDE = 3;
    public const int SPECTATOR = 7;

    public static readonly List<int[]> RandomSelectors = [[0, 1], [2, 3]];

    public static readonly List<MultiplayerColor> Colors =
    [
        MultiplayerColor.CreateFromStringArray("Gold", ["255", "255", "0", "0"]),
        MultiplayerColor.CreateFromStringArray("Red", ["255", "0", "0", "1"]),
        MultiplayerColor.CreateFromStringArray("Blue", ["0", "0", "255", "2"]),
        MultiplayerColor.CreateFromStringArray("Green", ["0", "255", "0", "3"]),
        MultiplayerColor.CreateFromStringArray("Orange", ["255", "128", "0", "4"]),
        MultiplayerColor.CreateFromStringArray("Sky Blue", ["0", "200", "255", "5"]),
        MultiplayerColor.CreateFromStringArray("Purple", ["160", "0", "255", "6"]),
        MultiplayerColor.CreateFromStringArray("Pink", ["255", "0", "200", "7"]),
    ];

    public TestLobby(string mapPath, string gameModeName)
    {
        GameModeMap = TestGame.LoadGameModeMap(mapPath, gameModeName);
    }

    public GameModeMap GameModeMap { get; }

    public List<PlayerInfo> Players { get; } = [];

    public List<PlayerInfo> AIPlayers { get; } = [];

    public int Seed { get; set; } = 1234;

    public bool IsMultiplayer { get; set; }

    public bool RemoveStartingLocations { get; set; }

    public List<IGameSessionSetting> CheckBoxes { get; } =
    [
        FakeGameOption.CheckBox("chkShortGame", "ShortGame", true),
        new FakeGameOption("chkNoAlpha", "NoAlpha", ["no", "yes"], 1) { DisallowedSides = [0], MapCodeSection = "NoAlphaRules" },
    ];

    public List<IGameSessionSetting> DropDowns { get; } =
    [
        FakeGameOption.DropDown("ddCredits", "Credits", 2, "5000", "7500", "10000"),
    ];

    public List<TeamStartMapping> TeamStartMappings { get; set; } = [];

    public List<KeyValuePair<string, string>> ForcedSpawnIniOptions { get; } = [new("Forced", "Yes")];

    public IniFile SpawnIniAdditions { get; set; } = new();

    public TestLobby AddPlayer(string name, int side = RANDOM, int color = RANDOM, int start = RANDOM, int team = 0, int port = 0)
    {
        Players.Add(new PlayerInfo(name, side, start, color, team) { Port = port, IPAddress = $"10.0.0.{Players.Count + 1}" });
        return this;
    }

    public TestLobby AddAI(int aiLevel, int side = RANDOM, int color = RANDOM, int start = RANDOM, int team = 0)
    {
        AIPlayers.Add(new PlayerInfo(null, side, start, color, team) { IsAI = true, AILevel = aiLevel });
        return this;
    }

    public LaunchRequest CreateRequest(string localPlayerName) => new()
    {
        Players = Players,
        AIPlayers = AIPlayers,
        LocalPlayerName = localPlayerName,
        PlayerIPAddresses = Players.Select(p => p.Name == localPlayerName ? null : p.IPAddress).ToList(),
        RandomSeed = Seed,
        GameModeMap = GameModeMap,
        IsMultiplayer = IsMultiplayer,
        CheckBoxes = CheckBoxes,
        DropDowns = DropDowns,
        MPColors = Colors,
        SideCount = SIDE_COUNT,
        RandomSelectors = RandomSelectors,
        RandomSelectorCount = RandomSelectors.Count + 1,
        TeamStartMappings = TeamStartMappings,
        RemoveStartingLocations = RemoveStartingLocations,
        ForcedSpawnIniOptions = ForcedSpawnIniOptions,
        SpawnIniAdditions = SpawnIniAdditions,
        BroadcastedGameOptionValues = "1,2",
    };

    public LaunchArtifacts Build(string localPlayerName) =>
        GameLaunchBuilder.Build(CreateRequest(localPlayerName), TestGame.Root);
}
