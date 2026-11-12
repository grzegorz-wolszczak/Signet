using System.Collections.Generic;
using System.Linq;
using AvaloniaEdit.Document;
using AwesomeAssertions;
using Signet.App.ViewModels;
using Signet.Core.MainUI;
using Xunit;

namespace Signet.App.Tests;

/// <summary>
/// The bookmarks of the book: one per line, toggled, found next / previous, and following their line through the edits of
/// the open file (the lines are what the gutter marks and the panel lists).
/// </summary>
public sealed class BookmarkListTests
{
    private const string Chapter = "OEBPS/Text/chapter1.xhtml";
    private const string Style = "OEBPS/Styles/style.css";

    [Fact]
    public void Toggling_a_line_adds_a_bookmark_and_toggling_it_again_removes_it()
    {
        // Arrange
        BookmarkList sut = new();

        // Act
        Bookmark? added = sut.Toggle(Chapter, 5);

        // Assert
        added.Should().NotBeNull();
        sut.Find(Chapter, 5).Should().BeSameAs(added);
        sut.Items.Should().ContainSingle();

        // Act
        Bookmark? removed = sut.Toggle(Chapter, 5);

        // Assert
        removed.Should().BeNull();
        sut.Items.Should().BeEmpty();
    }

    [Fact]
    public void The_bookmarks_are_ordered_by_file_and_line()
    {
        // Arrange
        BookmarkList sut = new();

        // Act
        sut.Toggle(Style, 9);
        sut.Toggle(Chapter, 30);
        sut.Toggle(Chapter, 4);

        // Assert
        sut.Items.Select(b => (b.BookPath, b.Line)).Should().Equal((Style, 9), (Chapter, 4), (Chapter, 30));
        sut.LinesIn(Chapter).Should().BeEquivalentTo(new List<int> { 4, 30 });
    }

    [Fact]
    public void Changes_are_announced_once_each()
    {
        // Arrange
        BookmarkList sut = new();
        int changes = 0;
        sut.Changed += (_, _) => changes++;

        // Act
        Bookmark? bookmark = sut.Toggle(Chapter, 2);
        sut.Remove(bookmark!);
        sut.Remove(bookmark!); // already gone: nothing changes

        // Assert
        changes.Should().Be(2);
    }

    [Fact]
    public void Loaded_places_replace_the_bookmarks_and_are_given_back_as_places()
    {
        // Arrange
        BookmarkList sut = new();
        sut.Toggle(Style, 1);
        BookmarkPlace[] places = { new(Chapter, 7), new(Chapter, 3), new(Chapter, 7) };

        // Act
        sut.Load(places);

        // Assert
        sut.Places.Should().Equal(new BookmarkPlace(Chapter, 3), new BookmarkPlace(Chapter, 7));
    }

    [Fact]
    public void The_neighbour_is_looked_for_in_the_file_only()
    {
        // Arrange
        BookmarkList sut = new();
        sut.Toggle(Chapter, 10);
        sut.Toggle(Chapter, 20);
        sut.Toggle(Style, 15);

        // Act & Assert
        sut.Neighbour(Chapter, 10, forward: true).Should().Be(20);
        sut.Neighbour(Chapter, 20, forward: true).Should().Be(10);
        sut.Neighbour(Chapter, 10, forward: false).Should().Be(20);
        sut.Neighbour("OEBPS/Text/none.xhtml", 1, forward: true).Should().BeNull();
    }

    [Fact]
    public void A_bookmark_moves_with_its_line_when_lines_are_added_above()
    {
        // Arrange
        BookmarkList sut = new();
        TextDocument document = new("one\ntwo\nthree\nfour");
        sut.Toggle(Chapter, 3);
        sut.Attach(Chapter, document);

        // Act
        document.Insert(0, "zero\nzero-and-a-half\n");

        // Assert
        sut.Items.Should().ContainSingle().Which.Line.Should().Be(5);
    }

    [Fact]
    public void A_bookmark_added_while_the_file_is_open_follows_its_line_too()
    {
        // Arrange
        BookmarkList sut = new();
        TextDocument document = new("one\ntwo\nthree");
        sut.Attach(Chapter, document);
        sut.Toggle(Chapter, 2);

        // Act
        document.Insert(0, "zero\n");

        // Assert
        sut.Items.Should().ContainSingle().Which.Line.Should().Be(3);
    }

