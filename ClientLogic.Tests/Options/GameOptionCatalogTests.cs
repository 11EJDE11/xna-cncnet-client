using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;

using ClientCore;

using ClientLogic.Options;

using DTAClient.DXGUI.Generic;

using Rampastring.Tools;

using Xunit;

namespace ClientLogic.Tests.Options;

/// <summary>
/// Reads the game options of the default theme shipped in DXMainClient/Resources. (That the catalog matches the
/// options the XNA controls register is checked in the client with CNCNET_CHECK_OPTION_CATALOG=1.)
/// </summary>
public class GameOptionCatalogTests
{
    private static string ThemeDirectory([CallerFilePath] string sourceFile = "") =>
        Path.GetFullPath(Path.Combine(Path.GetDirectoryName(sourceFile), "..", "..", "DXMainClient", "Resources", "DTA"));

    private static List<GameOptionDefinition> Load(string windowName)
    {
        TestGame.EnsureInitialized();
        var ini = new CCIniFile(Path.Combine(ThemeDirectory(), windowName + ".ini"));
        return GameOptionCatalog.Load(ini, windowName);
    }

    [Fact]
    public void DefaultThemeOptionsAreRead()
    {
        // MultiplayerGameLobby.ini is based on GameLobbyBase.ini, which defines the options
        List<GameOptionDefinition> options = Load("MultiplayerGameLobby");

        Assert.Equal(26, options.Count);
        Assert.Equal(options.Count, options.Select(o => o.Name).Distinct().Count());
        Assert.All(options.Where(o => o.Kind == GameOptionKind.DropDown), o => Assert.NotEmpty(o.Items));
        Assert.All(options.Where(o => o.Kind == GameOptionKind.CheckBox), o => Assert.False(string.IsNullOrEmpty(o.Label)));
    }

    [Fact]
    public void KeysAreParsedLikeTheControls()
    {
        var ini = new IniFile();
        ini.SetStringValue("Lobby", "$CC0", "panel:XNAPanel");
        ini.SetStringValue("panel", "$CC0", "chkCrates:GameLobbyCheckBox");
        ini.SetStringValue("panel", "$CC1", "ddCredits:GameLobbyDropDown");
        ini.SetStringValue("chkCrates", "Text", "Crates");
        ini.SetStringValue("chkCrates", "SpawnIniOption", "Crates");
        ini.SetStringValue("chkCrates", "Checked", "yes");
        ini.SetStringValue("chkCrates", "Reversed", "yes");
        ini.SetStringValue("chkCrates", "MapScoringMode", "DenyWhenChecked");
        ini.SetStringValue("chkCrates", "DisallowedSideIndices", "2,3,2");
        ini.SetStringValue("ddCredits", "Items", "5000,10000");
        ini.SetStringValue("ddCredits", "ItemLabels", "Low,");
        ini.SetStringValue("ddCredits", "DataWriteMode", "String");
        ini.SetStringValue("ddCredits", "SpawnIniOption", "Credits");
        ini.SetStringValue("ddCredits", "DefaultIndex", "1");
        ini.SetStringValue("ddCredits", "OptionName", "Credits");
        TestGame.EnsureInitialized();

        List<GameOptionDefinition> options = GameOptionCatalog.Load(ini, "Lobby");

        Assert.Equal(new[] { "chkCrates", "ddCredits" }, options.Select(o => o.Name));

        GameOptionDefinition crates = options[0];
        Assert.Equal(("Crates", "Crates", 1, true, CheckBoxMapScoringMode.DenyWhenChecked),
            (crates.Label, crates.SpawnIniOption, crates.DefaultValue, crates.Reversed, crates.MapScoringMode));
        Assert.Equal(new[] { 2, 3 }, crates.DisallowedSideIndices);

        GameOptionDefinition credits = options[1];
        Assert.Equal(DropDownDataWriteMode.STRING, credits.DataWriteMode);
        Assert.Equal(1, credits.DefaultValue);
        Assert.Equal(new[] { ("5000", "Low"), ("10000", "10000") }, credits.Items.Select(i => (i.Value, i.Label)));
    }

    [Fact]
    public void OptionsWriteSpawnIniLikeTheControlsDid()
    {
        var ini = new IniFile();
        ini.SetStringValue("Lobby", "$CC0", "chkCrates:GameLobbyCheckBox");
        ini.SetStringValue("Lobby", "$CC1", "ddCredits:GameLobbyDropDown");
        ini.SetStringValue("chkCrates", "SpawnIniOption", "Crates");
        ini.SetStringValue("chkCrates", "Reversed", "yes");
        ini.SetStringValue("ddCredits", "Items", "5000,10000");
        ini.SetStringValue("ddCredits", "DataWriteMode", "String");
        ini.SetStringValue("ddCredits", "SpawnIniOption", "Credits");
        TestGame.EnsureInitialized();

        List<GameOptionDefinition> definitions = GameOptionCatalog.Load(ini, "Lobby");
        var crates = new GameOption(definitions[0], value: 0);
        var credits = new GameOption(definitions[1], value: 1);

        var spawnIni = new IniFile();
        crates.ApplySpawnIniCode(spawnIni);
        credits.ApplySpawnIniCode(spawnIni);

        // Reversed: unchecked writes the enabled value
        Assert.Equal("True", spawnIni.GetStringValue("Settings", "Crates", null));
        Assert.Equal("10000", spawnIni.GetStringValue("Settings", "Credits", null));
        Assert.True(crates.AllowScoring);
    }
}
