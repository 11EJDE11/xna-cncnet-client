using System;
using System.Collections.Generic;
using System.Linq;

using Avalonia.Controls;
using Avalonia.Layout;

using AvClientView.Theme;

using ClientCore;
using ClientCore.Extensions;

using ClientLogic.Layout;

using Rampastring.Tools;

namespace AvClientView.Views;

/// <summary>
/// The XNA LoadingScreen: an 800 x 600 window with loadingscreen.png, or the theme's LoadingScreen.ini (its size,
/// background and draw mode, or a random one of RandomBackgroundTextures).
/// </summary>
public sealed class LoadingScreenView : UserControl
{
    private static readonly Random random = new();

    public LoadingScreenView()
    {
        try
        {
            Content = Build();
        }
        catch (Exception ex)
        {
            Logger.Log("LoadingScreenView: building the loading screen failed: " + ex);
        }
    }

    private static Canvas Build()
    {
        var window = new LayoutControl("LoadingScreen", "XNAWindow", LayoutControlKind.Panel)
        {
            Width = 800,
            Height = 600,
            BackgroundTexture = "loadingscreen.png",
        };

        string iniPath = XnaLayoutReader.FindWindowIni(window.Name);
        if (iniPath != null)
        {
            var ini = new CCIniFile(iniPath);
            ThemedWindow.CreateReader().ReadWindow(ini, window);

            List<string> randomTextures = ini.GetStringListValue(window.Name, "RandomBackgroundTextures", string.Empty).ToList();
            if (randomTextures.Count > 0)
                window.BackgroundTexture = randomTextures[random.Next(randomTextures.Count)];
        }

        Canvas canvas = ThemedWindow.Build(window, (_, _) => { }, new Dictionary<string, Func<LayoutControl, Control>>(), readIni: false);

        // CenterOnParent
        canvas.HorizontalAlignment = HorizontalAlignment.Center;
        canvas.VerticalAlignment = VerticalAlignment.Center;
        return canvas;
    }
}
