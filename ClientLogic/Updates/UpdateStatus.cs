using System;
using System.IO;

using ClientCore;
using ClientCore.Extensions;

using ClientLogic.UI;

using ClientUpdater;

namespace ClientLogic.Updates;

/// <summary>An update is available: its version and download size.</summary>
public sealed class UpdateAvailableEventArgs(string version, int sizeInKb) : EventArgs
{
    public string Version { get; } = version;

    public int SizeInKb { get; } = sizeInKb;
}

/// <summary>An update is available that has to be downloaded and installed by hand.</summary>
public sealed class ManualUpdateEventArgs(string version, string downloadUrl) : EventArgs
{
    public string Version { get; } = version;

    public string DownloadUrl { get; } = downloadUrl;
}

/// <summary>
/// The main menu's update status link (the XNA MainMenu's lblUpdateStatus and its update checks): its text, whether it
/// can be clicked and is underlined, and when to ask the user about an update.
/// </summary>
public sealed class UpdateStatus
{
    private const double UPDATE_RE_CHECK_THRESHOLD = 30.0;

    private readonly IUpdater updater;
    private DateTime lastUpdateCheckTime;

    public UpdateStatus(IUpdater updater, IUiDispatcher uiDispatcher)
    {
        this.updater = updater;
        updater.FileIdentifiersUpdated += () => uiDispatcher.Post(OnUpdateCheckFinished);
    }

    /// <summary>The update status and version are shown (not in ModMode, which disables the updater).</summary>
    public static bool IsEnabled => !ClientConfiguration.Instance.ModMode;

    public string Text { get; private set; } = string.Empty;

    /// <summary>The link can be clicked (XNA's Enabled).</summary>
    public bool IsClickable { get; private set; } = true;

    public bool IsUnderlined { get; private set; } = true;

    /// <summary>An update is being downloaded: the main menu's buttons, hotkeys and top bar are locked.</summary>
    public bool IsUpdateInProgress { get; private set; }

    /// <summary>The installed version (lblVersion).</summary>
    public string GameVersion => updater.GameVersion;

    /// <summary>The state changed.</summary>
    public event EventHandler Changed;

    /// <summary>An update is available: ask whether to install it (UpdateQueryWindow).</summary>
    public event EventHandler<UpdateAvailableEventArgs> UpdateAvailable;

    /// <summary>An update needs a manual download (ManualUpdateQueryWindow).</summary>
    public event EventHandler<ManualUpdateEventArgs> ManualUpdateAvailable;

    /// <summary>The main menu was shown for the first time: check for updates if the user wants that.</summary>
    public void Start()
    {
        if (!IsEnabled)
            return;

        if (!updater.HasUpdateMirrors)
            Set("No update download mirrors available.".L10N("Client:Main:NoUpdateMirrorsAvailable"), IsClickable, underlined: false);
        else if (UserINISettings.Instance.CheckForUpdates)
            CheckForUpdates();
        else
            Set("Click to check for updates.".L10N("Client:Main:ClickToCheckUpdate"), IsClickable, IsUnderlined);
    }

    /// <summary>The main menu was shown again: check again if the last check is more than 30 seconds old.</summary>
    public void SwitchedOn()
    {
        if (IsEnabled && UserINISettings.Instance.CheckForUpdates &&
            DateTime.Now - lastUpdateCheckTime > TimeSpan.FromSeconds(UPDATE_RE_CHECK_THRESHOLD))
        {
            CheckForUpdates();
        }
    }

    /// <summary>The link was clicked: check (again), unless a check or an update is running.</summary>
    public void Click()
    {
        if (updater.VersionState is VersionState.OUTDATED or VersionState.MISMATCHED or VersionState.UNKNOWN or VersionState.UPTODATE)
            CheckForUpdates();
    }

    /// <summary>Starts a check for updates (also on the CnCNet lobby's UPDATE request).</summary>
    public void CheckForUpdates()
    {
        if (!updater.HasUpdateMirrors)
            return;

        updater.CheckForUpdates();
        lastUpdateCheckTime = DateTime.Now;
        Set("Checking for updates...".L10N("Client:Main:CheckingForUpdates"), clickable: false, IsUnderlined);
    }

