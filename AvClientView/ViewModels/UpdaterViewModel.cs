using System;

using ClientCore;
using ClientCore.Extensions;

using ClientLogic.UI;
using ClientLogic.Updates;

using CommunityToolkit.Mvvm.ComponentModel;

namespace AvClientView.ViewModels;

/// <summary>
/// The XNA main menu's update windows: UpdateQueryWindow (install the update?), ManualUpdateQueryWindow (download it
/// by hand) and UpdateWindow (the download's progress), over <see cref="UpdateStatus"/> and <see cref="UpdateProgress"/>.
/// </summary>
public sealed partial class UpdaterViewModel : ObservableObject
{
    private readonly UpdateStatus status;
    private readonly IUpdater updater;
    private readonly IDialogService dialogs;
    private string manualDownloadUrl;

    public UpdaterViewModel(UpdateStatus status, UpdateProgress progress, IUpdater updater, IDialogService dialogs, IUiDispatcher uiDispatcher)
    {
        this.status = status;
        this.updater = updater;
        this.dialogs = dialogs;
        Progress = progress;

        status.UpdateAvailable += (_, e) =>
        {
            QueryText = UpdateProgress.QueryText(e.Version);
            SizeText = UpdateProgress.SizeText(e.SizeInKb);
            IsQueryOpen = true;
        };
        status.ManualUpdateAvailable += (_, e) =>
        {
            ManualVersion = e.Version;
            manualDownloadUrl = e.DownloadUrl;
            IsManualOpen = true;
        };

        progress.Completed += (_, _) =>
        {
            IsUpdateOpen = false;
            status.UpdateCompleted();
            VersionChanged?.Invoke(this, EventArgs.Empty);

            // The update didn't need a restart: the new version's translation game files are applied now
            if (StartupChecks.ApplyTranslationGameFiles(updater.GameVersion, skipVersionCheck: true) is { } message)
                dialogs.ShowMessage(message.Title, message.Text);
        };
        progress.Cancelled += (_, _) =>
        {
            IsUpdateOpen = false;
            status.UpdateCancelled();
        };
        progress.Failed += (_, reason) =>
        {
            IsUpdateOpen = false;
            StartupMessage message = status.UpdateFailed(reason);
            dialogs.ShowMessage(message.Title, message.Text);
        };
        progress.ForceUpdateCheckFailed += (_, _) =>
            dialogs.ShowMessage("Force Update Failure".L10N("Client:Main:ForceUpdateFailureTitle"), "Checking for updates failed.".L10N("Client:Main:ForceUpdateFailureText"));

        // The second-stage updater has been started and replaces the client's files: exit (Updater_Restart)
        updater.Restart += () => uiDispatcher.Post(() => RestartRequested?.Invoke(this, EventArgs.Empty));
    }

    public UpdateProgress Progress { get; }

    [ObservableProperty]
    private bool isQueryOpen;

    [ObservableProperty]
    private string queryText = string.Empty;

    [ObservableProperty]
    private string sizeText = string.Empty;

    [ObservableProperty]
    private bool isManualOpen;

    /// <summary>The version that needs a manual download (the window's text, which the theme can change, formats it).</summary>
    [ObservableProperty]
    private string manualVersion = string.Empty;

    [ObservableProperty]
    private bool isUpdateOpen;

    /// <summary>An update completed without a restart: the version label changed.</summary>
    public event EventHandler VersionChanged;

    /// <summary>The updater started its second stage: the client must exit.</summary>
    public event EventHandler RestartRequested;

    public void AcceptUpdate()
    {
        IsQueryOpen = false;
        Progress.Start(updater.ServerGameVersion);
        IsUpdateOpen = true;
        status.AcceptUpdate();
    }

    public void DeclineUpdate()
    {
        IsQueryOpen = false;
        status.DeclineUpdate();
    }

    /// <summary>The options window's Force Update.</summary>
    public void ForceUpdate()
    {
        status.ForceUpdate();
        Progress.ForceUpdate();
        IsUpdateOpen = true;
    }

    public void CancelUpdate() => Progress.Cancel();

    public static void ViewChangelog() => ProcessLauncher.StartShellProcess(ClientConfiguration.Instance.ChangelogURL);

    public void ViewDownloads() => ProcessLauncher.StartShellProcess(manualDownloadUrl);

    public void CloseManual() => IsManualOpen = false;
}
