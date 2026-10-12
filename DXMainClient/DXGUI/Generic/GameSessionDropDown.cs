using System.Collections.Generic;
using System.Linq;

using ClientCore.Extensions;
using ClientCore.I18N;

using ClientGUI;

using DTAClient.Domain.Multiplayer;

using Rampastring.Tools;
using Rampastring.XNAUI;
using Rampastring.XNAUI.XNAControls;

namespace DTAClient.DXGUI.Generic;

/// <summary>
/// A game option drop-down for the game lobby or campaign.
/// </summary>
// TODO split the logic between descendants better and clean up
public class GameSessionDropDown : XNAClientDropDown, IGameSessionSetting
{

    private const int DEFAULT_SORT_ORDER = 0;

    public GameSessionDropDown(WindowManager windowManager) : base(windowManager) { }

    public string OptionName { get; private set; }
    public bool AffectsSpawnIni => HasLegacySpawnIniOption || itemEffects.Exists(e => e.AffectsSpawnIni);
    public bool AffectsMapCode => dataWriteMode == DropDownDataWriteMode.MAPCODE || itemEffects.Exists(e => e.AffectsMapCode);
    public bool AllowScoring => true;  // TODO

    private DropDownDataWriteMode dataWriteMode = DropDownDataWriteMode.BOOLEAN;

    private string spawnIniOption = string.Empty;

    private bool HasLegacySpawnIniOption => dataWriteMode != DropDownDataWriteMode.MAPCODE && !string.IsNullOrEmpty(spawnIniOption);

    /// <summary>
    /// Effects of each item, by item index, defined with ItemEffects.
    /// </summary>
    private List<GameOptionEffects> itemEffects = [];

    private int defaultIndex;

    /// <summary>
    /// Whether this dropdown should be included in the GAME broadcast.
    /// </summary>
    public bool BroadcastToLobby { get; private set; }

    /// <summary>
    /// Whether the icon/text should be shown in the game list.
    /// </summary>
    public bool ShowInGameList { get; private set; }

    /// <summary>
    /// Whether the icon should be shown on the right side of the game list.
    /// Only applies if ShowInGameList is true.
    /// </summary>
    public bool ShowInGameListOnRight { get; private set; }

    /// <summary>
    /// Whether the icon/text should be shown in the game information panel.
    /// </summary>
    public bool ShowInGameInformationPanel { get; private set; }

    /// <summary>
    /// Whether to show only the icon (without text) in the game information panel.
    /// Only applies if ShowInGameInformationPanel is true.
    /// </summary>
    public bool ShowInGameInformationPanelAsIconOnly { get; private set; }

    /// <summary>
    /// Whether the icon should be shown in the game lobby control itself.
    /// </summary>
    public bool ShowIconInGameLobby { get; private set; }

    /// <summary>
    /// Whether this setting should be filterable and shown in the filters panel.
    /// </summary>
    public bool ShowInFilters { get; private set; }

    /// <summary>
    /// Sort order for displaying icons in the GameInformationPanel and GameListBox.
    /// Lower values appear first.
    /// </summary>
    public int SortOrder { get; private set; } = DEFAULT_SORT_ORDER;

