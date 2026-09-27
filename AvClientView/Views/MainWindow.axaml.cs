using System;

using Avalonia.Controls;

using AvClientView.ViewModels;

namespace AvClientView.Views;

public partial class MainWindow : Window
{
    public MainWindow()
    {
        InitializeComponent();
    }

    protected override void OnDataContextChanged(EventArgs e)
    {
        base.OnDataContextChanged(e);

        if (DataContext is MainWindowViewModel viewModel)
            viewModel.ExitRequested += (_, _) => Close();
    }
}
