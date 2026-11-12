using System;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.Threading;
using Avalonia.VisualTree;
using AvaloniaEdit;
using AwesomeAssertions;
using Moq;
using Signet.App.Actions;
using Signet.App.CodeEditor;
using Signet.App.Resources;
using Signet.App.Services;
using Signet.App.ViewModels;
using Signet.App.ViewModels.Tabs;
using Signet.App.Views.Tabs;
using Signet.Core.MainUI;
using Signet.Core.Misc;
using Signet.Core.Resources;
using Signet.Core.Spellcheck;
using Signet.Core.Tests.TestSupport;

namespace Signet.App.UiTests;

/// <summary>
/// The gutter of Code Editor: the column that marks the bookmarked lines, and the menu of a right click on it (add or
/// delete the bookmark of the line, soft wrap) — which replaces the soft wrap item of the menu of the text.
/// </summary>
public sealed class BookmarkGutterTests : IDisposable
{
    private const string Text = "<p>one</p>\n<p>two</p>\n<p>three</p>\n<p>four</p>";

    private readonly Disposables _disposables = new();

    /// <inheritdoc />
    public void Dispose() => _disposables.Dispose();

    private (Window Window, CodeTabViewModel Vm, BookmarkList Bookmarks, TextEditor Editor) Show()
    {
        string root = _disposables.Add(new TempDir("signet-uitests-")).Path;
        SettingsStore settings = new(System.IO.Path.Combine(root, "settings.xml"));
        SpellChecker spellChecker = new(
            settings,
            System.IO.Path.Combine(root, "hunspell_dictionaries"),
            System.IO.Path.Combine(root, "user_dictionaries"));
        string path = System.IO.Path.Combine(root, "Section0001.xhtml");
        System.IO.File.WriteAllText(path, Text);
        HtmlResource html = new(root, path);
        html.InitialLoad();

        BookmarkList bookmarks = new();
        Mock<ICodeTabHost> host = new();
        host.SetupGet(h => h.Bookmarks).Returns(bookmarks);
        host.SetupGet(h => h.CodeEditorShortcutActions).Returns(Array.Empty<AppAction>());
        host.SetupGet(h => h.ClipLibraryRoot).Returns(new Signet.Core.MiscEditors.ClipEditorModel().Root);
        host.Setup(h => h.GetActionIcon(AppActionIds.BookmarkLocation)).Returns("bookmark");
        CodeTabViewModel vm = new(new TabManagerModel().OpenResource(html), new StatusBarService(), settings, spellChecker)
        {
            Host = host.Object,
        };
        Window window = new() { Width = 600, Height = 400, Content = new CodeTabView { DataContext = vm } };
        window.Show();
        Dispatcher.UIThread.RunJobs();
        window.CaptureRenderedFrame();
        Dispatcher.UIThread.RunJobs();
        return (window, vm, bookmarks, window.GetVisualDescendants().OfType<TextEditor>().Single());
    }

    [AvaloniaFact]
    public void The_gutter_has_the_numbers_then_the_icon_column_then_the_folding_markers_then_the_separator()
    {
        // Arrange
        (Window window, _, _, TextEditor editor) = Show();

        // Act
        Control[] margins = editor.TextArea.LeftMargins.ToArray();

        // Assert
        margins.Select(m => m.GetType()).Should().Equal(
            typeof(ThemedLineNumberMargin), typeof(BookmarkMargin), typeof(FoldingGutterMargin), typeof(Avalonia.Controls.Shapes.Line));
        window.Close();
    }

    [AvaloniaFact]
    public void A_bookmarked_line_is_marked_in_the_gutter_and_an_unmarked_one_is_not()
    {
        // Arrange
        (Window window, CodeTabViewModel vm, BookmarkList bookmarks, TextEditor editor) = Show();
        BookmarkMargin margin = editor.TextArea.LeftMargins.OfType<BookmarkMargin>().Single();
        Rect secondLine = LineRowIn(editor, margin, 2);

        // Assert
        RibbonsIn(margin).Should().BeEmpty("no bookmark yet");

        // Act
        bookmarks.Toggle(vm.ResourceBookPath, 2);
        Dispatcher.UIThread.RunJobs();

        // Assert
        RibbonsIn(margin).Should().ContainSingle("the ribbon of the bookmark is drawn")
            .Which.Center.Y.Should().BeInRange(secondLine.Top, secondLine.Bottom, "next to the line that carries the bookmark");

        // Act
        bookmarks.Toggle(vm.ResourceBookPath, 2);

        // Assert
        RibbonsIn(margin).Should().BeEmpty("the mark goes away with the bookmark");
        window.Close();
    }

