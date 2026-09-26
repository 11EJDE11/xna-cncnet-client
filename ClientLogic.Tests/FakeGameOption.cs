using System.Collections.Generic;

using DTAClient.Domain.Multiplayer;
using DTAClient.DXGUI;

using Rampastring.Tools;

namespace ClientLogic.Tests;

/// <summary>
/// A game option without a control. It writes one spawn.ini value, can add a map code section
/// and can disallow sides, like the lobby's check-boxes and drop-downs do.
/// </summary>
internal sealed class FakeGameOption : IGameSessionSetting
{
    private readonly string spawnIniKey;
    private readonly string[] spawnIniValues;

    /// <param name="name">The option name.</param>
    /// <param name="spawnIniKey">The [Settings] key to write, or null to write nothing.</param>
    /// <param name="spawnIniValues">The value to write for each <see cref="Value"/> (0/1 for a check-box).</param>
    /// <param name="value">The current value.</param>
    public FakeGameOption(string name, string spawnIniKey, string[] spawnIniValues, int value)
    {
        Name = name;
        this.spawnIniKey = spawnIniKey;
        this.spawnIniValues = spawnIniValues;
        Value = value;
    }

    public static FakeGameOption CheckBox(string name, string spawnIniKey, bool isChecked) =>
        new(name, spawnIniKey, ["False", "True"], isChecked ? 1 : 0);

    public static FakeGameOption DropDown(string name, string spawnIniKey, int selectedIndex, params string[] values) =>
        new(name, spawnIniKey, values, selectedIndex);

    public string Name { get; }

    public bool AffectsSpawnIni => spawnIniKey != null;

    public bool AffectsMapCode => MapCodeSection != null;

    public bool AllowScoring => true;

    public bool BroadcastToLobby => true;

    public int Value { get; set; }

    /// <summary>When set and the value is not 0, this section is added to the map.</summary>
    public string MapCodeSection { get; init; }

    /// <summary>Sides that are disallowed while the value is not 0.</summary>
    public List<int> DisallowedSides { get; init; } = [];

    public void ApplySpawnIniCode(IniFile spawnIni)
    {
        if (AffectsSpawnIni)
            spawnIni.SetStringValue("Settings", spawnIniKey, spawnIniValues[Value]);
    }

    public void ApplyDisallowedSideIndex(bool[] disallowedArray)
    {
        if (Value == 0)
            return;

        foreach (int side in DisallowedSides)
            disallowedArray[side] = true;
    }

    public void ApplyMapCode(IniFile mapIni, GameMode gameMode)
    {
        if (AffectsMapCode && Value != 0)
            mapIni.SetStringValue(MapCodeSection, "AppliedBy", Name);
    }
}
