using System.Collections.Generic;

using ClientLogic.Lobby;

using DTAClient.Domain.Multiplayer;

using Xunit;

namespace ClientLogic.Tests.Lobby;

public class PlayerExtraOptionsStateTests
{
    private static (PlayerExtraOptionsState State, List<int> Changes) Create()
    {
        TestGame.EnsureInitialized();
        var state = new PlayerExtraOptionsState();
        var changes = new List<int>();
        state.Changed += (_, _) => changes.Add(changes.Count);
        return (state, changes);
    }

    private static List<TeamStartMapping> Mappings() =>
    [
        new TeamStartMapping { Team = "A", Start = 1 },
        new TeamStartMapping { Team = "B", Start = 2 },
    ];

    [Fact]
    public void AutoAllyingForcesNoTeamsAndLocksIt()
    {
        var (state, changes) = Create();
        Assert.True(state.CanChangeForceNoTeams);

        state.UseTeamStartMappings = true;

        Assert.True(state.ForceNoTeams);
        Assert.False(state.CanChangeForceNoTeams);
        Assert.Single(changes);

        // Turning it off unlocks "no teams" but leaves it on
        state.UseTeamStartMappings = false;
        Assert.True(state.ForceNoTeams);
        Assert.True(state.CanChangeForceNoTeams);
    }

    [Fact]
    public void MapsWithoutTeamsTurnTeamOptionsOffAndLockThem()
    {
        var (state, changes) = Create();
        state.UseTeamStartMappings = true;
        changes.Clear();

        state.SetTeamOptionsAllowed(false);

        Assert.False(state.ForceNoTeams);
        Assert.False(state.UseTeamStartMappings);
        Assert.False(state.CanChangeForceNoTeams);
        Assert.False(state.CanChangeUseTeamStartMappings);
        Assert.Single(changes);

        state.SetTeamOptionsAllowed(true);
        Assert.True(state.CanChangeForceNoTeams);
        Assert.True(state.CanChangeUseTeamStartMappings);
        Assert.Single(changes);
    }

    [Fact]
    public void ApplyingHostOptionsRaisesOneChangeAndOrderedNotices()
    {
        var (state, changes) = Create();
        state.ForceRandomColors = true;
        changes.Clear();

        IReadOnlyList<string> notices = state.ApplyFromHost(new PlayerExtraOptions
        {
            IsForceRandomSides = true,
            IsForceRandomColors = false,
            IsForceRandomStarts = true,
            IsForceNoTeams = true,
            IsUseTeamStartMappings = true,
            TeamStartMappings = Mappings(),
        });

        Assert.Single(changes);
        Assert.Equal(5, notices.Count);
        Assert.Contains("disabled side selection", notices[0]);
        Assert.Contains("enabled color selection", notices[1]);
        Assert.Contains("disabled start selection", notices[2]);
        Assert.Contains("disabled team selection", notices[3]);
        Assert.Contains("enabled auto ally", notices[4]);   // inverted: auto allying is turned on

        Assert.StartsWith("10111;", state.ToPlayerExtraOptions().ToString());
        Assert.Equal(2, state.ToPlayerExtraOptions().TeamStartMappings.Count);

        Assert.Empty(state.ApplyFromHost(state.ToPlayerExtraOptions()));
        Assert.Single(changes);
    }

    [Fact]
    public void MappingsOnlyCountWhileAutoAllyingIsOn()
    {
        var (state, changes) = Create();

        state.SetTeamStartMappings(Mappings());
        Assert.Single(changes);
        Assert.Empty(state.ToPlayerExtraOptions().TeamStartMappings);

        // The same mappings again are no change
        state.SetTeamStartMappings(Mappings());
        Assert.Single(changes);

        state.UseTeamStartMappings = true;
        Assert.Equal(2, state.ToPlayerExtraOptions().TeamStartMappings.Count);
    }

    [Fact]
    public void BatchesRaiseOneChange()
    {
        var (state, changes) = Create();

        state.BeginUpdate();
        state.ForceRandomSides = true;
        state.ForceRandomStarts = true;
        Assert.Empty(changes);
        state.EndUpdate();

        Assert.Single(changes);

        state.BeginUpdate();
        state.EndUpdate();
        Assert.Single(changes);
    }
}
