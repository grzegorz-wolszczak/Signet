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
        _sut.Dispose();
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
        // Arrange
        OpenChapterWithCaretOn("ote\">Hello");

        // Act
        _sut.Actions.Require(AppActionIds.FindUsages).Execute(null);

        FindUsagesViewModel panel = _sut.FindUsages;

        // Assert
        panel.ClassName.Should().Be("note");
        panel.Header.Should().Be(Strings.Format("FindUsages_Header", "note"));
        FindUsagesNode root = panel.Roots.Should().ContainSingle().Subject;
        (root.Text, root.CountText).Should().Be(("Found usages", "3 results in 2 files"));
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
        // Arrange
        _sut.Tabs.OpenResources(new Resource[] { _book.GetCssResources().Single() });
        int offset = _sut.ActiveCodeTab!.DocumentText.IndexOf(".note", StringComparison.Ordinal) + 2;
        _sut.ActiveCodeTab.UpdateCaret(1, 1, offset, 0);

        // Act
        _sut.Actions.Require(AppActionIds.FindUsages).Execute(null);

        // Assert
        _sut.FindUsages.ClassName.Should().Be("note");
        _sut.FindUsages.Roots.Single().CountText.Should().Be("3 results in 2 files");
    }

    [Fact]
    public void Without_a_class_under_the_caret_only_the_status_bar_says_so()
    {
        // Arrange
        OpenChapterWithCaretOn("Hello");

        // Act
        _sut.Actions.Require(AppActionIds.FindUsages).Execute(null);

        // Assert
        _sut.FindUsages.HasSearch.Should().BeFalse();
        _sut.StatusMessage.Should().Be(Strings.Get("Status_FindUsagesNoClass"));
    }

    [Fact]
    public void Grouping_by_file_gives_one_node_per_file_with_line_and_column_usages()
    {
        // Act
        _sut.FindClassUsages("note");
        _sut.FindUsages.GroupByFile = true;

        FindUsagesNode root = _sut.FindUsages.Roots.Single();

        // Assert
        root.Children.Select(f => (f.Text, f.CountText, f.Usage)).Should().Equal(
            (ChapterPath, "2 results", null),
            (StylesPath, "1 result", null));
        ClassUsage first = _book.FindClassUsages("note")[0];
        root.Children[0].Children[0].Text.Should().Be($"line {first.Line}, col. {first.Column}");
    }

    [Fact]
    public void Double_clicking_a_usage_opens_its_file()
    {
        // Arrange
        _sut.FindClassUsages("note");
        FindUsagesNode cssUsage = _sut.FindUsages.Roots.Single().Children.Last();

        // Act
        _sut.FindUsages.Activate(_sut.FindUsages.Roots.Single());
        _sut.FindUsages.Activate(cssUsage);

        // Assert
        _sut.ActiveCodeTab!.Resource.BookPath.Should().Be(StylesPath);
    }

    [Fact]
    public void Refresh_searches_the_same_class_again()
    {
        // Act & Assert
        _sut.FindUsages.RefreshCommand.CanExecute(null).Should().BeFalse("there is nothing to refresh yet");

        // Arrange
        _sut.FindClassUsages("note");
        CssResource css = _book.GetCssResources().Single();
        css.SetText(css.GetText() + "\np.note { margin: 0 }\n");

        // Act
        _sut.FindUsages.RefreshCommand.Execute(null);

        // Assert
        _sut.FindUsages.Roots.Single().CountText.Should().Be("4 results in 2 files");
    }

    [Fact]
    public void Saving_refreshes_the_shown_panel_without_taking_the_focus_or_the_status_bar()
    {
        // Arrange
        _sut.FindClassUsages("note");
        string status = _sut.StatusMessage;
        CssResource css = _book.GetCssResources().Single();
        css.SetText(css.GetText() + "\np.note { margin: 0 }\n");

        // Act
        _sut.RefreshFindUsagesAfterSave();

        // Assert
        _sut.FindUsages.Roots.Single().CountText.Should().Be("4 results in 2 files");
        _sut.StatusMessage.Should().Be(status);
    }

    [Fact]
    public void Saving_does_not_refresh_a_hidden_panel_nor_after_a_failed_save()
    {
        // Arrange
        _sut.FindClassUsages("note");
        CssResource css = _book.GetCssResources().Single();
        css.SetText(css.GetText() + "\np.note { margin: 0 }\n");

        // Act
        _sut.RefreshFindUsagesAfterSave(saved: false);

        // Assert
        _sut.FindUsages.Roots.Single().CountText.Should().Be("3 results in 2 files");

        // Act
        _sut.Actions.Require(AppActionIds.ToggleFindUsages).Execute(null);
        _sut.RefreshFindUsagesAfterSave();

        // Assert
        _sut.FindUsages.Roots.Single().CountText.Should().Be("3 results in 2 files");
    }

    [Fact]
    public void The_grouping_is_remembered_in_the_settings()
    {
        // Arrange
        using TempDir dir = new();
        SettingsStore settings = new(dir.Combine("settings.xml"));
        FindUsagesViewModel panel = new(settings) { GroupByFile = true };

        // Act & Assert
        settings.FindUsagesGroupByFile.Should().BeTrue();
        new FindUsagesViewModel(settings).GroupByFile.Should().BeTrue();
        panel.GroupByFile.Should().BeTrue();
    }

    [Fact]
    public void Wrapping_rows_is_remembered_in_the_settings()
    {
        // Arrange
        using TempDir dir = new();
        SettingsStore settings = new(dir.Combine("settings.xml"));
        FindUsagesViewModel panel = new(settings);

        // Act & Assert
        panel.WrapRows.Should().BeFalse("rows are cut with an ellipsis by default");

        // Act
        panel.WrapRows = true;

        // Assert
        settings.FindUsagesWrapRows.Should().BeTrue();
        new FindUsagesViewModel(settings).WrapRows.Should().BeTrue();
    }

    [Fact]
    public void The_usage_column_fits_its_content_until_Preferences_say_otherwise()
    {
        // Arrange
        using TempDir dir = new();
        SettingsStore settings = new(dir.Combine("settings.xml"));
        FindUsagesViewModel panel = new(settings);

        // Act & Assert
        panel.FitUsageColumn.Should().BeTrue();

        // Act
        settings.FindUsagesFitUsageColumn = false;

        // Assert
        panel.FitUsageColumn.Should().BeTrue("the panel reads the setting when Preferences are saved");

        // Act
        panel.ReloadSettings();

        // Assert
        panel.FitUsageColumn.Should().BeFalse();
    }

    [Fact]
    public void The_width_the_user_gave_the_usage_column_is_kept_in_the_settings()
    {
        // Arrange
        using TempDir dir = new();
        SettingsStore settings = new(dir.Combine("settings.xml"));

        // Act
        FindUsagesViewModel panel = new(settings) { UsageColumnWidth = 321 };

        // Assert
        settings.FindUsagesUsageColumnWidth.Should().Be(321);
        panel.UsageColumnWidth.Should().Be(321);
    }

    [Fact]
    public void A_change_of_the_fonts_in_the_settings_raises_AppearanceChanged()
    {
        // Arrange
        using TempDir dir = new();
        SettingsStore settings = new(dir.Combine("settings.xml"));
        FindUsagesViewModel panel = new(settings);
        int raised = 0;
        panel.AppearanceChanged += (_, _) => raised++;

        // Act
        settings.FindUsagesWrapRows = true;

        // Assert
        raised.Should().Be(0, "only the fonts matter");

        // Act
        settings.FindUsagesAppearance = new FindUsagesAppearance(string.Empty, 0, "Consolas", 14, string.Empty, 0);

        // Assert
        raised.Should().Be(1);
        panel.Appearance.ContextFontFamily.Should().Be("Consolas");
    }

    [Fact]
    public void A_node_knows_its_level_and_puts_a_gap_before_the_count()
    {
        // Arrange
        using TempDir dir = new();
        FindUsagesViewModel panel = new(new SettingsStore(dir.Combine("settings.xml"))) { GroupByFile = true };

        // Act
        panel.Load("note", new[] { new ClassUsage("a.xhtml", 10, 3, 14, ClassUsageKind.ClassAttribute) });

        FindUsagesNode root = panel.Roots.Single();
        FindUsagesNode file = root.Children.Single();
        FindUsagesNode usage = file.Children.Single();

        // Assert
        new[] { root.Level, file.Level, usage.Level }.Should().Equal(0, 1, 2);
        root.CountSuffix.Should().Be("  " + root.CountText);
        usage.CountSuffix.Should().BeEmpty();
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
        // Arrange
        using UiCultureScope polish = new("pl");

        // Act & Assert
        FindUsagesViewModel.ResultsText(count).Should().Be(expected);
    }

    [Fact]
    public void Collapse_All_and_Expand_All_switch_the_root_and_the_file_nodes()
    {
        // Act & Assert
        _sut.FindUsages.CollapseAllCommand.CanExecute(null).Should().BeFalse("there is no tree yet");

        // Arrange
        _sut.FindClassUsages("note");
        _sut.FindUsages.GroupByFile = true;
        FindUsagesNode root = _sut.FindUsages.Roots.Single();

        // Act
        _sut.FindUsages.CollapseAllCommand.Execute(null);

        // Assert
        root.IsExpanded.Should().BeFalse();
        root.Children.Should().OnlyContain(file => !file.IsExpanded);

        // Act
        _sut.FindUsages.ExpandAllCommand.Execute(null);

        // Assert
        root.IsExpanded.Should().BeTrue();
        root.Children.Should().OnlyContain(file => file.IsExpanded);
    }

    [Fact]
    public void Usage_nodes_carry_the_line_column_kind_and_context_and_the_other_nodes_none()
    {
        // Arrange
        _sut.FindClassUsages("note");
        _sut.FindUsages.GroupByFile = true;
        FindUsagesNode root = _sut.FindUsages.Roots.Single();
        FindUsagesNode chapter = root.Children[0];
        FindUsagesNode attribute = chapter.Children[0];
        FindUsagesNode selector = root.Children[1].Children[0];
        ClassUsage first = _book.FindClassUsages("note")[0];

        // Act & Assert
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
        // Arrange
        _sut.FindClassUsages("note");
        _sut.FindUsages.GroupByFile = true;
        _sut.FindUsages.Roots.Single().Children.Single(f => f.Text == ChapterPath).IsExpanded = false;

        // Act
        _sut.FindUsages.RefreshCommand.Execute(null);

        FindUsagesNode root = _sut.FindUsages.Roots.Single();

        // Assert
        root.Children.Select(f => (f.Text, f.IsExpanded)).Should().Equal((ChapterPath, false), (StylesPath, true));

        // Act
        root.IsExpanded = false;
        _sut.FindUsages.GroupByFile = false;

        // Assert
        _sut.FindUsages.Roots.Single().IsExpanded.Should().BeFalse("switching the grouping keeps the root collapsed");

        // Act
        _sut.FindClassUsages("lead");

        // Assert
        _sut.FindUsages.Roots.Single().IsExpanded.Should().BeTrue("another class starts fully expanded");
    }

    // ------------------------------------------------------ text search ("Count All") --- //

    private int CountAllInAllHtml(string text)
    {
        _sut.FindReplace.LookWhereIndex = (int)Signet.Core.Search.LookWhere.AllHtmlFiles;
        _sut.FindReplace.FindText = text;
        return _sut.FindReplace.Count();
    }

    [Fact]
    public void Count_All_shows_its_matches_in_the_window_with_the_number_of_files()
    {
        // Act
        int count = CountAllInAllHtml("note");

        // Assert
        count.Should().Be(2);
        _sut.FindUsages.HasTextSearch.Should().BeTrue();
        _sut.FindUsages.ClassName.Should().BeNull();
        _sut.FindUsages.Header.Should().Be(Strings.Format(
            "FindUsages_TextHeader",
            "note",
            Strings.Get("FindReplaceView_ModeNormal"),
            Strings.Get("FindReplace_LookInAllHtml"),
            string.Empty));
        FindUsagesNode root = _sut.FindUsages.Roots.Single();
        root.CountText.Should().Be("2 results in 1 file");
        root.Children.Should().HaveCount(2);
        FindUsagesNode match = root.Children[0];
        match.KindText.Should().Be(Strings.Get("FindUsages_KindText"));
        match.Context.Should().Contain("class=\"note\"");
        (match.Line, match.Column).Should().NotBe((null, null));
    }

    [Fact]
    public void Count_All_without_matches_leaves_the_window_alone()
    {
        // Arrange
        _sut.FindClassUsages("note");

        // Act & Assert
        CountAllInAllHtml("no such text anywhere").Should().Be(0);

        // Assert
        _sut.FindUsages.HasTextSearch.Should().BeFalse();
        _sut.FindUsages.ClassName.Should().Be("note");
    }

    [Fact]
    public void Double_clicking_a_match_selects_it_in_the_editor()
    {
        // Arrange
        CountAllInAllHtml("again");
        FindUsagesNode match = _sut.FindUsages.Roots.Single().Children.Single();
        _sut.Tabs.OpenResources(new Resource[] { Chapter });
        (int Start, int End)? selected = null;
        _sut.ActiveCodeTab!.SearchResultRequested += (start, end, _) => selected = (start, end);

        // Act
        _sut.FindUsages.Activate(match);

        // Assert
        _sut.ActiveCodeTab!.Resource.BookPath.Should().Be(ChapterPath);
        selected.Should().Be((match.Usage!.Offset, match.Usage.Offset + "Again".Length));
    }

    [Fact]
    public void Refresh_runs_the_text_search_again_and_the_save_refreshes_it_too()
    {
        // Arrange
        CountAllInAllHtml("note");

        // Act & Assert
        _sut.FindUsages.RefreshCommand.CanExecute(null).Should().BeTrue();
        Chapter.SetText(Chapter.GetText().Replace("Hello", "note Hello", StringComparison.Ordinal));

        // Act
        _sut.FindUsages.RefreshCommand.Execute(null);

        // Assert
        _sut.FindUsages.Roots.Single().CountText.Should().Be("3 results in 1 file");

        // Act
        Chapter.SetText(Chapter.GetText().Replace("Hello", "note Hello", StringComparison.Ordinal));
        _sut.RefreshFindUsagesAfterSave();

        // Assert
        _sut.FindUsages.Roots.Single().CountText.Should().Be("4 results in 1 file");
    }

    [Fact]
    public void A_class_search_replaces_the_text_search_and_the_other_way_round()
    {
        // Arrange
        CountAllInAllHtml("note");

        // Act
        _sut.FindClassUsages("note");

        // Assert
        _sut.FindUsages.HasTextSearch.Should().BeFalse();
        _sut.FindUsages.Header.Should().Be(Strings.Format("FindUsages_Header", "note"));

        // Act
        CountAllInAllHtml("note");

        // Assert
        _sut.FindUsages.ClassName.Should().BeNull();
        _sut.FindUsages.HasTextSearch.Should().BeTrue();
    }

    [Theory]
    [InlineData(1, "in 1 file")]
    [InlineData(2, "in 2 files")]
    [InlineData(5, "in 5 files")]
    public void File_counts_are_worded_in_the_current_language(int count, string expected)
    {
        // Act & Assert
        FindUsagesViewModel.FilesText(count).Should().Be(expected);
    }

    [Theory]
    [InlineData(1, "w 1 pliku")]
    [InlineData(2, "w 2 plikach")]
    [InlineData(34, "w 34 plikach")]
    public void Polish_file_counts_use_the_locative_forms(int count, string expected)
    {
        // Arrange
        using UiCultureScope polish = new("pl");

        // Act & Assert
        FindUsagesViewModel.FilesText(count).Should().Be(expected);
    }
}
