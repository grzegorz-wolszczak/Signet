using System.Linq;
using AwesomeAssertions;
using Signet.Core.BookManipulation;
using Signet.Core.Resources;
using Signet.Core.Toc;
using Xunit;

namespace Signet.Core.Tests.BookManipulation;

/// <summary>Tests of <see cref="Book.MergeResources"/> — merging XHTML files into the first one on the list.</summary>
public sealed class MergeTests
{
    private static string Xhtml(string body) =>
        "<?xml version=\"1.0\" encoding=\"utf-8\"?>\n<!DOCTYPE html>\n" +
        "<html xmlns=\"http://www.w3.org/1999/xhtml\"><head><title>T</title></head>" +
        "<body>" + body + "</body></html>\n";

    [Fact]
    public void MergeResources_FewerThanTwo_IsNoOp()
    {
        using Book book = BookCreator.CreateNewBook("2.0");
        HtmlResource only = book.GetHtmlResources()[0];

        HtmlResource? result = book.MergeResources(new[] { only });

        result.Should().BeNull();
        book.Modified.Should().BeFalse();
    }

    [Fact]
    public void MergeResources_CombinesBodiesIntoSinkAndRemovesOthers()
    {
        using Book book = BookCreator.CreateNewBook("2.0");
        HtmlResource sink = book.GetHtmlResources()[0];
        sink.SetText(Xhtml("<p>first</p>"));
        HtmlResource second = book.CreateEmptyHtmlFile();
        second.SetText(Xhtml("<p>second</p>"));
        string secondBookPath = second.BookPath;

        HtmlResource? result = book.MergeResources(new[] { sink, second });

        result.Should().BeNull();
        sink.GetText().Should().Contain("<p>first</p>").And.Contain("<p>second</p>");
        book.GetAllResources().Should().NotContain(r => r.BookPath == secondBookPath);
        book.Modified.Should().BeTrue();
    }

    [Fact]
    public void MergeResources_InjectsSectionAnchorBeforeNonSinkBody()
    {
        using Book book = BookCreator.CreateNewBook("3.0");
        HtmlResource sink = book.GetHtmlResources()[0];
        sink.SetText(Xhtml("<p>first</p>"));
        HtmlResource second = book.CreateEmptyHtmlFile();
        second.SetText(Xhtml("<p>second</p>"));

        book.MergeResources(new[] { sink, second });

        sink.GetText().Should().MatchRegex("<a id=\"section_\\d+\"></a>");
    }

    [Fact]
    public void MergeResources_Epub2_UsesHiddenParagraphAnchor()
    {
        using Book book = BookCreator.CreateNewBook("2.0");
        HtmlResource sink = book.GetHtmlResources()[0];
        sink.SetText(Xhtml("<p>first</p>"));
        HtmlResource second = book.CreateEmptyHtmlFile();
        second.SetText(Xhtml("<p>second</p>"));

        book.MergeResources(new[] { sink, second });

        sink.GetText().Should().Contain("style=\"display:none\"");
    }

    [Fact]
    public void MergeResources_InternalCrossReferenceBecomesLocalAnchor()
    {
        using Book book = BookCreator.CreateNewBook("2.0");
        HtmlResource sink = book.GetHtmlResources()[0];
        sink.SetText(Xhtml("<p>first</p>"));
        HtmlResource second = book.CreateEmptyHtmlFile();
        second.SetText(Xhtml("<p id=\"target\">second</p>"));
        sink.SetText(Xhtml($"<a href=\"{second.Filename}#target\">go</a><p>first</p>"));

        book.MergeResources(new[] { sink, second });

        sink.GetText().Should().Contain("href=\"#target\"");
    }

    [Fact]
    public void MergeResources_ExternalReferenceWithFragment_RedirectsToSink()
    {
        using Book book = BookCreator.CreateNewBook("2.0");
        HtmlResource sink = book.GetHtmlResources()[0];
        sink.SetText(Xhtml("<p>first</p>"));
        HtmlResource second = book.CreateEmptyHtmlFile();
        second.SetText(Xhtml("<p id=\"target\">second</p>"));
        string secondFilename = second.Filename;

        HtmlResource other = book.CreateEmptyHtmlFile();
        other.SetText(Xhtml($"<a href=\"{secondFilename}#target\">go</a>"));

        book.MergeResources(new[] { sink, second });

        other.GetText().Should().Contain($"href=\"{sink.Filename}#target\"");
    }

