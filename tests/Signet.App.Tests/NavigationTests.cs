using System;
using System.Linq;
using AwesomeAssertions;
using Signet.App.Actions;
using Signet.App.Tests.TestSupport;
using Signet.App.ViewModels;
using Signet.App.ViewModels.Tabs;
using Signet.Core.BookManipulation;
using Signet.Core.Misc;
using Signet.Core.Resources;
using Signet.Core.Tests.TestSupport;
using Xunit;

namespace Signet.App.Tests;

/// <summary>
/// Navigation tests: Go To Line, bookmarks (bookmark round-trip), the "Table Of Contents" panel,
/// "Focus on…" messages, focus-dependent zoom.
/// </summary>
public sealed class NavigationTests : IDisposable
{
    private readonly Disposables _disposables = new();

    /// <inheritdoc />
    public void Dispose() => _disposables.Dispose();

    private MainWindowViewModel New() => _disposables.Add(new MainWindowViewModel());

    private (MainWindowViewModel Sut, Book Book) Loaded(TempDir temp, string corpus)
    {
        string epub = EpubBuilder.BuildInto(corpus, temp);
        MainWindowViewModel sut = New();
        Book book = _disposables.Add(new ImportEpub(epub).GetBook());
        sut.LoadBook(book, epub);
        return (sut, book);
    }

    [Fact]
    public void GoToLine_scrolls_the_active_code_tab()
    {
        // Arrange
        TempDir temp = _disposables.Add(new TempDir());
        (MainWindowViewModel sut, Book book) = Loaded(temp, CorpusPaths.Epub3Media);
        sut.Tabs.OpenResources(new Resource[] { book.GetAllResources().OfType<HtmlResource>().First() });
        var tab = (CodeTabViewModel)sut.Tabs.ActiveTab!;
        int? requested = null;
        tab.ScrollToLineRequested += l => requested = l;

        // Act
        sut.GoToLine(3);

        // Assert
        requested.Should().Be(3);
    }

    [Fact]
    public void Bookmark_survives_a_tab_switch_and_the_panel_returns_to_it()
    {
        // Arrange
        TempDir temp = _disposables.Add(new TempDir());
        (MainWindowViewModel sut, Book book) = Loaded(temp, CorpusPaths.Epub3WithNcx);
        HtmlResource chapter1 = book.GetHtmlResources().First(h => h.Filename == "chapter1.xhtml");
        HtmlResource chapter2 = book.GetHtmlResources().First(h => h.Filename == "chapter2.xhtml");

        sut.Tabs.OpenResources(new Resource[] { chapter1 });
        var tab1 = (CodeTabViewModel)sut.Tabs.ActiveTab!;
        tab1.UpdateCaret(2, 1, 5, 'x');

        // Act
        sut.Actions.Require(AppActionIds.BookmarkLocation).Execute(null);

        // Assert
        sut.Bookmarks.Items.Should().ContainSingle();
        sut.Bookmarks.Items[0].BookPath.Should().Be(chapter1.BookPath);
        sut.Bookmarks.Items[0].Line.Should().Be(2);

        // Act
        sut.Tabs.OpenResources(new Resource[] { chapter2 });

        // Assert
        sut.Tabs.ActiveTab!.Resource.BookPath.Should().Be(chapter2.BookPath);

        // Act
        sut.BookmarksPanel.Activate(sut.BookmarksPanel.Roots[0].Children[0]);

        // Assert
        sut.Tabs.ActiveTab!.Resource.BookPath.Should().Be(chapter1.BookPath);
    }

    [Fact]
    public void Toggle_Bookmark_adds_the_bookmark_on_the_caret_line_and_removes_it_the_second_time()
    {
        // Arrange
        TempDir temp = _disposables.Add(new TempDir());
        (MainWindowViewModel sut, Book book) = Loaded(temp, CorpusPaths.Epub3WithNcx);
        HtmlResource chapter1 = book.GetHtmlResources().First(h => h.Filename == "chapter1.xhtml");
        sut.Tabs.OpenResources(new Resource[] { chapter1 });
        var tab = (CodeTabViewModel)sut.Tabs.ActiveTab!;
        tab.UpdateCaret(3, 1, 10, 'x');

        // Act
        sut.Actions.Require(AppActionIds.BookmarkLocation).Execute(null);

        // Assert
        sut.Bookmarks.Find(chapter1.BookPath, 3).Should().NotBeNull();

        // Act
        sut.Actions.Require(AppActionIds.BookmarkLocation).Execute(null);

        // Assert
        sut.Bookmarks.Items.Should().BeEmpty();
    }

