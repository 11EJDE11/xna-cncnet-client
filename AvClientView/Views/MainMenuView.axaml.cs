using System;
using System.Diagnostics;

using Avalonia.Controls;

using AvClientView.Theme;
using AvClientView.ViewModels;

using ClientLogic.Layout;

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
                version.Text = ViewModel.Version;

            if (LayoutView.FindNamed<TextBlock>(canvas, "lblCnCNetPlayerCount") is TextBlock playerCount)
            {
                playerCount.Text = ViewModel.PlayerCount;
                ViewModel.PropertyChanged += (_, e) =>
                {
                    if (e.PropertyName == nameof(MainMenuViewModel.PlayerCount))
                        playerCount.Text = ViewModel.PlayerCount;
                };
            }

            MenuHost.Content = canvas;
        }
        catch (Exception ex)
        {
            Logger.Log("MainMenuView: building the themed main menu failed: " + ex);
            MenuHost.Content = new TextBlock { Text = "The theme's main menu could not be loaded: " + ex.Message };
        }
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

        var reader = new XnaLayoutReader(ThemeAssets.TextureSize, 1280, 768);

        foreach ((string name, string texture) in Buttons)
        {
            var button = new LayoutControl(name, "XNAClientButton", LayoutControlKind.Button);
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
