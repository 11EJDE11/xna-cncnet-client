using System;
using System.Collections.Generic;
using System.Collections.Specialized;
using System.ComponentModel;
using System.Linq;

using Avalonia;
using Avalonia.Controls;
using Avalonia.Data;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Threading;

using AvClientView.ViewModels;

using ClientCore;
using ClientCore.Extensions;

using ClientLogic.Layout;

namespace AvClientView.Theme;

/// <summary>
/// The XNA MapPreviewBox: the map preview, its extra textures, the start location indicators (the theme's
/// slocindicator textures, turning, tinted with the player's colour, with the players' names), the start menus,
/// the favourite and extra icons buttons, the map menu, and the co-op briefing.
/// </summary>
public sealed class MapPreviewView : Canvas
{
    private const double TEXTURE_SCALE = 0.25;
    private const int MAX_STARTING_LOCATIONS = 8;

    private readonly LobbyViewModelBase viewModel;
    private readonly Canvas extraTextureLayer = new();
    private readonly Canvas indicatorLayer = new();
    private readonly List<Indicator> indicators = [];
    private readonly ThemedButton favoriteButton;
    private readonly ThemedButton extraTexturesButton;
    private readonly Border briefingBox;
    private readonly TextBlock briefingText;
    private readonly DispatcherTimer timer;
    private readonly ThemeSound clickSound = ThemeSound.Load("button.wav");
    private readonly ThemeSound dropDownSound = ThemeSound.Load("dropdown.wav");
    private readonly Color nameBackgroundColor = ThemeAssets.ParseColor(ClientConfiguration.Instance.MapPreviewNameBackgroundColor, Color.FromArgb(144, 0, 0, 0));
    private readonly Color nameBorderColor = ThemeAssets.ParseColor(ClientConfiguration.Instance.MapPreviewNameBorderColor, Color.FromArgb(128, 128, 128, 128));
    private readonly Color hoverRemapColor = ThemeAssets.ParseColor(ClientConfiguration.Instance.MapPreviewStartingLocationHoverRemapColor, Color.FromArgb(128, 255, 255, 255));
    private readonly double angularVelocity;
    private readonly double reservedAngularVelocity;
    private DateTime lastTick = DateTime.Now;

