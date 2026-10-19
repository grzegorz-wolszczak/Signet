using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.Text;
using AwesomeAssertions;
using Signet.Core.Fonts;
using Signet.Core.Tests.TestSupport;
using Xunit;

namespace Signet.Core.Tests.Fonts;

/// <summary>Tests of <see cref="OpenTypeFontInfo"/> (reading the <c>name</c> table of an SFNT font).</summary>
public sealed class OpenTypeFontInfoTests
{
    [Fact]
    public void Parse_reads_family_and_subfamily_from_windows_name_records()
    {
        // Arrange
        byte[] font = BuildFont(new (int PlatformId, int NameId, string Value)[]
        {
            (3, 1, "Acme Serif"),
            (3, 2, "Bold Italic"),
            (3, 4, "Acme Serif Bold Italic"),
            (3, 5, "Version 1.234"),
        });

        // Act
        FontFileInfo info = OpenTypeFontInfo.Parse(font);

        // Assert
        info.Family.Should().Be("Acme Serif");
        info.Subfamily.Should().Be("Bold Italic");
        info.FullName.Should().Be("Acme Serif Bold Italic");
        info.Version.Should().Be("Version 1.234");
    }

    [Fact]
    public void Parse_prefers_windows_record_over_mac_record_for_the_same_name_id()
    {
        // Arrange
        byte[] font = BuildFont(new (int, int, string)[]
        {
            (1, 1, "Mac Name"),
            (3, 1, "Windows Name"),
        });

        // Act & Assert
        OpenTypeFontInfo.Parse(font).Family.Should().Be("Windows Name");
    }

    [Fact]
    public void Parse_returns_empty_for_garbage_or_truncated_data()
    {
        // Act & Assert
        OpenTypeFontInfo.Parse(Array.Empty<byte>()).Should().Be(FontFileInfo.Empty);

        // Act & Assert
        OpenTypeFontInfo.Parse(new byte[] { 0x00, 0x01, 0x02, 0x03, 0x04, 0x05 }).Should().Be(FontFileInfo.Empty);

        // Act & Assert
        OpenTypeFontInfo.Parse(new byte[] { 0x00, 0x01, 0x00, 0x00, 0x00, 0x01, 0x0C, 0x0D, 0x0E, 0x0F, 0x10, 0x11 })
            .Should().Be(FontFileInfo.Empty);
    }

    [Fact]
    public void Read_returns_empty_for_missing_file()
    {
        // Act & Assert
        OpenTypeFontInfo.Read("/no/such/font.ttf").Should().Be(FontFileInfo.Empty);
    }

    [Fact]
    public void Parse_reads_weight_style_and_typographic_family_from_os2_and_name()
    {
        // Arrange
        byte[] font = FontFileBuilder.Ttf("Acme Light", "Italic", 300, italic: true, typographicFamily: "Acme");

        // Act
        FontFileInfo info = OpenTypeFontInfo.Parse(font);

        // Assert
        info.Format.Should().Be(FontFileFormat.TrueType);
        info.Family.Should().Be("Acme Light");
        info.TypographicFamily.Should().Be("Acme");
        info.PreferredFamily.Should().Be("Acme");
        info.WeightClass.Should().Be(300);
        info.IsItalic.Should().BeTrue();
        info.IsBold.Should().BeFalse();
    }

    [Fact]
    public void Parse_reads_the_weight_axis_of_a_variable_font()
    {
        // Arrange
        byte[] font = FontFileBuilder.Sfnt(new Dictionary<string, byte[]>
        {
            ["name"] = FontFileBuilder.NameTable((1, "Acme VF"), (2, "Regular")),
            ["OS/2"] = FontFileBuilder.Os2Table(400, 0x0040),
            ["fvar"] = FontFileBuilder.FvarTable(("wdth", 75, 100, 100), ("wght", 100, 400, 900)),
        });

        // Act
        FontFileInfo info = OpenTypeFontInfo.Parse(font);

        // Assert
        info.HasWeightAxis.Should().BeTrue();
        info.WeightAxisMin.Should().Be(100);
        info.WeightAxisMax.Should().Be(900);
    }

