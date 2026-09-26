using System;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;

using ClientLogic.Launch;

using DTAClient.Domain.Multiplayer;

using Rampastring.Tools;

using Xunit;

namespace ClientLogic.Tests;

/// <summary>
/// Builds fixed lobbies and compares spawn.ini and spawnmap.ini byte for byte with the files in
/// Expected/&lt;case&gt;. Any difference changes the game that is launched, and between client
/// versions (or .NET Framework and .NET 8) it desyncs multiplayer games.
/// </summary>
/// <remarks>
/// If a change to the output is intended, set CLIENTLOGIC_UPDATE_EXPECTED=1, run the tests once
/// to rewrite the files, and review the diff.
/// </remarks>
public class GameLaunchBuilderExpectedOutputTests
{
    private const string UPDATE_VARIABLE = "CLIENTLOGIC_UPDATE_EXPECTED";

    [Fact]
    public void RandomSidesColorsAndStarts()
    {
        var lobby = new TestLobby("Maps/Test/four", "Battle")
            .AddPlayer("Alice")
            .AddPlayer("Bob", side: 1, color: 3, team: 1)
            .AddAI(2)
            .AddAI(0, side: TestLobby.FIRST_SIDE + 2, start: 4);

        AssertMatchesExpected(nameof(RandomSidesColorsAndStarts), lobby.Build("Alice"));
    }

    [Fact]
    public void RandomizedMapCodeAndComputerSideRestrictions()
    {
        var lobby = new TestLobby("Maps/Test/four", "Random Rules")
            .AddPlayer("Alice")
            .AddAI(1)
            .AddAI(1)
            .AddAI(2);

        AssertMatchesExpected(nameof(RandomizedMapCodeAndComputerSideRestrictions), lobby.Build("Alice"));
    }

    [Fact]
    public void CoopMapWithRandomEnemySides()
    {
        var lobby = new TestLobby("Maps/Test/coop", "Cooperative")
            .AddPlayer("Alice", team: 1)
            .AddPlayer("Bob", team: 1);

        AssertMatchesExpected(nameof(CoopMapWithRandomEnemySides), lobby.Build("Alice"));
    }

    [Fact]
    public void SpectatorAndStackedStartLocations()
    {
        // The open map doesn't enforce its player limit, so players can share a start location.
        var lobby = new TestLobby("Maps/Test/open", "Battle")
            .AddPlayer("Alice", side: TestLobby.SPECTATOR)
            .AddPlayer("Bob", start: 2)
            .AddAI(2, start: 2)
            .AddAI(2, start: 2)
            .AddAI(1, start: 3);

        AssertMatchesExpected(nameof(SpectatorAndStackedStartLocations), lobby.Build("Alice"));
    }

    [Fact]
    public void RemoveStartingLocations()
    {
        var lobby = new TestLobby("Maps/Test/open", "Battle")
            {
                RemoveStartingLocations = true,
            }
            .AddPlayer("Alice", start: 1)
            .AddAI(2, start: 1);

        AssertMatchesExpected(nameof(RemoveStartingLocations), lobby.Build("Alice"));
    }

    [Fact]
    public void TeamStartMappings()
    {
        var lobby = new TestLobby("Maps/Test/four", "Battle")
            {
                TeamStartMappings = TeamStartMapping.FromListString("A,x,B,-"),
            }
            .AddPlayer("Alice", team: 1)
            .AddPlayer("Bob", team: 2)
            .AddAI(2, team: 1)
            .AddAI(2);

        AssertMatchesExpected(nameof(TeamStartMappings), lobby.Build("Alice"));
    }

