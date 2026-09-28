using System;
using System.Collections.Generic;

using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Controls.Templates;
using Avalonia.Data;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;

using AvClientView.Services;
using AvClientView.Theme;
using AvClientView.ViewModels;

using ClientCore.Extensions;

using ClientLogic.Layout;
using ClientLogic.Settings;

using Rampastring.Tools;

namespace AvClientView.Views;

/// <summary>
/// The XNA HotkeyConfigurationWindow over a darkening panel, laid out as its Initialize does, with the theme's
/// HotkeyConfigurationWindow.ini applied. While it is open it takes the keyboard: a pressed key (with Shift, Ctrl and
/// Alt) becomes the new hotkey.
/// </summary>
public sealed class HotkeyWindowView : Panel
{
    private readonly HotkeyWindowViewModel viewModel;

    public HotkeyWindowView(HotkeyWindowViewModel viewModel)
    {
        this.viewModel = viewModel;
        ThemedStyle.Darken(this);
        IsVisible = false;
        Focusable = true;

        viewModel.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName != nameof(HotkeyWindowViewModel.IsOpen))
                return;

            IsVisible = viewModel.IsOpen;
            if (viewModel.IsOpen)
                Dispatcher.Post(() => Focus());
        };

        try
        {
            Children.Add(Build());
        }
        catch (Exception ex)
        {
            Logger.Log("HotkeyWindowView: building the hotkey window failed: " + ex);
            Children.Add(new TextBlock { Text = "The hotkey window could not be built: " + ex.Message, Foreground = Brushes.White });
        }
    }

    private static Avalonia.Threading.Dispatcher Dispatcher => Avalonia.Threading.Dispatcher.UIThread;

    private static (int Width, int Height) Measure(string text, int fontIndex = 0) => ThemeFonts.Measure(text, fontIndex);

    private static LayoutControl Label(LayoutControl parent, string name, int x, int y, string text, int fontIndex = 0)
    {
        LayoutControl label = ThemedWindow.Add(parent, name, "XNALabel", x, y, 0, 0, text);
        label.FontIndex = fontIndex;
        (label.Width, label.Height) = Measure(text, fontIndex);
        return label;
    }

    private static int Right(LayoutControl c) => c.X + c.Width;

    private static int Bottom(LayoutControl c) => c.Y + c.Height;

    private Canvas Build()
    {
        var window = new LayoutControl("HotkeyConfigurationWindow", "XNAWindow", LayoutControlKind.Panel)
        {
            Width = 600,
            Height = 450,
            BackgroundTexture = "hotkeyconfigbg.png",
        };

        LayoutControl lblCategory = Label(window, "lblCategory", 12, 12, "Category:".L10N("Client:DTAConfig:Category"));

        // XNADropDown's constructor height (item height + 2); Initialize later makes it the arrow's height
        int dropDownHeight = Measure("Test String @").Height + 1 + 2;
        LayoutControl ddCategory = ThemedWindow.Add(window, "ddCategory", "XNAClientDropDown", Right(lblCategory) + 12, lblCategory.Y - 1, 250, dropDownHeight);

        LayoutControl lbHotkeys = ThemedWindow.Add(window, "lbHotkeys", "XNAMultiColumnListBox", 12, Bottom(ddCategory) + 12,
            Right(ddCategory) - 12, window.Height - Bottom(ddCategory) - 59);

        var hotkeyInfoPanel = new LayoutControl("HotkeyInfoPanel", "XNAPanel", LayoutControlKind.Panel)
        {
            X = Right(lbHotkeys) + 12,
            Y = ddCategory.Y,
            Width = window.Width - Right(lbHotkeys) - 24,
            Height = lbHotkeys.Height + ddCategory.Height + 12,
            DrawBorders = true,
        };

        LayoutControl lblCommandCaption = Label(hotkeyInfoPanel, "lblCommandCaption", 12, 12, "Command name".L10N("Client:DTAConfig:CommandName"), 1);
        LayoutControl lblDescription = Label(hotkeyInfoPanel, "lblDescription", 12, Bottom(lblCommandCaption) + 12, "Command description".L10N("Client:DTAConfig:CommandDescription"));
        LayoutControl lblCurrentHotkey = Label(hotkeyInfoPanel, "lblCurrentHotkey", lblDescription.X, Bottom(lblDescription) + 48, "Currently assigned hotkey:".L10N("Client:DTAConfig:CurrentHotKey"), 1);
        LayoutControl lblCurrentHotkeyValue = Label(hotkeyInfoPanel, "lblCurrentHotkeyValue", lblDescription.X, Bottom(lblCurrentHotkey) + 6, "Current hotkey value".L10N("Client:DTAConfig:CurrentHotKeyValue"));
        LayoutControl lblNewHotkey = Label(hotkeyInfoPanel, "lblNewHotkey", lblDescription.X, Bottom(lblCurrentHotkeyValue) + 48, "New hotkey:".L10N("Client:DTAConfig:NewHotKey"), 1);
        LayoutControl lblNewHotkeyValue = Label(hotkeyInfoPanel, "lblNewHotkeyValue", lblDescription.X, Bottom(lblNewHotkey) + 6, "Press a key...".L10N("Client:DTAConfig:PressAKey"));
        LayoutControl lblCurrentlyAssignedTo = Label(hotkeyInfoPanel, "lblCurrentlyAssignedTo", lblDescription.X, Bottom(lblNewHotkeyValue) + 12,
            "Currently assigned to:".L10N("Client:DTAConfig:CurrentHotKeyAssign") + "\nKey");
        LayoutControl btnAssign = ThemedWindow.Add(hotkeyInfoPanel, "btnAssign", "XNAClientButton", lblDescription.X, Bottom(lblCurrentlyAssignedTo) + 24, 121, 23, "Assign Hotkey".L10N("Client:DTAConfig:AssignHotkey"));
        LayoutControl btnResetKey = ThemedWindow.Add(hotkeyInfoPanel, "btnResetKey", "XNAClientButton", btnAssign.X, Bottom(btnAssign) + 12, btnAssign.Width, 23, "Reset to Default".L10N("Client:DTAConfig:ResetToDefault"));
        LayoutControl lblDefaultHotkey = Label(hotkeyInfoPanel, "lblOriginalHotkey", lblCurrentHotkey.X, Bottom(btnResetKey) + 12, "Default hotkey:".L10N("Client:DTAConfig:DefaultHotKey"));
        Label(hotkeyInfoPanel, "lblDefaultHotkeyValue", Right(lblDefaultHotkey) + 12, lblDefaultHotkey.Y, string.Empty);

        LayoutControl btnSave = ThemedWindow.Add(window, "btnSave", "XNAClientButton", 12, Bottom(lbHotkeys) + 12, 92, 23, "Save".L10N("Client:DTAConfig:ButtonSave"));
        ThemedWindow.Add(window, "btnResetAllToDefaults", "XNAClientButton", (window.Width - 121) / 2, btnSave.Y, 121, 23, "Reset All Keys".L10N("Client:DTAConfig:ResetAllHotkey"));
        ThemedWindow.Add(window, "btnExit", "XNAClientButton", window.Width - 104, btnSave.Y, 92, 23, "Cancel".L10N("Client:DTAConfig:ButtonCancel"));
        window.AddChild(hotkeyInfoPanel);

        int descriptionWidth = hotkeyInfoPanel.Width - lblDescription.X;
        var overlays = new Dictionary<string, Func<LayoutControl, Control>>
        {
            ["ddCategory"] = layout =>
            {
                var dropDown = new ThemedDropDown(layout.Width, 22, layout.FontIndex) { DataContext = viewModel, ItemsSource = viewModel.Categories };
                dropDown.Bind(ThemedDropDown.SelectedIndexProperty, new Binding(nameof(HotkeyWindowViewModel.SelectedCategoryIndex)) { Mode = BindingMode.TwoWay });
                return dropDown;
            },
            ["lbHotkeys"] = HotkeyList,
            ["lblCommandCaption"] = layout => BoundLabel(layout, nameof(HotkeyWindowViewModel.CommandCaption)),
            ["lblDescription"] = layout =>
            {
                TextBlock label = BoundLabel(layout, nameof(HotkeyWindowViewModel.Description));
                label.MaxWidth = descriptionWidth;
                label.TextWrapping = TextWrapping.Wrap;
                return label;
            },
            ["lblCurrentHotkeyValue"] = layout => BoundLabel(layout, nameof(HotkeyWindowViewModel.CurrentHotkey)),
            ["lblNewHotkeyValue"] = layout => BoundLabel(layout, nameof(HotkeyWindowViewModel.NewHotkeyText)),
            ["lblCurrentlyAssignedTo"] = layout => BoundLabel(layout, nameof(HotkeyWindowViewModel.CurrentlyAssignedTo)),
            ["lblDefaultHotkeyValue"] = layout => BoundLabel(layout, nameof(HotkeyWindowViewModel.DefaultHotkey)),
        };

        Canvas canvas = ThemedWindow.Build(window, (name, _) => OnButton(name), overlays);
        canvas.HorizontalAlignment = HorizontalAlignment.Center;
        canvas.VerticalAlignment = VerticalAlignment.Center;

        if (LayoutView.FindNamed<Canvas>(canvas, "HotkeyInfoPanel") is Canvas infoPanel)
        {
            infoPanel.DataContext = viewModel;
            infoPanel.Bind(IsVisibleProperty, new Binding(nameof(HotkeyWindowViewModel.IsCommandSelected)));
        }

        if (LayoutView.FindNamed<ThemedButton>(canvas, "btnResetKey") is ThemedButton resetButton)
        {
            resetButton.DataContext = viewModel;
            resetButton.Bind(IsEnabledProperty, new Binding(nameof(HotkeyWindowViewModel.CanReset)));
        }

        return canvas;
    }

    private TextBlock BoundLabel(LayoutControl layout, string path)
    {
        (FontFamily family, double size) = ThemeFonts.Get(layout.FontIndex);
        var label = new TextBlock { FontFamily = family, FontSize = size, Foreground = new SolidColorBrush(ThemeAssets.LabelColor), DataContext = viewModel };
        label.Bind(TextBlock.TextProperty, new Binding(path));
        return label;
    }

    /// <summary>The XNAMultiColumnListBox: "Command" (150 px) and "Shortcut" columns with bordered headers.</summary>
    private Control HotkeyList(LayoutControl layout)
    {
        (FontFamily headerFamily, double headerSize) = ThemeFonts.Get(1);
        int headerHeight = Measure("Command", 1).Height + 3;
        var borderBrush = new SolidColorBrush(ThemeAssets.PanelBorderColor);

        Border Header(string text, double width) => new()
        {
            Width = width,
            Height = headerHeight,
            BorderBrush = borderBrush,
            BorderThickness = new Thickness(1),
            Child = new TextBlock
            {
                Text = text,
                FontFamily = headerFamily,
                FontSize = headerSize,
                Foreground = new SolidColorBrush(ThemeAssets.LabelColor),
                Margin = new Thickness(3, 2, 0, 0),
            },
        };

        var headers = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Children =
            {
                Header("Command".L10N("Client:DTAConfig:Command"), 150),
                Header("Shortcut".L10N("Client:DTAConfig:Shortcut"), layout.Width - 150),
            },
        };

        ListBox list = ThemedStyle.List(layout.Width, layout.Height - headerHeight + 1);
        list.Background = new SolidColorBrush(Color.FromArgb(128, 0, 0, 0));
        list.DataContext = viewModel;
        list.Bind(ItemsControl.ItemsSourceProperty, new Binding(nameof(HotkeyWindowViewModel.Rows)));
        list.Bind(SelectingItemsControl.SelectedIndexProperty, new Binding(nameof(HotkeyWindowViewModel.SelectedIndex)) { Mode = BindingMode.TwoWay });
        list.Resources["ListBoxItemPadding"] = new Thickness(0);
        list.ItemTemplate = new FuncDataTemplate<HotkeyRowViewModel>((row, _) =>
        {
            var grid = new Grid { ColumnDefinitions = new ColumnDefinitions("150,*") };
            var command = new TextBlock { Text = row?.Command.UIName, Margin = new Thickness(5, 1), TextTrimming = TextTrimming.CharacterEllipsis };
            var shortcut = new TextBlock { Text = row?.Shortcut, Margin = new Thickness(5, 1) };
            Grid.SetColumn(shortcut, 1);
            grid.Children.Add(command);
            grid.Children.Add(shortcut);
            return grid;
        });

        return new StackPanel { Children = { headers, list } };
    }

    private void OnButton(string name)
    {
        switch (name)
        {
            case "btnAssign":
                viewModel.Assign();
                break;
            case "btnResetKey":
                viewModel.ResetKey();
                break;
            case "btnResetAllToDefaults":
                viewModel.ResetAll();
                break;
            case "btnSave":
                viewModel.Save();
                break;
            case "btnExit":
                viewModel.Cancel();
                break;
        }
    }

    private static HotkeyModifiers ModifiersOf(KeyModifiers modifiers)
    {
        HotkeyModifiers result = HotkeyModifiers.None;
        if (modifiers.HasFlag(KeyModifiers.Shift))
            result |= HotkeyModifiers.Shift;
        if (modifiers.HasFlag(KeyModifiers.Control))
            result |= HotkeyModifiers.Ctrl;
        if (modifiers.HasFlag(KeyModifiers.Alt))
            result |= HotkeyModifiers.Alt;

        return result;
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);
        if (!viewModel.IsOpen)
            return;

        viewModel.KeyPressed(VirtualKeys.Of(e.Key), ModifiersOf(e.KeyModifiers));
        e.Handled = true;
    }

    protected override void OnKeyUp(KeyEventArgs e)
    {
        base.OnKeyUp(e);
        if (!viewModel.IsOpen)
            return;

        viewModel.ModifiersChanged(ModifiersOf(e.KeyModifiers));
        e.Handled = true;
    }
}
