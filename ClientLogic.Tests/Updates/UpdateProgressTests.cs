using ClientLogic.Updates;

using ClientUpdater;

using Xunit;

using static ClientLogic.Tests.Updates.UpdateStatusTests;

namespace ClientLogic.Tests.Updates;

public class UpdateProgressTests
{
    private readonly FakeUpdater updater = new();
    private readonly QueuedDispatcher dispatcher = new();
    private readonly UpdateProgress progress;

    public UpdateProgressTests()
    {
        TestGame.EnsureInitialized();
        progress = new UpdateProgress(updater, dispatcher);
    }

    [Fact]
    public void OnlyTheLatestProgressIsShownAndOutOfRangeValuesShowZero()
    {
        int changes = 0;
        progress.Changed += (_, _) => changes++;

        updater.ReportProgress("a.mix", 10, 5);
        updater.ReportProgress("b.mix", 150, 40);
        dispatcher.Run();

        Assert.Equal(1, changes);
        Assert.Equal("Current file: b.mix", progress.CurrentFile);
        Assert.Equal(0, progress.FilePercentage);
        Assert.Equal(40, progress.TotalPercentage);
        Assert.Equal("Downloading files", progress.Status);
    }

    [Fact]
    public void AForceUpdateChecksFirstThenUpdates()
    {
        progress.ForceUpdate();
        Assert.Equal(1, updater.Checks);
        Assert.Equal("Connecting", progress.Status);

        updater.ServerGameVersion = "3.0";
        updater.FinishCheck(VersionState.OUTDATED);
        dispatcher.Run();

        Assert.Equal(1, updater.Starts);
        Assert.Contains("version 3.0", progress.Description);

        // Cancelling now stops the running download
        progress.Cancel();
        Assert.Equal(1, updater.Stops);
    }

    [Fact]
    public void AFailedForceUpdateCheckClosesTheWindowWithoutStopping()
    {
        bool failed = false, cancelled = false;
        progress.ForceUpdateCheckFailed += (_, _) => failed = true;
        progress.Cancelled += (_, _) => cancelled = true;

        progress.ForceUpdate();
        updater.FinishCheck(VersionState.UNKNOWN);
        dispatcher.Run();

        Assert.True(failed);
        Assert.True(cancelled);
        Assert.Equal(0, updater.Starts);
        Assert.Equal(0, updater.Stops);
    }

    [Fact]
    public void CompletionAndFailureArePassedOn()
    {
        bool completed = false;
        string reason = null;
        progress.Completed += (_, _) => completed = true;
        progress.Failed += (_, message) => reason = message;

        updater.Complete();
        updater.Fail("timeout");
        dispatcher.Run();

        Assert.True(completed);
        Assert.Equal("timeout", reason);
    }

    [Theory]
    [InlineData(999, "The size of the update is 999 KB.")]
    [InlineData(2500, "The size of the update is 2 MB.")]
    public void TheSizeIsGivenInKilobytesOrMegabytes(int sizeInKb, string text) =>
        Assert.Equal(text, UpdateProgress.SizeText(sizeInKb));
}