    [Fact]
    public void MultiplayerLobbyAdditions()
    {
        var additions = new IniFile();
        additions.SetIntValue("Settings", "FrameSendRate", 7);
        additions.SetIntValue("Settings", "Protocol", 2);
        additions.SetStringValue("Tunnel", "Ip", "127.0.0.1");
        additions.SetIntValue("Tunnel", "Port", 50000);
        additions.SetIntValue("Settings", "GameID", 4242);
        additions.SetBooleanValue("Settings", "Host", true);

        var lobby = new TestLobby("Maps/Test/four", "Battle")
            {
                IsMultiplayer = true,
                SpawnIniAdditions = additions,
            }
            .AddPlayer("Alice", port: 1000)
            .AddPlayer("Bob", port: 1001)
            .AddPlayer("Carol", side: TestLobby.SPECTATOR, port: 1002);

        AssertMatchesExpected(nameof(MultiplayerLobbyAdditions), lobby.Build("Bob"));
    }

    [Fact]
    public void SupplementalMapFiles()
    {
        var lobby = new TestLobby("Maps/Test/supplemental", "Battle")
            .AddPlayer("Alice")
            .AddAI(1);

        LaunchArtifacts launch = lobby.Build("Alice");

        SupplementalMapFile file = Assert.Single(launch.SupplementalMapFiles);
        Assert.Equal("spawnmap.bin", file.TargetFileName);
        Assert.Equal(Path.Combine(TestGame.Root, "Maps", "Test", "supplemental.bin"), file.SourcePath, ignoreCase: true);
        AssertMatchesExpected(nameof(SupplementalMapFiles), launch);
    }

    [Fact]
    public void BuildingTwiceGivesTheSameFiles()
    {
        var lobby = new TestLobby("Maps/Test/four", "Random Rules")
            .AddPlayer("Alice")
            .AddAI(2)
            .AddAI(2);

        LaunchArtifacts first = lobby.Build("Alice");
        LaunchArtifacts second = lobby.Build("Alice");

        Assert.Equal(TestGame.ToText(first.SpawnIni), TestGame.ToText(second.SpawnIni));
        Assert.Equal(TestGame.ToText(first.MapIni), TestGame.ToText(second.MapIni));
    }

    [Fact]
    public void DifferentSeedsGiveDifferentGames()
    {
        var lobby = new TestLobby("Maps/Test/four", "Random Rules")
            .AddPlayer("Alice")
            .AddAI(2)
            .AddAI(2);

        string first = TestGame.ToText(lobby.Build("Alice").SpawnIni);
        lobby.Seed++;
        string second = TestGame.ToText(lobby.Build("Alice").SpawnIni);

        Assert.NotEqual(first, second);
    }

    private static void AssertMatchesExpected(string caseName, LaunchArtifacts launch, [CallerFilePath] string sourceFile = "")
    {
        string expectedDirectory = Path.Combine(Path.GetDirectoryName(sourceFile), "Expected", caseName);

        Compare(Path.Combine(expectedDirectory, "spawn.ini"), TestGame.ToText(launch.SpawnIni));
        Compare(Path.Combine(expectedDirectory, "spawnmap.ini"), TestGame.ToText(launch.MapIni));
    }

    private static void Compare(string expectedFile, string actual)
    {
        if (Environment.GetEnvironmentVariable(UPDATE_VARIABLE) == "1")
        {
            Directory.CreateDirectory(Path.GetDirectoryName(expectedFile));
            File.WriteAllText(expectedFile, actual);
            return;
        }

        Assert.True(File.Exists(expectedFile), $"{expectedFile} is missing. Run the tests with {UPDATE_VARIABLE}=1 to create it.");

        // Git may check the file out with other line endings; the builder always writes CRLF.
        string expected = File.ReadAllText(expectedFile).Replace("\r\n", "\n").Replace("\n", "\r\n");

        if (expected != actual)
        {
            string[] expectedLines = expected.Split('\n');
            string[] actualLines = actual.Split('\n');
            int line = 0;
            while (line < expectedLines.Length && line < actualLines.Length && expectedLines[line] == actualLines[line])
                line++;

            string expectedLine = line < expectedLines.Length ? expectedLines[line].TrimEnd('\r') : "<end of file>";
            string actualLine = line < actualLines.Length ? actualLines[line].TrimEnd('\r') : "<end of file>";
            Assert.Fail($"{expectedFile} differs at line {line + 1}.\nExpected: {expectedLine}\nActual:   {actualLine}");
        }
    }
}
