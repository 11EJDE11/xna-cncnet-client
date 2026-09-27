using System;
using System.ComponentModel;

using Avalonia.Controls;

using AvClientView.Theme;
using AvClientView.ViewModels;

using ClientLogic.Layout;

using Rampastring.Tools;

namespace AvClientView.Views;

/// <summary>
/// A LAN or CnCNet game room, drawn from the theme's multiplayer lobby layout for the local player's role (players
/// don't get the map list, and their chat takes its place).
/// </summary>
public partial class GameRoomView : UserControl
{
    private MultiplayerRoomViewModel viewModel;
    private bool? builtAsHost;

    public GameRoomView()
    {
        InitializeComponent();
        DataContextChanged += (_, _) => Attach(DataContext as MultiplayerRoomViewModel);
        AttachedToVisualTree += (_, _) => BuildIfRoleChanged();

        // The XNA CnCNet lobby resets the host inactivity check on mouse moves
        PointerMoved += (_, _) => (viewModel as CnCNetGameRoomViewModel)?.ResetInactivity();
    }

    private void Attach(MultiplayerRoomViewModel newViewModel)
    {
        if (viewModel != null)
        {
            viewModel.Opened -= ViewModel_Opened;
            viewModel.PropertyChanged -= ViewModel_PropertyChanged;
        }

        viewModel = newViewModel;
        builtAsHost = null;

        WindowHost.Children.Clear();
        if (viewModel is CnCNetGameRoomViewModel cncnet)
        {
            WindowHost.Children.Add(new GameLobbySettingsView(cncnet.Settings));
            WindowHost.Children.Add(new TunnelSelectionView(cncnet.Tunnels));
        }

        if (viewModel != null)
        {
            viewModel.Opened += ViewModel_Opened;
            viewModel.PropertyChanged += ViewModel_PropertyChanged;
            BuildIfRoleChanged();
        }
    }

    private void ViewModel_Opened(object sender, EventArgs e) => builtAsHost = null;

    private void ViewModel_PropertyChanged(object sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(MultiplayerRoomViewModel.IsHost))
            BuildIfRoleChanged();
        else if (e.PropertyName is nameof(MultiplayerRoomViewModel.LaunchText) or nameof(MultiplayerRoomViewModel.LockText))
            UpdateButtonTexts();
    }

    private void BuildIfRoleChanged()
    {
        if (viewModel == null || builtAsHost == viewModel.IsHost)
            return;

        builtAsHost = viewModel.IsHost;

        try
        {
            LobbyHost.Content = ThemedLobbyView.Build(viewModel, ThemedLobbyView.Multiplayer(viewModel.LayoutIniName),
                viewModel.IsHost, OnButton);
            UpdateButtonTexts();
        }
        catch (Exception ex)
        {
            Logger.Log("GameRoomView: building the themed lobby failed: " + ex);
            LobbyHost.Content = new TextBlock { Text = "The theme's game lobby could not be loaded: " + ex.Message };
        }
    }

    private void UpdateButtonTexts()
    {
        if (LobbyHost.Content is not Canvas root || viewModel == null)
            return;

        if (LayoutView.FindNamed<ThemedButton>(root, "btnLaunchGame") is ThemedButton launch)
            launch.Text = viewModel.LaunchText;

        if (LayoutView.FindNamed<ThemedButton>(root, "btnLockGame") is ThemedButton lockButton)
            lockButton.Text = viewModel.LockText;
    }

    private void OnButton(string name, LayoutControl layout)
    {
        switch (name)
        {
            case "btnLaunchGame":
                viewModel.LaunchCommand.Execute(null);
                break;
            case "btnLeaveGame":
                viewModel.LeaveCommand.Execute(null);
                break;
            case "btnLockGame":
                viewModel.ToggleLockCommand.Execute(null);
                break;
            case "btnPickRandomMap":
                viewModel.PickRandomMap();
                break;
            case "btnSaveLoadGameOptions":
                ThemedLobbyView.OpenGameOptionPresetMenu(LobbyHost.Content as Control, viewModel);
                break;
            case "btnChangeTunnel":
                (viewModel as CnCNetGameRoomViewModel)?.Tunnels.Open();
                break;
            case "btnGameLobbySettings":
                (viewModel as CnCNetGameRoomViewModel)?.Settings.Open();
                break;
            default:
                ThemeAssets.OpenUrl(layout?.Url);
                break;
        }
    }
}