    public MapPreviewView(LayoutControl layout, LobbyViewModelBase viewModel, Rampastring.Tools.IniFile gameOptionsIni)
    {
        this.viewModel = viewModel;
        Width = layout.Width;
        Height = layout.Height;
        ClipToBounds = true;
        Background = layout.SolidBackground is { } solid ? new SolidColorBrush(ThemeAssets.ToColor(solid)) : ThemedStyle.PanelBackground;
        DataContext = viewModel;

        angularVelocity = gameOptionsIni?.GetDoubleValue("General", "StartingLocationAngularVelocity", 0.015) ?? 0.015;
        reservedAngularVelocity = gameOptionsIni?.GetDoubleValue("General", "ReservedStartingLocationAngularVelocity", -0.0075) ?? -0.0075;

        viewModel.SetPreviewArea(layout.Width, layout.Height);

        var image = new Image { Stretch = Stretch.Fill };
        image.Bind(Image.SourceProperty, new Binding(nameof(LobbyViewModelBase.Preview)));
        image.Bind(LeftProperty, new Binding(nameof(LobbyViewModelBase.PreviewX)));
        image.Bind(TopProperty, new Binding(nameof(LobbyViewModelBase.PreviewY)));
        image.Bind(WidthProperty, new Binding(nameof(LobbyViewModelBase.PreviewImageWidth)));
        image.Bind(HeightProperty, new Binding(nameof(LobbyViewModelBase.PreviewImageHeight)));
        Children.Add(image);

        void UpdateInterpolation() => RenderOptions.SetBitmapInterpolationMode(image,
            viewModel.UseNearestNeighbour ? BitmapInterpolationMode.None : BitmapInterpolationMode.HighQuality);

        if (layout.DrawBorders)
        {
            Children.Add(new Border
            {
                Width = layout.Width,
                Height = layout.Height,
                BorderBrush = new SolidColorBrush(ThemeAssets.PanelBorderColor),
                BorderThickness = new Thickness(1),
                IsHitTestVisible = false,
            });
        }

        Children.Add(extraTextureLayer);
        Children.Add(indicatorLayer);

        for (int i = 0; i < MAX_STARTING_LOCATIONS; i++)
            indicators.Add(new Indicator(this, i + 1));

        favoriteButton = SmallButton("favInactive.png", "Toggle Favorite Map".L10N("Client:Main:ToggleFavoriteMap"), () => viewModel.ToggleFavoriteMap());
        extraTexturesButton = SmallButton("pvTexturesActive.png", "Toggle Extra Icons".L10N("Client:Main:ToggleExtraIcons"), () =>
        {
            viewModel.ToggleExtraTextures();
            RefreshExtraTextures();
        });

        // CoopBriefingBox: 400 wide, black at alpha 224 with a border, the text in the alt colour, centred
        (FontFamily family, double size) = ThemeFonts.Get(0);
        briefingText = new TextBlock
        {
            FontFamily = family,
            FontSize = size,
            Foreground = new SolidColorBrush(ThemeAssets.ButtonTextColor),
            TextWrapping = TextWrapping.Wrap,
            Width = 400 - 24,
        };
        briefingBox = new Border
        {
            Width = 400,
            Background = new SolidColorBrush(Color.FromArgb(224, 0, 0, 0)),
            BorderBrush = new SolidColorBrush(ThemeAssets.PanelBorderColor),
            BorderThickness = new Thickness(1),
            Padding = new Thickness(12),
            Child = briefingText,
            IsHitTestVisible = false,
            Opacity = 0,
            Transitions = [new Avalonia.Animation.DoubleTransition { Property = OpacityProperty, Duration = TimeSpan.FromSeconds(0.25) }],
        };
        Children.Add(briefingBox);

        void Refreshed(object sender, EventArgs e)
        {
            RefreshIndicators();
            RefreshExtraTextures();
        }

        UpdateInterpolation();
        void InterpolationChanged(object sender, PropertyChangedEventArgs e)
        {
            if (e.PropertyName == nameof(LobbyViewModelBase.UseNearestNeighbour))
                UpdateInterpolation();
        }

        RefreshIndicators();
        RefreshExtraTextures();
        RefreshButtons();
        RefreshBriefing();

        timer = new DispatcherTimer(TimeSpan.FromMilliseconds(16), DispatcherPriority.Render, (_, _) => Tick());
        AttachedToVisualTree += (_, _) =>
        {
            viewModel.PropertyChanged += ViewModel_PropertyChanged;
            viewModel.PropertyChanged += InterpolationChanged;
            viewModel.Refreshed += Refreshed;
            Refreshed(null, EventArgs.Empty);
            RefreshButtons();
            RefreshBriefing();
            UpdateInterpolation();
            lastTick = DateTime.Now;
            timer.Start();
        };
        DetachedFromVisualTree += (_, _) =>
        {
            timer.Stop();
            viewModel.PropertyChanged -= ViewModel_PropertyChanged;
            viewModel.PropertyChanged -= InterpolationChanged;
            viewModel.Refreshed -= Refreshed;
        };
    }

    private ThemedButton SmallButton(string texture, string toolTip, Action onClick)
    {
        var layout = new LayoutControl("btn" + texture, "XNAClientButton", LayoutControlKind.Button)
        {
            Width = 18,
            Height = 18,
            IdleTexture = texture,
            HoverTexture = texture,
        };

        var button = new ThemedButton(layout) { IsVisible = false, ZIndex = 100 };
        ToolTip.SetTip(button, toolTip);
        button.Click += (_, _) => onClick();
        Children.Add(button);
        return button;
    }

    private void ViewModel_PropertyChanged(object sender, PropertyChangedEventArgs e)
    {
        switch (e.PropertyName)
        {
            case nameof(LobbyViewModelBase.IsFavoriteMap):
            case nameof(LobbyViewModelBase.HasToggleableExtraTextures):
            case nameof(LobbyViewModelBase.ShowToggleableExtraTextures):
            case nameof(LobbyViewModelBase.Preview):
                RefreshButtons();
                break;
            case nameof(LobbyViewModelBase.Briefing):
                RefreshBriefing();
                break;
        }
    }

    /// <summary>The favourite button at the top right, left of the extra icons button when the map has toggleable icons.</summary>
    private void RefreshButtons()
    {
        bool hasMap = viewModel.Preview != null;
        double buttonX = Width;

        bool showExtraButton = hasMap && viewModel.HasToggleableExtraTextures;
        extraTexturesButton.IsVisible = showExtraButton;
        if (showExtraButton)
        {
            string texture = viewModel.ShowToggleableExtraTextures ? "pvTexturesActive" : "pvTexturesInactive";
            extraTexturesButton.SetTextures(texture + ".png", ThemeAssets.FindFile(texture + "_c.png") != null ? texture + "_c.png" : texture + ".png");
            SetLeft(extraTexturesButton, buttonX - 22);
            SetTop(extraTexturesButton, 4);
            buttonX -= 22;
        }

        favoriteButton.IsVisible = hasMap;
        string fav = viewModel.IsFavoriteMap ? "favActive" : "favInactive";
        favoriteButton.SetTextures(fav + ".png", ThemeAssets.FindFile(fav + "_c.png") != null ? fav + "_c.png" : fav + ".png");
        SetLeft(favoriteButton, buttonX - 22);
        SetTop(favoriteButton, 4);
    }

