using AwesomeAssertions;
using Signet.Core.BookManipulation;
using Signet.Core.Resources;
using Signet.Core.Toc;
using Xunit;

namespace Signet.Core.Tests.BookManipulation;

/// <summary>
/// An EPUB 3 book can have both the landmarks of its nav and the legacy OPF guide: the landmarks win, the guide only fills in where there
/// is no landmark of that kind (Calibre 251c1726e7: "Prefer EPUB3 landmarks over legacy guide").
/// </summary>
public sealed class LandmarksOverGuideTests
{
    private static (Book Book, HtmlResource First, HtmlResource Second) NewBook(string version = "3.0")
    {
        Book book = BookCreator.CreateNewBook(version);
        HtmlResource first = book.GetHtmlResourcesExcludingNav()[0];
        HtmlResource second = book.CreateEmptyHtmlFile();
        return (book, first, second);
    }

    private static void Landmark(Book book, HtmlResource html, string code) =>
        new NavProcessor(book.GetNavResource()!).AddLandmarkCode(html, code, toggle: false);

    [Fact]
    public void The_cover_of_the_landmarks_is_found_though_the_guide_names_another_page_first_in_the_book()
    {
        // Arrange
        (Book book, HtmlResource first, HtmlResource second) = NewBook();
        using (book)
        {
            book.GetOpf().AddGuideSemanticCode(first, "cover", toggle: false);
            Landmark(book, second, "cover");

            // Act
            HtmlResource? cover = book.FindExistingCoverHtmlResource();

            // Assert
            cover.Should().BeSameAs(second, "the landmarks win over the guide");
        }
    }

    [Fact]
    public void The_guide_names_the_cover_when_the_landmarks_have_none()
    {
        // Arrange
        (Book book, HtmlResource first, HtmlResource second) = NewBook();
        using (book)
        {
            Landmark(book, second, "bodymatter");
            book.GetOpf().AddGuideSemanticCode(first, "cover", toggle: false);

            // Act
            HtmlResource? cover = book.FindExistingCoverHtmlResource();

            // Assert
            cover.Should().BeSameAs(first, "the guide fills in where there is no landmark of that kind");
        }
    }

    [Fact]
    public void An_epub2_book_uses_its_guide()
    {
        // Arrange
        (Book book, _, HtmlResource second) = NewBook("2.0");
        using (book)
        {
            book.GetOpf().AddGuideSemanticCode(second, "cover", toggle: false);

            // Act & Assert
            book.FindExistingCoverHtmlResource().Should().BeSameAs(second);
        }
    }

    [Fact]
    public void The_html_toc_is_written_to_the_page_of_the_toc_landmark_not_the_one_of_the_guide()
    {
        // Arrange
        (Book book, HtmlResource first, HtmlResource second) = NewBook();
        using (book)
        {
            book.GetOpf().AddGuideSemanticCode(first, "toc", toggle: false);
            Landmark(book, second, "toc");

            // Act
            HtmlResource toc = TocGenerator.CreateHtmlToc(book);

            // Assert
            toc.Should().BeSameAs(second);
        }
    }

    [Fact]
    public void The_html_toc_goes_to_the_page_the_guide_names_when_the_landmarks_name_none()
    {
        // Arrange
        (Book book, HtmlResource first, HtmlResource second) = NewBook();
        using (book)
        {
            Landmark(book, second, "bodymatter");
            book.GetOpf().AddGuideSemanticCode(first, "toc", toggle: false);

            // Act
            HtmlResource toc = TocGenerator.CreateHtmlToc(book);

            // Assert
            toc.Should().BeSameAs(first);
        }
    }
}
