using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Text;

namespace Signet.EpubCheck.Core.Tests.TestSupport;

/// <summary>
/// Writes small ZIP files byte by byte (stored entries only), so that a test controls what <see cref="ZipArchive"/>
/// would decide for it: the name bytes, the UTF-8 flag and the extra field.
/// </summary>
internal static class TestZip
{
    private const ushort Utf8Flag = 0x0800;

    /// <summary>One entry: its raw name, content, general purpose flags and local-header extra field.</summary>
    public sealed record Entry(byte[] Name, byte[] Content, ushort Flags = 0, byte[]? Extra = null)
    {
        public static Entry Of(string name, string content, bool utf8Flag = true) =>
            new(Encoding.UTF8.GetBytes(name), Encoding.UTF8.GetBytes(content), utf8Flag ? Utf8Flag : (ushort)0);
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
            WriteUInt32(zip, 0x04034B50);
            WriteUInt16(zip, 20);
            WriteUInt16(zip, entry.Flags);
            WriteUInt16(zip, 0); // stored
            WriteUInt16(zip, 0);
            WriteUInt16(zip, 0x21);
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
            WriteUInt32(zip, 0x02014B50);
            WriteUInt16(zip, 20);
            WriteUInt16(zip, 20);
            WriteUInt16(zip, entry.Flags);
            WriteUInt16(zip, 0);
            WriteUInt16(zip, 0);
            WriteUInt16(zip, 0x21);
            WriteUInt32(zip, crc);
            WriteUInt32(zip, (uint)entry.Content.Length);
            WriteUInt32(zip, (uint)entry.Content.Length);
            WriteUInt16(zip, (ushort)entry.Name.Length);
            WriteUInt16(zip, 0);
            WriteUInt16(zip, 0);
            WriteUInt16(zip, 0);
            WriteUInt16(zip, 0);
            WriteUInt32(zip, 0);
            WriteUInt32(zip, (uint)offset);
            zip.Write(entry.Name);
        }

        long directoryEnd = zip.Position;
        WriteUInt32(zip, 0x06054B50);
        WriteUInt16(zip, 0);
        WriteUInt16(zip, 0);
        WriteUInt16(zip, (ushort)written.Count);
        WriteUInt16(zip, (ushort)written.Count);
        WriteUInt32(zip, (uint)(directoryEnd - directoryStart));
        WriteUInt32(zip, (uint)directoryStart);
        WriteUInt16(zip, 0);
        File.WriteAllBytes(path, zip.ToArray());
    }

    // CRC-32 of ZIP (polynomial 0xEDB88320), bit by bit: the test files are tiny.
    private static uint Crc32(byte[] data)
    {
        uint crc = 0xFFFFFFFF;
        foreach (byte value in data)
        {
            crc ^= value;
            for (int bit = 0; bit < 8; bit++)
            {
                crc = (crc & 1) != 0 ? (crc >> 1) ^ 0xEDB88320 : crc >> 1;
            }
        }

        return ~crc;
    }

    private static void WriteUInt16(Stream stream, ushort value)
    {
        Span<byte> bytes = stackalloc byte[2];
        BinaryPrimitives.WriteUInt16LittleEndian(bytes, value);
        stream.Write(bytes);
    }

    private static void WriteUInt32(Stream stream, uint value)
    {
        Span<byte> bytes = stackalloc byte[4];
        BinaryPrimitives.WriteUInt32LittleEndian(bytes, value);
        stream.Write(bytes);
    }
}
