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

    private const string Caret = "|";

    private static string Compact(string text) => string.Concat(text.Where(c => !char.IsWhiteSpace(c)));

    // Splits the book's first file at the position of "|" in the body (the marker itself is removed first).
    private static (Book Book, HtmlResource Original, HtmlResource? Created) SplitAtCaret(string bodyWithCaret, string? existingMarker = null)
    {
        Book book = BookCreator.CreateNewBook("2.0");
        HtmlResource original = book.GetHtmlResources()[0];
        string text = Xhtml(bodyWithCaret + (existingMarker ?? string.Empty));
        int caret = text.IndexOf(Caret, System.StringComparison.Ordinal);
        original.SetText(text.Remove(caret, 1));
        return (book, original, book.SplitAtPosition(original, caret));
    }

    [Fact]
    public void SplitAtPosition_KeepsTheTopInTheFileAndMovesTheRestToANewFileRightAfterIt()
    {
        (Book book, HtmlResource original, HtmlResource? created) = SplitAtCaret("<p>first</p>|<p>second</p>");
        using Book _ = book;

        created.Should().NotBeNull();
        original.GetText().Should().Contain("<p>first</p>").And.NotContain("second");
        created!.GetText().Should().Contain("<p>second</p>").And.NotContain("first");
        created.Filename.Should().Be(original.Filename[..original.Filename.LastIndexOf('.')] + "_0001.xhtml");

        var spine = book.GetOpf().GetSpineOrderBookPaths().ToList();
        spine.IndexOf(created.BookPath).Should().Be(spine.IndexOf(original.BookPath) + 1);
        book.Modified.Should().BeTrue();
    }

    [Fact]
    public void SplitAtPosition_InsideAnElement_ClosesItInTheTopAndReopensItInTheNewFile()
    {
        (Book book, HtmlResource original, HtmlResource? created) = SplitAtCaret("<div class=\"c\"><p>first</p>|<p>second</p></div>");
        using Book _ = book;

        Compact(original.GetText()).Should().Contain("<divclass=\"c\"><p>first</p></div></body>");
        Compact(created!.GetText()).Should().Contain("<body><divclass=\"c\"><p>second</p></div></body>");
    }

    [Fact]
    public void SplitAtPosition_LeavesOtherSplitMarkersAlone()
    {
        (Book book, HtmlResource _, HtmlResource? created) = SplitAtCaret("<p>a</p>|<p>b</p>", $"{Marker}<p>c</p>");
        using Book __ = book;

        book.GetHtmlResources().Should().HaveCount(2);
        created!.GetText().Should().Contain("signet_split_marker").And.Contain("<p>c</p>");
    }

    [Fact]
    public void SplitAtPosition_InsideATag_ReturnsNullAndLeavesTheBookUnchanged()
    {
        (Book book, HtmlResource original, HtmlResource? created) = SplitAtCaret("<p cl|ass=\"x\">first</p>");
        using Book _ = book;

        created.Should().BeNull();
        book.GetHtmlResources().Should().ContainSingle();
        original.GetText().Should().Contain("<p class=\"x\">first</p>");
    }

    [Fact]
    public void SplitAtPosition_RightBeforeATag_Splits()
    {
        (Book book, HtmlResource original, HtmlResource? created) = SplitAtCaret("<p>first</p>|<p>second</p>");
        using Book _ = book;

        created.Should().NotBeNull();
        original.GetText().Should().NotContain("second");
    }

    [Fact]
    public void SplitAtPosition_AtTheStartOfTheBody_LeavesAnEmptyParagraphInTheFile()
    {
        (Book book, HtmlResource original, HtmlResource? created) = SplitAtCaret("|<p>all</p>");
        using Book _ = book;

        original.GetText().Should().Contain("<p>&#160;</p>").And.NotContain("all");
        created!.GetText().Should().Contain("<p>all</p>");
    }

    [Fact]
    public void SplitAtPosition_ExternalLinkToAMovedFragment_IsRedirectedToTheNewFile()
    {
        using Book book = BookCreator.CreateNewBook("2.0");
        HtmlResource original = book.GetHtmlResources()[0];
        string text = Xhtml("<p>first</p><p id=\"target\">second</p>");
        original.SetText(text);
        HtmlResource other = book.CreateEmptyHtmlFile();
        other.SetText(Xhtml($"<a href=\"{original.Filename}#target\">go</a>"));

        HtmlResource? created = book.SplitAtPosition(original, text.IndexOf("<p id", System.StringComparison.Ordinal));

        other.GetText().Should().Contain($"href=\"{created!.Filename}#target\"");
    }
}
