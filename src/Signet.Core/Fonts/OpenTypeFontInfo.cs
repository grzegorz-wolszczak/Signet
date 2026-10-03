using System;
using System.Buffers.Binary;
using System.IO;
using System.Text;

namespace Signet.Core.Fonts;

/// <summary>
/// Names read from the <c>name</c> table of an SFNT font file (TTF / OTF / TTC).
/// </summary>
/// <param name="Family">Font family (nameID 1); <c>""</c> when absent.</param>
/// <param name="Subfamily">Subfamily / style (nameID 2), e.g. <c>Regular</c> / <c>Bold Italic</c>.</param>
/// <param name="FullName">Full name (nameID 4).</param>
/// <param name="Version">Version (nameID 5).</param>
/// <param name="FsType">
/// The <c>fsType</c> field of the <c>OS/2</c> table (embedding permissions set by the font vendor), or
/// <c>null</c> when the font has no <c>OS/2</c> table (e.g. a pure CFF/OTF without one — rare —
/// or an unrecognized font). See <see cref="OpenTypeFontInfo.IsEmbeddingRestricted"/>.
/// </param>
public readonly record struct FontFileInfo(string Family, string Subfamily, string FullName, string Version, ushort? FsType = null)
{
    /// <summary>Empty result (unrecognized font / compressed WOFF).</summary>
    public static FontFileInfo Empty { get; } = new(string.Empty, string.Empty, string.Empty, string.Empty, null);
}

/// <summary>
/// Minimal reader of the <c>name</c> table of SFNT fonts — no dependency on graphics libraries.
/// Supports TTF (<c>0x00010000</c>, <c>true</c>), OTF (<c>OTTO</c>) and TTC collections (<c>ttcf</c> —
/// the first font is read). WOFF/WOFF2 are compressed and return <see cref="FontFileInfo.Empty"/>.
/// </summary>
public static class OpenTypeFontInfo
{
    private const int SfntHeaderSize = 12;
    private const int TableRecordSize = 16;

    /// <summary>Reads names from a font file; on any error returns <see cref="FontFileInfo.Empty"/>.</summary>
    public static FontFileInfo Read(string path)
    {
        ArgumentNullException.ThrowIfNull(path);
        try
        {
            return Parse(File.ReadAllBytes(path));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return FontFileInfo.Empty;
        }
    }

    /// <summary>Reads names from font file content; on a structural error returns <see cref="FontFileInfo.Empty"/>.</summary>
    public static FontFileInfo Parse(byte[] data)
    {
        ArgumentNullException.ThrowIfNull(data);
        if (data.Length < SfntHeaderSize)
        {
            return FontFileInfo.Empty;
        }

        uint tag = BinaryPrimitives.ReadUInt32BigEndian(data);
        int sfntStart = 0;
        if (tag == 0x74746366) // 'ttcf'
        {
            if (data.Length < 16)
            {
                return FontFileInfo.Empty;
            }

            sfntStart = (int)BinaryPrimitives.ReadUInt32BigEndian(data.AsSpan(12, 4));
            if (sfntStart + SfntHeaderSize > data.Length)
            {
                return FontFileInfo.Empty;
            }

            tag = BinaryPrimitives.ReadUInt32BigEndian(data.AsSpan(sfntStart, 4));
        }

        bool knownSfnt = tag is 0x00010000 or 0x4F54544F /* OTTO */ or 0x74727565 /* true */ or 0x74797031 /* typ1 */;
        if (!knownSfnt)
        {
            return FontFileInfo.Empty;
        }

        int numTables = BinaryPrimitives.ReadUInt16BigEndian(data.AsSpan(sfntStart + 4, 2));
        int dirStart = sfntStart + SfntHeaderSize;
        int nameOffset = -1;
        int nameLength = 0;
        int os2Offset = -1;
        int os2Length = 0;
        for (int i = 0; i < numTables; i++)
        {
            int rec = dirStart + (i * TableRecordSize);
            if (rec + TableRecordSize > data.Length)
            {
                return FontFileInfo.Empty;
            }

            uint recTag = BinaryPrimitives.ReadUInt32BigEndian(data.AsSpan(rec, 4));
            if (recTag == 0x6E616D65) // 'name'
            {
                nameOffset = (int)BinaryPrimitives.ReadUInt32BigEndian(data.AsSpan(rec + 8, 4));
                nameLength = (int)BinaryPrimitives.ReadUInt32BigEndian(data.AsSpan(rec + 12, 4));
            }
            else if (recTag == 0x4F532F32) // 'OS/2'
            {
                os2Offset = (int)BinaryPrimitives.ReadUInt32BigEndian(data.AsSpan(rec + 8, 4));
                os2Length = (int)BinaryPrimitives.ReadUInt32BigEndian(data.AsSpan(rec + 12, 4));
            }
        }

        if (nameOffset < 0 || nameOffset + 6 > data.Length || nameLength < 6)
        {
            return FontFileInfo.Empty;
        }

        FontFileInfo info = ParseNameTable(data, nameOffset);

        // fsType is at offset 8 of the OS/2 table (16-bit, the same offset in all
        // table versions — version/xAvgCharWidth/usWeightClass/usWidthClass precede it).
        if (os2Offset >= 0 && os2Length >= 10 && os2Offset + 10 <= data.Length)
        {
            ushort fsType = BinaryPrimitives.ReadUInt16BigEndian(data.AsSpan(os2Offset + 8, 2));
            info = info with { FsType = fsType };
        }

        return info;
    }

