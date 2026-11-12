using System;
using System.Collections.Generic;
using System.Linq;
using AwesomeAssertions;
using Signet.App.Actions;
using Signet.App.Resources;
using Signet.App.ViewModels;
using Signet.App.ViewModels.Tabs;
using Signet.Core.BookManipulation;
using Signet.Core.Resources;
using Signet.Core.Tests.TestSupport;
using Xunit;

namespace Signet.App.Tests;

/// <summary>
/// The actions of the Edit, Insert and Window menus that work on the text and the files of the book: Cut, Copy, Paste,
/// Delete Line, Insert Role and Next / Previous File.
/// </summary>
public sealed class EditActionsTests : IDisposable
{
    private const string Body = "<p>see <a href=\"n.xhtml#n1\">1</a> and <em>this</em></p>";

    private readonly Disposables _disposables = new();

    /// <inheritdoc />
    public void Dispose() => _disposables.Dispose();

    private (MainWindowViewModel Sut, Book Book) Loaded(string corpus)
    {
        TempDir temp = _disposables.Add(new TempDir());
        string epub = EpubBuilder.BuildInto(corpus, temp);
        MainWindowViewModel sut = _disposables.Add(new MainWindowViewModel());
        Book book = _disposables.Add(new ImportEpub(epub).GetBook());
        sut.LoadBook(book, epub);
        return (sut, book);
    }

    private static CodeTabViewModel OpenChapter(MainWindowViewModel sut, Book book, string fileName = "chapter1.xhtml")
    {
        HtmlResource chapter = book.GetHtmlResources().First(h => h.Filename == fileName);
        sut.Tabs.OpenResources(new Resource[] { chapter });
        return (CodeTabViewModel)sut.Tabs.ActiveTab!;
    }

    private static void PlaceCaret(CodeTabViewModel tab, int offset)
    {
        tab.UpdateCaret(1, 1, offset, 'x');
        tab.UpdateSelection(offset, offset);
    }

    // ------------------------------------------------------- Cut, Copy, Paste --- //

    [Fact]
    public void Cut_and_Copy_need_a_selection_and_Paste_and_Delete_Line_only_a_code_tab()
    {
        // Arrange
        (MainWindowViewModel sut, Book book) = Loaded(CorpusPaths.Epub3WithNcx);
        CodeTabViewModel tab = OpenChapter(sut, book);
        PlaceCaret(tab, 0);

        // Assert
        sut.Actions.Require(AppActionIds.Cut).IsEnabled.Should().BeFalse();
        sut.Actions.Require(AppActionIds.Copy).IsEnabled.Should().BeFalse();
        sut.Actions.Require(AppActionIds.Paste).IsEnabled.Should().BeTrue();
        sut.Actions.Require(AppActionIds.DeleteLine).IsEnabled.Should().BeTrue();

        // Act
        tab.UpdateSelection(0, 5);

        // Assert
        sut.Actions.Require(AppActionIds.Cut).IsEnabled.Should().BeTrue();
        sut.Actions.Require(AppActionIds.Copy).IsEnabled.Should().BeTrue();

        // Act
        tab.UpdateSelection(5, 5);

        // Assert
        sut.Actions.Require(AppActionIds.Cut).IsEnabled.Should().BeFalse("the selection is gone again");
    }

    [Fact]
    public void The_clipboard_actions_ask_the_active_editor_to_do_it()
    {
        // Arrange
        (MainWindowViewModel sut, Book book) = Loaded(CorpusPaths.Epub3WithNcx);
        CodeTabViewModel tab = OpenChapter(sut, book);
        tab.UpdateSelection(0, 5);
        List<EditorClipboardCommand> requested = new();
        tab.ClipboardRequested += requested.Add;

        // Act
        sut.Actions.Require(AppActionIds.Cut).Execute(null);
        sut.Actions.Require(AppActionIds.Copy).Execute(null);
        sut.Actions.Require(AppActionIds.Paste).Execute(null);

        // Assert
        requested.Should().Equal(EditorClipboardCommand.Cut, EditorClipboardCommand.Copy, EditorClipboardCommand.Paste);
    }

