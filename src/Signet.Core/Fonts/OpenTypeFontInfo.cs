using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Text;

namespace Signet.Core.Fonts;

/// <summary>The container format of a font file.</summary>
public enum FontFileFormat
{
    /// <summary>Not recognized.</summary>
    Unknown,

    /// <summary>TrueType outlines in an SFNT file (<c>0x00010000</c> / <c>true</c>).</summary>
    TrueType,

    /// <summary>CFF (PostScript) outlines in an SFNT file (<c>OTTO</c>).</summary>
    OpenTypeCff,

    /// <summary>A TrueType / OpenType collection (<c>ttcf</c>) — the first font is read.</summary>
    Collection,

    /// <summary>WOFF 1.0 (zlib-compressed tables).</summary>
    Woff,

    /// <summary>WOFF 2.0 (one Brotli-compressed stream).</summary>
    Woff2,
}

/// <summary>
/// What a font file says about itself: the names from its <c>name</c> table, the style data from its <c>OS/2</c>
/// table and the weight axis of a variable font (<c>fvar</c>).
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
    /// <summary>Empty result (unrecognized font).</summary>
    public static FontFileInfo Empty { get; } = new(string.Empty, string.Empty, string.Empty, string.Empty, null);

    /// <summary>The container format of the file.</summary>
    public FontFileFormat Format { get; init; }

    /// <summary>
    /// The typographic family (nameID 16) — set by fonts whose family has more than the four classic styles, where
    /// <see cref="Family"/> (nameID 1) also carries the style (e.g. <c>Acme Light</c>); <c>""</c> when absent.
    /// </summary>
    public string TypographicFamily { get; init; } = string.Empty;

    /// <summary>The typographic subfamily (nameID 17); <c>""</c> when absent.</summary>
    public string TypographicSubfamily { get; init; } = string.Empty;

    /// <summary>The <c>usWeightClass</c> field of the <c>OS/2</c> table (100–900), or <c>null</c>.</summary>
    public ushort? WeightClass { get; init; }

    /// <summary>The <c>fsSelection</c> field of the <c>OS/2</c> table (italic, bold, oblique bits…), or <c>null</c>.</summary>
    public ushort? FsSelection { get; init; }

    /// <summary>The <c>sFamilyClass</c> field of the <c>OS/2</c> table (IBM font class in the high byte), or <c>null</c>.</summary>
    public short? FamilyClass { get; init; }

    /// <summary>PANOSE <c>bFamilyType</c> (2 = Latin text, 3 = script, …), or <c>null</c>.</summary>
    public byte? PanoseFamilyType { get; init; }

    /// <summary>PANOSE <c>bSerifStyle</c> (11–13 = sans serif for Latin text), or <c>null</c>.</summary>
    public byte? PanoseSerifStyle { get; init; }

    /// <summary>PANOSE <c>bProportion</c> (9 = monospaced for Latin text), or <c>null</c>.</summary>
    public byte? PanoseProportion { get; init; }

    /// <summary>The minimum of the <c>wght</c> axis of a variable font, or <c>null</c> for a static font.</summary>
    public double? WeightAxisMin { get; init; }

    /// <summary>The maximum of the <c>wght</c> axis of a variable font, or <c>null</c> for a static font.</summary>
    public double? WeightAxisMax { get; init; }

    /// <summary>The family a stylesheet should use: <see cref="TypographicFamily"/>, or <see cref="Family"/> without it.</summary>
    public string PreferredFamily => TypographicFamily.Length > 0 ? TypographicFamily : Family;

    /// <summary>The style name matching <see cref="PreferredFamily"/>: <see cref="TypographicSubfamily"/> or <see cref="Subfamily"/>.</summary>
    public string PreferredSubfamily => TypographicSubfamily.Length > 0 ? TypographicSubfamily : Subfamily;

    /// <summary>Whether <c>fsSelection</c> marks the font italic (bit 0).</summary>
    public bool IsItalic => FsSelection is { } fs && (fs & 0x0001) != 0;

    /// <summary>Whether <c>fsSelection</c> marks the font bold (bit 5).</summary>
    public bool IsBold => FsSelection is { } fs && (fs & 0x0020) != 0;

    /// <summary>Whether <c>fsSelection</c> marks the font oblique (bit 9).</summary>
    public bool IsOblique => FsSelection is { } fs && (fs & 0x0200) != 0;

    /// <summary>Whether the font is a variable font with a <c>wght</c> axis.</summary>
    public bool HasWeightAxis => WeightAxisMin is not null && WeightAxisMax is not null;
}

