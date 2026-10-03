using System.Collections.Generic;
using System.Linq;
using AwesomeAssertions;
using Signet.Core.BookManipulation;
using Signet.Core.MainUI;
using Signet.Core.Resources;
using Signet.Core.Tests.TestSupport;
using Signet.Core.Toc;
using Xunit;

namespace Signet.Core.Tests.Toc;

/// <summary>Tests of <see cref="TocHtmlWriter"/> and <see cref="TocGenerator.CreateHtmlToc"/>.</summary>
public sealed class TocHtmlWriterTests
{
    [Fact]
    public void Writes_nested_levels_with_relative_hrefs_and_epub3_doctype()
    {
        var entries = new List<TocHtmlWriter.Entry>
        {
            new()
            {
                Text = "Chapter 1",
                TargetBookPath = "EPUB/text/ch1.xhtml",
                Children = new[]
                {
                    new TocHtmlWriter.Entry { Text = "Part A", TargetBookPath = "EPUB/text/ch1.xhtml", Fragment = "a" },
                },
            },
        };

        string xhtml = TocHtmlWriter.WriteXml("EPUB/TOC.xhtml", "EPUB/styles/sgc-toc.css", entries, "Contents", "3.0");

        xhtml.Should().Contain("<!DOCTYPE html>\n");
        xhtml.Should().Contain("href=\"styles/sgc-toc.css\"");
        xhtml.Should().Contain("<div class=\"sgc-toc-level-1\">");
        xhtml.Should().Contain("<div class=\"sgc-toc-level-2\">");
        xhtml.Should().Contain("<a href=\"text/ch1.xhtml\">Chapter 1</a>");
        xhtml.Should().Contain("<a href=\"text/ch1.xhtml#a\">Part A</a>");
    }

    [Fact]
    public void Uses_the_xhtml11_doctype_for_epub2()
    {
        string xhtml = TocHtmlWriter.WriteXml(
            "OEBPS/TOC.xhtml", "OEBPS/Styles/sgc-toc.css", System.Array.Empty<TocHtmlWriter.Entry>(), "Contents", "2.0");

        xhtml.Should().Contain("-//W3C//DTD XHTML 1.1//EN");
    }

    [Fact]
    public void Create_html_toc_adds_a_toc_file_at_the_start_of_the_spine_with_css_and_semantic()
    {
        using TempDir temp = new();
        using Book book = new ImportEpub(EpubBuilder.BuildInto(CorpusPaths.Epub3Minimal, temp)).GetBook();

        HtmlResource chapter = book.GetHtmlResources().First(h => h.Filename == "chapter1.xhtml");
        string text = chapter.GetText();
        int start = text.IndexOf("<body", System.StringComparison.Ordinal);
        int end = text.IndexOf("</body>", System.StringComparison.Ordinal) + "</body>".Length;
        chapter.SetText(text[..start] + "<body>\n<h1>Alpha</h1>\n<h2 id=\"x\">Beta</h2>\n</body>" + text[end..]);

        new HeadingSelectorModel(book).Apply();
        TocGenerator.GenerateToc(book);

        HtmlResource tocResource = TocGenerator.CreateHtmlToc(book);

        tocResource.Filename.Should().Be("TOC.xhtml");
        book.GetHtmlResources()[0].Should().BeSameAs(tocResource);
        book.GetCssResources().Should().Contain(c => c.Filename == "sgc-toc.css");
        new NavProcessor(book.GetNavResource()!).GetLandmarkCodeForResource(tocResource).Should().Be("toc");

        string toc = tocResource.GetText();
        toc.Should().Contain("<a href=\"text/chapter1.xhtml\">Alpha</a>");
        toc.Should().Contain("<a href=\"text/chapter1.xhtml#x\">Beta</a>");

        // A repeated call overwrites the same file and does not create a second one.
        TocGenerator.CreateHtmlToc(book).Should().BeSameAs(tocResource);
    }
}
