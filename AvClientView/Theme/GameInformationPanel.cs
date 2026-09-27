using System;
using System.Collections.Generic;

using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Media.Imaging;

using ClientCore;
using ClientCore.Extensions;
using ClientCore.I18N;

using ClientLogic.GameList;

using DTAClient.Domain.Multiplayer;
using DTAClient.Domain.Multiplayer.CnCNet;

namespace AvClientView.Theme;

/// <summary>
/// The panel the XNA GameListBox shows next to the game list for the selected or hovered game, laid out as the XNA
/// GameInformationPanel: host, ping, version, skill level and players on the left; game mode, map and map preview on
/// the right; the broadcast game options (ShowInGameInformationPanel) and the locked/passworded/incompatible legend
/// below.
/// </summary>
public static class GameInformationPanel
{
    private const int MAX_PLAYERS = 8;
    private const int leftColumnPositionX = 10;
    private const int columnMargin = 10;
    private const int topStartingPositionY = 30;
    private const int rowHeight = 24;
    private const int initialPanelHeight = 260;
    private const int columnWidth = 235;
    private const int maxPreviewHeight = 150;
    private const int mapPreviewMargin = 15;
    private const int playerNameRowHeight = 20;
    private const int playerColumn2OffsetX = 115;
    private const int legendTopSpacing = 15;
    private const int legendIconHeight = 18;
    private const int legendPadding = 5;
    private const int legendIconPadding = 2;
    private const int iconLabelSpacing = 6;
    private const int gameInfoLabelTopPadding = 6;
    private const int mapPreviewHorizontalMargin = 10;
    private const int mapPreviewVerticalMargin = 20;

    public const int Width = columnWidth * 2;

