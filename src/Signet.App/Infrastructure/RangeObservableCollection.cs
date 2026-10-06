using System;
using System.Collections;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;
using System.Linq;

namespace Signet.App.Infrastructure;

/// <summary>
/// An <see cref="ObservableCollection{T}"/> that inserts, removes and replaces ranges of items with a single
/// <see cref="INotifyCollectionChanged.CollectionChanged"/> event (Avalonia's items controls handle multi-item
/// <c>Add</c> / <c>Remove</c> events). For large lists — e.g. the visible rows of a flattened tree, where expanding a
/// node inserts thousands of rows — raising one event per item would be quadratic.
/// </summary>
public sealed class RangeObservableCollection<T> : ObservableCollection<T>
{
    private static readonly PropertyChangedEventArgs CountChanged = new(nameof(Count));
    private static readonly PropertyChangedEventArgs IndexerChanged = new("Item[]");

    /// <summary>Inserts <paramref name="items"/> at <paramref name="index"/> (one <c>Add</c> event).</summary>
    public void InsertRange(int index, IEnumerable<T> items)
    {
        ArgumentNullException.ThrowIfNull(items);
        ArgumentOutOfRangeException.ThrowIfNegative(index);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(index, Count);
        CheckReentrancy();
        List<T> added = items.ToList();
        if (added.Count == 0)
        {
            return;
        }

        ItemList.InsertRange(index, added);
        RaiseChanged(new NotifyCollectionChangedEventArgs(NotifyCollectionChangedAction.Add, (IList)added, index));
    }

    /// <summary>Removes <paramref name="count"/> items from <paramref name="index"/> (one <c>Remove</c> event).</summary>
    public void RemoveRange(int index, int count)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(index);
        ArgumentOutOfRangeException.ThrowIfNegative(count);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(index + count, Count);
        CheckReentrancy();
        if (count == 0)
        {
            return;
        }

        List<T> removed = ItemList.GetRange(index, count);
        ItemList.RemoveRange(index, count);
        RaiseChanged(new NotifyCollectionChangedEventArgs(NotifyCollectionChangedAction.Remove, (IList)removed, index));
    }

    /// <summary>Replaces the whole content with <paramref name="items"/> (one <c>Reset</c> event).</summary>
    public void ResetTo(IEnumerable<T> items)
    {
        ArgumentNullException.ThrowIfNull(items);
        CheckReentrancy();
        ItemList.Clear();
        ItemList.AddRange(items);
        RaiseChanged(new NotifyCollectionChangedEventArgs(NotifyCollectionChangedAction.Reset));
    }

    // Collection<T>() wraps a List<T>.
    private List<T> ItemList => (List<T>)Items;

    private void RaiseChanged(NotifyCollectionChangedEventArgs e)
    {
        OnPropertyChanged(CountChanged);
        OnPropertyChanged(IndexerChanged);
        OnCollectionChanged(e);
    }
}
