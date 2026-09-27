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
    [NotifyCanExecuteChangedFor(nameof(OpenCnCNetCommand))]
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

    public event EventHandler CnCNetRequested;

    [RelayCommand(CanExecute = nameof(MapsLoaded))]
    private void OpenCnCNet() => CnCNetRequested?.Invoke(this, EventArgs.Empty);

    [RelayCommand(CanExecute = nameof(MapsLoaded))]
    private void OpenLan() => LanRequested?.Invoke(this, EventArgs.Empty);

    [RelayCommand]
    private void Exit() => ExitRequested?.Invoke(this, EventArgs.Empty);

    /// <summary>The game version, as the XNA main menu shows it.</summary>
    public string Version => ProgramConstants.GAME_VERSION;

    /// <summary>
    /// A themed main menu button was clicked (the XNA button names). Buttons for screens the preview doesn't have
    /// yet say so in the status line.
    /// </summary>
    public void Click(string buttonName, string buttonText)
    {
        switch (buttonName)
        {
            case "btnSkirmish":
                if (OpenSkirmishCommand.CanExecute(null))
                    OpenSkirmishCommand.Execute(null);
                break;
            case "btnCnCNet":
                if (OpenCnCNetCommand.CanExecute(null))
                    OpenCnCNetCommand.Execute(null);
                break;
            case "btnLan":
                if (OpenLanCommand.CanExecute(null))
                    OpenLanCommand.Execute(null);
                break;
            case "btnExit":
                Exit();
                break;
            default:
                Status = string.Format("{0} isn't in the Avalonia preview yet.", string.IsNullOrWhiteSpace(buttonText) ? buttonName : buttonText.Replace(Environment.NewLine, " "));
                break;
        }
    }
}
