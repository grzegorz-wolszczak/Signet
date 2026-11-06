using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace Signet.EpubCheck.Core.Ocf;

/// <summary>An EPUB source backed by an expanded EPUB directory (upstream's <c>OCFDirectoryResources</c>).</summary>
public sealed class DirectoryEpubSource : IEpubSource
{
    /// <summary>The text of the error a symbolic link in the directory causes (upstream's).</summary>
    internal const string SymbolicLinkMessage = "symbolic links are disallowed";

    private readonly string[] _paths;

    /// <summary>Opens an expanded EPUB directory. Entries are in ordinal order of their paths; a directory ends with <c>/</c>.</summary>
    /// <exception cref="DirectoryNotFoundException">The directory does not exist.</exception>
    /// <exception cref="IOException">The directory contains a symbolic link (upstream does not follow them either).</exception>
    public DirectoryEpubSource(string root)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(root);
        Root = Path.GetFullPath(root);
        if (!Directory.Exists(Root))
        {
            throw new DirectoryNotFoundException(Root);
        }

        EnumerationOptions options = new() { RecurseSubdirectories = true, AttributesToSkip = 0, IgnoreInaccessible = false };
        List<(string Name, FileSystemInfo Info)> found = [];
        foreach (FileSystemInfo info in new DirectoryInfo(Root).EnumerateFileSystemInfos("*", options))
        {
            if (info.LinkTarget is not null)
            {
                throw new IOException(SymbolicLinkMessage);
            }

            string name = Path.GetRelativePath(Root, info.FullName).Replace(Path.DirectorySeparatorChar, '/');
            found.Add((info is DirectoryInfo ? name + "/" : name, info));
        }

        found.Sort((left, right) => StringComparer.Ordinal.Compare(left.Name, right.Name));
        _paths = found.Select(entry => entry.Info.FullName).ToArray();
        Entries = found.Select((entry, order) =>
        {
            bool isDirectory = entry.Info is DirectoryInfo;
            long length = entry.Info is FileInfo file ? file.Length : 0;
            return new EpubSourceEntry(entry.Name, order, CompressionMethod.Stored, length, length, isDirectory, entry.Info.CreationTimeUtc);
        }).ToArray();
    }

    /// <summary>The full path of the directory.</summary>
    public string Root { get; }

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

        return File.OpenRead(_paths[entry.Order]);
    }

    /// <inheritdoc />
    public void Dispose()
    {
    }
}
