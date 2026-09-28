using System;
using System.Collections.Generic;
using System.Linq;

using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Controls.Templates;
using Avalonia.Data;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Media.Imaging;

using AvClientView.Theme;
using AvClientView.ViewModels;

using ClientCore;
using ClientCore.Extensions;

using ClientLogic.Layout;
using ClientLogic.Options;
using ClientLogic.Settings;

using Rampastring.Tools;

namespace AvClientView.Views;

/// <summary>
/// The XNA CampaignSelector over a darkening panel, laid out as its Initialize does, then with the theme's
/// CampaignSelector.ini (its campaign options and settings become the view model's options); and the CheaterScreen.
/// </summary>
public sealed class CampaignView : Panel
{
    private const int DEFAULT_WIDTH = 650;
    private const int DEFAULT_HEIGHT = 600;

    private readonly CampaignViewModel viewModel;
    private readonly Dictionary<string, Func<LayoutControl, Control>> overlays = [];
    private Control cheaterWindow;

    public CampaignView(CampaignViewModel viewModel)
    {
        this.viewModel = viewModel;
        ThemedStyle.Darken(this);
        IsVisible = false;

        viewModel.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(CampaignViewModel.IsOpen))
                IsVisible = viewModel.IsOpen;
            else if (e.PropertyName == nameof(CampaignViewModel.ShowCheaterWindow) && cheaterWindow != null)
                cheaterWindow.IsVisible = viewModel.ShowCheaterWindow;
        };

        try
        {
            Children.Add(BuildWindow());
            cheaterWindow = BuildCheaterWindow();
            cheaterWindow.IsVisible = false;
            Children.Add(cheaterWindow);
        }
        catch (Exception ex)
        {
            Logger.Log("CampaignView: building the campaign window failed: " + ex);
            Children.Add(new TextBlock { Text = "The campaign window could not be built: " + ex.Message, Foreground = Brushes.White });
        }
    }

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

    private Canvas BuildWindow()
    {
        var window = new LayoutControl("CampaignSelector", "XNAWindow", LayoutControlKind.Panel)
        {
            Width = DEFAULT_WIDTH,
            Height = DEFAULT_HEIGHT,
            BackgroundTexture = "missionselectorbg.png",
            DrawBorders = true,
        };

        LayoutControl lblSelectCampaign = Label(window, "lblSelectCampaign", 12, 12, "MISSIONS:".L10N("Client:Main:Missions"), 1);
        LayoutControl lbCampaignList = ThemedWindow.Add(window, "lbCampaignList", "XNAListBox", 12, Bottom(lblSelectCampaign) + 6, 300, 516);

        LayoutControl lblMissionDescriptionHeader = Label(window, "lblMissionDescriptionHeader", Right(lbCampaignList) + 12, lblSelectCampaign.Y,
            "MISSION DESCRIPTION:".L10N("Client:Main:MissionDescription"), 1);
        bool previewEnabled = CampaignViewModel.MissionPreviewEnabled;
        LayoutControl tbMissionDescription = ThemedWindow.Add(window, "tbMissionDescription", "XNATextBlock", lblMissionDescriptionHeader.X,
            Bottom(lblMissionDescriptionHeader) + 6, window.Width - 24 - Right(lbCampaignList), previewEnabled ? 430 - 200 - 12 : 430);

        string difficultyText = "DIFFICULTY LEVEL".L10N("Client:Main:DifficultyLevel");
        (int difficultyWidth, _) = Measure(difficultyText, 1);
        LayoutControl lblDifficultyLevel = Label(window, "lblDifficultyLevel", tbMissionDescription.X + (tbMissionDescription.Width - difficultyWidth) / 2,
            Bottom(tbMissionDescription) + 12, difficultyText, 1);

        LayoutControl trbDifficultySelector = ThemedWindow.Add(window, "trbDifficultySelector", "XNATrackbar", tbMissionDescription.X,
            Bottom(lblDifficultyLevel) + 6, tbMissionDescription.Width, 30);

        LayoutControl lblEasy = Label(window, "lblEasy", trbDifficultySelector.X, Bottom(trbDifficultySelector) + 6, "EASY".L10N("Client:Main:DifficultyEasy"), 1);
        string normalText = "NORMAL".L10N("Client:Main:DifficultyNormal");
        Label(window, "lblNormal", tbMissionDescription.X + (tbMissionDescription.Width - Measure(normalText, 1).Width) / 2, lblEasy.Y, normalText, 1);
        string hardText = "HARD".L10N("Client:Main:DifficultyHard");
        Label(window, "lblHard", Right(tbMissionDescription) - Measure(hardText, 1).Width, lblEasy.Y, hardText, 1);

        LayoutControl btnLaunch = ThemedWindow.Add(window, "btnLaunch", "XNAClientButton", 12, window.Height - 35, 133, 23, "Launch".L10N("Client:Main:ButtonLaunch"));
        ThemedWindow.Add(window, "btnCancel", "XNAClientButton", window.Width - 145, btnLaunch.Y, 133, 23, "Cancel".L10N("Client:Main:ButtonCancel"));

        // With the campaign tag selector: Campaigns, disabled (so hidden) unless the theme enables it
        if (ClientConfiguration.Instance.CampaignTagSelectorEnabled)
        {
            LayoutControl btnReturn = ThemedWindow.Add(window, "btnReturn", "XNAClientButton", trbDifficultySelector.X, btnLaunch.Y, 133, 23,
                "Campaigns".L10N("Client:Main:ButtonReturnToCampaigns"));
            btnReturn.Visible = btnReturn.Enabled = false;
        }

        if (previewEnabled)
            ThemedWindow.Add(window, "pnlMissionPreview", "XNAPanel", tbMissionDescription.X, Bottom(tbMissionDescription) + 12, tbMissionDescription.Width, 200);

        XnaLayoutReader reader = ThemedWindow.CreateReader();
        string iniPath = XnaLayoutReader.FindWindowIni(window.Name);
        if (iniPath != null)
            reader.ReadWindow(new CCIniFile(iniPath), window);

        foreach (LayoutControl control in ThemedWindow.All(window))
            reader.Initialize(control);

        AddThemeOptions(window);

        overlays["lbCampaignList"] = MissionList;
        overlays["tbMissionDescription"] = DescriptionBlock;
        overlays["trbDifficultySelector"] = DifficultyTrackbar;
        if (previewEnabled)
            overlays["pnlMissionPreview"] = MissionPreview;

        Canvas canvas = ThemedWindow.Build(window, (name, _) => OnButton(name), overlays, readIni: false);
        canvas.HorizontalAlignment = HorizontalAlignment.Center;
        canvas.VerticalAlignment = VerticalAlignment.Center;
        canvas.DataContext = viewModel;
        canvas.Bind(IsEnabledProperty, new Binding(nameof(CampaignViewModel.ControlsEnabled)));

        if (LayoutView.FindNamed<ThemedButton>(canvas, "btnLaunch") is ThemedButton launch)
        {
            launch.DataContext = viewModel;
            launch.Bind(IsEnabledProperty, new Binding(nameof(CampaignViewModel.CanLaunch)));
        }

        return canvas;
    }

    /// <summary>
    /// The theme's controls that are campaign options (CampaignCheckBox / CampaignDropDown, as GameOptions) or user
    /// settings (SettingCheckBox etc.).
    /// </summary>
    private void AddThemeOptions(LayoutControl window)
    {
        foreach (LayoutControl control in window.Children.ToList())
        {
            switch (control.TypeName)
            {
                case "CampaignCheckBox":
                {
                    GameOption option = viewModel.AddOption(control.Name, true, control.Text, control.Attributes, window.Name);
                    overlays[control.Name] = layout =>
                    {
                        var checkBox = new ThemedCheckBox(layout.Text, layout.FontIndex) { DataContext = new OptionBinding(option) };
                        checkBox.Bind(ThemedCheckBox.IsCheckedProperty, new Binding(nameof(OptionBinding.IsChecked)) { Mode = BindingMode.TwoWay });
                        if (!string.IsNullOrEmpty(layout.ToolTip))
                            ToolTip.SetTip(checkBox, layout.ToolTip);
                        return checkBox;
                    };
                    break;
                }
                case "CampaignDropDown":
                {
                    GameOption option = viewModel.AddOption(control.Name, false, control.Text, control.Attributes, window.Name);
                    overlays[control.Name] = layout =>
                    {
                        var dropDown = new ThemedDropDown(layout.Width, 22, layout.FontIndex)
                        {
                            DataContext = new OptionBinding(option),
                            ItemsSource = option.Definition.Items.Select(i => i.Label).ToList(),
                        };
                        dropDown.Bind(ThemedDropDown.SelectedIndexProperty, new Binding(nameof(OptionBinding.Index)) { Mode = BindingMode.TwoWay });
                        if (!string.IsNullOrEmpty(layout.ToolTip))
                            ToolTip.SetTip(dropDown, layout.ToolTip);
                        return dropDown;
                    };
                    break;
                }
                case "SettingCheckBox" or "FileSettingCheckBox":
                {
                    var setting = new CheckBoxSetting(control.Name, control.TypeName == "FileSettingCheckBox");
                    foreach (KeyValuePair<string, string> attribute in control.Attributes)
                        setting.TryParse(attribute.Key, attribute.Value);
                    viewModel.UserSettings.Add(setting);
                    overlays[control.Name] = layout =>
                    {
                        var checkBox = new ThemedCheckBox(layout.Text, layout.FontIndex) { DataContext = setting };
                        checkBox.Bind(ThemedCheckBox.IsCheckedProperty, new Binding(nameof(CheckBoxSetting.IsChecked)) { Mode = BindingMode.TwoWay });
                        checkBox.Bind(IsEnabledProperty, new Binding(nameof(CheckBoxSetting.AllowChecking)));
                        return checkBox;
                    };
                    break;
                }
                case "SettingDropDown" or "FileSettingDropDown":
                {
                    var setting = new DropDownSetting(control.Name, window.Name, control.TypeName == "FileSettingDropDown");
                    foreach (KeyValuePair<string, string> attribute in control.Attributes)
                        setting.TryParse(attribute.Key, attribute.Value);
                    viewModel.UserSettings.Add(setting);
                    overlays[control.Name] = layout =>
                    {
                        var dropDown = new ThemedDropDown(layout.Width, 22, layout.FontIndex)
                        {
                            DataContext = setting,
                            ItemsSource = setting.Items.Select(i => i.Text).ToList(),
                        };
                        dropDown.Bind(ThemedDropDown.SelectedIndexProperty, new Binding(nameof(DropDownSetting.SelectedIndex)) { Mode = BindingMode.TwoWay });
                        return dropDown;
                    };
                    break;
                }
            }
        }
    }

    /// <summary>Two-way binding helpers for a GameOption (check-box state or drop-down index).</summary>
    private sealed class OptionBinding : System.ComponentModel.INotifyPropertyChanged
    {
        private readonly GameOption option;

        public OptionBinding(GameOption option)
        {
            this.option = option;
            option.PropertyChanged += (_, e) =>
            {
                if (e.PropertyName == nameof(GameOption.Value))
                {
                    PropertyChanged?.Invoke(this, new(nameof(IsChecked)));
                    PropertyChanged?.Invoke(this, new(nameof(Index)));
                }
            };
        }

        public event System.ComponentModel.PropertyChangedEventHandler PropertyChanged;

        public bool IsChecked
        {
            get => option.IsChecked;
            set => option.Value = value ? 1 : 0;
        }

        public int Index
        {
            get => option.Value;
            set => option.Value = value;
        }
    }

    private void OnButton(string name)
    {
        switch (name)
        {
            case "btnLaunch":
                viewModel.Launch();
                break;
            case "btnCancel":
                viewModel.Cancel();
                break;
            case "btnReturn":
                viewModel.Return();
                break;
        }
    }

    /// <summary>The mission list: the missions' icons, headers in ListBoxHeaderColor (not selectable), disabled ones grey.</summary>
    private Control MissionList(LayoutControl layout)
    {
        ListBox list = ThemedStyle.List(layout.Width, layout.Height);
        list.Background = new SolidColorBrush(Color.FromArgb(128, 0, 0, 0));
        list.DataContext = viewModel;
        list.Bind(ItemsControl.ItemsSourceProperty, new Binding(nameof(CampaignViewModel.Missions)));
        list.Bind(SelectingItemsControl.SelectedIndexProperty, new Binding(nameof(CampaignViewModel.SelectedIndex)) { Mode = BindingMode.TwoWay });
        list.Resources["ListBoxItemPadding"] = new Thickness(0);

        var headerBrush = new SolidColorBrush(ThemeAssets.ParseColor(ClientConfiguration.Instance.ListBoxHeaderColor, Colors.White));
        var disabledBrush = new SolidColorBrush(ThemeAssets.DisabledItemColor);
        var defaultBrush = new SolidColorBrush(ThemeAssets.ButtonTextColor);

        list.ItemTemplate = new FuncDataTemplate<MissionItemViewModel>((item, _) =>
        {
            var row = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 2, Margin = new Thickness(2, 1) };
            if (item?.IconName != null && ThemeAssets.LoadBitmap(item.IconName) is Bitmap icon)
                row.Children.Add(new Image { Source = icon, Stretch = Stretch.None, VerticalAlignment = VerticalAlignment.Center });

            row.Children.Add(new TextBlock
            {
                Text = item?.Text,
                VerticalAlignment = VerticalAlignment.Center,
                Foreground = item == null ? defaultBrush : !item.Enabled ? disabledBrush : item.IsHeader ? headerBrush : defaultBrush,
            });
            return row;
        });

        // Headers can't be selected
        list.SelectionChanged += (_, _) =>
        {
            if (list.SelectedItem is MissionItemViewModel { IsHeader: true })
                list.SelectedIndex = -1;
        };

        return list;
    }

    /// <summary>XNATextBlock: the description on its background, wrapped.</summary>
    private Control DescriptionBlock(LayoutControl layout)
    {
        (FontFamily family, double size) = ThemeFonts.Get(layout.FontIndex);
        var text = new TextBlock
        {
            FontFamily = family,
            FontSize = size,
            Foreground = new SolidColorBrush(ThemeAssets.LabelColor),
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(4, 2),
            DataContext = viewModel,
        };
        text.Bind(TextBlock.TextProperty, new Binding(nameof(CampaignViewModel.Description)));

        IBrush background = ThemeAssets.LoadBitmap(layout.BackgroundTexture) is Bitmap bitmap
            ? new ImageBrush(bitmap) { Stretch = Stretch.Fill }
            : new SolidColorBrush(ThemeAssets.ParseColor(ClientConfiguration.Instance.AltUIBackgroundColor, Color.FromRgb(196, 196, 196)));

        return new Border
        {
            Width = layout.Width,
            Height = layout.Height,
            Background = background,
            BorderBrush = new SolidColorBrush(ThemeAssets.PanelBorderColor),
            BorderThickness = new Thickness(1),
            Child = new ScrollViewer { Content = text, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled },
        };
    }

    private Control DifficultyTrackbar(LayoutControl layout)
    {
        string clickSound = layout.Attributes.TryGetValue("ClickSound", out string sound) ? sound : null;
        var trackbar = new ThemedTrackbar(layout.Width, layout.Height > 0 ? layout.Height : 30, 0, 2, "trackbarButton_difficulty.png",
            clickSound == null ? null : ThemeSound.Load(clickSound)) { DataContext = viewModel };

        if (ThemeAssets.LoadBitmap(layout.BackgroundTexture) is Bitmap background)
            trackbar.Background = new ImageBrush(background) { Stretch = Stretch.Fill };

        trackbar.Bind(ThemedTrackbar.ValueProperty, new Binding(nameof(CampaignViewModel.Difficulty)) { Mode = BindingMode.TwoWay });
        return trackbar;
    }

    /// <summary>The mission preview, letterboxed on the alt background colour (CreateLetterboxedTexture).</summary>
    private Control MissionPreview(LayoutControl layout)
    {
        var image = new Image { Stretch = Stretch.Uniform, Width = layout.Width, Height = layout.Height };
        var border = new Border
        {
            Width = layout.Width,
            Height = layout.Height,
            Background = new SolidColorBrush(ThemeAssets.ParseColor(ClientConfiguration.Instance.AltUIBackgroundColor, Color.FromRgb(196, 196, 196))),
            Child = image,
        };

        void Refresh()
        {
            try
            {
                image.Source = string.IsNullOrEmpty(viewModel.PreviewPath) ? null : new Bitmap(viewModel.PreviewPath);
            }
            catch (Exception ex)
            {
                Logger.Log("CampaignView: mission preview failed: " + ex.Message);
                image.Source = null;
            }
        }

        viewModel.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(CampaignViewModel.PreviewPath))
                Refresh();
        };
        Refresh();
        return border;
    }

    /// <summary>The CheaterScreen: "CHEATER!", the warning, cheater.png, Yes and Cancel.</summary>
    private Control BuildCheaterWindow()
    {
        var window = new LayoutControl("CheaterScreen", "XNAWindow", LayoutControlKind.Panel)
        {
            Width = 334,
            Height = 453,
            BackgroundTexture = "cheaterbg.png",
            DrawBorders = true,
        };

        string title = "CHEATER!".L10N("Client:Main:Cheater");
        LayoutControl lblCheater = Label(window, "lblCheater", 0, 12, title, 1);
        lblCheater.X = (window.Width - lblCheater.Width) / 2;

        LayoutControl lblDescription = Label(window, "lblDescription", 12, 40, ("Modified game files have been detected. They could affect\n" +
            "the game experience.\n\n" +
            "Do you really lack the skill for winning the mission without\ncheating?").L10N("Client:Main:CheaterText"));

        var imagePanel = ThemedWindow.Add(window, "imagePanel", "XNAPanel", lblDescription.X, Bottom(lblDescription) + 12,
            window.Width - 24, window.Height - (Bottom(lblDescription) + 59));
        imagePanel.BackgroundTexture = "cheater.png";

        LayoutControl btnCancel = ThemedWindow.Add(window, "btnCancel", "XNAClientButton", window.Width - 104, window.Height - 35, 92, 23, "Cancel".L10N("Client:Main:ButtonCancel"));
        ThemedWindow.Add(window, "btnYes", "XNAClientButton", 12, btnCancel.Y, btnCancel.Width, btnCancel.Height, "Yes".L10N("Client:Main:ButtonYes"));

        Canvas canvas = ThemedWindow.Build(window, (name, _) =>
        {
            if (name == "btnYes")
                viewModel.CheaterYes();
            else if (name == "btnCancel")
                viewModel.CheaterCancel();
        }, new Dictionary<string, Func<LayoutControl, Control>>());

        canvas.HorizontalAlignment = HorizontalAlignment.Center;
        canvas.VerticalAlignment = VerticalAlignment.Center;
        return new Panel { Background = new SolidColorBrush(Color.FromArgb(128, 0, 0, 0)), Children = { canvas } };
    }
}
