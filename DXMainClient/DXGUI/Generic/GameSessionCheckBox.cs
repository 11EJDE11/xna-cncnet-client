using System;

using ClientCore.I18N;

using ClientGUI;

using ClientLogic.Options;

using DTAClient.Domain.Multiplayer;
using DTAClient.DXGUI.Multiplayer.GameLobby;

using Rampastring.Tools;
using Rampastring.XNAUI;

namespace DTAClient.DXGUI.Generic;

/// <summary>
/// A game option check box for the game lobby or campaign. The option itself is <see cref="Option"/>; the check
/// box shows it and changes it.
/// </summary>
public class GameSessionCheckBox : XNAClientCheckBox, IGameSessionSetting
{
    public GameSessionCheckBox(WindowManager windowManager) : base (windowManager) { }

    private GameOptionDefinitionBuilder builder;
    private GameOption option;

    public bool AllowChanges { get; set; } = true;

    /// <summary>
    /// The game option this check box shows. Created from the check box's INI section the first time it is used,
    /// which must be after the section has been read.
    /// </summary>
    public GameOption Option => option ??= CreateOption();

    public bool AffectsSpawnIni => Option.AffectsSpawnIni;
    public bool AffectsMapCode => Option.AffectsMapCode;
    public bool AllowScoring => Option.AllowScoring;

    /// <summary>
    /// Whether this checkbox should be included in the GAME broadcast.
    /// </summary>
    public bool BroadcastToLobby => Option.Definition.BroadcastToLobby;

    /// <summary>
    /// Whether the icon/text should be shown in the game list.
    /// </summary>
    public bool ShowInGameList => Option.Definition.ShowInGameList;

    /// <summary>
    /// Whether the icon should be shown on the right side of the game list.
    /// Only applies if ShowInGameList is true.
    /// </summary>
    public bool ShowInGameListOnRight => Option.Definition.ShowInGameListOnRight;

    /// <summary>
    /// Whether the icon/text should be shown in the game information panel.
    /// </summary>
    public bool ShowInGameInformationPanel => Option.Definition.ShowInGameInformationPanel;

    /// <summary>
    /// Whether to show only the icon (without text) in the game information panel.
    /// Only applies if ShowInGameInformationPanel is true.
    /// </summary>
    public bool ShowInGameInformationPanelAsIconOnly => Option.Definition.ShowInGameInformationPanelAsIconOnly;

    /// <summary>
    /// Whether the icon should be shown in the game lobby control itself.
    /// </summary>
    public bool ShowIconInGameLobby => Option.Definition.ShowIconInGameLobby;

    /// <summary>
    /// Whether this setting should be filterable and shown in the filters panel.
    /// </summary>
    public bool ShowInFilters => Option.Definition.ShowInFilters;

    /// <summary>
    /// The texture name for the icon when setting is enabled.
    /// </summary>
    public string EnabledIcon => Option.Definition.EnabledIcon;

    /// <summary>
    /// The texture name for the icon when setting is disabled.
    /// </summary>
    public string DisabledIcon => Option.Definition.DisabledIcon;

    /// <summary>
    /// Sort order for displaying icons in the GameInformationPanel and GameListBox.
    /// Lower values appear first.
    /// </summary>
    public int SortOrder => Option.Definition.SortOrder;

    private GameOptionDefinitionBuilder Builder => builder ??= new GameOptionDefinitionBuilder(
        Name, GameOptionKind.CheckBox, isLobbyOption: this is GameLobbyCheckBox,
        (attributeName, defaultValue) => Translation.Instance.LookUp(this, attributeName, defaultValue));

    protected override void ParseControlINIAttribute(IniFile iniFile, string key, string value)
    {
        if (Builder.TryParse(iniFile, key, value))
        {
            if (key == "Checked")
                Checked = Builder.DefaultValue != 0;

            return;
        }

        base.ParseControlINIAttribute(iniFile, key, value);
    }

    private GameOption CreateOption()
    {
        Builder.Label = Text;
        var created = new GameOption(Builder.Build(), Checked ? 1 : 0);

        // Keep the option and the check box in sync both ways; each only raises its event on a real change
        CheckedChanged += (s, e) => created.Value = Checked ? 1 : 0;
        created.PropertyChanged += (s, e) =>
        {
            if (e.PropertyName == nameof(GameOption.Value) && Checked != created.IsChecked)
                Checked = created.IsChecked;
        };

        return created;
    }

    public int Value
    {
        get => Checked ? 1 : 0;  // 0 = unchecked/off, 1 = checked/on
        set => Checked = value != 0;  // 0 = unchecked/off, 1 = checked/on
    }

    public void ApplySpawnIniCode(IniFile spawnIni) => Option.ApplySpawnIniCode(spawnIni);

    public void ApplyDisallowedSideIndex(bool[] disallowedArray) => Option.ApplyDisallowedSideIndex(disallowedArray);

    public void ApplyMapCode(IniFile mapIni, GameMode gameMode) => Option.ApplyMapCode(mapIni, gameMode);

    public override void OnLeftClick(InputEventArgs inputEventArgs)
    {
        // FIXME there's a discrepancy with how base XNAUI handles this
        // it doesn't set handled if changing the setting is not allowed
        inputEventArgs.Handled = true;

        if (!AllowChanges)
            return;

        base.OnLeftClick(inputEventArgs);
    }

    public void ResetToDefault()
    {
        if (!AllowChanges)
            throw new InvalidOperationException("Cannot reset to default when changes are not allowed.");

        Checked = Option.Definition.DefaultValue != 0;
    }
}
