using ClientCore;
using ClientGUI.Options;
using Rampastring.Tools;
using Rampastring.XNAUI;
using System.Collections.Generic;
using System.Linq;

namespace ClientGUI.Settings
{
    /// <summary>
    /// A dropdown that switches between multiple sets of files.
    /// </summary>
    public class FileSettingDropDown : SettingDropDownBase, IFileSetting
    {
        public FileSettingDropDown(WindowManager windowManager) : base(windowManager) { }

        public FileSettingDropDown(WindowManager windowManager, int defaultValue, string settingSection, string settingKey,
            bool checkAvailability = false, bool resetUnavailableValue = false, bool restartRequired = false)
            : base(windowManager, defaultValue, settingSection, settingKey, restartRequired)
        {
            CheckAvailability = checkAvailability;
            ResetUnavailableValue = resetUnavailableValue;
        }

        private readonly List<List<FileSourceDestinationInfo>> itemFilesList = new List<List<FileSourceDestinationInfo>>();

        public bool CheckAvailability { get; private set; }
        public bool ResetUnavailableValue { get; private set; }

        public override void GetAttributes(IniFile iniFile)
        {
            base.GetAttributes(iniFile);

            var section = iniFile.GetSection(Name);
            if (section == null)
                return;

            for (int i = 0; i < Items.Count; i++)
                itemFilesList.Add(FileSourceDestinationInfo.ParseFSDInfoList(section, $"Item{i}File"));
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
            }

            base.ParseControlINIAttribute(iniFile, key, value);
        }

        public bool RefreshSetting()
        {
            int currentValue = SelectedIndex;

            if (CheckAvailability)
            {
                for (int i = 0; i < Items.Count; i++)
                    Items[i].Selectable = this.IsStateAvailable(i);

                if (ResetUnavailableValue && !Items[SelectedIndex].Selectable)
                    SelectedIndex = DefaultValue;
            }

            return SelectedIndex != currentValue;
        }

        public void AddFile(int itemIndex, string source, string destination, FileOperationOption option)
        {
            if (itemIndex < 0 || itemIndex >= Items.Count)
                return;

            if (itemFilesList.Count < itemIndex + 1)
                itemFilesList.Add(new List<FileSourceDestinationInfo>());

            itemFilesList[itemIndex].Add(new FileSourceDestinationInfo(source, destination, option));
        }

        protected override IEnumerable<OptionEffect> GetBuiltInEffects(int itemIndex)
        {
            if (itemIndex >= itemFilesList.Count)
                return [];

            return itemFilesList[itemIndex].Select(f => new FileEffect(f));
        }

        protected override int ReadSavedValue() => UserINISettings.Instance.GetValue(SettingSection, SettingKey, DefaultValue);

        public override bool Save()
        {
            if (Items[SelectedIndex].Selectable)
            {
                this.CommitPersistentEffects(SelectedIndex);
            }
            else // selected item is unavailable, don't do anything
            {
                Logger.Log($"{nameof(FileSettingDropDown)}: " +
                    $"The selected item \"{Items[SelectedIndex].Text}\" ({Items[SelectedIndex].Tag}) is unavailable in {Name}.");
                return false;
            }

            UserINISettings.Instance.SetValue(SettingSection, SettingKey, SelectedIndex);
            return RestartRequired && (SelectedIndex != originalState);
        }
    }
}
