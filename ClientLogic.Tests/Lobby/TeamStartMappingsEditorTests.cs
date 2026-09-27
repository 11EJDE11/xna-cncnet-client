using System.Collections.Generic;

using ClientLogic.Lobby;

using DTAClient.Domain.Multiplayer;

using Xunit;

namespace ClientLogic.Tests.Lobby;

public class TeamStartMappingsEditorTests
{
    private static (PlayerExtraOptionsState State, TeamStartMappingsEditor Editor) Create(bool isHost)
    {
        TestGame.EnsureInitialized();
        var state = new PlayerExtraOptionsState();
        var editor = new TeamStartMappingsEditor(state);
        editor.SetIsHost(isHost);
        editor.UpdateForGameModeMap(TestGame.LoadGameModeMap("Maps/Test/four", "Battle"));
        return (state, editor);
    }

    [Fact]
    public void MappingsCanOnlyBeChangedByTheHostWithAutoAllyingOn()
    {
        var (state, editor) = Create(isHost: true);
        Assert.False(editor.CanChangeSlot(0));

        state.UseTeamStartMappings = true;
        Assert.True(editor.CanChangeSlot(0));
        Assert.False(editor.CanChangeSlot(7)); // the test map has four starts
        Assert.True(editor.CanChangePreset);

        var (playerState, player) = Create(isHost: false);
        playerState.UseTeamStartMappings = true;
        Assert.False(player.CanChangeSlot(0));
        Assert.False(player.CanChangePreset);
    }

    [Fact]
    public void TheMapsFirstPresetIsAppliedAndAChangedTeamMakesItCustom()
    {
        var (state, editor) = Create(isHost: true);
        state.UseTeamStartMappings = true;

        // The test map has a preset: turning auto allying on applies it (XNA RefreshTeamStartMappingPresets)
        Assert.Equal(1, editor.PresetIndex);
        List<(string Team, int Start)> preset = state.TeamStartMappings.ConvertAll();
        Assert.NotEmpty(preset);

        editor.SelectTeam(2, TeamStartMapping.TEAMS.IndexOf(TeamStartMapping.NO_PLAYER));

        Assert.Equal(0, editor.PresetIndex);
        Assert.Contains(("x", 3), state.TeamStartMappings.ConvertAll());

        editor.SelectPreset(1);
        Assert.Equal(preset, state.TeamStartMappings.ConvertAll());
    }

    [Fact]
    public void TheHostsMappingsFillTheDropDowns()
    {
        var (state, editor) = Create(isHost: false);
        state.UseTeamStartMappings = true;

        state.SetTeamStartMappings(TeamStartMapping.FromListString("B,-,x,A"));

        Assert.Equal(TeamStartMapping.TEAMS.IndexOf("B"), editor.GetTeamIndex(0));
        Assert.Equal(TeamStartMapping.TEAMS.IndexOf("A"), editor.GetTeamIndex(3));
        Assert.Equal(-1, editor.GetTeamIndex(4));
    }

    [Fact]
    public void TurningAutoAllyingOffClearsTheMappings()
    {
        var (state, editor) = Create(isHost: true);
        state.UseTeamStartMappings = true;
        editor.SelectTeam(1, TeamStartMapping.TEAMS.IndexOf("C"));

        state.UseTeamStartMappings = false;

        Assert.Empty(state.TeamStartMappings);
        Assert.False(editor.CanChangeSlot(1));
    }
}

internal static class MappingListExtensions
{
    public static List<(string Team, int Start)> ConvertAll(this IReadOnlyList<TeamStartMapping> mappings) =>
        [.. System.Linq.Enumerable.Select(mappings, m => (m.Team, m.Start))];
}
