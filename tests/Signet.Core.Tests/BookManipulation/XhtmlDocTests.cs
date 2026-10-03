using System;
using System.IO;
using System.Linq;
using AngleSharp.Dom;
using AngleSharp.Html.Dom;
using AwesomeAssertions;
using Signet.Core.BookManipulation;
using Signet.Core.Tests.TestSupport;
using Xunit;

namespace Signet.Core.Tests.BookManipulation;

/// <summary>Tests of <see cref="XhtmlDoc"/> — the shared read operations on XHTML documents.</summary>
public sealed class XhtmlDocTests
{
    private static readonly string[] ExpectedResolvedHrefs =
    {
        "EPUB/styles/main.css",
        "EPUB/js/app.js",
        "EPUB/text/other.xhtml",
        "EPUB/img/pic.png",
    };

    private static readonly string[] LinkElementTags = { "a", "link", "img", "script" };

    private const string Sample =
        "<?xml version=\"1.0\" encoding=\"utf-8\"?>\n" +
        "<!DOCTYPE html>\n" +
        "<html xmlns=\"http://www.w3.org/1999/xhtml\" lang=\"en\">\n" +
        "<head>\n" +
        "  <title>T</title>\n" +
        "  <link rel=\"stylesheet\" type=\"text/css\" href=\"../styles/main.css\"/>\n" +
        "  <script src=\"../js/app.js\"></script>\n" +
        "</head>\n" +
        "<body class=\"chap  fancy\">\n" +
        "  <h1 id=\"top\">Title</h1>\n" +
        "  <p id=\"p1\">Hello <b>bold</b> world</p>\n" +
        "  <h2>Sub &amp; section</h2>\n" +
        "  <a name=\"legacy\">anchor</a>\n" +
        "  <a href=\"other.xhtml#frag\">link</a>\n" +
        "  <img src=\"../img/pic.png\" alt=\"x\"/>\n" +
        "  <a href=\"https://example.com/\">ext</a>\n" +
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

    [Fact]
    public void ParseThenSerialize_PreservesEpubTypeAndClosesEmptyElements()
    {
        const string source =
            "<html xmlns=\"http://www.w3.org/1999/xhtml\"><body>" +
            "<nav epub:type=\"toc\"><ol><li><a href=\"a.xhtml\">A</a></li></ol></nav><hr></body></html>";

        IHtmlDocument document = XhtmlDoc.Parse(source);
        string xhtml = XhtmlDoc.Serialize(document.Body!);

        xhtml.Should().Contain("epub:type=\"toc\"");
        xhtml.Should().Contain("<hr />");
    }

    [Fact]
    public void GetAllDescendantIds_IncludesIdAttributesAndLegacyAnchorNames()
    {
        XhtmlDoc.GetAllDescendantIds(Sample).Should().Equal("top", "p1", "legacy");
    }

    [Fact]
    public void GetIdsInFile_ReturnsOnlyIdAttributes()
    {
        XhtmlDoc.GetIdsInFile(Sample).Should().Equal("top", "p1");
    }

    [Fact]
    public void GetAllDescendantHrefs_ReturnsRawValues_Deduplicated()
    {
        XhtmlDoc.GetAllDescendantHrefs(Sample)
            .Should().Equal("../styles/main.css", "other.xhtml#frag", "https://example.com/");
    }

    [Fact]
    public void GetLinkElements_CollectsAnchorLinkImgScriptWithNaturalAttribute()
    {
        var links = XhtmlDoc.GetLinkElements(Sample);

        links.Should().Contain(new LinkElement("link", "href", "../styles/main.css"));
        links.Should().Contain(new LinkElement("script", "src", "../js/app.js"));
        links.Should().Contain(new LinkElement("img", "src", "../img/pic.png"));
        links.Should().Contain(new LinkElement("a", "href", "other.xhtml#frag"));
        links.Should().OnlyContain(l => LinkElementTags.Contains(l.TagName));
    }

    [Fact]
    public void ResolveHrefs_RelativeReferencesBecomeBookPaths_AbsoluteAndFragmentSkipped()
    {
        var resolved = XhtmlDoc.ResolveHrefs(Sample, "EPUB/text");

        resolved.Should().BeEquivalentTo(ExpectedResolvedHrefs);
    }

