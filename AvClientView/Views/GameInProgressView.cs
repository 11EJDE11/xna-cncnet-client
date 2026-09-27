using System;
using System.Collections.Generic;

using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;

using AvClientView.Theme;

using ClientCore.Extensions;

using ClientLogic.Launch;
using ClientLogic.Layout;

using Rampastring.Tools;

namespace AvClientView.Views;

/// <summary>
/// The XNA GameInProgressWindow's look: "A game is in progress." in a 200×100 window (gameinprogresswindowbg.png, the
/// theme's GameInProgressWindow.ini) over a darkening panel that covers the client while a game runs.
/// </summary>
public sealed class GameInProgressView : Panel
{
    public GameInProgressView(GameInProgressTracker tracker)
    {
        Background = new SolidColorBrush(Color.FromArgb(128, 0, 0, 0));
        IsVisible = false;

        tracker.GameStarted += (_, _) => IsVisible = true;
        tracker.GameExited += (_, _) => IsVisible = false;

        try
        {
            Children.Add(Build());
        }
        catch (Exception ex)
        {
            Logger.Log("GameInProgressView: building the window failed: " + ex);
        }
    }

    private static Canvas Build()
    {
        var window = new LayoutControl("GameInProgressWindow", "XNAWindow", LayoutControlKind.Panel)
        {
            Width = 200,
            Height = 100,
            BackgroundTexture = "gameinprogresswindowbg.png",
        };

        // The label is centred on the window (CenterOnParent after the INI is read)
        string text = "A game is in progress.".L10N("Client:Main:GameInProgress");
        (int width, int height) = ThemeFonts.Measure(text, 0);
        ThemedWindow.Add(window, "lblExplanation", "XNALabel", (window.Width - width) / 2, (window.Height - height) / 2, width, height, text);

        Canvas canvas = ThemedWindow.Build(window, (_, _) => { }, new Dictionary<string, Func<LayoutControl, Control>>());
        canvas.HorizontalAlignment = HorizontalAlignment.Center;
        canvas.VerticalAlignment = VerticalAlignment.Center;
        return canvas;
    }
}
