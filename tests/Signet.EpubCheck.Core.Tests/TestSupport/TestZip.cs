using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Text;
using Signet.EpubCheck.Core.Ocf;

namespace Signet.EpubCheck.Core.Tests.TestSupport;

/// <summary>
/// Writes small ZIP files byte by byte (stored entries only), so that a test controls what <see cref="ZipArchive"/>
/// would decide for it: the name bytes, the UTF-8 flag and the extra field.
/// </summary>
internal static class TestZip
{
    private const ushort VersionNeeded = 20; // ZIP 2.0
    private const ushort DosTime = 0;
    private const ushort DosDate = 0x21; // 1980-01-01
    private const uint Crc32Polynomial = 0xEDB88320;
    private const uint Crc32Initial = 0xFFFFFFFF;
    private const int BitsPerByte = 8;

    /// <summary>One entry: its raw name, content, general purpose flags and local-header extra field.</summary>
    public sealed record Entry(byte[] Name, byte[] Content, ushort Flags = 0, byte[]? Extra = null)
    {
        public static Entry Of(string name, string content, bool utf8Flag = true) =>
            new(Encoding.UTF8.GetBytes(name), Encoding.UTF8.GetBytes(content), utf8Flag ? ZipFormat.Utf8NameFlag : (ushort)0);
    }

    /// <summary>A packaged EPUB with a correct <c>mimetype</c> first, then <paramref name="others"/>.</summary>
    public static void WriteEpub(string path, params Entry[] others)
    {
        List<Entry> entries = [Entry.Of("mimetype", "application/epub+zip", utf8Flag: false)];
        entries.AddRange(others);
        Write(path, entries);
    }

    public static void Write(string path, IReadOnlyList<Entry> entries)
    {
        using MemoryStream zip = new();
        List<(Entry Entry, uint Crc, long Offset)> written = [];
        foreach (Entry entry in entries)
        {
            uint crc = Crc32(entry.Content);
            written.Add((entry, crc, zip.Position));
            byte[] extra = entry.Extra ?? [];
            WriteUInt32(zip, ZipFormat.LocalHeader.Signature);
            WriteUInt16(zip, VersionNeeded);
            WriteUInt16(zip, entry.Flags);
            WriteUInt16(zip, ZipFormat.MethodStored);
            WriteUInt16(zip, DosTime);
            WriteUInt16(zip, DosDate);
            WriteUInt32(zip, crc);
            WriteUInt32(zip, (uint)entry.Content.Length);
            WriteUInt32(zip, (uint)entry.Content.Length);
            WriteUInt16(zip, (ushort)entry.Name.Length);
            WriteUInt16(zip, (ushort)extra.Length);
            zip.Write(entry.Name);
            zip.Write(extra);
            zip.Write(entry.Content);
        }

        long directoryStart = zip.Position;
        foreach ((Entry entry, uint crc, long offset) in written)
        {
            WriteUInt32(zip, ZipFormat.CentralHeader.Signature);
            WriteUInt16(zip, VersionNeeded); // made by
            WriteUInt16(zip, VersionNeeded);
            WriteUInt16(zip, entry.Flags);
            WriteUInt16(zip, ZipFormat.MethodStored);
            WriteUInt16(zip, DosTime);
            WriteUInt16(zip, DosDate);
            WriteUInt32(zip, crc);
            WriteUInt32(zip, (uint)entry.Content.Length);
            WriteUInt32(zip, (uint)entry.Content.Length);
            WriteUInt16(zip, (ushort)entry.Name.Length);
            WriteUInt16(zip, 0); // extra field length
            WriteUInt16(zip, 0); // comment length
            WriteUInt16(zip, 0); // disk number
            WriteUInt16(zip, 0); // internal attributes
            WriteUInt32(zip, 0); // external attributes
            WriteUInt32(zip, (uint)offset);
            zip.Write(entry.Name);
        }

        long directoryEnd = zip.Position;
        WriteUInt32(zip, ZipFormat.EndOfCentralDirectory.Signature);
        WriteUInt16(zip, 0); // disk number
        WriteUInt16(zip, 0); // disk of the central directory
        WriteUInt16(zip, (ushort)written.Count);
        WriteUInt16(zip, (ushort)written.Count);
        WriteUInt32(zip, (uint)(directoryEnd - directoryStart));
        WriteUInt32(zip, (uint)directoryStart);
        WriteUInt16(zip, 0); // comment length
        File.WriteAllBytes(path, zip.ToArray());
    }

    // CRC-32 of ZIP, bit by bit: the test files are tiny.
    private static uint Crc32(byte[] data)
    {
        uint crc = Crc32Initial;
        foreach (byte value in data)
        {
            crc ^= value;
            for (int bit = 0; bit < BitsPerByte; bit++)
            {
                crc = (crc & 1) != 0 ? (crc >> 1) ^ Crc32Polynomial : crc >> 1;
            }
        }

        return ~crc;
    }

    private static void WriteUInt16(Stream stream, ushort value)
    {
        Span<byte> bytes = stackalloc byte[sizeof(ushort)];
        BinaryPrimitives.WriteUInt16LittleEndian(bytes, value);
        stream.Write(bytes);
    }

    private static void WriteUInt32(Stream stream, uint value)
    {
        Span<byte> bytes = stackalloc byte[sizeof(uint)];
        BinaryPrimitives.WriteUInt32LittleEndian(bytes, value);
        stream.Write(bytes);
    }
}
