using System;
using System.Collections.Generic;

using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;

namespace AvClientView.Theme;

/// <summary>An item of a <see cref="ThemedContextMenu"/>.</summary>
/// <param name="Text">The item text.</param>
/// <param name="Action">Run when the item is chosen.</param>
/// <param name="Color">The text colour, or null for the theme's.</param>
/// <param name="Selectable">False: drawn in the disabled colour and can't be chosen.</param>
public sealed record ThemedMenuItem(string Text, Action Action, Color? Color = null, bool Selectable = true);

/// <summary>
/// An XNAContextMenu: a black, bordered list of items at a point, closing when an item is chosen or the user clicks
/// elsewhere; the hovered item is highlighted with the list focus colour.
/// </summary>
public static class ThemedContextMenu
{
    public static void Open(Control target, Point position, IReadOnlyList<ThemedMenuItem> items, double width = 150)
    {
        if (items.Count == 0)
            return;

        (FontFamily family, double size) = ThemeFonts.Get(0);
        var list = new StackPanel { Width = width };
        var popup = new Popup
        {
            PlacementTarget = target,
            Placement = PlacementMode.AnchorAndGravity,
            PlacementAnchor = Avalonia.Controls.Primitives.PopupPositioning.PopupAnchor.TopLeft,
            PlacementGravity = Avalonia.Controls.Primitives.PopupPositioning.PopupGravity.BottomRight,
            PlacementRect = new Rect(position, new Size(1, 1)),
            IsLightDismissEnabled = true,
            Child = new LayoutTransformControl
            {
                // Scaled like the window's content, with the client cursor (popups are their own windows)
                LayoutTransform = ThemeAssets.PopupTransform,
                Cursor = ThemeAssets.ArrowCursor,
                Child = new Border
                {
                    Background = Brushes.Black,
                    BorderBrush = new SolidColorBrush(ThemeAssets.PanelBorderColor),
                    BorderThickness = new Thickness(1),
                    Child = list,
                },
            },
        };

        var focusBrush = new SolidColorBrush(ThemeAssets.ListFocusColor);
        foreach (ThemedMenuItem item in items)
        {
            var row = new Border
            {
                Background = Brushes.Transparent,
                Padding = new Thickness(4, 1),
                Cursor = item.Selectable ? ThemeAssets.HandCursor : ThemeAssets.ArrowCursor,
                Child = new TextBlock
                {
                    Text = item.Text,
                    FontFamily = family,
                    FontSize = size,
                    Foreground = new SolidColorBrush(!item.Selectable ? ThemeAssets.DisabledItemColor : item.Color ?? ThemeAssets.ButtonTextColor),
                    VerticalAlignment = VerticalAlignment.Center,
                },
            };

            if (item.Selectable)
            {
                row.PointerEntered += (_, _) => row.Background = focusBrush;
                row.PointerExited += (_, _) => row.Background = Brushes.Transparent;
                row.PointerPressed += (_, e) =>
                {
                    if (!e.GetCurrentPoint(row).Properties.IsLeftButtonPressed)
                        return;

                    e.Handled = true;
                    popup.IsOpen = false;
                    item.Action?.Invoke();
                };
            }

            list.Children.Add(row);
        }

        // The popup needs a visual parent to open from
        if (target is Panel panel)
            panel.Children.Add(popup);
        else if (target.Parent is Panel parentPanel)
            parentPanel.Children.Add(popup);

        popup.Closed += (_, _) => (popup.Parent as Panel)?.Children.Remove(popup);
        popup.IsOpen = true;
    }
}