    [Fact]
    public void A_bookmark_follows_its_line_while_the_file_is_open_in_a_tab()
    {
        // Arrange
        TempDir temp = _disposables.Add(new TempDir());
        (MainWindowViewModel sut, Book book) = Loaded(temp, CorpusPaths.Epub3WithNcx);
        HtmlResource chapter1 = book.GetHtmlResources().First(h => h.Filename == "chapter1.xhtml");
        sut.Tabs.OpenResources(new Resource[] { chapter1 });
        var tab = (CodeTabViewModel)sut.Tabs.ActiveTab!;
        tab.UpdateCaret(3, 1, 10, 'x');
        sut.Actions.Require(AppActionIds.BookmarkLocation).Execute(null);

        // Act
        tab.Document.Insert(0, "<!-- one -->\n<!-- two -->\n");

        // Assert
        sut.Bookmarks.Items.Should().ContainSingle().Which.Line.Should().Be(5);
        sut.BookmarksPanel.Roots[0].Children[0].Text.Should().StartWith("5:");
    }

    [Fact]
    public void Next_and_Previous_Bookmark_move_the_caret_between_the_bookmarks_of_the_file()
    {
        // Arrange
        TempDir temp = _disposables.Add(new TempDir());
        (MainWindowViewModel sut, Book book) = Loaded(temp, CorpusPaths.Epub3WithNcx);
        HtmlResource chapter1 = book.GetHtmlResources().First(h => h.Filename == "chapter1.xhtml");
        sut.Tabs.OpenResources(new Resource[] { chapter1 });
        var tab = (CodeTabViewModel)sut.Tabs.ActiveTab!;
        sut.Bookmarks.Toggle(chapter1.BookPath, 2);
        sut.Bookmarks.Toggle(chapter1.BookPath, 6);
        System.Collections.Generic.List<int> requested = new();
        tab.ScrollToLineRequested += requested.Add;
        tab.UpdateCaret(3, 1, 10, 'x');

        // Act
        sut.Actions.Require(AppActionIds.NextBookmark).Execute(null);
        tab.UpdateCaret(6, 1, 50, 'x');
        sut.Actions.Require(AppActionIds.NextBookmark).Execute(null);
        sut.Actions.Require(AppActionIds.PreviousBookmark).Execute(null);

        // Assert
        requested.Should().Equal(6, 2, 2);
    }

    [Fact]
    public void Next_Bookmark_says_so_when_the_file_has_no_other_bookmark()
    {
        // Arrange
        TempDir temp = _disposables.Add(new TempDir());
        (MainWindowViewModel sut, Book book) = Loaded(temp, CorpusPaths.Epub3WithNcx);
        HtmlResource chapter1 = book.GetHtmlResources().First(h => h.Filename == "chapter1.xhtml");
        sut.Tabs.OpenResources(new Resource[] { chapter1 });
        var tab = (CodeTabViewModel)sut.Tabs.ActiveTab!;
        int requests = 0;
        tab.ScrollToLineRequested += _ => requests++;

        // Act
        sut.Actions.Require(AppActionIds.NextBookmark).Execute(null);

        // Assert
        requests.Should().Be(0);
    }

    [Fact]
    public void The_bookmarks_of_a_book_come_back_when_it_is_opened_again()
    {
        // Arrange
        TempDir temp = _disposables.Add(new TempDir());
        string epub = EpubBuilder.BuildInto(CorpusPaths.Epub3WithNcx, temp);
        SettingsStore settings = new(temp.Combine("settings.xml"));
        settings.RecentFiles = new[] { epub };
        MainWindowViewModel first = MainWindowFactory.On(settings, _disposables);
        first.LoadBook(_disposables.Add(new ImportEpub(epub).GetBook()), epub);
        first.Bookmarks.Toggle("EPUB/text/chapter1.xhtml", 4);
        first.Bookmarks.Toggle("EPUB/text/chapter2.xhtml", 9);

        // Act
        first.PersistState();
        MainWindowViewModel second = MainWindowFactory.On(settings, _disposables);
        second.LoadBook(_disposables.Add(new ImportEpub(epub).GetBook()), epub);

        // Assert
        second.Bookmarks.Places.Select(p => (p.BookPath, p.Line)).Should().Equal(("EPUB/text/chapter1.xhtml", 4), ("EPUB/text/chapter2.xhtml", 9));
    }

    [Fact]
    public void A_bookmark_of_a_file_that_is_no_longer_in_the_book_is_dropped_when_it_is_used()
    {
        // Arrange
        TempDir temp = _disposables.Add(new TempDir());
        (MainWindowViewModel sut, _) = Loaded(temp, CorpusPaths.Epub3WithNcx);
        Bookmark gone = sut.Bookmarks.Toggle("EPUB/text/missing.xhtml", 2)!;

        // Act
        sut.BookmarksPanel.Activate(sut.BookmarksPanel.Roots[0].Children[0]);

        // Assert
        sut.Bookmarks.Items.Should().NotContain(gone);
    }

