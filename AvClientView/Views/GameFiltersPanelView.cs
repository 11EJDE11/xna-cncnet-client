using System;
using System.Collections.Generic;
using System.Linq;

using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Media.Imaging;

using AvClientView.Theme;
using AvClientView.ViewModels;

using ClientCore;
using ClientCore.Extensions;

using ClientLogic.Layout;
using ClientLogic.Options;

namespace AvClientView.Views;

/// <summary>
/// The XNA GameFiltersPanel over the CnCNet game list: friends only, hide locked / passworded / incompatible games,
/// max player count, and a filter per filterable broadcast option (All / On / Off, or All / each item); Reset
/// Defaults, Save and Cancel. Saved in the settings INI.
/// </summary>
public sealed class GameFiltersPanelView : Border
{
    private const int GAP = 12;
    private const int BOTTOM_PANEL_HEIGHT = 60;
    private const int BUTTON_HEIGHT = 23;
    private const int MIN_PLAYER_COUNT = 2;
    private const int MAX_PLAYER_COUNT = 8;

    private readonly CnCNetLobbyViewModel viewModel;
    private readonly Canvas content = new();
    private readonly ThemedCheckBox friendsOnly;
    private readonly ThemedCheckBox hideLocked;
    private readonly ThemedCheckBox hidePassworded;
    private readonly ThemedCheckBox hideIncompatible;
    private readonly ThemedDropDown maxPlayerCount;
    private readonly List<(GameOption Option, ThemedDropDown DropDown)> optionFilters = [];
    private ThemedButton resetDefaults;
    private bool optionFiltersCreated;

    public GameFiltersPanelView(double width, double height, CnCNetLobbyViewModel viewModel)
    {
        this.viewModel = viewModel;
        Width = width;
        Height = height;
        Background = Brushes.Black;
        BorderBrush = new SolidColorBrush(ThemeAssets.PanelBorderColor);
        BorderThickness = new Thickness(1);
        IsVisible = false;

        (FontFamily family, double size) = ThemeFonts.Get(0);
        var title = new TextBlock { Text = "Game Filters".L10N("Client:Main:GameFilters"), FontFamily = family, FontSize = size, Foreground = new SolidColorBrush(ThemeAssets.LabelColor) };
        Place(title, GAP, GAP);

        int y = GAP + BUTTON_HEIGHT + GAP;
        friendsOnly = CheckBox("Show Friend Games Only".L10N("Client:Main:FriendGameOnly"), ref y);
        hideLocked = CheckBox("Hide Locked Games".L10N("Client:Main:HideLockedGame"), ref y);
        hidePassworded = CheckBox("Hide Passworded Games".L10N("Client:Main:HidePasswordGame"), ref y);
        hideIncompatible = CheckBox("Hide Incompatible Games".L10N("Client:Main:HideIncompatibleGame"), ref y);

        maxPlayerCount = new ThemedDropDown(40, BUTTON_HEIGHT)
        {
            ItemsSource = Enumerable.Range(MIN_PLAYER_COUNT, MAX_PLAYER_COUNT - MIN_PLAYER_COUNT + 1).Select(i => i.ToString()).ToList(),
        };
        Place(maxPlayerCount, GAP, y);
        Place(new TextBlock { Text = "Max Player Count".L10N("Client:Main:MaxPlayerCount"), FontFamily = family, FontSize = size,
            Foreground = new SolidColorBrush(ThemeAssets.LabelColor), Height = BUTTON_HEIGHT }, GAP + 40 + GAP, y + 3);
        y += BUTTON_HEIGHT + GAP;

        resetDefaults = Button("btnResetDefaults", "Reset Defaults".L10N("Client:Main:ResetDefaults"), 133, () =>
        {
            UserINISettings.Instance.ResetGameFilters();
            Load();
        });
        Place(resetDefaults, GAP, y);
        content.Height = y + BUTTON_HEIGHT + GAP;

        var scroll = new ScrollViewer
        {
            Height = height - BOTTOM_PANEL_HEIGHT,
            Background = new SolidColorBrush(Color.FromArgb(200, 0, 0, 0)),
            Content = content,
            HorizontalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Disabled,
        };

        var bottom = new Canvas { Height = BOTTOM_PANEL_HEIGHT, Background = Brushes.Black };
        ThemedButton save = Button("btnSave", "Save".L10N("Client:Main:ButtonSave"), 92, () =>
        {
            Save();
            IsVisible = false;
            viewModel.GameFiltersChanged();
        });
        Canvas.SetLeft(save, GAP);
        Canvas.SetTop(save, (BOTTOM_PANEL_HEIGHT - BUTTON_HEIGHT) / 2);
        bottom.Children.Add(save);

        ThemedButton cancel = Button("btnCancel", "Cancel".L10N("Client:Main:ButtonCancel"), 92, Cancel);
        Canvas.SetLeft(cancel, width - GAP - 92);
        Canvas.SetTop(cancel, (BOTTOM_PANEL_HEIGHT - BUTTON_HEIGHT) / 2);
        bottom.Children.Add(cancel);

        Child = new DockPanel
        {
            Children =
            {
                WithDock(bottom, Dock.Bottom),
                scroll,
            },
        };
    }