/// <summary>
/// Minimal reader of font files — no dependency on graphics libraries. Reads the <c>name</c>, <c>OS/2</c> and
/// <c>fvar</c> tables of TTF (<c>0x00010000</c>, <c>true</c>), OTF (<c>OTTO</c>), TTC collections (<c>ttcf</c> —
/// the first font is read), WOFF (zlib) and WOFF2 (Brotli; the tables read here are never transformed).
/// </summary>
public static class OpenTypeFontInfo
{
    private const int SfntHeaderSize = 12;
    private const int TableRecordSize = 16;
    private const int WoffHeaderSize = 44;
    private const int WoffTableRecordSize = 20;
    private const int Woff2HeaderSize = 48;

    // A decompressed font larger than this is treated as damaged (protects against absurd header values).
    private const int MaxDecompressedSize = 256 * 1024 * 1024;

    private const uint NameTag = 0x6E616D65; // 'name'
    private const uint Os2Tag = 0x4F532F32; // 'OS/2'
    private const uint FvarTag = 0x66766172; // 'fvar'
    private const uint GlyfTag = 0x676C7966; // 'glyf'
    private const uint LocaTag = 0x6C6F6361; // 'loca'
    private const uint WghtTag = 0x77676874; // 'wght'

    private static readonly uint[] WantedTables = { NameTag, Os2Tag, FvarTag };

    // The WOFF2 known-table index (the 6-bit tag index of a table directory entry).
    private static readonly string[] Woff2KnownTags =
    {
        "cmap", "head", "hhea", "hmtx", "maxp", "name", "OS/2", "post", "cvt ", "fpgm", "glyf", "loca", "prep", "CFF ",
        "VORG", "EBDT", "EBLC", "gasp", "hdmx", "kern", "LTSH", "PCLT", "VDMX", "vhea", "vmtx", "BASE", "GDEF", "GPOS",
        "GSUB", "EBSC", "JSTF", "MATH", "CBDT", "CBLC", "COLR", "CPAL", "SVG ", "sbix", "acnt", "avar", "bdat", "bloc",
        "bsln", "cvar", "fdsc", "feat", "fmtx", "fvar", "gvar", "hsty", "just", "lcar", "mort", "morx", "opbd", "prop",
        "trak", "Zapf", "Silf", "Glat", "Gloc", "Feat", "Sill",
    };

    /// <summary>Reads a font file; on any error returns <see cref="FontFileInfo.Empty"/>.</summary>
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

    /// <summary>Reads font file content; on a structural error (or without a <c>name</c> table) returns <see cref="FontFileInfo.Empty"/>.</summary>
    public static FontFileInfo Parse(byte[] data)
    {
        ArgumentNullException.ThrowIfNull(data);
        if (!TryReadTables(data, out FontFileFormat format, out Dictionary<uint, byte[]> tables)
            || !tables.TryGetValue(NameTag, out byte[]? name) || name.Length < 6)
        {
            return FontFileInfo.Empty;
        }

        FontFileInfo info = ParseNameTable(name) with { Format = format };
        if (tables.TryGetValue(Os2Tag, out byte[]? os2))
        {
            info = ApplyOs2(info, os2);
        }

        if (tables.TryGetValue(FvarTag, out byte[]? fvar))
        {
            info = ApplyFvar(info, fvar);
        }

        return info;
    }

    /// <summary>
    /// Whether <paramref name="fsType"/> (the <c>OS/2.fsType</c> field) denotes a font WITHOUT the right to
    /// be embedded in a document (bits 1 "Restricted License" or 2 "Preview &amp; Print" — as
    /// opposed to bit 3 "Editable" and flags 0/8/9, which allow embedding).
    /// </summary>
    public static bool IsEmbeddingRestricted(ushort fsType) => (fsType & 0x0006) != 0;