    /// <summary>
    /// Whether <paramref name="fsType"/> (the <c>OS/2.fsType</c> field) denotes a font WITHOUT the right to
    /// be embedded in a document (bits 1 "Restricted License" or 2 "Preview &amp; Print" — as
    /// opposed to bit 3 "Editable" and flags 0/8/9, which allow embedding).
    /// </summary>
    public static bool IsEmbeddingRestricted(ushort fsType) => (fsType & 0x0006) != 0;

    private static FontFileInfo ParseNameTable(byte[] data, int nameOffset)
    {
        int count = BinaryPrimitives.ReadUInt16BigEndian(data.AsSpan(nameOffset + 2, 2));
        int stringStorage = nameOffset + BinaryPrimitives.ReadUInt16BigEndian(data.AsSpan(nameOffset + 4, 2));

        string?[] best = new string?[6]; // index = nameID (0..5), platformID 3 preferred
        int[] bestPlatform = new int[6];
        Array.Fill(bestPlatform, -1);

        int recordsStart = nameOffset + 6;
        for (int i = 0; i < count; i++)
        {
            int rec = recordsStart + (i * 12);
            if (rec + 12 > data.Length)
            {
                break;
            }

            int platformId = BinaryPrimitives.ReadUInt16BigEndian(data.AsSpan(rec, 2));
            int nameId = BinaryPrimitives.ReadUInt16BigEndian(data.AsSpan(rec + 6, 2));
            int length = BinaryPrimitives.ReadUInt16BigEndian(data.AsSpan(rec + 8, 2));
            int offset = BinaryPrimitives.ReadUInt16BigEndian(data.AsSpan(rec + 10, 2));

            if (nameId > 5 || length == 0)
            {
                continue;
            }

            int strStart = stringStorage + offset;
            if (strStart + length > data.Length)
            {
                continue;
            }

            // Prefer Windows (3) > Unicode (0) > Mac (1); do not overwrite a better platform.
            int rank = platformId switch { 3 => 3, 0 => 2, 1 => 1, _ => 0 };
            if (rank <= bestPlatform[nameId])
            {
                continue;
            }

            string value = platformId is 3 or 0
                ? Encoding.BigEndianUnicode.GetString(data, strStart, length)
                : DecodeMacRoman(data, strStart, length);

            value = value.Trim();
            if (value.Length == 0)
            {
                continue;
            }

            best[nameId] = value;
            bestPlatform[nameId] = rank;
        }

        return new FontFileInfo(
            best[1] ?? string.Empty,
            best[2] ?? string.Empty,
            best[4] ?? string.Empty,
            best[5] ?? string.Empty);
    }

    private static string DecodeMacRoman(byte[] data, int start, int length)
    {
        // The ASCII-compatible subset of Mac Roman is enough for font names.
        StringBuilder sb = new(length);
        for (int i = 0; i < length; i++)
        {
            byte b = data[start + i];
            sb.Append(b < 0x80 ? (char)b : '?');
        }

        return sb.ToString();
    }
}
