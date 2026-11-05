using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text;

namespace Signet.EpubCheck.Core.Ocf;

/// <summary>An EPUB source backed by a ZIP file.</summary>
public sealed class ZipEpubSource : IEpubSource
{
    private const uint CentralDirectorySignature = 0x02014B50;
    private const uint EndOfCentralDirectorySignature = 0x06054B50;
    private const uint LocalFileHeaderSignature = 0x04034B50;
    private readonly FileStream _stream;
    private readonly ZipArchive _archive;
    private readonly Dictionary<EpubSourceEntry, ZipArchiveEntry> _entries;

    /// <summary>Opens a ZIP file as an EPUB source.</summary>
    public ZipEpubSource(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        Path = System.IO.Path.GetFullPath(path);
        byte[] bytes = File.ReadAllBytes(Path);
        List<CentralDirectoryEntry> centralEntries = ReadCentralDirectory(bytes);
        if (centralEntries.Count == 0)
        {
            throw new InvalidDataException("ZIP file contains no entries.");
        }

        FirstEntryName = centralEntries[0].Name;
        FirstEntryExtraLength = ReadLocalHeaderExtraLength(bytes, centralEntries[0].LocalHeaderOffset);

        _stream = File.Open(Path, FileMode.Open, FileAccess.Read, FileShare.Read);
        try
        {
            _archive = new ZipArchive(_stream, ZipArchiveMode.Read, leaveOpen: false, entryNameEncoding: Encoding.UTF8);
            if (_archive.Entries.Count != centralEntries.Count)
            {
                throw new InvalidDataException("ZIP central directory entries could not be read.");
            }

            _entries = centralEntries.Select((entry, order) =>
            {
                bool isDirectory = entry.Name.EndsWith('/');
                CompressionMethod method = entry.CompressionMethod switch
                {
                    0 => CompressionMethod.Stored,
                    8 => CompressionMethod.Deflate,
                    _ => CompressionMethod.Other,
                };
                EpubSourceEntry metadata = new(entry.Name, order, method, entry.CompressedLength, entry.Length, isDirectory);
                return new KeyValuePair<EpubSourceEntry, ZipArchiveEntry>(metadata, _archive.Entries[order]);
            }).ToDictionary();
            Entries = _entries.Keys.OrderBy(entry => entry.Order).ToArray();
        }
        catch
        {
            _stream.Dispose();
            throw;
        }
    }

    /// <summary>The full path of the archive.</summary>
    public string Path { get; }

    /// <summary>The name of the first ZIP entry, decoded from the central directory.</summary>
    public string FirstEntryName { get; }

    /// <summary>The extra-field length declared in the first entry's local header.</summary>
    public int FirstEntryExtraLength { get; }

    /// <inheritdoc />
    public bool IsPackaged => true;

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

        return _entries[entry].Open();
    }

    /// <inheritdoc />
    public void Dispose()
    {
        _archive.Dispose();
    }

    private static List<CentralDirectoryEntry> ReadCentralDirectory(byte[] bytes)
    {
        int end = FindEndOfCentralDirectory(bytes);
        int count = BinaryPrimitives.ReadUInt16LittleEndian(bytes.AsSpan(end + 10, 2));
        int offset = checked((int)BinaryPrimitives.ReadUInt32LittleEndian(bytes.AsSpan(end + 16, 4)));
        List<CentralDirectoryEntry> entries = new(count);
        for (int i = 0; i < count; i++)
        {
            EnsureRange(bytes, offset, 46);
            if (BinaryPrimitives.ReadUInt32LittleEndian(bytes.AsSpan(offset, 4)) != CentralDirectorySignature)
            {
                throw new InvalidDataException("ZIP central directory is invalid.");
            }

            ushort flags = BinaryPrimitives.ReadUInt16LittleEndian(bytes.AsSpan(offset + 8, 2));
            ushort method = BinaryPrimitives.ReadUInt16LittleEndian(bytes.AsSpan(offset + 10, 2));
            uint compressedLength = BinaryPrimitives.ReadUInt32LittleEndian(bytes.AsSpan(offset + 20, 4));
            uint length = BinaryPrimitives.ReadUInt32LittleEndian(bytes.AsSpan(offset + 24, 4));
            int nameLength = BinaryPrimitives.ReadUInt16LittleEndian(bytes.AsSpan(offset + 28, 2));
            int extraLength = BinaryPrimitives.ReadUInt16LittleEndian(bytes.AsSpan(offset + 30, 2));
            int commentLength = BinaryPrimitives.ReadUInt16LittleEndian(bytes.AsSpan(offset + 32, 2));
            uint localHeaderOffset = BinaryPrimitives.ReadUInt32LittleEndian(bytes.AsSpan(offset + 42, 4));
            EnsureRange(bytes, offset + 46, checked(nameLength + extraLength + commentLength));
            ReadOnlySpan<byte> rawName = bytes.AsSpan(offset + 46, nameLength);
            if ((flags & 0x0800) == 0 && rawName.IndexOfAnyInRange((byte)0x80, byte.MaxValue) >= 0)
            {
                throw new EpubZipEncodingException();
            }

            string name;
            try
            {
                name = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false, throwOnInvalidBytes: true).GetString(rawName);
            }
            catch (DecoderFallbackException ex)
            {
                throw new EpubZipEncodingException(ex);
            }

            entries.Add(new CentralDirectoryEntry(name, method, compressedLength, length, localHeaderOffset));
            offset += 46 + nameLength + extraLength + commentLength;
        }

        return entries;
    }

    private static int FindEndOfCentralDirectory(byte[] bytes)
    {
        int minimumLength = 22;
        if (bytes.Length < minimumLength)
        {
            throw new InvalidDataException("ZIP file is too short.");
        }

        int first = Math.Max(0, bytes.Length - minimumLength - ushort.MaxValue);
        for (int index = bytes.Length - minimumLength; index >= first; index--)
        {
            if (BinaryPrimitives.ReadUInt32LittleEndian(bytes.AsSpan(index, 4)) == EndOfCentralDirectorySignature)
            {
                return index;
            }
        }

        throw new InvalidDataException("ZIP end of central directory record is missing.");
    }

    private static int ReadLocalHeaderExtraLength(byte[] bytes, uint localHeaderOffset)
    {
        int offset = checked((int)localHeaderOffset);
        EnsureRange(bytes, offset, 30);
        if (BinaryPrimitives.ReadUInt32LittleEndian(bytes.AsSpan(offset, 4)) != LocalFileHeaderSignature)
        {
            throw new InvalidDataException("ZIP local file header is invalid.");
        }

        return BinaryPrimitives.ReadUInt16LittleEndian(bytes.AsSpan(offset + 28, 2));
    }

    private static void EnsureRange(byte[] bytes, int offset, int length)
    {
        if (offset < 0 || length < 0 || offset > bytes.Length - length)
        {
            throw new InvalidDataException("ZIP file is truncated.");
        }
    }

    private sealed record CentralDirectoryEntry(string Name, ushort CompressionMethod, uint CompressedLength, uint Length, uint LocalHeaderOffset);
}

internal sealed class EpubZipEncodingException : Exception
{
    public EpubZipEncodingException()
        : base("ZIP file names are not encoded as UTF-8.")
    {
    }

    public EpubZipEncodingException(Exception innerException)
        : base("ZIP file names are not encoded as UTF-8.", innerException)
    {
    }
}
