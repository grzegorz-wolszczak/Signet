using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;

namespace Signet.Core;

/// <summary>
/// Reads the entries of a ZIP archive from their local headers, one after another, for an archive whose central directory is missing or
/// unreadable (a download that stopped, a damaged end) — what <see cref="ZipArchive"/> refuses to open (Calibre's <c>LocalZipFile</c>).
/// Stored and deflated entries are read; sizes come from the local header or, for an entry written to a stream that could not seek, from the
/// data descriptor behind its data. Whatever cannot be read is left out.
/// </summary>
internal static class LocalZipReader
{
    private const uint LocalHeaderSignature = 0x04034b50;
    private const uint CentralHeaderSignature = 0x02014b50;
    private const uint DescriptorSignature = 0x08074b50;
    private const int LocalHeaderLength = 30;
    private const int DescriptorLength = 16;
    private const ushort HasDescriptorFlag = 0x0008;
    private const ushort Stored = 0;
    private const ushort Deflated = 8;

    /// <summary>The files (not the folders) of the archive in <paramref name="bytes"/>, in the order they are in it.</summary>
    public static IReadOnlyList<(string Name, byte[] Data)> Read(byte[] bytes)
    {
        ArgumentNullException.ThrowIfNull(bytes);
        List<(string, byte[])> entries = new();
        int position = 0;
        while (position + LocalHeaderLength <= bytes.Length)
        {
            uint signature = BitConverter.ToUInt32(bytes, position);
            if (signature == CentralHeaderSignature)
            {
                break;
            }

            if (signature != LocalHeaderSignature)
            {
                position = IndexOf(bytes, LocalHeaderSignature, position + 1);
                if (position < 0)
                {
                    break;
                }

                continue;
            }

            ushort flags = BitConverter.ToUInt16(bytes, position + 6);
            ushort method = BitConverter.ToUInt16(bytes, position + 8);
            uint compressedSize = BitConverter.ToUInt32(bytes, position + 18);
            ushort nameLength = BitConverter.ToUInt16(bytes, position + 26);
            ushort extraLength = BitConverter.ToUInt16(bytes, position + 28);
            int nameStart = position + LocalHeaderLength;
            int dataStart = nameStart + nameLength + extraLength;
            if (dataStart > bytes.Length)
            {
                break;
            }

            string name = System.Text.Encoding.UTF8.GetString(bytes, nameStart, nameLength);
            bool descriptor = (flags & HasDescriptorFlag) != 0;
            long size = compressedSize;
            if (descriptor && compressedSize == 0)
            {
                size = FindDescriptor(bytes, dataStart);
                if (size < 0)
                {
                    break;
                }
            }

            if (size < 0 || dataStart + size > bytes.Length)
            {
                break;
            }

            if (!name.EndsWith('/') && Unpack(bytes, dataStart, (int)size, method) is { } data)
            {
                entries.Add((name, data));
            }

            position = dataStart + (int)size + (descriptor ? DescriptorLength : 0);
        }

        return entries;
    }

    // The size of the data that starts at dataStart and is followed by a data descriptor: the descriptor (with its signature) says how
    // long the data was, so the one whose size matches the distance is the right one. -1 when there is none.
    private static long FindDescriptor(byte[] bytes, int dataStart)
    {
        int at = dataStart;
        while ((at = IndexOf(bytes, DescriptorSignature, at)) >= 0)
        {
            if (at + DescriptorLength <= bytes.Length && BitConverter.ToUInt32(bytes, at + 8) == (uint)(at - dataStart))
            {
                return at - dataStart;
            }

            at++;
        }

        return -1;
    }

    private static byte[]? Unpack(byte[] bytes, int start, int length, ushort method)
    {
        if (method == Stored)
        {
            return bytes[start..(start + length)];
        }

        if (method != Deflated)
        {
            return null;
        }

        try
        {
            using MemoryStream input = new(bytes, start, length);
            using DeflateStream deflate = new(input, CompressionMode.Decompress);
            using MemoryStream output = new();
            deflate.CopyTo(output);
            return output.ToArray();
        }
        catch (InvalidDataException)
        {
            return null;
        }
    }

    private static int IndexOf(byte[] bytes, uint signature, int from)
    {
        ReadOnlySpan<byte> pattern = [(byte)signature, (byte)(signature >> 8), (byte)(signature >> 16), (byte)(signature >> 24)];
        int found = from < bytes.Length ? bytes.AsSpan(from).IndexOf(pattern) : -1;
        return found < 0 ? -1 : from + found;
    }
}
