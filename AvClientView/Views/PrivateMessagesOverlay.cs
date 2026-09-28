using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;

using AvClientView.ViewModels;

namespace AvClientView.Views;

/// <summary>
/// The XNA PrivateMessagingPanel: the private messaging window over a darkening panel that closes it when clicked
/// outside the window.
/// </summary>
public sealed class PrivateMessagesOverlay : Panel
{
    public PrivateMessagesOverlay(PrivateMessagesViewModel viewModel)
    {
        Background = new SolidColorBrush(Color.FromArgb(128, 0, 0, 0));
        IsVisible = viewModel.IsOpen;

        Children.Add(new PrivateMessagesView
        {
            DataContext = viewModel,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
        });

        viewModel.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(PrivateMessagesViewModel.IsOpen))
                IsVisible = viewModel.IsOpen;
        };

        PointerPressed += (_, e) =>
        {
            if (e.Source == this && e.GetCurrentPoint(this).Properties.IsLeftButtonPressed)
            {
                viewModel.Close();
                e.Handled = true;
            }
        };
    }
}
