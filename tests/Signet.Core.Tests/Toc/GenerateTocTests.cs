using System.Linq;
using AwesomeAssertions;
using Signet.Core.BookManipulation;
using Signet.Core.MainUI;
using Signet.Core.Resources;
using Signet.Core.Tests.TestSupport;
using Signet.Core.Toc;
using Xunit;

namespace Signet.Core.Tests.Toc;

/// <summary>
/// Tests of generating a table of contents from headings: <see cref="HeadingSelectorModel"/> +
/// <see cref="TocGenerator"/> → the <c>nav[epub:type=toc]</c> section (EPUB 3) / the NCX <c>navMap</c> (EPUB 2).
/// </summary>
public sealed class GenerateTocTests
{
    private const string MultiHeadingBody =
        "<body>\n" +
        "  <h1>Alpha</h1>\n" +
        "  <p>x</p>\n" +
        "  <h2>Alpha One</h2>\n" +
        "  <p>x</p>\n" +
        "  <h2 id=\"existing\">Alpha Two</h2>\n" +
        "  <p>x</p>\n" +
        "  <h1 class=\"signet_not_in_toc\">Skip Me</h1>\n" +
        "  <h1>Beta</h1>\n" +
        "</body>";

    private static Book Load(TempDir temp, string corpus) =>
        new ImportEpub(EpubBuilder.BuildInto(corpus, temp)).GetBook();

    private static HtmlResource Chapter(Book book) =>
        book.GetHtmlResources().First(h => h.Filename == "chapter1.xhtml");

    private static void SetChapterBody(Book book, string body)
    {
        HtmlResource chapter = Chapter(book);
        string text = chapter.GetText();
        int start = text.IndexOf("<body", System.StringComparison.Ordinal);
        int end = text.IndexOf("</body>", System.StringComparison.Ordinal) + "</body>".Length;
        chapter.SetText(text[..start] + body + text[end..]);
    }

    [Fact]
    public void Epub3_nav_toc_reflects_heading_hierarchy_and_fragments()
    {
        using TempDir temp = new();
        using Book book = Load(temp, CorpusPaths.Epub3Minimal);
        SetChapterBody(book, MultiHeadingBody);

        new HeadingSelectorModel(book).Apply();
        TocGenerator.GenerateToc(book);

        var tree = new NavProcessor(book.GetNavResource()!).GetTocTree();

        tree.Select(e => e.Title).Should().Equal("Alpha", "Beta");
        tree[0].Children.Select(e => e.Title).Should().Equal("Alpha One", "Alpha Two");
        tree[0].Href.Should().Be("text/chapter1.xhtml");
        tree[0].Children[0].Href.Should().Be("text/chapter1.xhtml#signet_toc_id_1");
        tree[0].Children[1].Href.Should().Be("text/chapter1.xhtml#existing");
        tree[1].Href.Should().Be("text/chapter1.xhtml#signet_toc_id_2");
    }

    [Fact]
    public void Heading_marked_not_in_toc_is_omitted()
    {
        using TempDir temp = new();
        using Book book = Load(temp, CorpusPaths.Epub3Minimal);
        SetChapterBody(book, MultiHeadingBody);

        new HeadingSelectorModel(book).Apply();
        TocGenerator.GenerateToc(book);

        var flat = new NavProcessor(book.GetNavResource()!).GetToc();
        flat.Select(e => e.Title).Should().NotContain("Skip Me");
    }

    [Fact]
    public void Regenerating_an_unchanged_toc_reports_no_change()
    {
        using TempDir temp = new();
        using Book book = Load(temp, CorpusPaths.Epub3Minimal);
        SetChapterBody(book, MultiHeadingBody);
        new HeadingSelectorModel(book).Apply();

        TocGenerator.GenerateToc(book).Should().BeTrue();
        TocGenerator.GenerateToc(book).Should().BeFalse();
    }

    [Fact]
    public void Existing_non_generated_id_is_preserved()
    {
        using TempDir temp = new();
        using Book book = Load(temp, CorpusPaths.Epub3Minimal);
        SetChapterBody(book, MultiHeadingBody);

        new HeadingSelectorModel(book).Apply();

        string source = Chapter(book).GetText();
        source.Should().Contain("id=\"existing\"");
        source.Should().Contain("id=\"signet_toc_id_1\"");
    }

    [Fact]
    public void Epub2_ncx_navmap_is_built_from_headings_with_sequential_play_order()
    {
        using TempDir temp = new();
        using Book book = Load(temp, CorpusPaths.Epub2Minimal);
        SetChapterBody(book, MultiHeadingBody);

        new HeadingSelectorModel(book).Apply();
        TocGenerator.GenerateToc(book);

        NcxDocument ncx = book.GetNcx()!.GetNcxDocument();
        ncx.NavMap.Select(p => p.Label).Should().Equal("Alpha", "Beta");
        ncx.NavMap[0].Children.Select(p => p.Label).Should().Equal("Alpha One", "Alpha Two");
        ncx.NavMap[0].ContentSrc.Should().Be("Text/chapter1.xhtml");
        ncx.NavMap[0].Children[0].ContentSrc.Should().Be("Text/chapter1.xhtml#signet_toc_id_1");
        ncx.NavMap[0].PlayOrder.Should().Be(1);
        ncx.NavMap[0].Children[0].PlayOrder.Should().Be(2);
        ncx.NavMap[1].PlayOrder.Should().Be(4);
        ncx.DtbUid.Should().NotBeEmpty();
        ncx.Depth.Should().Be(2);
    }

    [Fact]
    public void Ncx_without_headings_gets_a_single_fallback_navpoint()
    {
        using TempDir temp = new();
        using Book book = Load(temp, CorpusPaths.Epub2Minimal);
        SetChapterBody(book, "<body>\n  <p>no headings here</p>\n</body>");

        TocGenerator.GenerateToc(book);

        NcxDocument ncx = book.GetNcx()!.GetNcxDocument();
        ncx.NavMap.Should().ContainSingle();
        ncx.NavMap[0].Label.Should().Be("Start");
    }

    [Fact]
    public void At_file_start_heading_links_to_the_file_without_a_fragment()
    {
        using TempDir temp = new();
        using Book book = Load(temp, CorpusPaths.Epub3Minimal);
        SetChapterBody(book, "<body>\n  <h1 id=\"signet_toc_id_9\">Only</h1>\n  <p>x</p>\n</body>");

        new HeadingSelectorModel(book).Apply();
        TocGenerator.GenerateToc(book);

        var tree = new NavProcessor(book.GetNavResource()!).GetTocTree();
        tree.Should().ContainSingle();
        tree[0].Href.Should().Be("text/chapter1.xhtml");
        // The auto-generated id disappears when the heading does not need a fragment.
        Chapter(book).GetText().Should().NotContain("signet_toc_id_9");
    }
}
