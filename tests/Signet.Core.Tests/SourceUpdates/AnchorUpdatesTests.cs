using System.Collections.Generic;
using AwesomeAssertions;
using Signet.Core.BookManipulation;
using Signet.Core.Resources;
using Signet.Core.SourceUpdates;
using Signet.Core.Toc;
using Xunit;

namespace Signet.Core.Tests.SourceUpdates;

/// <summary>
/// Tests for <see cref="AnchorUpdates"/> — recomputing <c>&lt;a href&gt;</c> and NCX entries
/// from the location of the <c>id</c> fragment after Split/Merge.
/// </summary>
public sealed class AnchorUpdatesTests
{
    private static string Xhtml(string body) =>
        "<?xml version=\"1.0\" encoding=\"utf-8\"?>\n<!DOCTYPE html>\n" +
        "<html xmlns=\"http://www.w3.org/1999/xhtml\"><head><title>T</title></head>" +
        "<body>" + body + "</body></html>\n";

    [Fact]
    public void GetIdLocations_MapsEachIdToItsResourceBookPath()
    {
        using Book book = BookCreator.CreateNewBook("2.0");
        HtmlResource a = book.CreateEmptyHtmlFile();
        a.SetText(Xhtml("<p id=\"one\">1</p>"));
        HtmlResource b = book.CreateEmptyHtmlFile();
        b.SetText(Xhtml("<p id=\"two\">2</p>"));

        Dictionary<string, string> locations = AnchorUpdates.GetIdLocations(new[] { a, b });

        locations["one"].Should().Be(a.BookPath);
        locations["two"].Should().Be(b.BookPath);
    }

    [Fact]
    public void UpdateAllAnchorsWithIds_RedirectsLinkToFileNowHoldingTheFragment()
    {
        using Book book = BookCreator.CreateNewBook("2.0");
        HtmlResource a = book.CreateEmptyHtmlFile();
        a.SetText(Xhtml("<a href=\"#target\">go</a>"));
        HtmlResource b = book.CreateEmptyHtmlFile();
        b.SetText(Xhtml("<p id=\"target\">Target</p>"));

        AnchorUpdates.UpdateAllAnchorsWithIds(new[] { a, b });

        a.GetText().Should().Contain($"href=\"{b.Filename}#target\"");
    }

    [Fact]
    public void UpdateAllAnchorsWithIds_LeavesLocalFragmentAlone()
    {
        using Book book = BookCreator.CreateNewBook("2.0");
        HtmlResource a = book.CreateEmptyHtmlFile();
        a.SetText(Xhtml("<p id=\"local\">x</p><a href=\"#local\">go</a>"));
        HtmlResource b = book.CreateEmptyHtmlFile();
        b.SetText(Xhtml("<p>other</p>"));

        AnchorUpdates.UpdateAllAnchorsWithIds(new[] { a, b });

        a.GetText().Should().Contain("href=\"#local\"");
    }

    [Fact]
    public void UpdateExternalAnchors_RedirectsReferenceToOriginatingFileIntoNewFile()
    {
        using Book book = BookCreator.CreateNewBook("2.0");
        HtmlResource originating = book.CreateEmptyHtmlFile();
        string originatingBookPath = originating.BookPath;
        HtmlResource other = book.CreateEmptyHtmlFile();
        other.SetText(Xhtml($"<a href=\"{originating.Filename}#target\">go</a>"));
        HtmlResource newFile = book.CreateEmptyHtmlFile();
        newFile.SetText(Xhtml("<p id=\"target\">Target</p>"));

        AnchorUpdates.UpdateExternalAnchors(new[] { other }, originatingBookPath, new[] { newFile });

        other.GetText().Should().Contain($"href=\"{newFile.Filename}#target\"");
    }

    [Fact]
    public void UpdateAllAnchors_RedirectsFragmentReferenceToMergedFileIntoSink()
    {
        using Book book = BookCreator.CreateNewBook("2.0");
        HtmlResource mergedA = book.CreateEmptyHtmlFile();
        string mergedABookPath = mergedA.BookPath;
        HtmlResource sink = book.CreateEmptyHtmlFile();
        HtmlResource other = book.CreateEmptyHtmlFile();
        other.SetText(Xhtml($"<a href=\"{mergedA.Filename}#chapter\">go</a>"));

        AnchorUpdates.UpdateAllAnchors(
            new[] { other }, new[] { mergedABookPath }, sink, new Dictionary<string, string>());

        other.GetText().Should().Contain($"href=\"{sink.Filename}#chapter\"");
    }

    [Fact]
    public void UpdateAllAnchors_NoFragment_UsesInjectedSectionAnchor()
    {
        using Book book = BookCreator.CreateNewBook("2.0");
        HtmlResource mergedA = book.CreateEmptyHtmlFile();
        string mergedABookPath = mergedA.BookPath;
        HtmlResource sink = book.CreateEmptyHtmlFile();
        HtmlResource other = book.CreateEmptyHtmlFile();
        other.SetText(Xhtml($"<a href=\"{mergedA.Filename}\">go</a>"));
        Dictionary<string, string> sectionIdMap = new() { [mergedABookPath] = "section1" };

        AnchorUpdates.UpdateAllAnchors(new[] { other }, new[] { mergedABookPath }, sink, sectionIdMap);

        other.GetText().Should().Contain($"href=\"{sink.Filename}#section1\"");
    }

    [Fact]
    public void UpdateTocEntries_RedirectsNcxContentSrcAfterSplit()
    {
        using Book book = BookCreator.CreateNewBook("2.0");
        NcxResource ncx = book.GetNcx()!;
        HtmlResource originating = book.CreateEmptyHtmlFile();
        string originatingBookPath = originating.BookPath;
        HtmlResource newFile = book.CreateEmptyHtmlFile();
        newFile.SetText(Xhtml("<p id=\"chapter2\">2</p>"));

        NcxDocument document = ncx.GetNcxDocument();
        document.NavMap.Clear();
        document.NavMap.Add(new NcxNavPoint
        {
            Id = "navpoint-1",
            Label = "Chapter 2",
            ContentSrc = BookPath.Relative(ncx.BookPath, originatingBookPath) + "#chapter2",
        });
        ncx.SetNcxDocument(document);

        AnchorUpdates.UpdateTocEntries(ncx, originatingBookPath, new[] { newFile });

        ncx.GetNcxDocument().NavMap[0].ContentSrc.Should().Be(
            BookPath.Relative(ncx.BookPath, newFile.BookPath) + "#chapter2");
    }

    [Fact]
    public void UpdateTocEntriesAfterMerge_RedirectsNcxContentSrcToSink()
    {
        using Book book = BookCreator.CreateNewBook("2.0");
        NcxResource ncx = book.GetNcx()!;
        HtmlResource mergedA = book.CreateEmptyHtmlFile();
        string mergedABookPath = mergedA.BookPath;
        HtmlResource sink = book.CreateEmptyHtmlFile();

        NcxDocument document = ncx.GetNcxDocument();
        document.NavMap.Clear();
        document.NavMap.Add(new NcxNavPoint
        {
            Id = "navpoint-1",
            Label = "Chapter",
            ContentSrc = BookPath.Relative(ncx.BookPath, mergedABookPath),
        });
        ncx.SetNcxDocument(document);

        AnchorUpdates.UpdateTocEntriesAfterMerge(ncx, sink.BookPath, new[] { mergedABookPath });

        ncx.GetNcxDocument().NavMap[0].ContentSrc.Should().Be(BookPath.Relative(ncx.BookPath, sink.BookPath));
    }
}
