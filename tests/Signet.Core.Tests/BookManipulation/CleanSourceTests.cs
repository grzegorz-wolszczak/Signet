using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using AwesomeAssertions;
using Signet.Core.BookManipulation;
using Signet.Core.Tests.TestSupport;
using Xunit;

namespace Signet.Core.Tests.BookManipulation;

/// <summary>Tests of <see cref="CleanSource"/> — XHTML repair and normalization.</summary>
public sealed class CleanSourceTests
{
    private const string ValidXhtml =
        "<?xml version=\"1.0\" encoding=\"utf-8\"?>\n" +
        "<!DOCTYPE html>\n" +
        "<html xmlns=\"http://www.w3.org/1999/xhtml\" xmlns:epub=\"http://www.idpf.org/2007/ops\">\n" +
        "<head>\n" +
        "  <title>OK</title>\n" +
        "</head>\n" +
        "<body>\n" +
        "  <p>All good &amp; well.</p>\n" +
        "</body>\n" +
        "</html>\n";

    public static TheoryData<string> AllCorpusXhtml()
    {
        TheoryData<string> data = new();
        foreach (string path in Directory.EnumerateFiles(CorpusPaths.Root, "*.xhtml", SearchOption.AllDirectories))
        {
            data.Add(path);
        }

        return data;
    }

    /// <summary>
    /// The corpus files that are not faulty on purpose. <c>Mend</c> repairs an ordinary fault — an unclosed tag, a
    /// bare ampersand, a missing <c>body</c> — but it does not rewrite what a comment swallowed, so the books of
    /// <c>broken/</c> and <c>malformed/</c> are left to the tests written for them.
    /// </summary>
    public static TheoryData<string> SoundCorpusXhtml()
    {
        TheoryData<string> data = new();
        foreach (string path in Directory
                     .EnumerateFiles(CorpusPaths.Root, "*.xhtml", SearchOption.AllDirectories)
                     .Where(p => !CorpusPaths.IsFaultyOnPurpose(p)))
        {
            data.Add(path);
        }

        return data;
    }

    // =====================================================================
    //  Mend — naprawa struktury
    // =====================================================================

    [Fact]
    public void Mend_UnclosedAndMismatchedTags_BecomeWellFormed()
    {
        // Arrange
        const string broken =
            "<html><head><title>t</title></head><body>\n" +
            "  <p>never closed\n" +
            "  <p>and <br> not self-closed</p>\n" +
            "  <div><span>crossed</div></span>\n" +
            "</body></html>";

        // Act
        string mended = CleanSource.Mend(broken, "3.0");

        // Assert
        WellFormedChecker.Check(mended).IsWellFormed.Should().BeTrue();
        mended.Should().Contain("<br />");
    }

    [Fact]
    public void Mend_MissingHtmlHeadBody_AreInserted()
    {
        // Act
        string mended = CleanSource.Mend("<p>bare paragraph</p>", "3.0");

        // Assert
        string structure = WellFormedChecker.CheckXhtmlStructure(mended, "3.0").Message;
        WellFormedChecker.CheckXhtmlStructure(mended, "3.0").IsWellFormed.Should().BeTrue(structure);
    }

    [Fact]
    public void Mend_BareAmpersand_IsEscaped()
    {
        // Act
        string mended = CleanSource.Mend("<html><head><title>t</title></head><body><p>Tom & Jerry</p></body></html>", "3.0");

        // Assert
        mended.Should().Contain("Tom &amp; Jerry");
        WellFormedChecker.Check(mended).IsWellFormed.Should().BeTrue();
    }

    [Fact]
    public void Mend_StripsLeadingXmlDeclaration_AndReaddsCanonicalOne()
    {
        // Arrange
        const string source =
            "<?xml version=\"1.0\" encoding=\"UTF-8\"?>\n" +
            "<!DOCTYPE html>\n" +
            "<html><head><title>t</title></head><body><p>x</p></body></html>";

        // Act
        string mended = CleanSource.Mend(source, "3.0");

        // Assert
        mended.Should().StartWith("<?xml version=\"1.0\" encoding=\"utf-8\"?>\n");
        mended.IndexOf("<?xml", StringComparison.Ordinal).Should().Be(mended.LastIndexOf("<?xml", StringComparison.Ordinal));
    }

    [Fact]
    public void Mend_Epub2_UsesXhtml11Doctype()
    {
        // Act
        string mended = CleanSource.Mend("<html><head><title>t</title></head><body><p>x</p></body></html>", "2.0", addMissingDoctype: true);

        // Assert
        mended.Should().Contain("<!DOCTYPE html PUBLIC \"-//W3C//DTD XHTML 1.1//EN\"");
        mended.Should().Contain("xhtml11.dtd");
    }

    [Fact]
    public void Mend_Epub3_UsesHtml5Doctype()
    {
        // Act
        string mended = CleanSource.Mend("<html><head><title>t</title></head><body><p>x</p></body></html>", "3.0", addMissingDoctype: true);

        // Assert
        mended.Should().Contain("<!DOCTYPE html>\n");
        mended.Should().NotContain("PUBLIC");
    }

