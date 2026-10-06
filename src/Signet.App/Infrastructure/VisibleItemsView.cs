using System;
using System.Collections;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;
using System.Linq;
using System.Runtime.CompilerServices;
using Avalonia.Threading;

namespace Signet.App.Infrastructure;

/// <summary>
/// A read-only, observable view of the items of an <see cref="ObservableCollection{T}"/> that are visible by a
/// predicate (e.g. a node's <c>IsVisible</c>, set by a filter) — for the items / children of a hierarchical
/// <c>TreeDataGrid</c> source, which (unlike <c>TreeView</c>) cannot hide its rows: the presenter manages the rows'
/// <c>IsVisible</c> itself when recycling them.
/// </summary>
/// <remarks>
/// The view follows the source collection and the visibility property of its items; changes are coalesced into one
/// <see cref="NotifyCollectionChangedAction.Reset"/> per dispatcher cycle (a filter usually changes many items at once).
/// One view exists per source collection (<see cref="VisibleItems.For{T}"/>), so a children selector can be called
/// repeatedly. It is also
/// a read-only <see cref="IList"/>: the TreeDataGrid requires that of every observable items collection.
/// </remarks>
/// <typeparam name="T">The item type.</typeparam>
public sealed class VisibleItemsView<T> : IReadOnlyList<T>, IList, INotifyCollectionChanged
    where T : class, INotifyPropertyChanged
{
    private static readonly ConditionalWeakTable<ObservableCollection<T>, VisibleItemsView<T>> Views = new();

    private readonly ObservableCollection<T> _source;
    private readonly Func<T, bool> _isVisible;
    private readonly string _visibilityProperty;
    private readonly HashSet<T> _watched = new(ReferenceEqualityComparer.Instance);
    private List<T> _items;
    private bool _refreshPosted;

    private VisibleItemsView(ObservableCollection<T> source, Func<T, bool> isVisible, string visibilityProperty)
    {
        _source = source;
        _isVisible = isVisible;
        _visibilityProperty = visibilityProperty;
        _source.CollectionChanged += (_, _) => PostRefresh();
        WatchItems();
        _items = _source.Where(_isVisible).ToList();
    }

    /// <inheritdoc />
    public event NotifyCollectionChangedEventHandler? CollectionChanged;

    /// <inheritdoc />
    public int Count => _items.Count;

    /// <inheritdoc />
    public T this[int index] => _items[index];

    bool IList.IsFixedSize => false;

    bool IList.IsReadOnly => true;

    bool ICollection.IsSynchronized => false;

    object ICollection.SyncRoot => this;

    object? IList.this[int index]
    {
        get => _items[index];
        set => throw new NotSupportedException();
    }

    // See VisibleItems.For.
    internal static VisibleItemsView<T> GetOrCreate(ObservableCollection<T> source, Func<T, bool> isVisible, string visibilityProperty) =>
        Views.GetValue(source, s => new VisibleItemsView<T>(s, isVisible, visibilityProperty));

    /// <inheritdoc />
    public IEnumerator<T> GetEnumerator() => _items.GetEnumerator();

    IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();

    bool IList.Contains(object? value) => value is T item && _items.Contains(item);

    int IList.IndexOf(object? value) => value is T item ? _items.IndexOf(item) : -1;

    void ICollection.CopyTo(Array array, int index) => ((ICollection)_items).CopyTo(array, index);

    int IList.Add(object? value) => throw new NotSupportedException();

    void IList.Clear() => throw new NotSupportedException();

    void IList.Insert(int index, object? value) => throw new NotSupportedException();

    void IList.Remove(object? value) => throw new NotSupportedException();

    void IList.RemoveAt(int index) => throw new NotSupportedException();

    private void WatchItems()
    {
        foreach (T item in _source)
        {
            if (_watched.Add(item))
            {
                item.PropertyChanged += OnItemPropertyChanged;
            }
        }
    }

    private void OnItemPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == _visibilityProperty)
        {
            PostRefresh();
        }
    }

    private void PostRefresh()
    {
        if (!_refreshPosted)
        {
            _refreshPosted = true;
            Dispatcher.UIThread.Post(Refresh);
        }
    }

    private void Refresh()
    {
        _refreshPosted = false;

        // Stop watching removed items, watch new ones.
        foreach (T gone in _watched.Where(w => !_source.Contains(w)).ToList())
        {
            gone.PropertyChanged -= OnItemPropertyChanged;
            _watched.Remove(gone);
        }

        WatchItems();
        List<T> items = _source.Where(_isVisible).ToList();
        if (items.SequenceEqual(_items, ReferenceEqualityComparer.Instance))
        {
            return;
        }

        _items = items;
        CollectionChanged?.Invoke(this, new NotifyCollectionChangedEventArgs(NotifyCollectionChangedAction.Reset));
    }
}

/// <summary>Creates the <see cref="VisibleItemsView{T}"/> of collections.</summary>
public static class VisibleItems
{
    /// <summary>
    /// The view of <paramref name="source"/> (created on the first call, then the same one): the items for which
    /// <paramref name="isVisible"/> is true, re-evaluated when an item raises a change of
    /// <paramref name="visibilityProperty"/>.
    /// </summary>
    public static VisibleItemsView<T> For<T>(ObservableCollection<T> source, Func<T, bool> isVisible, string visibilityProperty)
        where T : class, INotifyPropertyChanged
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(isVisible);
        ArgumentException.ThrowIfNullOrEmpty(visibilityProperty);
        return VisibleItemsView<T>.GetOrCreate(source, isVisible, visibilityProperty);
    }
}
