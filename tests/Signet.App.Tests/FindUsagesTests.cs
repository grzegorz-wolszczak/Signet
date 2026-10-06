using System;
using System.IO;
using System.Linq;
using AwesomeAssertions;
using Signet.App.Actions;
using Signet.App.Resources;
using Signet.App.Tests.TestSupport;
using Signet.App.ViewModels;
using Signet.Core.BookManipulation;
using Signet.Core.Misc;
using Signet.Core.Resources;
using Signet.Core.Tests.TestSupport;
using Xunit;

namespace Signet.App.Tests;

/// <summary>
/// "Find Usages" (Alt+F7): the action on the class under the caret, the tree of the "Find Usages" panel (flat or
/// grouped by file), navigation and refresh.
/// </summary>
public sealed class FindUsagesTests : IDisposable
{
    private const string ChapterPath = "EPUB/text/chapter1.xhtml";
    private const string StylesPath = "EPUB/styles/style.css";

    private readonly TempDir _temp = new();
    private readonly UiCultureScope _culture = new("en");
    private readonly MainWindowViewModel _sut = new();
    private readonly Book _book;

    public FindUsagesTests()
    {
        string tree = _temp.Combine("tree");
        TestFs.CopyDirectory(CorpusPaths.Epub3Minimal, tree);
        string chapter = Path.Combine(tree, "EPUB", "text", "chapter1.xhtml");
        File.WriteAllText(chapter, File.ReadAllText(chapter).Replace(
            "<p>Hello, world.</p>", "<p class=\"note\">Hello, world.</p>\n  <p class=\"lead note\">Again.</p>", StringComparison.Ordinal));
        File.AppendAllText(Path.Combine(tree, "EPUB", "styles", "style.css"), "\n.note { color: red }\n");
        string epub = EpubBuilder.BuildInto(tree, _temp, "book.epub");
        _book = new ImportEpub(epub).GetBook();
        _sut.LoadBook(_book, epub);
    }

    public void Dispose()
    {
        _culture.Dispose();
        _temp.Dispose();
    }

    private HtmlResource Chapter => _book.GetHtmlResources().Single(h => h.Filename == "chapter1.xhtml");

    private void OpenChapterWithCaretOn(string marker)
    {
        _sut.Tabs.OpenResources(new Resource[] { Chapter });
        int offset = _sut.ActiveCodeTab!.DocumentText.IndexOf(marker, StringComparison.Ordinal);
        _sut.ActiveCodeTab.UpdateCaret(1, 1, offset, 0);
    }

    [Fact]
    public void Alt_F7_on_a_class_lists_its_usages_in_html_and_css_in_book_browser_order()
    {
        OpenChapterWithCaretOn("ote\">Hello");

        _sut.Actions.Require(AppActionIds.FindUsages).Execute(null);

        FindUsagesViewModel panel = _sut.FindUsages;
        panel.ClassName.Should().Be("note");
        panel.Header.Should().Be(Strings.Format("FindUsages_Header", "note"));
        FindUsagesNode root = panel.Roots.Should().ContainSingle().Subject;
        (root.Text, root.CountText).Should().Be(("Found usages", "3 results"));
        root.Children.Select(c => c.Text).Should().SatisfyRespectively(
            first => first.Should().StartWith(ChapterPath + ":"),
            second => second.Should().StartWith(ChapterPath + ":"),
            third => third.Should().StartWith(StylesPath + ":"));
        root.Children.Should().OnlyContain(c => c.Usage != null && c.CountText.Length == 0);
        _sut.StatusMessage.Should().Be(Strings.Format("Status_FindUsages", "note", "3 results"));
    }

    [Fact]
    public void Alt_F7_on_a_class_in_a_css_selector_finds_the_same_usages()
    {
        _sut.Tabs.OpenResources(new Resource[] { _book.GetCssResources().Single() });
        int offset = _sut.ActiveCodeTab!.DocumentText.IndexOf(".note", StringComparison.Ordinal) + 2;
        _sut.ActiveCodeTab.UpdateCaret(1, 1, offset, 0);

        _sut.Actions.Require(AppActionIds.FindUsages).Execute(null);

        _sut.FindUsages.ClassName.Should().Be("note");
        _sut.FindUsages.Roots.Single().CountText.Should().Be("3 results");
    }

    [Fact]
    public void Without_a_class_under_the_caret_only_the_status_bar_says_so()
    {
        OpenChapterWithCaretOn("Hello");

        _sut.Actions.Require(AppActionIds.FindUsages).Execute(null);

        _sut.FindUsages.HasSearch.Should().BeFalse();
        _sut.StatusMessage.Should().Be(Strings.Get("Status_FindUsagesNoClass"));
    }

    [Fact]
    public void Grouping_by_file_gives_one_node_per_file_with_line_and_column_usages()
    {
        _sut.FindClassUsages("note");
        _sut.FindUsages.GroupByFile = true;

        FindUsagesNode root = _sut.FindUsages.Roots.Single();
        root.Children.Select(f => (f.Text, f.CountText, f.Usage)).Should().Equal(
            (ChapterPath, "2 results", null),
            (StylesPath, "1 result", null));
        ClassUsage first = _book.FindClassUsages("note")[0];
        root.Children[0].Children[0].Text.Should().Be($"line {first.Line}, col. {first.Column}");
    }