    // ------------------------------------------------------------ Delete Line --- //

    private static CodeTabViewModel TabWith(string text, out MainWindowViewModel sut, EditActionsTests host)
    {
        (sut, Book book) = host.Loaded(CorpusPaths.Epub3WithNcx);
        CodeTabViewModel tab = OpenChapter(sut, book);
        tab.Document.Text = text;
        return tab;
    }

    [Theory]
    [InlineData("one\ntwo\nthree\nfour", 5, 5, "one\nthree\nfour")] // the caret line
    [InlineData("one\ntwo\nthree\nfour", 5, 10, "one\nfour")] // the selection touches two lines
    [InlineData("one\ntwo\nthree\nfour", 5, 8, "one\nthree\nfour")] // a selection that ends at the start of a line does not touch it
    [InlineData("one\ntwo\nthree\nfour", 0, 0, "two\nthree\nfour")] // the first line
    [InlineData("one\ntwo\nthree\nfour", 16, 16, "one\ntwo\nthree")] // the last line takes the break before it
    [InlineData("one", 1, 1, "")] // the only line
    public void Delete_Line_deletes_the_lines_of_the_caret_or_the_selection(string text, int start, int end, string expected)
    {
        // Arrange
        CodeTabViewModel tab = TabWith(text, out _, this);
        tab.UpdateSelection(start, end);

        // Act
        tab.DeleteLines();

        // Assert
        tab.Document.Text.Should().Be(expected);
    }

    [Fact]
    public void Delete_Line_is_one_undo_step_and_puts_the_caret_where_the_line_was()
    {
        // Arrange
        CodeTabViewModel tab = TabWith("one\ntwo\nthree", out _, this);
        tab.Document.UndoStack.ClearAll();
        tab.UpdateSelection(5, 9);
        (int Start, int End)? requested = null;
        tab.SelectionRequested += (start, end) => requested = (start, end);

        // Act
        tab.DeleteLines();

        // Assert
        tab.Document.Text.Should().Be("one");
        requested.Should().Be((3, 3));

        // Act
        tab.Document.UndoStack.Undo();

        // Assert
        tab.Document.Text.Should().Be("one\ntwo\nthree");
    }

    // ------------------------------------------------------------ Insert Role --- //

    [Fact]
    public void Insert_Role_gives_the_element_at_the_caret_the_role_and_its_epub_type()
    {
        // Arrange
        (MainWindowViewModel sut, Book book) = Loaded(CorpusPaths.Epub3WithNcx);
        CodeTabViewModel tab = OpenChapter(sut, book);
        tab.Document.Text = "<?xml version=\"1.0\"?>\n<html xmlns=\"http://www.w3.org/1999/xhtml\"><head><title>t</title></head>\n<body>" + Body + "</body></html>";
        PlaceCaret(tab, tab.Document.Text.IndexOf("\">1<", StringComparison.Ordinal) + 3);
        AppAction action = sut.Actions.Require(AppActionIds.InsertRole);
        int requests = 0;
        sut.InsertRoleRequested += (_, _) => requests++;

        // Assert
        action.IsEnabled.Should().BeTrue();
        sut.GetRoleOptionsAtCaret().Keys.Should().Contain("doc-noteref");

        // Act
        action.Execute(null);
        sut.ApplyRole("doc-noteref");

        // Assert
        requests.Should().Be(1);
        tab.Document.Text.Should().Contain("<a href=\"n.xhtml#n1\" epub:type=\"noteref\" role=\"doc-noteref\">1</a>");
    }

