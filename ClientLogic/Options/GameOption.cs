using System.Collections.Generic;
using System.Linq;

using CommunityToolkit.Mvvm.ComponentModel;

using DTAClient.Domain.Multiplayer;
using DTAClient.DXGUI;
using DTAClient.DXGUI.Generic;

using Rampastring.Tools;

namespace ClientLogic.Options;

/// <summary>
/// A game option of a lobby or campaign: its definition and current value. It writes itself into spawn.ini and the
/// map, decides whether the game can be scored and which sides it disallows.
/// </summary>
public sealed partial class GameOption : ObservableObject, IGameSessionSetting
{
    public GameOption(GameOptionDefinition definition, int value)
    {
        Definition = definition;
        this.value = value;
        hostValue = definition.DefaultValue;
        userValue = definition.DefaultValue;
    }

    public GameOptionDefinition Definition { get; }

    /// <summary>
    /// The current value: 0/1 for check-boxes (unchecked/checked), the selected index for drop-downs.
    /// </summary>
    [ObservableProperty]
    private int value;

    /// <summary>
    /// The last value the game host chose. Options that a map or game mode doesn't force return to it when the
    /// map changes.
    /// </summary>
    [ObservableProperty]
    private int hostValue;

    /// <summary>The last value the local player chose; restored when they become the game host.</summary>
    [ObservableProperty]
    private int userValue;

    public string Name => Definition.Name;

    public bool IsCheckBox => Definition.Kind == GameOptionKind.CheckBox;

    public bool IsChecked => Value != 0;

    public bool AffectsSpawnIni => IsCheckBox
        ? !string.IsNullOrWhiteSpace(Definition.SpawnIniOption)
        : Definition.DataWriteMode != DropDownDataWriteMode.MAPCODE;

    public bool AffectsMapCode => IsCheckBox
        ? !string.IsNullOrWhiteSpace(Definition.CustomIniPath)
        : Definition.DataWriteMode == DropDownDataWriteMode.MAPCODE;

    public bool AllowScoring => !IsCheckBox ||
        !((Definition.MapScoringMode == CheckBoxMapScoringMode.DenyWhenChecked && IsChecked)
          || (Definition.MapScoringMode == CheckBoxMapScoringMode.DenyWhenUnchecked && !IsChecked));

    public bool BroadcastToLobby => Definition.BroadcastToLobby;

    public void ApplySpawnIniCode(IniFile spawnIni)
    {
        if (IsCheckBox)
        {
            if (!AffectsSpawnIni)
                return;

            string spawnIniValue = Definition.DisabledSpawnIniValue;
            if (IsChecked != Definition.Reversed)
            {
                spawnIniValue = Definition.EnabledSpawnIniValue;
            }

            spawnIni.SetStringValue("Settings", Definition.SpawnIniOption, spawnIniValue);
            return;
        }

        IReadOnlyList<GameOptionItem> items = Definition.Items;
        if (!AffectsSpawnIni || Value < 0 || Value >= items.Count)
            return;

        if (string.IsNullOrEmpty(Definition.SpawnIniOption))
        {
            Logger.Log("GameLobbyDropDown.WriteSpawnIniCode: " + Name + " has no associated spawn INI option!");
            return;
        }

        switch (Definition.DataWriteMode)
        {
            case DropDownDataWriteMode.BOOLEAN:
                spawnIni.SetBooleanValue("Settings", Definition.SpawnIniOption, Value > 0);
                break;
            case DropDownDataWriteMode.INDEX:
                spawnIni.SetIntValue("Settings", Definition.SpawnIniOption, Value);
                break;
            default:
            case DropDownDataWriteMode.STRING:
                spawnIni.SetStringValue("Settings", Definition.SpawnIniOption, items[Value].Value);
                break;
        }
    }

    /// <summary>
    /// Applies the option's disallowed side indexes to a bool array that determines which sides are disabled.
    /// </summary>
    public void ApplyDisallowedSideIndex(bool[] disallowedArray)
    {
        if (!IsCheckBox || Definition.DisallowedSideIndices.Count == 0)
            return;

        if (IsChecked != Definition.Reversed)
        {
            foreach (int sideNotAllowed in Definition.DisallowedSideIndices)
                disallowedArray[sideNotAllowed] = true;
        }
    }

    public void ApplyMapCode(IniFile mapIni, GameMode gameMode)
    {
        if (IsCheckBox)
        {
            if (!AffectsMapCode || IsChecked == Definition.Reversed)
                return;

            MapCodeHelper.ApplyMapCode(mapIni, Definition.CustomIniPath, gameMode);
            return;
        }

        if (!AffectsMapCode || Value < 0 || Value >= Definition.Items.Count)
            return;

        MapCodeHelper.ApplyMapCode(mapIni, Definition.Items[Value].Value, gameMode);
    }
}

/// <summary>The game options of a lobby, in INI order (which is also the order they are sent in).</summary>
public sealed class GameOptionSet
{
    private readonly List<GameOption> all = [];
    private readonly List<GameOption> checkBoxes = [];
    private readonly List<GameOption> dropDowns = [];

    public IReadOnlyList<GameOption> All => all;

    public IReadOnlyList<GameOption> CheckBoxes => checkBoxes;

    public IReadOnlyList<GameOption> DropDowns => dropDowns;

    public void Add(GameOption option)
    {
        all.Add(option);
        (option.IsCheckBox ? checkBoxes : dropDowns).Add(option);
    }

    public GameOption Find(string name) => all.FirstOrDefault(o => o.Name == name);
}