    protected override void ParseControlINIAttribute(IniFile iniFile, string key, string value)
    {
        // shorthand for localization function
        static string Localize(XNAControl control, string attributeName, string defaultValue, TranslationNotificationLevel notificationLevel = TranslationNotificationLevel.Default)
            => Translation.Instance.LookUp(control, attributeName, defaultValue, notificationLevel);

        switch (key)
        {
            case "Items":
                string[] items = value.SplitWithCleanup();
                string[] itemLabels = iniFile.GetStringListValue(Name, "ItemLabels", "");
                string[] iconNames = iniFile.GetStringListValue(Name, "Icons", "");
                for (int i = 0; i < items.Length; i++)
                {
                    bool hasLabel = itemLabels.Length > i && !string.IsNullOrEmpty(itemLabels[i]);
                    string iconName = iconNames.Length > i ? iconNames[i] : null;
                    XNADropDownItem item = new()
                    {
                        Text = Localize(this, $"Item{i}",
                            hasLabel ? itemLabels[i] : items[i]),
                        Tag = items[i],
                        Texture = !string.IsNullOrEmpty(iconName) ? AssetLoader.LoadTexture(iconName) : null,
                    };
                    AddItem(item);
                }
                return;
            case "DataWriteMode":
                if (value.ToUpper() == "INDEX")
                    dataWriteMode = DropDownDataWriteMode.INDEX;
                else if (value.ToUpper() == "BOOLEAN")
                    dataWriteMode = DropDownDataWriteMode.BOOLEAN;
                else if (value.ToUpper() == "MAPCODE")
                    dataWriteMode = DropDownDataWriteMode.MAPCODE;
                else
                    dataWriteMode = DropDownDataWriteMode.STRING;
                return;
            case "SpawnIniOption":
                spawnIniOption = value;
                return;
            case "ItemEffects":
                // empty entries are kept so that the list stays aligned with item indices
                itemEffects = value.Split(',')
                    .Select(section => GameOptionEffects.FromIniSection(iniFile, section.Trim(), Name))
                    .ToList();
                return;
            case "DefaultIndex":
                SelectedIndex = int.Parse(value);
                defaultIndex = SelectedIndex;
                return;
            case "OptionName":
                OptionName = Localize(this, "OptionName", value);
                return;
            case "BroadcastToLobby":
                BroadcastToLobby = Conversions.BooleanFromString(value, false);
                return;
            case "ShowInGameList":
                ShowInGameList = Conversions.BooleanFromString(value, false);
                return;
            case "ShowInGameListOnRight":
                ShowInGameListOnRight = Conversions.BooleanFromString(value, false);
                return;
            case "ShowInGameInformationPanel":
                ShowInGameInformationPanel = Conversions.BooleanFromString(value, false);
                return;
            case "ShowInGameInformationPanelAsIconOnly":
                ShowInGameInformationPanelAsIconOnly = Conversions.BooleanFromString(value, false);
                return;
            case "ShowIconInGameLobby":
                ShowIconInGameLobby = Conversions.BooleanFromString(value, false);
                return;
            case "ShowInFilters":
                ShowInFilters = Conversions.BooleanFromString(value, false);
                return;
            case "SortOrder":
                SortOrder = int.Parse(value);
                return;
        }

        base.ParseControlINIAttribute(iniFile, key, value);
    }

    public override void Initialize()
    {
        base.Initialize();

        // INI attributes can come in any order, so this can only be checked once they're all parsed
        if (itemEffects.Count > 0 && itemEffects.Count != Items.Count)
        {
            Logger.Log($"GameSessionDropDown: {Name} has {Items.Count} items but {itemEffects.Count} ItemEffects entries. "
                + "Items without a matching entry have no effects and extra entries are ignored.");
        }
    }

    public int Value
    {
        get => SelectedIndex;
        set => SelectedIndex = value;
    }

    /// <summary>
    /// Gets the effects of the item with the given index. Effects defined
    /// by SpawnIniOption and DataWriteMode come first, followed by ItemEffects.
    /// </summary>
    private GameOptionEffects GetItemEffects(int index)
    {
        var effects = new GameOptionEffects();

        if (dataWriteMode == DropDownDataWriteMode.MAPCODE)
        {
            effects.AddMapCode(Items[index].Tag.ToString());
        }
        else if (HasLegacySpawnIniOption)
        {
            string spawnIniValue = dataWriteMode switch
            {
                DropDownDataWriteMode.BOOLEAN => Conversions.BooleanToString(index > 0, BooleanStringStyle.TRUEFALSE),
                DropDownDataWriteMode.INDEX => index.ToString(),
                _ => Items[index].Tag.ToString(),
            };

            effects.AddSpawnIniOption(spawnIniOption, spawnIniValue);
        }

        if (index < itemEffects.Count)
            effects.Add(itemEffects[index]);

        return effects;
    }

    public void ApplySpawnIniCode(IniFile spawnIni)
    {
        if (SelectedIndex < 0 || SelectedIndex >= Items.Count)
            return;

        if (!AffectsSpawnIni && !AffectsMapCode)
        {
            Logger.Log("GameLobbyDropDown.WriteSpawnIniCode: " + Name + " has no associated spawn INI option!");
            return;
        }

        GetItemEffects(SelectedIndex).ApplySpawnIniCode(spawnIni);
    }

    public void ApplyMapCode(IniFile mapIni, GameMode gameMode)
    {
        if (!AffectsMapCode || SelectedIndex < 0 || SelectedIndex >= Items.Count) return;

        GetItemEffects(SelectedIndex).ApplyMapCode(mapIni, gameMode);
    }

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