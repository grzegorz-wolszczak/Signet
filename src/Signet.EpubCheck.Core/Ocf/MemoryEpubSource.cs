using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace Signet.EpubCheck.Core.Ocf;

/// <summary>An expanded EPUB source backed by bytes, intended for validating unsaved in-memory edits.</summary>
public sealed class MemoryEpubSource : IEpubSource
{
    private readonly byte[][] _contents;

    /// <summary>Creates an expanded source from named byte arrays, in the given order; a name ending with <c>/</c> is a directory.</summary>
    public MemoryEpubSource(IEnumerable<KeyValuePair<string, byte[]>> entries)
    {
        ArgumentNullException.ThrowIfNull(entries);
        KeyValuePair<string, byte[]>[] items = entries.ToArray();
        foreach ((string name, byte[] content) in items)
        {
            ArgumentNullException.ThrowIfNull(name);
            ArgumentNullException.ThrowIfNull(content);
        }

        _contents = items.Select(item => item.Value).ToArray();
        Entries = items.Select((item, order) => new EpubSourceEntry(
            item.Key, order, CompressionMethod.Stored, item.Value.Length, item.Value.Length, item.Key.EndsWith('/'))).ToArray();
    }

    /// <inheritdoc />
    public bool IsPackaged => false;

    /// <inheritdoc />
    public IReadOnlyList<EpubSourceEntry> Entries { get; }

    /// <inheritdoc />
    public Stream OpenRead(EpubSourceEntry entry)
    {
        ArgumentNullException.ThrowIfNull(entry);
        if (entry.IsDirectory)
        {
            throw new InvalidOperationException("A directory cannot be opened for reading.");
        }

        if ((uint)entry.Order >= (uint)Entries.Count || !Entries[entry.Order].Equals(entry))
        {
            throw new ArgumentException("The entry does not belong to this source.", nameof(entry));
        }

        return new MemoryStream(_contents[entry.Order], writable: false);
    }

    /// <inheritdoc />
    public void Dispose()
    {
    }
}
