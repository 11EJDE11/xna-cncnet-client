using System;

using Avalonia;
using Avalonia.Controls;
using Avalonia.Data;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Media.Imaging;

namespace AvClientView.Theme;

/// <summary>
/// An XNA trackbar: the background (black at alpha 128 in the option panels) with the theme's trackbarButton.png at
/// the value's position; clicking or dragging picks the value under the cursor as XNATrackbar.Scroll does.
/// </summary>
public sealed class ThemedTrackbar : Canvas
{
    public static readonly StyledProperty<int> ValueProperty =
        AvaloniaProperty.Register<ThemedTrackbar, int>(nameof(Value), defaultBindingMode: BindingMode.TwoWay);

    private static readonly Bitmap ButtonTexture = ThemeAssets.LoadBitmap("trackbarButton.png");

    private readonly Image button;
    private bool isHeldDown;

    public ThemedTrackbar(double width, double height, int minValue, int maxValue)
    {
        Width = width;
        Height = height;
        MinValue = minValue;
        MaxValue = maxValue;
        Background = new SolidColorBrush(Color.FromArgb(128, 0, 0, 0));

        button = new Image { Source = ButtonTexture, Stretch = Stretch.Fill, Width = ButtonTexture?.PixelSize.Width ?? 12, Height = height };
        Children.Add(button);
        UpdateButton();
    }

    public int MinValue { get; }

    public int MaxValue { get; }

    public int Value
    {
        get => GetValue(ValueProperty);
        set => SetValue(ValueProperty, Math.Clamp(value, MinValue, MaxValue));
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property == ValueProperty)
            UpdateButton();
    }

    private void UpdateButton()
    {
        if (button == null)
            return;

        int tabCount = MaxValue - MinValue;
        double pixelsPerTab = tabCount > 0 ? (Width - button.Width) / tabCount : 0;
        SetLeft(button, (int)((Value - MinValue) * pixelsPerTab));
    }

    protected override void OnPointerPressed(PointerPressedEventArgs e)
    {
        base.OnPointerPressed(e);
        if (!IsEffectivelyEnabled || !e.GetCurrentPoint(this).Properties.IsLeftButtonPressed)
            return;

        isHeldDown = true;
        e.Pointer.Capture(this);
        Scroll(e.GetPosition(this).X);
        e.Handled = true;
    }

    protected override void OnPointerMoved(PointerEventArgs e)
    {
        base.OnPointerMoved(e);
        if (isHeldDown)
            Scroll(e.GetPosition(this).X);
    }

    protected override void OnPointerReleased(PointerReleasedEventArgs e)
    {
        base.OnPointerReleased(e);
        isHeldDown = false;
        e.Pointer.Capture(null);
    }

    /// <summary>XNATrackbar.Scroll: the value whose slot the cursor is in.</summary>
    private void Scroll(double xOffset)
    {
        int tabCount = MaxValue - MinValue + 1;
        double pixelsPerTab = Width / tabCount;
        int currentTab = 0;

        for (int i = 0; i <= tabCount; i++)
        {
            if (i * pixelsPerTab < xOffset)
            {
                currentTab = i;
            }
            else
            {
                Value = currentTab + MinValue;
                return;
            }
        }

        Value = MaxValue;
    }
}