    private void OnUpdateCheckFinished()
    {
        if (IsUpdateInProgress)
            return;

        switch (updater.VersionState)
        {
            case VersionState.UPTODATE:
                Set(string.Format("{0} is up to date.".L10N("Client:Main:GameUpToDate"), ClientConfiguration.Instance.LocalGame),
                    clickable: true, underlined: false);
                break;
            case VersionState.OUTDATED when updater.ManualUpdateRequired:
                Set("An update is available. Manual download & installation required.".L10N("Client:Main:UpdateAvailableManualDownloadRequired"),
                    clickable: true, underlined: false);
                if (!string.IsNullOrEmpty(updater.ManualDownloadUrl))
                    ManualUpdateAvailable?.Invoke(this, new ManualUpdateEventArgs(updater.ServerGameVersion, updater.ManualDownloadUrl));
                break;
            case VersionState.OUTDATED:
                Set("An update is available.".L10N("Client:Main:UpdateAvailable"), IsClickable, IsUnderlined);
                UpdateAvailable?.Invoke(this, new UpdateAvailableEventArgs(updater.ServerGameVersion, updater.UpdateSizeInKb));
                break;
            case VersionState.UNKNOWN:
                Set("Checking for updates failed! Click to retry.".L10N("Client:Main:CheckUpdateFailedClickToRetry"),
                    clickable: true, underlined: true);
                break;
        }
    }

    /// <summary>The user declined the update.</summary>
    public void DeclineUpdate() =>
        Set("An update is available, click to install.".L10N("Client:Main:UpdateAvailableClickToInstall"), clickable: true, underlined: true);

    /// <summary>The user accepted the update (after the update window was opened): the download starts.</summary>
    public void AcceptUpdate()
    {
        IsUpdateInProgress = true;
        Set("Updating...".L10N("Client:Main:Updating"), IsClickable, IsUnderlined);
        updater.StartUpdate();
    }

    /// <summary>The options window's Force Update was clicked (the update window then checks and updates).</summary>
    public void ForceUpdate()
    {
        IsUpdateInProgress = true;
        Set("Force updating...".L10N("Client:Main:ForceUpdating"), IsClickable, IsUnderlined);
    }

    /// <summary>The update window reported that the update was cancelled.</summary>
    public void UpdateCancelled()
    {
        IsUpdateInProgress = false;
        Set("The update was cancelled. Click to retry.".L10N("Client:Main:UpdateCancelledClickToRetry"), clickable: true, underlined: true);
    }

    /// <summary>The update window reported a completed update (one that didn't need a restart).</summary>
    public void UpdateCompleted()
    {
        IsUpdateInProgress = false;
        Set(string.Format("{0} was succesfully updated to v.{1}".L10N("Client:Main:UpdateSuccess"),
            ClientConfiguration.Instance.LocalGame, updater.GameVersion), clickable: true, underlined: false);
    }

    /// <summary>The update window reported a failed update.</summary>
    /// <returns>The message to show the user.</returns>
    public StartupMessage UpdateFailed(string reason)
    {
        IsUpdateInProgress = false;
        Set("Updating failed! Click to retry.".L10N("Client:Main:UpdateFailedClickToRetry"), clickable: true, underlined: true);

        return new StartupMessage("Update failed".L10N("Client:Main:UpdateFailedTitle"),
            string.Format(("An error occured while updating. Returned error was: {0}\n\nIf you are connected to the Internet and your firewall isn't blocking\n{1}, and the issue is reproducible, contact us at\n{2} for support.").L10N("Client:Main:UpdateFailedText"),
                reason, Path.GetFileName(ProgramConstants.StartupExecutable), ClientConfiguration.Instance.ShortSupportURL));
    }

    private void Set(string text, bool clickable, bool underlined)
    {
        Text = text;
        IsClickable = clickable;
        IsUnderlined = underlined;
        Changed?.Invoke(this, EventArgs.Empty);
    }
}
