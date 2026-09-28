using System;
using System.Collections.Generic;

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

    /// <summary>The download mirrors, in priority order.</summary>
    IReadOnlyList<UpdateMirror> UpdateMirrors { get; }

    /// <summary>The optional components exist (the Components tab can be used).</summary>
    bool HasCustomComponents { get; }

    void MoveMirrorUp(int mirrorIndex);

    void MoveMirrorDown(int mirrorIndex);

    /// <summary>Forgets the local file versions, so the next update re-downloads everything (force update).</summary>
    void ClearVersionInfo();

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

    public IReadOnlyList<UpdateMirror> UpdateMirrors => (IReadOnlyList<UpdateMirror>)Updater.UpdateMirrors ?? [];

    public bool HasCustomComponents => Updater.CustomComponents?.Count > 0;

    public void MoveMirrorUp(int mirrorIndex) => Updater.MoveMirrorUp(mirrorIndex);

    public void MoveMirrorDown(int mirrorIndex) => Updater.MoveMirrorDown(mirrorIndex);

    public void ClearVersionInfo() => Updater.ClearVersionInfo();

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
