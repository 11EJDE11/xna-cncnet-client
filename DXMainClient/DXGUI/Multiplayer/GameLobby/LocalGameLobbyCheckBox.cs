#nullable enable

using ClientCore;

using DTAClient.DXGUI.Generic;

using Rampastring.Tools;
using Rampastring.XNAUI;
using Rampastring.XNAUI.XNAControls;

namespace DTAClient.DXGUI.Multiplayer.GameLobby;

/// <summary>
/// A lobby option that only affects the local player. Written to spawn.ini and remembered in
/// the user's settings, but never sent in game option messages.
/// </summary>
public class LocalGameLobbyCheckBox : GameSessionCheckBox
{
    public LocalGameLobbyCheckBox(WindowManager windowManager) : base(windowManager) { }

    /// <summary>
    /// Key in the user's [Replays] settings that remembers this option.
    /// </summary>
    public string? UserSettingKey { get; private set; }

    /// <summary>
    /// When checked, writes ForceMultiplayer=true and switches compatible drop-down labels.
    /// </summary>
    public bool ForcesMultiplayerSession { get; private set; }

    public override void Initialize()
    {
        // Register with the game lobby that owns us, so local options stay out of
        // GameLobbyBase.CheckBoxes and therefore out of the broadcasted option list.
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
    }

    protected override void ParseControlINIAttribute(IniFile iniFile, string key, string value)
    {
        switch (key)
        {
            case "UserSettingKey":
                UserSettingKey = value;
                return;
            case "ForcesMultiplayerSession":
                ForcesMultiplayerSession = Conversions.BooleanFromString(value, false);
                return;
        }

        base.ParseControlINIAttribute(iniFile, key, value);
    }

    public void PersistValue()
    {
        if (string.IsNullOrWhiteSpace(UserSettingKey))
            return;

        UserINISettings.Instance.SetValue(UserINISettings.REPLAYS, UserSettingKey, Checked);

        UserINISettings.Instance.SaveSettings();
    }

    private void LoadPersistedValue()
    {
        if (string.IsNullOrWhiteSpace(UserSettingKey))
            return;

        Checked = UserINISettings.Instance.GetValue(UserINISettings.REPLAYS, UserSettingKey, Checked);
    }
}
