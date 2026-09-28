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

using ClientCore.Extensions;

using ClientLogic.Layout;

using Rampastring.Tools;

namespace AvClientView.Views;

/// <summary>
/// The XNA GameLoadingWindow over a darkening panel: the saved games in two columns (name, date / time), Load, Delete
/// and Cancel, with the theme's GameLoadingWindow.ini applied.
/// </summary>
public sealed class LoadGameView : Panel
{
    private readonly LoadGameViewModel viewModel;

    public LoadGameView(LoadGameViewModel viewModel)
    {
        this.viewModel = viewModel;
        ThemedStyle.Darken(this);
        IsVisible = false;

        viewModel.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(LoadGameViewModel.IsOpen))
                IsVisible = viewModel.IsOpen;
        };

        try
        {
            Children.Add(Build());
        }
        catch (Exception ex)
        {
            Logger.Log("LoadGameView: building the load game window failed: " + ex);
            Children.Add(new TextBlock { Text = "The load game window could not be built: " + ex.Message, Foreground = Brushes.White });
        }
    }

    private Canvas Build()
    {
        var window = new LayoutControl("GameLoadingWindow", "XNAWindow", LayoutControlKind.Panel)
        {
            Width = 600,
            Height = 380,
            BackgroundTexture = "loadmissionbg.png",
        };

        ThemedWindow.Add(window, "lbSaveGameList", "XNAMultiColumnListBox", 13, 13, 574, 317);
        LayoutControl btnLaunch = ThemedWindow.Add(window, "btnLaunch", "XNAClientButton", 125, 345, 110, 23, "Load".L10N("Client:Main:ButtonLoad"));
        LayoutControl btnDelete = ThemedWindow.Add(window, "btnDelete", "XNAClientButton", btnLaunch.X + btnLaunch.Width + 10, btnLaunch.Y, 110, 23, "Delete".L10N("Client:Main:ButtonDelete"));
        ThemedWindow.Add(window, "btnCancel", "XNAClientButton", btnDelete.X + btnDelete.Width + 10, btnLaunch.Y, 110, 23, "Cancel".L10N("Client:Main:ButtonCancel"));

        Canvas canvas = ThemedWindow.Build(window, (name, _) =>
        {
            switch (name)
            {
                case "btnLaunch":
                    viewModel.Load();
                    break;
                case "btnDelete":
                    viewModel.Delete();
                    break;
                case "btnCancel":
                    viewModel.Cancel();
                    break;
            }
        }, new Dictionary<string, Func<LayoutControl, Control>> { ["lbSaveGameList"] = SaveList });

        canvas.HorizontalAlignment = HorizontalAlignment.Center;
        canvas.VerticalAlignment = VerticalAlignment.Center;

        foreach (string name in new[] { "btnLaunch", "btnDelete" })
        {
            if (LayoutView.FindNamed<ThemedButton>(canvas, name) is ThemedButton button)
            {
                button.DataContext = viewModel;
                button.Bind(IsEnabledProperty, new Binding(nameof(LoadGameViewModel.HasSelection)));
            }
        }

        return canvas;
    }

    /// <summary>The XNAMultiColumnListBox: "SAVED GAME NAME" (400 px) and "DATE / TIME" with bordered headers.</summary>
    private Control SaveList(LayoutControl layout)
    {
        (FontFamily headerFamily, double headerSize) = ThemeFonts.Get(1);
        int headerHeight = ThemeFonts.Measure("SAVED GAME NAME", 1).Height + 3;
        var borderBrush = new SolidColorBrush(ThemeAssets.PanelBorderColor);

        Border Header(string text, double width) => new()
        {
            Width = width,
            Height = headerHeight,
            BorderBrush = borderBrush,
            BorderThickness = new Thickness(1),
            Child = new TextBlock
            {
                Text = text,
                FontFamily = headerFamily,
                FontSize = headerSize,
                Foreground = new SolidColorBrush(ThemeAssets.LabelColor),
                Margin = new Thickness(3, 2, 0, 0),
            },
        };

        var headers = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Children =
            {
                Header("SAVED GAME NAME".L10N("Client:Main:SavedGameNameColumnHeader"), 400),
                Header("DATE / TIME".L10N("Client:Main:SavedGameDateTimeColumnHeader"), layout.Width - 400),
            },
        };

        ListBox list = ThemedStyle.List(layout.Width, layout.Height - headerHeight + 1);
        list.Background = new SolidColorBrush(Color.FromArgb(128, 0, 0, 0));
        list.DataContext = viewModel;
        list.Bind(ItemsControl.ItemsSourceProperty, new Binding(nameof(LoadGameViewModel.SavedGames)));
        list.Bind(SelectingItemsControl.SelectedIndexProperty, new Binding(nameof(LoadGameViewModel.SelectedIndex)) { Mode = BindingMode.TwoWay });
        list.Resources["ListBoxItemPadding"] = new Thickness(0);
        list.ItemTemplate = new FuncDataTemplate<SavedGameItemViewModel>((row, _) =>
        {
            var grid = new Grid { ColumnDefinitions = new ColumnDefinitions("400,*") };
            var name = new TextBlock { Text = row?.Name, Margin = new Thickness(5, 1), TextTrimming = TextTrimming.CharacterEllipsis };
            var date = new TextBlock { Text = row?.DateTime, Margin = new Thickness(5, 1) };
            Grid.SetColumn(date, 1);
            grid.Children.Add(name);
            grid.Children.Add(date);
            return grid;
        });
        list.DoubleTapped += (_, _) => viewModel.Load();

        return new StackPanel { Children = { headers, list } };
    }
}
