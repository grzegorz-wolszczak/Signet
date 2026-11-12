using System.Linq;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using Avalonia.VisualTree;
using AwesomeAssertions;
using Signet.App.ViewModels;
using Signet.App.Views;
using Signet.Controls.TreeDataGrid;

namespace Signet.App.UiTests;

/// <summary>The "Bookmarks" panel as the user sees it: the hint without a bookmark, the tree with them.</summary>
public sealed class BookmarksPanelViewTests
{
    private const string Chapter = "EPUB/text/ch_18.xhtml";

    private static (Window Window, BookmarkList List, BookmarksViewModel Vm) Show()
    {
        BookmarkList list = new();
        BookmarksViewModel vm = new(list);
        Window window = new() { Width = 600, Height = 300, Content = new BookmarksView { DataContext = vm } };
        window.Show();
        Dispatcher.UIThread.RunJobs();
        return (window, list, vm);
    }

    [AvaloniaFact]
    public void The_panel_can_be_created_and_shows_the_hint_without_a_bookmark()
    {
        // Arrange & Act
        (Window window, _, BookmarksViewModel vm) = Show();

        // Assert
        vm.HasBookmarks.Should().BeFalse();
        window.GetVisualDescendants().OfType<TextBlock>().Where(t => t.IsEffectivelyVisible).Select(t => t.Text)
            .Should().Contain(t => t != null && t.Contains("F11", System.StringComparison.Ordinal));
        window.GetVisualDescendants().OfType<TreeDataGrid>().Single().IsEffectivelyVisible.Should().BeFalse();
        window.Close();
    }

    [AvaloniaFact]
    public void A_bookmark_added_while_the_panel_is_open_shows_up_in_the_tree_and_hides_the_hint()
    {
        // Arrange
        (Window window, BookmarkList list, BookmarksViewModel vm) = Show();

        // Act
        list.Toggle(Chapter, 158);
        Dispatcher.UIThread.RunJobs();

        // Assert
        vm.HasBookmarks.Should().BeTrue();
        TreeDataGrid tree = window.GetVisualDescendants().OfType<TreeDataGrid>().Single();
        tree.IsEffectivelyVisible.Should().BeTrue();
        tree.Rows!.Count.Should().Be(2, "the file and its bookmark");
        window.GetVisualDescendants().OfType<TextBlock>().Where(t => t.IsEffectivelyVisible).Select(t => t.Text)
            .Should().NotContain(t => t != null && t.Contains("F11", System.StringComparison.Ordinal));

        // Act
        list.Toggle(Chapter, 158);
        Dispatcher.UIThread.RunJobs();

        // Assert
        tree.IsEffectivelyVisible.Should().BeFalse("the last bookmark is gone");
        window.Close();
    }

    [AvaloniaFact]
    public void A_panel_created_after_the_bookmarks_shows_them()
    {
        // Arrange
        BookmarkList list = new();
        list.Toggle(Chapter, 158);
        list.Toggle("EPUB/text/ch_01.xhtml", 3);
        BookmarksViewModel vm = new(list);

        // Act
        Window window = new() { Width = 600, Height = 300, Content = new BookmarksView { DataContext = vm } };
        window.Show();
        Dispatcher.UIThread.RunJobs();

        // Assert
        window.GetVisualDescendants().OfType<TreeDataGrid>().Single().Rows!.Count.Should().Be(4, "two files and a bookmark in each");
        window.Close();
    }

    [AvaloniaFact]
    public void The_line_of_a_bookmark_is_colored_by_the_syntax_of_its_file()
    {
        // Arrange
        BookmarkList list = new() { ClosedFileLineText = (_, _) => "<p class=\"paragraph\">No, jest dosyc jasne.</p>" };
        list.Toggle(Chapter, 160);
        BookmarksViewModel vm = new(list, _ => Signet.Core.MainUI.CodeEditorSyntax.Html);
        Window window = new() { Width = 600, Height = 300, Content = new BookmarksView { DataContext = vm } };

        // Act
        window.Show();
        Dispatcher.UIThread.RunJobs();

        // Assert
        BookmarkNodeText line = window.GetVisualDescendants().OfType<BookmarkNodeText>().First(t => t.Node?.Bookmark is not null);
        line.Inlines!.OfType<Avalonia.Controls.Documents.Run>().Select(r => r.Text).Should().StartWith("160: ").And.Contain("<p");
        line.Inlines!.OfType<Avalonia.Controls.Documents.Run>().Where(r => r.Foreground is not null).Should().NotBeEmpty("the tag and the attribute are colored");
        string.Concat(line.Inlines!.OfType<Avalonia.Controls.Documents.Run>().Select(r => r.Text)).Should().Be("160: <p class=\"paragraph\">No, jest dosyc jasne.</p>");
        window.Close();
    }
}
