using System.Linq;
using AwesomeAssertions;
using Signet.Core.BookManipulation;
using Signet.Core.Resources;
using Signet.Core.Toc;
using Xunit;

namespace Signet.Core.Tests.BookManipulation;

/// <summary>
/// Tests of <see cref="Book.SplitOnSectionMarkers"/>/<see cref="Book.CreateNewSections"/>
/// — Split At Markers, including the updates of references and spine/NCX entries.
/// </summary>
public sealed class SplitTests
{
    private const string Marker = "<hr class=\"signet_split_marker\"/>";

    private static string Xhtml(string body) =>
        "<?xml version=\"1.0\" encoding=\"utf-8\"?>\n<!DOCTYPE html>\n" +
        "<html xmlns=\"http://www.w3.org/1999/xhtml\"><head><title>T</title></head>" +
        "<body>" + body + "</body></html>\n";

    [Fact]
    public void SplitOnSectionMarkers_NoMarker_ReturnsEmptyAndLeavesResourceUnchanged()
    {
        using Book book = BookCreator.CreateNewBook("2.0");
        HtmlResource html = book.GetHtmlResources()[0];
        html.SetText(Xhtml("<p>whole</p>"));

        var created = book.SplitOnSectionMarkers(html);

        created.Should().BeEmpty();
        html.GetText().Should().Contain("<p>whole</p>");
    }

    [Fact]
    public void SplitOnSectionMarkers_OneMarker_CreatesOneNewSectionRightAfterOriginalInSpine()
    {
        using Book book = BookCreator.CreateNewBook("2.0");
        HtmlResource original = book.GetHtmlResources()[0];
        original.SetText(Xhtml($"<p>first</p>{Marker}<p>second</p>"));

        var created = book.SplitOnSectionMarkers(original);

        created.Should().ContainSingle();
        original.GetText().Should().Contain("<p>first</p>").And.NotContain("<p>second</p>");
        created[0].GetText().Should().Contain("<p>second</p>").And.NotContain("<p>first</p>");

        var spineOrder = book.GetOpf().GetSpineOrderBookPaths().ToList();
        int originalIndex = spineOrder.IndexOf(original.BookPath);
        int newIndex = spineOrder.IndexOf(created[0].BookPath);
        newIndex.Should().Be(originalIndex + 1);
        book.Modified.Should().BeTrue();
    }

    [Fact]
    public void SplitOnSectionMarkers_NewSectionFilename_FollowsPrefixNumberingPattern()
    {
        using Book book = BookCreator.CreateNewBook("2.0");
        HtmlResource original = book.GetHtmlResources()[0];
        string baseName = original.Filename[..original.Filename.LastIndexOf('.')];
        original.SetText(Xhtml($"<p>a</p>{Marker}<p>b</p>{Marker}<p>c</p>"));

        var created = book.SplitOnSectionMarkers(original);

        created.Should().HaveCount(2);
        created[0].Filename.Should().Be($"{baseName}_0001.xhtml");
        created[1].Filename.Should().Be($"{baseName}_0002.xhtml");
    }

    [Fact]
    public void SplitOnSectionMarkers_InternalLinkToMovedFragment_IsRedirectedToNewFile()
    {
        using Book book = BookCreator.CreateNewBook("2.0");
        HtmlResource original = book.GetHtmlResources()[0];
        original.SetText(Xhtml(
            $"<a href=\"#target\">go</a><p>first</p>{Marker}<p id=\"target\">second</p>"));

        var created = book.SplitOnSectionMarkers(original);

        original.GetText().Should().Contain($"href=\"{created[0].Filename}#target\"");
    }

    [Fact]
    public void SplitOnSectionMarkers_ExternalLinkToOriginalFragment_IsRedirectedToNewFile()
    {
        using Book book = BookCreator.CreateNewBook("2.0");
        HtmlResource original = book.GetHtmlResources()[0];
        string originalFilename = original.Filename;
        original.SetText(Xhtml($"<p>first</p>{Marker}<p id=\"target\">second</p>"));

        HtmlResource other = book.CreateEmptyHtmlFile();
        other.SetText(Xhtml($"<a href=\"{originalFilename}#target\">go</a>"));

        var created = book.SplitOnSectionMarkers(original);

        other.GetText().Should().Contain($"href=\"{created[0].Filename}#target\"");
    }

    [Fact]
    public void SplitOnSectionMarkers_NcxEntryToMovedFragment_IsRedirectedToNewFile()
    {
        using Book book = BookCreator.CreateNewBook("2.0");
        HtmlResource original = book.GetHtmlResources()[0];
        original.SetText(Xhtml($"<p>first</p>{Marker}<p id=\"chapter2\">second</p>"));
        NcxResource ncx = book.GetNcx()!;
        NcxDocument document = ncx.GetNcxDocument();
        document.NavMap.Clear();
        document.NavMap.Add(new NcxNavPoint
        {
            Id = "navpoint-1",
            Label = "Chapter 2",
            ContentSrc = Signet.Core.BookPath.Relative(ncx.BookPath, original.BookPath) + "#chapter2",
        });
        ncx.SetNcxDocument(document);

        var created = book.SplitOnSectionMarkers(original);

        string expected = Signet.Core.BookPath.Relative(ncx.BookPath, created[0].BookPath) + "#chapter2";
        ncx.GetNcxDocument().NavMap[0].ContentSrc.Should().Be(expected);
    }
}
