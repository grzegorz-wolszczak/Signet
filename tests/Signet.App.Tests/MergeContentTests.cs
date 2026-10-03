using System.Linq;
using AwesomeAssertions;
using Signet.App.Actions;
using Signet.App.Menu;
using Signet.App.Resources;
using Signet.App.ViewModels;
using Signet.App.ViewModels.Tabs;
using Signet.Core.BookManipulation;
using Signet.Core.MainUI;
using Signet.Core.Resources;
using Signet.Core.Tests.TestSupport;
using Signet.App.Tests.TestSupport;
using Xunit;

namespace Signet.App.Tests;

/// <summary>
/// "Merge Content" in the application: action state and label, merging in the tab, confirmation
/// when attributes differ, the position in the Edit menu.
/// </summary>
public sealed class MergeContentTests
{
    private const string Doc = "<html><body>\n<p class=\"a\">Ala</p>\n<p class=\"a\">ma</p>\n<p class=\"b\">kota</p>\n</body></html>";

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
        return (vm, tab, vm.Actions.Require(AppActionIds.MergeContent));
    }

    private static void Select(CodeTabViewModel tab, string from, string toEndOf)
    {
        string text = tab.Document.Text;
        int start = text.IndexOf(from, System.StringComparison.Ordinal);
        int end = text.IndexOf(toEndOf, start, System.StringComparison.Ordinal) + toEndOf.Length;
        tab.UpdateSelection(start, end);
        tab.UpdateCaret(1, 1, end, -1);
    }

    [Fact]
    public void Action_is_disabled_with_the_plain_label_until_the_selection_covers_mergeable_elements()
    {
        using TempDir temp = new();
        (_, CodeTabViewModel tab, AppAction action) = Open(temp);
        string plain = CodeTabViewModel.MergeContentTextFor(null);

        action.IsEnabled.Should().BeFalse();
        tab.MergeContentText.Should().Be(plain);

        Select(tab, "<p class=\"a\">Ala", "ma</p>");

        action.IsEnabled.Should().BeTrue();
        string label = Strings.Format("CodeViewMenu_MergeContentCount", "p", 2);
        action.Text.Should().Be(label);
        tab.MergeContentText.Should().Be(label);

        Select(tab, "la", "ma");

        action.IsEnabled.Should().BeTrue("a selection from the middle of the first to the middle of the second paragraph is enough");

        Select(tab, "Al", "la");

        action.IsEnabled.Should().BeFalse();
        action.Text.Should().Be(action.DefaultText.Replace('&', '_'));
    }

    [Fact]
    public void Same_attributes_merge_immediately_without_asking()
    {
        using TempDir temp = new();
        (MainWindowViewModel vm, CodeTabViewModel tab, AppAction action) = Open(temp);
        bool asked = false;
        vm.MergeContentConfirmationRequested += (_, _) => asked = true;
        Select(tab, "<p class=\"a\">Ala", "ma</p>");

        action.Execute(null);

        asked.Should().BeFalse();
        tab.Document.Text.Should().Contain("<p class=\"a\">Ala ma</p>\n<p class=\"b\">kota</p>");
    }

    [Fact]
    public void Different_attributes_ask_first_and_merge_only_after_confirmation()
    {
        using TempDir temp = new();
        (MainWindowViewModel vm, CodeTabViewModel tab, AppAction action) = Open(temp);
        ElementMergeCandidate? asked = null;
        vm.MergeContentConfirmationRequested += (_, c) => asked = c;
        Select(tab, "<p class=\"a\">Ala", "kota</p>");

        action.Execute(null);

        asked.Should().NotBeNull();
        asked!.Count.Should().Be(3);
        asked.DifferingOpenTags.Should().Equal("<p class=\"b\">");
        tab.Document.Text.Should().Be(Doc);

        vm.ConfirmMergeContent();

        tab.Document.Text.Should().Contain("<p class=\"a\">Ala ma kota</p>");
    }

    [Fact]
    public void Merge_Content_is_the_last_item_of_the_Edit_menu()
    {
        using UiCultureScope culture = new("en");
        using TestHost host = new();
        MenuBuilder builder = new(host.Registry, host.Toolbars, new CommunityToolkit.Mvvm.Input.RelayCommand(() => { }));

        MenuItemViewModel edit = builder.Build().Single(m => m.Header == "_Edit");

        edit.Items!.Last().Command.Should().BeSameAs(host.Registry.Require(AppActionIds.MergeContent));
    }
    [Fact]
    public void Text_and_a_comment_between_elements_ask_first_and_are_merged_after_confirmation()
    {
        using TempDir temp = new();
        const string doc = "<html><body><span>abc</span>  bcd <!-- k --> <span>xyz</span></body></html>";
        (MainWindowViewModel vm, CodeTabViewModel tab, AppAction action) = Open(temp, doc);
        ElementMergeCandidate? asked = null;
        vm.MergeContentConfirmationRequested += (_, c) => asked = c;
        Select(tab, "<span>abc", "xyz</span>");

        action.Execute(null);

        asked!.TextBetween.Should().Equal("bcd");
        asked.RemovedNodes.Should().Equal("<!-- k -->");
        tab.Document.Text.Should().Be(doc);

        vm.ConfirmMergeContent();

        tab.Document.Text.Should().Be("<html><body><span>abc bcd xyz</span></body></html>");
    }

    [Fact]
    public void Warning_lists_a_section_for_every_reason()
    {
        ElementMergeCandidate candidate = new(
            "p", 3, "<p class=\"a\">", ["<p class=\"b\">"], ["tekst"], ["<!-- k -->"]);

        string warning = MainWindowViewModel.MergeContentWarning(candidate);

        warning.Should().Contain(Strings.Format("MergeContent_AttributesWarning", "p", "<p class=\"a\">", "<p class=\"b\">"));
        warning.Should().Contain(Strings.Format("MergeContent_TextBetweenWarning", "tekst"));
        warning.Should().Contain(Strings.Format("MergeContent_RemovedNodesWarning", "<!-- k -->"));
        MainWindowViewModel.MergeContentWarning(candidate with { DifferingOpenTags = [], RemovedNodes = [] })
            .Should().Be(Strings.Format("MergeContent_TextBetweenWarning", "tekst"));
    }
}
