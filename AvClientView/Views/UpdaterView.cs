using System;
using System.Collections.Generic;

using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Shapes;
using Avalonia.Data;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;

using AvClientView.Theme;
using AvClientView.ViewModels;

using ClientCore.Extensions;

using ClientLogic.Layout;
using ClientLogic.Updates;

using Rampastring.Tools;

namespace AvClientView.Views;

/// <summary>
/// The XNA main menu's update windows, each over a darkening panel: UpdateQueryWindow, ManualUpdateQueryWindow and
/// UpdateWindow, with the theme's INIs applied.
/// </summary>
public sealed class UpdaterView : Panel
{
    private const double DOT_TIME = 0.66;
    private const int MAX_DOTS = 5;

    private readonly UpdaterViewModel viewModel;

    public UpdaterView(UpdaterViewModel viewModel)
    {
        this.viewModel = viewModel;
        DataContext = viewModel;

        Children.Add(Darkened(BuildQueryWindow, nameof(UpdaterViewModel.IsQueryOpen)));
        Children.Add(Darkened(BuildManualWindow, nameof(UpdaterViewModel.IsManualOpen)));
        Children.Add(Darkened(BuildUpdateWindow, nameof(UpdaterViewModel.IsUpdateOpen)));
    }

    private Panel Darkened(Func<Canvas> build, string isOpenPath)
    {
        var panel = new Panel { Background = new SolidColorBrush(Color.FromArgb(128, 0, 0, 0)), DataContext = viewModel };
        panel.Bind(IsVisibleProperty, new Binding(isOpenPath));

        try
        {
            Canvas canvas = build();
            canvas.HorizontalAlignment = HorizontalAlignment.Center;
            canvas.VerticalAlignment = VerticalAlignment.Center;
            panel.Children.Add(canvas);
        }
        catch (Exception ex)
        {
            Logger.Log("UpdaterView: building an update window failed: " + ex);
            panel.Children.Add(new TextBlock { Text = "The update window could not be built: " + ex.Message, Foreground = Brushes.White });
        }

        return panel;
    }

    private Canvas BuildQueryWindow()
    {
        var window = new LayoutControl("UpdateQueryWindow", "XNAWindow", LayoutControlKind.Panel)
        {
            Width = 251,
            Height = 140,
            BackgroundTexture = "updatequerybg.png",
        };

        ThemedWindow.Add(window, "lblDescription", "XNALabel", 12, 9, 0, 0);
        LayoutControl changelog = ThemedWindow.Add(window, "lblChangelogLink", "XNALinkLabel", 12, 50, 0, 0, "View Changelog".L10N("Client:Main:ViewChangeLog"));
        changelog.TextColor = new ClientLogic.UI.ChatColor(218, 165, 32); // Color.Goldenrod
        ThemedWindow.Add(window, "lblUpdateSize", "XNALabel", 12, 80, 0, 0);
        ThemedWindow.Add(window, "btnYes", "XNAClientButton", 12, 110, 75, 23, "Yes".L10N("Client:Main:ButtonYes"));
        ThemedWindow.Add(window, "btnNo", "XNAClientButton", 164, 110, 75, 23, "No".L10N("Client:Main:ButtonNo"));

        Canvas canvas = ThemedWindow.Build(window, (name, _) =>
        {
            if (name == "btnYes")
                viewModel.AcceptUpdate();
            else if (name == "btnNo")
                viewModel.DeclineUpdate();
        }, new Dictionary<string, Func<LayoutControl, Control>>());

        if (LayoutView.FindNamed<TextBlock>(canvas, "lblChangelogLink") is TextBlock link)
            MakeLink(link, UpdaterViewModel.ViewChangelog);

        BindText(canvas, "lblDescription", nameof(UpdaterViewModel.QueryText));
        BindText(canvas, "lblUpdateSize", nameof(UpdaterViewModel.SizeText));
        return canvas;
    }