    // --- container formats ---------------------------------------------------------------------------------- //

    private static bool TryReadTables(byte[] data, out FontFileFormat format, out Dictionary<uint, byte[]> tables)
    {
        format = FontFileFormat.Unknown;
        tables = new Dictionary<uint, byte[]>();
        if (data.Length < SfntHeaderSize)
        {
            return false;
        }

        try
        {
            uint tag = BinaryPrimitives.ReadUInt32BigEndian(data);
            switch (tag)
            {
                case 0x774F4646: // 'wOFF'
                    format = FontFileFormat.Woff;
                    return ReadWoff(data, tables);
                case 0x774F4632: // 'wOF2'
                    format = FontFileFormat.Woff2;
                    return ReadWoff2(data, tables);
                default:
                    return ReadSfnt(data, ref format, tables);
            }
        }
        catch (Exception ex) when (ex is ArgumentOutOfRangeException or ArgumentException or InvalidDataException
                                       or IOException or OverflowException or IndexOutOfRangeException)
        {
            return false;
        }
    }

    private static bool ReadSfnt(byte[] data, ref FontFileFormat format, Dictionary<uint, byte[]> tables)
    {
        uint tag = BinaryPrimitives.ReadUInt32BigEndian(data);
        int sfntStart = 0;
        bool collection = false;
        if (tag == 0x74746366) // 'ttcf'
        {
            if (data.Length < 16)
            {
                return false;
            }

            collection = true;
            sfntStart = (int)BinaryPrimitives.ReadUInt32BigEndian(data.AsSpan(12, 4));
            if (sfntStart < 0 || sfntStart + SfntHeaderSize > data.Length)
            {
                return false;
            }

            tag = BinaryPrimitives.ReadUInt32BigEndian(data.AsSpan(sfntStart, 4));
        }

        if (tag is not (0x00010000 or 0x4F54544F /* OTTO */ or 0x74727565 /* true */ or 0x74797031 /* typ1 */))
        {
            return false;
        }

        format = collection ? FontFileFormat.Collection : tag == 0x4F54544F ? FontFileFormat.OpenTypeCff : FontFileFormat.TrueType;
        int numTables = BinaryPrimitives.ReadUInt16BigEndian(data.AsSpan(sfntStart + 4, 2));
        int dirStart = sfntStart + SfntHeaderSize;
        for (int i = 0; i < numTables; i++)
        {
            int rec = dirStart + (i * TableRecordSize);
            if (rec + TableRecordSize > data.Length)
            {
                return false;
            }

            uint recTag = BinaryPrimitives.ReadUInt32BigEndian(data.AsSpan(rec, 4));
            if (Array.IndexOf(WantedTables, recTag) < 0)
            {
                continue;
            }

            long offset = BinaryPrimitives.ReadUInt32BigEndian(data.AsSpan(rec + 8, 4));
            long length = BinaryPrimitives.ReadUInt32BigEndian(data.AsSpan(rec + 12, 4));
            if (offset + length <= data.Length)
            {
                tables[recTag] = data.AsSpan((int)offset, (int)length).ToArray();
            }
        }

        return true;
    }

    private static bool ReadWoff(byte[] data, Dictionary<uint, byte[]> tables)
    {
        if (data.Length < WoffHeaderSize)
        {
            return false;
        }

        int numTables = BinaryPrimitives.ReadUInt16BigEndian(data.AsSpan(12, 2));
        for (int i = 0; i < numTables; i++)
        {
            int rec = WoffHeaderSize + (i * WoffTableRecordSize);
            if (rec + WoffTableRecordSize > data.Length)
            {
                return false;
            }

            uint recTag = BinaryPrimitives.ReadUInt32BigEndian(data.AsSpan(rec, 4));
            if (Array.IndexOf(WantedTables, recTag) < 0)
            {
                continue;
            }

            long offset = BinaryPrimitives.ReadUInt32BigEndian(data.AsSpan(rec + 4, 4));
            long compLength = BinaryPrimitives.ReadUInt32BigEndian(data.AsSpan(rec + 8, 4));
            long origLength = BinaryPrimitives.ReadUInt32BigEndian(data.AsSpan(rec + 12, 4));
            if (offset + compLength > data.Length || origLength > MaxDecompressedSize || compLength > origLength)
            {
                continue;
            }

            if (compLength == origLength)
            {
                tables[recTag] = data.AsSpan((int)offset, (int)origLength).ToArray();
                continue;
            }

            using MemoryStream input = new(data, (int)offset, (int)compLength, writable: false);
            using ZLibStream zlib = new(input, CompressionMode.Decompress);
            byte[] table = new byte[origLength];
            zlib.ReadExactly(table);
            tables[recTag] = table;
        }

        return true;
    }

