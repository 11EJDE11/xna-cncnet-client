using System;
using System.Collections.Specialized;
using System.Linq;

using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Controls.Templates;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.VisualTree;

using AvClientView.ViewModels;

using ClientCore;
using ClientCore.Extensions;

using ClientLogic.Layout;

using DTAClient.Domain.Multiplayer;

namespace AvClientView.Theme;

/// <summary>
/// The XNA GameListBox (CnCNet and LAN lobbies): rows with the game's icons, the HoverOnGameColor behind the hovered
/// game, and the game information panel to the right of the list for the selected game, else the hovered one.
/// </summary>
public static class GameListView
{
    public static ListBox Create(LayoutControl layout, object dataContext, string itemsPath, INotifyCollectionChanged items,
        Func<GenericHostedGame, (string MapName, Bitmap Preview)> findMap)
    {
        ListBox list = ThemedWindow.GameList(layout, dataContext, itemsPath,
            new FuncDataTemplate<HostedGameItemViewModel>((item, _) => Row(item)));

        var infoHost = new ContentControl { IsHitTestVisible = false, ZIndex = 2000 };
        HostedGameItemViewModel shown = null;
        HostedGameItemViewModel hovered = null;

        void Refresh(bool force = false)
        {
            HostedGameItemViewModel item = list.SelectedItem as HostedGameItemViewModel ?? hovered;
            if (item == shown && !force)
                return;

            shown = item;
            infoHost.Content = item == null ? null : GameInformationPanel.Build(item.Game, findMap, item.InformationOptions);
        }

        list.PointerMoved += (_, e) =>
        {
            hovered = (e.Source as Visual)?.FindAncestorOfType<ListBoxItem>(includeSelf: true)?.DataContext as HostedGameItemViewModel;
            Refresh();
        };
        list.PointerExited += (_, _) => { hovered = null; Refresh(); };
        list.SelectionChanged += (_, _) => Refresh();
        void ItemsChanged(object sender, NotifyCollectionChangedEventArgs e)
        {
            // A broadcast may replace a game's row. Keep the hovered game by identity instead of hiding its
            // information panel whenever any other game is announced or leaves.
            hovered = list.ItemsSource?.Cast<HostedGameItemViewModel>().FirstOrDefault(item =>
                item.Game == hovered?.Game || (item.Game is DTAClient.Domain.Multiplayer.CnCNet.HostedCnCNetGame game &&
                    hovered?.Game is DTAClient.Domain.Multiplayer.CnCNet.HostedCnCNetGame oldGame && game.ChannelName == oldGame.ChannelName) ||
                (item.Game is DTAClient.Domain.LAN.HostedLANGame lanGame &&
                    hovered?.Game is DTAClient.Domain.LAN.HostedLANGame oldLanGame && lanGame.EndPoint.Equals(oldLanGame.EndPoint)));
            Refresh(force: true);
        }

        list.AttachedToVisualTree += (_, _) =>
        {
            items.CollectionChanged += ItemsChanged;
            ItemsChanged(items, new NotifyCollectionChangedEventArgs(NotifyCollectionChangedAction.Reset));
            if (list.Parent is Canvas canvas && !canvas.Children.Contains(infoHost))
            {
                Canvas.SetLeft(infoHost, layout.X + layout.Width);
                Canvas.SetTop(infoHost, layout.Y);
                canvas.Children.Add(infoHost);
            }
        };
        list.DetachedFromVisualTree += (_, _) => items.CollectionChanged -= ItemsChanged;

        return list;
    }

    /// <summary>
    /// A row as the XNA GameListBox draws it: option icons, game icon (for other games, or if the theme shows it),
    /// locked and incompatible icons, the room name (grey if it can't be joined, the label colour for other games),
    /// and on the right the right-side option icons, password and skill level icons.
    /// </summary>
    public static Control Row(HostedGameItemViewModel item)
    {
        var row = new DockPanel { Margin = new Thickness(2, 1), LastChildFill = true };
        if (item == null)
            return row;

        GenericHostedGame game = item.Game;

        void AddIcon(Bitmap bitmap, Dock dock)
        {
            if (bitmap == null)
                return;

            var image = new Image { Source = bitmap, Stretch = Stretch.None, Margin = new Thickness(0, 0, 2, 0), VerticalAlignment = VerticalAlignment.Center };
            DockPanel.SetDock(image, dock);
            row.Children.Add(image);
        }

        // Right side, from the edge inwards: option icons, password, skill level
        for (int i = item.OptionIcons.Right.Count - 1; i >= 0; i--)
            AddIcon(ThemeAssets.LoadBitmap(item.OptionIcons.Right[i]), Dock.Right);
        if (game.Passworded)
            AddIcon(ThemeAssets.LoadBitmap("passwordedgame.png"), Dock.Right);
        string skillIcon = $"skillLevel{game.SkillLevel}.png";
        if (ThemeAssets.FindFile(skillIcon) != null)
            AddIcon(ThemeAssets.LoadBitmap(skillIcon), Dock.Right);

        // Left side: option icons, game icon, locked, incompatible
        foreach (string icon in item.OptionIcons.Left)
            AddIcon(ThemeAssets.LoadBitmap(icon), Dock.Left);

        bool otherGame = !string.Equals(game.Game?.InternalName, ClientConfiguration.Instance.LocalGame, StringComparison.OrdinalIgnoreCase);
        if (ClientConfiguration.Instance.ShowGameIconInGameList || otherGame)
            AddIcon(ThemeAssets.GameIcon(game.Game), Dock.Left);
        if (game.Locked)
            AddIcon(ThemeAssets.LoadBitmap("lockedgame.png"), Dock.Left);
        if (game.Incompatible)
            AddIcon(ThemeAssets.LoadBitmap("incompatible.png"), Dock.Left);

        Color textColor = game.Locked || game.Incompatible ? Colors.Gray
            : otherGame ? ThemeAssets.LabelColor : ThemeAssets.ButtonTextColor;

        row.Children.Add(new TextBlock
        {
            Text = game.RoomName + (game.IsLoadedGame ? " (" + "Loaded Game".L10N("Client:Main:LoadedGame") + ")" : string.Empty),
            VerticalAlignment = VerticalAlignment.Center,
            Foreground = new SolidColorBrush(textColor),
            TextTrimming = TextTrimming.CharacterEllipsis,
        });

        return row;
    }
}
