using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.IO;
using System.Linq;

using ClientCore;
using ClientCore.I18N;

using ClientGUI.Settings;

using CommunityToolkit.Mvvm.ComponentModel;

using Rampastring.Tools;

namespace ClientLogic.Settings;

/// <summary>
/// A user setting shown in the options window, as ClientGUI's IUserSetting controls (SettingCheckBox,
/// FileSettingCheckBox, SettingDropDown, FileSettingDropDown) read, load and save it, without the XNA control.
/// The setting's INI keys are read with <see cref="TryParse"/>; file settings also read their file lists with
/// <see cref="ReadFiles"/>.
/// </summary>
public abstract partial class UserSetting : ObservableObject
{
    protected UserSetting(string name) => Name = name;

    /// <summary>The control name; the default setting key is derived from it.</summary>
    public string Name { get; }

    private string settingSection;

    public string SettingSection
    {
        get => string.IsNullOrEmpty(settingSection) ? "CustomSettings" : settingSection;
        set => settingSection = value;
    }

    private string settingKey;

    public string SettingKey
    {
        get => string.IsNullOrEmpty(settingKey) ? Name + DefaultKeySuffix : settingKey;
        set => settingKey = value;
    }

    protected abstract string DefaultKeySuffix { get; }

    public bool RestartRequired { get; set; }

    public bool ResetToDefaultOnGameExit { get; set; }

    /// <summary>Whether the setting copies files (FileSettingCheckBox / FileSettingDropDown).</summary>
    public abstract bool IsFileSetting { get; }

    /// <summary>Reads one INI key of the setting's section; false if it isn't a setting key.</summary>
    public virtual bool TryParse(string key, string value)
    {
        switch (key)
        {
            case "SettingSection":
                SettingSection = string.IsNullOrEmpty(value) ? SettingSection : value;
                return true;
            case "SettingKey":
                SettingKey = string.IsNullOrEmpty(value) ? SettingKey : value;
                return true;
            case "RestartRequired":
                RestartRequired = Conversions.BooleanFromString(value, false);
                return true;
            default:
                return false;
        }
    }

    /// <summary>Reads the file lists of a file setting from its INI section.</summary>
    public virtual void ReadFiles(IniSection section)
    {
    }

    public abstract void Load();

    /// <summary>Saves the setting; true if the change needs a client restart.</summary>
    public abstract bool Save();

    /// <summary>Re-checks a file setting's available values; true if the value changed.</summary>
    public virtual bool Refresh() => false;

    /// <summary>Resets the value to its default (after a game, for ResetToDefaultOnGameExit settings).</summary>
    public abstract void ResetToDefault();
}

/// <summary>A check-box setting: SettingCheckBox, or FileSettingCheckBox when <see cref="IsFileSetting"/>.</summary>
public sealed partial class CheckBoxSetting : UserSetting
{
    private readonly bool isFileSetting;
    private List<FileSourceDestinationInfo> enabledFiles = [];
    private List<FileSourceDestinationInfo> disabledFiles = [];
    private bool useLegacyImplementation;
    private bool originalState;
    private CheckBoxSetting parent;

    public CheckBoxSetting(string name, bool isFileSetting = false) : base(name) => this.isFileSetting = isFileSetting;

    /// <summary>A SettingCheckBox as the XNA option panels create it in code.</summary>
    public CheckBoxSetting(string name, bool defaultValue, string settingSection, string settingKey,
        bool writeSettingValue = false, string enabledValue = "", string disabledValue = "", bool restartRequired = false)
        : base(name)
    {
        DefaultValue = defaultValue;
        SettingSection = settingSection;
        SettingKey = settingKey;
        WriteSettingValue = writeSettingValue;
        EnabledSettingValue = enabledValue;
        DisabledSettingValue = disabledValue;
        RestartRequired = restartRequired;
    }

    public override bool IsFileSetting => isFileSetting;

    protected override string DefaultKeySuffix => !isFileSetting && WriteSettingValue ? "_Value" : "_Checked";

    [ObservableProperty]
    private bool isChecked;