    /// <summary>
    /// The panel for a game; <paramref name="findMap"/> gives the game's map name and preview, if known, and
    /// <paramref name="options"/> the game options section (see BroadcastedOptionDisplay.InformationPanel).
    /// </summary>
    public static Control Build(GenericHostedGame game, Func<GenericHostedGame, (string MapName, Bitmap Preview)> findMap,
        (List<BroadcastedOptionIcon> IconsOnly, List<BroadcastedOptionIcon> WithText) options)
    {
        const int rightColumnPositionX = Width / 2 - columnMargin;
        const int mapPreviewPositionY = topStartingPositionY + (rowHeight * 2 + mapPreviewMargin);

        var canvas = new Canvas { Width = Width, ClipToBounds = true };

        TextBlock Label(string text, int x, int y, int fontIndex = 0, Color? color = null)
        {
            (FontFamily family, double size) = ThemeFonts.Get(fontIndex);
            var label = new TextBlock
            {
                Text = text,
                FontFamily = family,
                FontSize = size,
                Foreground = new SolidColorBrush(color ?? ThemeAssets.LabelColor),
            };
            Canvas.SetLeft(label, x);
            Canvas.SetTop(label, y);
            canvas.Children.Add(label);
            return label;
        }

        TextBlock title = Label("GAME INFORMATION".L10N("Client:Main:GameInfo"), 0, gameInfoLabelTopPadding, fontIndex: 1);
        title.Width = Width;
        title.TextAlignment = TextAlignment.Center;

        (string mapName, Bitmap preview) = findMap(game);
        mapName ??= string.IsNullOrEmpty(game.Map) ? "Unknown".L10N("Client:Main:Unknown") : game.Map;

        string gameMode = string.IsNullOrEmpty(game.GameMode)
            ? "Unknown".L10N("Client:Main:Unknown")
            : game.GameMode.L10N($"INI:GameModes:{game.GameMode}:UIName", TranslationNotificationLevel.Verbose);

        TextBlock lblGameMode = Label("Game mode:".L10N("Client:Main:GameInfoGameMode") + " " + gameMode, rightColumnPositionX, topStartingPositionY);
        lblGameMode.MaxWidth = Width - rightColumnPositionX;
        lblGameMode.TextTrimming = TextTrimming.CharacterEllipsis;

        TextBlock lblMap = Label("Map:".L10N("Client:Main:GameInfoMap") + " " + mapName, rightColumnPositionX, topStartingPositionY + rowHeight);
        lblMap.MaxWidth = columnWidth;
        lblMap.TextWrapping = TextWrapping.Wrap;

        Label("Host:".L10N("Client:Main:GameInfoHost") + " " + game.HostName, leftColumnPositionX, topStartingPositionY);

        string pingText = game is HostedCnCNetGame hostedGame
            ? hostedGame.TunnelServer == null
                ? "Ping: Dynamic".L10N("Client:Main:GameInfoPingDynamic")
                : "Ping:".L10N("Client:Main:GameInfoPing") + " " + hostedGame.TunnelServer.Ping
            : "Ping:".L10N("Client:Main:GameInfoPing") + " " + game.Ping;
        Label(pingText, leftColumnPositionX, topStartingPositionY + rowHeight);

        Label("Game version:".L10N("Client:Main:GameInfoGameVersion") + " " + game.GameVersion, leftColumnPositionX, topStartingPositionY + (rowHeight * 2));

        string[] skillLevels = ClientConfiguration.Instance.GetSkillLevelOptions();
        int skillLevelIndex = Math.Clamp(game.SkillLevel, 0, skillLevels.Length - 1);
        Label("Preferred Skill Level:".L10N("Client:Main:GameInfoSkillLevel") + " " +
            skillLevels[skillLevelIndex].L10N($"INI:ClientDefinitions:SkillLevel:{skillLevelIndex}"),
            leftColumnPositionX, topStartingPositionY + (rowHeight * 3));

        int playersY = topStartingPositionY + (rowHeight * 4);
        Label("Players".L10N("Client:Main:GameInfoPlayers") + " (" + game.Players.Length + " / " + game.MaxPlayers + "):", leftColumnPositionX, playersY);

        for (int i = 0; i < game.Players.Length && i < MAX_PLAYERS; i++)
        {
            int column = i / (MAX_PLAYERS / 2);
            int row = i % (MAX_PLAYERS / 2);
            Label(game.Players[i], leftColumnPositionX + column * playerColumn2OffsetX, playersY + rowHeight + row * playerNameRowHeight,
                color: ThemeAssets.ButtonTextColor);
        }

        int height = initialPanelHeight;
        int sectionY = playersY + rowHeight + (MAX_PLAYERS / 2 * playerNameRowHeight) + legendTopSpacing;

        void Divider(int y)
        {
            var divider = new Border { Width = Width, Height = 1, Background = new SolidColorBrush(ThemeAssets.PanelBorderColor) };
            Canvas.SetTop(divider, y);
            canvas.Children.Add(divider);
        }

        // A GameInformationIconPanel: the icon, then the label after the widest icon
        void IconRow(Bitmap icon, string text, int x, int y, double iconColumnWidth)
        {
            var row = new StackPanel { Orientation = Orientation.Horizontal, Spacing = iconLabelSpacing, Height = legendIconHeight };
            row.Children.Add(new Image
            {
                Source = icon,
                Stretch = Stretch.None,
                Width = Math.Max(iconColumnWidth, icon?.Size.Width ?? 0),
                HorizontalAlignment = HorizontalAlignment.Left,
                VerticalAlignment = VerticalAlignment.Center,
            });

            (FontFamily family, double size) = ThemeFonts.Get(0);
            row.Children.Add(new TextBlock
            {
                Text = text,
                FontFamily = family,
                FontSize = size,
                Foreground = new SolidColorBrush(ThemeAssets.LabelColor),
                VerticalAlignment = VerticalAlignment.Center,
            });

            Canvas.SetLeft(row, x);
            Canvas.SetTop(row, y);
            canvas.Children.Add(row);
        }

        // SetGameOptionsInfo: icons alone in a row, then icons with text in two columns
        if (options.IconsOnly.Count > 0 || options.WithText.Count > 0)
        {
            Divider(sectionY);
            int y = sectionY + 1 + legendPadding;

            if (options.IconsOnly.Count > 0)
            {
                int x = leftColumnPositionX;
                double maxIconHeight = 0;
                foreach (BroadcastedOptionIcon option in options.IconsOnly)
                {
                    Bitmap icon = ThemeAssets.LoadBitmap(option.IconName);
                    if (icon == null)
                        continue;

                    var image = new Image { Source = icon, Stretch = Stretch.None };
                    Canvas.SetLeft(image, x);
                    Canvas.SetTop(image, y);
                    canvas.Children.Add(image);
                    x += (int)icon.Size.Width + 4;
                    maxIconHeight = Math.Max(maxIconHeight, icon.Size.Height);
                }

                y += (int)maxIconHeight + legendPadding;
            }

            if (options.WithText.Count > 0)
            {
                double maxIconWidth = 0;
                foreach (BroadcastedOptionIcon option in options.WithText)
                    maxIconWidth = Math.Max(maxIconWidth, ThemeAssets.LoadBitmap(option.IconName)?.Size.Width ?? 0);

                int leftY = y;
                int rightY = y;
                for (int i = 0; i < options.WithText.Count; i++)
                {
                    bool isLeftColumn = i % 2 == 0;
                    IconRow(ThemeAssets.LoadBitmap(options.WithText[i].IconName), options.WithText[i].Text,
                        isLeftColumn ? leftColumnPositionX : rightColumnPositionX, isLeftColumn ? leftY : rightY, maxIconWidth);

                    if (isLeftColumn)
                        leftY += legendIconHeight + legendIconPadding;
                    else
                        rightY += legendIconHeight + legendIconPadding;
                }

                y = Math.Max(leftY, rightY);
            }

            sectionY = y + legendPadding;
            height = Math.Max(height, sectionY);
        }

        // SetLegendInfo
        var legend = new List<(Bitmap Icon, string Text)>();
        if (game.Locked) legend.Add((ThemeAssets.LoadBitmap("lockedgame.png"), "Game is locked".L10N("Client:Main:LockedGame")));
        if (game.Passworded) legend.Add((ThemeAssets.LoadBitmap("passwordedgame.png"), "Game is passworded".L10N("Client:Main:PasswordedGame")));
        if (game.Incompatible) legend.Add((ThemeAssets.LoadBitmap("incompatible.png"), "Incompatible client version".L10N("Client:Main:IncompatibleGame")));

        if (legend.Count > 0)
        {
            Divider(sectionY);
            int y = sectionY + 1 + legendPadding;
            foreach ((Bitmap icon, string text) in legend)
            {
                IconRow(icon, text, leftColumnPositionX, y, 0);
                y += legendIconHeight;
            }

            height = Math.Max(height, y + legendPadding);
        }

        canvas.Height = height;

        preview ??= ThemeAssets.FindFile("noMapPreview.png") != null ? ThemeAssets.LoadBitmap("noMapPreview.png") : null;
        if (preview != null)
        {
            // RenderMapPreview: fit the right half, at most maxPreviewHeight high, centred in the right column
            double xRatio = (Width / 2 - mapPreviewHorizontalMargin) / preview.Size.Width;
            double yRatio = (height - mapPreviewVerticalMargin) / preview.Size.Height;
            double ratio = Math.Min(xRatio, yRatio);
            int textureWidth = (int)(preview.Size.Width * ratio);
            int textureHeight = (int)(preview.Size.Height * ratio);

            if (textureHeight > maxPreviewHeight)
            {
                ratio = maxPreviewHeight / preview.Size.Height;
                textureHeight = maxPreviewHeight;
                textureWidth = (int)(preview.Size.Width * ratio);
            }

            var image = new Image { Source = preview, Width = textureWidth, Height = textureHeight, Stretch = Stretch.Fill };
            Canvas.SetLeft(image, rightColumnPositionX + (Width / 2 - textureWidth) / 2);
            Canvas.SetTop(image, mapPreviewPositionY);
            canvas.Children.Insert(0, image);
        }

        // GameListBox sets cncnetlobbypanelbg.png, but the panel's Initialize then replaces it with black
        return new Border
        {
            Width = Width,
            Height = height,
            Background = Brushes.Black,
            BorderBrush = new SolidColorBrush(ThemeAssets.PanelBorderColor),
            BorderThickness = new Thickness(1),
            Child = canvas,
            IsHitTestVisible = false,
        };
    }
}
