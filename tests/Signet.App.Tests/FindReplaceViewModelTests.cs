using System;
using System.IO;
using AwesomeAssertions;
using Signet.App.Services;
using Signet.App.Tests.TestSupport;
using Signet.App.ViewModels;
using Signet.App.ViewModels.Tabs;
using Signet.Core.BookManipulation;
using Signet.Core.MainUI;
using Signet.Core.Misc;
using Signet.App.Resources;
using Signet.Core.Resources;
using Signet.Core.Search;
using Signet.Core.Spellcheck;
using Xunit;

namespace Signet.App.Tests;

/// <summary>Tests for <see cref="FindReplaceViewModel"/>: the F&amp;R panel for the current file.</summary>
public sealed class FindReplaceViewModelTests : IDisposable
{
    private readonly string _settingsPath =
        Path.Combine(Path.GetTempPath(), $"signet-fr-{Guid.NewGuid():N}.json");

    private readonly Book _book = BookCreator.CreateNewBook("2.0");

    public void Dispose()
    {
        _book.Dispose();
        if (File.Exists(_settingsPath))
        {
            File.Delete(_settingsPath);
        }
    }

    private CodeTabViewModel NewTab(string text)
    {
        HtmlResource html = _book.CreateEmptyHtmlFile();
        html.SetText(text);
        var model = new TabManagerModel();
        OpenTab tab = model.OpenResource(html);
        (SettingsStore settings, SpellChecker spellChecker) = SpellCheckTestFactory.New();
        var vm = new CodeTabViewModel(tab, new StatusBarService(), settings, spellChecker);
        vm.Document.Text = text;
        vm.UpdateSelection(0, 0);
        vm.UpdateCaret(1, 1, 0, -1);

        // View emulation: after Find, and after the view sets a selection, push the selection
        // and caret back to the tab model, just like CodeTabView does.
        vm.SearchResultRequested += (s, e, _) =>
        {
            vm.UpdateSelection(s, e);
            vm.UpdateCaret(1, 1, e, -1);
        };
        vm.SelectionRequested += (s, e) =>
        {
            vm.UpdateSelection(s, e);
            vm.UpdateCaret(1, 1, e, -1);
        };
        return vm;
    }

    private FindReplaceViewModel NewPanel(CodeTabViewModel? tab, out SettingsStore settings)
    {
        settings = new SettingsStore(_settingsPath);
        SettingsStore captured = settings;
        var vm = new FindReplaceViewModel(captured, new StatusBarService(), () => tab, new FakeMultiFileSearchHost());
        vm.AttachToActiveTab(tab);
        return vm;
    }

    [Fact]
    public void FindNext_ReportsMatchAndSelectsIt()
    {
        CodeTabViewModel tab = NewTab("alpha beta alpha");
        FindReplaceViewModel panel = NewPanel(tab, out _);

        panel.FindText = "alpha";
        panel.FindNext().Should().BeTrue();

        (tab.SelectionStart, tab.SelectionEnd).Should().Be((0, 5));
    }

    private static (int Start, int End)[] Occurrences(string text, string word)
    {
        var result = new System.Collections.Generic.List<(int Start, int End)>();
        for (int i = text.IndexOf(word, StringComparison.Ordinal); i >= 0; i = text.IndexOf(word, i + 1, StringComparison.Ordinal))
        {
            result.Add((i, i + word.Length));
        }

        return result.ToArray();
    }

