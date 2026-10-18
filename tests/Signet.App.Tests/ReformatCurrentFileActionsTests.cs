using System.Linq;
using AwesomeAssertions;
using Signet.App.Actions;
using Signet.App.Toolbars;
using Signet.App.ViewModels;
using Signet.App.ViewModels.Tabs;
using Signet.Core.BookManipulation;
using Signet.Core.Resources;
using Signet.Core.Tests.TestSupport;
using Xunit;

namespace Signet.App.Tests;

/// <summary>
/// The "Prettify Code" / "Mend Code" actions for the current file: Tools toolbar buttons with the
/// beautify / html-fix icons.
/// </summary>
public sealed class ReformatCurrentFileActionsTests
{
    private const string Unformatted =
        "<?xml version=\"1.0\" encoding=\"utf-8\"?>\n<!DOCTYPE html>\n" +
        "<html xmlns=\"http://www.w3.org/1999/xhtml\"><head><title>t</title></head><body><div><p>Ala</p><p>kot</p></div></body></html>";

    private static (MainWindowViewModel Vm, Book Book) Open(TempDir temp)
    {
        string epub = EpubBuilder.BuildInto(CorpusPaths.Epub3Media, temp);
        MainWindowViewModel vm = new();
        Book book = new ImportEpub(epub).GetBook();
        vm.LoadBook(book, epub);
        vm.Tabs.CloseAllTabs();
        return (vm, book);
    }

    private static CodeTabViewModel OpenHtml(MainWindowViewModel vm, Book book, string text)
    {
        vm.Tabs.OpenResources(new Resource[] { book.GetAllResources().OfType<HtmlResource>().First() });
        CodeTabViewModel tab = vm.ActiveCodeTab!;
        tab.Document.Text = text;
        return tab;
    }

    [Fact]
    public void Actions_are_enabled_only_for_an_html_code_tab()
    {
        using TempDir temp = new();
        (MainWindowViewModel vm, Book book) = Open(temp);
        AppAction prettify = vm.Actions.Require(AppActionIds.PrettifyCurrentHtml);
        AppAction mend = vm.Actions.Require(AppActionIds.MendCurrentHtml);

        prettify.IsEnabled.Should().BeFalse("no code tab is open");
        mend.IsEnabled.Should().BeFalse();

        vm.Tabs.OpenResources(new Resource[] { book.GetAllResources().OfType<CssResource>().First() });
        prettify.IsEnabled.Should().BeFalse("a CSS stylesheet is not an HTML file");
        mend.IsEnabled.Should().BeFalse();

        OpenHtml(vm, book, Unformatted);
        prettify.IsEnabled.Should().BeTrue();
        mend.IsEnabled.Should().BeTrue();
    }

    [Fact]
    public void Prettify_reformats_the_current_file_like_the_context_menu_entry()
    {
        using TempDir temp = new();
        (MainWindowViewModel vm, Book book) = Open(temp);
        CodeTabViewModel tab = OpenHtml(vm, book, Unformatted);

        vm.Actions.Require(AppActionIds.PrettifyCurrentHtml).Execute(null);

        tab.Document.Text.Should().NotBe(Unformatted);
        tab.Document.Text.Should().Contain("<p>Ala</p>");
        tab.Document.Text.Split('\n').Length.Should().BeGreaterThan(Unformatted.Split('\n').Length,
            "prettifying splits nested elements onto separate, indented lines");
    }

    [Fact]
    public void Prettify_leaves_a_not_well_formed_file_unchanged()
    {
        using TempDir temp = new();
        (MainWindowViewModel vm, Book book) = Open(temp);
        string broken = Unformatted.Replace("<p>kot</p>", "<p>kot", System.StringComparison.Ordinal);
        CodeTabViewModel tab = OpenHtml(vm, book, broken);

        vm.Actions.Require(AppActionIds.PrettifyCurrentHtml).Execute(null);

        tab.Document.Text.Should().Be(broken);
    }

    [Fact]
    public void Mend_repairs_a_not_well_formed_file()
    {
        using TempDir temp = new();
        (MainWindowViewModel vm, Book book) = Open(temp);
        string broken = Unformatted.Replace("<p>kot</p>", "<p>kot", System.StringComparison.Ordinal);
        CodeTabViewModel tab = OpenHtml(vm, book, broken);

        vm.Actions.Require(AppActionIds.MendCurrentHtml).Execute(null);

        tab.Document.Text.Should().Contain("<p>kot</p>");
    }

    [Fact]
    public void Current_file_and_all_files_actions_use_the_beautify_and_html_fix_icons()
    {
        using TempDir temp = new();
        (MainWindowViewModel vm, _) = Open(temp);

        vm.Actions.Require(AppActionIds.PrettifyCurrentHtml).IconKey.Should().Be("beautify");
        vm.Actions.Require(AppActionIds.MendPrettifyHtml).IconKey.Should().Be("beautify");
        vm.Actions.Require(AppActionIds.MendCurrentHtml).IconKey.Should().Be("html-fix");
        vm.Actions.Require(AppActionIds.MendHtml).IconKey.Should().Be("html-fix");
    }

    [Fact]
    public void Tools_toolbar_ends_with_prettify_and_mend_by_default()
    {
        ToolbarManager.GetDefaultItems(ToolbarId.Tools).TakeLast(3).Should().Equal(
            ToolbarManager.Separator, AppActionIds.PrettifyCurrentHtml, AppActionIds.MendCurrentHtml);
    }
}