    private void RefreshExtraTextures()
    {
        extraTextureLayer.Children.Clear();
        foreach (ExtraTextureViewModel extra in viewModel.ExtraTextures)
        {
            if (extra.Toggleable && !viewModel.ShowToggleableExtraTextures)
                continue;

            if (ThemeAssets.LoadBitmap(extra.TextureName) is not Bitmap bitmap)
                continue;

            var image = new Image { Source = bitmap, Stretch = Stretch.None, IsHitTestVisible = false };
            SetLeft(image, extra.X);
            SetTop(image, extra.Y);
            extraTextureLayer.Children.Add(image);
        }

        RefreshButtons();
    }

    private void RefreshBriefing()
    {
        briefingText.Text = viewModel.Briefing;
        briefingBox.IsVisible = !string.IsNullOrEmpty(viewModel.Briefing);
        briefingBox.Measure(Size.Infinity);
        SetLeft(briefingBox, (Width - briefingBox.Width) / 2);
        SetTop(briefingBox, (Height - briefingBox.DesiredSize.Height) / 2);
        briefingBox.Opacity = IsPointerOver ? 0 : 1;
    }

    private void RefreshIndicators()
    {
        foreach (Indicator indicator in indicators)
            indicator.Hide();

        foreach (StartMarkerViewModel marker in viewModel.StartMarkers)
        {
            if (marker.Number >= 1 && marker.Number <= indicators.Count)
                indicators[marker.Number - 1].Show(marker);
        }
    }

    protected override void OnPointerEntered(PointerEventArgs e)
    {
        base.OnPointerEntered(e);
        foreach (Indicator indicator in indicators)
            indicator.BackgroundShown = true;

        briefingBox.Opacity = 0;
    }

    protected override void OnPointerExited(PointerEventArgs e)
    {
        base.OnPointerExited(e);
        foreach (Indicator indicator in indicators)
            indicator.BackgroundShown = false;

        briefingBox.Opacity = 1;
    }

    protected override void OnPointerPressed(PointerPressedEventArgs e)
    {
        base.OnPointerPressed(e);
        if (e.Handled)
            return;

        PointerPointProperties properties = e.GetCurrentPoint(this).Properties;
        if (properties.IsRightButtonPressed)
        {
            // The map menu
            if (viewModel.Preview == null)
                return;

            var items = new List<ThemedMenuItem>
            {
                new(viewModel.IsFavoriteMap ? "Remove Favorite".L10N("Client:Main:RemoveFavorite") : "Add Favorite".L10N("Client:Main:AddFavorite"),
                    viewModel.ToggleFavoriteMap),
            };

            if (viewModel.HasToggleableExtraTextures)
            {
                items.Add(new(viewModel.ShowToggleableExtraTextures ? "Hide Extra Icons".L10N("Client:Main:HideExtraIcons") : "Show Extra Icons".L10N("Client:Main:ShowExtraIcons"),
                    () =>
                    {
                        viewModel.ToggleExtraTextures();
                        RefreshExtraTextures();
                    }));
            }

            items.Add(new("Show in Folder".L10N("Client:Main:ShowInFolder"), viewModel.ShowMapInFolder));
            ThemedContextMenu.Open(this, e.GetPosition(this), items, 120);
            e.Handled = true;
        }
        else if (properties.IsLeftButtonPressed && e.KeyModifiers.HasFlag(KeyModifiers.Control))
        {
            viewModel.OpenPreviewFile();
            e.Handled = true;
        }
    }

    private void Tick()
    {
        DateTime now = DateTime.Now;
        double frameTimeCoefficient = (now - lastTick).TotalMilliseconds / 10.0;
        lastTick = now;

        foreach (Indicator indicator in indicators)
            indicator.Update(frameTimeCoefficient);
    }

