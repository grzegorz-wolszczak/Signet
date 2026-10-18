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
public sealed class UnwrapDivTests
{
    private const string Doc = "<html><body>\n<div class=\"a\">\n  <div class=\"b\">\n    <p>x</p>\n  </div>\n</div>\n<p>out</p>\n</body></html>";

    private static (MainWindowViewModel Vm, CodeTabViewModel Tab, AppAction Action) Open(TempDir temp, string doc = Doc)
    {
        string epub = EpubBuilder.BuildInto(CorpusPaths.Epub3Media, temp);
        MainWindowViewModel vm = new();
        Book book = new ImportEpub(epub).GetBook();
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
        using TempDir temp = new();
        (_, _, AppAction action) = Open(temp);

        action.Descriptor.DefaultShortcut.Should().BeEmpty();
    }

    [Fact]
    public void The_innermost_div_is_unwrapped_and_the_edit_is_one_undo_step()
    {
        using TempDir temp = new();
        (_, CodeTabViewModel tab, AppAction action) = Open(temp);
        Caret(tab, "<p>x");

        action.Execute(null);

        tab.Document.Text.Should().Be("<html><body>\n<div class=\"a\">\n  <p>x</p>\n</div>\n<p>out</p>\n</body></html>");
        tab.Document.UndoStack.Undo();
        tab.Document.Text.Should().Be(Doc);
    }

    [Fact]
    public void A_div_with_an_id_asks_first()
    {
        using TempDir temp = new();
        string doc = "<div id=\"k\"><p>x</p></div>";
        (MainWindowViewModel vm, CodeTabViewModel tab, AppAction action) = Open(temp, doc);
        string? asked = null;
        vm.UnwrapDivConfirmationRequested += (_, id) => asked = id;
        Caret(tab, "<p>");

        action.Execute(null);

        asked.Should().Be("k");
        tab.Document.Text.Should().Be(doc);

        vm.ConfirmUnwrapDiv();

        tab.Document.Text.Should().Be("<p>x</p>");
    }

    [Fact]
    public void The_action_is_enabled_only_while_the_caret_is_inside_a_div()
    {
        using TempDir temp = new();
        (_, CodeTabViewModel tab, AppAction action) = Open(temp);

        Caret(tab, "<?xml");
        action.IsEnabled.Should().BeFalse();
        Caret(tab, "<p>x");
        action.IsEnabled.Should().BeTrue();
        Caret(tab, "<p>out");
        action.IsEnabled.Should().BeFalse();
        Caret(tab, "<div class=\"b\"");
        action.IsEnabled.Should().BeTrue();
    }

    [Fact]
    public void Outside_a_div_nothing_changes_and_the_reason_is_given()
    {
        using TempDir temp = new();
        (_, CodeTabViewModel tab, AppAction action) = Open(temp);
        Caret(tab, "<p>out");

        action.Execute(null);

        action.IsEnabled.Should().BeFalse("the Edit menu item is greyed out when the caret is not in a div");
        tab.Document.Text.Should().Be(Doc);
        tab.CheckUnwrapDiv().Problem.Should().Be(DivUnwrapProblem.NotInDiv);
        CodeTabViewModel.UnwrapDivProblemText(DivUnwrapProblem.NotInDiv).Should().Be(Strings.Get("UnwrapDiv_NotInDiv"));
        CodeTabViewModel.UnwrapDivProblemText(DivUnwrapProblem.None).Should().BeNull();
    }
}
