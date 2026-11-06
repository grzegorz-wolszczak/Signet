using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Text;

namespace Signet.EpubCheck.Core.Ocf;

/// <summary>
/// An EPUB source backed by a ZIP file. The central directory is read by hand (the order of the entries and strict UTF-8
/// names are facts <see cref="ZipArchive"/> hides); the contents come from <see cref="ZipArchive"/>.
/// </summary>
public sealed class ZipEpubSource : IEpubSource
{
    // The texts of Java's ZipFile, which upstream prints in PKG-008.
    internal const string EmptyZipMessage = "zip file is empty";
    internal const string NoEndHeaderMessage = "zip END header not found";
    internal const string BadCentralDirectoryMessage = "invalid CEN header (bad signature)";
    internal const string TruncatedZipMessage = "invalid zip file (truncated)";

    private static readonly UTF8Encoding s_strictUtf8 = new(encoderShouldEmitUTF8Identifier: false, throwOnInvalidBytes: true);

    private readonly ZipArchive _archive;
    private readonly ZipArchiveEntry[] _archiveEntries;

    /// <summary>Opens a ZIP file as an EPUB source.</summary>
    /// <exception cref="InvalidDataException">The file is not a readable ZIP archive (the message says why).</exception>
    /// <exception cref="EpubZipEncodingException">A file name of the archive is not UTF-8.</exception>
    public ZipEpubSource(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        Path = System.IO.Path.GetFullPath(path);
        FileStream stream = new(Path, FileMode.Open, FileAccess.Read, FileShare.Read);
        ZipArchive? archive = null;
        try
        {
            List<CentralDirectoryEntry> centralEntries = ReadCentralDirectory(stream);
            archive = new ZipArchive(stream, ZipArchiveMode.Read, leaveOpen: false, entryNameEncoding: Encoding.UTF8);
            if (archive.Entries.Count != centralEntries.Count)
            {
                throw new InvalidDataException(BadCentralDirectoryMessage);
            }

            EpubSourceEntry[] entries = new EpubSourceEntry[centralEntries.Count];
            _archiveEntries = new ZipArchiveEntry[centralEntries.Count];
            for (int order = 0; order < entries.Length; order++)
            {
                CentralDirectoryEntry entry = centralEntries[order];
                ZipArchiveEntry archiveEntry = archive.Entries[order];
                CompressionMethod method = entry.CompressionMethod switch
                {
                    ZipFormat.MethodStored => CompressionMethod.Stored,
                    ZipFormat.MethodDeflated => CompressionMethod.Deflate,
                    _ => CompressionMethod.Other,
                };
                entries[order] = new EpubSourceEntry(entry.Name, order, method, entry.CompressedLength, entry.Length,
                    entry.Name.EndsWith('/'), archiveEntry.LastWriteTime.DateTime);
                _archiveEntries[order] = archiveEntry;
            }

            Entries = entries;
            _archive = archive;
        }
        catch
        {
            archive?.Dispose();
            stream.Dispose();
            throw;
        }
    }

    /// <summary>The full path of the archive.</summary>
    public string Path { get; }

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

        if ((uint)entry.Order >= (uint)Entries.Count || !Entries[entry.Order].Equals(entry))
        {
            throw new ArgumentException("The entry does not belong to this source.", nameof(entry));
        }

