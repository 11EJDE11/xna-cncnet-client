using System;
using System.Collections.Generic;

using ClientCore;

using ClientLogic.UI;
using ClientLogic.Updates;

using ClientUpdater;

using Xunit;

namespace ClientLogic.Tests.Updates;

public class UpdateStatusTests
{
    internal sealed class FakeUpdater : IUpdater
    {
        public VersionState VersionState { get; set; } = VersionState.UNKNOWN;
        public bool ManualUpdateRequired { get; set; }
        public string ManualDownloadUrl { get; set; } = string.Empty;
        public string GameVersion { get; set; } = "1.0";
        public string ServerGameVersion { get; set; } = "N/A";
        public int UpdateSizeInKb { get; set; }
        public bool HasUpdateMirrors { get; set; } = true;
        public List<UpdateMirror> Mirrors { get; } = [];
        public IReadOnlyList<UpdateMirror> UpdateMirrors => Mirrors;
        public bool HasCustomComponents => false;
        public bool VersionInfoCleared { get; private set; }

        public void MoveMirrorUp(int mirrorIndex)
        {
            if (mirrorIndex >= 1 && mirrorIndex < Mirrors.Count)
                (Mirrors[mirrorIndex - 1], Mirrors[mirrorIndex]) = (Mirrors[mirrorIndex], Mirrors[mirrorIndex - 1]);
        }

        public void MoveMirrorDown(int mirrorIndex)
        {
            if (mirrorIndex >= 0 && mirrorIndex < Mirrors.Count - 1)
                (Mirrors[mirrorIndex + 1], Mirrors[mirrorIndex]) = (Mirrors[mirrorIndex], Mirrors[mirrorIndex + 1]);
        }

        public void ClearVersionInfo() => VersionInfoCleared = true;
        public int Checks { get; private set; }
        public int Starts { get; private set; }
        public int Stops { get; private set; }

        public void CheckForUpdates() => Checks++;
        public void StartUpdate() => Starts++;
        public void StopUpdate() => Stops++;

        public void FinishCheck(VersionState state)
        {
            VersionState = state;
            FileIdentifiersUpdated?.Invoke();
        }

        public void ReportProgress(string file, int filePercentage, int total) => UpdateProgressChanged?.Invoke(file, filePercentage, total);
        public void Complete() => UpdateCompleted?.Invoke();
        public void Fail(string message) => UpdateFailed?.Invoke(new Exception(message));

        public event Action FileIdentifiersUpdated;
        public event Action CustomComponentsOutdated { add { } remove { } }
        public event Action<int, int> LocalFileCheckProgressChanged { add { } remove { } }
        public event Action<string, int, int> UpdateProgressChanged;
        public event Action<string> FileDownloadCompleted { add { } remove { } }
        public event Action UpdateCompleted;
        public event Action<Exception> UpdateFailed;
        public event Action Restart { add { } remove { } }
    }

    internal sealed class QueuedDispatcher : IUiDispatcher
    {
        private readonly Queue<Action> actions = new();

        public void Post(Action action) => actions.Enqueue(action);

        public bool CheckAccess() => true;

        public void Run()
        {
            while (actions.Count > 0)
                actions.Dequeue()();
        }
    }

    private readonly FakeUpdater updater = new();
    private readonly QueuedDispatcher dispatcher = new();
    private readonly UpdateStatus status;

    public UpdateStatusTests()
    {
        TestGame.EnsureInitialized();
        status = new UpdateStatus(updater, dispatcher);
    }

    [Fact]
    public void WithoutMirrorsItSaysSoAndDoesNotCheck()
    {
        updater.HasUpdateMirrors = false;
        status.Start();

        Assert.Equal("No update download mirrors available.", status.Text);
        Assert.False(status.IsUnderlined);
        Assert.Equal(0, updater.Checks);
    }

    [Fact]
    public void ACheckLocksTheLinkUntilItsResultArrives()
    {
        status.CheckForUpdates();
        Assert.Equal(1, updater.Checks);
        Assert.Equal("Checking for updates...", status.Text);
        Assert.False(status.IsClickable);

        updater.FinishCheck(VersionState.UPTODATE);
        Assert.Equal("Checking for updates...", status.Text); // posted to the UI thread
        dispatcher.Run();

        Assert.Equal(ClientConfiguration.Instance.LocalGame + " is up to date.", status.Text);
        Assert.True(status.IsClickable);
        Assert.False(status.IsUnderlined);
    }

    [Fact]
    public void AnAvailableUpdateIsOfferedAndCanBeDeclinedOrAccepted()
    {
        UpdateAvailableEventArgs offered = null;
        status.UpdateAvailable += (_, e) => offered = e;
        updater.ServerGameVersion = "2.0";
        updater.UpdateSizeInKb = 2500;

        status.CheckForUpdates();
        updater.FinishCheck(VersionState.OUTDATED);
        dispatcher.Run();

        Assert.Equal("2.0", offered.Version);
        Assert.Equal(2500, offered.SizeInKb);
        Assert.Equal("An update is available.", status.Text);

        status.DeclineUpdate();
        Assert.Equal("An update is available, click to install.", status.Text);
        Assert.True(status.IsClickable);

        status.AcceptUpdate();
        Assert.True(status.IsUpdateInProgress);
        Assert.Equal(1, updater.Starts);

        // Check results are ignored during an update
        updater.FinishCheck(VersionState.UNKNOWN);
        dispatcher.Run();
        Assert.Equal("Updating...", status.Text);

        status.UpdateCompleted();
        Assert.False(status.IsUpdateInProgress);
        Assert.Contains("was succesfully updated to v.1.0", status.Text);
    }

    [Fact]
    public void AManualUpdateIsOfferedOnlyWithADownloadLink()
    {
        int offers = 0;
        status.ManualUpdateAvailable += (_, _) => offers++;
        updater.ManualUpdateRequired = true;

        updater.FinishCheck(VersionState.OUTDATED);
        dispatcher.Run();
        Assert.Equal(0, offers);

        updater.ManualDownloadUrl = "https://example.org";
        updater.FinishCheck(VersionState.OUTDATED);
        dispatcher.Run();
        Assert.Equal(1, offers);
        Assert.Equal("An update is available. Manual download & installation required.", status.Text);
    }

    [Fact]
    public void ClickingChecksAgainOnlyWhenNoCheckIsRunning()
    {
        updater.VersionState = VersionState.UPDATECHECKINPROGRESS;
        status.Click();
        Assert.Equal(0, updater.Checks);

        updater.VersionState = VersionState.UNKNOWN;
        status.Click();
        Assert.Equal(1, updater.Checks);
    }

    [Fact]
    public void AFailedUpdateUnlocksTheMenuAndDescribesTheError()
    {
        status.AcceptUpdate();
        StartupMessage message = status.UpdateFailed("disk full");

        Assert.False(status.IsUpdateInProgress);
        Assert.Equal("Updating failed! Click to retry.", status.Text);
        Assert.Equal("Update failed", message.Title);
        Assert.Contains("disk full", message.Text);
    }
}
