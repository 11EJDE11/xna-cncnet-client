using System.Collections;
using System.Collections.Generic;
using System.Linq;

using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Data;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Media.Imaging;

namespace AvClientView.Theme;

/// <summary>
/// An XNA-style drop-down: the selected item's text on black with a thin border, the theme's comboBoxArrow.png on
/// the right while it can be changed, and a list below when opened. Items are strings.
/// </summary>
public sealed class ThemedDropDown : Border
{
    public static readonly StyledProperty<IEnumerable> ItemsSourceProperty =
        AvaloniaProperty.Register<ThemedDropDown, IEnumerable>(nameof(ItemsSource));

    public static readonly StyledProperty<int> SelectedIndexProperty =
        AvaloniaProperty.Register<ThemedDropDown, int>(nameof(SelectedIndex), -1, defaultBindingMode: BindingMode.TwoWay);

    /// <summary>The value can be changed (XNA AllowDropDown); otherwise the arrow is hidden and clicks do nothing.</summary>
    public static readonly StyledProperty<bool> CanChangeProperty =
        AvaloniaProperty.Register<ThemedDropDown, bool>(nameof(CanChange), true);

    private static readonly Bitmap Arrow = ThemeAssets.LoadBitmap("comboBoxArrow.png");

    private readonly TextBlock text;
    private readonly Image arrow;
    private readonly Popup popup;
    private readonly ListBox list;
    private readonly int fontIndex;

    public ThemedDropDown(double width, double height, int fontIndex = 0)
    {
        this.fontIndex = fontIndex;
        Width = width;
        Height = height;
        Background = Brushes.Black;
        BorderBrush = new SolidColorBrush(ThemeAssets.PanelBorderColor);
        BorderThickness = new Thickness(1);
        Cursor = new Cursor(StandardCursorType.Hand);

        (FontFamily family, double size) = ThemeFonts.Get(fontIndex);
        IBrush foreground = new SolidColorBrush(ThemeAssets.ButtonTextColor);

        text = new TextBlock
        {
            FontFamily = family,
            FontSize = size,
            Foreground = foreground,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(3, 0, 0, 0),
            TextTrimming = TextTrimming.None,
            ClipToBounds = true,
        };

        arrow = new Image
        {
            Source = Arrow,
            Stretch = Stretch.None,
            HorizontalAlignment = HorizontalAlignment.Right,
            VerticalAlignment = VerticalAlignment.Center,
        };

        list = new ListBox
        {
            Background = Brushes.Black,
            Foreground = foreground,
            FontFamily = family,
            FontSize = size,
            BorderBrush = BorderBrush,
            BorderThickness = new Thickness(1),
            Padding = new Thickness(0),
            MinWidth = width,
            MaxHeight = 400,
            ItemTemplate = new Avalonia.Controls.Templates.FuncDataTemplate<object>((item, _) =>
                new TextBlock { Text = item?.ToString(), Margin = new Thickness(3, 1) }),
        };
        list.Resources["ListBoxItemPadding"] = new Thickness(0);
        list.SelectionChanged += (_, _) =>
        {
            if (popup.IsOpen && list.SelectedIndex >= 0)
            {
                SelectedIndex = list.SelectedIndex;
                popup.IsOpen = false;
            }
        };

        popup = new Popup
        {
            Child = list,
            PlacementTarget = this,
            Placement = PlacementMode.Bottom,
            IsLightDismissEnabled = true,
        };

        Child = new Grid { Children = { text, arrow, popup } };
        UpdateView();
    }

    public IEnumerable ItemsSource
    {
        get => GetValue(ItemsSourceProperty);
        set => SetValue(ItemsSourceProperty, value);
    }

    public int SelectedIndex
    {
        get => GetValue(SelectedIndexProperty);
        set => SetValue(SelectedIndexProperty, value);
    }

    public bool CanChange
    {
        get => GetValue(CanChangeProperty);
        set => SetValue(CanChangeProperty, value);
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);

        if (change.Property == ItemsSourceProperty || change.Property == SelectedIndexProperty ||
            change.Property == CanChangeProperty || change.Property == IsEffectivelyEnabledProperty)
        {
            UpdateView();
        }
    }

    private void UpdateView()
    {
        if (text == null)
            return;

        List<object> items = ItemsSource?.Cast<object>().ToList() ?? [];
        int index = SelectedIndex;
        text.Text = index >= 0 && index < items.Count ? items[index]?.ToString() : string.Empty;
        arrow.IsVisible = CanChange && IsEffectivelyEnabled && items.Count > 0;
        text.Margin = new Thickness(3, 0, arrow.IsVisible && Arrow != null ? Arrow.PixelSize.Width : 0, 0);
        Cursor = arrow.IsVisible ? new Cursor(StandardCursorType.Hand) : Cursor.Default;
    }

    protected override void OnPointerPressed(PointerPressedEventArgs e)
    {
        base.OnPointerPressed(e);

        if (!CanChange || !IsEffectivelyEnabled || !e.GetCurrentPoint(this).Properties.IsLeftButtonPressed)
            return;

        list.ItemsSource = ItemsSource;
        list.SelectedIndex = -1;
        popup.IsOpen = !popup.IsOpen;
        e.Handled = true;
    }
}
