using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;

namespace ClientLogic.UI;

/// <summary>Updates a live list without resetting rows that are still present (and their selection/popups).</summary>
public static class ObservableCollectionSync
{
    public static void Synchronize<T, TKey>(ObservableCollection<T> items, IReadOnlyList<T> desired, Func<T, TKey> key)
    {
        var keys = new HashSet<TKey>(desired.Select(key));
        for (int i = items.Count - 1; i >= 0; i--)
            if (!keys.Contains(key(items[i])))
                items.RemoveAt(i);

        for (int i = 0; i < desired.Count; i++)
        {
            TKey desiredKey = key(desired[i]);
            int existing = i;
            while (existing < items.Count && !EqualityComparer<TKey>.Default.Equals(key(items[existing]), desiredKey))
                existing++;

            if (existing == items.Count)
                items.Insert(i, desired[i]);
            else
            {
                if (existing != i)
                    items.Move(existing, i);
                if (!EqualityComparer<T>.Default.Equals(items[i], desired[i]))
                    items[i] = desired[i];
            }
        }
    }
}