    /// <summary>Whether the user can change it (false while its parent check-box has the other value).</summary>
    [ObservableProperty]
    private bool allowChecking = true;

    public bool DefaultValue { get; set; }

    public bool WriteSettingValue { get; set; }

    public string EnabledSettingValue { get; set; } = string.Empty;

    public string DisabledSettingValue { get; set; } = string.Empty;

    /// <summary>The name of the check-box this one depends on (ParentCheckBoxName), or null.</summary>
    public string ParentCheckBoxName { get; set; }

    public bool ParentCheckBoxRequiredValue { get; set; } = true;

    public bool CheckAvailability { get; set; }

    public bool ResetUnavailableValue { get; set; }

    public bool Reversed { get; set; }

    public override bool TryParse(string key, string value)
    {
        switch (key)
        {
            case "Checked":
            case "DefaultValue":
                DefaultValue = Conversions.BooleanFromString(value, false);
                return true;
            case "ParentCheckBoxName":
                ParentCheckBoxName = value;
                return true;
            case "ParentCheckBoxRequiredValue":
                ParentCheckBoxRequiredValue = Conversions.BooleanFromString(value, true);
                return true;
            case "ResetToDefaultOnGameExit":
                ResetToDefaultOnGameExit = Conversions.BooleanFromString(value, false);
                return true;
            case "WriteSettingValue" when !isFileSetting:
                WriteSettingValue = Conversions.BooleanFromString(value, false);
                return true;
            case "EnabledSettingValue" when !isFileSetting:
                EnabledSettingValue = value;
                return true;
            case "DisabledSettingValue" when !isFileSetting:
                DisabledSettingValue = value;
                return true;
            case "CheckAvailability" when isFileSetting:
                CheckAvailability = Conversions.BooleanFromString(value, false);
                return true;
            case "ResetUnavailableValue" when isFileSetting:
                ResetUnavailableValue = Conversions.BooleanFromString(value, false);
                return true;
            case "Reversed" when isFileSetting:
                Reversed = Conversions.BooleanFromString(value, false);
                return true;
            default:
                return base.TryParse(key, value);
        }
    }

    public override void ReadFiles(IniSection section)
    {
        if (!isFileSetting || section == null)
            return;

        List<FileSourceDestinationInfo> files = FileSourceDestinationInfo.ParseFSDInfoList(section, "File");
        if (files.Count > 0)
        {
            // Backwards compatibility with the old FileSettingCheckBox
            enabledFiles = files;
            useLegacyImplementation = true;
        }
        else
        {
            enabledFiles = FileSourceDestinationInfo.ParseFSDInfoList(section, "EnabledFile");
            disabledFiles = FileSourceDestinationInfo.ParseFSDInfoList(section, "DisabledFile");
        }
    }

    public void AddEnabledFile(string source, string destination, FileOperationOption option)
        => enabledFiles.Add(new FileSourceDestinationInfo(source, destination, option));

    public void AddDisabledFile(string source, string destination, FileOperationOption option)
        => disabledFiles.Add(new FileSourceDestinationInfo(source, destination, option));

    /// <summary>Links the parent check-box (ParentCheckBoxName): this one can only be checked while it has the required value.</summary>
    public void SetParent(CheckBoxSetting parentCheckBox)
    {
        if (parent != null)
            parent.PropertyChanged -= Parent_PropertyChanged;

        parent = parentCheckBox;
        UpdateAllowChecking();

        if (parent != null)
            parent.PropertyChanged += Parent_PropertyChanged;
    }

