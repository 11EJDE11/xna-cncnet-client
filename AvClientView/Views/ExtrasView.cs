using System;
using System.Collections.Generic;

using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;

using AvClientView.Theme;
using AvClientView.ViewModels;

using ClientCore.Extensions;

using ClientLogic.Layout;

using Rampastring.Tools;

namespace AvClientView.Views;

/// <summary>The XNA ExtrasWindow over a darkening panel, with the theme's ExtrasWindow.ini applied.</summary>
public sealed class ExtrasView : Panel
{
    private const int BUTTON_WIDTH_133 = 133;
    private const int BUTTON_HEIGHT = 23;

    private readonly ExtrasViewModel viewModel;

    public ExtrasView(ExtrasViewModel viewModel)
    {
        this.viewModel = viewModel;
        ThemedStyle.Darken(this);
        IsVisible = false;

        viewModel.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(ExtrasViewModel.IsOpen))
                IsVisible = viewModel.IsOpen;
        };

        try
        {
            Children.Add(Build());
        }
        catch (Exception ex)
        {
            Logger.Log("ExtrasView: building the extras window failed: " + ex);
            Children.Add(new TextBlock { Text = "The extras window could not be built: " + ex.Message, Foreground = Brushes.White });
        }
    }

    private Canvas Build()
    {
        var window = new LayoutControl("ExtrasWindow", "XNAWindow", LayoutControlKind.Panel)
        {
            Width = 284,
            Height = 190,
            BackgroundTexture = "extrasMenu.png",
        };

        ThemedWindow.Add(window, "btnExStatistics", "XNAClientButton", 76, 17, BUTTON_WIDTH_133, BUTTON_HEIGHT, "Statistics".L10N("Client:Main:Statistics"));
        ThemedWindow.Add(window, "btnExMapEditor", "XNAClientButton", 76, 59, BUTTON_WIDTH_133, BUTTON_HEIGHT, "Map Editor".L10N("Client:Main:MapEditor"));
        ThemedWindow.Add(window, "btnExCredits", "XNAClientButton", 76, 101, BUTTON_WIDTH_133, BUTTON_HEIGHT, "Credits".L10N("Client:Main:Credits"));
        ThemedWindow.Add(window, "btnExCancel", "XNAClientButton", 76, 160, BUTTON_WIDTH_133, BUTTON_HEIGHT, "Cancel".L10N("Client:Main:ButtonCancel"));

        Canvas canvas = ThemedWindow.Build(window, (name, _) =>
        {
            switch (name)
            {
                case "btnExStatistics":
                    viewModel.Statistics();
                    break;
                case "btnExMapEditor":
                    viewModel.MapEditor();
                    break;
                case "btnExCredits":
                    ExtrasViewModel.Credits();
                    break;
                case "btnExCancel":
                    viewModel.Cancel();
                    break;
            }
        }, new Dictionary<string, Func<LayoutControl, Control>>());

        canvas.HorizontalAlignment = HorizontalAlignment.Center;
        canvas.VerticalAlignment = VerticalAlignment.Center;
        return canvas;
    }
}
