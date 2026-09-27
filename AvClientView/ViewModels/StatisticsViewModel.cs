using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;

using Avalonia.Media;
using Avalonia.Threading;

using AvClientView.Theme;

using ClientCore;
using ClientCore.Extensions;
using ClientCore.Statistics;

using ClientLogic.Statistics;

using CommunityToolkit.Mvvm.ComponentModel;

using DTAClient.Domain.Multiplayer;

using Rampastring.Tools;

namespace AvClientView.ViewModels;

/// <summary>A row of the game list: date / time, map, game mode, FPS, duration, completed.</summary>
public sealed record StatisticsGameRow(string DateTime, string Map, string GameMode, string Fps, string Duration, string Completed);

/// <summary>A row of a game's player list, in the player's colour; the side has its icon (null for none).</summary>
public sealed record StatisticsPlayerRow(string Name, string Kills, string Losses, string Economy, string Score, string Won,
    string Side, string SideIcon, string Team, Color Color, bool IsLocalPlayer);

/// <summary>
/// The statistics window, as the XNA StatisticsWindow: the recorded games with the class, game mode and spectated
/// filters, the selected game's players, and the totals of the listed games.
/// </summary>
public sealed partial class StatisticsViewModel : ObservableObject
{
    private readonly MapLoader mapLoader;
    private readonly (string Name, string UIName)[] sides;
    private readonly List<MultiplayerColor> mpColors;
    private List<MatchStatistics> matches = [];
    private List<int> listedGameIndexes = [];
    private List<string> gameModeTags = [];
    private bool initialized;

    public StatisticsViewModel(MapLoader mapLoader)
    {
        this.mapLoader = mapLoader;

        sides = ClientConfiguration.Instance.Sides.Split(',')
            .Select(s => (Name: s, UIName: s.L10N($"INI:Sides:{s}"))).ToArray();
        mpColors = MultiplayerColor.LoadColors();

        StatisticsManager.Instance.GameAdded += (_, _) => Dispatcher.UIThread.Post(ListGames);
    }

    public IReadOnlyList<string> GameClassItems { get; } =
    [
        "All games".L10N("Client:Main:FilterAll"),
        "Online games".L10N("Client:Main:FilterOnline"),
        "Online PvP".L10N("Client:Main:FilterPvP"),
        "Online Co-Op".L10N("Client:Main:FilterCoOp"),
        "Skirmish".L10N("Client:Main:FilterSkirmish"),
    ];

    public ObservableCollection<string> GameModeItems { get; } = [];

    public ObservableCollection<StatisticsGameRow> Games { get; } = [];

    public ObservableCollection<StatisticsPlayerRow> Players { get; } = [];

    /// <summary>The ECONOMY column's header (BUILT with UseBuiltStatistic).</summary>
    public static string EconomyHeader => ClientConfiguration.Instance.UseBuiltStatistic
        ? "BUILT".L10N("Client:Main:StatisticBuildCount") : "ECONOMY".L10N("Client:Main:StatisticEconomy");

    /// <summary>The average economy label (the average number of objects built with UseBuiltStatistic).</summary>
    public static string AverageEconomyLabel => ClientConfiguration.Instance.UseBuiltStatistic
        ? "Avg. number of objects built:".L10N("Client:Main:StatisticBuildCountAvg") : "Average economy:".L10N("Client:Main:StatisticEconomyAvg");

    /// <summary>The side names (for the side icons: "{name}icon.png").</summary>
    public IReadOnlyList<string> SideNames => sides.Select(s => s.Name).ToList();

    [ObservableProperty]
    private bool isOpen;

    [ObservableProperty]
    private int selectedTab;

    [ObservableProperty]
    private int selectedGameClass;

    [ObservableProperty]
    private int selectedGameMode;

    [ObservableProperty]
    private bool includeSpectatedGames = true;

    [ObservableProperty]
    private int selectedGameIndex = -1;

    [ObservableProperty]
    private StatisticsTotals totals;

    [ObservableProperty]
    private string favouriteSide = string.Empty;

    partial void OnSelectedGameClassChanged(int value) => ListGames();

    partial void OnSelectedGameModeChanged(int value) => ListGames();

    partial void OnIncludeSpectatedGamesChanged(bool value) => ListGames();

    partial void OnSelectedGameIndexChanged(int value) => ListPlayers();

