using System.Linq;
using AwesomeAssertions;
using Signet.Core.BookManipulation;
using Signet.Core.Resources;
using Xunit;

namespace Signet.Core.Tests.BookManipulation;

/// <summary>Tests for <see cref="Book.SetNavDocument"/> (Book Browser → "Mark As Table Of Contents (NAV)").</summary>
public sealed class BookNavDocumentTests
{
    private static int NavItems(Book book) =>
        book.GetOpf().GetOpfDocument().Manifest.Count(m => m.Attributes.Value("properties").Split(' ').Contains("nav"));

    [Fact]
    public void The_nav_property_moves_to_the_chosen_file()
    {
        // Arrange
        using Book book = BookCreator.CreateNewBook("3.0");
        HtmlResource oldNav = book.GetNavResource()!;
        HtmlResource chapter = book.GetHtmlResourcesExcludingNav().First();
        book.Modified = false;

        // Act
        book.SetNavDocument(chapter).Should().BeTrue();

        // Assert
        book.GetNavResource().Should().BeSameAs(chapter);
        NavItems(book).Should().Be(1, "only one manifest item may be the navigation document");
        book.GetOpf().GetOpfDocument().Manifest.Single(m => m.Href.EndsWith(oldNav.Filename, System.StringComparison.Ordinal))
            .Attributes.Contains("properties").Should().BeFalse("an emptied properties attribute is removed");
        book.Modified.Should().BeTrue();
    }

    [Fact]
    public void Marking_the_current_nav_document_changes_nothing()
    {
        // Arrange
        using Book book = BookCreator.CreateNewBook("3.0");
        string opf = book.GetOpf().GetText();

        // Act
        book.SetNavDocument(book.GetNavResource()!).Should().BeFalse();

        // Assert
        book.GetOpf().GetText().Should().Be(opf);
    }

    [Fact]
    public void An_epub2_book_has_no_nav_document()
    {
        // Arrange
        using Book book = BookCreator.CreateNewBook("2.0");

        // Act
        book.SetNavDocument(book.GetHtmlResources()[0]).Should().BeFalse();

        // Assert
        NavItems(book).Should().Be(0);
    }
}
