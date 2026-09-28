using System;

using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Styling;

using ClientCore;

namespace AvClientView.Theme;

/// <summary>
/// The XNA text shadow on all text, and tooltips as ClientGUI's ToolTip draws them: the UI background colour (AltUIBackgroundColor) with a border and text
/// in the alternative colour, ToolTipFontIndex, ToolTipMargin around the text, no wrapping, shown after ToolTipDelay.
/// </summary>
public static class ThemedToolTips
{
    public static void Apply(Application application)
    {
        ClientConfiguration config = ClientConfiguration.Instance;
        (FontFamily family, double size) = ThemeFonts.Get(config.ToolTipFontIndex);
        var altBrush = new SolidColorBrush(ThemeAssets.ButtonTextColor);
        var background = new SolidColorBrush(ThemeAssets.ParseColor(config.AltUIBackgroundColor, Color.FromRgb(196, 196, 196)));

        application.Styles.Add(new Style(selector => selector.OfType<ToolTip>())
        {
            Setters =
            {
                new Setter(TemplatedControl.BackgroundProperty, background),
                new Setter(TemplatedControl.BorderBrushProperty, altBrush),
                new Setter(TemplatedControl.BorderThicknessProperty, new Thickness(1)),
                new Setter(TemplatedControl.CornerRadiusProperty, new CornerRadius(0)),
                new Setter(TemplatedControl.PaddingProperty, new Thickness(config.ToolTipMargin)),
                new Setter(TemplatedControl.ForegroundProperty, altBrush),
                new Setter(TemplatedControl.FontFamilyProperty, family),
                new Setter(TemplatedControl.FontSizeProperty, size),
                new Setter(Layoutable.MaxWidthProperty, double.PositiveInfinity),
            },
        });

        // XNA draws nearly all text with a black copy one pixel down and right (DrawStringWithShadow,
        // TextShadowDistance 1), tooltips excepted
        var shadow = new DropShadowEffect { OffsetX = 1, OffsetY = 1, BlurRadius = 0, Color = Colors.Black, Opacity = 1 };
        application.Styles.Add(new Style(selector => selector.OfType<TextBlock>())
        {
            Setters = { new Setter(Visual.EffectProperty, shadow) },
        });
        application.Styles.Add(new Style(selector => selector.OfType<Avalonia.Controls.Presenters.TextPresenter>())
        {
            Setters = { new Setter(Visual.EffectProperty, shadow) },
        });
        application.Styles.Add(new Style(selector => selector.OfType<ToolTip>().Descendant().OfType<TextBlock>())
        {
            Setters = { new Setter(Visual.EffectProperty, null) },
        });

        // The tooltips appear after the delay, above and right of the cursor (ToolTip.DisplayAtLocation: the cursor
        // plus ToolTipOffsetX/Y, then moved up by its height). Pointer placement puts the tooltip's top left at the
        // cursor, so it is moved up by the height of its text, margins and border; set on each control that gets one.
        int showDelay = (int)Math.Round(config.ToolTipDelay * 1000);
        ToolTip.TipProperty.Changed.AddClassHandler<Control>((control, e) =>
        {
            int textHeight = ThemeFonts.Measure(e.NewValue?.ToString(), config.ToolTipFontIndex).Height;
            ToolTip.SetShowDelay(control, showDelay);
            ToolTip.SetPlacement(control, PlacementMode.Pointer);
            ToolTip.SetHorizontalOffset(control, config.ToolTipOffsetX);
            ToolTip.SetVerticalOffset(control, config.ToolTipOffsetY - (textHeight + (config.ToolTipMargin * 2) + 2));
        });
    }
}
