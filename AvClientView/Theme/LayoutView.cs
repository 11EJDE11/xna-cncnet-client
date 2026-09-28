using System;
using System.Collections.Generic;
using System.Linq;

using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.LogicalTree;
using Avalonia.Media;
using Avalonia.Media.Imaging;

using ClientLogic.Layout;

namespace AvClientView.Theme;

/// <summary>
/// Draws an XNA layout (see <see cref="XnaLayoutReader"/>) with Avalonia controls at the same positions: panels
/// with their backgrounds and borders, textured buttons with idle and hover images and a centred caption, and
/// labels placed by their anchor as the XNA label does.
/// </summary>
public static class LayoutView
{
    /// <summary>Builds the controls of a window.</summary>
    /// <param name="window">The window layout.</param>
    /// <param name="onClick">Called with a button's name when it's clicked.</param>
    /// <param name="skip">Controls not to draw (the screen draws them itself, or doesn't have them yet).</param>
    public static Canvas Build(LayoutControl window, Action<string> onClick, Func<LayoutControl, bool> skip = null)
    {
        var canvas = new Canvas { Width = window.Width, Height = window.Height, ClipToBounds = false };
        AddPanelVisuals(canvas, window);
        AddChildren(canvas, window, onClick, skip ?? (_ => false));
        return canvas;
    }

    /// <summary>A named control of a built layout (the screen fills in text such as the version).</summary>
    public static T FindNamed<T>(Canvas canvas, string name) where T : Control =>
        canvas.GetLogicalDescendants().OfType<T>().FirstOrDefault(c => c.Name == name);

    private static void AddChildren(Canvas canvas, LayoutControl parent, Action<string> onClick, Func<LayoutControl, bool> skip)
    {
        int index = 0;
        foreach (LayoutControl child in parent.Children.OrderBy(c => c.DrawOrder))
        {
            index++;
            if (!child.Visible || skip(child))
                continue;

            Control control = child.Kind switch
            {
                LayoutControlKind.Button => BuildButton(child, onClick),
                LayoutControlKind.Label => BuildLabel(child),
                LayoutControlKind.Panel => BuildPanel(child, onClick, skip),
                _ => null,
            };

            if (control == null)
                continue;

            control.ZIndex = index;
            if (!string.IsNullOrEmpty(child.ToolTip))
                ToolTip.SetTip(control, child.ToolTip);

            if (control.Tag is not (double, double))
            {
                Canvas.SetLeft(control, child.X);
                Canvas.SetTop(control, child.Y);
            }

            canvas.Children.Add(control);
        }
    }

    private static Control BuildPanel(LayoutControl panel, Action<string> onClick, Func<LayoutControl, bool> skip)
    {
        var canvas = new Canvas { Name = panel.Name, Width = panel.Width, Height = panel.Height };
        AddPanelVisuals(canvas, panel);
        AddChildren(canvas, panel, onClick, skip);
        return canvas;
    }

    private static void AddPanelVisuals(Canvas canvas, LayoutControl panel)
    {
        if (panel.SolidBackground is { } solid)
        {
            canvas.Background = new SolidColorBrush(ThemeAssets.ToColor(solid));
        }
        else if (ThemeAssets.LoadBitmap(panel.BackgroundTexture) is Bitmap bitmap)
        {
            if (panel.Width == 0 && panel.Height == 0)
            {
                canvas.Width = bitmap.PixelSize.Width;
                canvas.Height = bitmap.PixelSize.Height;
            }

            canvas.Background = panel.DrawMode switch
            {
                "Tiled" => new ImageBrush(bitmap)
                {
                    TileMode = TileMode.Tile,
                    Stretch = Stretch.None,
                    DestinationRect = new RelativeRect(0, 0, bitmap.PixelSize.Width, bitmap.PixelSize.Height, RelativeUnit.Absolute),
                },
                "Centered" => new ImageBrush(bitmap) { Stretch = Stretch.None },
                _ => new ImageBrush(bitmap) { Stretch = Stretch.Fill },
            };
        }

        if (panel.DrawBorders && panel.Width > 0 && panel.Height > 0)
        {
            canvas.Children.Add(new Border
            {
                Width = panel.Width,
                Height = panel.Height,
                BorderThickness = new Thickness(1),
                BorderBrush = new SolidColorBrush(panel.BorderColor is { } border ? ThemeAssets.ToColor(border) : ThemeAssets.PanelBorderColor),
                IsHitTestVisible = false,
                ZIndex = int.MaxValue,
            });
        }
    }

