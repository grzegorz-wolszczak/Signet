using System;
using System.IO;
using System.Linq;
using AwesomeAssertions;
using Signet.Core.BookManipulation;
using Signet.Core.Resources;
using Signet.Core.Tests.TestSupport;
using Signet.Core.Toc;
using Xunit;

namespace Signet.Core.Tests.BookManipulation;

/// <summary>
/// The accessibility of what Signet generates (Calibre 627717dfe4, eee070af52, 91b5b81209, 48863bb103): the roles of the nav, and the
/// language and the title of new pages, cover pages and the HTML table of contents.
/// </summary>
public sealed class GeneratedAccessibilityTests
{
    private const string OnePixelPngBase64 =
        "iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAQAAAC1HAwCAAAAC0lEQVR42mNk+A8AAQUBAScY42YAAAAASUVORK5CYII=";

    private static Book NewBook(string version, params string[] languages)
    {
        Book book = BookCreator.CreateNewBook(version);
        if (languages.Length > 0)
        {
            AccessibilityLanguage.SetLanguages(book, languages);
        }

        return book;
    }

    private static ImageResource AddImage(Book book, TempDir temp)
    {
        string path = temp.Combine("cover.png");
        File.WriteAllBytes(path, Convert.FromBase64String(OnePixelPngBase64));
        return (ImageResource)book.AddExistingFiles([path]).Single();
    }

    // --- the nav ---

    [Fact]
    public void The_nav_of_a_new_epub3_book_has_the_roles_of_its_sections_and_a_regenerated_one_keeps_them()
    {
        // Arrange
        using Book book = NewBook("3.0");
        HtmlResource nav = book.GetNavResource()!;

        // Assert
        nav.GetText().Should().Contain("epub:type=\"toc\" id=\"toc\" role=\"doc-toc\"");

        // Act
        NavProcessor processor = new(nav);
        HtmlResource page = book.GetHtmlResourcesExcludingNav()[0];
        processor.SetToc([new NavTocEntry { Level = 1, Title = "One", Href = page.Filename }]);
        processor.SetPageList([new NavPageListEntry { PageName = "1", Href = page.Filename }]);
        processor.SetLandmarks([new NavLandmarkEntry { EpubType = "bodymatter", Title = "Start", Href = page.Filename }]);

        // Assert
        string text = nav.GetText();
        text.Should().Contain("role=\"doc-toc\"").And.Contain("role=\"doc-pagelist\"");
        text.Should().NotContain("epub:type=\"landmarks\" id=\"landmarks\" role=", "the landmarks nav has no ARIA role of its own");
    }

    // --- the language of the files Signet creates ---

    [Fact]
    public void A_new_file_gets_the_main_language_of_the_book()
    {
        // Arrange
        using Book epub3 = NewBook("3.0", "pl", "en");
        using Book epub2 = NewBook("2.0", "pl");

        // Act
        string text3 = epub3.CreateEmptyHtmlFile().GetText();
        string text2 = epub2.CreateEmptyHtmlFile().GetText();

        // Assert
        text3.Should().Contain("lang=\"pl\"").And.Contain("xml:lang=\"pl\"");
        text2.Should().Contain("xml:lang=\"pl\"").And.NotContain(" lang=\"pl\"");
    }

    [Fact]
    public void A_new_file_of_a_book_without_a_language_claims_none()
    {
        // Arrange
        using Book book = NewBook("3.0");
        book.GetOpf().SetLanguages([]);

        // Act
        string text = book.CreateEmptyHtmlFile().GetText();

        // Assert
        text.Should().NotContain("lang=");
    }

    [Fact]
    public void A_file_split_from_another_keeps_the_language_it_had()
    {
        // Arrange
        using Book book = NewBook("3.0", "pl");
        HtmlResource original = book.GetHtmlResourcesExcludingNav()[0];
        original.SetText(
            "<?xml version=\"1.0\" encoding=\"utf-8\"?>\n<!DOCTYPE html>\n<html xmlns=\"http://www.w3.org/1999/xhtml\" lang=\"de\" xml:lang=\"de\">\n"
            + "<head>\n<title>t</title>\n</head>\n<body>\n<p>one</p>\n<p>two</p>\n</body>\n</html>");
        int caret = original.GetText().IndexOf("<p>two", StringComparison.Ordinal);

        // Act
        HtmlResource created = book.SplitAtPosition(original, caret)!;

        // Assert
        created.GetText().Should().Contain("lang=\"de\"");
    }

    // --- the HTML table of contents ---

    [Fact]
    public void The_html_toc_has_the_language_of_the_book()
    {
        // Arrange
        using Book epub3 = NewBook("3.0", "pl");
        using Book epub2 = NewBook("2.0", "pl");

        // Act
        string text3 = TocGenerator.CreateHtmlToc(epub3).GetText();
        string text2 = TocGenerator.CreateHtmlToc(epub2).GetText();

        // Assert
        text3.Should().Contain("lang=\"pl\"").And.Contain("xml:lang=\"pl\"");
        text2.Should().Contain("xml:lang=\"pl\"").And.NotContain(" lang=\"pl\"");
    }

    // --- cover pages ---

    [Theory]
    [InlineData("3.0")]
    [InlineData("2.0")]
    public void A_cover_page_has_the_language_of_the_book_the_title_as_its_own_and_as_the_text_alternative_of_the_cover_image(string version)
    {
        // Arrange
        using TempDir temp = new();
        using Book book = NewBook(version, "pl");
        book.GetOpf().SetPrimaryBookTitle("Tom & Jerry <100%>");
        ImageResource image = AddImage(book, temp);

        // Act
        HtmlResource cover = book.SetCoverImage(image);

        // Assert
        string text = cover.GetText();
        text.Should().Contain("xml:lang=\"pl\"").And.Contain("<title>Tom &amp; Jerry &lt;100%&gt;</title>");
        text.Should().Contain("<svg").And.Contain("</title>").And.NotContain("<title>Cover</title>");
        text.Split("<title>", StringSplitOptions.None).Length.Should().Be(3, "the page and the svg image each have a title");
        AccessibilityValidator.Validate(book).Should().NotContain(r => r.Code == "Validation_Accessibility_SvgNoAlt" && r.BookPath == cover.BookPath);
        AccessibilityValidator.Validate(book).Should().NotContain(r => r.Code == "Validation_Accessibility_NoLanguage" && r.BookPath == cover.BookPath);
    }

    [Fact]
    public void A_cover_page_of_a_book_without_a_title_or_a_language_says_cover_and_claims_no_language()
    {
        // Arrange
        using TempDir temp = new();
        using Book book = NewBook("3.0");
        book.GetOpf().SetPrimaryBookTitle(" ");
        book.GetOpf().SetLanguages([]);
        ImageResource image = AddImage(book, temp);

        // Act
        string text = book.SetCoverImage(image).GetText();

        // Assert
        text.Should().Contain("<title>Cover</title>").And.NotContain("lang=");
        text.Split("<title>", StringSplitOptions.None).Length.Should().Be(3);
    }
}
