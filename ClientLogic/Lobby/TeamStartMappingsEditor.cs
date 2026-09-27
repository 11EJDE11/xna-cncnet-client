using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;

using ClientCore.Extensions;

using DTAClient.Domain.Multiplayer;

namespace ClientLogic.Lobby;

/// <summary>
/// The auto-allying part of the extra player options panel, as DXMainClient's PlayerExtraOptionsPanel,
/// TeamStartMappingsPanel and TeamStartMappingPanel drive it: a team drop-down per start location (index into
/// <see cref="TeamStartMapping.TEAMS"/>, -1 for none) and the map's presets. It follows the XNA controls' event order
/// (a drop-down reports a change only when its index changes; choosing a preset sets the drop-downs in one
/// extra options change; a changed drop-down sets the preset back to "Custom").
/// </summary>
public sealed class TeamStartMappingsEditor
{
    public const int MAX_START_COUNT = 8;

    private readonly PlayerExtraOptionsState state;
    private readonly int[] teamIndexes = Enumerable.Repeat(-1, MAX_START_COUNT).ToArray();
    private readonly bool[] slotEnabled = new bool[MAX_START_COUNT];
    private readonly List<(string Name, List<TeamStartMapping> Mappings)> presets = [];
    private GameModeMap gameModeMap;
    private bool ignoreMappingChanges;
    private bool updatingMappingsFromControls;
    private bool isHost;

    public TeamStartMappingsEditor(PlayerExtraOptionsState state)
    {
        this.state = state;
        CustomPresetName = "Custom".L10N("Client:Main:CustomPresetName");

        // PlayerExtraOptionsPanel.Initialize, before it is bound to the lobby's state
        RefreshTeamStartMappingsPanel(useTeamStartMappings: false);
        state.PropertyChanged += State_PropertyChanged;
    }

    public string CustomPresetName { get; }

    /// <summary>The team drop-downs' items.</summary>
    public static IReadOnlyList<string> Teams => TeamStartMapping.TEAMS;

    /// <summary>Raised when a drop-down's value, enabled state or the preset list changed.</summary>
    public event EventHandler Changed;

    public int GetTeamIndex(int slot) => teamIndexes[slot];

    public bool CanChangeSlot(int slot) => slotEnabled[slot];

    public IReadOnlyList<string> PresetNames => presets.Select(p => p.Name).ToList();

    public int PresetIndex { get; private set; }

    /// <summary>The preset drop-down can be opened (the host, with auto allying on).</summary>
    public bool CanChangePreset => isHost && state.UseTeamStartMappings;

    public bool IsHost => isHost;

    private void State_PropertyChanged(object sender, PropertyChangedEventArgs e)
    {
        switch (e.PropertyName)
        {
            case nameof(PlayerExtraOptionsState.UseTeamStartMappings):
                RefreshTeamStartMappingsPanel(state.UseTeamStartMappings);
                break;
            case nameof(PlayerExtraOptionsState.TeamStartMappings):
                if (!updatingMappingsFromControls)
                    SetTeamStartMappings([.. state.TeamStartMappings]);
                break;
        }

        Changed?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>SetIsHost: the host can change the options and, with auto allying on, the mappings.</summary>
    public void SetIsHost(bool host)
    {
        isHost = host;
        EnableAllSlots(host && state.UseTeamStartMappings);
        Changed?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>UpdateForGameModeMap: a new map clears the mappings and, with auto allying on, applies its first preset.</summary>
    public void UpdateForGameModeMap(GameModeMap map)
    {
        if (gameModeMap == map)
            return;

        gameModeMap = map;
        RefreshTeamStartMappingPanels(state.UseTeamStartMappings);
        Changed?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>The user picked a team for a start location.</summary>
    public void SelectTeam(int slot, int teamIndex)
    {
        if (!slotEnabled[slot])
            return;

        SetSlot(slot, teamIndex);
        Changed?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>The user picked a preset.</summary>
    public void SelectPreset(int index)
    {
        if (!CanChangePreset || index < 0 || index >= presets.Count)
            return;

        SetPresetIndex(index);
        Changed?.Invoke(this, EventArgs.Empty);
    }

    private void EnableAllSlots(bool enable)
    {
        for (int i = 0; i < MAX_START_COUNT; i++)
            slotEnabled[i] = enable;
    }

    private void RefreshTeamStartMappingsPanel(bool useTeamStartMappings)
    {
        EnableAllSlots(isHost && useTeamStartMappings);
        RefreshTeamStartMappingPanels(useTeamStartMappings);
    }

    private void RefreshTeamStartMappingPanels(bool useTeamStartMappings)
    {
        for (int i = 0; i < MAX_START_COUNT; i++)
            SetSlot(i, -1);

        for (int i = 0; i < MAX_START_COUNT; i++)
        {
            SetSlot(i, -1);

            if (!useTeamStartMappings)
                continue;

            slotEnabled[i] = isHost && gameModeMap != null && gameModeMap.AllowedStartingLocations.Contains(i + 1);
            RefreshTeamStartMappingPresets(gameModeMap?.Map?.TeamStartMappingPresets);
        }
    }

    private void RefreshTeamStartMappingPresets(List<TeamStartMappingPreset> mapPresets)
    {
        presets.Clear();
        presets.Add((CustomPresetName, []));
        SetPresetIndex(0);

        if (!(mapPresets?.Any() ?? false))
            return;

        foreach (TeamStartMappingPreset preset in mapPresets)
            presets.Add((preset.Name, preset.TeamStartMappings));

        SetPresetIndex(1);
    }

    private void SetPresetIndex(int index)
    {
        if (PresetIndex == index)
            return;

        PresetIndex = index;

        // DdTeamMappingPreset_SelectedIndexChanged
        if (index < 0 || index >= presets.Count || presets[index].Name == CustomPresetName)
            return;

        state.BeginUpdate();
        ignoreMappingChanges = true;
        SetTeamStartMappings(presets[index].Mappings);
        ignoreMappingChanges = false;
        state.EndUpdate();
    }

    private void SetTeamStartMappings(List<TeamStartMapping> mappings)
    {
        for (int i = 0; i < MAX_START_COUNT; i++)
        {
            if (mappings == null || mappings.Count <= i)
            {
                SetSlot(i, -1);
                continue;
            }

            int teamIndex = mappings[i]?.TeamIndex ?? -1;
            SetSlot(i, teamIndex >= 0 && teamIndex < Teams.Count ? teamIndex : -1);
        }
    }

    /// <summary>A team drop-down's SelectedIndex: reports a change (Mapping_Changed) only when the index changes.</summary>
    private void SetSlot(int slot, int teamIndex)
    {
        if (teamIndexes[slot] == teamIndex)
            return;

        teamIndexes[slot] = teamIndex;

        updatingMappingsFromControls = true;
        state.SetTeamStartMappings(GetTeamStartMappings());
        updatingMappingsFromControls = false;

        if (!ignoreMappingChanges)
            SetPresetIndex(0);
    }

    /// <summary>TeamStartMappingsPanel.GetTeamStartMappings: the valid mappings, by start.</summary>
    public List<TeamStartMapping> GetTeamStartMappings() =>
        Enumerable.Range(0, MAX_START_COUNT)
            .Select(i => new TeamStartMapping { Team = teamIndexes[i] >= 0 ? Teams[teamIndexes[i]] : null, Start = i + 1 })
            .Where(mapping => mapping.IsValid)
            .ToList();
}
