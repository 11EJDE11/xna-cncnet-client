using System;

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
/// An XNA-style check box: the theme's checkBoxChecked/checkBoxClear textures (the "D" variants when disabled) and
/// the label to the right.
/// </summary>
public sealed class ThemedCheckBox : StackPanel
{
    public static readonly StyledProperty<bool> IsCheckedProperty =
        AvaloniaProperty.Register<ThemedCheckBox, bool>(nameof(IsChecked), defaultBindingMode: BindingMode.TwoWay);

    private static readonly Bitmap Checked = ThemeAssets.LoadBitmap("checkBoxChecked.png");
    private static readonly Bitmap Clear = ThemeAssets.LoadBitmap("checkBoxClear.png");
    private static readonly Bitmap CheckedDisabled = ThemeAssets.LoadBitmap("checkBoxCheckedD.png") ?? Checked;
    private static readonly Bitmap ClearDisabled = ThemeAssets.LoadBitmap("checkBoxClearD.png") ?? Clear;

    private readonly Image box;
    private readonly TextBlock label;

    public ThemedCheckBox(string text, int fontIndex = 0)
    {
        Orientation = Orientation.Horizontal;
        Spacing = 3;
        Background = Brushes.Transparent;
        Cursor = new Cursor(StandardCursorType.Hand);

        (FontFamily family, double size) = ThemeFonts.Get(fontIndex);
        box = new Image { Stretch = Stretch.None, VerticalAlignment = VerticalAlignment.Center };
        label = new TextBlock
        {
            Text = text,
            FontFamily = family,
            FontSize = size,
            Foreground = new SolidColorBrush(ThemeAssets.LabelColor),
            VerticalAlignment = VerticalAlignment.Center,
        };

        Children.Add(box);
        Children.Add(label);
        UpdateImage();
    }

    public bool IsChecked
    {
        get => GetValue(IsCheckedProperty);
        set => SetValue(IsCheckedProperty, value);
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);

        if (change.Property == IsCheckedProperty || change.Property == IsEffectivelyEnabledProperty)
            UpdateImage();
    }

    private void UpdateImage()
    {
        if (box == null)
            return;

        bool enabled = IsEffectivelyEnabled;
        box.Source = IsChecked ? (enabled ? Checked : CheckedDisabled) : (enabled ? Clear : ClearDisabled);
        label.Opacity = enabled ? 1 : 0.6;
    }

    protected override void OnPointerReleased(Avalonia.Input.PointerReleasedEventArgs e)
    {
        base.OnPointerReleased(e);

        if (IsEffectivelyEnabled && e.InitialPressMouseButton == MouseButton.Left)
        {
            IsChecked = !IsChecked;
            ThemeSounds.CheckBox.Play();
            e.Handled = true;
        }
    }
}

/// <summary>Makes Avalonia's standard controls look like the XNA client's: black backgrounds, theme colours and fonts.</summary>
public static class ThemedStyle
{
    public static IBrush PanelBackground { get; } = new SolidColorBrush(Color.FromArgb(200, 0, 0, 0));

    public static T Apply<T>(T control, int fontIndex = 0) where T : TemplatedControl
    {
        (FontFamily family, double size) = ThemeFonts.Get(fontIndex);
        control.FontFamily = family;
        control.FontSize = size;
        control.Foreground = new SolidColorBrush(ThemeAssets.ButtonTextColor);
        control.Background = Brushes.Black;
        control.BorderBrush = new SolidColorBrush(ThemeAssets.PanelBorderColor);
        control.BorderThickness = new Thickness(1);
        control.CornerRadius = new CornerRadius(0);
        control.MinHeight = 0;
        control.Padding = new Thickness(4, 0, 2, 0);
        return control;
    }

    public static ComboBox DropDown(double width, double height, int fontIndex = 0)
    {
        var comboBox = Apply(new ComboBox { Width = width, Height = height, VerticalContentAlignment = VerticalAlignment.Center }, fontIndex);
        comboBox.Resources["ComboBoxDropDownBackground"] = Brushes.Black;
        comboBox.Resources["ComboBoxBackgroundPointerOver"] = Brushes.Black;
        comboBox.Resources["ComboBoxBackgroundDisabled"] = Brushes.Black;
        comboBox.Resources["ComboBoxForegroundDisabled"] = new SolidColorBrush(Color.FromArgb(160, 160, 160, 160));
        return comboBox;
    }

    public static ListBox List(double width, double height, int fontIndex = 0)
    {
        var listBox = Apply(new ListBox { Width = width, Height = height }, fontIndex);
        listBox.Background = PanelBackground;
        listBox.Padding = new Thickness(0);
        return listBox;
    }

    public static TextBox TextBox(double width, double height, string watermark, int fontIndex = 0)
    {
        var textBox = Apply(new TextBox { Width = width, Height = height, Watermark = watermark, VerticalContentAlignment = VerticalAlignment.Center }, fontIndex);
        textBox.Resources["TextControlBackgroundPointerOver"] = Brushes.Black;
        textBox.Resources["TextControlBackgroundFocused"] = Brushes.Black;
        textBox.Resources["TextControlForegroundFocused"] = new SolidColorBrush(ThemeAssets.ButtonTextColor);
        textBox.Resources["TextControlForegroundPointerOver"] = new SolidColorBrush(ThemeAssets.ButtonTextColor);
        return textBox;
    }
}
