using System;

using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;

using AvClientView.Theme;

using ClientLogic.UI;

using Microsoft.Extensions.DependencyInjection;

namespace AvClientView.Services;

/// <summary>Runs actions on Avalonia's UI thread.</summary>
public sealed class AvaloniaUiDispatcher : IUiDispatcher
{
    public void Post(Action action) => Dispatcher.UIThread.Post(action);

    public bool CheckAccess() => Dispatcher.UIThread.CheckAccess();
}

/// <summary>
/// Shows message boxes as the XNA client does, themed over the main window's content (<see cref="ThemedMessageBox"/>);
/// as small modal windows if the main window has no content panel yet.
/// </summary>
public sealed class AvaloniaDialogService(Window owner) : IDialogService
{
    public void ShowMessage(string title, string text) => Show(title, text, null);

    public void ShowMessage(string title, string text, Action onOk) => Show(title, text, null, onOk);

    public void Confirm(string title, string text, Action onYes) => Show(title, text, onYes);

    public void Confirm(string title, string text, Action onYes, Action onNo) => Show(title, text, onYes, onNo: onNo);

    private void Show(string title, string text, Action onYes, Action onOk = null, Action onNo = null)
    {
        if (owner.Content is Panel host)
        {
            ThemedMessageBox.Show(host, title, text, onOk, onYes, onNo);
            return;
        }

        var dialog = new Window
        {
            Title = title,
            Width = 440,
            SizeToContent = SizeToContent.Height,
            CanResize = false,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
        };

        var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Spacing = 8 };

        if (onYes == null)
        {
            var ok = new Button { Content = "OK", IsDefault = true };
            ok.Click += (_, _) =>
            {
                dialog.Close();
                onOk?.Invoke();
            };
            buttons.Children.Add(ok);
        }
        else
        {
            var yes = new Button { Content = "Yes", IsDefault = true };
            var no = new Button { Content = "No", IsCancel = true };
            yes.Click += (_, _) =>
            {
                dialog.Close();
                onYes();
            };
            no.Click += (_, _) =>
            {
                dialog.Close();
                onNo?.Invoke();
            };
            buttons.Children.Add(yes);
            buttons.Children.Add(no);
        }

        dialog.Content = new StackPanel
        {
            Margin = new Thickness(16),
            Spacing = 16,
            Children = { new TextBlock { Text = text, TextWrapping = TextWrapping.Wrap }, buttons },
        };

        dialog.ShowDialog(owner);
    }
}

/// <summary>Plays no sounds (the preview has no audio yet).</summary>
public sealed class NullSoundService : ISoundService
{
    public void Play(LobbySound sound)
    {
    }

    public void SetEnabled(LobbySound sound, bool enabled)
    {
    }
}

public static class AvaloniaFrontEndServiceCollectionExtensions
{
    /// <summary>Registers the Avalonia front end's UI services. Call before <c>AddClientLogic()</c>.</summary>
    public static IServiceCollection AddAvaloniaFrontEnd(this IServiceCollection services, Window mainWindow) => services
        .AddSingleton<IUiDispatcher>(new AvaloniaUiDispatcher())
        .AddSingleton<IDialogService>(new AvaloniaDialogService(mainWindow))
        .AddSingleton<ISoundService>(_ => new AvClientView.Theme.ThemeLobbySoundService());
}
