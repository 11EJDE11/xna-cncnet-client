using System;

using ClientCore;

using DTAClient.DXGUI.Generic;

using Rampastring.Tools;
using Rampastring.XNAUI;
using Rampastring.XNAUI.XNAControls;

namespace DTAClient.DXGUI.Campaign;

public class CampaignCheckBox : GameSessionCheckBox
{
    public CampaignCheckBox(WindowManager windowManager) : base(windowManager) { }

    public bool ResetToDefaultOnGameExit { get; private set; }

    /// <summary>Optional key used to persist this option in [LocalGameOptions].</summary>
    public string UserSettingKey { get; private set; } = string.Empty;

    public override void Initialize()
    {
        // Find the campaign selector that this control belongs to and register ourselves as a game option.

        XNAControl parent = Parent;
        while (true)
        {
            if (parent == null)
                break;

            // oh no, we have a circular class reference here!
            if (parent is CampaignSelector configView)
            {
                configView.CheckBoxes.Add(this);
                break;
            }

            parent = parent.Parent;
        }

        base.Initialize();

        LoadPersistedValue();
        CheckedChanged += (_, _) => PersistValue();
    }

    private void PersistValue()
    {
        if (string.IsNullOrWhiteSpace(UserSettingKey))
            return;

        UserINISettings.Instance.SetValue(UserINISettings.LOCAL_GAME_OPTIONS, UserSettingKey, Checked);
        UserINISettings.Instance.SaveSettings();
    }

    private void LoadPersistedValue()
    {
        if (string.IsNullOrWhiteSpace(UserSettingKey))
            return;

        Checked = UserINISettings.Instance.GetValue(UserINISettings.LOCAL_GAME_OPTIONS, UserSettingKey, Checked);
    }

    protected override void ParseControlINIAttribute(IniFile iniFile, string key, string value)
    {
        switch (key)
        {
            case "UserSettingKey":
                UserSettingKey = value;
                return;

            case "ResetToDefaultOnGameExit":
                ResetToDefaultOnGameExit = Conversions.BooleanFromString(value, false);
                return;

            case "CustomIniPath" when !ClientConfiguration.Instance.CopyMissionsToSpawnmapINI:
                throw new Exception($"Campaign settings can't affect map code if {nameof(ClientConfiguration.Instance.CopyMissionsToSpawnmapINI)} is disabled!\n\n"
                    + $"Offending setting control: {Name}");
        }

        base.ParseControlINIAttribute(iniFile, key, value);
    }
}