    [Fact]
    public void Insert_Role_is_off_where_no_role_fits_the_element()
    {
        // Arrange
        (MainWindowViewModel sut, Book book) = Loaded(CorpusPaths.Epub3WithNcx);
        CodeTabViewModel tab = OpenChapter(sut, book);
        tab.Document.Text = "<?xml version=\"1.0\"?>\n<html xmlns=\"http://www.w3.org/1999/xhtml\"><head><title>t</title></head>\n<body>" + Body + "</body></html>";

        // Act
        PlaceCaret(tab, tab.Document.Text.IndexOf("this", StringComparison.Ordinal) + 1);

        // Assert
        sut.Actions.Require(AppActionIds.InsertRole).IsEnabled.Should().BeFalse("no role fits <em>");
        sut.GetRoleOptionsAtCaret().Should().BeEmpty();
    }

    [Fact]
    public void Insert_Role_is_off_in_an_epub_2_book()
    {
        // Arrange
        (MainWindowViewModel sut, Book book) = Loaded(CorpusPaths.Epub2Minimal);
        HtmlResource chapter = book.GetHtmlResources().First();
        sut.Tabs.OpenResources(new Resource[] { chapter });
        var tab = (CodeTabViewModel)sut.Tabs.ActiveTab!;
        tab.Document.Text = "<?xml version=\"1.0\"?>\n<html xmlns=\"http://www.w3.org/1999/xhtml\"><head><title>t</title></head>\n<body>" + Body + "</body></html>";

        // Act
        PlaceCaret(tab, tab.Document.Text.IndexOf("\">1<", StringComparison.Ordinal) + 3);

        // Assert
        tab.InsertRoleEnabled.Should().BeTrue("a role fits the link");
        sut.Actions.Require(AppActionIds.InsertRole).IsEnabled.Should().BeFalse("the role attribute is not valid in EPUB 2");
    }

    // ---------------------------------------------------- Next / Previous File --- //

    [Fact]
    public void Next_and_Previous_File_walk_the_reading_order_and_stop_at_its_ends()
    {
        // Arrange
        (MainWindowViewModel sut, Book book) = Loaded(CorpusPaths.Epub3WithNcx);
        OpfResource opf = book.GetOpf();
        List<Resource> spine = book.GetAllResources().Where(r => opf.GetReadingOrder(r) >= 0).OrderBy(opf.GetReadingOrder).ToList();
        spine.Count.Should().BeGreaterThan(1);
        sut.Tabs.OpenResources(new[] { spine[0] });

        // Act
        sut.Actions.Require(AppActionIds.PreviousResource).Execute(null);

        // Assert
        sut.Tabs.ActiveTab!.Resource.Should().BeSameAs(spine[0], "there is nothing before the first file");
        sut.StatusMessage.Should().Be(Strings.Get("Status_NoPreviousFile"));

        // Act
        sut.Actions.Require(AppActionIds.NextResource).Execute(null);

        // Assert
        sut.Tabs.ActiveTab!.Resource.Should().BeSameAs(spine[1]);

        // Act
        for (int i = 1; i < spine.Count; i++)
        {
            sut.Actions.Require(AppActionIds.NextResource).Execute(null);
        }

        // Assert
        sut.Tabs.ActiveTab!.Resource.Should().BeSameAs(spine[^1]);
        sut.StatusMessage.Should().Be(Strings.Get("Status_NoNextFile"));

        // Act
        sut.Actions.Require(AppActionIds.PreviousResource).Execute(null);

        // Assert
        sut.Tabs.ActiveTab!.Resource.Should().BeSameAs(spine[^2]);
    }

    [Fact]
    public void A_file_outside_the_reading_order_has_no_next_file()
    {
        // Arrange
        (MainWindowViewModel sut, Book book) = Loaded(CorpusPaths.Epub3WithNcx);
        CssResource css = book.GetCssResources().First();
        sut.Tabs.OpenResources(new Resource[] { css });

        // Act
        sut.Actions.Require(AppActionIds.NextResource).Execute(null);

        // Assert
        sut.Tabs.ActiveTab!.Resource.Should().BeSameAs(css);
        sut.StatusMessage.Should().Be(Strings.Get("Status_FileNotInReadingOrder"));
    }
}
