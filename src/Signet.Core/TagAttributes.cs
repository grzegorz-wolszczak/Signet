using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;

namespace Signet.Core;

/// <summary>
/// An ordered (by insertion order) map of tag attributes: key -&gt; value,
/// both of type <see cref="string"/>.
/// </summary>
/// <remarks>
/// Inserting an existing key updates its value <b>in place</b> (the position does not change),
/// like <c>operator[]</c> on an ordered map. The order matters for
/// faithfully writing the OPF back to XML.
/// </remarks>
public sealed class TagAttributes : IEnumerable<KeyValuePair<string, string>>, IEquatable<TagAttributes>
{
    private readonly List<string> _order = new();
    private readonly Dictionary<string, string> _map = new(StringComparer.Ordinal);

    /// <summary>Creates an empty attribute map.</summary>
    public TagAttributes()
    {
    }

    /// <summary>Creates a copy (preserving the key order) of another attribute map.</summary>
    public TagAttributes(TagAttributes other)
    {
        ArgumentNullException.ThrowIfNull(other);
        foreach (string key in other._order)
        {
            Set(key, other._map[key]);
        }
    }

    /// <summary>The number of attributes.</summary>
    public int Count => _order.Count;

    /// <summary>Whether the map is empty.</summary>
    public bool IsEmpty => _order.Count == 0;

    /// <summary>The keys in insertion order.</summary>
    public IReadOnlyList<string> Keys => _order;

    /// <summary>
    /// Read: the attribute value or <c>""</c> if the key is absent.
    /// Write: <see cref="Set"/>.
    /// </summary>
    public string this[string key]
    {
        get => Value(key);
        set => Set(key, value);
    }

    /// <summary>
    /// Inserts an attribute or updates its value. An update does not change the key position.
    /// </summary>
    public void Set(string key, string value)
    {
        ArgumentNullException.ThrowIfNull(key);
        ArgumentNullException.ThrowIfNull(value);
        if (!_map.ContainsKey(key))
        {
            _order.Add(key);
        }

        _map[key] = value;
    }

    /// <summary>Removes an attribute if it exists.</summary>
    public void Remove(string key)
    {
        ArgumentNullException.ThrowIfNull(key);
        if (_map.Remove(key))
        {
            _order.Remove(key);
        }
    }

    /// <summary>Whether an attribute with the given key exists.</summary>
    public bool Contains(string key)
    {
        ArgumentNullException.ThrowIfNull(key);
        return _map.ContainsKey(key);
    }

    /// <summary>The attribute value or <paramref name="fallback"/> if the key is absent.</summary>
    public string Value(string key, string fallback = "")
    {
        ArgumentNullException.ThrowIfNull(key);
        return _map.TryGetValue(key, out string? existing) ? existing : fallback;
    }

    /// <summary>The key/value pairs in insertion order.</summary>
    public IReadOnlyList<KeyValuePair<string, string>> Pairs() =>
        _order.Select(k => new KeyValuePair<string, string>(k, _map[k])).ToList();

    /// <inheritdoc/>
    public IEnumerator<KeyValuePair<string, string>> GetEnumerator()
    {
        foreach (string key in _order)
        {
            yield return new KeyValuePair<string, string>(key, _map[key]);
        }
    }

    IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();

    /// <inheritdoc/>
    public bool Equals(TagAttributes? other)
    {
        if (other is null || other._order.Count != _order.Count)
        {
            return false;
        }

        for (int i = 0; i < _order.Count; i++)
        {
            string key = _order[i];
            if (!string.Equals(key, other._order[i], StringComparison.Ordinal) ||
                !string.Equals(_map[key], other._map[key], StringComparison.Ordinal))
            {
                return false;
            }
        }

        return true;
    }

    /// <inheritdoc/>
    public override bool Equals(object? obj) => Equals(obj as TagAttributes);

    /// <inheritdoc/>
    public override int GetHashCode()
    {
        HashCode hash = new();
        foreach (string key in _order)
        {
            hash.Add(key, StringComparer.Ordinal);
            hash.Add(_map[key], StringComparer.Ordinal);
        }

        return hash.ToHashCode();
    }
}