    private void Parent_PropertyChanged(object sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(IsChecked))
            UpdateAllowChecking();
    }

    private void UpdateAllowChecking()
    {
        if (parent == null)
            return;

        if (parent.IsChecked == ParentCheckBoxRequiredValue)
        {
            AllowChecking = true;
        }
        else
        {
            AllowChecking = false;
            IsChecked = false;
        }
    }

    private bool EnabledFilesComplete => enabledFiles.All(f => File.Exists(f.SourcePath));

    private bool DisabledFilesComplete => disabledFiles.All(f => File.Exists(f.SourcePath));

    public override void Load()
    {
        if (isFileSetting)
        {
            if (useLegacyImplementation)
                IsChecked = Reversed != File.Exists(enabledFiles[0].DestinationPath);
            else
                IsChecked = UserINISettings.Instance.GetValue(SettingSection, SettingKey, DefaultValue);
        }
        else
        {
            string value = UserINISettings.Instance.GetValue(SettingSection, SettingKey, string.Empty);

            if (WriteSettingValue)
            {
                if (value == EnabledSettingValue)
                    IsChecked = true;
                else if (value == DisabledSettingValue)
                    IsChecked = false;
                else
                    IsChecked = DefaultValue;
            }
            else
            {
                IsChecked = Conversions.BooleanFromString(value, DefaultValue);
            }
        }

        originalState = IsChecked;
    }

    public override bool Save()
    {
        if (!isFileSetting)
        {
            if (WriteSettingValue)
                UserINISettings.Instance.SetValue(SettingSection, SettingKey, IsChecked ? EnabledSettingValue : DisabledSettingValue);
            else
                UserINISettings.Instance.SetValue(SettingSection, SettingKey, IsChecked);

            return RestartRequired && IsChecked != originalState;
        }

        if (useLegacyImplementation)
        {
            if (Reversed != IsChecked)
                enabledFiles.ForEach(f => f.Apply());
            else
                enabledFiles.ForEach(f => f.Revert());

            return RestartRequired && IsChecked != originalState;
        }

        bool canBeChecked = !CheckAvailability || EnabledFilesComplete;
        bool canBeUnchecked = !CheckAvailability || DisabledFilesComplete;

        if (IsChecked && canBeChecked)
        {
            disabledFiles.ForEach(f => f.Revert());
            enabledFiles.ForEach(f => f.Apply());
        }
        else if (!IsChecked && canBeUnchecked)
        {
            enabledFiles.ForEach(f => f.Revert());
            disabledFiles.ForEach(f => f.Apply());
        }
        else
        {
            Logger.Log($"FileSettingCheckBox: The selected state ({IsChecked}) is unavailable in {Name}");
            return false;
        }

        UserINISettings.Instance.SetValue(SettingSection, SettingKey, IsChecked);
        return RestartRequired && IsChecked != originalState;
    }

    public override bool Refresh()
    {
        if (!isFileSetting || useLegacyImplementation)
            return false;

        bool currentValue = IsChecked;

        if (CheckAvailability && ResetUnavailableValue)
        {
            if (DisabledFilesComplete != EnabledFilesComplete)
                IsChecked = EnabledFilesComplete;
            else if (!DisabledFilesComplete && !EnabledFilesComplete)
                IsChecked = DefaultValue;
        }

        return IsChecked != currentValue;
    }

    public override void ResetToDefault() => IsChecked = DefaultValue;
}

/// <summary>One item of a <see cref="DropDownSetting"/>.</summary>
public sealed partial class DropDownSettingItem(string text, string tag) : ObservableObject
{
    public string Text { get; } = text;

    /// <summary>The untranslated item text (written with WriteItemValue).</summary>
    public string Tag { get; } = tag;

    /// <summary>False when a file setting's files for this item are missing (CheckAvailability).</summary>
    [ObservableProperty]
    private bool selectable = true;

    public override string ToString() => Text;
}

/// <summary>A drop-down setting: SettingDropDown, or FileSettingDropDown when <see cref="IsFileSetting"/>.</summary>
public sealed partial class DropDownSetting : UserSetting
{
    private readonly bool isFileSetting;
    private readonly string parentName;
    private readonly List<List<FileSourceDestinationInfo>> itemFilesList = [];
    private int originalState;

    /// <param name="parentName">The panel's name, for the item translations (INI:Controls:parent:name:ItemN).</param>
    public DropDownSetting(string name, string parentName, bool isFileSetting = false) : base(name)
    {
        this.parentName = parentName;
        this.isFileSetting = isFileSetting;
    }

    public override bool IsFileSetting => isFileSetting;

