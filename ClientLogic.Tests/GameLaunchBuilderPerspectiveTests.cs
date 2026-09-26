using System.Collections.Generic;
using System.Linq;

using ClientLogic.Launch;

using Rampastring.Tools;

using Xunit;

namespace ClientLogic.Tests;

/// <summary>
/// Every player's client builds the launch from the same lobby. Everything but the local player's own
/// details must come out the same for all of them, or the game desyncs.
/// </summary>
public class GameLaunchBuilderPerspectiveTests
{
    /// <summary>[Settings] keys that describe the local player, not the game.</summary>
    private static readonly HashSet<string> localSettingsKeys = ["Name", "Side", "IsSpectator", "Color", "CustomLoadScreen"];

    public static IEnumerable<object[]> Lobbies()
    {
        for (int seed = 1; seed <= 40; seed++)
        {
            yield return ["four/Battle", seed];
            yield return ["four/Random Rules", seed];
            yield return ["coop/Cooperative", seed];
            yield return ["open/Battle", seed];
        }
    }

    [Theory]
    [MemberData(nameof(Lobbies))]
    public void EveryPlayerBuildsTheSameGame(string mapAndMode, int seed)
    {
        TestLobby lobby = CreateLobby(mapAndMode, seed);

        List<(string Player, LaunchArtifacts Launch)> launches = lobby.Players
            .Where(p => !p.IsAI)
            .Select(p => (p.Name, lobby.Build(p.Name)))
            .ToList();

        LaunchArtifacts first = launches[0].Launch;

        foreach (LaunchArtifacts launch in launches.Skip(1).Select(l => l.Launch))
        {
            Assert.Equal(TestGame.ToText(first.MapIni), TestGame.ToText(launch.MapIni));
            Assert.Equal(SharedSpawnIni(first.SpawnIni), SharedSpawnIni(launch.SpawnIni));
            Assert.Equal(AllPlayers(first.SpawnIni), AllPlayers(launch.SpawnIni));
        }

        Assert.Equal(lobby.Players.Count, launches.Count);
    }

    private static TestLobby CreateLobby(string mapAndMode, int seed)
    {
        string[] parts = mapAndMode.Split('/');
        var lobby = new TestLobby("Maps/Test/" + parts[0], parts[1]) { Seed = seed, IsMultiplayer = true };

        if (parts[0] == "coop")
        {
            lobby.AddPlayer("Alice", team: 1, port: 1000)
                .AddPlayer("Bob", team: 1, port: 1001);
            return lobby;
        }

        lobby.AddPlayer("Alice", port: 1000)
            .AddPlayer("Bob", side: 1, team: 1, port: 1001)
            .AddPlayer("Carol", color: 5, team: 1, port: 1002)
            .AddAI(2);

        if (parts[0] == "open")
        {
            // Stacked start locations
            lobby.Players[1].StartingLocation = 3;
            lobby.Players[2].StartingLocation = 3;
            lobby.AddAI(1, start: 3);
            lobby.AddPlayer("Dave", side: TestLobby.SPECTATOR, port: 1003);
        }

        return lobby;
    }

    /// <summary>spawn.ini without the local player's details and without the [OtherN] sections.</summary>
    private static string SharedSpawnIni(IniFile spawnIni)
    {
        var lines = new List<string>();
        foreach (string sectionName in spawnIni.GetSections().Where(s => !s.StartsWith("Other")))
        {
            lines.Add($"[{sectionName}]");
            foreach (KeyValuePair<string, string> kvp in spawnIni.GetSection(sectionName).Keys)
            {
                if (sectionName == "Settings" && localSettingsKeys.Contains(kvp.Key))
                    continue;

                lines.Add($"{kvp.Key}={kvp.Value}");
            }
        }

        return string.Join("\n", lines);
    }

    /// <summary>Name, side, colour and spectator status of every human player, from the local and [OtherN] sections.</summary>
    private static string AllPlayers(IniFile spawnIni)
    {
        var players = new List<string> { Describe(spawnIni.GetSection("Settings")) };
        players.AddRange(spawnIni.GetSections().Where(s => s.StartsWith("Other")).Select(s => Describe(spawnIni.GetSection(s))));
        players.Sort();
        return string.Join("\n", players);

        static string Describe(IniSection section) =>
            string.Join(",", section.GetStringValue("Name", ""), section.GetStringValue("Side", ""),
                section.GetStringValue("Color", ""), section.GetStringValue("IsSpectator", ""));
    }
}
