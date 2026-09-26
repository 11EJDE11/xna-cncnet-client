using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

using ClientCore.Extensions;

using DTAClient.DXGUI.Generic;

using Rampastring.Tools;

namespace ClientLogic.Options;

public enum GameOptionKind
{
    CheckBox,
    DropDown,
}

/// <summary>One choice of a drop-down game option.</summary>
/// <param name="Value">What is written to spawn.ini (STRING mode) or the map code INI path (MAPCODE mode).</param>
/// <param name="Label">The translated text shown to the user.</param>
/// <param name="IconName">The icon texture name, or null.</param>
public sealed record GameOptionItem(string Value, string Label, string IconName);

/// <summary>
/// What a game option is, as defined by its INI section: how it is shown, what it writes to spawn.ini or the map,
/// and its default. Immutable; the current value is in <see cref="GameOption"/>.
/// </summary>
public sealed class GameOptionDefinition
{
    internal GameOptionDefinition(GameOptionDefinitionBuilder b)
    {
        Name = b.Name;
        Kind = b.Kind;
        Label = b.Label;
        OptionName = b.OptionName;
        SpawnIniOption = b.SpawnIniOption;
        EnabledSpawnIniValue = b.EnabledSpawnIniValue;
        DisabledSpawnIniValue = b.DisabledSpawnIniValue;
        CustomIniPath = b.CustomIniPath;
        Reversed = b.Reversed;
        DefaultValue = b.DefaultValue;
        MapScoringMode = b.MapScoringMode;
        DataWriteMode = b.DataWriteMode;
        Items = b.Items.ToList();
        DisallowedSideIndices = b.DisallowedSideIndices.ToList();
        BroadcastToLobby = b.BroadcastToLobby;
        ShowInGameList = b.ShowInGameList;
        ShowInGameListOnRight = b.ShowInGameListOnRight;
        ShowInGameInformationPanel = b.ShowInGameInformationPanel;
        ShowInGameInformationPanelAsIconOnly = b.ShowInGameInformationPanelAsIconOnly;
        ShowIconInGameLobby = b.ShowIconInGameLobby;
        ShowInFilters = b.ShowInFilters;
        EnabledIcon = b.EnabledIcon;
        DisabledIcon = b.DisabledIcon;
        SortOrder = b.SortOrder;
    }

    /// <summary>The control name, which identifies the option in settings and presets.</summary>
    public string Name { get; }

    public GameOptionKind Kind { get; }

    /// <summary>The translated check-box text, or null.</summary>
    public string Label { get; }

    /// <summary>The translated drop-down option name, or null.</summary>
    public string OptionName { get; }

    public string SpawnIniOption { get; }

    public string EnabledSpawnIniValue { get; }

    public string DisabledSpawnIniValue { get; }

    public string CustomIniPath { get; }

    /// <summary>For check-boxes: unchecked means enabled.</summary>
    public bool Reversed { get; }

    /// <summary>1/0 for check-boxes, the selected index for drop-downs.</summary>
    public int DefaultValue { get; }

    public CheckBoxMapScoringMode MapScoringMode { get; }

    public DropDownDataWriteMode DataWriteMode { get; }

    public IReadOnlyList<GameOptionItem> Items { get; }

    /// <summary>Sides that are disallowed while the check-box is enabled.</summary>
    public IReadOnlyList<int> DisallowedSideIndices { get; }

    public bool BroadcastToLobby { get; }

    public bool ShowInGameList { get; }

    public bool ShowInGameListOnRight { get; }

    public bool ShowInGameInformationPanel { get; }

    public bool ShowInGameInformationPanelAsIconOnly { get; }

    public bool ShowIconInGameLobby { get; }

    public bool ShowInFilters { get; }

    public string EnabledIcon { get; }

    public string DisabledIcon { get; }

    public int SortOrder { get; }

