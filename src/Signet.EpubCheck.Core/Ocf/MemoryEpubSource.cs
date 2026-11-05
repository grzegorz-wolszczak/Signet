using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace Signet.EpubCheck.Core.Ocf;

/// <summary>An EPUB source backed by bytes, intended for validating unsaved in-memory edits.</summary>
public sealed class MemoryEpubSource : IEpubSource
{
    private readonly Dictionary<EpubSourceEntry, byte[]> _contents;

    /// <summary>Creates an expanded source from named byte arrays.</summary>
    public MemoryEpubSource(IEnumerable<KeyValuePair<string, byte[]>> entries)
    {
        ArgumentNullException.ThrowIfNull(entries);
        _contents = entries.Select((entry, order) =>
        {
            ArgumentNullException.ThrowIfNull(entry.Key);
            ArgumentNullException.ThrowIfNull(entry.Value);
            EpubSourceEntry metadata = new(entry.Key, order, CompressionMethod.Stored, entry.Value.Length, entry.Value.Length, entry.Key.EndsWith('/'));
            return new KeyValuePair<EpubSourceEntry, byte[]>(metadata, entry.Value);
        }).ToDictionary();
        Entries = _contents.Keys.OrderBy(entry => entry.Order).ToArray();
    }

    /// <inheritdoc />
    public bool IsPackaged => false;

    /// <inheritdoc />
    public IReadOnlyList<EpubSourceEntry> Entries { get; }

    /// <inheritdoc />
    public Stream OpenRead(EpubSourceEntry entry)
    {
        ArgumentNullException.ThrowIfNull(entry);
        return new MemoryStream(_contents[entry], writable: false);
    }

    /// <inheritdoc />
    public void Dispose()
    {
    }
}