    [Fact]
    public void Mend_PreservesEpubTypeRoleAndAria()
    {
        // Arrange
        const string source =
            "<html><head><title>t</title></head>" +
            "<body epub:type=\"bodymatter\"><section role=\"doc-chapter\" aria-label=\"Chapter\">" +
            "<p>x</p></section></body></html>";

        // Act
        string mended = CleanSource.Mend(source, "3.0");

        // Assert
        mended.Should().Contain("epub:type=\"bodymatter\"");
        mended.Should().Contain("role=\"doc-chapter\"");
        mended.Should().Contain("aria-label=\"Chapter\"");
    }

    [Fact]
    public void Mend_NonBreakingSpace_BecomesNumericEntity()
    {
        // Act
        string fromLiteral = CleanSource.Mend("<html><head><title>t</title></head><body><p>a b</p></body></html>", "3.0");
        string fromNamed = CleanSource.Mend("<html><head><title>t</title></head><body><p>a&nbsp;b</p></body></html>", "3.0");

        // Assert
        fromLiteral.Should().Contain("a&#160;b");
        fromNamed.Should().Contain("a&#160;b");
        fromLiteral.Should().NotContain("&nbsp;");
    }

    [Fact]
    public void Mend_RemovesMetaCharsetFromHead()
    {
        // Arrange
        const string source =
            "<html><head><title>t</title><meta charset=\"utf-8\"></head><body><p>x</p></body></html>";

        // Act
        string mended = CleanSource.Mend(source, "3.0");

        // Assert
        mended.Should().NotContain("charset");
        WellFormedChecker.Check(mended).IsWellFormed.Should().BeTrue();
    }

    [Fact]
    public void Mend_AlreadyValidDocument_StaysWellFormedAndIsIdempotent()
    {
        // Act
        string once = CleanSource.Mend(ValidXhtml, "3.0");
        string twice = CleanSource.Mend(once, "3.0");

        // Assert
        WellFormedChecker.Check(once).IsWellFormed.Should().BeTrue();
        twice.Should().Be(once);
    }

    [Fact]
    public void Mend_EmptyInput_ReturnsEmpty()
    {
        // Act & Assert
        CleanSource.Mend(string.Empty, "3.0").Should().BeEmpty();
    }

    // =====================================================================
    //  ToValidXHTML — bramka well-formed
    // =====================================================================

    [Fact]
    public void ToValidXHTML_WellFormedInput_IsReturnedUnchanged()
    {
        // Act & Assert
        CleanSource.ToValidXHTML(ValidXhtml, "3.0").Should().BeSameAs(ValidXhtml);
    }

    [Fact]
    public void ToValidXHTML_MalformedInput_IsMendedToWellFormed()
    {
        // Arrange
        const string broken = "<html><head><title>t</title></head><body><p>x<p>y</body></html>";

        // Act
        string result = CleanSource.ToValidXHTML(broken, "3.0");

        // Assert
        result.Should().NotBe(broken);
        WellFormedChecker.Check(result).IsWellFormed.Should().BeTrue();
    }

    // =====================================================================
    //  Component steps
    // =====================================================================

    [Fact]
    public void PreprocessSpecialCases_StripsSvgPrefixAndEmbedsNamespace()
    {
        // Arrange
        const string source =
            "<svg:svg xmlns:svg=\"http://www.w3.org/2000/svg\" viewBox=\"0 0 1 1\">" +
            "<svg:rect width=\"1\" height=\"1\"/></svg:svg>";

        // Act
        string result = CleanSource.PreprocessSpecialCases(source);

        // Assert
        result.Should().StartWith("<svg xmlns=\"http://www.w3.org/2000/svg\"");
        result.Should().NotContain("xmlns:svg");
        result.Should().Contain("<rect ");
        result.Should().Contain("</svg>");
        result.Should().NotContain("svg:");
    }

    [Fact]
    public void RemoveMetaCharset_NoHead_ReturnsSourceUnchanged()
    {
        // Arrange
        const string source = "<p>no head here <meta charset=\"utf-8\"></p>";

        // Act & Assert
        CleanSource.RemoveMetaCharset(source).Should().Be(source);
    }

    [Fact]
    public void RemoveMetaCharset_RemovesOnlyTheMetaInsideHead()
    {
        // Arrange
        const string source =
            "<html><head><meta charset=\"utf-8\"/><title>t</title></head>" +
            "<body><p>text with the word charset in it</p></body></html>";

        // Act
        string result = CleanSource.RemoveMetaCharset(source);

        // Assert
        result.Should().NotContain("<meta");
        result.Should().Contain("the word charset in it");
    }

    [Theory]
    [InlineData("2.0")]
    [InlineData("3.0")]
    public void CharToEntity_ConvertsLiteralAndNamedNbspToNumericEntity(string version)
    {
        // Act & Assert
        CleanSource.CharToEntity("x y &nbsp; z", version).Should().Be("x&#160;y &#160; z");
    }

