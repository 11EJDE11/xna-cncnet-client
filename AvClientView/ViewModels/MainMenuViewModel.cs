using System;
using System.Threading.Tasks;

using ClientCore;
using ClientCore.Extensions;

using ClientLogic.UI;
using ClientLogic.Updates;

using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

using DTAClient.Domain.Multiplayer;
using DTAClient.Domain.Multiplayer.CnCNet;

using Rampastring.Tools;

namespace AvClientView.ViewModels;

/// <summary>The main menu: loads the maps in the background and offers the screens the preview has.</summary>
public sealed partial class MainMenuViewModel : ObservableObject
{
    private readonly MapLoader mapLoader;
    private readonly IDialogService dialogs;

    public MainMenuViewModel(MapLoader mapLoader, IDialogService dialogs, IUiDispatcher uiDispatcher, UpdateStatus updateStatus)
    {
        this.mapLoader = mapLoader;
        this.dialogs = dialogs;
        UpdateStatus = updateStatus;

        // The online player count, as the XNA main menu shows it (the first query blocks, so not on the UI thread)
        CnCNetPlayerCountTask.CnCNetGameCountUpdated += (_, e) => uiDispatcher.Post(() =>
            PlayerCount = e.PlayerCount == -1 ? "N/A".L10N("Client:Main:N/A") : e.PlayerCount.ToString());
        Task.Run(() => CnCNetPlayerCountTask.InitializeService(new System.Threading.CancellationTokenSource()));
        string windowTitle = ClientConfiguration.Instance.WindowTitle;
        Title = string.IsNullOrEmpty(windowTitle) ? string.Format("{0} Client", ClientConfiguration.Instance.LocalGame) : windowTitle;
        _ = LoadMapsAsync();
    }

    public string Title { get; }

    [ObservableProperty]
    private string status = "Loading maps...";

    [ObservableProperty]
    private string playerCount = "-";

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

    public event EventHandler OptionsRequested;

    public event EventHandler CampaignRequested;

    public event EventHandler LoadGameRequested;

    public event EventHandler ExtrasRequested;

    public event EventHandler StatisticsRequested;

    public event EventHandler CnCNetRequested;

    [RelayCommand(CanExecute = nameof(MapsLoaded))]
    private void OpenCnCNet() => CnCNetRequested?.Invoke(this, EventArgs.Empty);

    [RelayCommand(CanExecute = nameof(MapsLoaded))]
    private void OpenLan() => LanRequested?.Invoke(this, EventArgs.Empty);

    [RelayCommand]
    private void Exit() => ExitRequested?.Invoke(this, EventArgs.Empty);

    /// <summary>The game version, as the XNA main menu shows it; an update changes it.</summary>
    [ObservableProperty]
    private string version = ProgramConstants.GAME_VERSION;

    /// <summary>The update status link (lblUpdateStatus) and whether an update is running.</summary>
    public UpdateStatus UpdateStatus { get; }

    /// <summary>An update finished without a restart: the version label shows the new version.</summary>
    public void RefreshVersion() => Version = UpdateStatus.GameVersion;

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
            case "btnLoadGame":
                LoadGameRequested?.Invoke(this, EventArgs.Empty);
                break;
            case "btnNewCampaign":
                CampaignRequested?.Invoke(this, EventArgs.Empty);
                break;
            case "btnOptions":
                OptionsRequested?.Invoke(this, EventArgs.Empty);
                break;
            case "btnMapEditor":
                ClientLogic.Launch.MapEditorLauncher.Launch();
                break;
            case "btnCredits":
                ExtrasViewModel.Credits();
                break;
            case "btnExtras":
                ExtrasRequested?.Invoke(this, EventArgs.Empty);
                break;
            case "btnStatistics":
                StatisticsRequested?.Invoke(this, EventArgs.Empty);
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
