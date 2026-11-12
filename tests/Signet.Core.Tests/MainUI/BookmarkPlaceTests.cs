using System.Collections.Generic;
using System.Linq;
using AwesomeAssertions;
using Signet.Core.MainUI;
using Signet.Core.Misc;
using Signet.Core.Tests.TestSupport;
using Xunit;

namespace Signet.Core.Tests.MainUI;

/// <summary>The bookmarks kept per book: round trip, which lines count as next / previous, and their lifetime in the settings.</summary>
public sealed class BookmarkPlaceTests
{
    private static BookmarkPlace[] Sample() => new[]
    {
        new BookmarkPlace("OEBPS/Text/a.xhtml", 12),
        new BookmarkPlace("OEBPS/Text/a.xhtml", 80),
        new BookmarkPlace("OEBPS/Styles/style.css", 3),
    };

    [Fact]
    public void Places_survive_serialization()
    {
        // Arrange
        BookmarkPlace[] places = Sample();

        // Act
        IReadOnlyList<BookmarkPlace> read = BookmarkPlace.Parse(BookmarkPlace.Serialize(places));

        // Assert
        read.Should().Equal(places);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("not json")]
    [InlineData("{\"x\":1}")]
    public void Garbage_is_no_places(string? text)
    {
        // Act & Assert
        BookmarkPlace.Parse(text).Should().BeEmpty();
    }

    [Fact]
    public void An_entry_without_a_path_or_a_line_is_skipped_and_a_line_is_not_taken_twice()
    {
        // Arrange
        const string text = "[{\"p\":\"a.xhtml\",\"l\":4},{\"p\":\"\",\"l\":2},{\"p\":\"b.xhtml\",\"l\":0},{\"p\":\"a.xhtml\",\"l\":4},{\"l\":1}]";

        // Act
        IReadOnlyList<BookmarkPlace> read = BookmarkPlace.Parse(text);

        // Assert
        read.Should().Equal(new BookmarkPlace("a.xhtml", 4));
    }

    [Theory]
    [InlineData(10, true, 20)]
    [InlineData(20, true, 30)]
    [InlineData(30, true, 10)] // wraps around at the end of the file
    [InlineData(35, true, 10)]
    [InlineData(5, true, 10)]
    [InlineData(10, false, 30)] // wraps around at the start of the file
    [InlineData(20, false, 10)]
    [InlineData(30, false, 20)]
    [InlineData(5, false, 30)]
    public void The_neighbour_is_the_next_line_with_a_bookmark_in_the_direction_wrapping_around(int current, bool forward, int expected)
    {
        // Arrange
        int[] lines = { 30, 10, 20 };

        // Act
        int? neighbour = BookmarkPlace.Neighbour(lines, current, forward);

        // Assert
        neighbour.Should().Be(expected);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void A_file_with_no_other_bookmark_than_the_one_on_the_line_has_no_neighbour(bool forward)
    {
        // Act & Assert
        BookmarkPlace.Neighbour(new List<int> { 7 }, 7, forward).Should().BeNull();
        BookmarkPlace.Neighbour(new List<int>(), 7, forward).Should().BeNull();
    }

    [Fact]
    public void Bookmarks_survive_in_the_settings_per_book_file()
    {
        // Arrange
        using TempDir dir = new();
        string book = dir.Combine("book.epub");
        string other = dir.Combine("other.epub");
        SettingsStore settings = new(dir.Combine("settings.xml"));
        settings.RecentFiles = new[] { book, other };

        // Act
        settings.SetBookBookmarks(book, Sample());

        // Assert
        settings.GetBookBookmarks(book).Should().Equal(Sample());
        settings.GetBookBookmarks(other).Should().BeEmpty();
    }

    [Fact]
    public void Keeping_no_bookmarks_forgets_them()
    {
        // Arrange
        using TempDir dir = new();
        string book = dir.Combine("book.epub");
        SettingsStore settings = new(dir.Combine("settings.xml"));
        settings.RecentFiles = new[] { book };
        settings.SetBookBookmarks(book, Sample());

        // Act
        settings.SetBookBookmarks(book, new List<BookmarkPlace>());

        // Assert
        settings.GetBookBookmarks(book).Should().BeEmpty();
    }

    [Fact]
    public void Bookmarks_go_away_with_their_entry_in_the_recent_files()
    {
        // Arrange
        using TempDir dir = new();
        string kept = dir.Combine("kept.epub");
        string removed = dir.Combine("removed.epub");
        SettingsStore settings = new(dir.Combine("settings.xml"));
        settings.RecentFiles = new[] { kept, removed };
        settings.SetBookBookmarks(kept, Sample());
        settings.SetBookBookmarks(removed, Sample());

        // Act
        settings.RecentFiles = new[] { kept };

        // Assert
        settings.GetBookBookmarks(kept).Should().NotBeEmpty();
        settings.GetBookBookmarks(removed).Should().BeEmpty();
    }

    [Fact]
    public void Clearing_the_recent_files_forgets_all_bookmarks()
    {
        // Arrange
        using TempDir dir = new();
        string book = dir.Combine("book.epub");
        SettingsStore settings = new(dir.Combine("settings.xml"));
        settings.RecentFiles = new[] { book };
        settings.SetBookBookmarks(book, Sample());

        // Act
        settings.RecentFiles = Enumerable.Empty<string>().ToList();

        // Assert
        settings.GetBookBookmarks(book).Should().BeEmpty();
    }

    [Fact]
    public void A_book_outside_the_recent_files_keeps_no_bookmarks()
    {
        // Arrange
        using TempDir dir = new();
        string book = dir.Combine("book.epub");
        SettingsStore settings = new(dir.Combine("settings.xml"));

        // Act
        settings.SetBookBookmarks(book, Sample());

        // Assert
        settings.GetBookBookmarks(book).Should().BeEmpty();
    }
}