    [Fact]
    public void Double_clicking_a_usage_opens_its_file()
    {
        _sut.FindClassUsages("note");
        FindUsagesNode cssUsage = _sut.FindUsages.Roots.Single().Children.Last();

        _sut.FindUsages.Activate(_sut.FindUsages.Roots.Single());
        _sut.FindUsages.Activate(cssUsage);

        _sut.ActiveCodeTab!.Resource.BookPath.Should().Be(StylesPath);
    }

    [Fact]
    public void Refresh_searches_the_same_class_again()
    {
        _sut.FindUsages.RefreshCommand.CanExecute(null).Should().BeFalse("there is nothing to refresh yet");
        _sut.FindClassUsages("note");
        CssResource css = _book.GetCssResources().Single();
        css.SetText(css.GetText() + "\np.note { margin: 0 }\n");

        _sut.FindUsages.RefreshCommand.Execute(null);

        _sut.FindUsages.Roots.Single().CountText.Should().Be("4 results");
    }

    [Fact]
    public void The_grouping_is_remembered_in_the_settings()
    {
        using TempDir dir = new();
        SettingsStore settings = new(dir.Combine("settings.json"));
        FindUsagesViewModel panel = new(settings) { GroupByFile = true };

        settings.FindUsagesGroupByFile.Should().BeTrue();
        new FindUsagesViewModel(settings).GroupByFile.Should().BeTrue();
        panel.GroupByFile.Should().BeTrue();
    }

    [Theory]
    [InlineData(1, "1 wynik")]
    [InlineData(2, "2 wyniki")]
    [InlineData(4, "4 wyniki")]
    [InlineData(5, "5 wyników")]
    [InlineData(12, "12 wyników")]
    [InlineData(22, "22 wyniki")]
    [InlineData(112, "112 wyników")]
    public void Polish_result_counts_use_the_plural_forms(int count, string expected)
    {
        using UiCultureScope polish = new("pl");

        FindUsagesViewModel.ResultsText(count).Should().Be(expected);
    }

    [Fact]
    public void Collapse_All_and_Expand_All_switch_the_root_and_the_file_nodes()
    {
        _sut.FindUsages.CollapseAllCommand.CanExecute(null).Should().BeFalse("there is no tree yet");
        _sut.FindClassUsages("note");
        _sut.FindUsages.GroupByFile = true;
        FindUsagesNode root = _sut.FindUsages.Roots.Single();

        _sut.FindUsages.CollapseAllCommand.Execute(null);

        root.IsExpanded.Should().BeFalse();
        root.Children.Should().OnlyContain(file => !file.IsExpanded);

        _sut.FindUsages.ExpandAllCommand.Execute(null);

        root.IsExpanded.Should().BeTrue();
        root.Children.Should().OnlyContain(file => file.IsExpanded);
    }

    [Fact]
    public void Usage_nodes_carry_the_line_column_kind_and_context_and_the_other_nodes_none()
    {
        _sut.FindClassUsages("note");
        _sut.FindUsages.GroupByFile = true;
        FindUsagesNode root = _sut.FindUsages.Roots.Single();
        FindUsagesNode chapter = root.Children[0];
        FindUsagesNode attribute = chapter.Children[0];
        FindUsagesNode selector = root.Children[1].Children[0];
        ClassUsage first = _book.FindClassUsages("note")[0];

        (attribute.Line, attribute.Column).Should().Be((first.Line, first.Column));
        attribute.KindText.Should().Be(Strings.Get("FindUsages_KindAttribute"));
        attribute.Context.Should().Contain("class=\"note\"").And.NotStartWith(" ");
        selector.KindText.Should().Be(Strings.Get("FindUsages_KindSelector"));
        selector.Context.Should().Be(".note { color: red }");
        attribute.Parent.Should().BeSameAs(chapter);
        foreach (FindUsagesNode group in new[] { root, chapter })
        {
            (group.Line, group.Column, group.KindText, group.Context).Should().Be(((int?)null, (int?)null, string.Empty, string.Empty));
        }
    }

    [Fact]
    public void Refresh_keeps_the_collapsed_nodes_collapsed_and_a_new_class_starts_expanded()
    {
        _sut.FindClassUsages("note");
        _sut.FindUsages.GroupByFile = true;
        _sut.FindUsages.Roots.Single().Children.Single(f => f.Text == ChapterPath).IsExpanded = false;

        _sut.FindUsages.RefreshCommand.Execute(null);

        FindUsagesNode root = _sut.FindUsages.Roots.Single();
        root.Children.Select(f => (f.Text, f.IsExpanded)).Should().Equal((ChapterPath, false), (StylesPath, true));

        root.IsExpanded = false;
        _sut.FindUsages.GroupByFile = false;

        _sut.FindUsages.Roots.Single().IsExpanded.Should().BeFalse("switching the grouping keeps the root collapsed");

        _sut.FindClassUsages("lead");

        _sut.FindUsages.Roots.Single().IsExpanded.Should().BeTrue("another class starts fully expanded");
    }
}
