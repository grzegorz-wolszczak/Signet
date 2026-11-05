namespace Signet.EpubCheck.Core.Ocf;

/// <summary>Metadata of one resource in an EPUB source.</summary>
public sealed record EpubSourceEntry(
    string Name,
    int Order,
    CompressionMethod CompressionMethod,
    long CompressedLength,
    long Length,
    bool IsDirectory);

/// <summary>The compression method declared by an EPUB source entry.</summary>
public enum CompressionMethod
{
    /// <summary>The bytes are stored without compression.</summary>
    Stored = 0,

    /// <summary>The bytes use the ZIP deflate method.</summary>
    Deflate = 8,

    /// <summary>A ZIP method not represented by <see cref="System.IO.Compression.CompressionLevel"/>.</summary>
    Other = -1,
}
