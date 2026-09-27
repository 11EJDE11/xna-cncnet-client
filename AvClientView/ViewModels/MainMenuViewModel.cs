using System;
using System.Threading.Tasks;

using ClientCore;
using ClientCore.Extensions;

using ClientLogic.UI;

using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

using DTAClient.Domain.Multiplayer;

using Rampastring.Tools;

namespace AvClientView.ViewModels;

/// <summary>The main menu: loads the maps in the background and offers the screens the preview has.</summary>
public sealed partial class MainMenuViewModel : ObservableObject
{
    private readonly MapLoader mapLoader;
    private readonly IDialogService dialogs;

    public MainMenuViewModel(MapLoader mapLoader, IDialogService dialogs)
    {
        this.mapLoader = mapLoader;
        this.dialogs = dialogs;
        string windowTitle = ClientConfiguration.Instance.WindowTitle;
        Title = string.IsNullOrEmpty(windowTitle) ? string.Format("{0} Client", ClientConfiguration.Instance.LocalGame) : windowTitle;
        _ = LoadMapsAsync();
    }

    public string Title { get; }

    [ObservableProperty]
    private string status = "Loading maps...";

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(OpenSkirmishCommand))]
    [NotifyCanExecuteChangedFor(nameof(OpenLanCommand))]
    private bool mapsLoaded;

    public event EventHandler ExitRequested;

    private async Task LoadMapsAsync()
    {
        try
        {
            mapLoader.Initialize();
            await mapLoader.LoadMapsAsync();
            MapsLoaded = true;
            Status = string.Format("{0} game modes, {1} maps", mapLoader.GameModes.Count, mapLoader.GameModeMaps.Count);
            Logger.Log("Maps loaded: " + Status);
        }
        catch (Exception ex)
        {
            Logger.Log("Loading maps failed: " + ex);
            Status = "Loading maps failed: " + ex.Message;
        }
    }

    public event EventHandler SkirmishRequested;

    [RelayCommand(CanExecute = nameof(MapsLoaded))]
    private void OpenSkirmish() => SkirmishRequested?.Invoke(this, EventArgs.Empty);

    public event EventHandler LanRequested;

    [RelayCommand(CanExecute = nameof(MapsLoaded))]
    private void OpenLan() => LanRequested?.Invoke(this, EventArgs.Empty);

    [RelayCommand]
    private void Exit() => ExitRequested?.Invoke(this, EventArgs.Empty);
}
