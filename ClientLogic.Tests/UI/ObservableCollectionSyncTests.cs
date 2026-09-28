using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Collections.Specialized;
using ClientLogic.UI;
using Xunit;

namespace ClientLogic.Tests.UI;

public class ObservableCollectionSyncTests
{
    private sealed record Player(string Name, bool Friend = false);

    [Fact]
    public void JoinAndLeaveKeepUnchangedRowsWithoutResettingTheList()
    {
        var selected = new Player("Bob");
        var items = new ObservableCollection<Player> { new("Alice"), selected };
        var changes = new List<NotifyCollectionChangedAction>();
        items.CollectionChanged += (_, e) => changes.Add(e.Action);
        ObservableCollectionSync.Synchronize(items, new[] { new Player("Bob"), new Player("Carol") }, p => p.Name);

        Assert.Same(selected, items[0]);
        Assert.Equal("Carol", items[1].Name);
        Assert.Equal(new[] { NotifyCollectionChangedAction.Remove, NotifyCollectionChangedAction.Add }, changes);
    }

    [Fact]
    public void SortingMovesTheExistingRowAndStatusChangesReplaceOnlyThatRow()
    {
        var alice = new Player("Alice");
        var bob = new Player("Bob");
        var items = new ObservableCollection<Player> { alice, bob };
        var changes = new List<NotifyCollectionChangedAction>();
        items.CollectionChanged += (_, e) => changes.Add(e.Action);
        ObservableCollectionSync.Synchronize(items, new[] { new Player("Bob"), new Player("Alice", true) }, p => p.Name);

        Assert.Same(bob, items[0]);
        Assert.True(items[1].Friend);
        Assert.Equal(new[] { NotifyCollectionChangedAction.Move, NotifyCollectionChangedAction.Replace }, changes);
    }

    [Fact]
    public void IdenticalSnapshotDoesNotNotifyOrReplaceRows()
    {
        var player = new Player("Alice");
        var items = new ObservableCollection<Player> { player };
        items.CollectionChanged += (_, _) => Assert.Fail("Unchanged snapshot should preserve the row.");
        ObservableCollectionSync.Synchronize(items, new[] { new Player("Alice") }, p => p.Name);
        Assert.Same(player, items[0]);
    }
}
