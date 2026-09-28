using System;
using System.Collections.Generic;

using Avalonia;
using Avalonia.Controls;
using Avalonia.Data;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Media.Imaging;

namespace AvClientView.Theme;

/// <summary>
/// XNAClientTabControl: tabs side by side, each drawn with "{width}pxtab.png" ("_c" when selected), or the
/// "{width}pxbtn.png" button textures if the theme has no tab textures; text centred in the alt colour, or the
/// disabled colour for tabs that can't be selected. Clicking a selectable tab plays button.wav.
/// </summary>
public sealed class ThemedTabControl : StackPanel
{
    public static readonly StyledProperty<int> SelectedTabProperty =
        AvaloniaProperty.Register<ThemedTabControl, int>(nameof(SelectedTab), defaultBindingMode: BindingMode.TwoWay);

    private readonly List<(Image Image, Bitmap Default, Bitmap Pressed, bool Selectable)> tabs = [];
    private readonly ThemeSound clickSound = ThemeSounds.ButtonHover;

    public ThemedTabControl(int fontIndex = 1)
    {
        Orientation = Orientation.Horizontal;
        FontIndex = fontIndex;
    }

    public int FontIndex { get; }

    public int SelectedTab
    {
        get => GetValue(SelectedTabProperty);
        set => SetValue(SelectedTabProperty, value);
    }

    public void AddTab(string text, int width, bool selectable = true)
    {
        string asset = width + "pxtab";
        bool hasTabTexture = ThemeAssets.FindFile(asset + ".png") != null;
        Bitmap defaultTexture = ThemeAssets.LoadBitmap(hasTabTexture ? asset + ".png" : width + "pxbtn.png");
        Bitmap pressedTexture = ThemeAssets.LoadBitmap(hasTabTexture ? asset + "_c.png" : width + "pxbtn_c.png") ?? defaultTexture;

        (FontFamily family, double size) = ThemeFonts.Get(FontIndex);
        var image = new Image { Source = defaultTexture, Stretch = Stretch.None };
        var caption = new TextBlock
        {
            Text = text,
            FontFamily = family,
            FontSize = size,
            Foreground = new SolidColorBrush(selectable ? ThemeAssets.ButtonTextColor : ThemeAssets.DisabledItemColor),
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
        };

        int index = tabs.Count;
        var tab = new Grid
        {
            Width = defaultTexture?.PixelSize.Width ?? width,
            Height = defaultTexture?.PixelSize.Height ?? 23,
            Background = Brushes.Transparent,
            Cursor = selectable ? ThemeAssets.HandCursor : ThemeAssets.ArrowCursor,
            Children = { image, caption },
        };
        tab.PointerPressed += (_, e) =>
        {
            if (!selectable || !e.GetCurrentPoint(tab).Properties.IsLeftButtonPressed)
                return;

            clickSound.Play();
            SelectedTab = index;
            e.Handled = true;
        };

        tabs.Add((image, defaultTexture, pressedTexture, selectable));
        Children.Add(tab);
        UpdateTabs();
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property == SelectedTabProperty)
            UpdateTabs();
    }

    private void UpdateTabs()
    {
        for (int i = 0; i < tabs.Count; i++)
            tabs[i].Image.Source = i == SelectedTab ? tabs[i].Pressed : tabs[i].Default;
    }
}
