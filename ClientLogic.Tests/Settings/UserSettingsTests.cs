using System;
using System.IO;

using ClientCore;

using ClientGUI.Settings;

using ClientLogic.Settings;

using Rampastring.Tools;

using Xunit;

namespace ClientLogic.Tests.Settings;

public class UserSettingsTests
{
    private static string UniqueSection() => "Test" + Guid.NewGuid().ToString("N");

    private static CheckBoxSetting CheckBox(string section, params (string Key, string Value)[] keys)
    {
        TestGame.EnsureInitialized();
        var setting = new CheckBoxSetting("chkTest");
        setting.TryParse("SettingSection", section);
        foreach ((string key, string value) in keys)
            setting.TryParse(key, value);

        return setting;
    }

    [Fact]
    public void ACheckBoxDefaultsToCustomSettingsAndItsName()
    {
        TestGame.EnsureInitialized();
        var setting = new CheckBoxSetting("chkThing");
        Assert.Equal(("CustomSettings", "chkThing_Checked"), (setting.SettingSection, setting.SettingKey));

        setting.TryParse("WriteSettingValue", "true");
        Assert.Equal("chkThing_Value", setting.SettingKey);
    }

    [Fact]
    public void ACheckBoxLoadsItsDefaultAndSavesABoolean()
    {
        string section = UniqueSection();
        CheckBoxSetting setting = CheckBox(section, ("DefaultValue", "true"), ("SettingKey", "Flag"));

        setting.Load();
        Assert.True(setting.IsChecked);

        setting.IsChecked = false;
        Assert.False(setting.Save());
        Assert.False(UserINISettings.Instance.GetValue(section, "Flag", true));
    }

    [Fact]
    public void AWriteSettingValueCheckBoxMapsItsValuesAndFallsBackToTheDefault()
    {
        string section = UniqueSection();
        CheckBoxSetting setting = CheckBox(section, ("SettingKey", "DisableEdgeScrolling"), ("WriteSettingValue", "true"),
            ("EnabledSettingValue", "false"), ("DisabledSettingValue", "true"), ("DefaultValue", "true"));

        UserINISettings.Instance.SetValue(section, "DisableEdgeScrolling", "true");
        setting.Load();
        Assert.False(setting.IsChecked);

        UserINISettings.Instance.SetValue(section, "DisableEdgeScrolling", "something else");
        setting.Load();
        Assert.True(setting.IsChecked);

        setting.IsChecked = false;
        setting.Save();
        Assert.Equal("true", UserINISettings.Instance.GetValue(section, "DisableEdgeScrolling", string.Empty));
    }

    [Fact]
    public void RestartIsRequiredOnlyWhenARestartSettingChanged()
    {
        string section = UniqueSection();
        CheckBoxSetting setting = CheckBox(section, ("RestartRequired", "true"));
        setting.Load();
        Assert.False(setting.Save());

        setting.IsChecked = !setting.IsChecked;
        Assert.True(setting.Save());
    }

    [Fact]
    public void AChildCheckBoxIsClearedAndLockedWhileItsParentHasTheOtherValue()
    {
        TestGame.EnsureInitialized();
        var parent = new CheckBoxSetting("chkParent") { IsChecked = true };
        var child = new CheckBoxSetting("chkChild") { IsChecked = true };
        child.TryParse("ParentCheckBoxName", "chkParent");

        var panel = new TestPanel();
        panel.AddSetting(parent);
        panel.AddSetting(child);
        panel.LinkParents();
        Assert.True(child.AllowChecking);

        parent.IsChecked = false;
        Assert.False(child.AllowChecking);
        Assert.False(child.IsChecked);

        parent.IsChecked = true;
        Assert.True(child.AllowChecking);
    }

