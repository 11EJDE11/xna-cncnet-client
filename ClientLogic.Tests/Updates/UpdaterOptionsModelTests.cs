using ClientLogic.Settings;

using ClientUpdater;

using Xunit;

using static ClientLogic.Tests.Updates.UpdateStatusTests;

namespace ClientLogic.Tests.Updates;

public class UpdaterOptionsModelTests
{
    [Fact]
    public void MirrorsMoveInTheListAndTheUpdaterTogether()
    {
        TestGame.EnsureInitialized();
        var updater = new FakeUpdater();
        updater.Mirrors.AddRange([new UpdateMirror("a", "Alpha", "EU"), new UpdateMirror("b", "Beta", string.Empty)]);
        var model = new UpdaterOptionsModel(updater);

        model.Load();
        Assert.Equal(["Alpha (EU)", "Beta"], model.Mirrors);

        model.SelectedMirrorIndex = 1;
        model.MoveUp();
        Assert.Equal(["Beta", "Alpha (EU)"], model.Mirrors);
        Assert.Equal("Beta", updater.Mirrors[0].Name);
        Assert.Equal(0, model.SelectedMirrorIndex);

        // Already at the top
        model.MoveUp();
        Assert.Equal("Beta", updater.Mirrors[0].Name);

        model.MoveDown();
        Assert.Equal("Alpha", updater.Mirrors[0].Name);
        Assert.Equal(1, model.SelectedMirrorIndex);
    }
}