    private void IndicatorLeftClick(Indicator indicator, Point position)
    {
        if (!viewModel.EnableStartLocationSelection)
            return;

        clickSound.Play();

        if (!viewModel.EnableStartMenu)
        {
            viewModel.SelectLocalStart(indicator.Start);
            return;
        }

        int start = indicator.Start;
        var items = viewModel.StartMenuItems(start)
            .Select(item => new ThemedMenuItem(item.Text, () =>
            {
                dropDownSound.Play();
                viewModel.AssignStart(item.Row, start);
            }, item.Color is (byte r, byte g, byte b) ? Color.FromRgb(r, g, b) : Colors.White, item.Selectable))
            .ToList();

        // Right of the indicator, or left of it if the menu doesn't fit
        double x = indicator.Right;
        if (x + 150 > Width)
            x = indicator.Left - 150;

        ThemedContextMenu.Open(this, new Point(x, indicator.Top), items);
    }

    /// <summary>A PlayerLocationIndicator.</summary>
    private sealed class Indicator
    {
        private readonly MapPreviewView owner;
        private readonly Canvas root = new() { IsVisible = false, Background = Brushes.Transparent, Cursor = new Cursor(StandardCursorType.Hand) };
        private readonly Image shadow = new() { Stretch = Stretch.Fill, IsHitTestVisible = false };
        private readonly Image hover = new() { Stretch = Stretch.Fill, IsHitTestVisible = false, IsVisible = false };
        private readonly Image main = new() { Stretch = Stretch.Fill, IsHitTestVisible = false };
        private readonly Image waypoint = new() { Stretch = Stretch.None, IsHitTestVisible = false };
        private readonly Canvas names = new() { IsHitTestVisible = false };
        private readonly int baseWidth;
        private readonly int baseHeight;
        private double angle;
        private double backgroundAlpha;
        private bool occupied;
        private bool isHovered;
        private string usedTexture = "slocindicator.png";

        public Indicator(MapPreviewView owner, int start)
        {
            this.owner = owner;
            Start = start;

            (baseWidth, baseHeight) = ThemeAssets.TextureSize("slocindicator.png") ?? (60, 60);
            root.Width = (int)(baseWidth * TEXTURE_SCALE);
            root.Height = (int)(baseHeight * TEXTURE_SCALE);

            waypoint.Source = ThemeAssets.LoadBitmap($"slocindicator{start}.png");

            root.Children.Add(shadow);
            root.Children.Add(hover);
            root.Children.Add(main);
            root.Children.Add(waypoint);
            owner.indicatorLayer.Children.Add(names);
            owner.indicatorLayer.Children.Add(root);

            root.PointerEntered += (_, _) => isHovered = true;
            root.PointerExited += (_, _) => isHovered = false;
            root.PointerPressed += (_, e) =>
            {
                PointerPointProperties properties = e.GetCurrentPoint(root).Properties;
                if (properties.IsLeftButtonPressed)
                    owner.IndicatorLeftClick(this, default);
                else if (properties.IsRightButtonPressed)
                    owner.viewModel.RightClickStart(Start);

                e.Handled = true;
            };
        }

        public int Start { get; }

        public bool BackgroundShown { get; set; }

        public double Left => Canvas.GetLeft(root);

        public double Top => Canvas.GetTop(root);

        public double Right => Left + root.Width;

        private IReadOnlyList<StartMarkerPlayer> players = [];

        public void Hide()
        {
            root.IsVisible = false;
            names.IsVisible = false;
        }

        public void Show(StartMarkerViewModel marker)
        {
            // SetPosition: the scaled texture centred on the marker
            Canvas.SetLeft(root, (int)marker.X - (int)root.Width / 2);
            Canvas.SetTop(root, (int)marker.Y - (int)root.Height / 2);
            root.IsVisible = true;
            names.IsVisible = true;

            players = marker.PlayersOnStart;
            occupied = players.Count > 0;
            usedTexture = occupied ? "slocindicatorh.png" : "slocindicator.png";

            Color remap = players.Count == 1 && players[0].Color is (byte r, byte g, byte b) ? Color.FromRgb(r, g, b) : Colors.White;
            Color hoverColor = players.Count == 1 && players[0].Color is (byte hr, byte hg, byte hb) ? Color.FromRgb(hr, hg, hb) : owner.hoverRemapColor;

            Place(shadow, ThemeAssets.TintedBitmap(usedTexture, Colors.Black), TEXTURE_SCALE, 1.5 - 0.5, 1);
            Place(hover, ThemeAssets.TintedBitmap(usedTexture, hoverColor), TEXTURE_SCALE + 0.1, 0, 0);
            Place(main, ThemeAssets.TintedBitmap(usedTexture, remap), TEXTURE_SCALE, 0, 0);

            if (waypoint.Source is Bitmap waypointBitmap)
            {
                Canvas.SetLeft(waypoint, root.Width / 2 + 0.5 - waypointBitmap.PixelSize.Width / 2.0);
                Canvas.SetTop(waypoint, root.Height / 2 - waypointBitmap.PixelSize.Height / 2.0);
            }

            BuildNames();
        }

