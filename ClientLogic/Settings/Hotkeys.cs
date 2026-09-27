#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;

using ClientCore;
using ClientCore.Extensions;

using Rampastring.Tools;

namespace ClientLogic.Settings;

[Flags]
public enum HotkeyModifiers
{
    None = 0,
    Shift = 1,
    Ctrl = 2,
    Alt = 4,
}

/// <summary>
/// A game hotkey, as ClientGUI's HotkeyConfigurationWindow stores it: a Windows virtual-key code (the XNA Keys values)
/// and modifiers, encoded for the game as (modifiers &lt;&lt; 8) + key.
/// </summary>
public sealed record Hotkey(int Key, HotkeyModifiers Modifier)
{
    public static readonly Hotkey None = new(0, HotkeyModifiers.None);

    public Hotkey(int encodedKeyValue) : this(encodedKeyValue & 255, (HotkeyModifiers)(encodedKeyValue >> 8))
    {
    }

    public int GetTSEncoded() => ((int)Modifier << 8) + Key;

    public override string ToString() => Key == 0 && Modifier == HotkeyModifiers.None ? string.Empty : GetString();

    public string ToStringWithNone() =>
        Key == 0 && Modifier == HotkeyModifiers.None ? "None".L10N("Client:DTAConfig:HotkeyNone") : GetString();

    private string GetString()
    {
        string str = string.Empty;
        if (Modifier.HasFlag(HotkeyModifiers.Shift))
            str += "SHIFT+";
        if (Modifier.HasFlag(HotkeyModifiers.Ctrl))
            str += "CTRL+";
        if (Modifier.HasFlag(HotkeyModifiers.Alt))
            str += "ALT+";

        return Key == 0 ? str : str + KeyNames.DisplayName(Key);
    }
}

/// <summary>The XNA Keys names of the Windows virtual-key codes, as the hotkey window shows them.</summary>
public static class KeyNames
{
    /// <summary>Virtual-key codes the hotkey window ignores as keys (they only act as modifiers).</summary>
    public static readonly HashSet<int> ModifierKeys = new HashSet<int> { 160, 161, 162, 163, 164, 165 };

    private static readonly Dictionary<int, string> names = BuildNames();

    private static Dictionary<int, string> BuildNames()
    {
        var map = new Dictionary<int, string>
        {
            [8] = "Back", [9] = "Tab", [13] = "Enter", [19] = "Pause", [20] = "CapsLock", [21] = "Kana", [25] = "Kanji",
            [27] = "Escape", [28] = "ImeConvert", [29] = "ImeNoConvert", [32] = "Space", [33] = "PageUp", [34] = "PageDown",
            [35] = "End", [36] = "Home", [37] = "Left", [38] = "Up", [39] = "Right", [40] = "Down", [41] = "Select",
            [42] = "Print", [43] = "Execute", [44] = "PrintScreen", [45] = "Insert", [46] = "Delete", [47] = "Help",
            [91] = "LeftWindows", [92] = "RightWindows", [93] = "Apps", [95] = "Sleep",
            [106] = "Multiply", [107] = "Add", [108] = "Separator", [109] = "Subtract", [110] = "Decimal", [111] = "Divide",
            [144] = "NumLock", [145] = "Scroll",
            [160] = "LeftShift", [161] = "RightShift", [162] = "LeftControl", [163] = "RightControl", [164] = "LeftAlt", [165] = "RightAlt",
            [166] = "BrowserBack", [167] = "BrowserForward", [168] = "BrowserRefresh", [169] = "BrowserStop", [170] = "BrowserSearch",
            [171] = "BrowserFavorites", [172] = "BrowserHome", [173] = "VolumeMute", [174] = "VolumeDown", [175] = "VolumeUp",
            [176] = "MediaNextTrack", [177] = "MediaPreviousTrack", [178] = "MediaStop", [179] = "MediaPlayPause",
            [180] = "LaunchMail", [181] = "SelectMedia", [182] = "LaunchApplication1", [183] = "LaunchApplication2",
            [186] = "OemSemicolon", [187] = "OemPlus", [188] = "OemComma", [189] = "OemMinus", [190] = "OemPeriod",
            [191] = "OemQuestion", [192] = "OemTilde", [202] = "ChatPadGreen", [203] = "ChatPadOrange",
            [219] = "OemOpenBrackets", [220] = "OemPipe", [221] = "OemCloseBrackets", [222] = "OemQuotes", [223] = "Oem8",
            [226] = "OemBackslash", [229] = "ProcessKey", [242] = "OemCopy", [243] = "OemAuto", [244] = "OemEnlW",
            [246] = "Attn", [247] = "Crsel", [248] = "Exsel", [249] = "EraseEof", [250] = "Play", [251] = "Zoom",
            [253] = "Pa1", [254] = "OemClear",
        };

        for (int i = 0; i <= 9; i++)
        {
            map[48 + i] = "D" + i;
            map[96 + i] = "NumPad" + i;
        }

        for (int i = 0; i < 26; i++)
            map[65 + i] = ((char)('A' + i)).ToString();

        for (int i = 1; i <= 24; i++)
            map[111 + i] = "F" + i;

        return map;
    }