    /// <summary>Every property as text, for comparing definitions.</summary>
    public string Describe()
    {
        var sb = new StringBuilder();
        sb.Append(Kind).Append(' ').Append(Name)
            .Append(" label=").Append(Label)
            .Append(" optionName=").Append(OptionName)
            .Append(" spawn=").Append(SpawnIniOption).Append('[').Append(EnabledSpawnIniValue).Append('/').Append(DisabledSpawnIniValue).Append(']')
            .Append(" customIni=").Append(CustomIniPath)
            .Append(" reversed=").Append(Reversed)
            .Append(" default=").Append(DefaultValue)
            .Append(" scoring=").Append(MapScoringMode)
            .Append(" write=").Append(DataWriteMode)
            .Append(" items=").Append(string.Join("|", Items.Select(i => $"{i.Value}:{i.Label}:{i.IconName}")))
            .Append(" disallowedSides=").Append(string.Join(",", DisallowedSideIndices))
            .Append(" broadcast=").Append(BroadcastToLobby)
            .Append(" list=").Append(ShowInGameList).Append('/').Append(ShowInGameListOnRight)
            .Append(" info=").Append(ShowInGameInformationPanel).Append('/').Append(ShowInGameInformationPanelAsIconOnly)
            .Append(" lobbyIcon=").Append(ShowIconInGameLobby)
            .Append(" filters=").Append(ShowInFilters)
            .Append(" icons=").Append(EnabledIcon).Append('/').Append(DisabledIcon)
            .Append(" sort=").Append(SortOrder);
        return sb.ToString();
    }
}

/// <summary>
/// Reads a game option's INI keys into a <see cref="GameOptionDefinition"/>. Used by the XNA option controls while
/// they parse their INI section, and by <see cref="GameOptionCatalog"/>.
/// </summary>
public sealed class GameOptionDefinitionBuilder
{
    private const int DEFAULT_SORT_ORDER = 0;

    private readonly Func<string, string, string> localize;

    /// <param name="name">The control name.</param>
    /// <param name="kind">Check-box or drop-down.</param>
    /// <param name="isLobbyOption">Whether the option is a game lobby option (which can disallow sides).</param>
    /// <param name="localize">Translates an attribute: (attribute name, default value) → text.</param>
    public GameOptionDefinitionBuilder(string name, GameOptionKind kind, bool isLobbyOption, Func<string, string, string> localize)
    {
        Name = name;
        Kind = kind;
        IsLobbyOption = isLobbyOption;
        this.localize = localize;
    }

    public string Name { get; }
    public GameOptionKind Kind { get; }
    public bool IsLobbyOption { get; }
    public string Label { get; set; }
    public string OptionName { get; private set; }
    public string SpawnIniOption { get; private set; }
    public string EnabledSpawnIniValue { get; private set; } = "True";
    public string DisabledSpawnIniValue { get; private set; } = "False";
    public string CustomIniPath { get; private set; }
    public bool Reversed { get; private set; }
    public int DefaultValue { get; private set; }
    public CheckBoxMapScoringMode MapScoringMode { get; private set; } = CheckBoxMapScoringMode.Irrelevant;
    public DropDownDataWriteMode DataWriteMode { get; private set; } = DropDownDataWriteMode.BOOLEAN;
    public List<GameOptionItem> Items { get; } = [];
    public List<int> DisallowedSideIndices { get; } = [];
    public bool BroadcastToLobby { get; private set; }
    public bool ShowInGameList { get; private set; }
    public bool ShowInGameListOnRight { get; private set; }
    public bool ShowInGameInformationPanel { get; private set; }
    public bool ShowInGameInformationPanelAsIconOnly { get; private set; }
    public bool ShowIconInGameLobby { get; private set; }
    public bool ShowInFilters { get; private set; }
    public string EnabledIcon { get; private set; }
    public string DisabledIcon { get; private set; }
    public int SortOrder { get; private set; } = DEFAULT_SORT_ORDER;

