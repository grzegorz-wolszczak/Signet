using System;
using System.Collections.Generic;

namespace Signet.Core.Search;

/// <summary>
/// Cache of compiled <see cref="Spcre"/> expressions keyed by pattern: a simple LRU
/// with a capacity of <see cref="Capacity"/> entries.
/// </summary>
/// <remarks>
/// The class is thread-safe. Besides the shared <see cref="Instance"/>, separate instances
/// can be created (useful in tests) — that is the only reason the constructor is public.
/// </remarks>
public sealed class PcreCache
{
    /// <summary>Maximum number of stored entries.</summary>
    public const int Capacity = 20;

    private static readonly PcreCache SharedInstance = new();

    private readonly object _gate = new();
    private readonly Dictionary<string, LinkedListNode<Entry>> _map = new(StringComparer.Ordinal);
    private readonly LinkedList<Entry> _lru = new();

    /// <summary>The shared instance.</summary>
    public static PcreCache Instance => SharedInstance;

    /// <summary>Number of entries currently in the cache.</summary>
    public int Count
    {
        get
        {
            lock (_gate)
            {
                return _map.Count;
            }
        }
    }

    /// <summary>
    /// Gets the <see cref="Spcre"/> for the pattern <paramref name="key"/>. If it is not cached,
    /// it is compiled, inserted and returned.
    /// </summary>
    public Spcre GetObject(string key)
    {
        ArgumentNullException.ThrowIfNull(key);

        lock (_gate)
        {
            if (_map.TryGetValue(key, out LinkedListNode<Entry>? node))
            {
                _lru.Remove(node);
                _lru.AddFirst(node);
                return node.Value.Value;
            }

            var spcre = new Spcre(key);
            InsertLocked(key, spcre);
            return spcre;
        }
    }

    /// <summary>Inserts <see cref="Spcre"/> under the key <paramref name="key"/>.</summary>
    /// <returns>Always <c>true</c>.</returns>
    public bool Insert(string key, Spcre spcre)
    {
        ArgumentNullException.ThrowIfNull(key);
        ArgumentNullException.ThrowIfNull(spcre);

        lock (_gate)
        {
            InsertLocked(key, spcre);
        }

        return true;
    }

    /// <summary>Removes all entries.</summary>
    public void Clear()
    {
        lock (_gate)
        {
            _map.Clear();
            _lru.Clear();
        }
    }

    private void InsertLocked(string key, Spcre spcre)
    {
        if (_map.TryGetValue(key, out LinkedListNode<Entry>? existing))
        {
            _lru.Remove(existing);
            _map.Remove(key);
        }

        var node = new LinkedListNode<Entry>(new Entry(key, spcre));
        _lru.AddFirst(node);
        _map[key] = node;

        while (_map.Count > Capacity && _lru.Last is { } last)
        {
            _lru.RemoveLast();
            _map.Remove(last.Value.Key);
        }
    }

    private readonly struct Entry
    {
        public Entry(string key, Spcre value)
        {
            Key = key;
            Value = value;
        }

        public string Key { get; }

        public Spcre Value { get; }
    }
}
