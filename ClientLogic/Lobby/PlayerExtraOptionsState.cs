using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;

using ClientCore.Extensions;

using CommunityToolkit.Mvvm.ComponentModel;

using DTAClient.Domain.Multiplayer;

namespace ClientLogic.Lobby;

/// <summary>
/// The host's extra player options of a game lobby: the four "force" flags, auto allying and its team-start
/// mappings. Owns the rules between them: auto allying forces "no teams" and locks it, and maps that don't allow
/// teams (co-op) turn both team options off and lock them.
/// </summary>
public sealed partial class PlayerExtraOptionsState : ObservableObject
{
    private static readonly HashSet<string> ValueProperties =
    [
        nameof(ForceRandomSides), nameof(ForceRandomColors), nameof(ForceNoTeams), nameof(ForceRandomStarts),
        nameof(UseTeamStartMappings), nameof(TeamStartMappings),
    ];

    private int updateDepth;
    private bool changedDuringUpdate;

    [ObservableProperty]
    private bool forceRandomSides;

    [ObservableProperty]
    private bool forceRandomColors;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanChangeForceNoTeams))]
    private bool forceNoTeams;

    [ObservableProperty]
    private bool forceRandomStarts;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanChangeForceNoTeams))]
    private bool useTeamStartMappings;

    /// <summary>The team-start mappings as the mapping controls hold them, whether or not auto allying is on.</summary>
    [ObservableProperty]
    private IReadOnlyList<TeamStartMapping> teamStartMappings = [];

    /// <summary>False while the map doesn't allow teams (co-op): both team options are then locked.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanChangeForceNoTeams))]
    [NotifyPropertyChangedFor(nameof(CanChangeUseTeamStartMappings))]
    private bool teamOptionsAllowed = true;

    public bool CanChangeForceNoTeams => TeamOptionsAllowed && !UseTeamStartMappings;

    public bool CanChangeUseTeamStartMappings => TeamOptionsAllowed;

    /// <summary>
    /// Raised once after a change, including everything it caused (rules, views reacting to it), or once at the
    /// end of a batch in which something changed.
    /// </summary>
    public event EventHandler Changed;

    public void BeginUpdate()
    {
        if (updateDepth++ == 0)
            changedDuringUpdate = false;
    }

    public void EndUpdate()
    {
        if (updateDepth == 0)
            throw new InvalidOperationException("EndUpdate called without BeginUpdate.");

        if (--updateDepth == 0 && changedDuringUpdate)
            Changed?.Invoke(this, EventArgs.Empty);
    }

    protected override void OnPropertyChanged(PropertyChangedEventArgs e)
    {
        BeginUpdate();

        if (ValueProperties.Contains(e.PropertyName))
            changedDuringUpdate = true;

        base.OnPropertyChanged(e);

        switch (e.PropertyName)
        {
            case nameof(UseTeamStartMappings) when UseTeamStartMappings:
                ForceNoTeams = true;
                break;
        }

        EndUpdate();
    }

    /// <summary>
    /// Called when the map changes: a map that doesn't allow teams turns both team options off and locks them.
    /// </summary>
    public void SetTeamOptionsAllowed(bool allowed)
    {
        BeginUpdate();
        TeamOptionsAllowed = allowed;

        if (!allowed)
        {
            ForceNoTeams = false;
            UseTeamStartMappings = false;
        }

        EndUpdate();
    }

    /// <summary>Replaces the team-start mappings if they differ from the current ones.</summary>
    public void SetTeamStartMappings(IEnumerable<TeamStartMapping> mappings)
    {
        List<TeamStartMapping> list = mappings?.ToList() ?? [];
        if (!list.Select(m => (m.Team, m.Start)).SequenceEqual(TeamStartMappings.Select(m => (m.Team, m.Start))))
            TeamStartMappings = list;
    }

    /// <summary>The options as sent and used at launch; the mappings only count while auto allying is on.</summary>
    public PlayerExtraOptions ToPlayerExtraOptions() => new()
    {
        IsForceRandomSides = ForceRandomSides,
        IsForceRandomColors = ForceRandomColors,
        IsForceRandomStarts = ForceRandomStarts,
        IsForceNoTeams = ForceNoTeams,
        IsUseTeamStartMappings = UseTeamStartMappings,
        TeamStartMappings = UseTeamStartMappings ? [.. TeamStartMappings] : [],
    };

    /// <summary>Sets all options at once, raising <see cref="Changed"/> at most once.</summary>
    public void Apply(PlayerExtraOptions options)
    {
        BeginUpdate();

        // Flags first: views may clear the mapping controls when auto allying changes
        ForceRandomSides = options.IsForceRandomSides;
        ForceRandomColors = options.IsForceRandomColors;
        ForceNoTeams = options.IsForceNoTeams;
        ForceRandomStarts = options.IsForceRandomStarts;
        UseTeamStartMappings = options.IsUseTeamStartMappings;
        SetTeamStartMappings(options.TeamStartMappings);

        EndUpdate();
    }

    /// <summary>
    /// Applies options received from the game host and returns the notices to show, in order.
    /// </summary>
    public IReadOnlyList<string> ApplyFromHost(PlayerExtraOptions options)
    {
        var notices = new List<string>();

        if (options.IsForceRandomSides != ForceRandomSides)
            notices.Add(ForcedNotice(options.IsForceRandomSides, "side selection".L10N("Client:Main:SideAsANoun")));

        if (options.IsForceRandomColors != ForceRandomColors)
            notices.Add(ForcedNotice(options.IsForceRandomColors, "color selection".L10N("Client:Main:ColorAsANoun")));

        if (options.IsForceRandomStarts != ForceRandomStarts)
            notices.Add(ForcedNotice(options.IsForceRandomStarts, "start selection".L10N("Client:Main:StartPositionAsANoun")));

        if (options.IsForceNoTeams != ForceNoTeams)
            notices.Add(ForcedNotice(options.IsForceNoTeams, "team selection".L10N("Client:Main:TeamAsANoun")));

        // Auto allying is a feature being turned on, not a choice being taken away
        if (options.IsUseTeamStartMappings != UseTeamStartMappings)
            notices.Add(ForcedNotice(!options.IsUseTeamStartMappings, "auto ally".L10N("Client:Main:AutoAllyAsANoun")));

        Apply(options);
        return notices;
    }

    private static string ForcedNotice(bool disabled, string type) => disabled
        ? string.Format("The game host has disabled {0}".L10N("Client:Main:HostDisableSection"), type)
        : string.Format("The game host has enabled {0}".L10N("Client:Main:HostEnableSection"), type);
}
