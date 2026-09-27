using Avalonia.Controls;
using Avalonia.Input;

using AvClientView.ViewModels;

namespace AvClientView.Views;

public partial class CnCNetLobbyView : UserControl
{
    public CnCNetLobbyView()
    {
        InitializeComponent();
    }

    private void Games_DoubleTapped(object sender, TappedEventArgs e)
    {
        if (DataContext is CnCNetLobbyViewModel viewModel)
            viewModel.JoinGameCommand.Execute(null);
    }
}
