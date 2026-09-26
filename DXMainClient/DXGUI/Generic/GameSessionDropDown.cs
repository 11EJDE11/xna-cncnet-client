using ClientCore.I18N;

using ClientGUI;

using ClientLogic.Options;

using DTAClient.Domain.Multiplayer;

using Rampastring.Tools;
using Rampastring.XNAUI;
using Rampastring.XNAUI.XNAControls;

namespace DTAClient.DXGUI.Generic;

/// <summary>
/// A game option drop-down for the game lobby or campaign. The option itself is <see cref="Option"/>; the
/// drop-down shows it and changes it.
/// </summary>
public class GameSessionDropDown : XNAClientDropDown, IGameSessionSetting
{
    public GameSessionDropDown(WindowManager windowManager) : base(windowManager) { }

    private GameOptionDefinitionBuilder builder;
    private GameOption option;

    /// <summary>
    /// The game option this drop-down shows. Created from the drop-down's INI section the first time it is used,
    /// which must be after the section has been read.
    /// </summary>
    public GameOption Option => option ??= CreateOption();

    public string OptionName => Option.Definition.OptionName;
    public bool AffectsSpawnIni => Option.AffectsSpawnIni;
    public bool AffectsMapCode => Option.AffectsMapCode;
    public bool AllowScoring => Option.AllowScoring;

    /// <summary>
    /// Whether this dropdown should be included in the GAME broadcast.
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
    /// Sort order for displaying icons in the GameInformationPanel and GameListBox.
    /// Lower values appear first.
    /// </summary>
    public int SortOrder => Option.Definition.SortOrder;

    private GameOptionDefinitionBuilder Builder => builder ??= new GameOptionDefinitionBuilder(
        Name, GameOptionKind.DropDown, isLobbyOption: false,
        (attributeName, defaultValue) => Translation.Instance.LookUp(this, attributeName, defaultValue));

    protected override void ParseControlINIAttribute(IniFile iniFile, string key, string value)
    {
        int itemCount = Builder.Items.Count;

        if (Builder.TryParse(iniFile, key, value))
        {
            switch (key)
            {
                case "Items":
                    for (int i = itemCount; i < Builder.Items.Count; i++)
                    {
                        GameOptionItem item = Builder.Items[i];
                        AddItem(new XNADropDownItem()
                        {
                            Text = item.Label,
                            Tag = item.Value,
                            Texture = item.IconName != null ? AssetLoader.LoadTexture(item.IconName) : null,
                        });
                    }

                    break;
                case "DefaultIndex":
                    SelectedIndex = Builder.DefaultValue;
                    break;
            }

            return;
        }

        base.ParseControlINIAttribute(iniFile, key, value);
    }

    private GameOption CreateOption()
    {
        var created = new GameOption(Builder.Build(), SelectedIndex);

        // Keep the option and the drop-down in sync both ways; each only raises its event on a real change
        SelectedIndexChanged += (s, e) => created.Value = SelectedIndex;
        created.PropertyChanged += (s, e) =>
        {
            if (e.PropertyName == nameof(GameOption.Value) && SelectedIndex != created.Value)
                SelectedIndex = created.Value;
        };

        return created;
    }

    public int Value
    {
        get => SelectedIndex;
        set => SelectedIndex = value;
    }

    public void ApplyDisallowedSideIndex(bool[] disallowedArray) => Option.ApplyDisallowedSideIndex(disallowedArray);

    public void ApplySpawnIniCode(IniFile spawnIni) => Option.ApplySpawnIniCode(spawnIni);

    public void ApplyMapCode(IniFile mapIni, GameMode gameMode) => Option.ApplyMapCode(mapIni, gameMode);

    public override void OnLeftClick(InputEventArgs inputEventArgs)
    {
        // FIXME there's a discrepancy with how base XNAUI handles this
        // it doesn't set handled if changing the setting is not allowed
        inputEventArgs.Handled = true;

        if (!AllowDropDown)
            return;

        base.OnLeftClick(inputEventArgs);
    }
}
