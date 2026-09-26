using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;

using DTAClient.Domain.Multiplayer;

using Rampastring.Tools;

namespace ClientLogic.Options;

/// <summary>
/// The game options of a lobby, in INI order (which is also the order they are sent in), and the rules that change
/// several of them at once: forced values of the game mode and map, presets and saved skirmish settings.
/// </summary>
public sealed class GameOptionSet
{
    private readonly List<GameOption> all = [];
    private readonly List<GameOption> checkBoxes = [];
    private readonly List<GameOption> dropDowns = [];

    private int updateDepth;
    private bool changedDuringUpdate;

    public IReadOnlyList<GameOption> All => all;

    public IReadOnlyList<GameOption> CheckBoxes => checkBoxes;

    public IReadOnlyList<GameOption> DropDowns => dropDowns;

    /// <summary>
    /// Raised when the value of an option changes outside a batch (<see cref="BeginUpdate"/>). Before it is raised,
    /// the option's <see cref="GameOption.HostValue"/> is set to the new value. Nothing is recorded while there are
    /// no handlers, so values set while the lobby is being built don't count as host choices.
    /// </summary>
    public event EventHandler<GameOption> OptionChanged;

    /// <summary>Whether a batch of changes is in progress.</summary>
    public bool IsUpdating => updateDepth > 0;

    public void Add(GameOption option)
    {
        all.Add(option);
        (option.IsCheckBox ? checkBoxes : dropDowns).Add(option);
        option.PropertyChanged += Option_PropertyChanged;
    }

    public GameOption Find(string name) => all.FirstOrDefault(o => o.Name == name);

    private GameOption FindCheckBox(string name) => checkBoxes.FirstOrDefault(o => o.Name == name);

    private GameOption FindDropDown(string name) => dropDowns.FirstOrDefault(o => o.Name == name);

    private void Option_PropertyChanged(object sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName != nameof(GameOption.Value))
            return;

        if (IsUpdating)
        {
            changedDuringUpdate = true;
            return;
        }

        if (OptionChanged == null)
            return;

