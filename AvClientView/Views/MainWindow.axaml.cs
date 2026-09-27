using System;

using Avalonia.Controls;

using AvClientView.ViewModels;

namespace AvClientView.Views;

public partial class MainWindow : Window
{
    public MainWindow()
    {
        InitializeComponent();

        // The XNA client's window size (its render resolution), so the theme's layouts come out the same
        Width = AvClientView.Theme.ThemeAssets.RenderWidth;
        Height = AvClientView.Theme.ThemeAssets.RenderHeight;
    }

    protected override void OnDataContextChanged(EventArgs e)
    {
        base.OnDataContextChanged(e);

        if (DataContext is MainWindowViewModel viewModel)
            viewModel.ExitRequested += (_, _) => Close();
    }
}