    [Fact]
    public void ADropDownReadsItsItemsAndWritesTheIndexOrTheItemValue()
    {
        TestGame.EnsureInitialized();
        string section = UniqueSection();
        var setting = new DropDownSetting("ddTest", "TestPanel");
        foreach ((string key, string value) in new[] { ("Items", "Low,Medium,High"), ("SettingSection", section), ("DefaultValue", "1") })
            setting.TryParse(key, value);

        Assert.Equal(["Low", "Medium", "High"], setting.Items.ConvertAll(i => i.Text));
        Assert.Equal("ddTest_SelectedIndex", setting.SettingKey);

        setting.Load();
        Assert.Equal(1, setting.SelectedIndex);

        setting.SelectedIndex = 2;
        setting.Save();
        Assert.Equal(2, UserINISettings.Instance.GetValue(section, "ddTest_SelectedIndex", 0));

        setting.TryParse("WriteItemValue", "true");
        setting.TryParse("SettingKey", "Detail");
        setting.Save();
        Assert.Equal("High", UserINISettings.Instance.GetValue(section, "Detail", string.Empty));

        UserINISettings.Instance.SetValue(section, "Detail", "Unknown");
        setting.Load();
        Assert.Equal(1, setting.SelectedIndex);
    }

    [Fact]
    public void AFileDropDownCopiesTheSelectedItemsFilesAndMarksMissingOnesUnselectable()
    {
        TestGame.EnsureInitialized();
        string dir = "SettingsTest" + Guid.NewGuid().ToString("N");
        Directory.CreateDirectory(Path.Combine(ProgramConstants.GamePath, dir));
        try
        {
            File.WriteAllText(Path.Combine(ProgramConstants.GamePath, dir, "a.ini"), "A");

            var ini = new IniFile();
            ini.SetStringValue("ddChannel", "Item0File0", $"{dir}/a.ini,{dir}/out.ini,AlwaysOverwrite");
            ini.SetStringValue("ddChannel", "Item1File0", $"{dir}/missing.ini,{dir}/out.ini,AlwaysOverwrite");

            var setting = new DropDownSetting("ddChannel", "UpdaterOptionsPanel", isFileSetting: true);
            setting.TryParse("Items", "Live,Dev");
            setting.TryParse("SettingSection", UniqueSection());
            setting.TryParse("CheckAvailability", "yes");
            setting.TryParse("ResetUnavailableValue", "yes");
            setting.ReadFiles(ini.GetSection("ddChannel"));

            setting.Load();
            setting.SelectedIndex = 1;
            Assert.True(setting.Refresh());
            Assert.False(setting.Items[1].Selectable);
            Assert.Equal(0, setting.SelectedIndex);

            setting.Save();
            Assert.Equal("A", File.ReadAllText(Path.Combine(ProgramConstants.GamePath, dir, "out.ini")));
        }
        finally
        {
            Directory.Delete(Path.Combine(ProgramConstants.GamePath, dir), recursive: true);
        }
    }

    [Theory]
    [InlineData("25", 10, 100, 25)]
    [InlineData(" 7 ", 10, 100, 7)]
    [InlineData("500", 10, 100, 100)]
    [InlineData("-1", 10, 100, 10)]
    [InlineData("abc", 10, 100, 10)]
    public void StorageLimitsAreParsedAsInTheXnaPanel(string text, int previous, int maximum, int expected)
        => Assert.Equal(expected, StorageOptionsModel.ParseLimit(text, previous, maximum));

    [Fact]
    public void ResetToDefaultOnGameExitSettingsAreResetAndSaved()
    {
        string section = UniqueSection();
        CheckBoxSetting setting = CheckBox(section, ("ResetToDefaultOnGameExit", "true"), ("SettingKey", "Once"));
        var panel = new TestPanel();
        panel.AddSetting(setting);

        setting.IsChecked = true;
        panel.OnGameExited();

        Assert.False(setting.IsChecked);
        Assert.False(UserINISettings.Instance.GetValue(section, "Once", true));
    }

    private sealed class TestPanel() : OptionsPanelModel("TestPanel");
}
