using System;
using System.Linq;
using AwesomeAssertions;
using Signet.App.Actions;
using Signet.App.Resources;
using Signet.App.ViewModels;
using Signet.App.ViewModels.Tabs;
using Signet.Core.BookManipulation;
using Signet.Core.MainUI;
using Signet.Core.Resources;
using Signet.Core.Tests.TestSupport;
using Xunit;

namespace Signet.App.Tests;

/// <summary>"Unwrap div" in the application: the action, the id confirmation, the reasons it cannot be used.</summary>
public sealed class UnwrapDivTests : IDisposable
{
    private readonly Disposables _disposables = new();

    /// <inheritdoc />
    public void Dispose() => _disposables.Dispose();

    private const string Doc = "<html><body>\n<div class=\"a\">\n  <div class=\"b\">\n    <p>x</p>\n  </div>\n</div>\n<p>out</p>\n</body></html>";

    private (MainWindowViewModel Vm, CodeTabViewModel Tab, AppAction Action) Open(TempDir temp, string doc = Doc)
    {
        string epub = EpubBuilder.BuildInto(CorpusPaths.Epub3Media, temp);
        MainWindowViewModel vm = _disposables.Add(new MainWindowViewModel());
        Book book = _disposables.Add(new ImportEpub(epub).GetBook());
        vm.LoadBook(book, epub);
        vm.Tabs.CloseAllTabs();
        vm.Tabs.OpenResources(new Resource[] { book.GetAllResources().OfType<HtmlResource>().First() });

        CodeTabViewModel tab = vm.ActiveCodeTab!;
        tab.Document.Text = doc;
        return (vm, tab, vm.Actions.Require(AppActionIds.UnwrapDiv));
    }

    private static void Caret(CodeTabViewModel tab, string marker)
    {
        int offset = tab.Document.Text.IndexOf(marker, System.StringComparison.Ordinal);
        tab.UpdateSelection(offset, offset);
        tab.UpdateCaret(1, 1, offset, -1);
    }

    [Fact]
    public void The_action_has_no_default_shortcut()
    {
        // Arrange
        TempDir temp = _disposables.Add(new TempDir());
        (_, _, AppAction action) = Open(temp);

        // Act & Assert
        action.Descriptor.DefaultShortcut.Should().BeEmpty();
    }

    [Fact]
    public void The_innermost_div_is_unwrapped_and_the_edit_is_one_undo_step()
    {
        // Arrange
        TempDir temp = _disposables.Add(new TempDir());
        (_, CodeTabViewModel tab, AppAction action) = Open(temp);
        Caret(tab, "<p>x");

        // Act
        action.Execute(null);

        // Assert
        tab.Document.Text.Should().Be("<html><body>\n<div class=\"a\">\n  <p>x</p>\n</div>\n<p>out</p>\n</body></html>");

        // Act
        tab.Document.UndoStack.Undo();

        // Assert
        tab.Document.Text.Should().Be(Doc);
    }

    [Fact]
    public void A_div_with_an_id_asks_first()
    {
        // Arrange
        TempDir temp = _disposables.Add(new TempDir());
        string doc = "<div id=\"k\"><p>x</p></div>";
        (MainWindowViewModel vm, CodeTabViewModel tab, AppAction action) = Open(temp, doc);
        string? asked = null;
        vm.UnwrapDivConfirmationRequested += (_, id) => asked = id;
        Caret(tab, "<p>");

        // Act
        action.Execute(null);

        // Assert
        asked.Should().Be("k");
        tab.Document.Text.Should().Be(doc);

        // Act
        vm.ConfirmUnwrapDiv();

        // Assert
        tab.Document.Text.Should().Be("<p>x</p>");
    }

    [Fact]
    public void The_action_is_enabled_only_while_the_caret_is_inside_a_div()
    {
        // Arrange
        TempDir temp = _disposables.Add(new TempDir());
        (_, CodeTabViewModel tab, AppAction action) = Open(temp);

        // Act
        Caret(tab, "<?xml");

        // Assert
        action.IsEnabled.Should().BeFalse();

        // Act
        Caret(tab, "<p>x");

        // Assert
        action.IsEnabled.Should().BeTrue();

        // Act
        Caret(tab, "<p>out");

        // Assert
        action.IsEnabled.Should().BeFalse();

        // Act
        Caret(tab, "<div class=\"b\"");

        // Assert
        action.IsEnabled.Should().BeTrue();
    }

    [Fact]
    public void Outside_a_div_nothing_changes_and_the_reason_is_given()
    {
        // Arrange
        TempDir temp = _disposables.Add(new TempDir());
        (_, CodeTabViewModel tab, AppAction action) = Open(temp);
        Caret(tab, "<p>out");

        // Act
        action.Execute(null);

        // Assert
        action.IsEnabled.Should().BeFalse("the Edit menu item is greyed out when the caret is not in a div");
        tab.Document.Text.Should().Be(Doc);

        // Act & Assert
        tab.CheckUnwrapDiv().Problem.Should().Be(DivUnwrapProblem.NotInDiv);
        CodeTabViewModel.UnwrapDivProblemText(DivUnwrapProblem.NotInDiv).Should().Be(Strings.Get("UnwrapDiv_NotInDiv"));
        CodeTabViewModel.UnwrapDivProblemText(DivUnwrapProblem.None).Should().BeNull();
    }
}
