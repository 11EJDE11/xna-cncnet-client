#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;

using ClientCore;
using ClientCore.Display;
using ClientCore.Extensions;

namespace ClientLogic.Settings;

/// <summary>The display modes of the default monitor (a front end reads them from the OS or the graphics API).</summary>
public interface IDisplayModeSource
{
    /// <summary>The desktop's current resolution.</summary>
    ScreenResolution DesktopResolution { get; }

    /// <summary>The supported full-screen modes; the same size may appear more than once (refresh rates).</summary>
    IEnumerable<ScreenResolution> DisplayModes { get; }
}

/// <summary>
/// The resolution lists of the options window, as ClientGUI's XNAScreenResolutionManager builds them (DirectX build:
/// the safe maximum is the desktop resolution), from an <see cref="IDisplayModeSource"/>.
/// </summary>
public sealed class ScreenResolutions(IDisplayModeSource source)
{
    public const int MAX_INT_SCALE = 9;

    public static readonly IReadOnlyList<ScreenResolution> OptimalWindowedResolutions =
    [
        "1024x600",
        "1024x720",
        "1280x600",
        "1280x720",
        "1280x768",
        "1280x800",
    ];

    public ScreenResolution DesktopResolution => source.DesktopResolution;

    public ScreenResolution SafeMaximumResolution => source.DesktopResolution;

    public ScreenResolution SafeFullScreenResolution =>
        GetFullScreenResolutions(ClientConfiguration.Instance.MinimumClientResolution).Max ?? SafeMaximumResolution;

    public SortedSet<ScreenResolution> GetFullScreenResolutions(ScreenResolution minResolution) =>
        GetFullScreenResolutions(minResolution, SafeMaximumResolution);

    public SortedSet<ScreenResolution> GetFullScreenResolutions(ScreenResolution minResolution, ScreenResolution maxResolution)
    {
        SortedSet<ScreenResolution> screenResolutions = [];
        foreach (ScreenResolution mode in source.DisplayModes)
        {
            if (mode.Width < minResolution.Width || mode.Height < minResolution.Height ||
                mode.Width > maxResolution.Width || mode.Height > maxResolution.Height)
            {
                continue;
            }

            screenResolutions.Add(mode);
        }

        return screenResolutions;
    }

    public SortedSet<ScreenResolution> GetIntegerScaledResolutionsFrom(ScreenResolution resolution)
    {
        SortedSet<ScreenResolution> resolutions = [];
        for (int i = 1; i <= MAX_INT_SCALE; i++)
        {
            ScreenResolution scaled = (resolution.Width * i, resolution.Height * i);
            if (SafeMaximumResolution.Fits(scaled))
                resolutions.Add(scaled);
            else
                break;
        }

        return resolutions;
    }

    public SortedSet<ScreenResolution> GetWindowedResolutions(ScreenResolution minResolution)
    {
        SortedSet<ScreenResolution> windowedResolutions = [];
        foreach (ScreenResolution optimal in OptimalWindowedResolutions)
        {
            if (optimal.Width < minResolution.Width || optimal.Height < minResolution.Height)
                continue;

            if (!SafeMaximumResolution.Fits(optimal))
                continue;

            windowedResolutions.Add(optimal);
        }

        return windowedResolutions;
    }

    public SortedSet<ScreenResolution> GetRecommendedResolutions()
    {
        ScreenResolution minimumClientResolution = ClientConfiguration.Instance.MinimumClientResolution;
        return
        [
            .. ClientConfiguration.Instance.RecommendedResolutions
                .Select(resolution => (ScreenResolution)resolution)
                .SelectMany(GetIntegerScaledResolutionsFrom)
                .Where(resolution => resolution.Fits(minimumClientResolution)),
        ];
    }

    public static SortedSet<ScreenResolution> GetCustomIngameResolutions() =>
    [
        .. ClientConfiguration.Instance.CustomIngameResolutions
            .Where(resolution => !string.IsNullOrWhiteSpace(resolution))
            .Select(resolution => (ScreenResolution)resolution),
    ];

    /// <summary>The in-game resolution list: the full-screen modes within the game's limits, plus the custom ones.</summary>
    public SortedSet<ScreenResolution> GetIngameResolutions()
    {
        ScreenResolution minimum = ClientConfiguration.Instance.MinimumIngameResolution;
        ScreenResolution maximum = ClientConfiguration.Instance.MaximumIngameResolution;
        SortedSet<ScreenResolution> resolutions = GetFullScreenResolutions(minimum, maximum);

        foreach (ScreenResolution custom in GetCustomIngameResolutions())
        {
            if (!custom.Fits(minimum))
                throw new ClientConfigurationException($"Custom in-game resolution {custom} is too small. Please check 'MinimumIngameWidth' and 'MinimumIngameHeight' in 'ClientDefinitions.ini' file.");

            if (!maximum.Fits(custom))
                throw new ClientConfigurationException($"Custom in-game resolution {custom} is too large. Please check 'MaximumIngameWidth' and 'MaximumIngameHeight' in 'ClientDefinitions.ini' file.");

            resolutions.Add(custom);
        }

        return resolutions;
    }

    /// <summary>
    /// The client resolution list (full-screen modes, the optimal windowed sizes and the recommended ones) and the
    /// indexes of the recommended ones in it, ascending.
    /// </summary>
    public (List<ScreenResolution> Resolutions, List<int> RecommendedIndexes) GetClientResolutions()
    {
        ScreenResolution minimum = ClientConfiguration.Instance.MinimumClientResolution;
        RequireDesktopResolutionFitsMinimumResolution(minimum);

        SortedSet<ScreenResolution> recommended = GetRecommendedResolutions();
        SortedSet<ScreenResolution> all =
        [
            .. GetFullScreenResolutions(minimum),
            .. GetWindowedResolutions(minimum),
            .. recommended,
        ];

        List<ScreenResolution> list = all.ToList();
        List<int> recommendedIndexes = recommended
            .Select(resolution => list.FindIndex(r => r == resolution))
            .Where(index => index > -1)
            .ToList();

        return (list, recommendedIndexes);
    }

    /// <summary>The largest recommended resolution, else the largest full-screen one (the windowed client's default).</summary>
    public ScreenResolution GetBestRecommendedResolution() => GetRecommendedResolutions().Max ?? SafeFullScreenResolution;

    /// <summary>
    /// Creates the ClientResolutionX/Y settings as the XNA client's Startup does, defaulting to the best recommended
    /// resolution for a windowed client and the largest full-screen resolution for a fullscreen one.
    /// </summary>
    public void CreateClientResolutionSettings()
    {
        UserINISettings settings = UserINISettings.Instance;
        ScreenResolution resolution = settings.BorderlessWindowedClient ? SafeFullScreenResolution : GetBestRecommendedResolution();
        settings.ClientResolutionX = new ClientCore.Settings.IntSetting(settings.SettingsIni, UserINISettings.VIDEO, "ClientResolutionX", resolution.Width);
        settings.ClientResolutionY = new ClientCore.Settings.IntSetting(settings.SettingsIni, UserINISettings.VIDEO, "ClientResolutionY", resolution.Height);
    }

    public void RequireDesktopResolutionFitsMinimumResolution(ScreenResolution minimumClientResolution)
    {
        if (!DesktopResolution.Fits(minimumClientResolution))
        {
            throw new Exception(string.Format("Your desktop resolution {0} is too small. At least {1} is required. Please change your desktop resolution and restart the client.".L10N("Client:DTAConfig:DesktopResolutionTooSmall"),
                DesktopResolution, minimumClientResolution));
        }
    }
}
