using System;
using System.Collections.Generic;
using System.IO;
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

    // =====================================================================
    //  Mend — naprawa struktury
    // =====================================================================

    [Fact]
    public void Mend_UnclosedAndMismatchedTags_BecomeWellFormed()
    {
        const string broken =
            "<html><head><title>t</title></head><body>\n" +
            "  <p>never closed\n" +
            "  <p>and <br> not self-closed</p>\n" +
            "  <div><span>crossed</div></span>\n" +
            "</body></html>";

        string mended = CleanSource.Mend(broken, "3.0");

        WellFormedChecker.Check(mended).IsWellFormed.Should().BeTrue();
        mended.Should().Contain("<br />");
    }

    [Fact]
    public void Mend_MissingHtmlHeadBody_AreInserted()
    {
        string mended = CleanSource.Mend("<p>bare paragraph</p>", "3.0");

        string structure = WellFormedChecker.CheckXhtmlStructure(mended, "3.0").Message;
        WellFormedChecker.CheckXhtmlStructure(mended, "3.0").IsWellFormed.Should().BeTrue(structure);
    }

    [Fact]
    public void Mend_BareAmpersand_IsEscaped()
    {
        string mended = CleanSource.Mend("<html><head><title>t</title></head><body><p>Tom & Jerry</p></body></html>", "3.0");

        mended.Should().Contain("Tom &amp; Jerry");
        WellFormedChecker.Check(mended).IsWellFormed.Should().BeTrue();
    }

    [Fact]
    public void Mend_StripsLeadingXmlDeclaration_AndReaddsCanonicalOne()
    {
        const string source =
            "<?xml version=\"1.0\" encoding=\"UTF-8\"?>\n" +
            "<!DOCTYPE html>\n" +
            "<html><head><title>t</title></head><body><p>x</p></body></html>";

        string mended = CleanSource.Mend(source, "3.0");

        mended.Should().StartWith("<?xml version=\"1.0\" encoding=\"utf-8\"?>\n");
        mended.IndexOf("<?xml", StringComparison.Ordinal).Should().Be(mended.LastIndexOf("<?xml", StringComparison.Ordinal));
    }

    [Fact]
    public void Mend_Epub2_UsesXhtml11Doctype()
    {
        string mended = CleanSource.Mend("<html><head><title>t</title></head><body><p>x</p></body></html>", "2.0");

        mended.Should().Contain("<!DOCTYPE html PUBLIC \"-//W3C//DTD XHTML 1.1//EN\"");
        mended.Should().Contain("xhtml11.dtd");
    }

    [Fact]
    public void Mend_Epub3_UsesHtml5Doctype()
    {
        string mended = CleanSource.Mend("<html><head><title>t</title></head><body><p>x</p></body></html>", "3.0");

        mended.Should().Contain("<!DOCTYPE html>\n");
        mended.Should().NotContain("PUBLIC");
    }

    [Fact]
    public void Mend_PreservesEpubTypeRoleAndAria()
    {
        const string source =
            "<html><head><title>t</title></head>" +
            "<body epub:type=\"bodymatter\"><section role=\"doc-chapter\" aria-label=\"Chapter\">" +
            "<p>x</p></section></body></html>";

        string mended = CleanSource.Mend(source, "3.0");

        mended.Should().Contain("epub:type=\"bodymatter\"");
        mended.Should().Contain("role=\"doc-chapter\"");
        mended.Should().Contain("aria-label=\"Chapter\"");
    }

    [Fact]
    public void Mend_NonBreakingSpace_BecomesNumericEntity()
    {
        string fromLiteral = CleanSource.Mend("<html><head><title>t</title></head><body><p>a b</p></body></html>", "3.0");
        string fromNamed = CleanSource.Mend("<html><head><title>t</title></head><body><p>a&nbsp;b</p></body></html>", "3.0");

        fromLiteral.Should().Contain("a&#160;b");
        fromNamed.Should().Contain("a&#160;b");
        fromLiteral.Should().NotContain("&nbsp;");
    }

    [Fact]
    public void Mend_RemovesMetaCharsetFromHead()
    {
        const string source =
            "<html><head><title>t</title><meta charset=\"utf-8\"></head><body><p>x</p></body></html>";

        string mended = CleanSource.Mend(source, "3.0");

        mended.Should().NotContain("charset");
        WellFormedChecker.Check(mended).IsWellFormed.Should().BeTrue();
    }

    [Fact]
    public void Mend_AlreadyValidDocument_StaysWellFormedAndIsIdempotent()
    {
        string once = CleanSource.Mend(ValidXhtml, "3.0");
        string twice = CleanSource.Mend(once, "3.0");

        WellFormedChecker.Check(once).IsWellFormed.Should().BeTrue();
        twice.Should().Be(once);
    }

    [Fact]
    public void Mend_EmptyInput_ReturnsEmpty()
    {
        CleanSource.Mend(string.Empty, "3.0").Should().BeEmpty();
    }

    // =====================================================================
    //  ToValidXHTML — bramka well-formed
    // =====================================================================

    [Fact]
    public void ToValidXHTML_WellFormedInput_IsReturnedUnchanged()
    {
        CleanSource.ToValidXHTML(ValidXhtml, "3.0").Should().BeSameAs(ValidXhtml);
    }

    [Fact]
    public void ToValidXHTML_MalformedInput_IsMendedToWellFormed()
    {
        const string broken = "<html><head><title>t</title></head><body><p>x<p>y</body></html>";

        string result = CleanSource.ToValidXHTML(broken, "3.0");

        result.Should().NotBe(broken);
        WellFormedChecker.Check(result).IsWellFormed.Should().BeTrue();
    }

    // =====================================================================
    //  Component steps
    // =====================================================================

    [Fact]
    public void PreprocessSpecialCases_StripsSvgPrefixAndEmbedsNamespace()
    {
        const string source =
            "<svg:svg xmlns:svg=\"http://www.w3.org/2000/svg\" viewBox=\"0 0 1 1\">" +
            "<svg:rect width=\"1\" height=\"1\"/></svg:svg>";

        string result = CleanSource.PreprocessSpecialCases(source);

        result.Should().StartWith("<svg xmlns=\"http://www.w3.org/2000/svg\"");
        result.Should().NotContain("xmlns:svg");
        result.Should().Contain("<rect ");
        result.Should().Contain("</svg>");
        result.Should().NotContain("svg:");
    }

    [Fact]
    public void RemoveMetaCharset_NoHead_ReturnsSourceUnchanged()
    {
        const string source = "<p>no head here <meta charset=\"utf-8\"></p>";

        CleanSource.RemoveMetaCharset(source).Should().Be(source);
    }

    [Fact]
    public void RemoveMetaCharset_RemovesOnlyTheMetaInsideHead()
    {
        const string source =
            "<html><head><meta charset=\"utf-8\"/><title>t</title></head>" +
            "<body><p>text with the word charset in it</p></body></html>";

        string result = CleanSource.RemoveMetaCharset(source);

        result.Should().NotContain("<meta");
        result.Should().Contain("the word charset in it");
    }

    [Theory]
    [InlineData("2.0")]
    [InlineData("3.0")]
    public void CharToEntity_ConvertsLiteralAndNamedNbspToNumericEntity(string version)
    {
        CleanSource.CharToEntity("x y &nbsp; z", version).Should().Be("x&#160;y &#160; z");
    }

    [Fact]
    public void Mend_forwards_entity_overrides_to_CharToEntity()
    {
        IReadOnlyDictionary<char, string> map = new Dictionary<char, string> { ['—'] = "&#8212;" };
        string source = "<html><head><title>t</title></head><body><p>a—b</p></body></html>";

        string result = CleanSource.Mend(source, "3.0", map);

        result.Should().Contain("a&#8212;b");
    }

    [Fact]
    public void PrettyPrint_forwards_entity_overrides_to_CharToEntity()
    {
        IReadOnlyDictionary<char, string> map = new Dictionary<char, string> { ['—'] = "&#8212;" };
        string source = "<?xml version=\"1.0\" encoding=\"utf-8\"?>\n<!DOCTYPE html>\n" +
            "<html xmlns=\"http://www.w3.org/1999/xhtml\"><head><title>t</title></head><body><p>a—b</p></body></html>";

        string result = CleanSource.PrettyPrint(source, keepWhitespace: false, "3.0", entityOverrides: map);

        result.Should().Contain("a&#8212;b");
    }

    [Fact]
    public void CharToEntity_HonoursOverridesMap()
    {
        IReadOnlyDictionary<char, string> map = new Dictionary<char, string> { ['—'] = "&#8212;" };

        CleanSource.CharToEntity("a—b c", "3.0", map).Should().Be("a&#8212;b c");
    }

    [Fact]
    public void PrettifyDOCTYPEHeader_RepairsInvalidPublicIdentifier()
    {
        // The DOCTYPE must not be at the very start (index > 0) — hence the leading XML prolog.
        const string source =
            "<?xml version=\"1.0\" encoding=\"utf-8\"?>\n" +
            "<!DOCTYPE html PUBLIC \"W3C//DTD XHTML 1.1//EN\">\n<html></html>";

        CleanSource.PrettifyDOCTYPEHeader(source).Should()
            .Contain("<!DOCTYPE html PUBLIC \"-//W3C//DTD XHTML 1.1//EN\"");
    }

    [Fact]
    public void PrettifyDOCTYPEHeader_LeavesCleanEpub3DoctypeAlone()
    {
        const string source = "<?xml version=\"1.0\" encoding=\"utf-8\"?>\n<!DOCTYPE html>\n\n<html></html>";

        CleanSource.PrettifyDOCTYPEHeader(source).Should().Be(source);
    }

    // =====================================================================
    //  Korpus
    // =====================================================================

    [Theory]
    [MemberData(nameof(AllCorpusXhtml))]
    public void Mend_EveryCorpusXhtml_IsWellFormedForBothEpubVersions(string xhtmlPath)
    {
        string source = File.ReadAllText(xhtmlPath);

        WellFormedChecker.Check(CleanSource.Mend(source, "2.0")).IsWellFormed.Should().BeTrue();
        WellFormedChecker.Check(CleanSource.Mend(source, "3.0")).IsWellFormed.Should().BeTrue();
    }

    [Theory]
    [MemberData(nameof(AllCorpusXhtml))]
    public void Mend_EveryCorpusXhtml_IsIdempotent(string xhtmlPath)
    {
        string once = CleanSource.Mend(File.ReadAllText(xhtmlPath), "3.0");

        CleanSource.Mend(once, "3.0").Should().Be(once);
    }

    [Fact]
    public void Mend_NotWellFormedCorpusFixture_BecomesWellFormed()
    {
        string source = File.ReadAllText(
            Path.Combine(CorpusPaths.Malformed("not-wellformed-xhtml"), "EPUB", "text", "chapter1.xhtml"));

        WellFormedChecker.Check(source).IsWellFormed.Should().BeFalse();
        WellFormedChecker.Check(CleanSource.Mend(source, "3.0")).IsWellFormed.Should().BeTrue();
    }
}