    [AvaloniaFact]
    public void A_right_click_in_the_gutter_opens_the_gutter_menu_and_adds_the_bookmark_of_the_line_under_the_pointer()
    {
        // Arrange
        (Window window, CodeTabViewModel vm, BookmarkList bookmarks, TextEditor editor) = Show();
        ThemedLineNumberMargin numbers = editor.TextArea.LeftMargins.OfType<ThemedLineNumberMargin>().Single();
        ContextMenu menu = editor.ContextMenu!;
        Rect thirdLine = LineRowIn(editor, numbers, 3);

        // Act
        RightClick(window, numbers, new Point(numbers.Bounds.Width / 2, thirdLine.Center.Y));
        Dispatcher.UIThread.RunJobs();

        // Assert
        menu.IsOpen.Should().BeTrue();
        menu.Items.OfType<MenuItem>().Select(i => (string)i.Header!).Should().Equal(
            Strings.Get("GutterMenu_AddBookmark"),
            Strings.Get("GutterMenu_SoftWrap"));

        // Act
        menu.Items.OfType<MenuItem>().First().RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent));
        menu.Close();
        Dispatcher.UIThread.RunJobs();

        // Assert
        bookmarks.Items.Should().ContainSingle().Which.Line.Should().Be(3, "the line under the pointer, not the one of the caret");
        vm.CaretLine.Should().Be(1, "the click in the gutter does not move the caret");

        // Act
        RightClick(window, numbers, new Point(numbers.Bounds.Width / 2, thirdLine.Center.Y));
        Dispatcher.UIThread.RunJobs();

        // Assert
        ((string)menu.Items.OfType<MenuItem>().First().Header!).Should().Be(Strings.Get("GutterMenu_DeleteBookmark"));
        menu.Close();
        window.Close();
    }

    [AvaloniaFact]
    public void The_soft_wrap_of_the_gutter_menu_toggles_the_wrapping_of_the_text()
    {
        // Arrange
        (Window window, CodeTabViewModel vm, _, TextEditor editor) = Show();
        BookmarkMargin margin = editor.TextArea.LeftMargins.OfType<BookmarkMargin>().Single();
        ContextMenu menu = editor.ContextMenu!;
        bool before = vm.WordWrap;

        // Act
        RightClick(window, margin, new Point(margin.Bounds.Width / 2, LineRowIn(editor, margin, 1).Center.Y));
        Dispatcher.UIThread.RunJobs();
        MenuItem softWrap = menu.Items.OfType<MenuItem>().Last();
        softWrap.RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent));
        menu.Close();
        Dispatcher.UIThread.RunJobs();

        // Assert
        softWrap.ToggleType.Should().Be(MenuItemToggleType.CheckBox);
        softWrap.IsChecked.Should().Be(before, "the check shows the state the menu was opened in");
        vm.WordWrap.Should().Be(!before);
        window.Close();
    }

    [AvaloniaFact]
    public void A_right_click_in_the_text_opens_the_menu_of_the_text_without_the_soft_wrap()
    {
        // Arrange
        (Window window, _, _, TextEditor editor) = Show();
        ContextMenu menu = editor.ContextMenu!;

        // Act
        editor.TextArea.RaiseEvent(new ContextRequestedEventArgs());
        Dispatcher.UIThread.RunJobs();

        // Assert
        menu.IsOpen.Should().BeTrue();
        menu.Items.OfType<MenuItem>().Select(i => i.Header as string).Should().NotContain(Strings.Get("GutterMenu_SoftWrap"));
        menu.Items.OfType<MenuItem>().Select(i => i.Header as string).Should().NotContain(Strings.Get("GutterMenu_AddBookmark"));
        menu.Close();
        window.Close();
    }

    // A press of the mouse on the gutter, delivered to the column itself (the headless platform does not hit the columns).
    private static void Press(Window window, Control column, double y, bool right)
    {
        Point inWindow = column.TranslatePoint(new Point(column.Bounds.Width / 2, y), window)!.Value;
        PointerPointProperties properties = new(
            right ? RawInputModifiers.RightMouseButton : RawInputModifiers.LeftMouseButton,
            right ? PointerUpdateKind.RightButtonPressed : PointerUpdateKind.LeftButtonPressed);
        PointerPressedEventArgs args = new(
            column, new Pointer(Pointer.GetNextFreeId(), PointerType.Mouse, isPrimary: true), window, inWindow, 0, properties, KeyModifiers.None)
        {
            RoutedEvent = InputElement.PointerPressedEvent,
        };
        column.RaiseEvent(args);
    }

    [AvaloniaFact]
    public void A_left_click_on_a_line_number_puts_the_caret_at_the_start_of_that_line_without_selecting_anything()
    {
        // Arrange
        (Window window, CodeTabViewModel vm, _, TextEditor editor) = Show();
        ThemedLineNumberMargin numbers = editor.TextArea.LeftMargins.OfType<ThemedLineNumberMargin>().Single();
        Rect thirdLine = LineRowIn(editor, numbers, 3);

        // Act
        Press(window, numbers, thirdLine.Center.Y, right: false);
        Dispatcher.UIThread.RunJobs();

        // Assert
        editor.TextArea.Caret.Line.Should().Be(3, "the line of the number, not the one below it");
        editor.TextArea.Caret.Column.Should().Be(1);
        editor.SelectionLength.Should().Be(0);
        vm.CaretLine.Should().Be(3);
        window.Close();
    }

    [AvaloniaFact]
    public void A_right_click_on_a_line_number_leaves_the_caret_and_the_selection_alone()
    {
        // Arrange
        (Window window, _, _, TextEditor editor) = Show();
        editor.TextArea.Caret.Line = 2;
        editor.TextArea.Caret.Column = 3;
        ThemedLineNumberMargin numbers = editor.TextArea.LeftMargins.OfType<ThemedLineNumberMargin>().Single();
        Rect fourthLine = LineRowIn(editor, numbers, 4);

        // Act
        Press(window, numbers, fourthLine.Center.Y, right: true);
        Dispatcher.UIThread.RunJobs();

        // Assert
        editor.TextArea.Caret.Line.Should().Be(2);
        editor.TextArea.Caret.Column.Should().Be(3);
        editor.SelectionLength.Should().Be(0);
        window.Close();
    }

    // A right click on the gutter, as the mouse sends it: the press (the view notes where it was) and the release, which
    // asks for the menu.
    private static void RightClick(Window window, Control gutter, Point inGutter)
    {
        Point inWindow = gutter.TranslatePoint(inGutter, window)!.Value;
        window.MouseDown(inWindow, MouseButton.Right);
        window.MouseUp(inWindow, MouseButton.Right);
    }

    // The rectangle of the first row of a line in the coordinates of the gutter column.
    private static Rect LineRowIn(TextEditor editor, Control gutter, int line)
    {
        AvaloniaEdit.Rendering.VisualLine visual = editor.TextArea.TextView.VisualLines.First(l => l.FirstDocumentLine.LineNumber == line);
        double top = visual.VisualTop - editor.TextArea.TextView.VerticalOffset;
        Point offset = editor.TextArea.TextView.TranslatePoint(new Point(0, 0), gutter)!.Value;
        return new Rect(0, offset.Y + top, gutter.Bounds.Width, visual.Height);
    }

    [AvaloniaFact]
    public void The_line_numbers_are_aligned_to_the_left_and_the_icon_column_comes_after_them()
    {
        // Arrange
        (Window window, _, _, TextEditor editor) = Show();
        editor.Document.Text = string.Join('\n', Enumerable.Range(1, 12).Select(i => $"<p>{i}</p>"));
        Dispatcher.UIThread.RunJobs();
        ThemedLineNumberMargin numbers = editor.TextArea.LeftMargins.OfType<ThemedLineNumberMargin>().Single();
        numbers.InvalidateMeasure();
        window.UpdateLayout();
        DrawingGroup drawing = new();

        // Act
        using (DrawingContext context = drawing.Open())
        {
            numbers.Render(context);
        }

        // Assert
        // The one-digit and the two-digit numbers start at the same place: the left padding (right-aligned they would not).
        // (Each number is a drawing group moved to the place of the number.)
        double[] starts = drawing.Children.OfType<DrawingGroup>().Select(g => g.Transform?.Value.M31 ?? 0).Distinct().ToArray();
        starts.Should().HaveCount(1);
        starts[0].Should().BeApproximately(ThemedLineNumberMargin.LeftPadding, 1);
        BookmarkMargin bookmarks = editor.TextArea.LeftMargins.OfType<BookmarkMargin>().Single();
        bookmarks.TranslatePoint(new Point(0, 0), window)!.Value.X
            .Should().BeApproximately(numbers.TranslatePoint(new Point(numbers.Bounds.Width, 0), window)!.Value.X, 1, "the column starts where the numbers end");
        window.Close();
    }

    // The ribbons the column draws (the bounds of each), taken from what it renders into a recording drawing context: the
    // headless platform of the tests draws nothing to look at.
    private static System.Collections.Generic.List<Rect> RibbonsIn(BookmarkMargin margin)
    {
        DrawingGroup drawing = new();
        using (DrawingContext context = drawing.Open())
        {
            margin.Render(context);
        }

        return drawing.Children.OfType<GeometryDrawing>().Where(d => ReferenceEquals(d.Brush, margin.MarkerBrush)).Select(d => d.Geometry!.Bounds).ToList();
    }
}