    /// <summary>
    /// Reads one INI key of the option. Returns false for keys that aren't game option keys; the control handles
    /// those itself (position, text, ...).
    /// </summary>
    public bool TryParse(IniFile iniFile, string key, string value)
    {
        switch (key)
        {
            case "BroadcastToLobby":
                BroadcastToLobby = Conversions.BooleanFromString(value, false);
                return true;
            case "ShowInGameList":
                ShowInGameList = Conversions.BooleanFromString(value, false);
                return true;
            case "ShowInGameListOnRight":
                ShowInGameListOnRight = Conversions.BooleanFromString(value, false);
                return true;
            case "ShowInGameInformationPanel":
                ShowInGameInformationPanel = Conversions.BooleanFromString(value, false);
                return true;
            case "ShowInGameInformationPanelAsIconOnly":
                ShowInGameInformationPanelAsIconOnly = Conversions.BooleanFromString(value, false);
                return true;
            case "ShowIconInGameLobby":
                ShowIconInGameLobby = Conversions.BooleanFromString(value, false);
                return true;
            case "ShowInFilters":
                ShowInFilters = Conversions.BooleanFromString(value, false);
                return true;
            case "SortOrder":
                SortOrder = int.Parse(value);
                return true;
        }

        return Kind == GameOptionKind.CheckBox ? TryParseCheckBox(key, value) : TryParseDropDown(iniFile, key, value);
    }

    private bool TryParseCheckBox(string key, string value)
    {
        switch (key)
        {
            case "SpawnIniOption":
                SpawnIniOption = value;
                return true;
            case "EnabledSpawnIniValue":
                EnabledSpawnIniValue = value;
                return true;
            case "DisabledSpawnIniValue":
                DisabledSpawnIniValue = value;
                return true;
            case "CustomIniPath":
                CustomIniPath = value;
                return true;
            case "Reversed":
                Reversed = Conversions.BooleanFromString(value, false);
                return true;
            case "Checked":
                DefaultValue = Conversions.BooleanFromString(value, false) ? 1 : 0;
                return true;
            case "MapScoringMode":
                MapScoringMode = (CheckBoxMapScoringMode)Enum.Parse(typeof(CheckBoxMapScoringMode), value);
                return true;
            case "EnabledIcon":
                EnabledIcon = value;
                return true;
            case "DisabledIcon":
                DisabledIcon = value;
                return true;
            case "DisallowedSideIndex" when IsLobbyOption:
            case "DisallowedSideIndices" when IsLobbyOption:
                List<int> sides = value.SplitWithCleanup()
                    .Select(s => Conversions.IntFromString(s, -1))
                    .Distinct()
                    .ToList();
                DisallowedSideIndices.AddRange(sides.Where(s => !DisallowedSideIndices.Contains(s)));
                return true;
        }

        return false;
    }

    private bool TryParseDropDown(IniFile iniFile, string key, string value)
    {
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
                    Items.Add(new GameOptionItem(
                        items[i],
                        localize($"Item{i}", hasLabel ? itemLabels[i] : items[i]),
                        !string.IsNullOrEmpty(iconName) ? iconName : null));
                }

                return true;
            case "DataWriteMode":
                if (value.ToUpper() == "INDEX")
                    DataWriteMode = DropDownDataWriteMode.INDEX;
                else if (value.ToUpper() == "BOOLEAN")
                    DataWriteMode = DropDownDataWriteMode.BOOLEAN;
                else if (value.ToUpper() == "MAPCODE")
                    DataWriteMode = DropDownDataWriteMode.MAPCODE;
                else
                    DataWriteMode = DropDownDataWriteMode.STRING;
                return true;
            case "SpawnIniOption":
                SpawnIniOption = value;
                return true;
            case "DefaultIndex":
                DefaultValue = int.Parse(value);
                return true;
            case "OptionName":
                OptionName = localize("OptionName", value);
                return true;
        }

        return false;
    }

    public GameOptionDefinition Build() => new(this);
}