    [Fact]
    public void A_bookmark_stays_with_its_text_when_a_line_break_is_typed_at_the_start_of_its_line()
    {
        // Arrange
        BookmarkList sut = new();
        TextDocument document = new("one\ntwo\nthree");
        sut.Toggle(Chapter, 2);
        sut.Attach(Chapter, document);

        // Act
        document.Insert(document.GetLineByNumber(2).Offset, "\n");

        // Assert
        sut.Items.Should().ContainSingle().Which.Line.Should().Be(3, "the text of the line moved one line down");
    }

    [Fact]
    public void Typing_inside_the_line_of_a_bookmark_changes_nothing()
    {
        // Arrange
        BookmarkList sut = new();
        TextDocument document = new("one\ntwo\nthree");
        sut.Toggle(Chapter, 2);
        sut.Attach(Chapter, document);
        int changes = 0;
        sut.Changed += (_, _) => changes++;

        // Act
        document.Insert(document.GetLineByNumber(2).Offset + 1, "xyz");
        document.Insert(document.GetLineByNumber(2).Offset, "abc");

        // Assert
        sut.Items.Should().ContainSingle().Which.Line.Should().Be(2);
        changes.Should().Be(0);
    }

    [Fact]
    public void A_bookmark_goes_away_with_its_line()
    {
        // Arrange
        BookmarkList sut = new();
        TextDocument document = new("one\ntwo\nthree\nfour");
        sut.Toggle(Chapter, 2);
        sut.Toggle(Chapter, 4);
        sut.Attach(Chapter, document);

        // Act
        document.Remove(document.GetLineByNumber(2).Offset, document.GetLineByNumber(2).TotalLength);

        // Assert
        sut.Items.Should().ContainSingle().Which.Line.Should().Be(3, "the bookmark of the last line moved up, the deleted line's is gone");
    }

    [Fact]
    public void Replacing_the_whole_text_keeps_the_bookmarks_on_their_lines()
    {
        // Arrange
        BookmarkList sut = new();
        TextDocument document = new("one\ntwo\nthree\nfour");
        sut.Toggle(Chapter, 2);
        sut.Toggle(Chapter, 4);
        sut.Attach(Chapter, document);

        // Act
        document.Text = "uno\ndos\ntres";

        // Assert
        sut.Items.Select(b => b.Line).Should().Equal(new List<int> { 2, 3 }, "a line beyond the end of the new text is clamped to its last line");

        // Act
        document.Insert(0, "cero\n");

        // Assert
        sut.Items.Select(b => b.Line).Should().Equal(new List<int> { 3, 4 }, "they follow the new text again");
    }

    [Fact]
    public void A_closed_file_keeps_the_line_its_bookmark_was_on()
    {
        // Arrange
        BookmarkList sut = new();
        TextDocument document = new("one\ntwo\nthree");
        sut.Toggle(Chapter, 2);
        sut.Attach(Chapter, document);
        document.Insert(0, "zero\n");

        // Act
        sut.Detach(Chapter);
        document.Insert(0, "minus-one\n"); // no longer followed

        // Assert
        sut.Items.Should().ContainSingle().Which.Line.Should().Be(3);
    }

    [Fact]
    public void The_text_of_a_line_comes_from_the_open_document_or_from_the_closed_file()
    {
        // Arrange
        BookmarkList sut = new() { ClosedFileLineText = (path, line) => path == Style ? $"  closed line {line}  " : null };
        Bookmark open = sut.Toggle(Chapter, 2)!;
        Bookmark closed = sut.Toggle(Style, 4)!;
        Bookmark unknown = sut.Toggle("OEBPS/Text/none.xhtml", 1)!;
        sut.Attach(Chapter, new TextDocument("one\n  <p>two</p>  \nthree"));

        // Act & Assert
        sut.LineText(open).Should().Be("<p>two</p>");
        sut.LineText(closed).Should().Be("closed line 4");
        sut.LineText(unknown).Should().BeEmpty();
    }

    [Fact]
    public void Clearing_removes_all_the_bookmarks()
    {
        // Arrange
        BookmarkList sut = new();
        sut.Toggle(Chapter, 1);
        sut.Toggle(Style, 1);
        List<int> changes = new();
        sut.Changed += (_, _) => changes.Add(sut.Items.Count);

        // Act
        sut.Clear();
        sut.Clear();

        // Assert
        sut.Items.Should().BeEmpty();
        changes.Should().Equal(0);
    }
}
