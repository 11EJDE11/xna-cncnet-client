using System;
using System.Collections.Generic;

using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Controls.Templates;
using Avalonia.Data;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Media.Imaging;

using AvClientView.Theme;
using AvClientView.ViewModels;

using ClientCore.Extensions;

using ClientLogic.Layout;
using ClientLogic.Tunnels;

using Rampastring.Tools;

namespace AvClientView.Views;

/// <summary>
/// The XNA TunnelSelectionWindow over a darkening panel: the description, the tunnel mode, the TunnelListBox (flag,
/// name, official, ping, players; covered in dynamic mode), Apply and Cancel; with the theme's
/// TunnelSelectionWindow.ini applied.
/// </summary>
public sealed class TunnelSelectionView : Panel
{
    private const int EMPTY_SPACE = 6;
    private const int MARGIN = 6;
    private const int BUTTON_WIDTH_92 = 92;
    private const int BUTTON_HEIGHT = 23;
    private const int LIST_WIDTH = 466;
    private const int FLAG_COLUMN_WIDTH = 20;

    private static readonly (string Header, int Width)[] Columns =
    [
        (string.Empty, FLAG_COLUMN_WIDTH),
        ("Name".L10N("Client:Main:NameHeader"), 210),
        ("Official".L10N("Client:Main:OfficialHeader"), 70),
        ("Ping".L10N("Client:Main:PingHeader"), 76),
        ("Players".L10N("Client:Main:PlayersHeader"), 90),
    ];

    private readonly TunnelSelectionViewModel viewModel;
    private static readonly Dictionary<int, CroppedBitmap> flags = [];

    public TunnelSelectionView(TunnelSelectionViewModel viewModel)
    {
        this.viewModel = viewModel;
        ThemedStyle.Darken(this);
        DataContext = viewModel;
        this.Bind(IsVisibleProperty, new Binding(nameof(TunnelSelectionViewModel.IsOpen)));

        try
        {
            Children.Add(Build());
        }
        catch (Exception ex)
        {
            Logger.Log("TunnelSelectionView: building the tunnel selection window failed: " + ex);
            Children.Add(new TextBlock { Text = "The tunnel selection window could not be built: " + ex.Message, Foreground = Brushes.White });
        }
    }

    private static int HeaderHeight => ThemeFonts.Measure("Name", 1).Height;

    /// <summary>XNAMultiColumnListBox's line height: the font's height less one.</summary>
    private static int LineHeight => ThemeFonts.Measure("Test String @", 0).Height - 1;

    private Canvas Build()
    {
        var window = new LayoutControl("TunnelSelectionWindow", "XNAWindow", LayoutControlKind.Panel)
        {
            BackgroundTexture = "gamecreationoptionsbg.png",
        };

        int x = EMPTY_SPACE + MARGIN;
        int descriptionHeight = ThemeFonts.Measure("Line 1" + Environment.NewLine + "Line 2", 0).Height;
        ThemedWindow.Add(window, "lblDescription", "XNALabel", x, EMPTY_SPACE + MARGIN, 0, descriptionHeight);
        LayoutControl ddMode = ThemedWindow.Add(window, "ddMode", "XNADropDown", x, EMPTY_SPACE + MARGIN + descriptionHeight + MARGIN, 220, BUTTON_HEIGHT);
        int listHeight = LineHeight * 12 + HeaderHeight + 3;
        LayoutControl lbTunnelList = ThemedWindow.Add(window, "lbTunnelList", "TunnelListBox", x, ddMode.Y + ddMode.Height + MARGIN, LIST_WIDTH, listHeight);
        LayoutControl btnApply = ThemedWindow.Add(window, "btnApply", "XNAClientButton", x, lbTunnelList.Y + lbTunnelList.Height + MARGIN * 3,
            BUTTON_WIDTH_92, BUTTON_HEIGHT, "Apply".L10N("Client:Main:ButtonApply"));

        window.Width = lbTunnelList.X + lbTunnelList.Width + MARGIN + EMPTY_SPACE;
        window.Height = btnApply.Y + btnApply.Height + MARGIN + EMPTY_SPACE;
        ThemedWindow.Add(window, "btnCancel", "XNAClientButton", window.Width - BUTTON_WIDTH_92 - EMPTY_SPACE - MARGIN, btnApply.Y,
            BUTTON_WIDTH_92, BUTTON_HEIGHT, "Cancel".L10N("Client:Main:ButtonCancel"));

        Canvas canvas = ThemedWindow.Build(window, (name, _) =>
        {
            if (name == "btnApply" && viewModel.CanApply)
                viewModel.Apply();
            else if (name == "btnCancel")
                viewModel.Cancel();
        }, new Dictionary<string, Func<LayoutControl, Control>>
        {
            ["lblDescription"] = Description,
            ["ddMode"] = Mode,
            ["lbTunnelList"] = layout => TunnelList(layout, viewModel.List),
        });

        if (LayoutView.FindNamed<ThemedButton>(canvas, "btnApply") is ThemedButton apply)
        {
            apply.DataContext = viewModel;
            apply.Bind(IsEnabledProperty, new Binding(nameof(TunnelSelectionViewModel.CanApply)));
        }

        canvas.HorizontalAlignment = HorizontalAlignment.Center;
        canvas.VerticalAlignment = VerticalAlignment.Center;
        return canvas;
    }

