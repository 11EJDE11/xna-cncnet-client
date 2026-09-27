using System.Collections.Specialized;

using Avalonia;
using Avalonia.Controls;
using Avalonia.Threading;

namespace AvClientView.Views;

/// <summary>
/// <c>views:ChatScroll.FollowNewItems="True"</c> on a list box keeps the newest item in view, as the XNA chat lists do.
/// </summary>
public static class ChatScroll
{
    public static readonly AttachedProperty<bool> FollowNewItemsProperty =
        AvaloniaProperty.RegisterAttached<ListBox, bool>("FollowNewItems", typeof(ChatScroll));

    static ChatScroll()
    {
        FollowNewItemsProperty.Changed.AddClassHandler<ListBox>((listBox, e) =>
        {
            if (e.NewValue is true)
                listBox.PropertyChanged += (_, args) =>
                {
                    if (args.Property == ItemsControl.ItemsSourceProperty)
                        Subscribe(listBox);
                };

            Subscribe(listBox);
        });
    }

    public static bool GetFollowNewItems(ListBox listBox) => listBox.GetValue(FollowNewItemsProperty);

    public static void SetFollowNewItems(ListBox listBox, bool value) => listBox.SetValue(FollowNewItemsProperty, value);

    private static void Subscribe(ListBox listBox)
    {
        if (listBox.ItemsSource is not INotifyCollectionChanged items)
            return;

        items.CollectionChanged += (_, e) =>
        {
            if (e.Action == NotifyCollectionChangedAction.Add && listBox.ItemCount > 0)
                Dispatcher.UIThread.Post(() => listBox.ScrollIntoView(listBox.ItemCount - 1), DispatcherPriority.Background);
        };
    }
}