    [Fact]
    public void ResolveHrefs_DeepFoldersFixture_ResolvesAgainstFileFolder()
    {
        string file = Path.Combine(CorpusPaths.EdgeDeepFolders, "content", "pages", "body", "ch01.xhtml");
        string source = File.ReadAllText(file);

        var resolved = XhtmlDoc.ResolveHrefs(source, "content/pages/body");

        resolved.Should().Contain("content/pages/front/title.xhtml");
        resolved.Should().Contain("content/resources/styles/main.css");
        resolved.Should().Contain("content/resources/img/pic.png");
    }

    [Fact]
    public void GetHeadingElements_ReturnsLevelIdAndCollapsedText_InDocumentOrder()
    {
        var headings = XhtmlDoc.GetHeadingElements(Sample);

        headings.Should().HaveCount(2);
        headings[0].Should().Be(new HeadingElement("h1", 1, "top", "Title"));
        headings[1].Should().Be(new HeadingElement("h2", 2, string.Empty, "Sub & section"));
    }

    [Theory]
    [InlineData("<html dir=\"rtl\"><body></body></html>", "rtl")]
    [InlineData("<html><body dir=\"RTL\"></body></html>", "rtl")]
    [InlineData("<html dir=\"ltr\"><body dir=\"rtl\"></body></html>", "ltr")]
    [InlineData("<html><body></body></html>", "")]
    [InlineData("<html dir=\"sideways\"><body></body></html>", "")]
    public void GetDominantTextDirection_PrefersHtmlThenBody(string source, string expected)
    {
        XhtmlDoc.GetDominantTextDirection(source).Should().Be(expected);
    }

    [Fact]
    public void GetBodyClasses_SplitsOnWhitespace()
    {
        XhtmlDoc.GetBodyClasses(Sample).Should().Equal("chap", "fancy");
        XhtmlDoc.GetBodyClasses("<html><body></body></html>").Should().BeEmpty();
    }

    [Fact]
    public void OffsetFromNode_ReturnsStartOfOpeningTag()
    {
        IHtmlDocument document = XhtmlDoc.Parse(Sample);
        IElement h2 = document.QuerySelector("h2")!;

        XhtmlDoc.OffsetFromNode(h2).Should().Be(Sample.IndexOf("<h2>", StringComparison.Ordinal));
    }

    [Fact]
    public void NodeFromOffset_ReturnsDeepestElementContainingOffset()
    {
        IHtmlDocument document = XhtmlDoc.Parse(Sample);

        int insideBold = Sample.IndexOf("bold</b>", StringComparison.Ordinal);
        INode? node = XhtmlDoc.NodeFromOffset(document, insideBold);
        (node as IElement)?.LocalName.Should().Be("b");

        int atH1 = Sample.IndexOf("<h1", StringComparison.Ordinal);
        (XhtmlDoc.NodeFromOffset(document, atH1) as IElement)?.LocalName.Should().Be("h1");
    }

    [Fact]
    public void OffsetFromNode_TextNode_ReturnsMinusOne()
    {
        IHtmlDocument document = XhtmlDoc.Parse(Sample);
        INode textNode = document.QuerySelector("p")!.FirstChild!;

        textNode.NodeType.Should().Be(NodeType.Text);
        XhtmlDoc.OffsetFromNode(textNode).Should().Be(-1);
    }

    [Theory]
    [MemberData(nameof(AllCorpusXhtml))]
    public void QueryHelpers_RunOnEveryCorpusXhtml_WithoutThrowing(string xhtmlPath)
    {
        string source = File.ReadAllText(xhtmlPath);

        IHtmlDocument document = XhtmlDoc.Parse(source);

        XhtmlDoc.GetAllDescendantIds(document).Should().NotBeNull();
        XhtmlDoc.GetIdsInFile(document).Should().NotBeNull();
        XhtmlDoc.GetAllDescendantHrefs(document).Should().NotBeNull();
        XhtmlDoc.GetLinkElements(document).Should().NotBeNull();
        XhtmlDoc.ResolveHrefs(document, "EPUB/text").Should().NotBeNull();
        XhtmlDoc.GetHeadingElements(document).Should().NotBeNull();
        XhtmlDoc.GetBodyClasses(document).Should().NotBeNull();
        XhtmlDoc.GetDominantTextDirection(document).Should().NotBeNull();
        XhtmlDoc.Serialize(document.DocumentElement!).Should().NotBeNullOrEmpty();
    }
}