    /// <summary>
    /// Regression: after "Split Tag" before the second match, the highlights stayed at the old
    /// offsets (shifted by the inserted <c>&lt;/p&gt;…&lt;p&gt;</c>).
    /// </summary>
    [Fact]
    public void Highlights_follow_the_matches_after_split_tag_inserts_text_before_them()
    {
        const string text = "<html><body>\n<p class=\"c\">To jeszcze nie wszystko.</p>\n" +
                            "<p class=\"c\">Poza tym? Chce Pan.</p>\n<p class=\"c\">Jest jeszcze sprawa.</p>\n</body></html>";
        CodeTabViewModel tab = NewTab(text);
        FindReplaceViewModel panel = NewPanel(tab, out _);
        panel.HighlightAllMatches = true;
        panel.FindText = "jeszcze";
        panel.FindNext().Should().BeTrue();
        tab.SearchHighlights.Should().Equal(Occurrences(text, "jeszcze"));

        int caret = text.IndexOf("Chce", StringComparison.Ordinal);
        tab.UpdateSelection(caret, caret);
        tab.UpdateCaret(1, 1, caret, 'C');
        tab.SplitTag();

        tab.Document.Text.Should().NotBe(text);
        tab.SearchHighlights.Should().Equal(Occurrences(tab.Document.Text, "jeszcze"));
    }

    [Fact]
    public void Highlights_are_recomputed_after_typing_and_undo()
    {
        CodeTabViewModel tab = NewTab("cat dog cat");
        FindReplaceViewModel panel = NewPanel(tab, out _);
        panel.HighlightAllMatches = true;
        panel.FindText = "cat";
        panel.Count().Should().Be(2);

        tab.Document.Insert(0, "cat ");
        tab.SearchHighlights.Should().Equal((0, 3), (4, 7), (12, 15));

        tab.Document.Replace(4, 3, "cow");
        tab.SearchHighlights.Should().Equal((0, 3), (12, 15)); // the broken match disappears

        tab.Undo();
        tab.Undo();
        tab.SearchHighlights.Should().Equal((0, 3), (8, 11));
    }

    [Fact]
    public void All_matches_are_not_highlighted_by_default_only_the_current_one_is_selected()
    {
        CodeTabViewModel tab = NewTab("cat dog cat");
        FindReplaceViewModel panel = NewPanel(tab, out _);

        panel.HighlightAllMatches.Should().BeFalse();
        panel.FindText = "cat";
        panel.FindNext().Should().BeTrue();

        (tab.SelectionStart, tab.SelectionEnd).Should().Be((0, 3));
        tab.SearchHighlights.Should().BeEmpty();

        panel.Count().Should().Be(2);
        tab.SearchHighlights.Should().BeEmpty();
    }

    [Fact]
    public void Toggling_highlight_all_matches_shows_and_hides_them_immediately()
    {
        CodeTabViewModel tab = NewTab("cat dog cat");
        FindReplaceViewModel panel = NewPanel(tab, out _);
        panel.FindText = "cat";

        panel.HighlightAllMatches = true;
        tab.SearchHighlights.Should().Equal((0, 3), (8, 11));

        tab.Document.Insert(0, "cat ");
        tab.SearchHighlights.Should().Equal((0, 3), (4, 7), (12, 15));

        panel.HighlightAllMatches = false;
        tab.SearchHighlights.Should().BeEmpty();

        tab.Document.Insert(0, "cat ");
        tab.SearchHighlights.Should().BeEmpty("once disabled, editing does not bring the highlights back");
    }

    [Fact]
    public void Highlight_all_matches_is_persisted()
    {
        CodeTabViewModel tab = NewTab("cat");
        FindReplaceViewModel panel = NewPanel(tab, out SettingsStore settings);

        panel.HighlightAllMatches = true;

        settings.GetFindReplaceSettings().HighlightAllMatches.Should().BeTrue();
        new FindReplaceViewModel(settings, new StatusBarService(), () => tab, new FakeMultiFileSearchHost())
            .HighlightAllMatches.Should().BeTrue();
    }

    [Fact]
    public void Switching_to_a_tab_with_leftover_highlights_clears_them_when_the_option_is_off()
    {
        CodeTabViewModel first = NewTab("cat");
        CodeTabViewModel second = NewTab("cat cat");
        second.SetSearchHighlights(new[] { (0, 3) });
        FindReplaceViewModel panel = NewPanel(first, out _);

        panel.AttachToActiveTab(second);

        second.SearchHighlights.Should().BeEmpty();
    }