    private static Control BuildButton(LayoutControl button, Action<string> onClick)
    {
        var themed = new ThemedButton(button) { Name = button.Name };
        themed.Click += (_, _) => onClick(button.Name);
        return themed;
    }

    private static Control BuildLabel(LayoutControl label)
    {
        (FontFamily family, double size) = ThemeFonts.Get(label.FontIndex);
        var text = new TextBlock
        {
            Name = label.Name,
            Text = label.Text,
            FontFamily = family,
            FontSize = size,
            Foreground = new SolidColorBrush(label.TextColor is { } color ? ThemeAssets.ToColor(color) : ThemeAssets.LabelColor),
        };

        if (label.AnchorPoint is (float ax, float ay) && !string.IsNullOrEmpty(label.TextAnchor))
            PlaceByAnchor(text, label, ax, ay);

        return text;
    }

    /// <summary>Places a label as XNALabel does (LEFT and RIGHT put the text on that side of the point).</summary>
    private static void PlaceByAnchor(TextBlock text, LayoutControl label, float ax, float ay)
    {
        void Place()
        {
            text.Measure(Size.Infinity);
            Size size = text.DesiredSize;
            string anchor = label.TextAnchor.ToUpperInvariant();

            double x = ax, y = ay;

            if (anchor.Contains("HORIZONTAL_CENTER") || anchor == "CENTER")
                x = ax - size.Width / 2;
            else if (anchor.Contains("LEFT"))
                x = ax - size.Width;

            if (anchor.Contains("VERTICAL_CENTER") || anchor == "CENTER")
                y = ay - size.Height / 2;
            else if (anchor.Contains("TOP"))
                y = ay - size.Height;

            Canvas.SetLeft(text, x);
            Canvas.SetTop(text, y);
        }

        text.Tag = ((double)ax, (double)ay);
        Place();
        text.PropertyChanged += (_, e) =>
        {
            if (e.Property == TextBlock.TextProperty)
                Place();
        };
    }
}

/// <summary>An XNA-style button: the idle texture, the hover texture while the pointer is over it, and centred text.</summary>
public sealed class ThemedButton : Border
{
    private readonly Image image;
    private readonly Image hoverImage;
    private readonly TextBlock caption;
    private Bitmap idle;
    private Bitmap hover;
    private readonly IBrush idleBrush;
    private readonly IBrush hoverBrush;
    private readonly ThemeSound hoverSound;
    private readonly ThemeSound clickSound;
    private readonly int fontIndex;
    private Image star;
    private bool pressed;