    public void Open()
    {
        if (!initialized)
        {
            initialized = true;
            ListGameModes();
        }

        IsOpen = true;
        ListGames();
    }

    public void Close() => IsOpen = false;

    private void ListGameModes()
    {
        gameModeTags = StatisticsQuery.GameModes(StatisticsQuery.AllMatches(StatisticsManager.Instance));

        GameModeItems.Clear();
        GameModeItems.Add("All".L10N("Client:Main:AllGameModes"));
        foreach (string gm in gameModeTags)
            GameModeItems.Add(gm.L10N($"INI:GameModes:{gm}:UIName"));

        SelectedGameMode = 0;
    }

    private void ListGames()
    {
        if (!IsOpen || !initialized)
            return;

        SelectedGameIndex = -1;
        Players.Clear();
        Games.Clear();

        matches = StatisticsQuery.AllMatches(StatisticsManager.Instance);
        string gameMode = SelectedGameMode > 0 && SelectedGameMode <= gameModeTags.Count ? gameModeTags[SelectedGameMode - 1] : null;
        listedGameIndexes = StatisticsQuery.ListGames(matches, (StatisticsGameClass)Math.Max(0, SelectedGameClass), gameMode, IncludeSpectatedGames);

        Totals = StatisticsQuery.Totals(matches, listedGameIndexes, sides.Length);
        FavouriteSide = Totals.FavouriteSideIndex >= 0 && Totals.FavouriteSideIndex < sides.Length ? sides[Totals.FavouriteSideIndex].UIName : string.Empty;

        foreach (int gameIndex in listedGameIndexes)
        {
            MatchStatistics ms = matches[gameIndex];
            Games.Add(new StatisticsGameRow(
                ms.DateAndTime.ToShortDateString() + " " + ms.DateAndTime.ToShortTimeString(),
                mapLoader.TranslatedMapNames.TryGetValue(ms.MapName, out string translated) ? translated : ms.MapName,
                ms.GameMode.L10N($"INI:GameModes:{ms.GameMode}:UIName"),
                ms.AverageFPS == 0 ? "-" : ms.AverageFPS.ToString(),
                TimeSpan.FromSeconds(ms.LengthInSeconds).ToString(),
                Conversions.BooleanToString(ms.SawCompletion, BooleanStringStyle.YESNO)));
        }
    }

    /// <summary>The selected game's players (LbGameList_SelectedIndexChanged).</summary>
    private void ListPlayers()
    {
        Players.Clear();

        if (SelectedGameIndex < 0 || SelectedGameIndex >= listedGameIndexes.Count)
            return;

        MatchStatistics ms = matches[listedGameIndexes[SelectedGameIndex]];
        Color textColor = ThemeAssets.ButtonTextColor;

        foreach (PlayerStatistics ps in StatisticsQuery.PlayersByScore(ms))
        {
            // As the XNA window: a player without a valid colour keeps the previous row's colour
            if (ps.Color > -1 && ps.Color < mpColors.Count)
                textColor = Color.FromRgb((byte)mpColors[ps.Color].R, (byte)mpColors[ps.Color].G, (byte)mpColors[ps.Color].B);

            string name = ps.IsAI ? ProgramConstants.GetAILevelName(ps.AILevel) : ps.Name;

            if (ps.WasSpectator)
            {
                Players.Add(new StatisticsPlayerRow(name, "-", "-", "-", "-", "-",
                    "Spectator".L10N("Client:Main:Spectator"), "spectatoricon.png", "-", textColor, ps.IsLocalPlayer));
                continue;
            }

            bool known = ms.SawCompletion;
            (string side, string sideIcon) = ps.Side == 0 || ps.Side > sides.Length
                ? ("Unknown".L10N("Client:Main:UnknownSide"), null)
                : (sides[ps.Side - 1].UIName, sides[ps.Side - 1].Name + "icon.png");

            Players.Add(new StatisticsPlayerRow(name,
                known ? ps.Kills.ToString() : "-",
                known ? ps.Losses.ToString() : "-",
                known ? ps.Economy.ToString() : "-",
                known ? ps.Score.ToString() : "-",
                known ? Conversions.BooleanToString(ps.Won, BooleanStringStyle.YESNO) : "-",
                side, sideIcon, StatisticsQuery.TeamIndexToString(ps.Team), textColor, ps.IsLocalPlayer));
        }
    }
}
