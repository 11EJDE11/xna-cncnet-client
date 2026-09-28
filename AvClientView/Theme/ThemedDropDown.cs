using System;
using System.Collections;
using System.Collections.Generic;
using System.Collections.Specialized;
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
    private INotifyCollectionChanged observedItems;
    private bool updatingItems;

    public ThemedDropDown(double width, double height, int fontIndex = 0)
    {
        this.fontIndex = fontIndex;
        Width = width;
        Height = height;
        Background = Brushes.Black;
        BorderBrush = new SolidColorBrush(ThemeAssets.PanelBorderColor);
        BorderThickness = new Thickness(1);
        Cursor = ThemeAssets.HandCursor;

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

        // XNADropDown fills the hovered item with the focus colour (ListBoxFocusColor) and nothing else
        var focus = new SolidColorBrush(ThemeAssets.ListFocusColor);
        list.Resources["ListBoxItemBackgroundPointerOver"] = focus;
        list.Resources["ListBoxItemBackgroundPressed"] = focus;
        list.Resources["ListBoxItemBackgroundSelected"] = Brushes.Transparent;
        list.Resources["ListBoxItemBackgroundSelectedPointerOver"] = focus;
        list.Resources["ListBoxItemBackgroundSelectedPressed"] = focus;
        foreach (string key in new[] { "ListBoxItemForegroundPointerOver", "ListBoxItemForegroundPressed", "ListBoxItemForegroundSelected",
            "ListBoxItemForegroundSelectedPointerOver", "ListBoxItemForegroundSelectedPressed" })
            list.Resources[key] = foreground;
        list.SelectionChanged += (_, _) =>
        {
            if (updatingItems)
                return;
            if (!CanChange || !IsEffectivelyEnabled)
            {
                popup.IsOpen = false;
                return;
            }

            if (popup.IsOpen && list.SelectedIndex >= 0 && ItemSelectable?.Invoke(list.SelectedIndex) == false)
            {
                list.SelectedIndex = -1;
                return;
            }

            if (popup.IsOpen && list.SelectedIndex >= 0)
            {
                // XNADropDown plays its click sound when an item is chosen too
                ThemeSounds.DropDown.Play();
                SelectedIndex = list.SelectedIndex;
                popup.IsOpen = false;
            }
        };

        // Popups are their own windows: the client cursor is set on them too
        var scaler = new LayoutTransformControl { Child = list, Cursor = ThemeAssets.ArrowCursor };
        ThemedAppStyles.ApplyTextRendering(scaler);
        popup = new Popup
        {
            Child = scaler,
            PlacementTarget = this,
            Placement = PlacementMode.Bottom,
            IsLightDismissEnabled = true,
        };

        // Scaled like the window's content
        popup.Opened += (_, _) => scaler.LayoutTransform = ThemeAssets.PopupTransform;

        Child = new Grid { Children = { decoration, text, arrow, popup } };
        AttachedToVisualTree += (_, _) => { ObserveItems(); RefreshItems(); };
        DetachedFromVisualTree += (_, _) =>
        {
            if (observedItems != null)
                observedItems.CollectionChanged -= ItemsChanged;
            observedItems = null;
            popup.IsOpen = false;
        };
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

        if (change.Property == ItemsSourceProperty && list != null)
        {
            ObserveItems();
            RefreshItems();
            return;
        }

        if (change.Property == ItemsSourceProperty || change.Property == SelectedIndexProperty ||
            change.Property == CanChangeProperty || change.Property == IsEffectivelyEnabledProperty)
        {
            UpdateView();
        }
    }

    private void ObserveItems()
    {
        if (observedItems != null)
            observedItems.CollectionChanged -= ItemsChanged;
        observedItems = TopLevel.GetTopLevel(this) != null ? ItemsSource as INotifyCollectionChanged : null;
        if (observedItems != null)
            observedItems.CollectionChanged += ItemsChanged;
    }

    private void ItemsChanged(object sender, NotifyCollectionChangedEventArgs e) => RefreshItems();

    private void RefreshItems()
    {
        updatingItems = true;
        try
        {
            list.SelectedIndex = -1;
            list.ItemsSource = ItemsSource?.Cast<object>().Select((item, i) => new DropDownItem(i, item?.ToString())).ToList();
        }
        finally
        {
            updatingItems = false;
        }
        UpdateView();
    }

    private void UpdateView()
    {
        if (text == null)
            return;

        List<object> items = ItemsSource?.Cast<object>().ToList() ?? [];
        // XNA's AllowDropDown setter closes an open list when it becomes locked.
        // A detached popup must not remain interactive after its owning control is disabled either.
        if (!CanChange || !IsEffectivelyEnabled || items.Count == 0)
            popup.IsOpen = false;

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
        Cursor = arrow.IsVisible ? ThemeAssets.HandCursor : ThemeAssets.ArrowCursor;
    }

    protected override void OnPointerPressed(PointerPressedEventArgs e)
    {
        base.OnPointerPressed(e);

        if (!CanChange || !IsEffectivelyEnabled || !e.GetCurrentPoint(this).Properties.IsLeftButtonPressed)
            return;

        RefreshItems();
        if (list.ItemCount == 0)
            return;
        if (!popup.IsOpen)
            ThemeSounds.DropDown.Play();

        popup.IsOpen = !popup.IsOpen;
        e.Handled = true;
    }

    protected override void OnPointerWheelChanged(PointerWheelEventArgs e)
    {
        base.OnPointerWheelChanged(e);
        if (!CanChange || !IsEffectivelyEnabled || e.Delta.Y == 0 || !new Rect(Bounds.Size).Contains(e.GetPosition(this)))
            return;

        int count = ItemsSource?.Cast<object>().Count() ?? 0;
        int next = SelectedIndex + (e.Delta.Y < 0 ? 1 : -1);
        if (next < 0 || next >= count)
            return;

        // XNA tries the adjacent item only: it does not skip disabled choices.
        e.Handled = true;
        if (ItemSelectable?.Invoke(next) != false)
            SetCurrentValue(SelectedIndexProperty, next);
    }
}
