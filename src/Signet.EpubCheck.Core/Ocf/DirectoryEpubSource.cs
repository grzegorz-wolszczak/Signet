using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace Signet.EpubCheck.Core.Ocf;

/// <summary>An EPUB source backed by an expanded EPUB directory.</summary>
public sealed class DirectoryEpubSource : IEpubSource
{
    private readonly string _root;
    private readonly Dictionary<EpubSourceEntry, string> _paths;

    /// <summary>Opens an expanded EPUB directory.</summary>
    public DirectoryEpubSource(string root)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(root);
        _root = Path.GetFullPath(root);
        if (!Directory.Exists(_root))
        {
            throw new DirectoryNotFoundException(_root);
        }

        string[] paths = Directory.EnumerateFileSystemEntries(_root, "*", SearchOption.AllDirectories)
            .OrderBy(path => path, StringComparer.Ordinal)
            .ToArray();
        _paths = paths.Select((path, order) =>
        {
            bool isDirectory = Directory.Exists(path);
            string name = Path.GetRelativePath(_root, path).Replace(Path.DirectorySeparatorChar, '/');
            if (isDirectory)
            {
                name += "/";
            }

            long length = isDirectory ? 0 : new FileInfo(path).Length;
            EpubSourceEntry entry = new(name, order, CompressionMethod.Stored, length, length, isDirectory);
            return new KeyValuePair<EpubSourceEntry, string>(entry, path);
        }).ToDictionary();
        Entries = _paths.Keys.OrderBy(entry => entry.Order).ToArray();
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

        return File.OpenRead(_paths[entry]);
    }

    /// <inheritdoc />
    public void Dispose()
    {
    }
}