    private static Control WithDock(Control control, Dock dock)
    {
        DockPanel.SetDock(control, dock);
        return control;
    }

    private void Place(Control control, double x, double y)
    {
        Canvas.SetLeft(control, x);
        Canvas.SetTop(control, y);
        content.Children.Add(control);
    }

    private ThemedCheckBox CheckBox(string text, ref int y)
    {
        var checkBox = new ThemedCheckBox(text);
        Place(checkBox, GAP, y);
        y += BUTTON_HEIGHT + GAP;
        return checkBox;
    }

    private static ThemedButton Button(string name, string text, int width, Action onClick)
    {
        var layout = new LayoutControl(name, "XNAClientButton", LayoutControlKind.Button) { Width = width, Height = BUTTON_HEIGHT, Text = text };
        ThemedWindow.CreateReader().Initialize(layout);
        var button = new ThemedButton(layout);
        button.Click += (_, _) => onClick();
        return button;
    }

    /// <summary>The per-option filters, created the first time the panel opens (CreateGameOptionFilters).</summary>
    private void CreateGameOptionFilters()
    {
        IReadOnlyList<GameOption> options = viewModel.FilterableOptions;
        if (options.Count == 0)
            return;

        const int iconLabelSpacing = 6;
        const int itemVerticalSpacing = 4;
        const int minLabelRowHeight = 18;

        int currentY = (int)Canvas.GetTop(maxPlayerCount) + BUTTON_HEIGHT + GAP;
        int contentWidth = (int)Width - 18;
        int dropDownWidth = (contentWidth - GAP * 3) / 2;

        Place(new Border { Width = contentWidth, Height = 1, Background = new SolidColorBrush(ThemeAssets.PanelBorderColor) }, 0, currentY);
        currentY += 1 + GAP;

        (FontFamily family, double size) = ThemeFonts.Get(0);
        int leftX = GAP;
        int rightX = leftX + dropDownWidth + GAP;
        int maxItemHeight = 0;
        var placed = new List<(Control Icon, Control Label, ThemedDropDown DropDown, int Index, int TopRowHeight)>();

        for (int index = 0; index < options.Count; index++)
        {
            GameOption option = options[index];
            bool isCheckBox = option.IsCheckBox;

            string iconName = isCheckBox ? option.Definition.EnabledIcon : option.Definition.Items.FirstOrDefault()?.IconName;
            Bitmap icon = string.IsNullOrEmpty(iconName) ? null : ThemeAssets.LoadBitmap(iconName);
            int iconWidth = icon?.PixelSize.Width ?? 0;
            int iconHeight = icon?.PixelSize.Height ?? 0;
            int topRowHeight = Math.Max(iconHeight, minLabelRowHeight);

            var items = new List<string> { "All".L10N("Client:Main:FilterAllGames") };
            var itemIcons = new List<string> { null };
            if (isCheckBox)
            {
                items.Add("On".L10N("Client:Main:FilterOn"));
                itemIcons.Add(option.Definition.EnabledIcon);
                items.Add("Off".L10N("Client:Main:FilterOff"));
                itemIcons.Add(option.Definition.DisabledIcon);
            }
            else
            {
                foreach (GameOptionItem item in option.Definition.Items)
                {
                    items.Add(item.Label);
                    itemIcons.Add(item.IconName);
                }
            }

            var dropDown = new ThemedDropDown(dropDownWidth, BUTTON_HEIGHT) { ItemsSource = items, SelectedIndex = 0 };
            dropDown.ItemDecoration = i => i >= 0 && i < itemIcons.Count && !string.IsNullOrEmpty(itemIcons[i]) && ThemeAssets.LoadBitmap(itemIcons[i]) is Bitmap b
                ? new Image { Source = b, Stretch = Stretch.None }
                : null;

            Control iconControl = icon == null ? null : new Image { Source = icon, Stretch = Stretch.None };
            var label = new TextBlock
            {
                Text = isCheckBox ? option.Definition.Label + ":" : option.Definition.OptionName,
                FontFamily = family,
                FontSize = size,
                Foreground = new SolidColorBrush(ThemeAssets.LabelColor),
                Tag = iconWidth > 0 ? iconWidth + iconLabelSpacing : 0,
            };

            maxItemHeight = Math.Max(maxItemHeight, topRowHeight + itemVerticalSpacing + BUTTON_HEIGHT + GAP);
            placed.Add((iconControl, label, dropDown, index, topRowHeight));
            optionFilters.Add((option, dropDown));
        }

        foreach ((Control iconControl, Control label, ThemedDropDown dropDown, int index, int topRowHeight) in placed)
        {
            int columnX = index % 2 == 0 ? leftX : rightX;
            int rowY = currentY + index / 2 * maxItemHeight;
            if (iconControl != null)
                Place(iconControl, columnX, rowY);

            Place(label, columnX + (int)label.Tag, rowY);
            Place(dropDown, columnX, rowY + topRowHeight + itemVerticalSpacing);
        }

        currentY += (options.Count + 1) / 2 * maxItemHeight;
        Place(new Border { Width = contentWidth, Height = 1, Background = new SolidColorBrush(ThemeAssets.PanelBorderColor) }, 0, currentY);
        currentY += 1 + GAP;

        Canvas.SetTop(resetDefaults, currentY);
        content.Height = currentY + BUTTON_HEIGHT + GAP;
    }

