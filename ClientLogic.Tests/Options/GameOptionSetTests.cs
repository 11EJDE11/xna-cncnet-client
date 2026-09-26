using System.Collections.Generic;

using ClientLogic.Options;

using DTAClient.Domain.Multiplayer;

using Rampastring.Tools;

using Xunit;

namespace ClientLogic.Tests.Options;

public class GameOptionSetTests
{
    private static GameOption CheckBox(string name, bool isChecked = false)
    {
        var builder = new GameOptionDefinitionBuilder(name, GameOptionKind.CheckBox, isLobbyOption: true, (_, d) => d);
        builder.TryParse(null, "Checked", isChecked.ToString());
        return new GameOption(builder.Build(), isChecked ? 1 : 0);
    }

    private static GameOption DropDown(string name, int itemCount = 4, int value = 0)
    {
        var builder = new GameOptionDefinitionBuilder(name, GameOptionKind.DropDown, isLobbyOption: true, (_, d) => d);
        for (int i = 0; i < itemCount; i++)
            builder.Items.Add(new GameOptionItem(i.ToString(), i.ToString(), null));

        return new GameOption(builder.Build(), value);
    }

    private static (GameOptionSet Set, GameOption Crates, GameOption Shroud, GameOption Credits, List<GameOption> Changed) Create()
    {
        TestGame.EnsureInitialized();
        var set = new GameOptionSet();
        GameOption crates = CheckBox("chkCrates");
        GameOption shroud = CheckBox("chkShroud", isChecked: true);
        GameOption credits = DropDown("ddCredits");
        set.Add(crates);
        set.Add(shroud);
        set.Add(credits);

        var changed = new List<GameOption>();
        set.OptionChanged += (_, o) => changed.Add(o);
        return (set, crates, shroud, credits, changed);
    }

    private static KeyValuePair<string, T> Kvp<T>(string key, T value) => new(key, value);

    [Fact]
    public void ChangesOutsideABatchAreRecordedAsTheHostsChoice()
    {
        var (_, crates, _, credits, changed) = Create();

        crates.Value = 1;
        credits.Value = 2;

        Assert.Equal([crates, credits], changed);
        Assert.Equal((1, 2), (crates.HostValue, credits.HostValue));
    }

    [Fact]
    public void NothingIsRecordedWithoutHandlers()
    {
        TestGame.EnsureInitialized();
        var set = new GameOptionSet();
        GameOption crates = CheckBox("chkCrates");
        set.Add(crates);

        crates.Value = 1;

        Assert.Equal(0, crates.HostValue);
    }

    [Fact]
    public void BatchesSuppressEventsAndReportWhetherAnythingChanged()
    {
        var (set, crates, _, _, changed) = Create();

        set.BeginUpdate();
        set.BeginUpdate();
        crates.Value = 1;
        Assert.False(set.EndUpdate());
        Assert.True(set.EndUpdate());

        Assert.Empty(changed);
        Assert.Equal(0, crates.HostValue);

        set.BeginUpdate();
        Assert.False(set.EndUpdate());
    }

    [Fact]
    public void ForcedValuesAreLockedAndOthersReturnToTheHostsChoice()
    {
        var (set, crates, shroud, credits, changed) = Create();
        crates.Value = 1;
        credits.Value = 3;
        changed.Clear();

        // A map forces crates off and credits to 1; the game mode forces credits to 2 first
        set.BeginUpdate();
        set.ApplyForcedValues(
            [[], [Kvp("chkCrates", false), Kvp("ddUnknown", false)]],
            [[Kvp("ddCredits", 2)], [Kvp("ddCredits", 1)]]);
        set.EndUpdate();

        Assert.Equal((0, true), (crates.Value, crates.ForcedLocked));
        Assert.Equal((1, true), (credits.Value, credits.ForcedLocked));
        Assert.Equal((1, false), (shroud.Value, shroud.ForcedLocked));
        Assert.Equal((1, 3), (crates.HostValue, credits.HostValue));
        Assert.Empty(changed);

        // The next map forces nothing: everything is unlocked and back to the host's choice
        set.BeginUpdate();
        set.ApplyForcedValues([[]], [[]]);
        set.EndUpdate();

        Assert.Equal((1, false), (crates.Value, crates.ForcedLocked));
        Assert.Equal((3, false), (credits.Value, credits.ForcedLocked));
    }

    [Fact]
    public void ForcedValuesMatchOnlyTheirOwnKind()
    {
        var (set, crates, _, credits, _) = Create();

        set.ApplyForcedValues([[Kvp("ddCredits", true)]], [[Kvp("chkCrates", 1)]]);

        Assert.False(crates.ForcedLocked);
        Assert.False(credits.ForcedLocked);
    }

    [Fact]
    public void RestoringUserValuesRecordsOnlyChangedOptions()
    {
        var (set, crates, shroud, credits, changed) = Create();
        crates.UserValue = 1;
        credits.UserValue = 2;
        shroud.UserValue = 1;
        shroud.HostValue = 0;   // e.g. a map forced the value the player had chosen anyway

        Assert.True(set.RestoreUserValues());

        Assert.Empty(changed);
        Assert.Equal((1, 1), (crates.Value, crates.HostValue));
        Assert.Equal((2, 2), (credits.Value, credits.HostValue));
        Assert.Equal((1, 0), (shroud.Value, shroud.HostValue));
        Assert.False(set.RestoreUserValues());
    }

    [Fact]
    public void PresetsRoundTripAndSkipLockedOptions()
    {
        var (set, crates, shroud, credits, changed) = Create();
        crates.Value = 1;
        credits.Value = 2;
        GameOptionPreset preset = set.CreatePreset("Test");

        Assert.Equal(new Dictionary<string, bool> { ["chkCrates"] = true, ["chkShroud"] = true }, preset.GetCheckBoxValues());
        Assert.Equal(new Dictionary<string, int> { ["ddCredits"] = 2 }, preset.GetDropDownValues());

        crates.Value = 0;
        shroud.Value = 0;
        credits.Value = 0;
        credits.ForcedLocked = true;
        changed.Clear();

        Assert.True(set.ApplyPreset(preset, option => option != shroud));

        Assert.Empty(changed);
        Assert.Equal((1, 1), (crates.Value, crates.HostValue));
        Assert.Equal(0, shroud.Value);
        Assert.Equal(0, credits.Value);
    }

    [Fact]
    public void SkirmishSettingsRoundTripAndSkipForcedOptions()
    {
        var (set, crates, shroud, credits, _) = Create();
        crates.Value = 1;
        credits.UserValue = 3;
        var ini = new IniFile();
        set.WriteSettings(ini, "GameOptions");

        Assert.Equal("True", ini.GetStringValue("GameOptions", "chkCrates", null));
        Assert.Equal("3", ini.GetStringValue("GameOptions", "ddCredits", null));

        crates.Value = 0;
        shroud.Value = 0;
        credits.UserValue = 0;
        var gameMode = new GameMode("Battle");
        gameMode.ForcedCheckBoxValues.Add(Kvp("chkShroud", false));

        set.ReadSettings(ini, "GameOptions", gameMode, map: null);

        Assert.Equal(1, crates.Value);
        Assert.Equal(0, shroud.Value);
        Assert.Equal((3, 3), (credits.UserValue, credits.Value));

        // An index outside the items is remembered but not selected
        ini.SetStringValue("GameOptions", "ddCredits", "9");
        set.ReadSettings(ini, "GameOptions", gameMode: null, map: null);
        Assert.Equal((9, 3), (credits.UserValue, credits.Value));
    }
}
