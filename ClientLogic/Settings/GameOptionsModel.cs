using System;

using ClientCore;
using ClientCore.Enums;

using CommunityToolkit.Mvvm.ComponentModel;

using DTAClient.Domain.Multiplayer.CnCNet;

namespace ClientLogic.Settings;

/// <summary>
/// The options window's Game tab, as DXMainClient's GameOptionsPanel: the scroll rate, the built-in in-game setting
/// check-boxes (the theme can change their keys) and the player name.
/// </summary>
public sealed partial class GameOptionsModel : OptionsPanelModel
{
    private const string TEXT_BACKGROUND_COLOR_TRANSPARENT = "0";
    private const string TEXT_BACKGROUND_COLOR_BLACK = "12";
    public const int MAX_SCROLL_RATE = 6;

    public GameOptionsModel() : base("GameOptionsPanel")
    {
        IsTiberianSun = ClientConfiguration.Instance.ClientGameType == ClientType.TS;

        // In the XNA panel's AddChild order (it decides where new keys go in the settings INI)
        if (IsTiberianSun)
        {
            AddSetting(new CheckBoxSetting("chkBlackChatBackground", false, UserINISettings.OPTIONS, "TextBackgroundColor", true,
                TEXT_BACKGROUND_COLOR_BLACK, TEXT_BACKGROUND_COLOR_TRANSPARENT));
            AddSetting(new CheckBoxSetting("chkAltToUndeploy", true, UserINISettings.OPTIONS, "MoveToUndeploy"));
        }
        else
        {
            AddSetting(new CheckBoxSetting("chkShowHiddenObjects", true, UserINISettings.OPTIONS, "ShowHidden"));
        }

        AddSetting(new CheckBoxSetting("chkScrollCoasting", true, UserINISettings.OPTIONS, "ScrollMethod", true, "0", "1"));
        AddSetting(new CheckBoxSetting("chkTargetLines", true, UserINISettings.OPTIONS, "UnitActionLines"));
        AddSetting(new CheckBoxSetting("chkTooltips", true, UserINISettings.OPTIONS, "ToolTips"));

        playerName = ProgramConstants.PLAYERNAME;
    }

    /// <summary>TS has the chat background and Alt-undeploy options; the others have Show Hidden Objects.</summary>
    public bool IsTiberianSun { get; }

    /// <summary>0 (slowest) to <see cref="MAX_SCROLL_RATE"/>; the INI stores it reversed.</summary>
    [ObservableProperty]
    private int scrollRate;

    [ObservableProperty]
    private string playerName;

    public int MaxNameLength => ClientConfiguration.Instance.MaxNameLength;

    public override void Load()
    {
        base.Load();

        int rate = ReverseScrollRate(IniSettings.ScrollRate);
        if (rate >= 0 && rate <= MAX_SCROLL_RATE)
            ScrollRate = rate;

        PlayerName = UserINISettings.Instance.PlayerName;
    }

    public override bool Save()
    {
        bool restartRequired = base.Save();

        IniSettings.ScrollRate.Value = ReverseScrollRate(ScrollRate);

        string name = NameValidator.GetValidOfflineName(PlayerName);
        if (name.Length > 0)
            IniSettings.PlayerName.Value = name;

        return restartRequired;
    }

    private static int ReverseScrollRate(int scrollRate) => Math.Abs(scrollRate - MAX_SCROLL_RATE);
}