    [Fact]
    public void Show_Bookmarks_opens_the_popup_with_the_bookmarks_and_goes_to_the_chosen_one()
    {
        // Arrange
        TempDir temp = _disposables.Add(new TempDir());
        (MainWindowViewModel sut, Book book) = Loaded(temp, CorpusPaths.Epub3WithNcx);
        HtmlResource chapter1 = book.GetHtmlResources().First(h => h.Filename == "chapter1.xhtml");
        HtmlResource chapter2 = book.GetHtmlResources().First(h => h.Filename == "chapter2.xhtml");
        sut.Tabs.OpenResources(new Resource[] { chapter1 });
        sut.Bookmarks.Toggle(chapter2.BookPath, 3);
        BookmarksPopupViewModel? popup = null;
        sut.BookmarksPopupRequested += (_, model) => popup = model;

        // Act
        sut.Actions.Require(AppActionIds.ShowBookmarks).Execute(null);

        // Assert
        popup.Should().NotBeNull();
        popup!.Items.Should().ContainSingle().Which.Label.Should().Be("chapter2.xhtml :3");
        popup.PreviewText.Should().Be(chapter2.GetText(), "the preview is the file of the bookmark");
        popup.PreviewSyntax.Should().Be(Signet.Core.MainUI.CodeEditorSyntax.Html);

        // Act
        popup.Navigate(popup.Items[0]);

        // Assert
        sut.Tabs.ActiveTab!.Resource.BookPath.Should().Be(chapter2.BookPath);
    }

    [Fact]
    public void Show_Bookmarks_opens_the_popup_without_any_bookmark_too()
    {
        // Arrange
        TempDir temp = _disposables.Add(new TempDir());
        (MainWindowViewModel sut, _) = Loaded(temp, CorpusPaths.Epub3WithNcx);
        BookmarksPopupViewModel? popup = null;
        sut.BookmarksPopupRequested += (_, model) => popup = model;

        // Act
        sut.Actions.Require(AppActionIds.ShowBookmarks).Execute(null);

        // Assert
        popup.Should().NotBeNull();
        popup!.HasItems.Should().BeFalse();
        popup.EmptyHint.Should().NotBeEmpty();
    }

    [Fact]
    public void The_bookmarks_panel_has_an_action_that_shows_it()
    {
        // Arrange
        TempDir temp = _disposables.Add(new TempDir());
        (MainWindowViewModel sut, _) = Loaded(temp, CorpusPaths.Epub3WithNcx);

        // Act
        sut.Actions.Require(AppActionIds.ToggleBookmarks).Execute(null);

        // Assert
        ((Signet.App.Docking.MainDockFactory)sut.DockFactory).IsToolVisible(Signet.App.Docking.DockableIds.Bookmarks).Should().BeTrue();
    }

    [Fact]
    public void Table_of_contents_panel_is_populated_from_the_book_and_navigates_on_activate()
    {
        // Arrange
        TempDir temp = _disposables.Add(new TempDir());
        (MainWindowViewModel sut, Book book) = Loaded(temp, CorpusPaths.Epub3WithNcx);

        // Act & Assert
        sut.TableOfContents.HasEntries.Should().BeTrue();
        sut.TableOfContents.Nodes.Select(n => n.Title).Should().Equal("Chapter 1", "Chapter 2");

        // Act
        sut.TableOfContents.Activate(sut.TableOfContents.Nodes[1]);

        // Assert
        sut.Tabs.ActiveTab!.Resource.Filename.Should().Be("chapter2.xhtml");
    }

    [Fact]
    public void Focus_action_reports_the_target_panel_on_the_status_bar()
    {
        // Arrange
        using UiCultureScope culture = new("en");
        MainWindowViewModel sut = New();

        // Act
        sut.Actions.Require(AppActionIds.FocusPreview).Execute(null);

        // Assert
        sut.StatusMessage.Should().Contain("Preview");
    }

    [Fact]
    public void Zoom_targets_the_code_editor_by_default_and_not_after_focusing_preview()
    {
        // Arrange
        TempDir temp = _disposables.Add(new TempDir());
        (MainWindowViewModel sut, Book book) = Loaded(temp, CorpusPaths.Epub3Media);
        sut.Tabs.OpenResources(new Resource[] { book.GetAllResources().OfType<HtmlResource>().First() });
        var tab = (CodeTabViewModel)sut.Tabs.ActiveTab!;

        // Act
        sut.Actions.Require(AppActionIds.ZoomIn).Execute(null);

        // Assert
        tab.ZoomFactor.Should().BeGreaterThan(1.0);

        double afterFirst = tab.ZoomFactor;

        // Act
        sut.Actions.Require(AppActionIds.FocusPreview).Execute(null);
        sut.Actions.Require(AppActionIds.ZoomIn).Execute(null);

        // Assert
        tab.ZoomFactor.Should().Be(afterFirst);
    }
}
