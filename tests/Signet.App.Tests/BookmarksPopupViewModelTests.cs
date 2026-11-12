using System;
using System.Collections.Generic;
using System.Linq;
using AwesomeAssertions;
using Signet.App.Actions;
using Signet.App.ViewModels;
using Signet.Core.MainUI;
using Xunit;

namespace Signet.App.Tests;

/// <summary>The Show Bookmarks popup: the list, the file shown for the selected bookmark, going to one and the empty popup.</summary>
public sealed class BookmarksPopupViewModelTests
{
    private const string Chapter = "OEBPS/Text/chapter1.xhtml";
    private const string Style = "OEBPS/Styles/style.css";

    private static (BookmarkList List, BookmarksPopupViewModel Sut, List<Bookmark> Visited) New(string shortcut = "")
    {
        BookmarkList list = new();
        list.Toggle(Chapter, 8);
        list.Toggle(Style, 2);
        list.Toggle(Chapter, 3);
        List<Bookmark> visited = new();
        BookmarksPopupViewModel sut = new(list, _ => null, path => $"text of {path}", visited.Add, shortcut);
        return (list, sut, visited);
    }

    [Fact]
    public void The_list_has_the_file_and_the_line_of_every_bookmark_and_the_first_is_selected()
    {
        // Arrange & Act
        (_, BookmarksPopupViewModel sut, _) = New();

        // Assert
        sut.Items.Select(i => i.Label).Should().Equal("style.css :2", "chapter1.xhtml :3", "chapter1.xhtml :8");
        sut.HasItems.Should().BeTrue();
        sut.Selected.Should().BeSameAs(sut.Items[0]);
    }

    [Fact]
    public void The_preview_follows_the_selected_bookmark()
    {
        // Arrange
        (BookmarkList list, BookmarksPopupViewModel sut, _) = New();
        int changes = 0;
        sut.PreviewChanged += (_, _) => changes++;

        // Act
        sut.Selected = sut.Items[2];

        // Assert
        changes.Should().Be(1);
        sut.PreviewBookPath.Should().Be(Chapter);
        sut.PreviewLine.Should().Be(8);
        sut.PreviewText.Should().Be($"text of {Chapter}");
        sut.PreviewSyntax.Should().Be(CodeEditorSyntax.PlainText, "the file is not in the book");
        sut.BookmarkedLinesOfPreview.Should().BeEquivalentTo(list.LinesIn(Chapter));
    }

    [Fact]
    public void Moving_the_selection_stays_within_the_list()
    {
        // Arrange
        (_, BookmarksPopupViewModel sut, _) = New();

        // Act
        sut.MoveSelection(1);

        // Assert
        sut.Selected.Should().BeSameAs(sut.Items[1]);

        // Act
        sut.MoveSelection(50);

        // Assert
        sut.Selected.Should().BeSameAs(sut.Items[2]);

        // Act
        sut.MoveSelection(-50);

        // Assert
        sut.Selected.Should().BeSameAs(sut.Items[0]);
    }

    [Fact]
    public void Going_to_a_bookmark_asks_the_window_to_go_there_and_to_close()
    {
        // Arrange
        (_, BookmarksPopupViewModel sut, List<Bookmark> visited) = New();
        int closes = 0;
        sut.CloseRequested += (_, _) => closes++;

        // Act
        sut.Navigate(sut.Items[1]);

        // Assert
        visited.Should().ContainSingle().Which.Should().BeSameAs(sut.Items[1].Bookmark);
        closes.Should().Be(1);
    }

    [Fact]
    public void Removing_the_selected_bookmark_deletes_it_and_selects_the_one_that_takes_its_place()
    {
        // Arrange
        (BookmarkList list, BookmarksPopupViewModel sut, _) = New();
        sut.Selected = sut.Items[1];

        // Act
        sut.RemoveSelected();

        // Assert
        list.Items.Select(b => (b.BookPath, b.Line)).Should().Equal((Style, 2), (Chapter, 8));
        sut.Items.Select(i => i.Label).Should().Equal("style.css :2", "chapter1.xhtml :8");
        sut.Selected.Should().BeSameAs(sut.Items[1]);

        // Act
        sut.RemoveSelected();
        sut.RemoveSelected();
        sut.RemoveSelected(); // nothing left: nothing happens

        // Assert
        sut.HasItems.Should().BeFalse();
        sut.Selected.Should().BeNull();
        sut.PreviewText.Should().BeEmpty();
        sut.PreviewLine.Should().Be(0);
    }

    [Fact]
    public void The_empty_popup_says_how_to_add_a_bookmark_with_the_shortcut_when_there_is_one()
    {
        // Arrange & Act
        BookmarksPopupViewModel withShortcut = new(new BookmarkList(), _ => null, _ => null, _ => { }, "F11");
        BookmarksPopupViewModel without = new(new BookmarkList(), _ => null, _ => null, _ => { }, string.Empty);

        // Assert
        withShortcut.HasItems.Should().BeFalse();
        withShortcut.EmptyTitle.Should().NotBeEmpty();
        withShortcut.EmptyHint.Should().Contain("F11");
        without.EmptyHint.Should().NotBeEmpty().And.NotContain("F11");
        withShortcut.Selected.Should().BeNull();
        withShortcut.PreviewText.Should().BeEmpty();
    }

    [Fact]
    public void Moving_in_an_empty_popup_does_nothing()
    {
        // Arrange
        BookmarksPopupViewModel sut = new(new BookmarkList(), _ => null, _ => null, _ => { }, string.Empty);

        // Act
        Action move = () => sut.MoveSelection(1);

        // Assert
        move.Should().NotThrow();
    }

    [Fact]
    public void Show_Bookmarks_has_a_shortcut_of_its_own_and_next_and_previous_have_none()
    {
        // Arrange
        Dictionary<string, AppActionDescriptor> catalog = AppActionCatalog.All.ToDictionary(a => a.Id);

        // Act & Assert
        catalog[AppActionIds.ShowBookmarks].DefaultShortcut.Should().Be("Shift+F11");
        catalog[AppActionIds.BookmarkLocation].DefaultShortcut.Should().Be("F11");
        catalog[AppActionIds.NextBookmark].DefaultShortcut.Should().BeEmpty();
        catalog[AppActionIds.PreviousBookmark].DefaultShortcut.Should().BeEmpty();
        catalog[AppActionIds.ToggleBookmarks].DefaultShortcut.Should().BeEmpty("the panel has none, like the Bookmarks tool window has no Shift+F11");
    }
}
