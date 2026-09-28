using System;

using ClientUpdater;

namespace ClientLogic.Updates;

/// <summary>
/// The client updater as the update UI uses it. <see cref="ClientUpdaterService"/> is the static
/// <see cref="Updater"/>; tests use their own. The events are raised on the updater's threads.
/// </summary>
public interface IUpdater
{
    VersionState VersionState { get; }

    bool ManualUpdateRequired { get; }

    string ManualDownloadUrl { get; }

    string GameVersion { get; }

    string ServerGameVersion { get; }

    int UpdateSizeInKb { get; }

    bool HasUpdateMirrors { get; }

    void CheckForUpdates();

    void StartUpdate();

    void StopUpdate();

    /// <summary>An update check finished (<see cref="VersionState"/> has its result).</summary>
    event Action FileIdentifiersUpdated;

    event Action CustomComponentsOutdated;

    /// <summary>The local files were checked: checked count, total count.</summary>
    event Action<int, int> LocalFileCheckProgressChanged;

    /// <summary>Download progress: current file, its percentage, total percentage.</summary>
    event Action<string, int, int> UpdateProgressChanged;

    /// <summary>An archive was downloaded and is being unpacked.</summary>
    event Action<string> FileDownloadCompleted;

    event Action UpdateCompleted;

    event Action<Exception> UpdateFailed;

    /// <summary>The second-stage updater was started: the client must exit.</summary>
    event Action Restart;
}

/// <summary>The static <see cref="Updater"/> as an <see cref="IUpdater"/>.</summary>
public sealed class ClientUpdaterService : IUpdater
{
    public ClientUpdaterService()
    {
        Updater.FileIdentifiersUpdated += () => FileIdentifiersUpdated?.Invoke();
        Updater.OnCustomComponentsOutdated += () => CustomComponentsOutdated?.Invoke();
        Updater.LocalFileCheckProgressChanged += (checkedCount, total) => LocalFileCheckProgressChanged?.Invoke(checkedCount, total);
        Updater.UpdateProgressChanged += (file, filePercentage, total) => UpdateProgressChanged?.Invoke(file, filePercentage, total);
        Updater.OnFileDownloadCompleted += archive => FileDownloadCompleted?.Invoke(archive);
        Updater.OnUpdateCompleted += () => UpdateCompleted?.Invoke();
        Updater.OnUpdateFailed += ex => UpdateFailed?.Invoke(ex);
        Updater.Restart += (_, _) => Restart?.Invoke();
    }

    public VersionState VersionState => Updater.VersionState;

    public bool ManualUpdateRequired => Updater.ManualUpdateRequired;

    public string ManualDownloadUrl => Updater.ManualDownloadURL;

    public string GameVersion => Updater.GameVersion;

    public string ServerGameVersion => Updater.ServerGameVersion;

    public int UpdateSizeInKb => Updater.UpdateSizeInKb;

    public bool HasUpdateMirrors => Updater.UpdateMirrors?.Count > 0;

    public void CheckForUpdates() => Updater.CheckForUpdates();

    public void StartUpdate() => Updater.StartUpdate();

    public void StopUpdate() => Updater.StopUpdate();

    public event Action FileIdentifiersUpdated;

    public event Action CustomComponentsOutdated;

    public event Action<int, int> LocalFileCheckProgressChanged;

    public event Action<string, int, int> UpdateProgressChanged;

    public event Action<string> FileDownloadCompleted;

    public event Action UpdateCompleted;

    public event Action<Exception> UpdateFailed;

    public event Action Restart;
}
