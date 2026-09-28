using System;

using ClientCore;
using ClientCore.Extensions;

using ClientLogic.UI;

using ClientUpdater;

namespace ClientLogic.Updates;

/// <summary>
/// The update window's state (the XNA UpdateWindow): its description, status, current file and progress, following
/// the updater, and the force update that checks for updates first.
/// </summary>
public sealed class UpdateProgress
{
    private readonly IUpdater updater;
    private readonly IUiDispatcher uiDispatcher;
    private readonly object locker = new();

    private bool isStartingForceUpdate;
    private bool progressPosted;
    private string pendingFileName = string.Empty;
    private int pendingFilePercentage;
    private int pendingTotalPercentage;

    public UpdateProgress(IUpdater updater, IUiDispatcher uiDispatcher)
    {
        this.updater = updater;
        this.uiDispatcher = uiDispatcher;

        updater.FileIdentifiersUpdated += () => uiDispatcher.Post(OnUpdateCheckFinished);
        updater.LocalFileCheckProgressChanged += (checkedCount, totalCount) =>
            uiDispatcher.Post(() => SetFilePercentage(totalCount > 0 ? checkedCount * 100 / totalCount : 0));
        updater.UpdateProgressChanged += OnUpdateProgressChanged;
        updater.FileDownloadCompleted += _ => uiDispatcher.Post(() => SetStatus("Unpacking archive".L10N("Client:Main:UnpackingArchive")));
        updater.UpdateCompleted += () => uiDispatcher.Post(() => Completed?.Invoke(this, EventArgs.Empty));
        updater.UpdateFailed += ex => uiDispatcher.Post(() => Failed?.Invoke(this, ex.Message));
    }

    public string Description { get; private set; } = string.Empty;

    /// <summary>The status line (the window adds its animated dots after it).</summary>
    public string Status { get; private set; } = "Preparing".L10N("Client:Main:StatusPreparing");

    public string CurrentFile { get; private set; } = string.Empty;

    public int FilePercentage { get; private set; }

    public int TotalPercentage { get; private set; }

    /// <summary>A shown value changed.</summary>
    public event EventHandler Changed;

    public event EventHandler Completed;

    /// <summary>The update was cancelled (also when a force update finds that a manual update is needed).</summary>
    public event EventHandler Cancelled;

    /// <summary>The update failed, with the updater's error message.</summary>
    public event EventHandler<string> Failed;

    /// <summary>A force update's check for updates failed: the window tells the user, then closes.</summary>
    public event EventHandler ForceUpdateCheckFailed;

    /// <summary>An update to <paramref name="newGameVersion"/> is starting (SetData).</summary>
    public void Start(string newGameVersion)
    {
        Description = string.Format(("Please wait while {0} is updated to version {1}.\nThis window will automatically close once the update is complete.\n\nThe client may also restart after the update has been downloaded.").L10N("Client:Main:UpdateVersionPleaseWait"),
            ClientConfiguration.Instance.LocalGame, newGameVersion);
        Status = "Preparing".L10N("Client:Main:StatusPreparing");
        Changed?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>Checks for updates and then updates to the latest version, whatever the installed version is.</summary>
    public void ForceUpdate()
    {
        isStartingForceUpdate = true;
        Description = string.Format("Force updating {0} to latest version...".L10N("Client:Main:ForceUpdateToLatest"), ClientConfiguration.Instance.LocalGame);
        Status = "Connecting".L10N("Client:Main:UpdateStatusConnecting");
        Changed?.Invoke(this, EventArgs.Empty);
        updater.CheckForUpdates();
    }

    /// <summary>The Cancel button.</summary>
    public void Cancel()
    {
        if (!isStartingForceUpdate)
            updater.StopUpdate();

        Close();
    }

    private void Close()
    {
        isStartingForceUpdate = false;
        Cancelled?.Invoke(this, EventArgs.Empty);
    }

    private void OnUpdateCheckFinished()
    {
        if (!isStartingForceUpdate)
            return;

        if (updater.VersionState == VersionState.UNKNOWN)
        {
            ForceUpdateCheckFailed?.Invoke(this, EventArgs.Empty);
            Close();
            return;
        }

        if (updater.VersionState == VersionState.OUTDATED && updater.ManualUpdateRequired)
        {
            Close();
            return;
        }

        Start(updater.ServerGameVersion);
        updater.StartUpdate();
        isStartingForceUpdate = false;
    }

    private void OnUpdateProgressChanged(string fileName, int filePercentage, int totalPercentage)
    {
        // The updater reports often: only the latest values are shown, once per UI turn
        lock (locker)
        {
            pendingFileName = fileName;
            pendingFilePercentage = filePercentage;
            pendingTotalPercentage = totalPercentage;

            if (progressPosted)
                return;

            progressPosted = true;
        }

        uiDispatcher.Post(ShowProgress);
    }

    private void ShowProgress()
    {
        lock (locker)
        {
            progressPosted = false;
            FilePercentage = pendingFilePercentage is < 0 or > 100 ? 0 : pendingFilePercentage;
            TotalPercentage = pendingTotalPercentage is < 0 or > 100 ? 0 : pendingTotalPercentage;
            CurrentFile = "Current file:".L10N("Client:Main:CurrentFile") + " " + pendingFileName;
        }

        Status = "Downloading files".L10N("Client:Main:DownloadingFiles");
        Changed?.Invoke(this, EventArgs.Empty);
    }

    private void SetFilePercentage(int value)
    {
        FilePercentage = value;
        Changed?.Invoke(this, EventArgs.Empty);
    }

    private void SetStatus(string status)
    {
        Status = status;
        Changed?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>The update query window's size text (UpdateQueryWindow.SetInfo).</summary>
    public static string SizeText(int updateSizeInKb) => updateSizeInKb >= 1000
        ? string.Format("The size of the update is {0} MB.".L10N("Client:Main:UpdateSizeMB"), updateSizeInKb / 1000)
        : string.Format("The size of the update is {0} KB.".L10N("Client:Main:UpdateSizeKB"), updateSizeInKb);

    /// <summary>The update query window's description (UpdateQueryWindow.SetInfo).</summary>
    public static string QueryText(string version) =>
        string.Format(("Version {0} is available for download.\nDo you wish to install it?").L10N("Client:Main:VersionAvailable"), version);
}
