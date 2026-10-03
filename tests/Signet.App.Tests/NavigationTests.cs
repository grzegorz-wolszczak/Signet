using System.Linq;
using AwesomeAssertions;
using Signet.App.Actions;
using Signet.App.ViewModels;
using Signet.App.ViewModels.Tabs;
using Signet.Core.BookManipulation;
using Signet.Core.Resources;
using Signet.Core.Tests.TestSupport;
using Signet.App.Tests.TestSupport;
using Xunit;

namespace Signet.App.Tests;

/// <summary>
/// Navigation tests: Go To Line, bookmarks (bookmark round-trip), the "Table Of Contents" panel,
/// "Focus on…" messages, focus-dependent zoom.
/// </summary>
public sealed class NavigationTests
{
    private static MainWindowViewModel New() => new();

    private static (MainWindowViewModel Sut, Book Book) Loaded(TempDir temp, string corpus)
    {
        string epub = EpubBuilder.BuildInto(corpus, temp);
        MainWindowViewModel sut = New();
        Book book = new ImportEpub(epub).GetBook();
        sut.LoadBook(book, epub);
        return (sut, book);
    }

    [Fact]
    public void GoToLine_scrolls_the_active_code_tab()
    {
        using TempDir temp = new();
        (MainWindowViewModel sut, Book book) = Loaded(temp, CorpusPaths.Epub3Media);
        sut.Tabs.OpenResources(new Resource[] { book.GetAllResources().OfType<HtmlResource>().First() });
        var tab = (CodeTabViewModel)sut.Tabs.ActiveTab!;
        int? requested = null;
        tab.ScrollToLineRequested += l => requested = l;

        sut.GoToLine(3);

        requested.Should().Be(3);
    }

    [Fact]
    public void Bookmark_survives_a_tab_switch_and_back_returns_to_it()
    {
        using TempDir temp = new();
        (MainWindowViewModel sut, Book book) = Loaded(temp, CorpusPaths.Epub3WithNcx);
        HtmlResource chapter1 = book.GetHtmlResources().First(h => h.Filename == "chapter1.xhtml");
        HtmlResource chapter2 = book.GetHtmlResources().First(h => h.Filename == "chapter2.xhtml");

        sut.Tabs.OpenResources(new Resource[] { chapter1 });
        var tab1 = (CodeTabViewModel)sut.Tabs.ActiveTab!;
        tab1.UpdateCaret(2, 1, 5, 'x');
        sut.Actions.Require(AppActionIds.BookmarkLocation).Execute(null);

        sut.Bookmarks.Should().ContainSingle();
        sut.Bookmarks[0].BookPath.Should().Be(chapter1.BookPath);

        sut.Tabs.OpenResources(new Resource[] { chapter2 });
        sut.Tabs.ActiveTab!.Resource.BookPath.Should().Be(chapter2.BookPath);

        sut.Actions.Require(AppActionIds.GoBackFromLinkOrStyle).Execute(null);

        sut.Tabs.ActiveTab!.Resource.BookPath.Should().Be(chapter1.BookPath);
    }

    [Fact]
    public void Table_of_contents_panel_is_populated_from_the_book_and_navigates_on_activate()
    {
        using TempDir temp = new();
        (MainWindowViewModel sut, Book book) = Loaded(temp, CorpusPaths.Epub3WithNcx);

        sut.TableOfContents.HasEntries.Should().BeTrue();
        sut.TableOfContents.Nodes.Select(n => n.Title).Should().Equal("Chapter 1", "Chapter 2");

        sut.TableOfContents.Activate(sut.TableOfContents.Nodes[1]);

        sut.Tabs.ActiveTab!.Resource.Filename.Should().Be("chapter2.xhtml");
    }

    [Fact]
    public void Focus_action_reports_the_target_panel_on_the_status_bar()
    {
        using UiCultureScope culture = new("en");
        MainWindowViewModel sut = New();

        sut.Actions.Require(AppActionIds.FocusPreview).Execute(null);

        sut.StatusMessage.Should().Contain("Preview");
    }

    [Fact]
    public void Zoom_targets_the_code_view_by_default_and_not_after_focusing_preview()
    {
        using TempDir temp = new();
        (MainWindowViewModel sut, Book book) = Loaded(temp, CorpusPaths.Epub3Media);
        sut.Tabs.OpenResources(new Resource[] { book.GetAllResources().OfType<HtmlResource>().First() });
        var tab = (CodeTabViewModel)sut.Tabs.ActiveTab!;

        sut.Actions.Require(AppActionIds.ZoomIn).Execute(null);
        tab.ZoomFactor.Should().BeGreaterThan(1.0);

        double afterFirst = tab.ZoomFactor;
        sut.Actions.Require(AppActionIds.FocusPreview).Execute(null);
        sut.Actions.Require(AppActionIds.ZoomIn).Execute(null);

        tab.ZoomFactor.Should().Be(afterFirst);
    }
}
