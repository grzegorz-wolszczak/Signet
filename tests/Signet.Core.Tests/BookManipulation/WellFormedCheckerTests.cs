using System;
using System.IO;
using System.Linq;
using AwesomeAssertions;
using Signet.Core.BookManipulation;
using Signet.Core.Tests.TestSupport;
using Xunit;
using Signet.Core.Localization;

namespace Signet.Core.Tests.BookManipulation;

/// <summary>Tests of <see cref="WellFormedChecker"/> — XML/XHTML syntax validation.</summary>
public sealed class WellFormedCheckerTests
{
    private const string ValidXhtml =
        "<?xml version=\"1.0\" encoding=\"utf-8\"?>\n" +
        "<!DOCTYPE html>\n" +
        "<html xmlns=\"http://www.w3.org/1999/xhtml\" xmlns:epub=\"http://www.idpf.org/2007/ops\">\n" +
        "<head>\n" +
        "  <title>OK</title>\n" +
        "  <meta charset=\"utf-8\"/>\n" +
        "</head>\n" +
        "<body>\n" +
        "  <p>All good &amp; well.</p>\n" +
        "</body>\n" +
        "</html>\n";

    public static TheoryData<string> AllCorpusMarkup()
    {
        TheoryData<string> data = new();
        foreach (string path in Directory
                     .EnumerateFiles(CorpusPaths.Root, "*.*", SearchOption.AllDirectories)
                     .Where(p => p.EndsWith(".xhtml", StringComparison.Ordinal)
                                 || p.EndsWith(".opf", StringComparison.Ordinal)
                                 || p.EndsWith(".ncx", StringComparison.Ordinal))
                     .Where(p => !p.Contains($"{Path.DirectorySeparatorChar}malformed{Path.DirectorySeparatorChar}", StringComparison.Ordinal)))
        {
            data.Add(path);
        }

        return data;
    }

    [Fact]
    public void Check_ValidXhtml_IsWellFormed()
    {
        WellFormedResult result = WellFormedChecker.Check(ValidXhtml, "application/xhtml+xml");

        result.IsWellFormed.Should().BeTrue();
        result.Line.Should().Be(-1);
        result.Column.Should().Be(-1);
        result.Message.Should().Be("well-formed");
    }

    [Fact]
    public void Check_UnclosedTag_IsNotWellFormed_WithPlausibleLine()
    {
        const string source =
            "<?xml version=\"1.0\"?>\n" +
            "<!DOCTYPE html>\n" +
            "<html><head><title>t</title></head>\n" +
            "<body>\n" +
            "  <p>never closed\n" +
            "</body>\n" +
            "</html>\n";

        WellFormedResult result = WellFormedChecker.Check(source);

        result.IsWellFormed.Should().BeFalse();
        result.Line.Should().BeInRange(5, 7);
        result.Message.Should().NotBeNullOrEmpty();
    }

    [Fact]
    public void Check_MismatchedNesting_ReportsNestingErrorMessageAtCloseTag()
    {
        WellFormedResult result = WellFormedChecker.Check("<root><a><b>x</a></b></root>");

        result.IsWellFormed.Should().BeFalse();
        result.Line.Should().Be(1);
        result.Message.Should().Be(CoreStrings.Format("Nesting_UnmatchedClosingTag", "a"));
    }

    [Fact]
    public void Check_UnescapedAmpersand_IsNotWellFormed()
    {
        WellFormedChecker.Check("<root>Tom & Jerry</root>").IsWellFormed.Should().BeFalse();
    }

    [Fact]
    public void Check_DuplicateAttribute_IsNotWellFormed()
    {
        WellFormedChecker.Check("<root><a x=\"1\" x=\"2\"/></root>").IsWellFormed.Should().BeFalse();
    }

    [Fact]
    public void Check_NamedHtmlEntity_IsNotWellFormed_DtdResolutionDisabled()
    {
        WellFormedChecker.Check("<p>hard&nbsp;space</p>").IsWellFormed.Should().BeFalse();
    }

    [Fact]
    public void Check_MalformedCorpusXhtml_DetectedNearLineSeven()
    {
        string source = File.ReadAllText(
            Path.Combine(CorpusPaths.Malformed("not-wellformed-xhtml"), "EPUB", "text", "chapter1.xhtml"));

        WellFormedResult result = WellFormedChecker.Check(source, "application/xhtml+xml");

        result.IsWellFormed.Should().BeFalse();
        result.Line.Should().BeInRange(6, 8);
    }

    [Fact]
    public void Check_MalformedCorpusOpf_IsNotWellFormed()
    {
        string source = File.ReadAllText(
            Path.Combine(CorpusPaths.Malformed("bad-opf-xml"), "EPUB", "package.opf"));

        WellFormedChecker.Check(source, "application/oebps-package+xml").IsWellFormed.Should().BeFalse();
    }

    [Fact]
    public void IsWellFormed_MatchesCheck()
    {
        WellFormedChecker.IsWellFormed(ValidXhtml).Should().BeTrue();
        WellFormedChecker.IsWellFormed("<root><a></b></root>").Should().BeFalse();
    }

    [Fact]
    public void CheckXhtmlStructure_ValidDocument_Passes()
    {
        WellFormedChecker.CheckXhtmlStructure(ValidXhtml, "3.0").IsWellFormed.Should().BeTrue();
    }

    [Theory]
    [InlineData(
        "<html xmlns=\"http://www.w3.org/1999/xhtml\"><head><title>t</title></head><body><p>x</p></body></html>",
        "DOCTYPE")]
    [InlineData(
        "<!DOCTYPE html>\n<html xmlns=\"http://www.w3.org/1999/xhtml\"><head><title>t</title></head></html>",
        "<body>")]
    [InlineData(
        "<!DOCTYPE html>\n<section><head><title>t</title></head><body><p>x</p></body></section>",
        "<html>")]
    public void CheckXhtmlStructure_MissingRequiredElement_IsReported(string source, string expectedFragment)
    {
        WellFormedResult result = WellFormedChecker.CheckXhtmlStructure(source);

        result.IsWellFormed.Should().BeFalse();
        result.Message.Should().Contain(expectedFragment);
        result.Line.Should().Be(1);
    }

    [Fact]
    public void CheckXhtmlStructure_NotWellFormed_ReturnsUnderlyingSyntaxError()
    {
        const string source = "<!DOCTYPE html>\n<html><head><title>t</title></head><body><p>x</body></html>";

        WellFormedResult result = WellFormedChecker.CheckXhtmlStructure(source);

        result.IsWellFormed.Should().BeFalse();
        result.Message.Should().Contain("</body>");
    }

    [Theory]
    [MemberData(nameof(AllCorpusMarkup))]
    public void Check_EveryValidCorpusMarkupFile_IsWellFormed(string path)
    {
        WellFormedChecker.Check(File.ReadAllText(path)).IsWellFormed.Should().BeTrue();
    }
}
