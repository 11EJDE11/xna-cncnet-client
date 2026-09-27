using System;

using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;

using AvClientView.Theme;

using ClientCore;
using ClientCore.Extensions;

using ClientLogic.Layout;

namespace AvClientView.Views;

/// <summary>
/// The XNA PrivacyNotification: along the bottom of the client until "Got it" is clicked, when the privacy policy
/// hasn't been accepted (PrivacyPolicyAccepted).
/// </summary>
public sealed class PrivacyNotificationView : Border
{
    private const int EMPTY_SPACE = 6;
    private const int MARGIN = 6;

    public PrivacyNotificationView()
    {
        VerticalAlignment = VerticalAlignment.Bottom;
        HorizontalAlignment = HorizontalAlignment.Stretch;
        Background = ThemedStyle.PanelBackground;
        BorderBrush = new SolidColorBrush(ThemeAssets.PanelBorderColor);
        BorderThickness = new Thickness(1);
        IsVisible = !UserINISettings.Instance.PrivacyPolicyAccepted;

        (FontFamily family, double size) = ThemeFonts.Get(0);
        IBrush labelBrush = new SolidColorBrush(ThemeAssets.LabelColor);

        TextBlock Label(string text, IBrush brush = null) => new()
        {
            Text = text,
            FontFamily = family,
            FontSize = size,
            Foreground = brush ?? labelBrush,
            TextWrapping = TextWrapping.Wrap,
        };

        Control Link(string url)
        {
            TextBlock link = Label(url, new SolidColorBrush(ThemeAssets.ButtonTextColor));
            link.TextWrapping = TextWrapping.NoWrap;
            link.Cursor = new Cursor(StandardCursorType.Hand);
            link.Margin = new Thickness(MARGIN, 0, 0, 0);
            link.PointerEntered += (_, _) => link.TextDecorations = TextDecorations.Underline;
            link.PointerExited += (_, _) => link.TextDecorations = null;
            link.PointerPressed += (_, _) => ProcessLauncher.StartShellProcess(url);
            return link;
        }

        var ok = ThemedButton("btnOK", "Got it".L10N("Client:Main:TOSButtonOK"));
        ok.Click += (_, _) =>
        {
            UserINISettings.Instance.PrivacyPolicyAccepted.Value = true;
            UserINISettings.Instance.SaveSettings();
            IsVisible = false;
        };

        var explanationRow = new DockPanel { Margin = new Thickness(0, MARGIN * 2, 0, 0) };
        DockPanel.SetDock(ok, Dock.Right);
        ok.VerticalAlignment = VerticalAlignment.Top;
        explanationRow.Children.Add(ok);
        explanationRow.Children.Add(Label(
            "By using this application you agree to the CnCNet Terms & Conditions as well as the CnCNet Privacy Policy. Privacy-related options can be configured in the client settings.".L10N("Client:Main:TOSExplanation"),
            new SolidColorBrush(ThemeAssets.ParseColor(ClientConfiguration.Instance.UIHintTextColor, Colors.Gray))));

        Child = new StackPanel
        {
            Margin = new Thickness(EMPTY_SPACE, EMPTY_SPACE, MARGIN, EMPTY_SPACE),
            Children =
            {
                Label("This application makes use of CnCNet web & tunnel server services and is subject to collection of technical & other necessary information through them.".L10N("Client:Main:TOSText")),
                new StackPanel
                {
                    Orientation = Orientation.Horizontal,
                    Margin = new Thickness(0, MARGIN, 0, 0),
                    Children =
                    {
                        Label("More information:".L10N("Client:Main:TOSMoreInfo") + " "),
                        Link("https://cncnet.org/terms-and-conditions"),
                        Link("https://cncnet.org/privacy-policy"),
                    },
                },
                explanationRow,
            },
        };
    }

    private static ThemedButton ThemedButton(string name, string text)
    {
        var layout = new LayoutControl(name, "XNAClientButton", LayoutControlKind.Button) { Width = 75, Height = 23, Text = text };
        ThemedWindow.CreateReader().Initialize(layout);
        return new ThemedButton(layout);
    }
}
