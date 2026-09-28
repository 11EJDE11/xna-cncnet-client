using System;
using System.Collections.Generic;

using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Controls.Templates;
using Avalonia.Data;
using Avalonia.Layout;
using Avalonia.Media;

using AvClientView.Theme;
using AvClientView.ViewModels;

using ClientCore;
using ClientCore.Extensions;

using ClientLogic.Layout;
using ClientLogic.Statistics;
using ClientLogic.UI;

using Rampastring.Tools;

namespace AvClientView.Views;

/// <summary>
/// The XNA StatisticsWindow over a darkening panel: the Game Statistics / Total Statistics tabs, the game class and
/// game mode filters, "Include spectated games", the game list and the selected game's players, the totals, and
/// "Return to Main Menu"; with the theme's StatisticsWindow.ini applied.
/// </summary>
public sealed class StatisticsView : Panel
{
    private const int BUTTON_WIDTH_133 = 133;
    private const int BUTTON_WIDTH_160 = 160;
    private const int BUTTON_HEIGHT = 23;
    private const int CHECKBOX_TEXTURE_SIZE = 18;
    private const int CHECKBOX_TEXT_PADDING = 5;

    private const int TOTAL_STATS_LOCATION_X1 = 40;
    private const int TOTAL_STATS_VALUE_LOCATION_X1 = 240;
    private const int TOTAL_STATS_LOCATION_X2 = 380;
    private const int TOTAL_STATS_VALUE_LOCATION_X2 = 580;
    private const int TOTAL_STATS_Y_INCREASE = 45;
    private const int TOTAL_STATS_FIRST_ITEM_Y = 20;

    private readonly StatisticsViewModel viewModel;

