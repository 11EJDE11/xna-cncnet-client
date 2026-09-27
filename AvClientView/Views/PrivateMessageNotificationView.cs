using System.ComponentModel;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using AvClientView.Theme;
using AvClientView.ViewModels;
using ClientCore;
using ClientCore.Extensions;
using DTAClient.Domain.Multiplayer.CnCNet;
using Microsoft.Extensions.DependencyInjection;

namespace AvClientView.Views;

/// <summary>The XNA PM notification: sender, game icon, clipped message and click-to-view hint.</summary>
public sealed class PrivateMessageNotificationView : Border
{
    public PrivateMessageNotificationView(PrivateMessagesViewModel model)
    {
        Width = 300;
        Height = 100;
        HorizontalAlignment = HorizontalAlignment.Right;
        VerticalAlignment = VerticalAlignment.Top;
        Background = new SolidColorBrush(Color.FromArgb(196, 0, 0, 0));
        BorderBrush = new SolidColorBrush(ThemeAssets.PanelBorderColor);
        BorderThickness = new Thickness(1);
        IsVisible = false;
        var canvas = new Canvas();
        Child = canvas;
        TextBlock Label(string text, int font, double x, double y)
        {
            var (family, size) = ThemeFonts.Get(font);
            var label = new TextBlock { Text = text, FontFamily = family, FontSize = size, Foreground = new SolidColorBrush(ThemeAssets.LabelColor) };
            Canvas.SetLeft(label, x);
            Canvas.SetTop(label, y);
            canvas.Children.Add(label);
            return label;
        }
        string headerText = "PRIVATE MESSAGE".L10N("Client:Main:PMHeader");
        Label(headerText, 1, (Width - ThemeFonts.Measure(headerText, 1).Width) / 2, 6);
        var icon = new Image { Width = 16, Height = 16 };
        Canvas.SetLeft(icon, 12);
        Canvas.SetTop(icon, 30);
        canvas.Children.Add(icon);
        var sender = Label(string.Empty, 1, 31, 30);
        var message = Label(string.Empty, 0, 12, 30 + ThemeFonts.Measure("H", 1).Height + 6);
        message.Width = Width - 12;
        message.TextTrimming = TextTrimming.CharacterEllipsis;
        message.Foreground = new SolidColorBrush(ThemeAssets.ParseColor(ClientConfiguration.Instance.ReceivedPMColor, Colors.White));
        var line = new Border { Width = Width, Height = 1, Background = BorderBrush };
        Canvas.SetTop(line, 80);
        canvas.Children.Add(line);
        string hint = "Click to view".L10N("Client:Main:ClickToView");
        Label(hint, 0, (Width - ThemeFonts.Measure(hint, 0).Width) / 2, 83);
        void Refresh(object source, PropertyChangedEventArgs e)
        {
            if (e.PropertyName != nameof(model.Notification))
                return;
            sender.Text = model.NotificationSender + ":";
            message.Text = model.Notification;
            var games = App.Services.GetRequiredService<GameCollection>().GameList;
            int id = model.NotificationGameId;
            icon.Source = id >= 0 && id < games.Count ? ThemeAssets.GameIcon(games[id]) : ThemeAssets.EmbeddedIcon("unknownicon.png");
            IsVisible = model.Notification != null;
        }
        AttachedToVisualTree += (_, _) => model.PropertyChanged += Refresh;
        DetachedFromVisualTree += (_, _) => model.PropertyChanged -= Refresh;
        PointerPressed += (_, e) => { if (e.GetCurrentPoint(this).Properties.IsLeftButtonPressed) model.Open(); };
        PointerMoved += (_, _) => model.KeepNotificationVisible();
    }
}
