using System.Collections.Generic;

using ClientCore;

using CommunityToolkit.Mvvm.ComponentModel;

namespace AvClientView.ViewModels;

/// <summary>
/// The XNA CampaignTagSelector: with CampaignTagSelectorEnabled, New Campaign first offers the theme's tag buttons
/// (ButtonTag_{tag}) and Show All Missions, which open the campaign window with those missions; the campaign window's
/// Campaigns button comes back here.
/// </summary>
public sealed partial class CampaignTagSelectorViewModel : ObservableObject
{
    private readonly CampaignViewModel campaign;

    public CampaignTagSelectorViewModel(CampaignViewModel campaign)
    {
        this.campaign = campaign;
        campaign.ReturnRequested += (_, _) => IsOpen = true;
    }

    public static bool IsEnabled => ClientConfiguration.Instance.CampaignTagSelectorEnabled;

    [ObservableProperty]
    private bool isOpen;

    /// <summary>New Campaign: the tag selector, or the campaign window when the theme doesn't use tags.</summary>
    public void Open()
    {
        if (IsEnabled)
            IsOpen = true;
        else
            campaign.Open();
    }

    public void Cancel() => IsOpen = false;

    /// <summary>btnShowAllMission: every mission, official and custom.</summary>
    public void ShowAllMissions() => Switch(null);

    /// <summary>A ButtonTag_{tag} button: the missions with that tag.</summary>
    public void ShowTag(string tag) => Switch(new HashSet<string> { tag });

    private void Switch(ISet<string> tags)
    {
        IsOpen = false;
        campaign.Open();
        campaign.LoadMissionsWithFilter(tags, disableCustomMissions: false, disableOfficialMissions: false);
    }
}