        /// <summary>A texture at a scale, centred on the indicator (plus an offset), turning around its centre.</summary>
        private void Place(Image image, Bitmap bitmap, double scale, double dx, double dy)
        {
            image.Source = bitmap;
            double w = baseWidth * scale;
            double h = baseHeight * scale;
            image.Width = w;
            image.Height = h;
            Canvas.SetLeft(image, root.Width / 2 + 0.5 + dx - w / 2);
            Canvas.SetTop(image, root.Height / 2 + dy - h / 2);
            image.RenderTransformOrigin = RelativePoint.Center;
            image.RenderTransform = new RotateTransform(angle * 180 / Math.PI);
        }

        /// <summary>
        /// PlayerLocationIndicator.Refresh and Draw: each player's name (with the team tag) right of the indicator,
        /// or left of it if it doesn't fit, on a background that fades in while the cursor is on the preview.
        /// </summary>
        private void BuildNames()
        {
            names.Children.Clear();
            if (players.Count == 0)
                return;

            (FontFamily family, double size) = ThemeFonts.Get(0);
            int lineHeight = ThemeFonts.Measure("@", 0).Height + 1;
            int textWidth = players.Max(p => ThemeFonts.Measure(string.IsNullOrEmpty(p.TeamTag) ? p.Name : p.TeamTag + " " + p.Name, 0).Width);

            int textXPosition = 3;
            bool textOnRight = Right + textXPosition + textWidth <= owner.Width;
            if (!textOnRight)
                textXPosition = -textWidth - 3 - (int)(baseWidth * TEXTURE_SCALE);

            double centerX = Left + root.Width / 2;
            double y = Top + ((int)(baseHeight * TEXTURE_SCALE) - lineHeight) / 2;

            foreach (StartMarkerPlayer player in players)
            {
                string text = string.IsNullOrEmpty(player.TeamTag) ? player.Name
                    : textOnRight ? player.TeamTag + " " + player.Name : player.Name + " " + player.TeamTag;

                double rectX = textOnRight ? centerX : centerX - (textWidth + root.Width / 2 + 5);
                double rectWidth = textOnRight ? textWidth + textXPosition + root.Width / 2 + 5 : textWidth + root.Width / 2 + 5;

                var background = new Border
                {
                    Width = rectWidth,
                    Height = lineHeight,
                    Background = new SolidColorBrush(owner.nameBackgroundColor),
                    BorderBrush = new SolidColorBrush(owner.nameBorderColor),
                    BorderThickness = new Thickness(1),
                    Opacity = backgroundAlpha,
                    Tag = "background",
                };
                Canvas.SetLeft(background, rectX);
                Canvas.SetTop(background, y);
                names.Children.Add(background);

                var label = new TextBlock
                {
                    Text = text,
                    FontFamily = family,
                    FontSize = size,
                    Foreground = new SolidColorBrush(player.Color is (byte r, byte g, byte b) ? Color.FromRgb(r, g, b) : Colors.White),
                };
                Canvas.SetLeft(label, Right + textXPosition);
                Canvas.SetTop(label, y);
                names.Children.Add(label);

                y += lineHeight;
            }
        }

        public void Update(double frameTimeCoefficient)
        {
            if (!root.IsVisible)
                return;

            angle += (occupied ? owner.reservedAngularVelocity : owner.angularVelocity) * frameTimeCoefficient;
            double degrees = angle * 180 / Math.PI;
            foreach (Image image in new[] { shadow, hover, main })
                image.RenderTransform = new RotateTransform(degrees);

            hover.IsVisible = isHovered;

            double target = BackgroundShown ? 1.0 : 0.0;
            if (Math.Abs(backgroundAlpha - target) > 0.001)
            {
                backgroundAlpha = BackgroundShown ? Math.Min(backgroundAlpha + 0.1, 1.0) : Math.Max(backgroundAlpha - 0.1, 0.0);
                foreach (Control child in names.Children)
                {
                    if (child is Border { Tag: "background" } border)
                        border.Opacity = backgroundAlpha;
                }
            }
        }
    }
}
