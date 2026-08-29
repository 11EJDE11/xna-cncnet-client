#nullable enable

using ClientCore;

using DTAClient.DXGUI.Generic;

using Rampastring.Tools;
using Rampastring.XNAUI;
using Rampastring.XNAUI.XNAControls;

namespace DTAClient.DXGUI.Multiplayer.GameLobby;

/// <summary>A persisted lobby option that is not broadcast to other players.</summary>
public class LocalGameLobbyCheckBox : GameSessionCheckBox
{
    public LocalGameLobbyCheckBox(WindowManager windowManager) : base(windowManager) { }

    /// <summary>Optional key used to persist this option in [LocalGameOptions].</summary>
    public string? UserSettingKey { get; private set; }

    public override void Initialize()
    {
        // Register separately from broadcast game options.
        XNAControl parent = Parent;
        while (parent != null)
        {
            if (parent is GameLobbyBase gameLobby)
            {
                gameLobby.LocalCheckBoxes.Add(this);
                break;
            }

            parent = parent.Parent;
        }

        base.Initialize();

        LoadPersistedValue();
        CheckedChanged += (_, _) => PersistValue();
    }

    protected override void ParseControlINIAttribute(IniFile iniFile, string key, string value)
    {
        switch (key)
        {
            case "UserSettingKey":
                UserSettingKey = value;
                return;
        }

        base.ParseControlINIAttribute(iniFile, key, value);
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
}