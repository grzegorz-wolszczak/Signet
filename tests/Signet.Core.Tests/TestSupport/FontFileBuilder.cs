using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text;

namespace Signet.Core.Tests.TestSupport;

/// <summary>
/// Builds minimal font files for tests: an SFNT (TTF/OTF) with the given tables, and the same tables wrapped as
/// WOFF (zlib) or WOFF2 (Brotli, null transforms). The tables themselves: <c>name</c>, <c>OS/2</c>, <c>fvar</c>.
/// </summary>
public static class FontFileBuilder
{
    /// <summary>The TrueType flavor (<c>0x00010000</c>).</summary>
    public const uint TrueTypeFlavor = 0x00010000;

    private static readonly string[] Woff2KnownTags =
    {
        "cmap", "head", "hhea", "hmtx", "maxp", "name", "OS/2", "post", "cvt ", "fpgm", "glyf", "loca", "prep", "CFF ",
        "VORG", "EBDT", "EBLC", "gasp", "hdmx", "kern", "LTSH", "PCLT", "VDMX", "vhea", "vmtx", "BASE", "GDEF", "GPOS",
        "GSUB", "EBSC", "JSTF", "MATH", "CBDT", "CBLC", "COLR", "CPAL", "SVG ", "sbix", "acnt", "avar", "bdat", "bloc",
        "bsln", "cvar", "fdsc", "feat", "fmtx", "fvar", "gvar", "hsty", "just", "lcar", "mort", "morx", "opbd", "prop",
        "trak", "Zapf", "Silf", "Glat", "Gloc", "Feat", "Sill",
    };

    /// <summary>
    /// A static TTF with a <c>name</c> table (family, subfamily, full name and — when given — the typographic
    /// family) and an <c>OS/2</c> table with the weight and the italic flag.
    /// </summary>
    public static byte[] Ttf(string family, string subfamily, ushort weight, bool italic, string? typographicFamily = null, ushort fsType = 0)
    {
        List<(int, string)> names = new() { (1, family), (2, subfamily), (4, family + " " + subfamily) };
        if (typographicFamily is not null)
        {
            names.Add((16, typographicFamily));
        }

        return Sfnt(new Dictionary<string, byte[]>
        {
            ["name"] = NameTable(names.ToArray()),
            ["OS/2"] = Os2Table(weight, (ushort)((italic ? 0x0001 : 0) | (weight >= 700 ? 0x0020 : 0) | (!italic && weight < 700 ? 0x0040 : 0)), fsType),
        });
    }

    /// <summary>An SFNT file with the given tables (keys are 4-character tags).</summary>
    public static byte[] Sfnt(IReadOnlyDictionary<string, byte[]> tables, uint flavor = TrueTypeFlavor)
    {
        List<KeyValuePair<string, byte[]>> sorted = tables.OrderBy(t => t.Key, StringComparer.Ordinal).ToList();
        int headerSize = 12 + (16 * sorted.Count);
        List<byte> data = new(new byte[headerSize]);
        byte[] header = new byte[headerSize];
        BinaryPrimitives.WriteUInt32BigEndian(header.AsSpan(0), flavor);
        BinaryPrimitives.WriteUInt16BigEndian(header.AsSpan(4), (ushort)sorted.Count);
        for (int i = 0; i < sorted.Count; i++)
        {
            int rec = 12 + (16 * i);
            WriteTag(header.AsSpan(rec), sorted[i].Key);
            BinaryPrimitives.WriteUInt32BigEndian(header.AsSpan(rec + 8), (uint)data.Count);
            BinaryPrimitives.WriteUInt32BigEndian(header.AsSpan(rec + 12), (uint)sorted[i].Value.Length);
            data.AddRange(sorted[i].Value);
            while (data.Count % 4 != 0)
            {
                data.Add(0);
            }
        }

        byte[] result = data.ToArray();
        header.CopyTo(result, 0);
        return result;
    }