    /// <summary>The XNA Keys name of a code (the number if it has none).</summary>
    public static string Name(int key) => names.TryGetValue(key, out string? name) ? name : key.ToString();

    /// <summary>HotkeyConfigurationWindow's display text for a key: digits plain, a few special names.</summary>
    public static string DisplayName(int key) => key switch
    {
        >= 48 and <= 57 => ((char)key).ToString(),
        12 => "NumPad5 (NumLock off)",
        0x10 => "Shift",
        0x11 => "Ctrl",
        0x12 => "Alt",
        _ => Name(key),
    };
}

/// <summary>A game command from KeyboardCommands.ini.</summary>
public sealed class GameCommand
{
    public GameCommand(IniSection section)
    {
        ININame = section.SectionName;
        UIName = section.GetStringValue("UIName", "Unnamed command").L10N($"INI:Hotkeys:{ININame}:UIName");
        string category = section.GetStringValue("Category", "Unknown category");
        Category = category.L10N($"INI:HotkeyCategories:{category}");
        Description = section.GetStringValue("Description", "Unknown description").L10N($"INI:Hotkeys:{ININame}:Description");
        DisableModifierKeys = section.GetBooleanValue("DisableModifierKeys", false);

        int? defaultKey = section.GetIntValueOrNull("DefaultKey");
        DefaultHotkey = defaultKey.HasValue ? new Hotkey(defaultKey.Value) : null;

        if (DefaultHotkey != null && DefaultHotkey != Hotkey.None && DisableModifierKeys && DefaultHotkey.Modifier != HotkeyModifiers.None)
        {
            throw new Exception(string.Format(
                ("The default hotkey {0} for command '{1}' has modifier keys but DisableModifierKeys is set to true." + " " +
                "Please remove the modifier from the default hotkey or set DisableModifierKeys=false in file {2}.").L10N("Client:DTAConfig:ExceptionModifierKeysDetected"),
                DefaultHotkey, ININame, HotkeyConfiguration.KEYBOARD_COMMANDS_INI));
        }

        // An explicit "no hotkey" default is treated as no default
        if (DefaultHotkey == Hotkey.None)
            DefaultHotkey = null;
    }

    public string UIName { get; }

    public string Category { get; }

    public string Description { get; }

    public string ININame { get; }

    /// <summary>The assigned hotkey; null when none is written (Hotkey.None is written as "no hotkey").</summary>
    public Hotkey? Hotkey { get; set; }

    public Hotkey? DefaultHotkey { get; }

    public bool DisableModifierKeys { get; }
}

/// <summary>
/// The hotkey window's logic, as ClientGUI's HotkeyConfigurationWindow: reads KeyboardCommands.ini, loads and writes
/// the keyboard INI (or the settings INI when SettingsIniAsKeyboardIni), assigns and resets hotkeys so no hotkey is
/// used twice.
/// </summary>
public sealed class HotkeyConfiguration
{
    public const string KEYBOARD_COMMANDS_INI = "KeyboardCommands.ini";

    public HotkeyConfiguration(IniFile keyboardCommandsIni)
    {
        var defaultHotkeys = new HashSet<Hotkey>();
        foreach (string sectionName in keyboardCommandsIni.GetSections())
        {
            var command = new GameCommand(keyboardCommandsIni.GetSection(sectionName));
            Commands.Add(command);

            if (command.DefaultHotkey != null && command.DefaultHotkey != Hotkey.None && !defaultHotkeys.Add(command.DefaultHotkey))
            {
                throw new Exception(string.Format(
                    ("The default hotkey {0} for command {1} is duplicated with another command's default hotkey." + " " +
                    "Please make sure all default hotkeys in file {2} are unique.").L10N("Client:DTAConfig:ExceptionDuplicateHotkeys"),
                    command.DefaultHotkey, command.ININame, KEYBOARD_COMMANDS_INI));
            }
        }

        Categories = Commands.Select(c => c.Category).Distinct().ToList();
    }

    /// <summary>The configuration of the running client's KeyboardCommands.ini.</summary>
    public static HotkeyConfiguration FromClient() =>
        new(new IniFile(SafePath.CombineFilePath(ProgramConstants.GetBaseResourcePath(), KEYBOARD_COMMANDS_INI)));