    private static bool ReadWoff2(byte[] data, Dictionary<uint, byte[]> tables)
    {
        if (data.Length < Woff2HeaderSize)
        {
            return false;
        }

        uint flavor = BinaryPrimitives.ReadUInt32BigEndian(data.AsSpan(4, 4));
        int numTables = BinaryPrimitives.ReadUInt16BigEndian(data.AsSpan(12, 2));
        long compressedSize = BinaryPrimitives.ReadUInt32BigEndian(data.AsSpan(20, 4));

        // The table directory: where each table is in the decompressed stream (tables follow each other unpadded).
        List<(uint Tag, long Offset, long Length)> directory = new(numTables);
        int pos = Woff2HeaderSize;
        long streamOffset = 0;
        for (int i = 0; i < numTables; i++)
        {
            byte flags = data[pos++];
            int tagIndex = flags & 0x3F;
            int transform = flags >> 6;
            uint tag;
            if (tagIndex == 63)
            {
                tag = BinaryPrimitives.ReadUInt32BigEndian(data.AsSpan(pos, 4));
                pos += 4;
            }
            else
            {
                tag = TagValue(Woff2KnownTags[tagIndex]);
            }

            long length = ReadUIntBase128(data, ref pos);
            bool transformed = tag is GlyfTag or LocaTag ? transform != 3 : transform != 0;
            if (transformed)
            {
                length = ReadUIntBase128(data, ref pos);
            }

            directory.Add((tag, streamOffset, length));
            streamOffset += length;
        }

        if (streamOffset > MaxDecompressedSize)
        {
            return false;
        }

        // A collection: only the tables of its first font are read.
        HashSet<int>? firstFontTables = null;
        if (flavor == 0x74746366) // 'ttcf'
        {
            pos += 4; // ttcVersion
            int numFonts = Read255UInt16(data, ref pos);
            for (int font = 0; font < numFonts; font++)
            {
                int fontTables = Read255UInt16(data, ref pos);
                pos += 4; // flavor
                HashSet<int> indices = new();
                for (int t = 0; t < fontTables; t++)
                {
                    indices.Add(Read255UInt16(data, ref pos));
                }

                firstFontTables ??= indices;
            }
        }

        if (pos + compressedSize > data.Length)
        {
            return false;
        }

        byte[] stream = new byte[streamOffset];
        using (MemoryStream input = new(data, pos, (int)compressedSize, writable: false))
        using (BrotliStream brotli = new(input, CompressionMode.Decompress))
        {
            brotli.ReadExactly(stream);
        }

        for (int i = 0; i < directory.Count; i++)
        {
            (uint tag, long offset, long length) = directory[i];
            if (Array.IndexOf(WantedTables, tag) >= 0 && (firstFontTables is null || firstFontTables.Contains(i))
                && !tables.ContainsKey(tag))
            {
                tables[tag] = stream.AsSpan((int)offset, (int)length).ToArray();
            }
        }

        return true;
    }

    // WOFF2 UIntBase128: up to 5 bytes, 7 bits each, high bit = more; no leading zero byte.
    private static long ReadUIntBase128(byte[] data, ref int pos)
    {
        long value = 0;
        for (int i = 0; i < 5; i++)
        {
            byte b = data[pos++];
            if (i == 0 && b == 0x80)
            {
                throw new InvalidDataException("UIntBase128 with a leading zero.");
            }

            value = (value << 7) | (uint)(b & 0x7F);
            if ((b & 0x80) == 0)
            {
                return value <= uint.MaxValue ? value : throw new InvalidDataException("UIntBase128 overflow.");
            }
        }

        throw new InvalidDataException("UIntBase128 longer than 5 bytes.");
    }