    /// <summary>The same tables as a WOFF file (each table zlib-compressed when that makes it smaller).</summary>
    public static byte[] Woff(IReadOnlyDictionary<string, byte[]> tables, uint flavor = TrueTypeFlavor)
    {
        List<KeyValuePair<string, byte[]>> sorted = tables.OrderBy(t => t.Key, StringComparer.Ordinal).ToList();
        int headerSize = 44 + (20 * sorted.Count);
        List<byte> data = new(new byte[headerSize]);
        byte[] header = new byte[headerSize];
        WriteTag(header.AsSpan(0), "wOFF");
        BinaryPrimitives.WriteUInt32BigEndian(header.AsSpan(4), flavor);
        BinaryPrimitives.WriteUInt16BigEndian(header.AsSpan(12), (ushort)sorted.Count);
        for (int i = 0; i < sorted.Count; i++)
        {
            byte[] table = sorted[i].Value;
            byte[] compressed = Zlib(table);
            byte[] stored = compressed.Length < table.Length ? compressed : table;
            int rec = 44 + (20 * i);
            WriteTag(header.AsSpan(rec), sorted[i].Key);
            BinaryPrimitives.WriteUInt32BigEndian(header.AsSpan(rec + 4), (uint)data.Count);
            BinaryPrimitives.WriteUInt32BigEndian(header.AsSpan(rec + 8), (uint)stored.Length);
            BinaryPrimitives.WriteUInt32BigEndian(header.AsSpan(rec + 12), (uint)table.Length);
            data.AddRange(stored);
            while (data.Count % 4 != 0)
            {
                data.Add(0);
            }
        }

        byte[] result = data.ToArray();
        header.CopyTo(result, 0);
        BinaryPrimitives.WriteUInt32BigEndian(result.AsSpan(8), (uint)result.Length);
        return result;
    }

    /// <summary>The same tables as a WOFF2 file (null transforms, one Brotli stream).</summary>
    public static byte[] Woff2(IReadOnlyDictionary<string, byte[]> tables, uint flavor = TrueTypeFlavor)
    {
        List<KeyValuePair<string, byte[]>> ordered = tables.ToList();
        List<byte> directory = new();
        using MemoryStream raw = new();
        foreach ((string tag, byte[] table) in ordered)
        {
            int known = Array.IndexOf(Woff2KnownTags, tag);
            int transform = tag is "glyf" or "loca" ? 3 : 0;
            if (known >= 0)
            {
                directory.Add((byte)(known | (transform << 6)));
            }
            else
            {
                directory.Add((byte)(63 | (transform << 6)));
                byte[] tagBytes = new byte[4];
                WriteTag(tagBytes, tag);
                directory.AddRange(tagBytes);
            }

            directory.AddRange(UIntBase128((uint)table.Length));
            raw.Write(table);
        }

        byte[] compressed;
        using (MemoryStream output = new())
        {
            using (BrotliStream brotli = new(output, CompressionLevel.Optimal, leaveOpen: true))
            {
                brotli.Write(raw.ToArray());
            }

            compressed = output.ToArray();
        }

        byte[] header = new byte[48];
        WriteTag(header, "wOF2");
        BinaryPrimitives.WriteUInt32BigEndian(header.AsSpan(4), flavor);
        BinaryPrimitives.WriteUInt16BigEndian(header.AsSpan(12), (ushort)ordered.Count);
        BinaryPrimitives.WriteUInt32BigEndian(header.AsSpan(20), (uint)compressed.Length);
        List<byte> result = new(header);
        result.AddRange(directory);
        result.AddRange(compressed);
        while (result.Count % 4 != 0)
        {
            result.Add(0);
        }

        byte[] bytes = result.ToArray();
        BinaryPrimitives.WriteUInt32BigEndian(bytes.AsSpan(8), (uint)bytes.Length);
        return bytes;
    }