    public void Show()
    {
        if (!optionFiltersCreated)
        {
            optionFiltersCreated = true;
            CreateGameOptionFilters();
        }

        Load();
        IsVisible = true;
    }

    public void Cancel() => IsVisible = false;

    private void Load()
    {
        UserINISettings settings = UserINISettings.Instance;
        friendsOnly.IsChecked = settings.ShowFriendGamesOnly.Value;
        hideLocked.IsChecked = settings.HideLockedGames.Value;
        hidePassworded.IsChecked = settings.HidePasswordedGames.Value;
        hideIncompatible.IsChecked = settings.HideIncompatibleGames.Value;
        maxPlayerCount.SelectedIndex = settings.MaxPlayerCount.Value - MIN_PLAYER_COUNT;

        foreach ((GameOption option, ThemedDropDown dropDown) in optionFilters)
        {
            int? filterValue = settings.GetGameOptionFilterValue(option.Name);
            dropDown.SelectedIndex = option.IsCheckBox
                ? filterValue switch { null => 0, 1 => 1, 0 => 2, _ => 0 }
                : filterValue == null ? 0 : filterValue.Value + 1;
        }
    }

    private void Save()
    {
        UserINISettings settings = UserINISettings.Instance;
        settings.ShowFriendGamesOnly.Value = friendsOnly.IsChecked;
        settings.HideLockedGames.Value = hideLocked.IsChecked;
        settings.HidePasswordedGames.Value = hidePassworded.IsChecked;
        settings.HideIncompatibleGames.Value = hideIncompatible.IsChecked;
        settings.MaxPlayerCount.Value = maxPlayerCount.SelectedIndex + MIN_PLAYER_COUNT;

        // Only non-default option filters are stored
        settings.SettingsIni.GetSection(UserINISettings.GAME_OPTION_FILTERS)?.RemoveAllKeys();
        foreach ((GameOption option, ThemedDropDown dropDown) in optionFilters)
        {
            int? filterValue = option.IsCheckBox
                ? dropDown.SelectedIndex switch { 1 => 1, 2 => 0, _ => null }
                : dropDown.SelectedIndex <= 0 ? null : dropDown.SelectedIndex - 1;

            if (filterValue != null)
                settings.SetGameOptionFilterValue(option.Name, filterValue);
        }

        settings.SaveSettings();
    }
}
