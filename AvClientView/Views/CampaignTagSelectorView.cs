using System;
using System.Collections.Generic;
using System.IO;

using Avalonia.Controls;
using Avalonia.Data;
using Avalonia.Layout;
using Avalonia.Media;

using AvClientView.Theme;
using AvClientView.ViewModels;

using ClientCore;

using ClientLogic.Layout;

using Rampastring.Tools;

namespace AvClientView.Views;

/// <summary>
/// The XNA CampaignTagSelector over a darkening panel: an INItializableWindow (576 x 475) whose buttons all come from
/// the theme's CampaignTagSelector.ini (btnCancel, btnShowAllMission, ButtonTag_{tag}).
/// </summary>
public sealed class CampaignTagSelectorView : Panel
{
    private const string TAG_BUTTONS_PREFIX = "ButtonTag_";

    private readonly CampaignTagSelectorViewModel viewModel;

    public CampaignTagSelectorView(CampaignTagSelectorViewModel viewModel)
    {
        this.viewModel = viewModel;
        Background = new SolidColorBrush(Color.FromArgb(128, 0, 0, 0));
        DataContext = viewModel;
        this.Bind(IsVisibleProperty, new Binding(nameof(CampaignTagSelectorViewModel.IsOpen)));

        if (!CampaignTagSelectorViewModel.IsEnabled)
            return;

        try
        {
            Children.Add(Build());
        }
        catch (Exception ex)
        {
            Logger.Log("CampaignTagSelectorView: building the campaign tag selector failed: " + ex);
            Children.Add(new TextBlock { Text = "The campaign tag selector could not be built: " + ex.Message, Foreground = Brushes.White });
        }
    }

    private Canvas Build()
    {
        var window = new LayoutControl("CampaignTagSelector", "INItializableWindow", LayoutControlKind.Panel)
        {
            Width = 576,
            Height = 475,
            DrawBorders = true,
        };

        // INItializableWindow reads only its own INI
        string iniPath = XnaLayoutReader.FindWindowIni(window.Name);
        if (iniPath == null || !Path.GetFileName(iniPath).Equals(window.Name + ".ini", StringComparison.OrdinalIgnoreCase))
            throw new FileNotFoundException("CampaignTagSelector.ini wasn't found in the theme.");

        XnaLayoutReader reader = ThemedWindow.CreateReader();
        Dictionary<string, int> constants = LayoutExpressionParser.ClientConstants(ThemeAssets.RenderWidth, ThemeAssets.RenderHeight,
            ClientConfiguration.Instance.GetParserConstants());
        reader.ReadInitializableWindow(new CCIniFile(iniPath), window, constants);

        foreach (LayoutControl control in ThemedWindow.All(window))
            reader.Initialize(control);

        Canvas canvas = ThemedWindow.Build(window, (name, layout) =>
        {
            if (name == "btnCancel")
                viewModel.Cancel();
            else if (name == "btnShowAllMission")
                viewModel.ShowAllMissions();
            else if (name.StartsWith(TAG_BUTTONS_PREFIX, StringComparison.Ordinal) && layout?.Enabled != false)
                viewModel.ShowTag(name.Substring(TAG_BUTTONS_PREFIX.Length));
        }, new Dictionary<string, Func<LayoutControl, Control>>(), readIni: false);

        canvas.HorizontalAlignment = HorizontalAlignment.Center;
        canvas.VerticalAlignment = VerticalAlignment.Center;
        return canvas;
    }
}