    private Canvas BuildManualWindow()
    {
        var window = new LayoutControl("ManualUpdateQueryWindow", "XNAWindow", LayoutControlKind.Panel)
        {
            Width = 251,
            Height = 140,
            BackgroundTexture = "updatequerybg.png",
        };

        LayoutControl description = ThemedWindow.Add(window, "lblDescription", "XNALabel", 12, 9, 0, 0,
            ("Version {0} is available.\n\nManual download and installation is\nrequired.").L10N("Client:Main:ManualDownloadAvailable"));
        ThemedWindow.Add(window, "btnDownload", "XNAClientButton", 12, 110, 110, 23, "View Downloads".L10N("Client:Main:ButtonViewDownloads"));
        ThemedWindow.Add(window, "btnClose", "XNAClientButton", 147, 110, 92, 23, "Close".L10N("Client:Main:ButtonClose"));

        Canvas canvas = ThemedWindow.Build(window, (name, _) =>
        {
            if (name == "btnDownload")
                viewModel.ViewDownloads();
            else if (name == "btnClose")
                viewModel.CloseManual();
        }, new Dictionary<string, Func<LayoutControl, Control>>());

        // The text as loaded from the INI is the format of the shown text
        string format = description.Text;
        if (LayoutView.FindNamed<TextBlock>(canvas, "lblDescription") is TextBlock text)
        {
            void Update() => text.Text = string.Format(format, viewModel.ManualVersion);
            viewModel.PropertyChanged += (_, e) =>
            {
                if (e.PropertyName == nameof(UpdaterViewModel.ManualVersion))
                    Update();
            };
            Update();
        }

        return canvas;
    }

    private Canvas BuildUpdateWindow()
    {
        var window = new LayoutControl("UpdateWindow", "XNAWindow", LayoutControlKind.Panel)
        {
            Width = 446,
            Height = 270,
            BackgroundTexture = "updaterbg.png",
        };

        ThemedWindow.Add(window, "lblDescription", "XNALabel", 12, 9, 0, 0);
        ThemedWindow.Add(window, "lblCurrentFileProgressPercentage", "XNALabel", 12, 90, 0, 0,
            "Progress percentage of current file:".L10N("Client:Main:CurrentFileProgressPercentage"));
        ThemedWindow.Add(window, "lblCurrentFileProgressPercentageValue", "XNALabel", 409, 90, 0, 0, "0%");
        ThemedWindow.Add(window, "prgCurrentFile", "XNAProgressBar", 12, 110, 422, 30);
        ThemedWindow.Add(window, "lblCurrentFile", "XNALabel", 12, 142, 0, 0);
        ThemedWindow.Add(window, "lblTotalProgressPercentage", "XNALabel", 12, 170, 0, 0,
            "Total progress percentage:".L10N("Client:Main:TotalProgressPercentage"));
        ThemedWindow.Add(window, "lblTotalProgressPercentageValue", "XNALabel", 409, 170, 0, 0, "0%");
        ThemedWindow.Add(window, "prgTotal", "XNAProgressBar", 12, 190, 422, 30);
        ThemedWindow.Add(window, "lblUpdaterStatus", "XNALabel", 12, 240, 0, 0, "Preparing".L10N("Client:Main:StatusPreparing"));
        ThemedWindow.Add(window, "btnCancel", "XNAClientButton", 301, 240, 133, 23, "Cancel".L10N("Client:Main:ButtonCancel"));

        UpdateProgress progress = viewModel.Progress;
        var bars = new Dictionary<string, ProgressBarView>();

        Canvas canvas = ThemedWindow.Build(window, (name, _) =>
        {
            if (name == "btnCancel")
                viewModel.CancelUpdate();
        }, new Dictionary<string, Func<LayoutControl, Control>>
        {
            ["prgCurrentFile"] = layout => bars["prgCurrentFile"] = new ProgressBarView(layout.Width, layout.Height),
            ["prgTotal"] = layout => bars["prgTotal"] = new ProgressBarView(layout.Width, layout.Height),
        });

        TextBlock Find(string name) => LayoutView.FindNamed<TextBlock>(canvas, name);
        TextBlock description = Find("lblDescription");
        TextBlock fileValue = Find("lblCurrentFileProgressPercentageValue");
        TextBlock totalValue = Find("lblTotalProgressPercentageValue");
        TextBlock currentFile = Find("lblCurrentFile");
        TextBlock status = Find("lblUpdaterStatus");
        int dotCount = 0;

        void Refresh()
        {
            description?.SetValue(TextBlock.TextProperty, progress.Description);
            fileValue?.SetValue(TextBlock.TextProperty, progress.FilePercentage + "%");
            totalValue?.SetValue(TextBlock.TextProperty, progress.TotalPercentage + "%");
            currentFile?.SetValue(TextBlock.TextProperty, progress.CurrentFile);

            // XNA draws up to five dots after the status, one more every 0.66 seconds
            status?.SetValue(TextBlock.TextProperty, progress.Status + (dotCount > 0 ? " " + new string('.', dotCount) : string.Empty));

            if (bars.TryGetValue("prgCurrentFile", out ProgressBarView fileBar))
                fileBar.Value = progress.FilePercentage;
            if (bars.TryGetValue("prgTotal", out ProgressBarView totalBar))
                totalBar.Value = progress.TotalPercentage;
        }

        var dotTimer = new DispatcherTimer(TimeSpan.FromSeconds(DOT_TIME), DispatcherPriority.Background, (_, _) =>
        {
            dotCount = dotCount >= MAX_DOTS ? 0 : dotCount + 1;
            Refresh();
        });

        progress.Changed += (_, _) => Refresh();
        viewModel.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName != nameof(UpdaterViewModel.IsUpdateOpen))
                return;