    protected override string DefaultKeySuffix => !isFileSetting && WriteItemValue ? "_Value" : "_SelectedIndex";

    public List<DropDownSettingItem> Items { get; } = [];

    [ObservableProperty]
    private int selectedIndex = -1;

    public int DefaultValue { get; set; }

    public bool WriteItemValue { get; set; }

    public bool CheckAvailability { get; private set; }

    public bool ResetUnavailableValue { get; private set; }

    public override bool TryParse(string key, string value)
    {
        switch (key)
        {
            case "Items":
                string[] items = value.Split(',');
                for (int i = 0; i < items.Length; i++)
                    Items.Add(new DropDownSettingItem(Localize($"Item{i}", items[i]), items[i]));
                return true;
            case "DefaultValue":
                DefaultValue = Conversions.IntFromString(value, 0);
                return true;
            case "WriteItemValue" when !isFileSetting:
                WriteItemValue = Conversions.BooleanFromString(value, false);
                return true;
            case "CheckAvailability" when isFileSetting:
                CheckAvailability = Conversions.BooleanFromString(value, false);
                return true;
            case "ResetUnavailableValue" when isFileSetting:
                ResetUnavailableValue = Conversions.BooleanFromString(value, false);
                return true;
            default:
                return base.TryParse(key, value);
        }
    }

    private string Localize(string attribute, string defaultValue)
    {
        if (Translation.Instance == null)
            return defaultValue;

        return Translation.Instance.LookUp($"INI:Controls:{parentName}:{Name}:{attribute}",
            fallbackKey: $"INI:Controls:Global:{Name}:{attribute}", defaultValue, TranslationNotificationLevel.Default);
    }

    public override void ReadFiles(IniSection section)
    {
        if (!isFileSetting || section == null)
            return;

        for (int i = 0; i < Items.Count; i++)
            itemFilesList.Add(FileSourceDestinationInfo.ParseFSDInfoList(section, $"Item{i}File"));
    }

    public override void Load()
    {
        if (!isFileSetting && WriteItemValue)
        {
            string value = UserINISettings.Instance.GetValue(SettingSection, SettingKey, null);
            int index = string.IsNullOrEmpty(value) ? -1 : Items.FindIndex(x => x.Tag == value);
            SelectedIndex = index < 0 ? DefaultValue : index;
        }
        else
        {
            SelectedIndex = UserINISettings.Instance.GetValue(SettingSection, SettingKey, DefaultValue);
        }

        originalState = SelectedIndex;
    }

    public override bool Save()
    {
        if (!isFileSetting)
        {
            if (WriteItemValue)
                UserINISettings.Instance.SetValue(SettingSection, SettingKey, Items[SelectedIndex].Tag);
            else
                UserINISettings.Instance.SetValue(SettingSection, SettingKey, SelectedIndex);

            return RestartRequired && SelectedIndex != originalState;
        }

        if (!Items[SelectedIndex].Selectable)
        {
            Logger.Log($"FileSettingDropDown: The selected item \"{Items[SelectedIndex].Text}\" ({Items[SelectedIndex].Tag}) is unavailable in {Name}.");
            return false;
        }

        for (int i = 0; i < itemFilesList.Count; i++)
        {
            if (i != SelectedIndex)
                itemFilesList[i].ForEach(f => f.Revert());
        }

        itemFilesList[SelectedIndex].ForEach(f => f.Apply());

        UserINISettings.Instance.SetValue(SettingSection, SettingKey, SelectedIndex);
        return RestartRequired && SelectedIndex != originalState;
    }

    public override bool Refresh()
    {
        if (!isFileSetting)
            return false;

        int currentValue = SelectedIndex;

        if (CheckAvailability)
        {
            for (int i = 0; i < Items.Count; i++)
                Items[i].Selectable = itemFilesList[i].All(fileInfo => File.Exists(fileInfo.SourcePath));

            if (ResetUnavailableValue && !Items[SelectedIndex].Selectable)
                SelectedIndex = DefaultValue;
        }

        return SelectedIndex != currentValue;
    }

    public override void ResetToDefault() => SelectedIndex = DefaultValue;
}