    public List<GameCommand> Commands { get; } = [];

    public IReadOnlyList<string> Categories { get; }

    public IEnumerable<GameCommand> InCategory(string category) => Commands.Where(c => c.Category == category);

    private static IniFile OpenKeyboardIni() => ClientConfiguration.Instance.SettingsIniAsKeyboardIni
        ? UserINISettings.Instance.SettingsIni
        : new IniFile(SafePath.CombineFilePath(ProgramConstants.GamePath, ClientConfiguration.Instance.KeyboardINI));

    public void Load() => Load(OpenKeyboardIni());

    /// <summary>Loads the hotkeys: stored ones (duplicates and forbidden modifiers dropped), then free defaults.</summary>
    public void Load(IniFile keyboardIni)
    {
        IniSection hotkeySection = keyboardIni.GetOrAddSection(ClientConfiguration.Instance.KeyboardHotkeySection);

        var assignedHotkeys = new HashSet<Hotkey>();
        foreach (GameCommand command in Commands)
        {
            int? stored = hotkeySection.GetIntValueOrNull(command.ININame);
            if (!stored.HasValue)
            {
                command.Hotkey = null;
                continue;
            }

            var hotkey = new Hotkey(stored.Value);
            if (command.DisableModifierKeys && hotkey.Modifier != HotkeyModifiers.None)
            {
                command.Hotkey = null;
                hotkeySection.RemoveKey(command.ININame);
                continue;
            }

            bool isDuplicate = hotkey != Hotkey.None && !assignedHotkeys.Add(hotkey);
            command.Hotkey = isDuplicate ? null : hotkey;
        }

        foreach (GameCommand command in Commands)
        {
            if (hotkeySection.KeyExists(command.ININame) || command.DefaultHotkey == null)
                continue;

            bool occupied = command.DefaultHotkey != Hotkey.None &&
                Commands.Any(other => other != command && command.DefaultHotkey == other.Hotkey);

            if (!occupied)
                command.Hotkey = command.DefaultHotkey;
        }
    }

    /// <summary>
    /// Writes the hotkeys. With the settings INI as keyboard INI, the file is only written when
    /// <paramref name="writeEvenIfSettingsIniAsKeyboardIni"/> (it is saved with the other settings).
    /// </summary>
    public void Write(bool writeEvenIfSettingsIniAsKeyboardIni = false)
    {
        IniFile keyboardIni = OpenKeyboardIni();
        Write(keyboardIni);

        if (writeEvenIfSettingsIniAsKeyboardIni || !ClientConfiguration.Instance.SettingsIniAsKeyboardIni)
            keyboardIni.WriteIniFile();
    }

    public void Write(IniFile keyboardIni)
    {
        IniSection hotkeySection = keyboardIni.GetOrAddSection(ClientConfiguration.Instance.KeyboardHotkeySection);
        foreach (GameCommand command in Commands)
        {
            if (command.Hotkey == null)
            {
                if (hotkeySection.KeyExists(command.ININame))
                    hotkeySection.RemoveKey(command.ININame);
            }
            else
            {
                hotkeySection.SetStringValue(command.ININame, command.Hotkey.GetTSEncoded().ToString());
            }
        }
    }

    /// <summary>Assigns a pressed hotkey; another command using it loses it. Returns false if modifiers aren't allowed.</summary>
    public bool Assign(GameCommand command, Hotkey pendingHotkey)
    {
        if (command.DisableModifierKeys && pendingHotkey.Modifier != HotkeyModifiers.None)
            return false;

        if (pendingHotkey != Hotkey.None)
        {
            foreach (GameCommand other in Commands)
            {
                if (pendingHotkey == other.Hotkey)
                    other.Hotkey = null;
            }
        }

        command.Hotkey = pendingHotkey;
        return true;
    }

    /// <summary>The command's default hotkey (taken from any other command using it).</summary>
    public void ResetToDefault(GameCommand command)
    {
        command.Hotkey = command.DefaultHotkey;
        if (command.DefaultHotkey == null)
            return;

        foreach (GameCommand other in Commands)
        {
            if (other != command && other.Hotkey == command.Hotkey)
                other.Hotkey = null;
        }
    }

    public void ResetAllToDefaults()
    {
        foreach (GameCommand command in Commands)
            command.Hotkey = command.DefaultHotkey;
    }

    /// <summary>The command that uses a hotkey, or null.</summary>
    public GameCommand? AssignedTo(Hotkey hotkey) => Commands.LastOrDefault(c => hotkey == c.Hotkey);
}
