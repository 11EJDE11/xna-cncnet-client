using System;

using Avalonia.Controls;

using AvClientView.Theme;
using AvClientView.ViewModels;

using ClientLogic.Layout;

using Rampastring.Tools;

namespace AvClientView.Views;

/// <summary>The skirmish lobby, drawn from the theme's SkirmishLobby layout.</summary>
public partial class SkirmishView : UserControl
{
    public SkirmishView()
    {
        InitializeComponent();
        DataContextChanged += (_, _) => Build();
    }

    private void Build()
    {
        if (DataContext is not SkirmishViewModel viewModel)
            return;

        try
        {
            LobbyHost.Content = ThemedLobbyView.Build(viewModel, ThemedLobbyView.Skirmish, isHost: true, (name, layout) => OnButton(viewModel, name, layout));
        }
        catch (Exception ex)
        {
            Logger.Log("SkirmishView: building the themed lobby failed: " + ex);
            LobbyHost.Content = new TextBlock { Text = "The theme's skirmish lobby could not be loaded: " + ex.Message };
        }
    }

    private static void OnButton(SkirmishViewModel viewModel, string name, LayoutControl layout)
    {
        switch (name)
        {
            case "btnLaunchGame":
                viewModel.LaunchCommand.Execute(null);
                break;
            case "btnLeaveGame":
                viewModel.BackCommand.Execute(null);
                break;
            case "btnPickRandomMap":
                viewModel.PickRandomMap();
                break;
            default:
                ThemeAssets.OpenUrl(layout?.Url);
                break;
        }
    }
}
