using System.Linq;
using AwesomeAssertions;
using Signet.App.ViewModels;
using Xunit;

namespace Signet.App.Tests;

/// <summary>The "Bookmarks" panel: the bookmarks by file, deleting them and going to one.</summary>
public sealed class BookmarksViewModelTests
{
    private const string Chapter = "OEBPS/Text/chapter1.xhtml";
    private const string Style = "OEBPS/Styles/style.css";

    private static (BookmarkList List, BookmarksViewModel Sut) New()
    {
        BookmarkList list = new() { ClosedFileLineText = (path, line) => $"text of {path} line {line}" };
        return (list, new BookmarksViewModel(list));
    }

    [Fact]
    public void An_empty_book_has_an_empty_panel()
    {
        // Arrange & Act
        (_, BookmarksViewModel sut) = New();

        // Assert
        sut.HasBookmarks.Should().BeFalse();
        sut.Roots.Should().BeEmpty();
        sut.ClearCommand.CanExecute(null).Should().BeFalse();
    }

    [Fact]
    public void The_bookmarks_are_grouped_by_file_with_their_lines_and_the_text_of_the_line()
    {
        // Arrange
        (BookmarkList list, BookmarksViewModel sut) = New();

        // Act
        list.Toggle(Style, 9);
        list.Toggle(Chapter, 30);
        list.Toggle(Chapter, 4);

        // Assert
        sut.HasBookmarks.Should().BeTrue();
        sut.Roots.Select(r => r.Text).Should().Equal("style.css (1)", "chapter1.xhtml (2)");
        sut.Roots[1].Children.Select(c => c.Text).Should().Equal($"4: text of {Chapter} line 4", $"30: text of {Chapter} line 30");
        sut.Roots[1].Children.Should().OnlyContain(c => c.Bookmark != null && c.Children.Count == 0);
        sut.Roots[1].Bookmark.Should().BeNull("a file node is not a bookmark");
    }

    [Fact]
    public void A_line_without_a_known_text_is_shown_by_its_number()
    {
        // Arrange
        BookmarkList list = new();
        BookmarksViewModel sut = new(list);

        // Act
        list.Toggle(Chapter, 12);

        // Assert
        sut.Roots[0].Children.Should().ContainSingle().Which.Text.Should().Be("12");
    }

    [Fact]
    public void Deleting_a_selected_bookmark_removes_only_that_one()
    {
        // Arrange
        (BookmarkList list, BookmarksViewModel sut) = New();
        list.Toggle(Chapter, 4);
        list.Toggle(Chapter, 30);
        sut.Selected = sut.Roots[0].Children[0];

        // Act
        sut.DeleteCommand.Execute(null);

        // Assert
        list.Items.Select(b => b.Line).Should().Equal(30);
        sut.Selected.Should().BeNull("the tree was built again");
        sut.DeleteCommand.CanExecute(null).Should().BeFalse();
    }

    [Fact]
    public void Deleting_a_selected_file_removes_all_its_bookmarks()
    {
        // Arrange
        (BookmarkList list, BookmarksViewModel sut) = New();
        list.Toggle(Chapter, 4);
        list.Toggle(Chapter, 30);
        list.Toggle(Style, 2);
        sut.Selected = sut.Roots[1];

        // Act
        sut.DeleteCommand.Execute(null);

        // Assert
        list.Items.Select(b => b.BookPath).Should().Equal(Style);
    }

    [Fact]
    public void Nothing_is_deleted_without_a_selection()
    {
        // Arrange
        (BookmarkList list, BookmarksViewModel sut) = New();
        list.Toggle(Chapter, 4);

        // Act & Assert
        sut.DeleteCommand.CanExecute(null).Should().BeFalse();
        sut.DeleteCommand.Execute(null);
        list.Items.Should().ContainSingle();
    }

    [Fact]
    public void Clearing_removes_every_bookmark()
    {
        // Arrange
        (BookmarkList list, BookmarksViewModel sut) = New();
        list.Toggle(Chapter, 4);
        list.Toggle(Style, 2);

        // Act
        sut.ClearCommand.Execute(null);

        // Assert
        list.Items.Should().BeEmpty();
        sut.Roots.Should().BeEmpty();
        sut.HasBookmarks.Should().BeFalse();
    }

    [Fact]
    public void Activating_a_bookmark_asks_to_go_to_it_and_a_file_does_not()
    {
        // Arrange
        (BookmarkList list, BookmarksViewModel sut) = New();
        Bookmark? bookmark = list.Toggle(Chapter, 4);
        Bookmark? activated = null;
        int activations = 0;
        sut.BookmarkActivated += (_, b) =>
        {
            activated = b;
            activations++;
        };

        // Act
        sut.Activate(sut.Roots[0]);
        sut.Activate(sut.Roots[0].Children[0]);

        // Assert
        activated.Should().BeSameAs(bookmark);
        activations.Should().Be(1);
    }
}
