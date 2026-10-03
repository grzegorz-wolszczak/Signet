using System.Linq;
using AwesomeAssertions;
using Signet.Core.BookManipulation;
using Signet.Core.Misc;
using Signet.Core.Resources;
using Signet.Core.Spellcheck;
using Signet.Core.Tests.TestSupport;
using Xunit;

namespace Signet.Core.Tests.Spellcheck;

/// <summary>
/// Tests for <see cref="SpellcheckEditorEngine"/> — aggregating the unique words of the whole book
/// with occurrence counts and spelling status, and replacing a word across all (X)HTML files.
/// </summary>
public sealed class SpellcheckEditorEngineTests
{
    private static (Book Book, HtmlResource First, HtmlResource Second) NewTwoFileBook()
    {
        Book book = BookCreator.CreateNewBook("2.0");
        HtmlResource first = book.GetHtmlResources()[0];
        HtmlResource second = book.CreateEmptyHtmlFile();
        return (book, first, second);
    }

    private static (SettingsStore Settings, SpellChecker SpellChecker) NewEngine(
        TempDir dicts, TempDir userDicts, params string[] correctWords)
    {
        DictionaryFixture.Write(dicts.Path, "en_US", correctWords);
        SettingsStore settings = new(dicts.Combine("settings.json"));
        settings.Dictionary = "en_US";
        SpellChecker spellChecker = new(settings, dicts.Path, userDicts.Path);
        return (settings, spellChecker);
    }

    [Fact]
    public void GetUniqueWords_counts_occurrences_across_all_html_files()
    {
        using TempDir dicts = new();
        using TempDir userDicts = new();
        (SettingsStore settings, SpellChecker spellChecker) = NewEngine(dicts, userDicts, "hello", "world");
        (Book book, HtmlResource first, HtmlResource second) = NewTwoFileBook();
        first.SetText("<p>hello world</p>");
        second.SetText("<p>hello there</p>");

        var words = SpellcheckEditorEngine.GetUniqueWords(book, spellChecker, settings);

        words.Single(w => w.Text == "hello").Count.Should().Be(2);
        words.Single(w => w.Text == "world").Count.Should().Be(1);
        words.Single(w => w.Text == "there").Count.Should().Be(1);
    }

    [Fact]
    public void GetUniqueWords_marks_misspelled_words()
    {
        using TempDir dicts = new();
        using TempDir userDicts = new();
        (SettingsStore settings, SpellChecker spellChecker) = NewEngine(dicts, userDicts, "hello");
        (Book book, HtmlResource first, _) = NewTwoFileBook();
        first.SetText("<p>hello wrold</p>");

        var words = SpellcheckEditorEngine.GetUniqueWords(book, spellChecker, settings);

        words.Single(w => w.Text == "hello").Misspelled.Should().BeFalse();
        words.Single(w => w.Text == "wrold").Misspelled.Should().BeTrue();
    }

    [Fact]
    public void GetUniqueWords_records_first_occurrence_bookpath_and_position()
    {
        using TempDir dicts = new();
        using TempDir userDicts = new();
        (SettingsStore settings, SpellChecker spellChecker) = NewEngine(dicts, userDicts, "hello");
        (Book book, HtmlResource first, HtmlResource second) = NewTwoFileBook();
        first.SetText("<p>one</p>");
        second.SetText("<p>hello</p>");

        var words = SpellcheckEditorEngine.GetUniqueWords(book, spellChecker, settings);

        var hello = words.Single(w => w.Text == "hello");
        hello.BookPath.Should().Be(second.BookPath);
        second.GetText().Substring(hello.Position, "hello".Length).Should().Be("hello");
    }

    [Fact]
    public void ReplaceWordInAllFiles_replaces_all_occurrences_in_all_files()
    {
        using TempDir dicts = new();
        using TempDir userDicts = new();
        (SettingsStore settings, SpellChecker spellChecker) = NewEngine(dicts, userDicts, "hello");
        (Book book, HtmlResource first, HtmlResource second) = NewTwoFileBook();
        first.SetText("<p>wrold is wrold</p>");
        second.SetText("<p>another wrold</p>");

        int replaced = SpellcheckEditorEngine.ReplaceWordInAllFiles(book, spellChecker, settings, "wrold", "en", "world");

        replaced.Should().Be(3);
        first.GetText().Should().Be("<p>world is world</p>");
        second.GetText().Should().Be("<p>another world</p>");
    }

    [Fact]
    public void ReplaceWordInAllFiles_only_matches_exact_text_and_language()
    {
        using TempDir dicts = new();
        using TempDir userDicts = new();
        (SettingsStore settings, SpellChecker spellChecker) = NewEngine(dicts, userDicts, "hello");
        (Book book, HtmlResource first, HtmlResource second) = NewTwoFileBook();
        first.SetText("<p>wrold</p>");
        second.SetText("<p lang=\"de\">wrold</p>");

        int replaced = SpellcheckEditorEngine.ReplaceWordInAllFiles(book, spellChecker, settings, "wrold", "en", "world");

        replaced.Should().Be(1);
        first.GetText().Should().Be("<p>world</p>");
        second.GetText().Should().Be("<p lang=\"de\">wrold</p>");
    }

    [Fact]
    public void ReplaceWordInAllFiles_returns_zero_when_word_is_not_found()
    {
        using TempDir dicts = new();
        using TempDir userDicts = new();
        (SettingsStore settings, SpellChecker spellChecker) = NewEngine(dicts, userDicts, "hello");
        (Book book, HtmlResource first, _) = NewTwoFileBook();
        const string original = "<p>hello</p>";
        first.SetText(original);

        int replaced = SpellcheckEditorEngine.ReplaceWordInAllFiles(book, spellChecker, settings, "missing", "en", "world");

        replaced.Should().Be(0);
        first.GetText().Should().Be(original);
    }
}
