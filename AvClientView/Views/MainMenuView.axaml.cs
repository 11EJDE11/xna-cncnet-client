using System;
using System.Diagnostics;

using Avalonia.Controls;
using Avalonia.Media;

using AvClientView.Theme;
using AvClientView.ViewModels;

using ClientLogic.Layout;
using ClientLogic.Updates;

using Rampastring.Tools;

namespace AvClientView.Views;

/// <summary>
/// The main menu, drawn from the theme's MainMenu.ini as the XNA main menu is: its background, the buttons it
/// creates in code (with their default textures) and the INI's extra controls.
/// </summary>
public partial class MainMenuView : UserControl
{
    // The XNA main menu's buttons and their default textures (MainMenu.cs)
    private static readonly (string Name, string Texture)[] Buttons =
    [
        ("btnNewCampaign", "campaign"),
        ("btnLoadGame", "loadmission"),
        ("btnSkirmish", "skirmish"),
        ("btnCnCNet", "cncnet"),
        ("btnLan", "lan"),
        ("btnOptions", "options"),
        ("btnMapEditor", "mapeditor"),
        ("btnStatistics", "statistics"),
        ("btnCredits", "credits"),
        ("btnExtras", "extras"),
        ("btnExit", "exitgame"),
    ];

    private LayoutControl layout;

    public MainMenuView()
    {
        InitializeComponent();
        DataContextChanged += (_, _) => Build();
    }

    private MainMenuViewModel ViewModel => DataContext as MainMenuViewModel;

    private void Build()
    {
        if (ViewModel == null)
            return;

        try
        {
            layout = CreateLayout();
            Canvas canvas = LayoutView.Build(layout, OnClick);

            if (LayoutView.FindNamed<TextBlock>(canvas, "lblVersion") is TextBlock version)
            {
                // ModMode disables version tracking and the updater, and the XNA menu doesn't add the labels
                version.IsVisible = UpdateStatus.IsEnabled;
                version.DataContext = ViewModel;
                version.Bind(TextBlock.TextProperty, new Avalonia.Data.Binding(nameof(MainMenuViewModel.Version)));

                // LblVersion_LeftClick: the changelog
                version.Cursor = new Avalonia.Input.Cursor(Avalonia.Input.StandardCursorType.Hand);
                version.IsHitTestVisible = true;
                version.PointerPressed += (_, _) => ClientCore.ProcessLauncher.StartShellProcess(ClientCore.ClientConfiguration.Instance.ChangelogURL);
            }

            if (LayoutView.FindNamed<TextBlock>(canvas, "lblUpdateStatus") is TextBlock updateStatus)
                BindUpdateStatus(updateStatus, ViewModel.UpdateStatus);

            if (LayoutView.FindNamed<TextBlock>(canvas, "lblCnCNetPlayerCount") is TextBlock playerCount)
            {
                // The view model outlives the view's DataContext (other pages replace this view), so keep it
                MainMenuViewModel viewModel = ViewModel;
                playerCount.DataContext = viewModel;
                playerCount.Bind(TextBlock.TextProperty, new Avalonia.Data.Binding(nameof(MainMenuViewModel.PlayerCount)));
            }

            MenuHost.Content = canvas;
        }
        catch (Exception ex)
        {
            Logger.Log("MainMenuView: building the themed main menu failed: " + ex);
            MenuHost.Content = new TextBlock { Text = "The theme's main menu could not be loaded: " + ex.Message };
        }
    }

    /// <summary>
    /// lblUpdateStatus, an XNALinkLabel: the status text, underlined while it can be clicked and DrawUnderline is set,
    /// the hover colour under the mouse (then the idle colour, not the INI's RemapColor, as XNALinkLabel does).
    /// </summary>
    private static void BindUpdateStatus(TextBlock label, UpdateStatus status)
    {
        label.IsVisible = UpdateStatus.IsEnabled;
        label.IsHitTestVisible = true;

        var hover = new SolidColorBrush(ThemeAssets.ButtonTextColor);
        var idle = new SolidColorBrush(ThemeAssets.LabelColor);

        void Refresh()
        {
            label.Text = status.Text;
            label.TextDecorations = status.IsClickable && status.IsUnderlined ? TextDecorations.Underline : null;
            label.Cursor = status.IsClickable ? new Avalonia.Input.Cursor(Avalonia.Input.StandardCursorType.Hand) : null;
        }

        void Changed(object sender, EventArgs e) => Refresh();
        label.AttachedToVisualTree += (_, _) => { status.Changed += Changed; Refresh(); };
        label.DetachedFromVisualTree += (_, _) => status.Changed -= Changed;

        label.PointerEntered += (_, _) =>
        {
            if (status.IsClickable)
                label.Foreground = hover;
        };
        label.PointerExited += (_, _) =>
        {
            if (status.IsClickable)
                label.Foreground = idle;
        };
        label.PointerPressed += (_, e) =>
        {
            if (status.IsClickable && e.GetCurrentPoint(label).Properties.IsLeftButtonPressed)
                status.Click();
        };
        Refresh();
    }

    private static LayoutControl CreateLayout()
    {
        var window = new LayoutControl("MainMenu", "MainMenu", LayoutControlKind.Panel)
        {
            BackgroundTexture = "MainMenu/mainmenubg.png",
        };

        if (ThemeAssets.TextureSize(window.BackgroundTexture) is (int width, int height))
        {
            window.Width = width;
            window.Height = height;
        }

        var reader = new XnaLayoutReader(ThemeAssets.TextureSize, ThemeAssets.RenderWidth, ThemeAssets.RenderHeight) { MeasureText = ThemeFonts.Measure };

        foreach ((string name, string texture) in Buttons)
        {
            var button = new LayoutControl(name, "XNAClientButton", LayoutControlKind.Button);
            button.Attributes["HoverSoundEffect"] = "MainMenu/button.wav";
            window.AddChild(button);
            reader.Apply(button, "IdleTexture", $"MainMenu/{texture}.png");
            reader.Apply(button, "HoverTexture", $"MainMenu/{texture}_c.png");
        }

        window.AddChild(new LayoutControl("lblCnCNetStatus", "XNALabel", LayoutControlKind.Label) { X = 12, Y = 9, Text = "Players on CnCNet:" });
        window.AddChild(new LayoutControl("lblCnCNetPlayerCount", "XNALabel", LayoutControlKind.Label) { Text = "-" });
        window.AddChild(new LayoutControl("lblVersion", "XNALinkLabel", LayoutControlKind.Label));
        window.AddChild(new LayoutControl("lblUpdateStatus", "XNALinkLabel", LayoutControlKind.Label) { Width = 160, Height = 20 });

        string iniPath = XnaLayoutReader.FindWindowIni("MainMenu");
        if (iniPath != null)
            reader.ReadWindow(new ClientCore.CCIniFile(iniPath), window);

        return window;
    }

    private void OnClick(string name)
    {
        LayoutControl button = layout?.Find(name);

        if (!string.IsNullOrEmpty(button?.Url) && button.Url.StartsWith("http", StringComparison.OrdinalIgnoreCase))
        {
            Process.Start(new ProcessStartInfo(button.Url) { UseShellExecute = true });
            return;
        }

        ViewModel?.Click(name, button?.Text);
    }
}
