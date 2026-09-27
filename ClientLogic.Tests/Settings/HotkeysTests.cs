using ClientCore;

using ClientLogic.Settings;

using Rampastring.Tools;

using Xunit;

namespace ClientLogic.Tests.Settings;

public class HotkeysTests
{
    private static HotkeyConfiguration Create()
    {
        TestGame.EnsureInitialized();
        var commands = new IniFile();
        commands.SetStringValue("Stop", "UIName", "Stop");
        commands.SetStringValue("Stop", "Category", "Control");
        commands.SetIntValue("Stop", "DefaultKey", 83); // S
        commands.SetStringValue("Guard", "UIName", "Guard");
        commands.SetStringValue("Guard", "Category", "Control");
        commands.SetIntValue("Guard", "DefaultKey", 71); // G
        commands.SetStringValue("Chat", "UIName", "Chat");
        commands.SetStringValue("Chat", "Category", "Interface");
        commands.SetStringValue("Chat", "DisableModifierKeys", "yes");
        return new HotkeyConfiguration(commands);
    }

    [Fact]
    public void HotkeysEncodeAsTheGameExpects()
    {
        var hotkey = new Hotkey(65, HotkeyModifiers.Ctrl | HotkeyModifiers.Shift);
        Assert.Equal((3 << 8) + 65, hotkey.GetTSEncoded());
        Assert.Equal(hotkey, new Hotkey(hotkey.GetTSEncoded()));
        Assert.Equal("SHIFT+CTRL+A", hotkey.ToString());
        Assert.Equal("5", new Hotkey(53, HotkeyModifiers.None).ToString());
        Assert.Equal("NumPad5", new Hotkey(101, HotkeyModifiers.None).ToString());
        Assert.Equal(string.Empty, Hotkey.None.ToString());
    }

    [Fact]
    public void LoadingUsesStoredKeysDropsDuplicatesAndFillsFreeDefaults()
    {
        HotkeyConfiguration config = Create();
        var keyboard = new IniFile();
        string section = ClientConfiguration.Instance.KeyboardHotkeySection;
        keyboard.SetIntValue(section, "Stop", 71); // G, Guard's default
        keyboard.SetIntValue(section, "Chat", (2 << 8) + 67); // CTRL+C, not allowed

        config.Load(keyboard);

        Assert.Equal(new Hotkey(71), config.Commands[0].Hotkey);
        Assert.Null(config.Commands[1].Hotkey); // default G is taken by Stop
        Assert.Null(config.Commands[2].Hotkey);
        Assert.False(keyboard.GetSection(section).KeyExists("Chat"));
    }

    [Fact]
    public void AssigningTakesTheHotkeyFromTheOtherCommand()
    {
        HotkeyConfiguration config = Create();
        config.Load(new IniFile());

        Assert.True(config.Assign(config.Commands[1], new Hotkey(83))); // Guard gets S
        Assert.Null(config.Commands[0].Hotkey);
        Assert.Equal(config.Commands[1], config.AssignedTo(new Hotkey(83)));

        Assert.False(config.Assign(config.Commands[2], new Hotkey(67, HotkeyModifiers.Alt)));

        config.ResetToDefault(config.Commands[0]);
        Assert.Equal(new Hotkey(83), config.Commands[0].Hotkey);
        Assert.Null(config.Commands[1].Hotkey);
    }

    [Fact]
    public void WritingRemovesUnassignedCommandsAndWritesNoneExplicitly()
    {
        HotkeyConfiguration config = Create();
        config.Load(new IniFile());
        config.Commands[0].Hotkey = Hotkey.None;
        config.Commands[1].Hotkey = null;

        var keyboard = new IniFile();
        string section = ClientConfiguration.Instance.KeyboardHotkeySection;
        keyboard.SetIntValue(section, "Guard", 71);
        config.Write(keyboard);

        Assert.Equal("0", keyboard.GetStringValue(section, "Stop", null));
        Assert.False(keyboard.GetSection(section).KeyExists("Guard"));
    }
}
