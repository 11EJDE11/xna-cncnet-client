using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Presenters;
using Avalonia.Controls.Primitives;
using Avalonia.Controls.Templates;
using Avalonia.Data;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Styling;

namespace AvClientView.Theme;

/// <summary>
/// Vertical scroll bars as XNAScrollBar draws them: the sbUpArrow / sbDownArrow buttons (with their Hovered
/// textures), the sbBackground track and a thumb of sbThumbTop, sbMiddle and sbThumbBottom, as wide as the arrow,
/// always shown (never auto-hidden) and only when the content can scroll.
/// </summary>
public static class ThemedScrollBars
{
    public static void Apply(Application application) => Apply(application,
        ThemeAssets.LoadBitmap("sbUpArrow.png"), ThemeAssets.LoadBitmap("sbUpArrowHovered.png"),
        ThemeAssets.LoadBitmap("sbDownArrow.png"), ThemeAssets.LoadBitmap("sbDownArrowHovered.png"),
        ThemeAssets.LoadBitmap("sbBackground.png"), ThemeAssets.LoadBitmap("sbThumbTop.png"),
        ThemeAssets.LoadBitmap("sbMiddle.png"), ThemeAssets.LoadBitmap("sbThumbBottom.png"));

    /// <summary>Applies the textures; a theme without the arrows keeps the standard scroll bars.</summary>
    public static void Apply(Application application, Bitmap up, Bitmap upHovered, Bitmap down, Bitmap downHovered,
        Bitmap background, Bitmap thumbTop, Bitmap thumbMiddle, Bitmap thumbBottom)
    {
        if (up == null || down == null || thumbMiddle == null)
            return;

        double width = up.PixelSize.Width;

        var thumbTemplate = new FuncControlTemplate<Thumb>((_, _) =>
        {
            var grid = new Grid { RowDefinitions = new RowDefinitions("Auto,*,Auto") };
            grid.Children.Add(Stretched(thumbTop, 0));
            grid.Children.Add(Stretched(thumbMiddle, 1));
            grid.Children.Add(Stretched(thumbBottom, 2));
            return grid;
        });

        var template = new FuncControlTemplate<ScrollBar>((_, scope) =>
        {
            RepeatButton Arrow(string name, Bitmap idle, Bitmap hovered)
            {
                var image = new Image { Source = idle, Stretch = Stretch.None };
                var button = new RepeatButton
                {
                    Name = name,
                    Content = image,
                    Padding = new Thickness(0),
                    BorderThickness = new Thickness(0),
                    MinWidth = 0,
                    MinHeight = 0,
                    Template = new FuncControlTemplate<RepeatButton>((b, _) =>
                        new ContentPresenter { [!ContentPresenter.ContentProperty] = b[!ContentControl.ContentProperty] }),
                };

                if (hovered != null)
                {
                    button.PointerEntered += (_, _) => image.Source = hovered;
                    button.PointerExited += (_, _) => image.Source = idle;
                }

                return button.RegisterInNameScope(scope);
            }

            RepeatButton Page(string name) => new RepeatButton
            {
                Name = name,
                Focusable = false,
                Background = Brushes.Transparent,
                Template = new FuncControlTemplate<RepeatButton>((b, _) => new Border { [!Border.BackgroundProperty] = b[!TemplatedControl.BackgroundProperty] }),
            }.RegisterInNameScope(scope);

            var track = new Track
            {
                Name = "PART_Track",
                Orientation = Orientation.Vertical,
                IsDirectionReversed = true,
                DecreaseButton = Page("PART_PageUpButton"),
                IncreaseButton = Page("PART_PageDownButton"),
                Thumb = new Thumb { Template = thumbTemplate },
            }.RegisterInNameScope(scope);
            track.Bind(Track.MinimumProperty, new TemplateBinding(RangeBase.MinimumProperty));
            track.Bind(Track.MaximumProperty, new TemplateBinding(RangeBase.MaximumProperty));
            track.Bind(Track.ValueProperty, new TemplateBinding(RangeBase.ValueProperty) { Mode = BindingMode.TwoWay });
            track.Bind(Track.ViewportSizeProperty, new TemplateBinding(ScrollBar.ViewportSizeProperty));
            Grid.SetRow(track, 1);

            var root = new Grid { RowDefinitions = new RowDefinitions("Auto,*,Auto"), Width = width };
            if (background != null)
            {
                var trackBackground = Stretched(background, 1);
                root.Children.Add(trackBackground);
            }

            RepeatButton upButton = Arrow("PART_LineUpButton", up, upHovered);
            RepeatButton downButton = Arrow("PART_LineDownButton", down, downHovered);
            Grid.SetRow(downButton, 2);
            root.Children.Add(upButton);
            root.Children.Add(track);
            root.Children.Add(downButton);
            return root;
        });

        application.Styles.Add(new Style(selector => selector.OfType<ScrollBar>().PropertyEquals(ScrollBar.OrientationProperty, Orientation.Vertical))
        {
            Setters =
            {
                new Setter(TemplatedControl.TemplateProperty, template),
                new Setter(Layoutable.WidthProperty, width),
                new Setter(Layoutable.MinWidthProperty, width),
                new Setter(ScrollBar.AllowAutoHideProperty, false),
            },
        });
        application.Styles.Add(new Style(selector => selector.OfType<ScrollViewer>())
        {
            Setters = { new Setter(ScrollViewer.AllowAutoHideProperty, false) },
        });

        // The content beside the bar, not under it (XNA lists narrow their text while the bar is shown)
        application.Styles.Add(new Style(selector => selector.OfType<ScrollViewer>().Template().OfType<ScrollContentPresenter>())
        {
            Setters = { new Setter(Grid.ColumnSpanProperty, 1) },
        });
    }

    private static Image Stretched(Bitmap bitmap, int row)
    {
        var image = new Image { Source = bitmap, Stretch = Stretch.Fill };
        Grid.SetRow(image, row);
        return image;
    }
}
