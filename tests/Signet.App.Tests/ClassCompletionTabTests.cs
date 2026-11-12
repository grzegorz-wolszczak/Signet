using System;
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
/// The CSS classes offered in the <c>class</c> attribute of an open (X)HTML file: the ones the stylesheets linked from the file
/// define, without the ones the attribute has, and the action that shows them before anything is typed.
/// </summary>
public sealed class ClassCompletionTabTests : IDisposable
{
    private readonly Disposables _disposables = new();

    /// <inheritdoc />
    public void Dispose() => _disposables.Dispose();

    // chapter1 links ../styles/style.css, which gets the given classes.
    private (MainWindowViewModel Sut, CodeTabViewModel Tab, Book Book) Open(string css)
    {
        TempDir temp = _disposables.Add(new TempDir());
        string epub = EpubBuilder.BuildInto(CorpusPaths.Epub3WithNcx, temp);
        MainWindowViewModel sut = _disposables.Add(new MainWindowViewModel());
        Book book = _disposables.Add(new ImportEpub(epub).GetBook());
        sut.LoadBook(book, epub);
        book.GetCssResources().First().SetText(css);
        sut.Tabs.OpenResources(new Resource[] { book.GetHtmlResources().First(h => h.Filename == "chapter1.xhtml") });
        return (sut, (CodeTabViewModel)sut.Tabs.ActiveTab!, book);
    }

    private static int Caret(CodeTabViewModel tab, string marked)
    {
        int caret = marked.IndexOf('|', StringComparison.Ordinal);
        tab.Document.Text = marked.Remove(caret, 1);
        return caret;
    }

    [Fact]
    public void Typing_in_a_class_attribute_offers_the_classes_that_match_without_the_ones_the_attribute_has()
    {
        // Arrange
        (_, CodeTabViewModel tab, _) = Open(".note { x: y } .notice { x: y } .lead { x: y } p.footnote { x: y }");
        int caret = Caret(tab, "<body>\n<p class=\"lead no|\">text</p>\n</body>");

        // Act
        LinkCompletionResult? result = tab.GetCompletions(caret, manual: false);

        // Assert
        result.Should().NotBeNull();
        result!.Items.Select(i => i.Text).Should().Contain("note").And.Contain("notice").And.Contain("footnote").And.NotContain("lead");
        result.Items.Select(i => i.Text).Should().NotContain("lead", "it is before the place where the class is typed");
        result.Items.All(i => i.Description == "style.css").Should().BeTrue("the file that defines the class");
        tab.Document.Text[result.ReplaceStart..result.ReplaceEnd!.Value].Should().Be("no");
    }

    [Fact]
    public void A_class_after_the_place_is_not_offered_either()
    {
        // Arrange
        (_, CodeTabViewModel tab, _) = Open(".note { x: y } .notice { x: y }");
        int caret = Caret(tab, "<body><p class=\"no| note\">text</p></body>");

        // Act
        LinkCompletionResult? result = tab.GetCompletions(caret, manual: false);

        // Assert
        result!.Items.Select(i => i.Text).Should().Equal("notice");
    }

    [Fact]
    public void Nothing_is_offered_before_something_is_typed_unless_the_completions_are_asked_for()
    {
        // Arrange
        (_, CodeTabViewModel tab, _) = Open(".note { x: y } .lead { x: y }");
        int caret = Caret(tab, "<body><p class=\"|\">text</p></body>");

        // Act
        LinkCompletionResult? auto = tab.GetCompletions(caret, manual: false);
        LinkCompletionResult? asked = tab.GetCompletions(caret, manual: true);

        // Assert
        auto.Should().BeNull();
        asked!.Items.Select(i => i.Text).Should().Equal("lead", "note");
    }

    [Fact]
    public void The_list_follows_a_stylesheet_that_changed_and_the_style_blocks_of_the_file()
    {
        // Arrange
        (_, CodeTabViewModel tab, Book book) = Open(".one { x: y }");
        int caret = Caret(tab, "<head><style>.inline { x: y }</style></head><body><p class=\"|\">text</p></body>");

        // Act
        LinkCompletionResult? first = tab.GetCompletions(caret, manual: true);
        book.GetCssResources().First().SetText(".two { x: y }");
        LinkCompletionResult? second = tab.GetCompletions(caret, manual: true);

        // Assert
        first!.Items.Select(i => i.Text).Should().Equal("inline", "one");
        second!.Items.Select(i => i.Text).Should().Equal("inline", "two");
    }

    [Fact]
    public void Nothing_is_offered_when_no_class_matches_or_the_caret_is_elsewhere()
    {
        // Arrange
        (_, CodeTabViewModel tab, _) = Open(".note { x: y }");
        int inClass = Caret(tab, "<body><p class=\"zzz|\">text</p></body>");

        // Act & Assert
        tab.GetCompletions(inClass, manual: true).Should().BeNull("no class matches");
        int inText = Caret(tab, "<body><p class=\"note\">te|xt</p></body>");
        tab.GetCompletions(inText, manual: true).Should().BeNull();
    }

    [Fact]
    public void Show_Completions_asks_the_active_editor_and_has_Ctrl_Shift_Space()
    {
        // Arrange
        (MainWindowViewModel sut, CodeTabViewModel tab, _) = Open(".note { x: y }");
        int requests = 0;
        tab.CompletionRequested += () => requests++;

        // Act
        sut.Actions.Require(AppActionIds.ShowCompletions).Execute(null);

        // Assert
        requests.Should().Be(1);
        AppActionCatalog.All.Single(a => a.Id == AppActionIds.ShowCompletions).DefaultShortcut.Should().Be("Ctrl+Shift+Space");
    }
}
