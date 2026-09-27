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

    private TopBarView topBar;

    protected override void OnPointerMoved(Avalonia.Input.PointerEventArgs e)
    {
        base.OnPointerMoved(e);
        topBar?.OnCursorMoved(e.GetPosition(this).Y);
    }

    protected override void OnKeyDown(Avalonia.Input.KeyEventArgs e)
    {
        base.OnKeyDown(e);
        if (!e.Handled && topBar != null && topBar.HandleKey(e.Key))
            e.Handled = true;
    }

    protected override void OnDataContextChanged(EventArgs e)
    {
        base.OnDataContextChanged(e);

        if (DataContext is MainWindowViewModel viewModel)
        {
            viewModel.ExitRequested += (_, _) => Close();

            if (topBar == null)
            {
                topBar = new TopBarView(viewModel.TopBar) { ZIndex = 10000 };
                Root.Children.Add(topBar);
            }
        }
    }
}
