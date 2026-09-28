using System;
using System.Linq;

using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Data;
using Avalonia.Input;
using Avalonia.Input.Platform;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.VisualTree;

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
    private readonly Image check;
    private bool hovered;
    private readonly TextBlock label;

    public ThemedCheckBox(string text, int fontIndex = 0)
    {
        Orientation = Orientation.Horizontal;
        Spacing = 5;
        Background = Brushes.Transparent;
        Cursor = ThemeAssets.HandCursor;

        (FontFamily family, double size) = ThemeFonts.Get(fontIndex);
        // XNACheckBox draws the clear texture and fades the checked one over it (CheckBoxAlphaRate per 10 ms)
        box = new Image { Stretch = Stretch.None, VerticalAlignment = VerticalAlignment.Center };
        check = new Image { Stretch = Stretch.None, VerticalAlignment = VerticalAlignment.Center };
        label = new TextBlock
        {
            Text = text,
            FontFamily = family,
            FontSize = size,
            Foreground = new SolidColorBrush(ThemeAssets.LabelColor),
            VerticalAlignment = VerticalAlignment.Center,
            RenderTransform = new TranslateTransform(0, ThemeFonts.CenteringOffset(fontIndex)),
        };

        Children.Add(new Grid { Children = { box, check } });
        Children.Add(label);
        UpdateImage();

        // The first value (usually from a binding) shows at once, as XNACheckBox's Initialize does; changes fade
        float alphaRate = ClientCore.ClientConfiguration.Instance.CheckBoxAlphaRate;
        AttachedToVisualTree += (_, _) => Avalonia.Threading.Dispatcher.UIThread.Post(() =>
        {
            if (alphaRate > 0 && check.Transitions == null)
            {
                check.Transitions =
                [
                    new Avalonia.Animation.DoubleTransition { Property = OpacityProperty, Duration = TimeSpan.FromMilliseconds(Math.Min(10 / alphaRate, 5000)) },
                ];
            }
        });
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
        box.Source = enabled ? Clear : ClearDisabled;
        check.Source = enabled ? Checked : CheckedDisabled;
        check.Opacity = IsChecked ? 1 : 0;
        label.Foreground = new SolidColorBrush(!enabled ? Colors.Gray : hovered ? ThemeAssets.ButtonTextColor : ThemeAssets.LabelColor);
    }

    // The text is in the highlight colour under the mouse (XNACheckBox's HighlightColor)
    protected override void OnPointerEntered(Avalonia.Input.PointerEventArgs e)
    {
        base.OnPointerEntered(e);
        hovered = true;
        UpdateImage();
    }

    protected override void OnPointerExited(Avalonia.Input.PointerEventArgs e)
    {
        base.OnPointerExited(e);
        hovered = false;
        UpdateImage();
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

    /// <summary>
    /// ClientGUI's DarkeningPanel: black at alpha 128 behind a window, fading in (ALPHA_RATE 0.6 per 100 ms) whenever
    /// it's shown. The window itself appears at once, as in XNA.
    /// </summary>
    public static void Darken(Panel panel)
    {
        var brush = new SolidColorBrush(Color.FromArgb(128, 0, 0, 0));
        panel.Background = brush;
        var fade = new Avalonia.Animation.Transitions
        {
            new Avalonia.Animation.DoubleTransition { Property = Brush.OpacityProperty, Duration = TimeSpan.FromMilliseconds(165) },
        };

        void FadeIn()
        {
            brush.Transitions = null;
            brush.Opacity = 0.01;
            brush.Transitions = fade;
            Avalonia.Threading.Dispatcher.UIThread.Post(() => brush.Opacity = 1, Avalonia.Threading.DispatcherPriority.Render);
        }

        panel.AttachedToVisualTree += (_, _) =>
        {
            if (panel.IsVisible)
                FadeIn();
        };
        panel.PropertyChanged += (_, e) =>
        {
            if (e.Property != Visual.IsVisibleProperty || TopLevel.GetTopLevel(panel) == null)
                return;

            if (panel.IsVisible)
                FadeIn();
            else if (panel.Parent is Panel parent)
                FadeOutDarkening(parent, panel.ZIndex, brush.Opacity);
        };
    }

    /// <summary>
    /// DarkeningPanel.Hide: the window goes at once and the darkening fades out (ALPHA_RATE), drawn by a short-lived
    /// layer where the panel was.
    /// </summary>
    public static void FadeOutDarkening(Panel host, int zIndex, double fromOpacity = 1)
    {
        var brush = new SolidColorBrush(Color.FromArgb(128, 0, 0, 0)) { Opacity = fromOpacity };
        var layer = new Border { Background = brush, IsHitTestVisible = false, ZIndex = zIndex };
        host.Children.Add(layer);

        brush.Transitions =
        [
            new Avalonia.Animation.DoubleTransition { Property = Brush.OpacityProperty, Duration = TimeSpan.FromMilliseconds(165) },
        ];
        Avalonia.Threading.Dispatcher.UIThread.Post(() => brush.Opacity = 0, Avalonia.Threading.DispatcherPriority.Render);
        Avalonia.Threading.DispatcherTimer.RunOnce(() => host.Children.Remove(layer), TimeSpan.FromMilliseconds(250));
    }

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

        // XNAListBox: Ctrl+C copies the selected item's text
        listBox.KeyDown += async (_, e) =>
        {
            if (e.Key != Key.C || !e.KeyModifiers.HasFlag(KeyModifiers.Control) || listBox.SelectedItem == null)
                return;

            string text = ItemText(listBox);
            if (string.IsNullOrEmpty(text) || TopLevel.GetTopLevel(listBox)?.Clipboard is not { } clipboard)
                return;

            e.Handled = true;
            try
            {
                await clipboard.SetTextAsync(text);
            }
            catch (Exception ex)
            {
                Rampastring.Tools.Logger.Log("Unable to copy a list item: " + ex.Message);
            }
        };
        return listBox;
    }

    /// <summary>The selected item's text as the list shows it: the text blocks of its row, in order.</summary>
    private static string ItemText(ListBox listBox)
    {
        if (listBox.SelectedItem is AvClientView.ViewModels.ChatLineViewModel line)
            return line.Text;

        if (listBox.ContainerFromItem(listBox.SelectedItem) is not Control container)
            return listBox.SelectedItem.ToString();

        return string.Join(" ", container.GetVisualDescendants().OfType<TextBlock>()
            .Where(block => block.IsEffectivelyVisible && !string.IsNullOrWhiteSpace(block.Text))
            .Select(block => block.Text));
    }

    public static TextBox TextBox(double width, double height, string watermark, int fontIndex = 0)
    {
        var textBox = Apply(new TextBox { Width = width, Height = height, Watermark = watermark, VerticalContentAlignment = VerticalAlignment.Center }, fontIndex);
        textBox.Resources["TextControlBackgroundPointerOver"] = Brushes.Black;
        textBox.Resources["TextControlBackgroundFocused"] = Brushes.Black;
        textBox.Resources["TextControlForegroundFocused"] = new SolidColorBrush(ThemeAssets.ButtonTextColor);
        textBox.Resources["TextControlForegroundPointerOver"] = new SolidColorBrush(ThemeAssets.ButtonTextColor);

        // XNA shows its one cursor over text boxes too (no text cursor)
        if (ThemeAssets.ClientCursor != null)
            textBox.Cursor = ThemeAssets.ClientCursor;
        return textBox;
    }
}
