using System;
using System.Collections.Generic;

using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;

using ClientCore.Extensions;

using ClientLogic.Layout;

namespace AvClientView.Theme;

/// <summary>
/// The XNA client's message box (ClientGUI's XNAMessageBox) over its own darkening panel: msgboxform.png, the caption
/// in font 1, a line, the description, and 75 px OK or Yes/No buttons (hotkeys Enter, Y and N).
/// </summary>
public static class ThemedMessageBox
{
    private const int BUTTON_WIDTH = 75;
    private const int BUTTON_HEIGHT = 23;

    /// <summary>Shows a message box on <paramref name="host"/>, above everything else.</summary>
    /// <param name="onYes">Null for an OK box.</param>
    public static void Show(Panel host, string caption, string description, Action onOk, Action onYes, Action onNo)
    {
        var previousFocus = TopLevel.GetTopLevel(host)?.FocusManager?.GetFocusedElement();
        var panel = new Panel
        {
            ZIndex = 20000,
            Focusable = true,
        };

        ThemedStyle.Darken(panel);

        void Close(Action then)
        {
            host.Children.Remove(panel);
            ThemedStyle.FadeOutDarkening(host, panel.ZIndex);
            // Another message box may be underneath this one. Return keyboard input to it (or the prior field).
            if (previousFocus is Control control && TopLevel.GetTopLevel(control) != null)
                control.Focus();
            then?.Invoke();
        }

        bool yesNo = onYes != null;
        Canvas box = Build(caption, description, yesNo, name =>
        {
            switch (name)
            {
                case "btnOK":
                    Close(onOk);
                    break;
                case "btnYes":
                    Close(onYes);
                    break;
                case "btnNo":
                    Close(onNo);
                    break;
            }
        });

        box.HorizontalAlignment = HorizontalAlignment.Center;
        box.VerticalAlignment = VerticalAlignment.Center;
        panel.Children.Add(box);

        panel.KeyDown += (_, e) =>
        {
            if (!yesNo && e.Key == Key.Enter)
                Close(onOk);
            else if (yesNo && e.Key == Key.Y)
                Close(onYes);
            else if (yesNo && e.Key == Key.N)
                Close(onNo);
            else
                return;

            e.Handled = true;
        };

        panel.AttachedToVisualTree += (_, _) => panel.Focus();
        host.Children.Add(panel);
    }

    private static Canvas Build(string caption, string description, bool yesNo, Action<string> onClick)
    {
        (int textWidth, int textHeight) = ThemeFonts.Measure(description, 0);

        var window = new LayoutControl("MessageBox", "XNAWindow", LayoutControlKind.Panel)
        {
            BackgroundTexture = "msgboxform.png",
            Width = textWidth + 24,
            Height = textHeight + 81,
        };

        LayoutControl lblCaption = ThemedWindow.Add(window, "lblCaption", "XNALabel", 12, 9, 0, 0, caption);
        lblCaption.FontIndex = 1;
        // An XNAPanel one pixel high: its border is the line
        ThemedWindow.Add(window, "line", "XNAPanel", 6, 29, window.Width - 12, 1).DrawBorders = true;
        ThemedWindow.Add(window, "lblDescription", "XNALabel", 12, 39, 0, 0, description);

        int y = window.Height - 28;
        if (yesNo)
        {
            int yesX = (window.Width - (BUTTON_WIDTH + 5) * 2) / 2;
            AddButton(window, "btnYes", yesX, y, "Yes".L10N("Client:ClientGUI:ButtonYes"));
            AddButton(window, "btnNo", yesX + BUTTON_WIDTH + 10, y, "No".L10N("Client:ClientGUI:ButtonNo"));
        }
        else
        {
            AddButton(window, "btnOK", (window.Width - BUTTON_WIDTH) / 2, y, "OK".L10N("Client:ClientGUI:ButtonOK"));
        }

        return ThemedWindow.Build(window, (name, _) => onClick(name), new Dictionary<string, Func<LayoutControl, Control>>());
    }

    /// <summary>XNAMessageBox's XNAButton: font 1, 75pxbtn.png, the button hover sound.</summary>
    private static void AddButton(LayoutControl window, string name, int x, int y, string text)
    {
        LayoutControl button = ThemedWindow.Add(window, name, "XNAButton", x, y, BUTTON_WIDTH, BUTTON_HEIGHT, text);
        button.FontIndex = 1;
        button.IdleTexture = "75pxbtn.png";
        button.HoverTexture = "75pxbtn_c.png";
        button.Attributes["HoverSoundEffect"] = "button.wav";
    }
}
