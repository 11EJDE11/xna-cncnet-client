using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using ClientCore;
using ClientLogic.Campaign;
using ClientLogic.Options;
using DTAClient.Domain;
using Rampastring.Tools;
using Xunit;

namespace ClientLogic.Tests.Campaign;

// These integration tests write spawn files in the synthetic test installation and change singleton settings.
[CollectionDefinition("Campaign files", DisableParallelization = true)]
public sealed class CampaignFileCollection { }

[Collection("Campaign files")]
public sealed class CampaignTests : IDisposable
{
    private readonly Dictionary<string, byte[]> originalFiles = [];
    private readonly int speed;
    private readonly int difficulty;

    public CampaignTests()
    {
        TestGame.EnsureInitialized();
        CustomMissionHelper.Initialize();
        speed = UserINISettings.Instance.GameSpeed;
        difficulty = UserINISettings.Instance.Difficulty;
        Remember(ProgramConstants.SPAWNER_SETTINGS);
        Remember("spawnmap.ini");
    }

    private string Remember(string relative)
    {
        string path = Path.Combine(TestGame.Root, relative);
        if (!originalFiles.ContainsKey(path))
            originalFiles[path] = File.Exists(path) ? File.ReadAllBytes(path) : null;
        return path;
    }

    private void Write(string relative, string contents)
    {
        string path = Remember(relative);
        Directory.CreateDirectory(Path.GetDirectoryName(path));
        File.WriteAllText(path, contents);
    }

    public void Dispose()
    {
        UserINISettings.Instance.GameSpeed.Value = speed;
        UserINISettings.Instance.Difficulty.Value = difficulty;
        foreach (var file in originalFiles)
        {
            if (file.Value == null)
                File.Delete(file.Key);
            else
                File.WriteAllBytes(file.Key, file.Value);
        }
    }

    [Fact]
    public void CatalogKeepsOrderDisablesDuplicateIdsAndFiltersLikeXna()
    {
        Write("INI/Battle.ini", """
            [Battles]
            review.map=Official
            heading=Heading
            [Official]
            Scenario=review.map
            Description=Official
            Tags=ALLIED,RA2
            [Heading]
            Description=Heading
            """);
        Write("Maps/CustomMissions/review.map", "[ClientMissionConfig]\nDescription=Duplicate\n");
        Write("Maps/CustomMissions/review-other.map", "[ClientMissionConfig]\nDescription=Custom\n");
        var catalog = CampaignCatalog.Load();
        Assert.Equal(new[] { "review.map", "heading" }, catalog.AllMissions.Take(2).Select(m => m.CodeName));
        var duplicate = catalog.AllMissions.Single(m => m.IsCustomMission && m.CodeName == "review.map");
        Assert.False(duplicate.Enabled);
        Assert.Same(catalog.AllMissions.First(), catalog.UniqueIDToMissions[duplicate.CustomMissionID]);
        Assert.Equal(2, catalog.Filter(null).Count);
        Assert.Equal(2, catalog.Filter(null, false, true).Count);
        Assert.Equal(4, catalog.Filter(null, true, true).Count); // XNA's both-disabled case intentionally shows all.
        Assert.Single(catalog.Filter(new HashSet<string> { "ALLIED", "RA2" }, false, false));
        Assert.Equal(2, catalog.Filter(new HashSet<string> { "CUSTOM" }, false, false).Count);
        Assert.Empty(catalog.Filter(new HashSet<string>(), false, false));
    }

    [Fact]
    public void EmptyBattleListFallsBackToBattleFs()
    {
        Write("INI/Battle.ini", "[Battles]\n0=MissingSection\n");
        Write("INI/" + ClientConfiguration.Instance.BattleFSFileName,
            "[Battles]\nreview=Mission\n[Mission]\nDescription=Fallback\nScenario=review.map\n");
        Assert.Equal("Fallback", CampaignCatalog.Load().AllMissions.First().UntranslatedGUIName);
    }

    private static GameOption Option(string name, GameOptionKind kind, int value, params (string Key, string Value)[] attributes)
    {
        var builder = new GameOptionDefinitionBuilder(name, kind, false, (_, fallback) => fallback);
        foreach (var attribute in attributes)
            builder.TryParse(new IniFile(), attribute.Key, attribute.Value);
        return new GameOption(builder.Build(), value);
    }

