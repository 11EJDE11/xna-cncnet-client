using System;
using System.Collections.Generic;

using Avalonia.Controls;
using Avalonia.Data;
using Avalonia.Layout;
using Avalonia.Media;

using AvClientView.Theme;
using AvClientView.ViewModels;

using ClientCore.Extensions;

using ClientLogic.Layout;

using Rampastring.Tools;

namespace AvClientView.Views;

/// <summary>The XNA LoadOrSaveGameOptionPresetWindow (325×185, black) over a darkening panel.</summary>
public sealed class GameOptionPresetsView : Panel
{
    private const int MARGIN = 10;
    private const int BUTTON_WIDTH_92 = 92;
    private const int BUTTON_HEIGHT = 23;

    private readonly GameOptionPresetsViewModel viewModel;

    public GameOptionPresetsView(GameOptionPresetsViewModel viewModel)
    {
        this.viewModel = viewModel;
        Background = new SolidColorBrush(Color.FromArgb(128, 0, 0, 0));
        IsVisible = false;

        viewModel.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(GameOptionPresetsViewModel.IsOpen))
                IsVisible = viewModel.IsOpen;
        };

        try
        {
            Children.Add(Build());
        }
        catch (Exception ex)
        {
            Logger.Log("GameOptionPresetsView: building the window failed: " + ex);
        }
    }

    private Canvas Build()
    {
        var window = new LayoutControl("LoadOrSaveGameOptionPresetWindow", "XNAWindow", LayoutControlKind.Panel)
        {
            Width = 325,
            Height = 185,
            SolidBackground = new ClientLogic.UI.ChatColor(0, 0, 0),
        };

        LayoutControl lblHeader = ThemedWindow.Add(window, "lblHeader", "XNALabel", MARGIN, MARGIN, 150, 22);
        lblHeader.FontIndex = 1;
        LayoutControl lblPresetName = ThemedWindow.Add(window, "lblPresetName", "XNALabel", MARGIN, lblHeader.Y + 22 + MARGIN, 150, 18,
            "Preset Name".L10N("Client:Main:PresetName"));
        LayoutControl ddPresetSelect = ThemedWindow.Add(window, "ddPresetSelect", "XNAClientDropDown", 10, lblPresetName.Y + 18 + 2, 150, 22);
        LayoutControl lblNewPresetName = ThemedWindow.Add(window, "lblNewPresetName", "XNALabel", MARGIN, ddPresetSelect.Y + 22 + MARGIN, 150, 18,
            "New Preset Name".L10N("Client:Main:NewPresetName"));
        ThemedWindow.Add(window, "tbNewPresetName", "XNATextBox", 10, lblNewPresetName.Y + 18 + 2, 150, 22);
        int buttonY = window.Height - BUTTON_HEIGHT - MARGIN;
        ThemedWindow.Add(window, "btnLoadSave", "XNAClientButton", MARGIN, buttonY, BUTTON_WIDTH_92, BUTTON_HEIGHT, " ");
        ThemedWindow.Add(window, "btnDelete", "XNAClientButton", MARGIN + BUTTON_WIDTH_92 + MARGIN, buttonY, BUTTON_WIDTH_92, BUTTON_HEIGHT,
            "Delete".L10N("Client:Main:ButtonDelete"));
        ThemedWindow.Add(window, "btnCancel", "XNAClientButton", MARGIN + (BUTTON_WIDTH_92 + MARGIN) * 2, buttonY, BUTTON_WIDTH_92, BUTTON_HEIGHT,
            "Cancel".L10N("Client:Main:ButtonCancel"));

        Canvas canvas = ThemedWindow.Build(window, (name, _) =>
        {
            switch (name)
            {
                case "btnLoadSave":
                    viewModel.LoadOrSave();
                    break;
                case "btnDelete":
                    viewModel.Delete();
                    break;
                case "btnCancel":
                    viewModel.Cancel();
                    break;
            }
        }, new Dictionary<string, Func<LayoutControl, Control>>
        {
            ["lblHeader"] = layout => BoundText(layout, nameof(GameOptionPresetsViewModel.Header)),
            ["ddPresetSelect"] = layout =>
            {
                var dropDown = new ThemedDropDown(layout.Width, layout.Height, layout.FontIndex) { DataContext = viewModel };
                dropDown.Bind(ThemedDropDown.ItemsSourceProperty, new Binding(nameof(GameOptionPresetsViewModel.Items)));
                dropDown.Bind(ThemedDropDown.SelectedIndexProperty, new Binding(nameof(GameOptionPresetsViewModel.SelectedIndex)) { Mode = BindingMode.TwoWay });
                return dropDown;
            },
            ["tbNewPresetName"] = layout =>
            {
                TextBox textBox = ThemedStyle.TextBox(layout.Width, layout.Height, string.Empty);
                textBox.DataContext = viewModel;
                textBox.Bind(TextBox.TextProperty, new Binding(nameof(GameOptionPresetsViewModel.NewPresetName)) { Mode = BindingMode.TwoWay });
                textBox.Bind(IsVisibleProperty, new Binding(nameof(GameOptionPresetsViewModel.ShowNewPresetName)));
                return textBox;
            },
        });

        if (LayoutView.FindNamed<TextBlock>(canvas, "lblNewPresetName") is TextBlock newNameLabel)
        {
            newNameLabel.DataContext = viewModel;
            newNameLabel.Bind(IsVisibleProperty, new Binding(nameof(GameOptionPresetsViewModel.ShowNewPresetName)));
        }

        if (LayoutView.FindNamed<ThemedButton>(canvas, "btnLoadSave") is ThemedButton loadSave)
        {
            loadSave.DataContext = viewModel;
            loadSave.Bind(IsEnabledProperty, new Binding(nameof(GameOptionPresetsViewModel.CanLoadOrSave)));
            viewModel.PropertyChanged += (_, e) =>
            {
                if (e.PropertyName == nameof(GameOptionPresetsViewModel.LoadSaveText))
                    loadSave.Text = viewModel.LoadSaveText;
            };
        }

        if (LayoutView.FindNamed<ThemedButton>(canvas, "btnDelete") is ThemedButton delete)
        {
            delete.DataContext = viewModel;
            delete.Bind(IsEnabledProperty, new Binding(nameof(GameOptionPresetsViewModel.CanDelete)));
        }

        canvas.HorizontalAlignment = HorizontalAlignment.Center;
        canvas.VerticalAlignment = VerticalAlignment.Center;
        return canvas;
    }

    private TextBlock BoundText(LayoutControl layout, string path)
    {
        (FontFamily family, double size) = ThemeFonts.Get(layout.FontIndex);
        var text = new TextBlock { FontFamily = family, FontSize = size, Foreground = new SolidColorBrush(ThemeAssets.LabelColor), DataContext = viewModel };
        text.Bind(TextBlock.TextProperty, new Binding(path));
        return text;
    }
}