    [Fact]
    public void Static_highlights_are_not_recomputed_after_editing()
    {
        CodeTabViewModel tab = NewTab("cat dog cat");
        tab.SetSearchHighlights(new[] { (0, 3) });

        tab.Document.Insert(0, "cat ");

        tab.SearchHighlights.Should().Equal((0, 3));
    }

    [Fact]
    public void FindNext_NoMatch_SetsMessage()
    {
        CodeTabViewModel tab = NewTab("only this");
        FindReplaceViewModel panel = NewPanel(tab, out _);

        panel.FindText = "missing";
        panel.FindNext().Should().BeFalse();
        panel.Message.Should().Be(Strings.Get("FindReplace_NotFound"));
    }

    [Fact]
    public void ReplaceAll_ReplacesEveryMatchInDocument()
    {
        CodeTabViewModel tab = NewTab("cat cat cat");
        FindReplaceViewModel panel = NewPanel(tab, out _);

        panel.FindText = "cat";
        panel.ReplaceText = "dog";
        panel.ReplaceAll().Should().Be(3);

        tab.Document.Text.Should().Be("dog dog dog");
        panel.Message.Should().Contain("3");
    }

    [Fact]
    public void Replace_ThenFind_WalksThroughMatches()
    {
        CodeTabViewModel tab = NewTab("x x x");
        FindReplaceViewModel panel = NewPanel(tab, out _);
        panel.FindText = "x";
        panel.ReplaceText = "y";

        panel.FindNext().Should().BeTrue();       // selects the first "x"
        panel.ReplaceFind().Should().BeTrue();    // replaces and finds the next one
        tab.Document.Text.Should().Be("y x x");
    }

    [Fact]
    public void RegexMode_InvalidPattern_SetsRegexError()
    {
        CodeTabViewModel tab = NewTab("abc");
        FindReplaceViewModel panel = NewPanel(tab, out _);

        panel.ModeIndex = (int)SearchMode.Regex;
        panel.FindText = "(unclosed";

        panel.HasRegexError.Should().BeTrue();
        panel.RegexError.Should().NotBeEmpty();
    }

    [Fact]
    public void RegexMode_BackreferenceReplaceAll()
    {
        CodeTabViewModel tab = NewTab("2026-09-08");
        FindReplaceViewModel panel = NewPanel(tab, out _);

        panel.ModeIndex = (int)SearchMode.Regex;
        panel.FindText = @"(\d{4})-(\d{2})-(\d{2})";
        panel.ReplaceText = @"\3.\2.\1";
        panel.ReplaceAll().Should().Be(1);

        tab.Document.Text.Should().Be("08.09.2026");
    }

    [Fact]
    public void Count_ReportsMatchCount()
    {
        CodeTabViewModel tab = NewTab("a a a a a");
        FindReplaceViewModel panel = NewPanel(tab, out _);

        panel.FindText = "a";
        panel.Count().Should().Be(5);
        panel.Message.Should().Contain("5");
    }

    [Fact]
    public void History_IsPersistedAndReloaded()
    {
        CodeTabViewModel tab = NewTab("hello hello");
        FindReplaceViewModel panel = NewPanel(tab, out SettingsStore settings);

        panel.FindText = "hello";
        panel.ReplaceText = "hi";
        panel.ReplaceAll();

        panel.FindHistory.Should().Contain("hello");
        panel.ReplaceHistory.Should().Contain("hi");

        var reloaded = new FindReplaceViewModel(settings, new StatusBarService(), () => tab, new FakeMultiFileSearchHost());
        reloaded.FindHistory.Should().Contain("hello");
        reloaded.ReplaceHistory.Should().Contain("hi");
    }

    [Fact]
    public void Options_ArePersisted()
    {
        CodeTabViewModel tab = NewTab("abc");
        FindReplaceViewModel panel = NewPanel(tab, out SettingsStore settings);

        panel.ModeIndex = (int)SearchMode.Regex;
        panel.DirectionIndex = (int)SearchDirection.Up;
        panel.OptionWrap = false;
        panel.RegexDotAll = true;

        FindReplaceSettings stored = settings.GetFindReplaceSettings();
        stored.Mode.Should().Be(SearchMode.Regex);
        stored.Direction.Should().Be(SearchDirection.Up);
        stored.OptionWrap.Should().BeFalse();
        stored.RegexDotAll.Should().BeTrue();
    }