        var option = (GameOption)sender;
        option.HostValue = option.Value;
        OptionChanged(this, option);
    }

    /// <summary>
    /// Starts a batch of changes: until the matching <see cref="EndUpdate"/>, value changes don't raise
    /// <see cref="OptionChanged"/> and don't update <see cref="GameOption.HostValue"/>. Batches can be nested.
    /// </summary>
    public void BeginUpdate()
    {
        if (updateDepth++ == 0)
            changedDuringUpdate = false;
    }

    /// <summary>Ends a batch of changes.</summary>
    /// <returns>True if this ended the outermost batch and a value changed during it.</returns>
    public bool EndUpdate()
    {
        if (updateDepth == 0)
            throw new InvalidOperationException("EndUpdate called without BeginUpdate.");

        return --updateDepth == 0 && changedDuringUpdate;
    }

    /// <summary>
    /// Applies the options that a game mode and map force, and returns every other option to the host's last
    /// choice so that one map's forced values don't carry over to the next. Call it inside a batch.
    /// </summary>
    public void ApplyGameModeMap(GameMode gameMode, Map map) => ApplyForcedValues(
        [gameMode.ForcedCheckBoxValues, map.ForcedCheckBoxValues],
        [gameMode.ForcedDropDownValues, map.ForcedDropDownValues]);

    /// <summary>
    /// Unlocks every option, then sets and locks the forced values in order (a later list wins), matched by name.
    /// Options that nothing forces return to their <see cref="GameOption.HostValue"/>.
    /// </summary>
    public void ApplyForcedValues(
        IEnumerable<IEnumerable<KeyValuePair<string, bool>>> forcedCheckBoxValues,
        IEnumerable<IEnumerable<KeyValuePair<string, int>>> forcedDropDownValues)
    {
        foreach (GameOption option in dropDowns)
            option.ForcedLocked = false;

        foreach (GameOption option in checkBoxes)
            option.ForcedLocked = false;

        var notForced = new HashSet<GameOption>(all);

        foreach (var forcedValues in forcedCheckBoxValues)
        {
            foreach (KeyValuePair<string, bool> forced in forcedValues)
            {
                GameOption option = FindCheckBox(forced.Key);
                if (option != null)
                {
                    option.Value = forced.Value ? 1 : 0;
                    option.ForcedLocked = true;
                    notForced.Remove(option);
                }
            }
        }

        foreach (var forcedValues in forcedDropDownValues)
        {
            foreach (KeyValuePair<string, int> forced in forcedValues)
            {
                GameOption option = FindDropDown(forced.Key);
                if (option != null)
                {
                    option.Value = forced.Value;
                    option.ForcedLocked = true;
                    notForced.Remove(option);
                }
            }
        }

        foreach (GameOption option in checkBoxes.Where(notForced.Contains))
            option.Value = option.HostValue;

        foreach (GameOption option in dropDowns.Where(notForced.Contains))
            option.Value = option.HostValue;
    }

    /// <summary>
    /// Sets every option to the local player's last choice. Used when the local player becomes the game host.
    /// An option whose value changes records it as the host's choice; the others keep theirs.
    /// </summary>
    /// <returns>True if a value changed.</returns>
    public bool RestoreUserValues()
    {
        BeginUpdate();

        foreach (GameOption option in dropDowns.Concat(checkBoxes))
        {
            if (option.Value == option.UserValue)
                continue;

            option.Value = option.UserValue;
            option.HostValue = option.UserValue;
        }

        return EndUpdate();
    }

    /// <summary>Creates a preset of the current values.</summary>
    public GameOptionPreset CreatePreset(string name)
    {
        var preset = new GameOptionPreset(name);

        foreach (GameOption option in checkBoxes)
            preset.AddCheckBoxValue(option.Name, option.IsChecked);

        foreach (GameOption option in dropDowns)
            preset.AddDropDownValue(option.Name, option.Value);

        return preset;
    }

    /// <summary>
    /// Applies a preset's values as the host's choice, skipping forced options.
    /// </summary>
    /// <param name="canChangeCheckBox">Whether the local player may change a check box; defaults to yes.</param>
    /// <returns>True if a value changed.</returns>
    public bool ApplyPreset(GameOptionPreset preset, Func<GameOption, bool> canChangeCheckBox = null)
    {
        BeginUpdate();

        foreach (var kvp in preset.GetCheckBoxValues())
        {
            GameOption option = FindCheckBox(kvp.Key);
            if (option != null && (canChangeCheckBox?.Invoke(option) ?? true) && !option.ForcedLocked)
            {
                option.Value = kvp.Value ? 1 : 0;
                option.HostValue = option.Value;
            }
        }

        foreach (var kvp in preset.GetDropDownValues())
        {
            GameOption option = FindDropDown(kvp.Key);
            if (option != null && !option.ForcedLocked)
            {
                option.Value = kvp.Value;
                option.HostValue = kvp.Value;
            }
        }

        return EndUpdate();
    }

    /// <summary>
    /// Writes the options to the saved skirmish settings: the local player's last choice for drop-downs, the
    /// current value for check-boxes.
    /// </summary>
    public void WriteSettings(IniFile settingsIni, string sectionName)
    {
        foreach (GameOption option in dropDowns)
            settingsIni.SetStringValue(sectionName, option.Name, option.UserValue + "");

        foreach (GameOption option in checkBoxes)
            settingsIni.SetStringValue(sectionName, option.Name, option.IsChecked.ToString());
    }

    /// <summary>
    /// Reads the options from the saved skirmish settings, skipping options that the game mode or map force.
    /// Changes are not batched.
    /// </summary>
    public void ReadSettings(IniFile settingsIni, string sectionName, GameMode gameMode, Map map)
    {
        // Maybe we should build an union of the game mode and map
        // forced options, we'd have less repetitive code that way

        foreach (GameOption option in dropDowns)
        {
            if (IsForced(option, "Dropdown", gameMode?.ForcedDropDownValues, map?.ForcedDropDownValues))
                continue;

            option.UserValue = settingsIni.GetIntValue(sectionName, option.Name, option.UserValue);

            if (option.UserValue > -1 && option.UserValue < option.Definition.Items.Count)
                option.Value = option.UserValue;
        }

        foreach (GameOption option in checkBoxes)
        {
            if (IsForced(option, "Checkbox", gameMode?.ForcedCheckBoxValues, map?.ForcedCheckBoxValues))
                continue;

            option.Value = settingsIni.GetBooleanValue(sectionName, option.Name, option.IsChecked) ? 1 : 0;
        }
    }

    private static bool IsForced<T>(GameOption option, string kindName,
        List<KeyValuePair<string, T>> gameModeForced, List<KeyValuePair<string, T>> mapForced)
    {
        if (gameModeForced != null && gameModeForced.FindIndex(p => p.Key.Equals(option.Name)) > -1)
        {
            Logger.Log(kindName + " '" + option.Name + "' has forced value in gamemode - saved settings ignored.");
            return true;
        }

        if (mapForced != null && mapForced.FindIndex(p => p.Key.Equals(option.Name)) > -1)
        {
            Logger.Log(kindName + " '" + option.Name + "' has forced value in map - saved settings ignored.");
            return true;
        }

        return false;
    }
}
