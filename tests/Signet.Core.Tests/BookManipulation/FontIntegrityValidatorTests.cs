using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using AwesomeAssertions;
using Signet.Core.BookManipulation;
using Signet.Core.Resources;
using Signet.Core.Tests.TestSupport;
using Xunit;

namespace Signet.Core.Tests.BookManipulation;

/// <summary>
/// Tests of <see cref="FontIntegrityValidator"/> (checking
/// fonts: embeddability, name aliasing).
/// </summary>
/// <remarks>
/// The fixture <c>tests/corpus/epub3/media/EPUB/fonts/font.ttf</c> has in its CSS the declaration
/// <c>@font-face { font-family: "Fancy"; src: url("../fonts/font.ttf"); }</c>, but the font file
/// itself is deliberately random bytes (not a real SFNT) — the tests overwrite the file content on
/// disk (<c>FontResource.FullPath</c>) with a synthetic, minimal SFNT font built
/// right here, to control the family/<c>fsType</c> that is read.
/// </remarks>
public sealed class FontIntegrityValidatorTests
{
    [Fact]
    public void Validate_returns_no_results_when_font_unparsable()
    {
        using Book book = OpenMutableBook();

        var results = FontIntegrityValidator.Validate(book);

        results.Should().BeEmpty();
    }

    [Fact]
    public void Validate_returns_no_results_when_family_matches_and_not_restricted()
    {
        using Book book = OpenMutableBook();
        OverwriteFont(book, family: "Fancy", fsType: 0x0000);

        var results = FontIntegrityValidator.Validate(book);

        results.Should().BeEmpty();
    }

    [Fact]
    public void Validate_reports_family_mismatch()
    {
        using Book book = OpenMutableBook();
        OverwriteFont(book, family: "Other Name", fsType: 0x0000);

        var results = FontIntegrityValidator.Validate(book);

        results.Should().Contain(r =>
            r.Severity == ValidationSeverity.Warning &&
            r.BookPath.EndsWith("style.css", StringComparison.Ordinal) &&
            r.Message.Contains("Fancy") &&
            r.Message.Contains("Other Name"));
    }

    [Fact]
    public void Validate_reports_embedding_restricted_font()
    {
        using Book book = OpenMutableBook();
        OverwriteFont(book, family: "Fancy", fsType: 0x0002);

        var results = FontIntegrityValidator.Validate(book);

        results.Should().Contain(r =>
            r.Severity == ValidationSeverity.Warning &&
            r.BookPath.EndsWith("font.ttf", StringComparison.Ordinal) &&
            r.Message.Contains("OS/2.fsType"));
    }

    [Fact]
    public void Validate_does_not_report_embedding_restriction_for_editable_fstype()
    {
        using Book book = OpenMutableBook();
        OverwriteFont(book, family: "Fancy", fsType: 0x0008); // bit 3 "Editable" — dopuszcza embedding

        var results = FontIntegrityValidator.Validate(book);

        results.Should().BeEmpty();
    }

    private static void OverwriteFont(Book book, string family, ushort fsType)
    {
        FontResource font = book.GetAllResources().OfType<FontResource>().First();
        File.WriteAllBytes(font.FullPath, BuildFont(family, fsType));
    }

    private static Book OpenMutableBook()
    {
        TempDir temp = new();
        string epub = EpubBuilder.BuildInto(CorpusPaths.Epub3Media, temp);
        return new ImportEpub(epub).GetBook();
    }

    // Builds a minimal SFNT with a 'name' table (nameID 1 = family, the Windows platform) and
    // an 'OS/2' table containing only the fsType field (offset 8), analogous to
    // OpenTypeFontInfoTests.BuildFont, but with an added OS/2 table.
    private static byte[] BuildFont(string family, ushort fsType)
    {
        byte[] familyBytes = Encoding.BigEndianUnicode.GetBytes(family);

        List<byte> name = new();
        AppendUInt16(name, 0);  // format
        AppendUInt16(name, 1);  // count
        AppendUInt16(name, 6 + 12); // stringOffset
        AppendUInt16(name, 3);      // platformID Windows
        AppendUInt16(name, 1);      // encodingID
        AppendUInt16(name, 0x409);  // languageID
        AppendUInt16(name, 1);      // nameID Family
        AppendUInt16(name, familyBytes.Length);
        AppendUInt16(name, 0);      // offset w storage
        name.AddRange(familyBytes);

        List<byte> os2 = new();
        os2.AddRange(new byte[8]); // version, xAvgCharWidth, usWeightClass, usWidthClass
        AppendUInt16(os2, fsType);

        const int sfntHeader = 12;
        const int tableRecord = 16;
        const int numTables = 2;
        int nameOffset = sfntHeader + (tableRecord * numTables);
        int os2Offset = nameOffset + name.Count;

        List<byte> font = new();
        AppendUInt32(font, 0x00010000); // sfnt version
        AppendUInt16(font, numTables);
        AppendUInt16(font, 0);
        AppendUInt16(font, 0);
        AppendUInt16(font, 0);

        AppendUInt32(font, 0x4F532F32); // 'OS/2'
        AppendUInt32(font, 0);
        AppendUInt32(font, (uint)os2Offset);
        AppendUInt32(font, (uint)os2.Count);

        AppendUInt32(font, 0x6E616D65); // 'name'
        AppendUInt32(font, 0);
        AppendUInt32(font, (uint)nameOffset);
        AppendUInt32(font, (uint)name.Count);

        font.AddRange(name);
        font.AddRange(os2);
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
