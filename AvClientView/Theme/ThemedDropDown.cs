using System;
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
    private readonly ContentControl decoration;
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
            RenderTransform = new TranslateTransform(0, ThemeFonts.CenteringOffset(fontIndex)),
            Margin = new Thickness(3, 0, 0, 0),
            TextTrimming = TextTrimming.None,
            ClipToBounds = true,
        };

        decoration = new ContentControl
        {
            VerticalAlignment = VerticalAlignment.Center,
            HorizontalAlignment = HorizontalAlignment.Left,
            Margin = new Thickness(2, 0, 0, 0),
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
            ItemTemplate = new Avalonia.Controls.Templates.FuncDataTemplate<DropDownItem>((item, _) =>
            {
                var row = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 3, Margin = new Thickness(3, 1) };
                if (item != null && ItemDecoration?.Invoke(item.Index) is Control itemDecoration)
                    row.Children.Add(itemDecoration);
                var itemText = new TextBlock { Text = item?.Text, VerticalAlignment = VerticalAlignment.Center };
                if (item != null && ItemTextColor?.Invoke(item.Index) is Color color)
                    itemText.Foreground = new SolidColorBrush(color);
                if (item != null && ItemSelectable?.Invoke(item.Index) == false)
                    itemText.Foreground = new SolidColorBrush(ThemeAssets.DisabledItemColor);

                row.Children.Add(itemText);
                return row;
            }),
        };
        list.Resources["ListBoxItemPadding"] = new Thickness(0);
        list.SelectionChanged += (_, _) =>
        {
            if (popup.IsOpen && list.SelectedIndex >= 0 && ItemSelectable?.Invoke(list.SelectedIndex) == false)
            {
                list.SelectedIndex = -1;
                return;
            }

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

        Child = new Grid { Children = { decoration, text, arrow, popup } };
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

    /// <summary>Whether an item can be picked (XNADropDownItem.Selectable); others are drawn in the disabled colour.</summary>
    public Func<int, bool> ItemSelectable { get; set; }

    /// <summary>An icon or colour swatch drawn left of an item's text (XNA TextAndIcon items), by item index.</summary>
    public Func<int, Control> ItemDecoration { get; set; }

    /// <summary>Per-item text colour, including the closed drop-down's selected item.</summary>
    public Func<int, Color?> ItemTextColor { get; set; }

    private sealed record DropDownItem(int Index, string Text);

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
        bool hasItem = index >= 0 && index < items.Count;
        text.Text = hasItem ? items[index]?.ToString() : string.Empty;
        text.Foreground = new SolidColorBrush(hasItem ? ItemTextColor?.Invoke(index) ?? ThemeAssets.ButtonTextColor : ThemeAssets.ButtonTextColor);
        arrow.IsVisible = CanChange && IsEffectivelyEnabled && items.Count > 0;

        Control itemDecoration = hasItem ? ItemDecoration?.Invoke(index) : null;
        decoration.Content = itemDecoration;
        double left = 3;
        if (itemDecoration != null)
        {
            itemDecoration.Measure(Size.Infinity);
            left = 2 + itemDecoration.DesiredSize.Width + 3;
        }

        text.Margin = new Thickness(left, 0, arrow.IsVisible && Arrow != null ? Arrow.PixelSize.Width : 0, 0);
        Cursor = arrow.IsVisible ? new Cursor(StandardCursorType.Hand) : Cursor.Default;
    }

    protected override void OnPointerPressed(PointerPressedEventArgs e)
    {
        base.OnPointerPressed(e);

        if (!CanChange || !IsEffectivelyEnabled || !e.GetCurrentPoint(this).Properties.IsLeftButtonPressed)
            return;

        list.ItemsSource = ItemsSource?.Cast<object>().Select((item, i) => new DropDownItem(i, item?.ToString())).ToList();
        list.SelectedIndex = -1;
        if (!popup.IsOpen)
            ThemeSounds.DropDown.Play();

        popup.IsOpen = !popup.IsOpen;
        e.Handled = true;
    }
}
