using System;
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
/// The "Prettify Code" / "Mend Code" / "Prettify Stylesheet" actions for the current file: Tools toolbar buttons with
/// the beautify-html / beautify-css / html-fix icons.
/// </summary>
public sealed class ReformatCurrentFileActionsTests : IDisposable
{
    private readonly Disposables _disposables = new();

    /// <inheritdoc />
    public void Dispose() => _disposables.Dispose();

    private const string Unformatted =
        "<?xml version=\"1.0\" encoding=\"utf-8\"?>\n<!DOCTYPE html>\n" +
        "<html xmlns=\"http://www.w3.org/1999/xhtml\"><head><title>t</title></head><body><div><p>Ala</p><p>kot</p></div></body></html>";

    private (MainWindowViewModel Vm, Book Book) Open(TempDir temp)
    {
        string epub = EpubBuilder.BuildInto(CorpusPaths.Epub3Media, temp);
        MainWindowViewModel vm = _disposables.Add(new MainWindowViewModel());
        Book book = _disposables.Add(new ImportEpub(epub).GetBook());
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

    private static CodeTabViewModel OpenCss(MainWindowViewModel vm, Book book, string text)
    {
        vm.Tabs.OpenResources(new Resource[] { book.GetAllResources().OfType<CssResource>().First() });
        CodeTabViewModel tab = vm.ActiveCodeTab!;
        tab.Document.Text = text;
        return tab;
    }

    [Fact]
    public void Actions_are_enabled_only_for_an_html_code_tab()
    {
        // Arrange
        TempDir temp = _disposables.Add(new TempDir());
        (MainWindowViewModel vm, Book book) = Open(temp);
        AppAction prettify = vm.Actions.Require(AppActionIds.PrettifyCurrentHtml);
        AppAction mend = vm.Actions.Require(AppActionIds.MendCurrentHtml);

        // Act & Assert
        prettify.IsEnabled.Should().BeFalse("no code tab is open");
        mend.IsEnabled.Should().BeFalse();

        // Act
        vm.Tabs.OpenResources(new Resource[] { book.GetAllResources().OfType<CssResource>().First() });

        // Assert
        prettify.IsEnabled.Should().BeFalse("a CSS stylesheet is not an HTML file");
        mend.IsEnabled.Should().BeFalse();

        // Act
        OpenHtml(vm, book, Unformatted);

        // Assert
        prettify.IsEnabled.Should().BeTrue();
        mend.IsEnabled.Should().BeTrue();
    }

    [Fact]
    public void Prettify_stylesheet_is_enabled_only_for_a_css_code_tab()
    {
        // Arrange
        TempDir temp = _disposables.Add(new TempDir());
        (MainWindowViewModel vm, Book book) = Open(temp);
        AppAction prettifyCss = vm.Actions.Require(AppActionIds.PrettifyCurrentCss);

        // Act & Assert
        prettifyCss.IsEnabled.Should().BeFalse("no code tab is open");

        // Act
        OpenHtml(vm, book, Unformatted);

        // Assert
        prettifyCss.IsEnabled.Should().BeFalse("an HTML file is not a stylesheet");

        // Act
        OpenCss(vm, book, "p { margin: 0; text-indent: 1em; }");

        // Assert
        prettifyCss.IsEnabled.Should().BeTrue();
    }

    [Fact]
    public void Prettify_stylesheet_reformats_the_current_stylesheet_into_the_multi_line_form()
    {
        // Arrange
        TempDir temp = _disposables.Add(new TempDir());
        (MainWindowViewModel vm, Book book) = Open(temp);
        CodeTabViewModel tab = OpenCss(vm, book, "p { margin: 0; text-indent: 1em; }");

        // Act
        vm.Actions.Require(AppActionIds.PrettifyCurrentCss).Execute(null);

        // Assert
        tab.Document.Text.Should().Contain("margin: 0;\n").And.Contain("text-indent: 1em;\n");
    }

    [Fact]
    public void Prettify_stylesheet_leaves_a_stylesheet_with_a_broken_structure_unchanged()
    {
        // Arrange
        TempDir temp = _disposables.Add(new TempDir());
        (MainWindowViewModel vm, Book book) = Open(temp);
        const string Broken = "p { margin: 0;";
        CodeTabViewModel tab = OpenCss(vm, book, Broken);

        // Act
        vm.Actions.Require(AppActionIds.PrettifyCurrentCss).Execute(null);

        // Assert
        tab.Document.Text.Should().Be(Broken);
    }

    [Fact]
    public void Prettify_reformats_the_current_file_like_the_context_menu_entry()
    {
        // Arrange
        TempDir temp = _disposables.Add(new TempDir());
        (MainWindowViewModel vm, Book book) = Open(temp);
        CodeTabViewModel tab = OpenHtml(vm, book, Unformatted);

        // Act
        vm.Actions.Require(AppActionIds.PrettifyCurrentHtml).Execute(null);

        // Assert
        tab.Document.Text.Should().NotBe(Unformatted);
        tab.Document.Text.Should().Contain("<p>Ala</p>");
        tab.Document.Text.Split('\n').Length.Should().BeGreaterThan(Unformatted.Split('\n').Length,
            "prettifying splits nested elements onto separate, indented lines");
    }

    [Fact]
    public void Prettify_leaves_a_not_well_formed_file_unchanged()
    {
        // Arrange
        TempDir temp = _disposables.Add(new TempDir());
        (MainWindowViewModel vm, Book book) = Open(temp);
        string broken = Unformatted.Replace("<p>kot</p>", "<p>kot", System.StringComparison.Ordinal);
        CodeTabViewModel tab = OpenHtml(vm, book, broken);

        // Act
        vm.Actions.Require(AppActionIds.PrettifyCurrentHtml).Execute(null);

        // Assert
        tab.Document.Text.Should().Be(broken);
    }

    [Fact]
    public void Mend_repairs_a_not_well_formed_file()
    {
        // Arrange
        TempDir temp = _disposables.Add(new TempDir());
        (MainWindowViewModel vm, Book book) = Open(temp);
        string broken = Unformatted.Replace("<p>kot</p>", "<p>kot", System.StringComparison.Ordinal);
        CodeTabViewModel tab = OpenHtml(vm, book, broken);

        // Act
        vm.Actions.Require(AppActionIds.MendCurrentHtml).Execute(null);

        // Assert
        tab.Document.Text.Should().Contain("<p>kot</p>");
    }

    [Fact]
    public void Current_file_and_all_files_actions_use_the_beautify_and_html_fix_icons()
    {
        // Arrange
        TempDir temp = _disposables.Add(new TempDir());
        (MainWindowViewModel vm, _) = Open(temp);

        // Act & Assert
        vm.Actions.Require(AppActionIds.PrettifyCurrentHtml).IconKey.Should().Be("beautify-html");
        vm.Actions.Require(AppActionIds.MendPrettifyHtml).IconKey.Should().Be("beautify-html");
        vm.Actions.Require(AppActionIds.MendCurrentHtml).IconKey.Should().Be("html-fix");
        vm.Actions.Require(AppActionIds.MendHtml).IconKey.Should().Be("html-fix");
        vm.Actions.Require(AppActionIds.PrettifyCurrentCss).IconKey.Should().Be("beautify-css");
        vm.Actions.Require(AppActionIds.PrettifyAllCss).IconKey.Should().Be("beautify-css");
    }

    [Fact]
    public void Prettify_and_mend_have_their_own_toolbar_by_default()
    {
        // Act & Assert
        ToolbarManager.GetDefaultItems(ToolbarId.PrettifyMend).Should().Equal(
            AppActionIds.PrettifyCurrentHtml, AppActionIds.MendCurrentHtml, AppActionIds.PrettifyCurrentCss);
    }
}
