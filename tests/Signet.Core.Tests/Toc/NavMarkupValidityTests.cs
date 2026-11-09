using System.Collections.Generic;
using System.Linq;
using AwesomeAssertions;
using Signet.Core.BookManipulation;
using Signet.Core.Resources;
using Signet.Core.Toc;
using Xunit;

namespace Signet.Core.Tests.Toc;

/// <summary>
/// The nav that Signet writes is valid for EPUBCheck whatever the entries are (Calibre 82d8dc6886: a regression produced invalid
/// navigation markup): the markup of nested lists, of an entry without text, of a first entry that is not on the top level.
/// </summary>
public sealed class NavMarkupValidityTests
{
    // The nav of a new book has empty landmarks and page-list sections, which are not valid on their own; only the toc is under test.
    private const string NavOnlyWithToc =
        "<?xml version=\"1.0\" encoding=\"UTF-8\"?>\n<!DOCTYPE html>\n"
        + "<html xmlns=\"http://www.w3.org/1999/xhtml\" xmlns:epub=\"http://www.idpf.org/2007/ops\" lang=\"en\" xml:lang=\"en\">\n"
        + "<head><title>Contents</title><meta charset=\"utf-8\"/></head>\n<body>\n</body>\n</html>\n";

    private static (Book Book, string A, string B) NewBook()
    {
        Book book = BookCreator.CreateNewBook("3.0");
        HtmlResource a = book.GetHtmlResourcesExcludingNav()[0];
        HtmlResource b = book.CreateEmptyHtmlFile();
        book.GetNavResource()!.SetText(NavOnlyWithToc);
        return (book, a.Filename, b.Filename);
    }

    private static List<ValidationResult> NavErrors(Book book) =>
        BookValidator.ValidateCurrentBook(book)
            .Where(r => r.BookPath == book.GetNavResource()!.BookPath && r.Severity >= ValidationSeverity.Error)
            .ToList();

    private static NavTocEntry Entry(int level, string title, string href) => new() { Level = level, Title = title, Href = href };

    [Fact]
    public void A_flat_and_a_nested_toc_are_valid()
    {
        // Arrange
        (Book book, string a, string b) = NewBook();
        using (book)
        {
            // Act
            new NavProcessor(book.GetNavResource()!).SetToc(
                [Entry(1, "A", a), Entry(2, "A.1", a), Entry(3, "A.1.1", a), Entry(2, "A.2", b), Entry(1, "B & <C>", b)]);

            // Assert
            NavErrors(book).Should().BeEmpty();
        }
    }

    [Fact]
    public void An_entry_without_text_gets_the_name_of_its_file_because_an_anchor_of_a_nav_must_have_text()
    {
        // Arrange
        (Book book, string a, string b) = NewBook();
        using (book)
        {
            NavProcessor processor = new(book.GetNavResource()!);

            // Act
            processor.SetToc([Entry(1, string.Empty, a), Entry(1, "   ", b)]);

            // Assert
            NavErrors(book).Should().BeEmpty();
            processor.GetToc().Select(e => e.Title).Should().Equal(System.IO.Path.GetFileNameWithoutExtension(a), System.IO.Path.GetFileNameWithoutExtension(b));
        }
    }

    [Fact]
    public void A_toc_that_starts_below_the_top_level_or_jumps_levels_is_written_as_a_valid_list_without_duplicated_entries()
    {
        // Arrange
        (Book book, string a, string b) = NewBook();
        using (book)
        {
            NavProcessor processor = new(book.GetNavResource()!);

            // Act
            processor.SetToc([Entry(2, "First", a), Entry(4, "Deep", a), Entry(1, "Top", b)]);

            // Assert
            NavErrors(book).Should().BeEmpty();
            processor.GetToc().Select(e => (e.Level, e.Title)).Should().Equal((1, "First"), (2, "Deep"), (1, "Top"));
        }
    }

    [Fact]
    public void An_empty_landmark_or_page_name_is_given_text_too()
    {
        // Arrange
        (Book book, string a, _) = NewBook();
        using (book)
        {
            NavProcessor processor = new(book.GetNavResource()!);
            processor.SetToc([Entry(1, "A", a)]);

            // Act
            processor.SetLandmarks([new NavLandmarkEntry { EpubType = "bodymatter", Title = " ", Href = a }]);
            processor.SetPageList([new NavPageListEntry { PageName = string.Empty, Href = a }]);

            // Assert
            NavErrors(book).Should().BeEmpty();
            processor.GetLandmarks().Single().Title.Should().NotBeNullOrWhiteSpace();
            processor.GetPageList().Single().PageName.Should().NotBeNullOrWhiteSpace();
        }
    }
}
