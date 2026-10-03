using System.Linq;
using AwesomeAssertions;
using Signet.App.Services;
using Signet.App.Tests.TestSupport;
using Signet.App.ViewModels.Tabs;
using Signet.Core.BookManipulation;
using Signet.Core.MainUI;
using Signet.Core.Misc;
using Signet.Core.Resources;
using Signet.Core.Spellcheck;
using Xunit;

namespace Signet.App.Tests;

/// <summary>
/// Spellcheck tests for <see cref="CodeTabViewModel"/>: highlights, the word under the caret,
/// Add/Ignore, "Next Misspelled Word". Uses the real bundled <c>en_US</c> dictionary; the test
/// words are chosen so that they are unambiguously correct or misspelled.
/// </summary>
public sealed class CodeTabViewModelSpellcheckTests
{
    private static CodeTabViewModel NewTab(string text, SettingsStore settings, SpellChecker spellChecker)
    {
        Book book = BookCreator.CreateNewBook("2.0");
        HtmlResource html = book.CreateEmptyHtmlFile();
        html.SetText(text);
        var model = new TabManagerModel();
        OpenTab tab = model.OpenResource(html);
        var vm = new CodeTabViewModel(tab, new StatusBarService(), settings, spellChecker);
        vm.Document.Text = text;
        return vm;
    }

    private const string Html = "<html><body><p>hello wrold</p></body></html>";

    [Fact]
    public void No_highlights_when_spellcheck_setting_is_disabled()
    {
        (SettingsStore settings, SpellChecker spellChecker) = SpellCheckTestFactory.New();
        settings.SpellCheck = false;

        CodeTabViewModel sut = NewTab(Html, settings, spellChecker);

        sut.MisspelledWordHighlights.Should().BeEmpty();
    }

    [Fact]
    public void Highlights_the_misspelled_word_when_spellcheck_is_enabled()
    {
        (SettingsStore settings, SpellChecker spellChecker) = SpellCheckTestFactory.New();
        settings.SpellCheck = true;

        CodeTabViewModel sut = NewTab(Html, settings, spellChecker);

        sut.MisspelledWordHighlights.Should().ContainSingle();
        (int start, int end) = sut.MisspelledWordHighlights.Single();
        sut.Document.GetText(start, end - start).Should().Be("wrold");
    }

    [Fact]
    public void GetMisspelledWordAtCaret_matches_only_strictly_inside_the_word()
    {
        (SettingsStore settings, SpellChecker spellChecker) = SpellCheckTestFactory.New();
        settings.SpellCheck = true;
        CodeTabViewModel sut = NewTab(Html, settings, spellChecker);
        int wordStart = Html.IndexOf("wrold", System.StringComparison.Ordinal);

        sut.UpdateCaret(1, 1, wordStart, -1);
        sut.GetMisspelledWordAtCaret().Should().BeNull();

        sut.UpdateCaret(1, 1, wordStart + 2, -1);
        sut.GetMisspelledWordAtCaret()?.Text.Should().Be("wrold");
    }

    [Fact]
    public void AddCurrentMisspelledWordToDictionary_removes_it_from_highlights()
    {
        (SettingsStore settings, SpellChecker spellChecker) = SpellCheckTestFactory.New();
        settings.SpellCheck = true;
        CodeTabViewModel sut = NewTab(Html, settings, spellChecker);
        int wordStart = Html.IndexOf("wrold", System.StringComparison.Ordinal);
        sut.UpdateCaret(1, 1, wordStart + 2, -1);

        sut.AddCurrentMisspelledWordToDictionary();

        sut.MisspelledWordHighlights.Should().BeEmpty();
        spellChecker.Check("wrold").Should().BeTrue();
    }

    [Fact]
    public void IgnoreCurrentMisspelledWord_removes_it_from_highlights_for_the_session()
    {
        (SettingsStore settings, SpellChecker spellChecker) = SpellCheckTestFactory.New();
        settings.SpellCheck = true;
        CodeTabViewModel sut = NewTab(Html, settings, spellChecker);
        int wordStart = Html.IndexOf("wrold", System.StringComparison.Ordinal);
        sut.UpdateCaret(1, 1, wordStart + 2, -1);

        sut.IgnoreCurrentMisspelledWord();

        sut.MisspelledWordHighlights.Should().BeEmpty();
        spellChecker.IsIgnored("wrold").Should().BeTrue();
    }

    [Fact]
    public void GoToNextMisspelledWord_raises_SearchResultRequested_for_the_next_word_and_wraps_around()
    {
        (SettingsStore settings, SpellChecker spellChecker) = SpellCheckTestFactory.New();
        settings.SpellCheck = true;
        const string html = "<html><body><p>wrold and wrold</p></body></html>";
        CodeTabViewModel sut = NewTab(html, settings, spellChecker);
        sut.MisspelledWordHighlights.Should().HaveCount(2);

        (int Start, int End, bool Wrapped)? found = null;
        sut.SearchResultRequested += (start, end, wrapped) => found = (start, end, wrapped);

        sut.UpdateCaret(1, 1, 0, -1);
        sut.GoToNextMisspelledWord();

        found.Should().NotBeNull();
        found!.Value.Wrapped.Should().BeFalse();
        sut.Document.GetText(found.Value.Start, found.Value.End - found.Value.Start).Should().Be("wrold");

        // Caret after both matches -> wraps around to the first one.
        sut.UpdateCaret(1, 1, html.Length, -1);
        found = null;
        sut.GoToNextMisspelledWord();

        found.Should().NotBeNull();
        found!.Value.Wrapped.Should().BeTrue();
    }

    [Fact]
    public void GetSuggestionsFor_returns_non_empty_suggestions_for_a_misspelled_word()
    {
        (SettingsStore settings, SpellChecker spellChecker) = SpellCheckTestFactory.New();
        settings.SpellCheck = true;
        CodeTabViewModel sut = NewTab(Html, settings, spellChecker);
        int wordStart = Html.IndexOf("wrold", System.StringComparison.Ordinal);
        sut.UpdateCaret(1, 1, wordStart + 2, -1);

        HtmlWord word = sut.GetMisspelledWordAtCaret()!.Value;
        sut.GetSuggestionsFor(word).Should().Contain("world");
    }
}
