using System;

using ClientCore;

using ClientLogic.Launch;

using CommunityToolkit.Mvvm.ComponentModel;

namespace AvClientView.ViewModels;

/// <summary>The XNA ExtrasWindow: Statistics, Map Editor, Credits and Cancel.</summary>
public sealed partial class ExtrasViewModel : ObservableObject
{
    [ObservableProperty]
    private bool isOpen;

    /// <summary>The statistics window must open.</summary>
    public event EventHandler StatisticsRequested;

    public void Open() => IsOpen = true;

    public void Cancel() => IsOpen = false;

    public void Statistics()
    {
        IsOpen = false;
        StatisticsRequested?.Invoke(this, EventArgs.Empty);
    }

    public void MapEditor()
    {
        MapEditorLauncher.Launch();
        IsOpen = false;
    }

    public static void Credits() => ProcessLauncher.StartShellProcess(ClientConfiguration.Instance.CreditsURL);
}
