using ClientCore;
using ClientGUI.Options;
using Rampastring.Tools;
using Rampastring.XNAUI;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace ClientGUI.Settings
{
    /// <summary>
    /// A check-box that toggles between two sets of files and saves the setting to user settings file.
    /// </summary>
    public class FileSettingCheckBox : SettingCheckBoxBase, IFileSetting
    {
        public FileSettingCheckBox(WindowManager windowManager) : base(windowManager) { }

        public FileSettingCheckBox(WindowManager windowManager, bool defaultValue, string settingSection, string settingKey,
            bool checkAvailability = false, bool resetUnavailableValue = false, bool restartRequired = false)
            : base(windowManager, defaultValue, settingSection, settingKey, restartRequired)
        {
            CheckAvailability = checkAvailability;
            ResetUnavailableValue = resetUnavailableValue;
        }

        public bool CheckAvailability { get; set; }
        public bool ResetUnavailableValue { get; set; }

        private List<FileSourceDestinationInfo> enabledFiles = new List<FileSourceDestinationInfo>();
        private List<FileSourceDestinationInfo> disabledFiles = new List<FileSourceDestinationInfo>();

        private bool EnabledFilesComplete => this.IsStateAvailable(CHECKED_STATE);
        private bool DisabledFilesComplete => this.IsStateAvailable(UNCHECKED_STATE);

        // Backwards compatibility with old FileSettingCheckBox implementation.
        private bool useLegacyImplementation = false;
        private bool reversed = false;

        public override void GetAttributes(IniFile iniFile)
        {
            base.GetAttributes(iniFile);

            var section = iniFile.GetSection(Name);

            if (section == null)
                return;

            var files = FileSourceDestinationInfo.ParseFSDInfoList(section, "File");

            if (files.Count > 0)
            {
                enabledFiles = files;
                useLegacyImplementation = true;
            }
            else
            {
                enabledFiles = FileSourceDestinationInfo.ParseFSDInfoList(section, "EnabledFile");
                disabledFiles = FileSourceDestinationInfo.ParseFSDInfoList(section, "DisabledFile");
            }
        }

        protected override void ParseControlINIAttribute(IniFile iniFile, string key, string value)
        {
            switch (key)
            {
                case "CheckAvailability":
                    CheckAvailability = Conversions.BooleanFromString(value, false);
                    return;
                case "ResetUnavailableValue":
                    ResetUnavailableValue = Conversions.BooleanFromString(value, false);
                    return;
                case "Reversed":
                    reversed = Conversions.BooleanFromString(value, false);
                    return;
            }

            base.ParseControlINIAttribute(iniFile, key, value);
        }

        public bool RefreshSetting()
        {
            if (useLegacyImplementation)
                return false;

            bool currentValue = Checked;

            if (CheckAvailability)
            {
                Enabled = true;

                if (ResetUnavailableValue)
                {
                    if (DisabledFilesComplete != EnabledFilesComplete)
                        Checked = EnabledFilesComplete;
                    else if (!DisabledFilesComplete && !EnabledFilesComplete)
                        Checked = DefaultValue;
                }
            }

            return Checked != currentValue;
        }

        public void AddEnabledFile(string source, string destination, FileOperationOption option)
            => enabledFiles.Add(new FileSourceDestinationInfo(source, destination, option));

        public void AddDisabledFile(string source, string destination, FileOperationOption option)
            => disabledFiles.Add(new FileSourceDestinationInfo(source, destination, option));

        protected override IEnumerable<OptionEffect> GetBuiltInEffects(bool isChecked)
        {
            // The legacy implementation only has files for the enabled state, which is the unchecked one when reversed
            List<FileSourceDestinationInfo> files = useLegacyImplementation
                ? (isChecked != reversed ? enabledFiles : [])
                : (isChecked ? enabledFiles : disabledFiles);

            return files.Select(f => new FileEffect(f));
        }

        protected override bool ReadSavedValue()
        {
            if (useLegacyImplementation)
                return reversed != File.Exists(enabledFiles[0].DestinationPath);

            return UserINISettings.Instance.GetValue(SettingSection, SettingKey, DefaultValue);
        }

        public override bool Save()
        {
            if (useLegacyImplementation)
            {
                this.CommitPersistentEffects(CurrentState);
                return RestartRequired && (Checked != originalState);
            }

            bool canBeChecked = !CheckAvailability || EnabledFilesComplete;
            bool canBeUnchecked = !CheckAvailability || DisabledFilesComplete;

            if ((Checked && canBeChecked) || (!Checked && canBeUnchecked))
            {
                this.CommitPersistentEffects(CurrentState);
            }
            else // selected state is unavailable, don't do anything
            {
                Logger.Log($"{nameof(FileSettingCheckBox)}: " +
                    $"The selected state ({Checked}) is unavailable in {Name}");
                return false;
            }

            UserINISettings.Instance.SetValue(SettingSection, SettingKey, Checked);
            return RestartRequired && (Checked != originalState);
        }
    }
}