    [Fact]
    public void MergeResources_ExternalReferenceWithoutFragment_RedirectsToInjectedSectionAnchor()
    {
        using Book book = BookCreator.CreateNewBook("2.0");
        HtmlResource sink = book.GetHtmlResources()[0];
        sink.SetText(Xhtml("<p>first</p>"));
        HtmlResource second = book.CreateEmptyHtmlFile();
        second.SetText(Xhtml("<p>second</p>"));
        string secondFilename = second.Filename;

        HtmlResource other = book.CreateEmptyHtmlFile();
        other.SetText(Xhtml($"<a href=\"{secondFilename}\">go</a>"));

        book.MergeResources(new[] { sink, second });

        other.GetText().Should().MatchRegex($"href=\"{System.Text.RegularExpressions.Regex.Escape(sink.Filename)}#section_\\d+\"");
    }

    [Fact]
    public void MergeResources_NcxEntry_RedirectsToSink()
    {
        using Book book = BookCreator.CreateNewBook("2.0");
        HtmlResource sink = book.GetHtmlResources()[0];
        sink.SetText(Xhtml("<p>first</p>"));
        HtmlResource second = book.CreateEmptyHtmlFile();
        second.SetText(Xhtml("<p>second</p>"));

        NcxResource ncx = book.GetNcx()!;
        NcxDocument document = ncx.GetNcxDocument();
        document.NavMap.Clear();
        document.NavMap.Add(new NcxNavPoint
        {
            Id = "navpoint-1",
            Label = "Chapter",
            ContentSrc = Signet.Core.BookPath.Relative(ncx.BookPath, second.BookPath),
        });
        ncx.SetNcxDocument(document);

        book.MergeResources(new[] { sink, second });

        ncx.GetNcxDocument().NavMap[0].ContentSrc.Should().Be(
            Signet.Core.BookPath.Relative(ncx.BookPath, sink.BookPath));
    }

    [Fact]
    public void MergeResources_Epub2GuideEntry_RedirectsToSink()
    {
        using Book book = BookCreator.CreateNewBook("2.0");
        HtmlResource sink = book.GetHtmlResources()[0];
        sink.SetText(Xhtml("<p>first</p>"));
        HtmlResource second = book.CreateEmptyHtmlFile();
        second.SetText(Xhtml("<p>second</p>"));
        book.GetOpf().AddGuideSemanticCode(second, "text");

        book.MergeResources(new[] { sink, second });

        book.GetOpf().GetAllGuideInfoByBookPath().Should().ContainSingle(
            info => info.BookPath == sink.BookPath);
    }

    [Fact]
    public void MergeResources_RemovesMergedFileFromSpine()
    {
        using Book book = BookCreator.CreateNewBook("2.0");
        HtmlResource sink = book.GetHtmlResources()[0];
        sink.SetText(Xhtml("<p>first</p>"));
        HtmlResource second = book.CreateEmptyHtmlFile();
        second.SetText(Xhtml("<p>second</p>"));
        string secondBookPath = second.BookPath;

        book.MergeResources(new[] { sink, second });

        book.GetOpf().GetSpineOrderBookPaths().Should().NotContain(secondBookPath);
    }

    [Fact]
    public void MergeResources_Epub3NavAmongSelection_IsRejectedWithoutChanges()
    {
        using Book book = BookCreator.CreateNewBook("3.0");
        HtmlResource sink = book.GetHtmlResources()[0];
        string navBookPath = book.GetOpf().GetNavResourceBookPath();
        HtmlResource nav = (HtmlResource)book.GetAllResources().Single(r => r.BookPath == navBookPath);

        HtmlResource? result = book.MergeResources(new[] { sink, nav });

        result.Should().Be(nav);
        book.Modified.Should().BeFalse();
        book.GetAllResources().Should().Contain(r => r.BookPath == navBookPath);
    }
}
