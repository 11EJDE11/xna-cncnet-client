using System.Collections.Specialized;
using System.Linq;
using System.Runtime.CompilerServices;

using Avalonia;
using Avalonia.Controls;
using Avalonia.Threading;
using Avalonia.VisualTree;

namespace AvClientView.Views;

/// <summary>
/// <c>views:ChatScroll.FollowNewItems="True"</c> on a list box keeps the newest item in view, as the XNA chat lists do.
/// </summary>
public static class ChatScroll
{
    private static readonly ConditionalWeakTable<ListBox, Subscription> subscriptions = new();
    public static readonly AttachedProperty<bool> FollowNewItemsProperty =
        AvaloniaProperty.RegisterAttached<ListBox, bool>("FollowNewItems", typeof(ChatScroll));

    static ChatScroll()
    {
        FollowNewItemsProperty.Changed.AddClassHandler<ListBox>((listBox, e) =>
        {
            subscriptions.GetValue(listBox, list => new Subscription(list)).Update();
        });
    }

    public static bool GetFollowNewItems(ListBox listBox) => listBox.GetValue(FollowNewItemsProperty);

    public static void SetFollowNewItems(ListBox listBox, bool value) => listBox.SetValue(FollowNewItemsProperty, value);

    private sealed class Subscription
    {
        private readonly ListBox list;
        private INotifyCollectionChanged source;
        private bool attached;
        private bool pending;
        private int generation;

        public Subscription(ListBox list)
        {
            this.list = list;
            attached = TopLevel.GetTopLevel(list) != null;
            list.AttachedToVisualTree += (_, _) => { attached = true; Update(); };
            list.DetachedFromVisualTree += (_, _) => { attached = false; Update(); };
            list.PropertyChanged += (_, e) =>
            {
                if (e.Property == ItemsControl.ItemsSourceProperty)
                    Update();
            };
        }

        public void Update()
        {
            if (source != null)
                source.CollectionChanged -= ItemsChanged;
            generation++;
            pending = false;
            source = attached && GetFollowNewItems(list) ? list.ItemsSource as INotifyCollectionChanged : null;
            if (source != null)
                source.CollectionChanged += ItemsChanged;
        }

        private void ItemsChanged(object sender, NotifyCollectionChangedEventArgs e)
        {
            if (e.Action == NotifyCollectionChangedAction.Reset)
            {
                generation++;
                pending = false;
                return;
            }
            if (e.Action != NotifyCollectionChangedAction.Add || list.ItemCount == 0 || pending)
                return;

            ScrollViewer scroll = list.GetVisualDescendants().OfType<ScrollViewer>().FirstOrDefault();
            double offset = scroll?.Offset.Y ?? 0;
            // XNA ChatListBox only follows a new message when the previous last message was visible.
            if (scroll != null && offset + scroll.Viewport.Height < scroll.Extent.Height - 1)
                return;

            pending = true;
            int queuedGeneration = generation;
            Dispatcher.UIThread.Post(() =>
            {
                if (queuedGeneration != generation)
                    return;
                pending = false;
                // Do not undo a scroll upwards while this callback was waiting for layout.
                if (list.ItemCount > 0 && (scroll == null || scroll.Offset.Y >= offset - 1))
                    list.ScrollIntoView(list.ItemCount - 1);
            }, DispatcherPriority.Background);
        }
    }
}
