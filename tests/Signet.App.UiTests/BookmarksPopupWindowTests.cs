using Avalonia;
using System.Linq;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Headless;
using Avalonia.Threading;
using Avalonia.VisualTree;
using AwesomeAssertions;
using Signet.App.CodeEditor;
using Signet.App.ViewModels;
using Signet.App.Views;

namespace Signet.App.UiTests;

/// <summary>The Show Bookmarks popup window: the preview of the selected bookmark, the keys and the empty popup.</summary>
public sealed class BookmarksPopupWindowTests
{
    private const string Chapter = "OEBPS/Text/chapter1.xhtml";
    private const string Style = "OEBPS/Styles/style.css";

    private static (BookmarksPopupWindow Window, BookmarksPopupViewModel Vm, System.Collections.Generic.List<Bookmark> Visited) Show(bool withBookmarks)
    {
        BookmarkList list = new();
        if (withBookmarks)
        {
            list.Toggle(Chapter, 2);
            list.Toggle(Style, 1);
        }

        System.Collections.Generic.List<Bookmark> visited = new();
        BookmarksPopupViewModel vm = new(
            list, _ => null, path => path == Chapter ? "<p>one</p>\n<p>two</p>\n<p>three</p>" : "p { margin: 0 }", visited.Add, "F11");
        BookmarksPopupWindow window = new() { DataContext = vm };
        window.Show();
        Dispatcher.UIThread.RunJobs();
        return (window, vm, visited);
    }

    [AvaloniaFact]
    public void The_preview_shows_the_file_of_the_selected_bookmark_with_its_line_and_the_bookmark_column()
    {
        // Arrange
        (BookmarksPopupWindow window, BookmarksPopupViewModel vm, _) = Show(withBookmarks: true);
        BookmarkPreviewEditor preview = window.GetVisualDescendants().OfType<BookmarkPreviewEditor>().Single();

        // Assert
        preview.Document.Text.Should().Be("p { margin: 0 }", "the first bookmark is selected: the stylesheet");

        // Act
        vm.Selected = vm.Items[1];
        Dispatcher.UIThread.RunJobs();

        // Assert
        preview.Document.Text.Should().Be("<p>one</p>\n<p>two</p>\n<p>three</p>");
        preview.TextArea.Caret.Line.Should().Be(2, "the line of the bookmark");
        preview.IsReadOnly.Should().BeTrue();
        preview.TextArea.LeftMargins.OfType<BookmarkMargin>().Should().ContainSingle()
            .Which.Lines!().Should().BeEquivalentTo(new System.Collections.Generic.List<int> { 2 });
        window.Close();
    }

    [AvaloniaFact]
    public void The_keys_move_the_selection_go_to_the_bookmark_and_close_the_popup()
    {
        // Arrange
        (BookmarksPopupWindow window, BookmarksPopupViewModel vm, System.Collections.Generic.List<Bookmark> visited) = Show(withBookmarks: true);

        // Act
        window.KeyPressQwerty(PhysicalKey.ArrowDown, RawInputModifiers.None);
        Dispatcher.UIThread.RunJobs();

        // Assert
        vm.Selected.Should().BeSameAs(vm.Items[1]);

        // Act
        window.KeyPressQwerty(PhysicalKey.Enter, RawInputModifiers.None);
        Dispatcher.UIThread.RunJobs();

        // Assert
        visited.Should().ContainSingle().Which.Line.Should().Be(2);
        window.IsVisible.Should().BeFalse("choosing a bookmark closes the popup");
    }

    [AvaloniaFact]
    public void Escape_closes_the_popup_without_going_anywhere()
    {
        // Arrange
        (BookmarksPopupWindow window, _, System.Collections.Generic.List<Bookmark> visited) = Show(withBookmarks: true);

        // Act
        window.KeyPressQwerty(PhysicalKey.Escape, RawInputModifiers.None);
        Dispatcher.UIThread.RunJobs();

        // Assert
        window.IsVisible.Should().BeFalse();
        visited.Should().BeEmpty();
    }

    [AvaloniaFact]
    public void Without_a_bookmark_the_popup_shows_the_hint_instead_of_the_list_and_the_preview()
    {
        // Arrange & Act
        (BookmarksPopupWindow window, BookmarksPopupViewModel vm, _) = Show(withBookmarks: false);

        // Assert
        vm.HasItems.Should().BeFalse();
        window.GetVisualDescendants().OfType<BookmarkPreviewEditor>().Single().IsEffectivelyVisible.Should().BeFalse();
        window.GetVisualDescendants().OfType<TextBlock>().Where(t => t.IsEffectivelyVisible).Select(t => t.Text)
            .Should().Contain(vm.EmptyTitle).And.Contain(vm.EmptyHint);
        window.Close();
    }