    private Control Description(LayoutControl layout)
    {
        (FontFamily family, double size) = ThemeFonts.Get(layout.FontIndex);
        var text = new TextBlock
        {
            FontFamily = family,
            FontSize = size,
            Foreground = new SolidColorBrush(ThemeAssets.LabelColor),
            DataContext = viewModel,
        };
        text.Bind(TextBlock.TextProperty, new Binding(nameof(TunnelSelectionViewModel.Description)));
        return text;
    }

    private Control Mode(LayoutControl layout)
    {
        var dropDown = new ThemedDropDown(layout.Width, layout.Height, layout.FontIndex) { DataContext = viewModel };
        dropDown.Bind(ThemedDropDown.ItemsSourceProperty, new Binding(nameof(TunnelSelectionViewModel.List) + "." + nameof(TunnelListViewModel.ModeItems)));
        dropDown.Bind(ThemedDropDown.SelectedIndexProperty, new Binding(nameof(TunnelSelectionViewModel.SelectedModeIndex)) { Mode = BindingMode.TwoWay });
        return dropDown;
    }

    private static IImage FlagImage(int? offset)
    {
        if (offset is not int y || ThemeAssets.EmbeddedIcon("flags16.png") is not Bitmap sheet)
            return null;

        if (!flags.TryGetValue(y, out CroppedBitmap flag))
        {
            flag = new CroppedBitmap(sheet, new PixelRect(0, y, TunnelFlags.FLAG_WIDTH, TunnelFlags.FLAG_HEIGHT));
            flags[y] = flag;
        }

        return flag;
    }

    /// <summary>The TunnelListBox, with the translucent black cover of dynamic mode (pnlTunnelListDisabledOverlay).</summary>
    public static Control TunnelList(LayoutControl layout, TunnelListViewModel viewModel)
    {
        int headerHeight = HeaderHeight + 3;
        (FontFamily headerFamily, double headerSize) = ThemeFonts.Get(1);
        var borderBrush = new SolidColorBrush(ThemeAssets.PanelBorderColor);

        var headers = new StackPanel { Orientation = Orientation.Horizontal };
        int usedWidth = 0;
        for (int i = 0; i < Columns.Length; i++)
        {
            int width = i == Columns.Length - 1 ? layout.Width - usedWidth : Columns[i].Width;
            usedWidth += width;
            headers.Children.Add(new Border
            {
                Width = width,
                Height = headerHeight,
                BorderBrush = borderBrush,
                BorderThickness = new Thickness(1),
                Child = new TextBlock
                {
                    Text = Columns[i].Header,
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
        list.DataContext = viewModel;
        list.Bind(ItemsControl.ItemsSourceProperty, new Binding(nameof(TunnelListViewModel.Tunnels)));
        list.Bind(SelectingItemsControl.SelectedIndexProperty, new Binding(nameof(TunnelListViewModel.SelectedTunnelIndex)) { Mode = BindingMode.TwoWay });
        list.ItemTemplate = new FuncDataTemplate<TunnelRow>((row, _) =>
        {
            var grid = new Grid { Height = LineHeight };
            for (int i = 0; i < Columns.Length; i++)
                grid.ColumnDefinitions.Add(i == Columns.Length - 1 ? new ColumnDefinition(GridLength.Star) : new ColumnDefinition(Columns[i].Width, GridUnitType.Pixel));

            if (row == null)
                return grid;

            var flag = new Image { Source = FlagImage(row.FlagOffset), Width = TunnelFlags.FLAG_WIDTH, Height = TunnelFlags.FLAG_HEIGHT, Stretch = Stretch.None };
            grid.Children.Add(flag);

            string[] texts = [row.Name, row.Official, row.Ping, row.Players];
            for (int i = 0; i < texts.Length; i++)
            {
                var cell = new TextBlock { Text = texts[i], Margin = new Thickness(3, 0), VerticalAlignment = VerticalAlignment.Center, ClipToBounds = true };
                Grid.SetColumn(cell, i + 1);
                grid.Children.Add(cell);
            }

            return grid;
        });
        list.Bind(IsHitTestVisibleProperty, new Binding(nameof(TunnelListViewModel.IsListEnabled)));

        var cover = new Border { Background = new SolidColorBrush(Color.FromArgb(128, 0, 0, 0)), DataContext = viewModel };
        cover.Bind(IsVisibleProperty, new Binding(nameof(TunnelListViewModel.IsListEnabled)) { Converter = Avalonia.Data.Converters.BoolConverters.Not });

        return new Grid
        {
            Width = layout.Width,
            Height = layout.Height,
            Children = { new StackPanel { Children = { headers, list } }, cover },
        };
    }
}
