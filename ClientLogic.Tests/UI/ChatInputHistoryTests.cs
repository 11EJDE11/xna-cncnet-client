using ClientLogic.UI;
using Xunit;

namespace ClientLogic.Tests.UI;

public class ChatInputHistoryTests
{
    [Fact]
    public void NavigationStopsAtBothEndsWithoutReturningAnEmptyDraft()
    {
        var history = new ChatInputHistory();
        Assert.Null(history.Older());
        Assert.Null(history.Newer());
        history.Record("first");
        history.Record("second");
        Assert.Null(history.Newer());
        Assert.Equal("second", history.Older());
        Assert.Equal("first", history.Older());
        Assert.Equal("first", history.Older());
        Assert.Equal("second", history.Newer());
        Assert.Equal("second", history.Newer());
    }

    [Fact]
    public void OtherKeysRestartNavigationAndEmptyMessagesAreNotRecorded()
    {
        var history = new ChatInputHistory();
        history.Record("first");
        history.Record("second");
        history.Record(string.Empty);
        history.Record(null);
        history.Older();
        history.Older();
        history.ResetNavigation();
        Assert.Null(history.Newer());
        Assert.Equal("second", history.Older());
        history.ResetNavigation();
        history.Record("third");
        Assert.Equal("third", history.Older());
    }

    [Fact]
    public void RepeatedMessagesRemainSeparateHistoryEntries()
    {
        var history = new ChatInputHistory();
        history.Record("first");
        history.Record("repeat");
        history.Record("repeat");
        Assert.Equal("repeat", history.Older());
        Assert.Equal("repeat", history.Older());
        Assert.Equal("first", history.Older());
    }
}