        return _archiveEntries[entry.Order].Open();
    }

    /// <inheritdoc />
    public void Dispose() => _archive.Dispose();

    private static List<CentralDirectoryEntry> ReadCentralDirectory(FileStream stream)
    {
        long length = stream.Length;
        if (length == 0)
        {
            throw new InvalidDataException(EmptyZipMessage);
        }

        long end = FindEndOfCentralDirectory(stream);
        byte[] record = ReadAt(stream, end, ZipFormat.EndOfCentralDirectory.Length);
        long count = BinaryPrimitives.ReadUInt16LittleEndian(record.AsSpan(ZipFormat.EndOfCentralDirectory.EntryCountOffset));
        long size = BinaryPrimitives.ReadUInt32LittleEndian(record.AsSpan(ZipFormat.EndOfCentralDirectory.DirectorySizeOffset));
        long offset = BinaryPrimitives.ReadUInt32LittleEndian(record.AsSpan(ZipFormat.EndOfCentralDirectory.DirectoryOffsetOffset));
        if (count == ZipFormat.Zip64Marker16 || size == ZipFormat.Zip64Marker32 || offset == ZipFormat.Zip64Marker32)
        {
            (count, size, offset) = ReadZip64EndOfCentralDirectory(stream, end);
        }

        if (offset < 0 || size < 0 || offset + size > end || size > int.MaxValue)
        {
            throw new InvalidDataException(TruncatedZipMessage);
        }

        byte[] directory = ReadAt(stream, offset, (int)size);
        List<CentralDirectoryEntry> entries = new((int)Math.Min(count, ZipFormat.Zip64Marker16));
        int position = 0;
        for (long i = 0; i < count; i++)
        {
            EnsureRange(directory, position, ZipFormat.CentralHeader.Length);
            ReadOnlySpan<byte> header = directory.AsSpan(position);
            if (BinaryPrimitives.ReadUInt32LittleEndian(header) != ZipFormat.CentralHeader.Signature)
            {
                throw new InvalidDataException(BadCentralDirectoryMessage);
            }

            ushort method = BinaryPrimitives.ReadUInt16LittleEndian(header[ZipFormat.CentralHeader.MethodOffset..]);
            long compressedLength = BinaryPrimitives.ReadUInt32LittleEndian(header[ZipFormat.CentralHeader.CompressedSizeOffset..]);
            long uncompressedLength = BinaryPrimitives.ReadUInt32LittleEndian(header[ZipFormat.CentralHeader.SizeOffset..]);
            int nameLength = BinaryPrimitives.ReadUInt16LittleEndian(header[ZipFormat.CentralHeader.NameLengthOffset..]);
            int extraLength = BinaryPrimitives.ReadUInt16LittleEndian(header[ZipFormat.CentralHeader.ExtraLengthOffset..]);
            int commentLength = BinaryPrimitives.ReadUInt16LittleEndian(header[ZipFormat.CentralHeader.CommentLengthOffset..]);
            EnsureRange(directory, position + ZipFormat.CentralHeader.Length, nameLength + extraLength + commentLength);
            ReadOnlySpan<byte> rawName = directory.AsSpan(position + ZipFormat.CentralHeader.Length, nameLength);
            ReadOnlySpan<byte> extra = directory.AsSpan(position + ZipFormat.CentralHeader.Length + nameLength, extraLength);
            (compressedLength, uncompressedLength) = ApplyZip64Sizes(extra, compressedLength, uncompressedLength);

            // Like Java's ZipFile(file, UTF_8): every name is read as UTF-8, whether or not the entry sets the UTF-8 flag.
            string name;
            try
            {
                name = s_strictUtf8.GetString(rawName);
            }
            catch (DecoderFallbackException ex)
            {
                throw new EpubZipEncodingException(ex);
            }

            entries.Add(new CentralDirectoryEntry(name, method, compressedLength, uncompressedLength));
            position += ZipFormat.CentralHeader.Length + nameLength + extraLength + commentLength;
        }

        return entries;
    }

    private static long FindEndOfCentralDirectory(FileStream stream)
    {
        long length = stream.Length;
        if (length < ZipFormat.EndOfCentralDirectory.Length)
        {
            throw new InvalidDataException(NoEndHeaderMessage);
        }

        // The record is the last thing in the file, followed only by a comment of at most 65535 bytes.
        int tailLength = (int)Math.Min(length, ZipFormat.EndOfCentralDirectory.Length + ZipFormat.MaxCommentLength);
        long tailStart = length - tailLength;
        byte[] tail = ReadAt(stream, tailStart, tailLength);
        for (int index = tailLength - ZipFormat.EndOfCentralDirectory.Length; index >= 0; index--)
        {
            if (BinaryPrimitives.ReadUInt32LittleEndian(tail.AsSpan(index)) == ZipFormat.EndOfCentralDirectory.Signature)
            {
                return tailStart + index;
            }
        }

        throw new InvalidDataException(NoEndHeaderMessage);
    }

    private static (long Count, long Size, long Offset) ReadZip64EndOfCentralDirectory(FileStream stream, long end)
    {
        if (end < ZipFormat.Zip64Locator.Length)
        {
            throw new InvalidDataException(NoEndHeaderMessage);
        }

        byte[] locator = ReadAt(stream, end - ZipFormat.Zip64Locator.Length, ZipFormat.Zip64Locator.Length);
        if (BinaryPrimitives.ReadUInt32LittleEndian(locator) != ZipFormat.Zip64Locator.Signature)
        {
            throw new InvalidDataException(NoEndHeaderMessage);
        }

        long recordOffset = checked((long)BinaryPrimitives.ReadUInt64LittleEndian(locator.AsSpan(ZipFormat.Zip64Locator.RecordOffsetOffset)));
        byte[] record = ReadAt(stream, recordOffset, ZipFormat.Zip64EndOfCentralDirectory.Length);
        if (BinaryPrimitives.ReadUInt32LittleEndian(record) != ZipFormat.Zip64EndOfCentralDirectory.Signature)
        {
            throw new InvalidDataException(NoEndHeaderMessage);
        }

        return (checked((long)BinaryPrimitives.ReadUInt64LittleEndian(record.AsSpan(ZipFormat.Zip64EndOfCentralDirectory.EntryCountOffset))),
            checked((long)BinaryPrimitives.ReadUInt64LittleEndian(record.AsSpan(ZipFormat.Zip64EndOfCentralDirectory.DirectorySizeOffset))),
            checked((long)BinaryPrimitives.ReadUInt64LittleEndian(record.AsSpan(ZipFormat.Zip64EndOfCentralDirectory.DirectoryOffsetOffset))));
    }

    // The ZIP64 extra field (id 1) holds, in this order, the sizes that the header marks with 0xFFFFFFFF.
    private static (long Compressed, long Uncompressed) ApplyZip64Sizes(ReadOnlySpan<byte> extra, long compressed, long uncompressed)
    {
        while (extra.Length >= ZipFormat.ExtraField.HeaderLength)
        {
            ushort id = BinaryPrimitives.ReadUInt16LittleEndian(extra);
            int size = BinaryPrimitives.ReadUInt16LittleEndian(extra[ZipFormat.ExtraField.SizeOffset..]);
            ReadOnlySpan<byte> data = extra.Slice(ZipFormat.ExtraField.HeaderLength, Math.Min(size, extra.Length - ZipFormat.ExtraField.HeaderLength));
            if (id == ZipFormat.ExtraField.Zip64Id)
            {
                if (uncompressed == ZipFormat.Zip64Marker32 && data.Length >= ZipFormat.ExtraField.Zip64SizeLength)
                {
                    uncompressed = (long)BinaryPrimitives.ReadUInt64LittleEndian(data);
                    data = data[ZipFormat.ExtraField.Zip64SizeLength..];
                }

                if (compressed == ZipFormat.Zip64Marker32 && data.Length >= ZipFormat.ExtraField.Zip64SizeLength)
                {
                    compressed = (long)BinaryPrimitives.ReadUInt64LittleEndian(data);
                }

                break;
            }

            extra = extra[Math.Min(extra.Length, ZipFormat.ExtraField.HeaderLength + size)..];
        }

        return (compressed, uncompressed);
    }

    private static byte[] ReadAt(FileStream stream, long offset, int count)
    {
        if (offset < 0 || offset + count > stream.Length)
        {
            throw new InvalidDataException(TruncatedZipMessage);
        }

        byte[] buffer = new byte[count];
        stream.Position = offset;
        stream.ReadExactly(buffer);
        return buffer;
    }

    private static void EnsureRange(byte[] bytes, int offset, int length)
    {
        if (offset < 0 || length < 0 || offset > bytes.Length - length)
        {
            throw new InvalidDataException(BadCentralDirectoryMessage);
        }
    }

    private sealed record CentralDirectoryEntry(string Name, ushort CompressionMethod, long CompressedLength, long Length);
}

/// <summary>A file name of a ZIP archive is not encoded as UTF-8 (upstream's <c>PKG-027</c>).</summary>
public sealed class EpubZipEncodingException : Exception
{
    /// <summary>Creates the exception.</summary>
    public EpubZipEncodingException()
        : base("ZIP file names are not encoded as UTF-8.")
    {
    }

    /// <summary>Creates the exception with the message.</summary>
    public EpubZipEncodingException(string message)
        : base(message)
    {
    }

    /// <summary>Creates the exception with the decoding error that caused it.</summary>
    public EpubZipEncodingException(Exception innerException)
        : base("ZIP file names are not encoded as UTF-8.", innerException)
    {
    }

    /// <summary>Creates the exception with the message and the error that caused it.</summary>
    public EpubZipEncodingException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}
