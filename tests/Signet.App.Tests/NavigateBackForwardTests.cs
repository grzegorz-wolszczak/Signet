using System;
using System.Collections.Generic;
using System.Linq;
using AwesomeAssertions;
using Signet.App.Actions;
using Signet.App.ViewModels;
using Signet.App.ViewModels.Tabs;
using Signet.Core.BookManipulation;
using Signet.Core.Resources;
using Signet.Core.Tests.TestSupport;
using Xunit;

namespace Signet.App.Tests;

/// <summary>
/// Navigate Back / Forward in the application: tab switches and jumps in Code View enter the history, the actions
/// walk it, deleted files are skipped and renamed ones followed.
/// </summary>
public sealed class NavigateBackForwardTests : IDisposable
{
    private readonly TempDir _temp = new();
    private readonly MainWindowViewModel _sut;
    private readonly Book _book;
    private readonly List<Action> _commandEnds = new();

    public NavigateBackForwardTests()
    {
        string epub = EpubBuilder.BuildInto(CorpusPaths.Epub3WithNcx, _temp);
        _sut = new MainWindowViewModel();
        _book = new ImportEpub(epub).GetBook();
        _sut.LoadBook(_book, epub);
        // The UI dispatcher stand-in: a user action ends when the test calls EndCommand.
        _sut.NavigationCommandScheduler = action =>
        {
            _commandEnds.Add(action);
            return true;
        };
    }

    public void Dispose()
    {
        _sut.CheckpointHistory.Dispose();
        _temp.Dispose();
    }

    private HtmlResource Chapter(int number) =>
        _book.GetHtmlResources().First(h => h.Filename == $"chapter{number}.xhtml");

    private Resource Stylesheet => _book.GetAllResources().OfType<CssResource>().First();

    private void EndCommand()
    {
        List<Action> ends = new(_commandEnds);
        _commandEnds.Clear();
        ends.ForEach(a => a());
    }

    // Opens the file in a tab as one user action.
    private void Open(Resource resource)
    {
        _sut.Tabs.OpenResources(new[] { resource });
        EndCommand();
    }

    [Fact]
    public void Back_and_forward_walk_the_tab_switches()
    {
        Open(Chapter(1));
        Open(Chapter(2));
        Open(Stylesheet);

        _sut.Actions.Require(AppActionIds.NavigateBack).Execute(null);
        _sut.Tabs.ActiveTab!.Resource.Should().BeSameAs(Chapter(2));

        _sut.Actions.Require(AppActionIds.NavigateBack).Execute(null);
        _sut.Tabs.ActiveTab!.Resource.Should().BeSameAs(Chapter(1));

        _sut.Actions.Require(AppActionIds.NavigateForward).Execute(null);
        _sut.Tabs.ActiveTab!.Resource.Should().BeSameAs(Chapter(2));
    }

    [Fact]
    public void A_jump_inside_a_file_returns_the_caret_to_where_it_was()
    {
        Open(Chapter(1));
        var tab = (CodeTabViewModel)_sut.Tabs.ActiveTab!;
        tab.Document.Text = string.Join("\n", Enumerable.Range(1, 60).Select(i => $"<p>{i}</p>"));
        tab.UpdateCaret(2, 1, tab.Document.GetLineByNumber(2).Offset, -1);
        int far = tab.Document.GetLineByNumber(40).Offset;

        tab.GoToLine(40);
        tab.UpdateCaret(40, 1, far, -1);
        EndCommand();
        (int Start, int End)? selected = null;
        tab.SearchResultRequested += (start, end, _) => selected = (start, end);

        _sut.NavigateBack();

        selected.Should().Be((tab.Document.GetLineByNumber(2).Offset, tab.Document.GetLineByNumber(2).Offset));
        _sut.NavigationHistory.ForwardPlaces.Should().ContainSingle().Which.Line.Should().Be(40);
    }

    [Fact]
    public void Back_skips_a_file_that_was_deleted()
    {
        Open(Chapter(1));
        Open(Chapter(2));
        Open(Stylesheet);
        _book.GetFolderKeeper().BulkRemoveResources(new Resource[] { Chapter(2) });

        _sut.NavigateBack();

        _sut.Tabs.ActiveTab!.Resource.Should().BeSameAs(Chapter(1));
        _sut.NavigationHistory.CanGoBack.Should().BeFalse();
    }

    [Fact]
    public void Back_follows_a_renamed_file()
    {
        HtmlResource chapter = Chapter(1);
        Open(chapter);
        Open(Chapter(2));
        chapter.RenameTo("renamed.xhtml").Should().BeTrue();

        _sut.NavigateBack();

        _sut.Tabs.ActiveTab!.Resource.Should().BeSameAs(chapter);
        _sut.Tabs.ActiveTab!.Resource.Filename.Should().Be("renamed.xhtml");
    }

    [Fact]
    public void The_actions_are_enabled_only_when_there_is_a_place_to_go_to()
    {
        AppAction back = _sut.Actions.Require(AppActionIds.NavigateBack);
        AppAction forward = _sut.Actions.Require(AppActionIds.NavigateForward);
        back.IsEnabled.Should().BeFalse();
        forward.IsEnabled.Should().BeFalse();

        Open(Chapter(1));
        Open(Chapter(2));
        back.IsEnabled.Should().BeTrue();

        back.Execute(null);
        forward.IsEnabled.Should().BeTrue();
    }

    [Fact]
    public void Loading_another_book_starts_an_empty_history()
    {
        Open(Chapter(1));
        Open(Chapter(2));
        string other = EpubBuilder.BuildInto(CorpusPaths.Epub3Media, _temp);

        _sut.LoadBook(new ImportEpub(other).GetBook(), other);

        _sut.NavigationHistory.CanGoBack.Should().BeFalse();
    }

    [Fact]
    public void The_navigation_actions_have_no_default_shortcut_and_the_old_back_action_is_gone()
    {
        _sut.Actions.Require(AppActionIds.NavigateBack).Gesture.Should().BeNull();
        _sut.Actions.Require(AppActionIds.NavigateForward).Gesture.Should().BeNull();
        _sut.Actions.Get("MainWindow.GoBackFromLinkOrStyle").Should().BeNull();
    }
}