    [Theory]
    [InlineData(0, 2)]
    [InlineData(1, 1)]
    [InlineData(2, 0)]
    public void LaunchAppliesDifficultyThenControlsThenForcedSpawnOptions(int difficulty, int computerDifficulty)
    {
        Write("Maps/review-campaign.map", "[Basic]\nName=Review\n[Rules]\nValue=Original\n[GameMissionConfig]\nBriefing=Briefing text\n");
        Write("INI/Map Code/Difficulty " + CampaignLauncher.DifficultyNames[difficulty] + ".ini", "[Rules]\nValue=Difficulty\nOnlyDifficulty=yes\n");
        Write("INI/Map Code/review-campaign-option.ini", "[Rules]\nValue=Option\n");
        var section = new IniSection("Mission");
        section.AddKey("Scenario", "Maps/review-campaign.map");
        section.AddKey("Side", "1");
        var mission = new Mission(section, "review");
        var checkBoxes = new[]
        {
            Option("chk", GameOptionKind.CheckBox, 1, ("SpawnIniOption", "TestOption")),
            Option("map", GameOptionKind.CheckBox, 1, ("CustomIniPath", "INI/Map Code/review-campaign-option.ini")),
        };
        var dropDowns = new[] { Option("dd", GameOptionKind.DropDown, 1, ("Items", "5000,10000"), ("DataWriteMode", "String"), ("SpawnIniOption", "Credits")) };
        var optionsIni = new IniFile();
        optionsIni.SetStringValue("CampaignForcedSpawnIniOptions", "TestOption", "Forced");
        UserINISettings.Instance.GameSpeed.Value = 0;
        Assert.Equal(CampaignLauncher.DifficultyNames[difficulty], CampaignLauncher.WriteSpawnFiles(mission, difficulty, checkBoxes, dropDowns, optionsIni));
        var spawn = new IniFile(Path.Combine(TestGame.Root, ProgramConstants.SPAWNER_SETTINGS));
        Assert.Equal("spawnmap.ini", spawn.GetStringValue("Settings", "Scenario", null));
        Assert.Equal(1, spawn.GetIntValue("Settings", "GameSpeed", -1));
        Assert.Equal(difficulty, spawn.GetIntValue("Settings", "DifficultyModeHuman", -1));
        Assert.Equal(computerDifficulty, spawn.GetIntValue("Settings", "DifficultyModeComputer", -1));
        Assert.Equal("Forced", spawn.GetStringValue("Settings", "TestOption", null));
        Assert.Equal(10000, spawn.GetIntValue("Settings", "Credits", -1));
        Assert.Equal("Yes", spawn.GetStringValue("Settings", "ReadMissionSection", null));
        Assert.Equal("Briefing text", spawn.GetStringValue(mission.Scenario, "Briefing", null));
        var map = new IniFile(Path.Combine(TestGame.Root, "spawnmap.ini"));
        Assert.Equal("Option", map.GetStringValue("Rules", "Value", null));
        Assert.Equal("yes", map.GetStringValue("Rules", "OnlyDifficulty", null));
    }

    [Fact]
    public void SpecialScenarioPassesThroughWithoutReplacingTheSpawnMap()
    {
        Write("spawnmap.ini", "previous map");
        var section = new IniSection("Mission");
        section.AddKey("Scenario", "<special>");
        section.AddKey("PlayerAlwaysOnNormalDifficulty", "yes");
        CampaignLauncher.WriteSpawnFiles(new Mission(section, "special"), 2, [], [], new IniFile());
        var spawn = new IniFile(Path.Combine(TestGame.Root, ProgramConstants.SPAWNER_SETTINGS));
        Assert.Equal("<special>", spawn.GetStringValue("Settings", "Scenario", null));
        Assert.Equal(1, spawn.GetIntValue("Settings", "DifficultyModeHuman", -1));
        Assert.False(spawn.KeyExists("Settings", "ReadMissionSection"));
        Assert.Equal("previous map", File.ReadAllText(Path.Combine(TestGame.Root, "spawnmap.ini")));
    }
}
