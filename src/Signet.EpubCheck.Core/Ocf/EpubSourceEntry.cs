using System;

namespace Signet.EpubCheck.Core.Ocf;

/// <summary>Metadata of one resource in an EPUB source.</summary>
/// <param name="Name">The path of the resource in the container, with <c>/</c> separators; a directory ends with <c>/</c>.</param>
/// <param name="Order">The position of the resource in the source (the order of the ZIP central directory).</param>
/// <param name="CompressionMethod">How the resource is stored.</param>
/// <param name="CompressedLength">The number of bytes the resource takes in the source.</param>
/// <param name="Length">The number of bytes of the resource.</param>
/// <param name="IsDirectory">Whether the entry is a directory.</param>
/// <param name="Timestamp">
/// The time stamp of the resource as the source records it (a ZIP entry: its modification time, as written; a file: its
/// creation time, UTC), or <c>null</c> when it has none.
/// </param>
public sealed record EpubSourceEntry(
    string Name,
    int Order,
    CompressionMethod CompressionMethod,
    long CompressedLength,
    long Length,
    bool IsDirectory,
    DateTime? Timestamp = null);

/// <summary>The compression method declared by an EPUB source entry.</summary>
public enum CompressionMethod
{
    /// <summary>The bytes are stored without compression.</summary>
    Stored = 0,

    /// <summary>The bytes use the ZIP deflate method.</summary>
    Deflate = 8,

    /// <summary>A ZIP method other than stored and deflate (upstream reports it as <c>Unsupported</c>).</summary>
    Other = -1,
}