    // A popup over a long file with a bookmark far down, and a font of its own for the preview.
    private static (BookmarksPopupWindow Window, BookmarksPopupViewModel Vm, System.Collections.Generic.List<Bookmark> Visited) ShowLongFile()
    {
        BookmarkList list = new();
        list.Toggle(Chapter, 150);
        System.Collections.Generic.List<Bookmark> visited = new();
        string text = string.Join('\n', Enumerable.Range(1, 300).Select(i => $"<p>line {i}</p>"));
        BookmarksPopupViewModel vm = new(
            list, _ => null, _ => text, visited.Add, "F11", new Signet.Core.Misc.CodeEditorAppearance { FontFamily = "Consolas", FontSize = 17 });
        BookmarksPopupWindow window = new() { DataContext = vm };
        window.Show();
        Dispatcher.UIThread.RunJobs();
        Avalonia.Headless.AvaloniaHeadlessPlatform.ForceRenderTimerTick();
        Dispatcher.UIThread.RunJobs();
        return (window, vm, visited);
    }

    [AvaloniaFact]
    public void The_preview_opens_scrolled_to_the_line_of_the_selected_bookmark()
    {
        // Arrange & Act
        (BookmarksPopupWindow window, _, _) = ShowLongFile();
        BookmarkPreviewEditor preview = window.GetVisualDescendants().OfType<BookmarkPreviewEditor>().Single();
        AvaloniaEdit.Rendering.TextView view = preview.TextArea.TextView;
        double top = view.GetVisualTopByDocumentLine(150);

        // Assert
        preview.VerticalOffset.Should().BeGreaterThan(0, "the file is not shown from its first line");
        top.Should().BeInRange(preview.VerticalOffset, preview.VerticalOffset + view.Bounds.Height, "the line of the bookmark is in view");
        window.Close();
    }

    [AvaloniaFact]
    public void A_single_click_on_a_bookmark_selects_it_and_leaves_the_popup_open()
    {
        // Arrange
        (BookmarksPopupWindow window, BookmarksPopupViewModel vm, System.Collections.Generic.List<Bookmark> visited) = Show(withBookmarks: true);
        ListBoxItem second = window.GetVisualDescendants().OfType<ListBoxItem>().Skip(1).First();
        Avalonia.Point point = second.TranslatePoint(new Avalonia.Point(10, second.Bounds.Height / 2), window)!.Value;

        // Act
        window.MouseDown(point, Avalonia.Input.MouseButton.Left);
        window.MouseUp(point, Avalonia.Input.MouseButton.Left);
        Dispatcher.UIThread.RunJobs();

        // Assert
        vm.Selected.Should().BeSameAs(vm.Items[1]);
        visited.Should().BeEmpty("a click does not go to the bookmark");
        window.IsVisible.Should().BeTrue("the popup closes on Esc, on a click outside it, or once a bookmark is gone to");
        window.Close();
    }

    [AvaloniaFact]
    public void The_preview_has_the_font_of_Code_Editor_and_Ctrl_wheel_changes_its_size()
    {
        // Arrange
        (BookmarksPopupWindow window, _, _) = ShowLongFile();
        BookmarkPreviewEditor preview = window.GetVisualDescendants().OfType<BookmarkPreviewEditor>().Single();
        int before = Signet.App.CodeEditor.EditorZoom.Delta;
        try
        {
            // Assert
            preview.FontFamily.Name.Should().Be("Consolas");
            preview.FontSize.Should().Be(17 + before);

            // Act
            Avalonia.Point inPreview = preview.TranslatePoint(new Avalonia.Point(100, 100), window)!.Value;
            window.MouseWheel(inPreview, new Avalonia.Vector(0, 1), Avalonia.Input.RawInputModifiers.Control);
            Dispatcher.UIThread.RunJobs();

            // Assert
            preview.FontSize.Should().Be(17 + before + 1, "the wheel with Ctrl makes the font a pixel larger");

            // Act
            window.MouseWheel(inPreview, new Avalonia.Vector(0, -1), Avalonia.Input.RawInputModifiers.Control);
            Dispatcher.UIThread.RunJobs();

            // Assert
            preview.FontSize.Should().Be(17 + before);
        }
        finally
        {
            Signet.App.CodeEditor.EditorZoom.Delta = before;
            window.Close();
        }
    }
}