    [Fact]
    public void Mend_forwards_entity_overrides_to_CharToEntity()
    {
        // Arrange
        IReadOnlyDictionary<char, string> map = new Dictionary<char, string> { ['—'] = "&#8212;" };
        string source = "<html><head><title>t</title></head><body><p>a—b</p></body></html>";

        // Act
        string result = CleanSource.Mend(source, "3.0", map);

        // Assert
        result.Should().Contain("a&#8212;b");
    }

    [Fact]
    public void PrettyPrint_forwards_entity_overrides_to_CharToEntity()
    {
        // Arrange
        IReadOnlyDictionary<char, string> map = new Dictionary<char, string> { ['—'] = "&#8212;" };
        string source = "<?xml version=\"1.0\" encoding=\"utf-8\"?>\n<!DOCTYPE html>\n" +
            "<html xmlns=\"http://www.w3.org/1999/xhtml\"><head><title>t</title></head><body><p>a—b</p></body></html>";

        // Act
        string result = CleanSource.PrettyPrint(source, keepWhitespace: false, "3.0", entityOverrides: map);

        // Assert
        result.Should().Contain("a&#8212;b");
    }

    [Fact]
    public void CharToEntity_HonoursOverridesMap()
    {
        // Arrange
        IReadOnlyDictionary<char, string> map = new Dictionary<char, string> { ['—'] = "&#8212;" };

        // Act & Assert
        CleanSource.CharToEntity("a—b c", "3.0", map).Should().Be("a&#8212;b c");
    }

    [Fact]
    public void PrettifyDOCTYPEHeader_RepairsInvalidPublicIdentifier()
    {
        // Arrange
        // The DOCTYPE must not be at the very start (index > 0) — hence the leading XML prolog.
        const string source =
            "<?xml version=\"1.0\" encoding=\"utf-8\"?>\n" +
            "<!DOCTYPE html PUBLIC \"W3C//DTD XHTML 1.1//EN\">\n<html></html>";

        // Act & Assert
        CleanSource.PrettifyDOCTYPEHeader(source).Should()
            .Contain("<!DOCTYPE html PUBLIC \"-//W3C//DTD XHTML 1.1//EN\"");
    }

    [Fact]
    public void PrettifyDOCTYPEHeader_LeavesCleanEpub3DoctypeAlone()
    {
        // Arrange
        const string source = "<?xml version=\"1.0\" encoding=\"utf-8\"?>\n<!DOCTYPE html>\n\n<html></html>";

        // Act & Assert
        CleanSource.PrettifyDOCTYPEHeader(source).Should().Be(source);
    }

    // =====================================================================
    //  Korpus
    // =====================================================================

    [Theory]
    [MemberData(nameof(SoundCorpusXhtml))]
    public void Mend_EveryCorpusXhtml_IsWellFormedForBothEpubVersions(string xhtmlPath)
    {
        // Arrange
        string source = File.ReadAllText(xhtmlPath);

        // Act & Assert
        WellFormedChecker.Check(CleanSource.Mend(source, "2.0")).IsWellFormed.Should().BeTrue();

        // Act & Assert
        WellFormedChecker.Check(CleanSource.Mend(source, "3.0")).IsWellFormed.Should().BeTrue();
    }

    [Theory]
    [MemberData(nameof(AllCorpusXhtml))]
    public void Mend_EveryCorpusXhtml_IsIdempotent(string xhtmlPath)
    {
        // Act
        string once = CleanSource.Mend(File.ReadAllText(xhtmlPath), "3.0");

        // Act & Assert
        CleanSource.Mend(once, "3.0").Should().Be(once);
    }

    [Fact]
    public void Mend_NotWellFormedCorpusFixture_BecomesWellFormed()
    {
        // Arrange
        string source = File.ReadAllText(
            Path.Combine(CorpusPaths.Malformed("not-wellformed-xhtml"), "EPUB", "text", "chapter1.xhtml"));

        // Act & Assert
        WellFormedChecker.Check(source).IsWellFormed.Should().BeFalse();

        // Act & Assert
        WellFormedChecker.Check(CleanSource.Mend(source, "3.0")).IsWellFormed.Should().BeTrue();
    }

    /// <summary>
    /// What <c>Mend</c> does not repair, found in October 2026 while writing <c>tests/corpus/broken/markup</c>:
    /// a comment holding a <c>--</c>. XML ends a comment at the first <c>--</c>, so the text after it is markup
    /// again — and that text is prose, not tags. Mending it would mean deciding where the author meant the comment
    /// to end, which <c>Mend</c> does not guess; the file is left not well formed, both before and after.
    /// This test asserts today's behaviour so the day it changes, something says it changed.
    /// </summary>
    [Fact]
    public void Mend_DoesNotRepairACommentHoldingADoubleHyphen()
    {
        // Arrange
        string source = File.ReadAllText(
            Path.Combine(CorpusPaths.Broken("markup"), "EPUB", "text", "comments.xhtml"));

        // Act & Assert
        WellFormedChecker.Check(source).IsWellFormed.Should().BeFalse("XML ends the comment at the first --");

        // Act & Assert
        WellFormedChecker.Check(CleanSource.Mend(source, "3.0")).IsWellFormed.Should().BeFalse();
    }
}