    /// <summary>A <c>name</c> table with Windows (platform 3, UTF-16BE) records.</summary>
    public static byte[] NameTable(params (int NameId, string Value)[] names)
    {
        List<byte> storage = new();
        List<(int NameId, int Offset, int Length)> layout = new();
        foreach ((int nameId, string value) in names)
        {
            byte[] bytes = Encoding.BigEndianUnicode.GetBytes(value);
            layout.Add((nameId, storage.Count, bytes.Length));
            storage.AddRange(bytes);
        }

        byte[] table = new byte[6 + (12 * names.Length) + storage.Count];
        BinaryPrimitives.WriteUInt16BigEndian(table.AsSpan(2), (ushort)names.Length);
        BinaryPrimitives.WriteUInt16BigEndian(table.AsSpan(4), (ushort)(6 + (12 * names.Length)));
        for (int i = 0; i < layout.Count; i++)
        {
            int rec = 6 + (12 * i);
            BinaryPrimitives.WriteUInt16BigEndian(table.AsSpan(rec), 3);
            BinaryPrimitives.WriteUInt16BigEndian(table.AsSpan(rec + 2), 1);
            BinaryPrimitives.WriteUInt16BigEndian(table.AsSpan(rec + 4), 0x0409);
            BinaryPrimitives.WriteUInt16BigEndian(table.AsSpan(rec + 6), (ushort)layout[i].NameId);
            BinaryPrimitives.WriteUInt16BigEndian(table.AsSpan(rec + 8), (ushort)layout[i].Length);
            BinaryPrimitives.WriteUInt16BigEndian(table.AsSpan(rec + 10), (ushort)layout[i].Offset);
        }

        storage.CopyTo(table, 6 + (12 * names.Length));
        return table;
    }

    /// <summary>A version 4 <c>OS/2</c> table with the fields the font reader uses.</summary>
    public static byte[] Os2Table(
        ushort weightClass,
        ushort fsSelection,
        ushort fsType = 0,
        short familyClass = 0,
        byte panoseFamilyType = 0,
        byte panoseSerifStyle = 0,
        byte panoseProportion = 0)
    {
        byte[] table = new byte[96];
        BinaryPrimitives.WriteUInt16BigEndian(table.AsSpan(0), 4);
        BinaryPrimitives.WriteUInt16BigEndian(table.AsSpan(4), weightClass);
        BinaryPrimitives.WriteUInt16BigEndian(table.AsSpan(6), 5);
        BinaryPrimitives.WriteUInt16BigEndian(table.AsSpan(8), fsType);
        BinaryPrimitives.WriteInt16BigEndian(table.AsSpan(30), familyClass);
        table[32] = panoseFamilyType;
        table[33] = panoseSerifStyle;
        table[35] = panoseProportion;
        BinaryPrimitives.WriteUInt16BigEndian(table.AsSpan(62), fsSelection);
        return table;
    }

    /// <summary>An <c>fvar</c> table with the given axes (no named instances).</summary>
    public static byte[] FvarTable(params (string Tag, double Min, double Default, double Max)[] axes)
    {
        byte[] table = new byte[16 + (20 * axes.Length)];
        BinaryPrimitives.WriteUInt16BigEndian(table.AsSpan(0), 1);
        BinaryPrimitives.WriteUInt16BigEndian(table.AsSpan(4), 16);
        BinaryPrimitives.WriteUInt16BigEndian(table.AsSpan(6), 2);
        BinaryPrimitives.WriteUInt16BigEndian(table.AsSpan(8), (ushort)axes.Length);
        BinaryPrimitives.WriteUInt16BigEndian(table.AsSpan(10), 20);
        for (int i = 0; i < axes.Length; i++)
        {
            int rec = 16 + (20 * i);
            WriteTag(table.AsSpan(rec), axes[i].Tag);
            BinaryPrimitives.WriteInt32BigEndian(table.AsSpan(rec + 4), (int)(axes[i].Min * 65536));
            BinaryPrimitives.WriteInt32BigEndian(table.AsSpan(rec + 8), (int)(axes[i].Default * 65536));
            BinaryPrimitives.WriteInt32BigEndian(table.AsSpan(rec + 12), (int)(axes[i].Max * 65536));
        }

        return table;
    }

    private static byte[] Zlib(byte[] data)
    {
        using MemoryStream output = new();
        using (ZLibStream zlib = new(output, CompressionLevel.Optimal, leaveOpen: true))
        {
            zlib.Write(data);
        }

        return output.ToArray();
    }

    private static List<byte> UIntBase128(uint value)
    {
        List<byte> bytes = new() { (byte)(value & 0x7F) };
        value >>= 7;
        while (value > 0)
        {
            bytes.Insert(0, (byte)(0x80 | (value & 0x7F)));
            value >>= 7;
        }

        return bytes;
    }

    private static void WriteTag(Span<byte> target, string tag)
    {
        for (int i = 0; i < 4; i++)
        {
            target[i] = (byte)tag[i];
        }
    }
}
