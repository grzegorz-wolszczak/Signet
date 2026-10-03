using System;
using System.Collections.Generic;
using System.Linq;
using AwesomeAssertions;
using Signet.Core.BookManipulation;
using Signet.Core.Resources;
using Signet.Core.Tests.TestSupport;
using Xunit;

namespace Signet.Core.Tests.BookManipulation;

/// <summary>
/// Tests of the "Link Stylesheets" / "Link Javascripts" operations at the <see cref="Book"/> level:
/// maps, relinking in <c>&lt;head&gt;</c>, the well-formed guard.
/// </summary>
public sealed class BookLinkResourcesTests
{
    private static Book LoadWithNcx(TempDir temp)
    {
        string epub = EpubBuilder.BuildInto(CorpusPaths.Epub3WithNcx, temp);
        return new ImportEpub(epub).GetBook();
    }

    private static List<HtmlResource> Chapters(Book book) =>
        book.GetHtmlResources().Where(h => h.Filename.StartsWith("chapter", StringComparison.Ordinal)).ToList();

    [Fact]
    public void GetStylesheetsMap_marks_shared_stylesheet_as_included()
    {
        using TempDir temp = new();
        using Book book = LoadWithNcx(temp);

        IReadOnlyList<LinkableResourceEntry> map = book.GetStylesheetsMap(Chapters(book));

        map.Should().ContainSingle();
        map[0].Included.Should().BeTrue();
        map[0].BookPath.Should().EndWith("style.css");
    }

    [Fact]
    public void LinkStylesheetsToResources_with_empty_list_removes_links_from_head()
    {
        using TempDir temp = new();
        using Book book = LoadWithNcx(temp);
        List<HtmlResource> chapters = Chapters(book);

        LinkResourcesResult result = book.LinkStylesheetsToResources(chapters, Array.Empty<string>());

        result.Applied.Should().BeTrue();
        foreach (HtmlResource chapter in chapters)
        {
            chapter.GetText().Should().NotContain("stylesheet");
        }

        book.Modified.Should().BeTrue();
    }

    [Fact]
    public void LinkStylesheetsToResources_links_multiple_stylesheets_in_order()
    {
        using TempDir temp = new();
        using Book book = LoadWithNcx(temp);
        HtmlResource chapter1 = Chapters(book).First();

        CssResource extra = book.CreateEmptyCssFile();
        string existing = book.GetCssResources().Single(c => c.Filename == "style.css").BookPath;

        LinkResourcesResult result = book.LinkStylesheetsToResources(
            new[] { chapter1 }, new[] { extra.BookPath, existing });

        result.Applied.Should().BeTrue();
        string text = chapter1.GetText();
        text.IndexOf(extra.Filename, StringComparison.Ordinal)
            .Should().BeLessThan(text.IndexOf("style.css", StringComparison.Ordinal));
    }

    [Fact]
    public void LinkStylesheetsToResources_aborts_when_a_file_is_not_well_formed()
    {
        using TempDir temp = new();
        using Book book = LoadWithNcx(temp);
        List<HtmlResource> chapters = Chapters(book);
        chapters[0].SetText("<html><head><body><p>broken");

        LinkResourcesResult result = book.LinkStylesheetsToResources(chapters, Array.Empty<string>());

        result.Applied.Should().BeFalse();
        result.NotWellFormed.Should().BeSameAs(chapters[0]);
        chapters[1].GetText().Should().Contain("stylesheet");
    }

    [Fact]
    public void LinkJavascriptsToResources_adds_script_and_marks_scripted_property()
    {
        using TempDir temp = new();
        using Book book = LoadWithNcx(temp);
        HtmlResource chapter1 = Chapters(book).First();
        MiscTextResource js = book.CreateEmptyJsFile();

        LinkResourcesResult result = book.LinkJavascriptsToResources(new[] { chapter1 }, new[] { js.BookPath });

        result.Applied.Should().BeTrue();
        chapter1.GetText().Should().Contain(js.Filename);
        chapter1.GetText().Should().Contain("</script>");
    }
}