    [Fact]
    public void Parse_reads_woff_and_woff2_files()
    {
        // Arrange
        Dictionary<string, byte[]> tables = new()
        {
            ["name"] = FontFileBuilder.NameTable((1, "Acme"), (2, "Bold Italic")),
            ["OS/2"] = FontFileBuilder.Os2Table(700, 0x0021),
            ["post"] = new byte[32],
        };

        // Act
        FontFileInfo woff = OpenTypeFontInfo.Parse(FontFileBuilder.Woff(tables));
        FontFileInfo woff2 = OpenTypeFontInfo.Parse(FontFileBuilder.Woff2(tables));

        // Assert
        woff.Format.Should().Be(FontFileFormat.Woff);
        woff2.Format.Should().Be(FontFileFormat.Woff2);
        foreach (FontFileInfo info in new[] { woff, woff2 })
        {
            info.Family.Should().Be("Acme");
            info.Subfamily.Should().Be("Bold Italic");
            info.WeightClass.Should().Be(700);
            info.IsItalic.Should().BeTrue();
        }
    }

    [Fact]
    public void Parse_returns_empty_for_a_truncated_woff2()
    {
        // Arrange
        byte[] woff2 = FontFileBuilder.Woff2(new Dictionary<string, byte[]> { ["name"] = FontFileBuilder.NameTable((1, "Acme")) });

        // Act & Assert
        OpenTypeFontInfo.Parse(woff2[..(woff2.Length - 8)]).Should().Be(FontFileInfo.Empty);
    }

    // Builds a minimal SFNT file with a single `name` table containing the given records.
    private static byte[] BuildFont(IReadOnlyList<(int PlatformId, int NameId, string Value)> records)
    {
        // The string storage (UTF-16BE for platforms 3/0, ASCII for 1).
        List<byte> storage = new();
        List<(int Platform, int NameId, int Offset, int Length)> layout = new();
        foreach ((int platform, int nameId, string value) in records)
        {
            byte[] bytes = platform == 1
                ? Encoding.ASCII.GetBytes(value)
                : Encoding.BigEndianUnicode.GetBytes(value);
            layout.Add((platform, nameId, storage.Count, bytes.Length));
            storage.AddRange(bytes);
        }

        int recordCount = records.Count;
        int nameHeader = 6 + (recordCount * 12);
        List<byte> name = new();
        AppendUInt16(name, 0);              // format
        AppendUInt16(name, recordCount);    // count
        AppendUInt16(name, nameHeader);     // stringOffset
        foreach ((int platform, int nameId, int offset, int length) in layout)
        {
            AppendUInt16(name, platform);
            AppendUInt16(name, platform == 1 ? 0 : 1); // encodingID
            AppendUInt16(name, platform == 1 ? 0 : 0x409); // languageID
            AppendUInt16(name, nameId);
            AppendUInt16(name, length);
            AppendUInt16(name, offset);
        }

        name.AddRange(storage);

        const int sfntHeader = 12;
        const int tableRecord = 16;
        int nameOffset = sfntHeader + tableRecord;

        List<byte> font = new();
        AppendUInt32(font, 0x00010000);     // sfnt version
        AppendUInt16(font, 1);              // numTables
        AppendUInt16(font, 0);              // searchRange
        AppendUInt16(font, 0);              // entrySelector
        AppendUInt16(font, 0);              // rangeShift
        AppendUInt32(font, 0x6E616D65);     // 'name'
        AppendUInt32(font, 0);              // checksum
        AppendUInt32(font, (uint)nameOffset);
        AppendUInt32(font, (uint)name.Count);
        font.AddRange(name);
        return font.ToArray();
    }

    private static void AppendUInt16(List<byte> target, int value)
    {
        Span<byte> buffer = stackalloc byte[2];
        BinaryPrimitives.WriteUInt16BigEndian(buffer, (ushort)value);
        target.Add(buffer[0]);
        target.Add(buffer[1]);
    }

    private static void AppendUInt32(List<byte> target, uint value)
    {
        Span<byte> buffer = stackalloc byte[4];
        BinaryPrimitives.WriteUInt32BigEndian(buffer, value);
        target.AddRange(buffer.ToArray());
    }
}
