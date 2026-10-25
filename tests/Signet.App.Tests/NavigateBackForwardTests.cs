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
/// Navigate Back / Forward in the application: tab switches and jumps in Code Editor enter the history, the actions
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
        _sut.Dispose();
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
        // Arrange
        Open(Chapter(1));
        Open(Chapter(2));
        Open(Stylesheet);

        // Act
        _sut.Actions.Require(AppActionIds.NavigateBack).Execute(null);

        // Assert
        _sut.Tabs.ActiveTab!.Resource.Should().BeSameAs(Chapter(2));

        // Act
        _sut.Actions.Require(AppActionIds.NavigateBack).Execute(null);

        // Assert
        _sut.Tabs.ActiveTab!.Resource.Should().BeSameAs(Chapter(1));

        // Act
        _sut.Actions.Require(AppActionIds.NavigateForward).Execute(null);

        // Assert
        _sut.Tabs.ActiveTab!.Resource.Should().BeSameAs(Chapter(2));
    }

    [Fact]
    public void A_jump_inside_a_file_returns_the_caret_to_where_it_was()
    {
        // Arrange
        Open(Chapter(1));
        var tab = (CodeTabViewModel)_sut.Tabs.ActiveTab!;
        tab.Document.Text = string.Join("\n", Enumerable.Range(1, 60).Select(i => $"<p>{i}</p>"));
        tab.UpdateCaret(2, 1, tab.Document.GetLineByNumber(2).Offset, -1);
        int far = tab.Document.GetLineByNumber(40).Offset;

        // Act
        tab.GoToLine(40);
        tab.UpdateCaret(40, 1, far, -1);
        EndCommand();
        // Going back is a jump: the caret lands where it was and the line is centred (JumpRequested).
        (int Start, int End)? selected = null;
        tab.JumpRequested += (start, end) => selected = (start, end);

        _sut.NavigateBack();

        // Assert
        selected.Should().Be((tab.Document.GetLineByNumber(2).Offset, tab.Document.GetLineByNumber(2).Offset));
        _sut.NavigationHistory.ForwardPlaces.Should().ContainSingle().Which.Line.Should().Be(40);
    }

    [Fact]
    public void Back_skips_a_file_that_was_deleted()
    {
        // Arrange
        Open(Chapter(1));
        Open(Chapter(2));
        Open(Stylesheet);
        _book.GetFolderKeeper().BulkRemoveResources(new Resource[] { Chapter(2) });

        // Act
        _sut.NavigateBack();

        // Assert
        _sut.Tabs.ActiveTab!.Resource.Should().BeSameAs(Chapter(1));
        _sut.NavigationHistory.CanGoBack.Should().BeFalse();
    }

    [Fact]
    public void Back_follows_a_renamed_file()
    {
        // Arrange
        HtmlResource chapter = Chapter(1);
        Open(chapter);
        Open(Chapter(2));

        // Act & Assert
        chapter.RenameTo("renamed.xhtml").Should().BeTrue();

        // Act
        _sut.NavigateBack();

        // Assert
        _sut.Tabs.ActiveTab!.Resource.Should().BeSameAs(chapter);
        _sut.Tabs.ActiveTab!.Resource.Filename.Should().Be("renamed.xhtml");
    }

    [Fact]
    public void The_actions_are_enabled_only_when_there_is_a_place_to_go_to()
    {
        // Arrange
        AppAction back = _sut.Actions.Require(AppActionIds.NavigateBack);
        AppAction forward = _sut.Actions.Require(AppActionIds.NavigateForward);

        // Act & Assert
        back.IsEnabled.Should().BeFalse();
        forward.IsEnabled.Should().BeFalse();

        // Arrange
        Open(Chapter(1));
        Open(Chapter(2));

        // Act & Assert
        back.IsEnabled.Should().BeTrue();

        // Act
        back.Execute(null);

        // Assert
        forward.IsEnabled.Should().BeTrue();
    }

    [Fact]
    public void Loading_another_book_starts_an_empty_history()
    {
        // Arrange
        Open(Chapter(1));
        Open(Chapter(2));
        string other = EpubBuilder.BuildInto(CorpusPaths.Epub3Media, _temp);

        // Act
        _sut.LoadBook(new ImportEpub(other).GetBook(), other);

        // Assert
        _sut.NavigationHistory.CanGoBack.Should().BeFalse();
    }

    [Fact]
    public void The_navigation_actions_have_no_default_shortcut_and_the_old_back_action_is_gone()
    {
        // Act & Assert
        _sut.Actions.Require(AppActionIds.NavigateBack).Gesture.Should().BeNull();
        _sut.Actions.Require(AppActionIds.NavigateForward).Gesture.Should().BeNull();
        _sut.Actions.Get("MainWindow.GoBackFromLinkOrStyle").Should().BeNull();
    }
}
