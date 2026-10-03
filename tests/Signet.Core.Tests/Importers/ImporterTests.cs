using System.IO;
using System.Linq;
using AwesomeAssertions;
using Signet.Core.BookManipulation;
using Signet.Core.Importers;
using Signet.Core.Tests.TestSupport;
using Xunit;

namespace Signet.Core.Tests.Importers;

/// <summary>Tests of <see cref="ImporterFactory"/>, <see cref="ImportTxt"/> and <see cref="ImportHtml"/>.</summary>
public sealed class ImporterTests
{
    [Theory]
    [InlineData("book.epub", typeof(ImportEpub))]
    [InlineData("page.xhtml", typeof(ImportHtml))]
    [InlineData("page.HTML", typeof(ImportHtml))]
    [InlineData("page.htm", typeof(ImportHtml))]
    [InlineData("notes.txt", typeof(ImportTxt))]
    public void Factory_picks_the_right_importer(string name, System.Type expected)
    {
        ImporterFactory.GetImporter(name).Should().BeOfType(expected);
    }

    [Fact]
    public void Factory_returns_null_for_unsupported_extensions()
    {
        ImporterFactory.GetImporter("cover.png").Should().BeNull();
    }

    [Fact]
    public void ImportTxt_wraps_paragraphs_and_builds_a_single_section_epub()
    {
        using TempDir temp = new();
        string txt = Path.Combine(temp.Path, "a.txt");
        File.WriteAllText(txt, "Pierwszy akapit.\nDalszy ciąg.\n\nDrugi akapit.\n");

        using Book book = new ImportTxt(txt, new ImporterOptions("2.0", false)).GetBook();

        book.GetHtmlResources().Should().ContainSingle();
        string text = book.GetHtmlResources()[0].GetText();
        text.Should().Contain("<p>").And.Contain("Pierwszy akapit").And.Contain("Drugi akapit");
        book.GetNcx().Should().NotBeNull();
    }

    [Fact]
    public void ImportTxt_epub3_gets_a_nav_document()
    {
        using TempDir temp = new();
        string txt = Path.Combine(temp.Path, "a.txt");
        File.WriteAllText(txt, "Treść.\n");

        using Book book = new ImportTxt(txt, new ImporterOptions("3.0", false)).GetBook();

        book.IsEpub3.Should().BeTrue();
        book.GetOpf().GetNavResourceBookPath().Should().EndWith("nav.xhtml");
        book.GetNcx().Should().BeNull();
    }

    [Fact]
    public void ImportHtml_imports_a_page_with_its_stylesheet_and_image()
    {
        using TempDir temp = new();
        File.WriteAllText(Path.Combine(temp.Path, "style.css"), "body { color: black; }");
        File.WriteAllBytes(Path.Combine(temp.Path, "pic.png"), new byte[] { 1, 2, 3, 4 });
        string html = Path.Combine(temp.Path, "chapter.xhtml");
        File.WriteAllText(html,
            "<?xml version=\"1.0\" encoding=\"utf-8\"?>\n"
            + "<html xmlns=\"http://www.w3.org/1999/xhtml\"><head><title>Rozdział 1</title>"
            + "<link rel=\"stylesheet\" href=\"style.css\"/></head>"
            + "<body><p><img src=\"pic.png\" alt=\"\"/></p></body></html>");

        using Book book = new ImportHtml(html, new ImporterOptions("3.0", true)).GetBook();

        book.GetAllResources().Select(r => r.Filename).Should()
            .Contain("style.css").And.Contain("pic.png");
        book.GetOpf().GetPrimaryBookTitle().Should().Be("Rozdział 1");

        string text = book.GetHtmlResources()[0].GetText();
        text.Should().NotContain("\"style.css\"");
        text.Should().Contain("Styles/style.css");
    }

    [Fact]
    public void ImportHtml_reports_not_well_formed_content()
    {
        using TempDir temp = new();
        string html = Path.Combine(temp.Path, "broken.xhtml");
        File.WriteAllText(html, "<html><body><p>oops</body></html>");

        WellFormedResult? result = new ImportHtml(html, new ImporterOptions("2.0", false)).CheckValidToLoad();

        result.Should().NotBeNull();
        result!.IsWellFormed.Should().BeFalse();
    }
}