            if (viewModel.IsUpdateOpen)
                dotTimer.Start();
            else
                dotTimer.Stop();
        };

        Refresh();
        return canvas;
    }

    private void BindText(Canvas canvas, string name, string path)
    {
        if (LayoutView.FindNamed<TextBlock>(canvas, name) is TextBlock text)
        {
            text.DataContext = viewModel;
            text.Bind(TextBlock.TextProperty, new Binding(path));
        }
    }

    /// <summary>An XNALinkLabel: underlined, the hover colour under the mouse, clickable.</summary>
    internal static void MakeLink(TextBlock link, Action onClick)
    {
        IBrush idle = link.Foreground;
        var hover = new SolidColorBrush(ThemeAssets.ButtonTextColor);
        link.IsHitTestVisible = true;
        link.Cursor = new Cursor(StandardCursorType.Hand);
        link.TextDecorations = TextDecorations.Underline;
        link.PointerEntered += (_, _) => link.Foreground = hover;
        link.PointerExited += (_, _) => link.Foreground = idle;
        link.PointerPressed += (_, e) =>
        {
            if (e.GetCurrentPoint(link).Properties.IsLeftButtonPressed)
                onClick();
        };
    }

    /// <summary>
    /// XNAProgressBar: a one-pixel border in the panel border colour, the filled part in the alternative colour and
    /// the rest in a third of it.
    /// </summary>
    private sealed class ProgressBarView : Border
    {
        private const int MAXIMUM = 100;

        private readonly Rectangle filled;
        private readonly double innerWidth;

        public ProgressBarView(int width, int height)
        {
            Width = width;
            Height = height;
            BorderThickness = new Thickness(1);
            BorderBrush = new SolidColorBrush(ThemeAssets.PanelBorderColor);

            Color filledColor = ThemeAssets.ButtonTextColor;
            Background = new SolidColorBrush(Color.FromArgb(filledColor.A, (byte)(filledColor.R / 3), (byte)(filledColor.G / 3), (byte)(filledColor.B / 3)));
            innerWidth = width - 2;
            filled = new Rectangle
            {
                Fill = new SolidColorBrush(filledColor),
                HorizontalAlignment = HorizontalAlignment.Left,
                Width = 0,
            };
            Child = filled;
        }

        public int Value
        {
            set => filled.Width = innerWidth * Math.Clamp(value, 0, MAXIMUM) / MAXIMUM;
        }
    }
}