    public ThemedButton(LayoutControl button)
    {
        idle = ThemeAssets.LoadBitmap(button.IdleTexture);
        hover = ThemeAssets.LoadBitmap(button.HoverTexture) ?? idle;
        // XNAClientButton's default hover sound; HoverSoundEffect / ClickSoundEffect keys replace them
        hoverSound = button.Attributes.TryGetValue("HoverSoundEffect", out string hoverSoundName) ? ThemeSound.Load(hoverSoundName)
            : button.TypeName is "XNAClientButton" or "GameLaunchButton" ? ThemeSounds.ButtonHover : null;
        clickSound = button.Attributes.TryGetValue("ClickSoundEffect", out string clickSoundName) ? ThemeSound.Load(clickSoundName) : null;

        idleBrush = new SolidColorBrush(button.TextColor is { } idleColor ? ThemeAssets.ToColor(idleColor) : ThemeAssets.ButtonTextColor);
        hoverBrush = new SolidColorBrush(button.TextColorHover is { } hoverColor ? ThemeAssets.ToColor(hoverColor) : ThemeAssets.ButtonHoverColor);

        fontIndex = button.FontIndex;
        (FontFamily family, double size) = ThemeFonts.Get(button.FontIndex);

        Width = button.Width > 0 ? button.Width : idle?.PixelSize.Width ?? double.NaN;
        Height = button.Height > 0 ? button.Height : idle?.PixelSize.Height ?? double.NaN;
        Background = Brushes.Transparent;
        Cursor = ThemeAssets.HandCursor;
        Focusable = true;

        // XNAButton cross-fades its idle and hover textures at AlphaRate per 10 ms (the theme's AlphaRate, or the
        // button's own)
        float alphaRate = button.Attributes.TryGetValue("AlphaRate", out string rateValue)
            ? Rampastring.Tools.Conversions.FloatFromString(rateValue, 0.01f)
            : ClientCore.ClientConfiguration.Instance.DefaultAlphaRate;
        TimeSpan fadeTime = TimeSpan.FromMilliseconds(alphaRate > 0 ? Math.Min(10 / alphaRate, 5000) : 0);
        Avalonia.Animation.Transitions Fade() =>
        [
            new Avalonia.Animation.DoubleTransition { Property = OpacityProperty, Duration = fadeTime },
        ];

        image = new Image { Source = idle, Stretch = Stretch.Fill, Transitions = Fade() };
        hoverImage = new Image { Source = hover, Stretch = Stretch.Fill, Opacity = 0, Transitions = Fade() };
        caption = new TextBlock
        {
            Text = button.Text,
            FontFamily = family,
            FontSize = size,
            Foreground = idleBrush,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
            TextAlignment = TextAlignment.Center,
        };

        Child = new Grid { Children = { image, hoverImage, caption } };
    }

    public event EventHandler Click;

    /// <summary>Changes the button's textures (e.g. the extra options button while options are set).</summary>
    public void SetTextures(string idleTexture, string hoverTexture)
    {
        idle = ThemeAssets.LoadBitmap(idleTexture) ?? idle;
        hover = ThemeAssets.LoadBitmap(hoverTexture) ?? idle;
        image.Source = idle;
        hoverImage.Source = hover;
    }

    public string Text
    {
        get => caption.Text;
        set
        {
            caption.Text = value;
            UpdateStarPosition();
        }
    }

    /// <summary>
    /// The GameLaunchButton's star display: a rank texture right of the centred text (null hides it).
    /// </summary>
    public void SetStar(Bitmap texture)
    {
        if (star == null)
        {
            star = new Image { Stretch = Stretch.None, HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Top, IsHitTestVisible = false };
            ((Grid)Child).Children.Add(star);
        }

        star.Source = texture;
        star.IsVisible = texture != null;
        UpdateStarPosition();
    }

    private void UpdateStarPosition()
    {
        if (star?.Source is not Bitmap texture)
            return;

        // StarDisplay: X = Width / 2 + text width / 2 + 3, centred vertically (integer maths, as XNA)
        int width = (int)Width;
        int height = (int)Height;
        int x = width / 2 + ThemeFonts.Measure(Text ?? string.Empty, fontIndex).Width / 2 + 3;
        int y = (height - texture.PixelSize.Height) / 2;
        star.Margin = new Thickness(x, y, 0, 0);
    }

    protected override void OnPointerEntered(PointerEventArgs e)
    {
        base.OnPointerEntered(e);
        if (!IsEffectivelyEnabled)
            return;

        hoverSound?.Play();
        image.Opacity = 0;
        hoverImage.Opacity = 1;
        caption.Foreground = hoverBrush;
    }

    protected override void OnPointerExited(PointerEventArgs e)
    {
        base.OnPointerExited(e);
        image.Opacity = 1;
        hoverImage.Opacity = 0;
        caption.Foreground = idleBrush;
        pressed = false;
    }

    protected override void OnPointerPressed(PointerPressedEventArgs e)
    {
        base.OnPointerPressed(e);
        if (e.GetCurrentPoint(this).Properties.IsLeftButtonPressed)
        {
            pressed = true;
            e.Handled = true;
        }
    }

    protected override void OnPointerReleased(PointerReleasedEventArgs e)
    {
        base.OnPointerReleased(e);
        if (e.InitialPressMouseButton != MouseButton.Left)
            return;
        if (pressed && IsEffectivelyEnabled)
        {
            pressed = false;
            e.Handled = true;
            clickSound?.Play();
            Click?.Invoke(this, EventArgs.Empty);
        }
    }
}
