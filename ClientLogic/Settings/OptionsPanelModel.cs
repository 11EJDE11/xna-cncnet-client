using System.Collections.Generic;
using System.Linq;

using ClientCore;

using CommunityToolkit.Mvvm.ComponentModel;

namespace ClientLogic.Settings;

/// <summary>
/// An options window tab, as ClientGUI's XNAOptionsPanel: its <see cref="UserSetting"/>s (created in code or from the
/// theme's "{Name}ExtraControls") plus the panel's own settings, loaded, saved and refreshed together. Settings with
/// ResetToDefaultOnGameExit go back to their default after each game.
/// </summary>
public abstract class OptionsPanelModel : ObservableObject
{
    private readonly List<UserSetting> userSettings = [];

    protected OptionsPanelModel(string name) => Name = name;

    /// <summary>The panel name (its INI section; extra controls are in "{Name}ExtraControls").</summary>
    public string Name { get; }

    public IReadOnlyList<UserSetting> UserSettings => userSettings;

    protected static UserINISettings IniSettings => UserINISettings.Instance;

    public void AddSetting(UserSetting setting) => userSettings.Add(setting);

    public UserSetting FindSetting(string name) => userSettings.FirstOrDefault(s => s.Name == name);

    /// <summary>Links check-boxes to their ParentCheckBoxName check-boxes (call once the settings are read).</summary>
    public void LinkParents()
    {
        foreach (CheckBoxSetting checkBox in userSettings.OfType<CheckBoxSetting>())
        {
            if (!string.IsNullOrEmpty(checkBox.ParentCheckBoxName))
                checkBox.SetParent(FindSetting(checkBox.ParentCheckBoxName) as CheckBoxSetting);
        }
    }

    /// <summary>A game ended: settings with ResetToDefaultOnGameExit go back to their default and are saved.</summary>
    public void OnGameExited()
    {
        foreach (UserSetting setting in userSettings)
        {
            if (!setting.ResetToDefaultOnGameExit)
                continue;

            setting.ResetToDefault();
            setting.Save();
        }
    }

    public virtual void Load()
    {
        foreach (UserSetting setting in userSettings)
            setting.Load();
    }

    /// <summary>Saves the panel; true if a change needs a client restart.</summary>
    public virtual bool Save()
    {
        bool restartRequired = false;
        foreach (UserSetting setting in userSettings)
            restartRequired = setting.Save() || restartRequired;

        return restartRequired;
    }

    /// <summary>Re-checks the file settings; true if a value was no longer available and changed.</summary>
    public virtual bool Refresh()
    {
        bool valuesChanged = false;
        foreach (UserSetting setting in userSettings)
            valuesChanged = setting.Refresh() || valuesChanged;

        return valuesChanged;
    }

    /// <summary>Options that can only be used from the main menu (the updater's Force Update).</summary>
    public virtual void ToggleMainMenuOnlyOptions(bool enable)
    {
    }
}