    public StatisticsView(StatisticsViewModel viewModel)
    {
        this.viewModel = viewModel;
        Background = new SolidColorBrush(Color.FromArgb(128, 0, 0, 0));
        IsVisible = false;

        viewModel.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(StatisticsViewModel.IsOpen))
                IsVisible = viewModel.IsOpen;
        };

        try
        {
            Children.Add(Build());
        }
        catch (Exception ex)
        {
            Logger.Log("StatisticsView: building the statistics window failed: " + ex);
            Children.Add(new TextBlock { Text = "The statistics window could not be built: " + ex.Message, Foreground = Brushes.White });
        }
    }

    /// <summary>The values of the totals tab: the value label's name and the <see cref="StatisticsTotals"/> property.</summary>
    private static readonly (string Label, string Text, string Value, string Property, int Column)[] TotalRows =
    [
        ("lblGamesStarted", "Games started:".L10N("Client:Main:StatisticsGamesStarted"), "lblGamesStartedValue", nameof(StatisticsTotals.GamesStarted), 1),
        ("lblGamesFinished", "Games finished:".L10N("Client:Main:StatisticsGamesFinished"), "lblGamesFinishedValue", nameof(StatisticsTotals.GamesFinished), 1),
        ("lblWins", "Wins:".L10N("Client:Main:StatisticsGamesWins"), "lblWinsValue", nameof(StatisticsTotals.Wins), 1),
        ("lblLosses", "Losses:".L10N("Client:Main:StatisticsGamesLosses"), "lblLossesValue", nameof(StatisticsTotals.Losses), 1),
        ("lblWinLossRatio", "Win / Loss ratio:".L10N("Client:Main:StatisticsGamesWinLossRatio"), "lblWinLossRatioValue", nameof(StatisticsTotals.WinLossRatio), 1),
        ("lblAverageGameLength", "Average game length:".L10N("Client:Main:StatisticsGamesLengthAvg"), "lblAverageGameLengthValue", nameof(StatisticsTotals.AverageGameLength), 1),
        ("lblTotalTimePlayed", "Total time played:".L10N("Client:Main:StatisticsTotalTimePlayed"), "lblTotalTimePlayedValue", nameof(StatisticsTotals.TotalTimePlayed), 1),
        ("lblAverageEnemyCount", "Average number of enemies:".L10N("Client:Main:StatisticsEnemiesAvg"), "lblAverageEnemyCountValue", nameof(StatisticsTotals.AverageEnemyCount), 1),
        ("lblAverageAllyCount", "Average number of allies:".L10N("Client:Main:StatisticsAlliesAvg"), "lblAverageAllyCountValue", nameof(StatisticsTotals.AverageAllyCount), 1),
        ("lblTotalKills", "Total kills:".L10N("Client:Main:StatisticsTotalKills"), "lblTotalKillsValue", nameof(StatisticsTotals.TotalKills), 2),
        ("lblKillsPerGame", "Kills / game:".L10N("Client:Main:StatisticsKillsPerGame"), "lblKillsPerGameValue", nameof(StatisticsTotals.KillsPerGame), 2),
        ("lblTotalLosses", "Total losses:".L10N("Client:Main:StatisticsTotalLosses"), "lblTotalLossesValue", nameof(StatisticsTotals.TotalLosses), 2),
        ("lblLossesPerGame", "Losses / game:".L10N("Client:Main:StatisticsLossesPerGame"), "lblLossesPerGameValue", nameof(StatisticsTotals.LossesPerGame), 2),
        ("lblKillLossRatio", "Kill / loss ratio:".L10N("Client:Main:StatisticsKillLossRatio"), "lblKillLossRatioValue", nameof(StatisticsTotals.KillLossRatio), 2),
        ("lblTotalScore", "Total score:".L10N("Client:Main:TotalScore"), "lblTotalScoreValue", nameof(StatisticsTotals.TotalScore), 2),
        ("lblAverageEconomy", StatisticsViewModel.AverageEconomyLabel, "lblAverageEconomyValue", nameof(StatisticsTotals.AverageEconomy), 2),
        ("lblFavouriteSide", "Favourite side:".L10N("Client:Main:FavouriteSide"), "lblFavouriteSideValue", null, 2),
        ("lblAverageAILevel", "Average AI level:".L10N("Client:Main:AvgAILevel"), "lblAverageAILevelValue", nameof(StatisticsTotals.AverageAILevel), 2),
    ];

    private Canvas Build()
    {
        var window = new LayoutControl("StatisticsWindow", "XNAWindow", LayoutControlKind.Panel)
        {
            Width = 700,
            Height = 521,
            BackgroundTexture = "scoreviewerbg.png",
        };

        // The controls in the XNA window's AddChild order
        LayoutControl panelGameStatistics = ThemedWindow.Add(window, "panelGameStatistics", "XNAPanel", 10, 55, 680, 425);
        panelGameStatistics.BackgroundTexture = "scoreviewerpanelbg.png";
        LayoutControl lblGames = ThemedWindow.Add(panelGameStatistics, "lblGames", "XNALabel", 4, 2, 0, 0, "GAMES:".L10N("Client:Main:GameMatches"));
        lblGames.FontIndex = 1;
        ThemedWindow.Add(panelGameStatistics, "lbGameList", "XNAMultiColumnListBox", 2, 25, 676, 250);
        ThemedWindow.Add(panelGameStatistics, "lbGameStatistics", "XNAMultiColumnListBox", 2, 280, 676, 143);

        LayoutControl panelTotalStatistics = ThemedWindow.Add(window, "panelTotalStatistics", "XNAPanel", 10, 55, 680, 425);
        panelTotalStatistics.BackgroundTexture = "scoreviewerpanelbg.png";
        ChatColor altColor = ChatColor.Parse(ClientConfiguration.Instance.AltUIColor);
        int[] locationY = [0, TOTAL_STATS_FIRST_ITEM_Y, TOTAL_STATS_FIRST_ITEM_Y];
        foreach ((string label, string text, string value, _, int column) in TotalRows)
        {
            int y = locationY[column];
            ThemedWindow.Add(panelTotalStatistics, label, "XNALabel", column == 1 ? TOTAL_STATS_LOCATION_X1 : TOTAL_STATS_LOCATION_X2, y, 0, 0, text);
            ThemedWindow.Add(panelTotalStatistics, value, "XNALabel", column == 1 ? TOTAL_STATS_VALUE_LOCATION_X1 : TOTAL_STATS_VALUE_LOCATION_X2, y, 0, 0, " ")
                .TextColor = altColor;
            locationY[column] += TOTAL_STATS_Y_INCREASE;
        }

        ThemedWindow.Add(window, "tabControl", "XNAClientTabControl", 12, 10, 2 * BUTTON_WIDTH_133, BUTTON_HEIGHT);
        ThemedWindow.Add(window, "lblFilter", "XNALabel", 527, 12, 0, 0, "FILTER:".L10N("Client:Main:Filter")).FontIndex = 1;
        ThemedWindow.Add(window, "cmbGameClassFilter", "XNAClientDropDown", 585, 11, 105, 21);
        ThemedWindow.Add(window, "lblGameMode", "XNALabel", 294, 12, 0, 0, "GAME MODE:".L10N("Client:Main:GameMode")).FontIndex = 1;
        ThemedWindow.Add(window, "cmbGameModeFilter", "XNAClientDropDown", 381, 11, 114, 21);
        ThemedWindow.Add(window, "btnReturnToMenu", "XNAClientButton", 270, 486, BUTTON_WIDTH_160, BUTTON_HEIGHT, "Return to Main Menu".L10N("Client:Main:ReturnToMainMenu"));

        // The check box is sized by its text when it's added, then placed at the right edge
        string spectatedText = "Include spectated games".L10N("Client:Main:IncludeSpectated");
        int checkBoxWidth = ThemeFonts.Measure(spectatedText, 0).Width + CHECKBOX_TEXT_PADDING + CHECKBOX_TEXTURE_SIZE;
        ThemedWindow.Add(window, "chkIncludeSpectatedGames", "XNAClientCheckBox", window.Width - checkBoxWidth - 12, 11 + 21 + 3,
            checkBoxWidth, CHECKBOX_TEXTURE_SIZE, spectatedText);

        Canvas canvas = ThemedWindow.Build(window, (name, _) =>
        {
            if (name == "btnReturnToMenu")
                viewModel.Close();
        }, new Dictionary<string, Func<LayoutControl, Control>>
        {
            ["tabControl"] = _ => TabControl(),
            ["cmbGameClassFilter"] = layout => DropDown(layout, nameof(StatisticsViewModel.GameClassItems), nameof(StatisticsViewModel.SelectedGameClass)),
            ["cmbGameModeFilter"] = layout => DropDown(layout, nameof(StatisticsViewModel.GameModeItems), nameof(StatisticsViewModel.SelectedGameMode)),
            ["chkIncludeSpectatedGames"] = layout =>
            {
                var checkBox = new ThemedCheckBox(layout.Text, layout.FontIndex) { DataContext = viewModel };
                checkBox.Bind(ThemedCheckBox.IsCheckedProperty, new Binding(nameof(StatisticsViewModel.IncludeSpectatedGames)) { Mode = BindingMode.TwoWay });
                return checkBox;
            },
            ["lbGameList"] = GameList,
            ["lbGameStatistics"] = PlayerList,
        });

        canvas.HorizontalAlignment = HorizontalAlignment.Center;
        canvas.VerticalAlignment = VerticalAlignment.Center;

        BindTotals(canvas);

        void ShowTab()
        {
            if (LayoutView.FindNamed<Canvas>(canvas, "panelGameStatistics") is Canvas games)
                games.IsVisible = viewModel.SelectedTab != 1;
            if (LayoutView.FindNamed<Canvas>(canvas, "panelTotalStatistics") is Canvas totals)
                totals.IsVisible = viewModel.SelectedTab == 1;
        }

        viewModel.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(StatisticsViewModel.SelectedTab))
                ShowTab();
        };
        ShowTab();

        return canvas;
    }

    private void BindTotals(Canvas canvas)
    {
        foreach ((_, _, string value, string property, _) in TotalRows)
        {
            if (LayoutView.FindNamed<TextBlock>(canvas, value) is not TextBlock text)
                continue;

            text.DataContext = viewModel;
            text.Bind(TextBlock.TextProperty, new Binding(property == null
                ? nameof(StatisticsViewModel.FavouriteSide)
                : nameof(StatisticsViewModel.Totals) + "." + property));
        }
    }

    private ThemedTabControl TabControl()
    {
        var tabs = new ThemedTabControl { DataContext = viewModel };
        tabs.AddTab("Game Statistics".L10N("Client:Main:GameStatistic"), BUTTON_WIDTH_133);
        tabs.AddTab("Total Statistics".L10N("Client:Main:TotalStatistic"), BUTTON_WIDTH_133);
        tabs.Bind(ThemedTabControl.SelectedTabProperty, new Binding(nameof(StatisticsViewModel.SelectedTab)) { Mode = BindingMode.TwoWay });
        return tabs;
    }

    private ThemedDropDown DropDown(LayoutControl layout, string itemsPath, string selectedPath)
    {
        var dropDown = new ThemedDropDown(layout.Width, 22, layout.FontIndex) { DataContext = viewModel };
        dropDown.Bind(ThemedDropDown.ItemsSourceProperty, new Binding(itemsPath));
        dropDown.Bind(ThemedDropDown.SelectedIndexProperty, new Binding(selectedPath) { Mode = BindingMode.TwoWay });
        return dropDown;
    }

    /// <summary>An XNAMultiColumnListBox: bordered column headers over a list with a translucent black background.</summary>
    private static (StackPanel Panel, ListBox List) MultiColumnList(LayoutControl layout, (string Header, int Width)[] columns)
    {
        int headerHeight = ThemeFonts.Measure("H", 1).Height + 3;
        (FontFamily headerFamily, double headerSize) = ThemeFonts.Get(1);
        var borderBrush = new SolidColorBrush(ThemeAssets.PanelBorderColor);

        var headers = new StackPanel { Orientation = Orientation.Horizontal };
        int usedWidth = 0;
        for (int i = 0; i < columns.Length; i++)
        {
            // The last column takes the rest of the width
            int width = i == columns.Length - 1 ? layout.Width - usedWidth : columns[i].Width;
            usedWidth += width;
            headers.Children.Add(new Border
            {
                Width = width,
                Height = headerHeight,
                BorderBrush = borderBrush,
                BorderThickness = new Thickness(1),
                Child = new TextBlock
                {
                    Text = columns[i].Header,
                    FontFamily = headerFamily,
                    FontSize = headerSize,
                    Foreground = new SolidColorBrush(ThemeAssets.LabelColor),
                    Margin = new Thickness(3, 2, 0, 0),
                },
            });
        }

        ListBox list = ThemedStyle.List(layout.Width, layout.Height - headerHeight + 1);
        list.Background = new SolidColorBrush(Color.FromArgb(128, 0, 0, 0));
        list.Resources["ListBoxItemPadding"] = new Thickness(0);

        return (new StackPanel { Children = { headers, list } }, list);
    }

    private static Grid Row((string Header, int Width)[] columns, IReadOnlyList<Control> cells)
    {
        var grid = new Grid();
        for (int i = 0; i < columns.Length; i++)
        {
            grid.ColumnDefinitions.Add(i == columns.Length - 1 ? new ColumnDefinition(GridLength.Star) : new ColumnDefinition(columns[i].Width, GridUnitType.Pixel));
            Grid.SetColumn(cells[i], i);
            grid.Children.Add(cells[i]);
        }

        return grid;
    }

    private static TextBlock Cell(string text, IBrush brush = null)
    {
        var cell = new TextBlock
        {
            Text = text,
            Margin = new Thickness(3, 1),
            TextTrimming = TextTrimming.None,
            ClipToBounds = true,
        };

        // Only a given colour: a null Foreground would hide the text instead of inheriting the list's colour
        if (brush != null)
            cell.Foreground = brush;

        return cell;
    }

    private Control GameList(LayoutControl layout)
    {
        (string, int)[] columns =
        [
            ("DATE / TIME".L10N("Client:Main:GameMatchDateTimeColumnHeader"), 130),
            ("MAP".L10N("Client:Main:GameMatchMapColumnHeader"), 200),
            ("GAME MODE".L10N("Client:Main:GameMatchGameModeColumnHeader"), 130),
            ("FPS".L10N("Client:Main:GameMatchFPSColumnHeader"), 50),
            ("DURATION".L10N("Client:Main:GameMatchDurationColumnHeader"), 76),
            ("COMPLETED".L10N("Client:Main:GameMatchCompletedColumnHeader"), 90),
        ];

        (StackPanel panel, ListBox list) = MultiColumnList(layout, columns);
        list.DataContext = viewModel;
        list.Bind(ItemsControl.ItemsSourceProperty, new Binding(nameof(StatisticsViewModel.Games)));
        list.Bind(SelectingItemsControl.SelectedIndexProperty, new Binding(nameof(StatisticsViewModel.SelectedGameIndex)) { Mode = BindingMode.TwoWay });
        list.ItemTemplate = new FuncDataTemplate<StatisticsGameRow>((row, _) => row == null ? new Grid() : Row(columns,
            [Cell(row.DateTime), Cell(row.Map), Cell(row.GameMode), Cell(row.Fps), Cell(row.Duration), Cell(row.Completed)]));
        return panel;
    }

    private Control PlayerList(LayoutControl layout)
    {
        (string, int)[] columns =
        [
            ("NAME".L10N("Client:Main:StatisticsName"), 130),
            ("KILLS".L10N("Client:Main:StatisticsKills"), 78),
            ("LOSSES".L10N("Client:Main:StatisticsLosses"), 78),
            (StatisticsViewModel.EconomyHeader, 80),
            ("SCORE".L10N("Client:Main:StatisticsScore"), 100),
            ("WON".L10N("Client:Main:StatisticsWon"), 50),
            ("SIDE".L10N("Client:Main:StatisticsSide"), 100),
            ("TEAM".L10N("Client:Main:StatisticsTeam"), 60),
        ];

        (StackPanel panel, ListBox list) = MultiColumnList(layout, columns);
        list.DataContext = viewModel;
        list.Bind(ItemsControl.ItemsSourceProperty, new Binding(nameof(StatisticsViewModel.Players)));
        list.ItemTemplate = new FuncDataTemplate<StatisticsPlayerRow>((row, _) =>
        {
            if (row == null)
                return new Grid();

            IBrush brush = new SolidColorBrush(row.Color);
            var side = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(3, 1) };
            if (row.SideIcon != null && ThemeAssets.LoadBitmap(row.SideIcon) is { } icon)
                side.Children.Add(new Image { Source = icon, Stretch = Stretch.None, Margin = new Thickness(0, 0, 2, 0) });
            side.Children.Add(new TextBlock { Text = row.Side, Foreground = brush });

            return Row(columns, [Cell(row.Name, brush), Cell(row.Kills, brush), Cell(row.Losses, brush), Cell(row.Economy, brush),
                Cell(row.Score, brush), Cell(row.Won, brush), side, Cell(row.Team, brush)]);
        });

        // Only the local player's row can be selected, and it is (the XNA list marks the others unselectable)
        void SelectLocal()
        {
            int local = -1;
            for (int i = 0; i < viewModel.Players.Count; i++)
            {
                if (viewModel.Players[i].IsLocalPlayer)
                    local = i;
            }

            if (list.SelectedIndex != local)
                list.SelectedIndex = local;
        }

        viewModel.Players.CollectionChanged += (_, _) => SelectLocal();
        list.SelectionChanged += (_, _) => SelectLocal();
        return panel;
    }
}
