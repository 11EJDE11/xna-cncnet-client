using System;
using System.Collections.ObjectModel;
using System.Linq;

using Avalonia.Threading;

using ClientCore.Extensions;

using ClientLogic.Launch;
using ClientLogic.Settings;

using CommunityToolkit.Mvvm.ComponentModel;

using Rampastring.Tools;

namespace AvClientView.ViewModels;

/// <summary>A row of the hotkey list: the command and its current shortcut.</summary>
public sealed record HotkeyRowViewModel(GameCommand Command, string Shortcut);

/// <summary>
/// The hotkey window, as ClientGUI's HotkeyConfigurationWindow: commands by category, the selected command's details,
/// a pressed key (with modifiers) to assign, reset to default, reset all, save. At start-up the keyboard INI is loaded
/// and written again (so new default keys from an update are stored), and it is reloaded after each game.
/// </summary>
public sealed partial class HotkeyWindowViewModel : ObservableObject
{
    private readonly string hotkeyTipText = "Press a key...".L10N("Client:DTAConfig:PressAKey");
    private readonly HotkeyConfiguration config;
    private Hotkey pendingHotkey = Hotkey.None;
    private HotkeyModifiers lastModifiers;

    public HotkeyWindowViewModel(GameProcessService gameProcess)
    {
        try
        {
            config = HotkeyConfiguration.FromClient();
            config.Load();
            config.Write(writeEvenIfSettingsIniAsKeyboardIni: true);
        }
        catch (Exception ex)
        {
            Logger.Log("HotkeyWindowViewModel: reading the keyboard commands failed: " + ex.Message);
            config = null;
        }

        Categories = config?.Categories.ToList() ?? [];
        if (Categories.Count == 0)
            Logger.Log("No keyboard game commands exist!");

        gameProcess.GameProcessExited += () => Dispatcher.UIThread.Post(() => config?.Load());
        newHotkeyText = hotkeyTipText;
    }

    public System.Collections.Generic.List<string> Categories { get; }

    public ObservableCollection<HotkeyRowViewModel> Rows { get; } = [];

    [ObservableProperty]
    private bool isOpen;

    [ObservableProperty]
    private int selectedCategoryIndex = -1;

    [ObservableProperty]
    private int selectedIndex = -1;

    [ObservableProperty]
    private bool isCommandSelected;

    [ObservableProperty]
    private string commandCaption = "Command name".L10N("Client:DTAConfig:CommandName");

    [ObservableProperty]
    private string description = "Command description".L10N("Client:DTAConfig:CommandDescription");

    [ObservableProperty]
    private string currentHotkey = "Current hotkey value".L10N("Client:DTAConfig:CurrentHotKeyValue");

    [ObservableProperty]
    private string defaultHotkey = string.Empty;

    [ObservableProperty]
    private bool canReset;

    [ObservableProperty]
    private string newHotkeyText;

    [ObservableProperty]
    private string currentlyAssignedTo = string.Empty;

    private GameCommand SelectedCommand => SelectedIndex >= 0 && SelectedIndex < Rows.Count ? Rows[SelectedIndex].Command : null;

    public void Open()
    {
        config?.Load();
        if (SelectedCategoryIndex < 0 && Categories.Count > 0)
            SelectedCategoryIndex = 0;

        RefreshList();
        IsOpen = true;
    }

    public void Cancel() => IsOpen = false;

    public void Save()
    {
        config?.Write();
        IsOpen = false;
    }

    partial void OnSelectedCategoryIndexChanged(int value)
    {
        FillRows();
        SelectedIndex = -1;
    }

    partial void OnSelectedIndexChanged(int value)
    {
        GameCommand command = SelectedCommand;
        IsCommandSelected = command != null;
        if (command == null)
            return;

        CommandCaption = command.UIName;
        Description = command.Description;
        CurrentHotkey = command.Hotkey?.ToStringWithNone();
        DefaultHotkey = command.DefaultHotkey?.ToStringWithNone();
        CanReset = command.DefaultHotkey != command.Hotkey;
        NewHotkeyText = hotkeyTipText;
        pendingHotkey = Hotkey.None;
        CurrentlyAssignedTo = string.Empty;
    }

    private void FillRows()
    {
        Rows.Clear();
        if (config == null || SelectedCategoryIndex < 0 || SelectedCategoryIndex >= Categories.Count)
            return;

        foreach (GameCommand command in config.InCategory(Categories[SelectedCategoryIndex]))
            Rows.Add(new HotkeyRowViewModel(command, command.Hotkey?.ToString()));
    }

    /// <summary>Refreshes the list, keeping the selection (RefreshHotkeyList).</summary>
    private void RefreshList()
    {
        int selected = SelectedIndex;
        FillRows();
        SelectedIndex = -1;
        SelectedIndex = selected < Rows.Count ? selected : -1;
    }

    /// <summary>A key was pressed (its virtual-key code); left/right Shift, Ctrl and Alt only act as modifiers.</summary>
    public void KeyPressed(int virtualKey, HotkeyModifiers modifiers)
    {
        if (virtualKey == 0 || KeyNames.ModifierKeys.Contains(virtualKey))
        {
            ModifiersChanged(modifiers);
            return;
        }

        pendingHotkey = new Hotkey(virtualKey, modifiers);
        CurrentlyAssignedTo = string.Empty;
        if (config?.AssignedTo(pendingHotkey) is GameCommand assigned)
            CurrentlyAssignedTo = "Currently assigned to:".L10N("Client:DTAConfig:CurrentAssignTo") + Environment.NewLine + assigned.UIName;

        lastModifiers = modifiers;
        UpdateNewHotkeyText();
    }

    /// <summary>
    /// The held modifiers changed (the XNA window's Update): with no key yet, or a key pressed without modifiers, the
    /// pending hotkey becomes the modifiers alone.
    /// </summary>
    public void ModifiersChanged(HotkeyModifiers modifiers)
    {
        if ((pendingHotkey.Key == 0 && modifiers != pendingHotkey.Modifier) ||
            (pendingHotkey.Key != 0 && lastModifiers == HotkeyModifiers.None && modifiers != lastModifiers))
        {
            pendingHotkey = new Hotkey(0, modifiers);
            CurrentlyAssignedTo = string.Empty;
        }

        lastModifiers = modifiers;
        UpdateNewHotkeyText();
    }

    private void UpdateNewHotkeyText()
    {
        string text = pendingHotkey.ToString();
        NewHotkeyText = text != string.Empty ? text : hotkeyTipText;
    }

    public void Assign()
    {
        GameCommand command = SelectedCommand;
        if (command == null || config == null)
            return;

        if (!config.Assign(command, pendingHotkey))
            return;

        RefreshList();
        pendingHotkey = Hotkey.None;
    }

    public void ResetKey()
    {
        GameCommand command = SelectedCommand;
        if (command == null || config == null)
            return;

        config.ResetToDefault(command);
        pendingHotkey = Hotkey.None;
        RefreshList();
    }

    public void ResetAll()
    {
        config?.ResetAllToDefaults();
        RefreshList();
    }
}