    // WOFF2 255UInt16.
    private static int Read255UInt16(byte[] data, ref int pos)
    {
        byte code = data[pos++];
        switch (code)
        {
            case 253:
                int value = BinaryPrimitives.ReadUInt16BigEndian(data.AsSpan(pos, 2));
                pos += 2;
                return value;
            case 254:
                return data[pos++] + (253 * 2);
            case 255:
                return data[pos++] + 253;
            default:
                return code;
        }
    }

    private static uint TagValue(string tag) =>
        ((uint)tag[0] << 24) | ((uint)tag[1] << 16) | ((uint)tag[2] << 8) | tag[3];

    // --- tables ------------------------------------------------------------------------------------------------ //

    private static FontFileInfo ApplyOs2(FontFileInfo info, byte[] os2)
    {
        // fsType is at offset 8 of the OS/2 table (16-bit, the same offset in all table versions —
        // version/xAvgCharWidth/usWeightClass/usWidthClass precede it).
        if (os2.Length >= 10)
        {
            info = info with
            {
                WeightClass = BinaryPrimitives.ReadUInt16BigEndian(os2.AsSpan(4, 2)),
                FsType = BinaryPrimitives.ReadUInt16BigEndian(os2.AsSpan(8, 2)),
            };
        }

        if (os2.Length >= 42)
        {
            info = info with
            {
                FamilyClass = BinaryPrimitives.ReadInt16BigEndian(os2.AsSpan(30, 2)),
                PanoseFamilyType = os2[32],
                PanoseSerifStyle = os2[33],
                PanoseProportion = os2[35],
            };
        }

        if (os2.Length >= 64)
        {
            info = info with { FsSelection = BinaryPrimitives.ReadUInt16BigEndian(os2.AsSpan(62, 2)) };
        }

        return info;
    }

    private static FontFileInfo ApplyFvar(FontFileInfo info, byte[] fvar)
    {
        if (fvar.Length < 16)
        {
            return info;
        }

        int axesOffset = BinaryPrimitives.ReadUInt16BigEndian(fvar.AsSpan(4, 2));
        int axisCount = BinaryPrimitives.ReadUInt16BigEndian(fvar.AsSpan(8, 2));
        int axisSize = BinaryPrimitives.ReadUInt16BigEndian(fvar.AsSpan(10, 2));
        if (axisSize < 16)
        {
            return info;
        }

        for (int i = 0; i < axisCount; i++)
        {
            int rec = axesOffset + (i * axisSize);
            if (rec + 16 > fvar.Length)
            {
                break;
            }

            if (BinaryPrimitives.ReadUInt32BigEndian(fvar.AsSpan(rec, 4)) == WghtTag)
            {
                return info with
                {
                    WeightAxisMin = BinaryPrimitives.ReadInt32BigEndian(fvar.AsSpan(rec + 4, 4)) / 65536.0,
                    WeightAxisMax = BinaryPrimitives.ReadInt32BigEndian(fvar.AsSpan(rec + 12, 4)) / 65536.0,
                };
            }
        }

        return info;
    }

    private static FontFileInfo ParseNameTable(byte[] data)
    {
        int count = BinaryPrimitives.ReadUInt16BigEndian(data.AsSpan(2, 2));
        int stringStorage = BinaryPrimitives.ReadUInt16BigEndian(data.AsSpan(4, 2));

        // index = nameID (0..17), platformID 3 preferred
        string?[] best = new string?[18];
        int[] bestPlatform = new int[18];
        Array.Fill(bestPlatform, -1);

        const int recordsStart = 6;
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

            if (nameId >= best.Length || nameId is > 5 and < 16 || length == 0)
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
            best[5] ?? string.Empty)
        {
            TypographicFamily = best[16] ?? string.Empty,
            TypographicSubfamily = best[17] ?? string.Empty,
        };
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