    [Fact]
    public void RestrictToSelection_MarksRegionAndLimitsReplaceAll()
    {
        CodeTabViewModel tab = NewTab("cat cat cat cat");
        FindReplaceViewModel panel = NewPanel(tab, out _);

        tab.UpdateSelection(4, 11); // second + third "cat"
        panel.RestrictToSelection = true;
        tab.Search.Marked.IsMarked.Should().BeTrue();

        tab.UpdateCaret(1, 5, 4, -1);
        panel.FindText = "cat";
        panel.ReplaceText = "dog";
        panel.ReplaceAll().Should().Be(2);

        tab.Document.Text.Should().Be("cat dog dog cat");
    }

    [Fact]
    public void TypingInDocument_ClearsMarkedRegion()
    {
        CodeTabViewModel tab = NewTab("cat cat");
        FindReplaceViewModel panel = NewPanel(tab, out _);

        tab.UpdateSelection(0, 3);
        panel.RestrictToSelection = true;
        tab.Search.Marked.IsMarked.Should().BeTrue();

        tab.Document.Insert(0, "X"); // simulated typing

        tab.Search.Marked.IsMarked.Should().BeFalse();
        panel.RestrictToSelection.Should().BeFalse();
    }

    [Fact]
    public void NoActiveTab_FindSetsMessage()
    {
        FindReplaceViewModel panel = NewPanel(null, out _);
        panel.FindText = "x";
        panel.FindNext().Should().BeFalse();
        panel.Message.Should().Contain(Strings.Get("FindReplace_NoActiveCodeTab"));
    }

    [Fact]
    public void SeedFindFromSelection_AutoTokeniseOffInRegexMode_CopiesSelectionVerbatim()
    {
        CodeTabViewModel tab = NewTab("a.b*c rest");
        FindReplaceViewModel panel = NewPanel(tab, out _);
        panel.ModeIndex = (int)SearchMode.Regex;
        panel.RegexAutoTokenise = false;
        tab.UpdateSelection(0, 5); // "a.b*c"

        panel.SeedFindFromSelection();

        panel.FindText.Should().Be("a.b*c");
    }

    [Fact]
    public void SeedFindFromSelection_AutoTokeniseOnInRegexMode_EscapesSelection()
    {
        CodeTabViewModel tab = NewTab("a.b*c rest");
        FindReplaceViewModel panel = NewPanel(tab, out _);
        panel.ModeIndex = (int)SearchMode.Regex;
        panel.RegexAutoTokenise = true;
        tab.UpdateSelection(0, 5); // "a.b*c"

        panel.SeedFindFromSelection();

        panel.FindText.Should().Be(@"a\.b\*c");
    }

    [Fact]
    public void SeedFindFromSelection_AutoTokeniseOnInNormalMode_DoesNotEscape()
    {
        // Auto Tokenise applies only to Regex mode.
        CodeTabViewModel tab = NewTab("a.b*c rest");
        FindReplaceViewModel panel = NewPanel(tab, out _);
        panel.ModeIndex = (int)SearchMode.Normal;
        panel.RegexAutoTokenise = true;
        tab.UpdateSelection(0, 5); // "a.b*c"

        panel.SeedFindFromSelection();

        panel.FindText.Should().Be("a.b*c");
    }

    [Fact]
    public void RegexAutoTokenise_RoundTripsThroughSettings()
    {
        CodeTabViewModel tab = NewTab("x");
        FindReplaceViewModel panel = NewPanel(tab, out SettingsStore settings);

        panel.RegexAutoTokenise = true;

        settings.GetFindReplaceSettings().RegexAutoTokenise.Should().BeTrue();
    }
}
