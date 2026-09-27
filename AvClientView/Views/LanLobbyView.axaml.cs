using Avalonia.Controls;
using Avalonia.Input;

using AvClientView.ViewModels;

namespace AvClientView.Views;

public partial class LanLobbyView : UserControl
{
    public LanLobbyView()
    {
        InitializeComponent();
    }

    private void Games_DoubleTapped(object sender, TappedEventArgs e)
    {
        if (DataContext is LanLobbyViewModel viewModel)
            viewModel.JoinGameCommand.Execute(null);
    }
}
