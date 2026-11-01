using System;
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
public sealed class SpellcheckEditorEngineTests : IDisposable
{
    private readonly Disposables _disposables = new();

    /// <inheritdoc />
    public void Dispose() => _disposables.Dispose();

    private (Book Book, HtmlResource First, HtmlResource Second) NewTwoFileBook()
    {
        Book book = _disposables.Add(BookCreator.CreateNewBook("2.0"));
        HtmlResource first = book.GetHtmlResources()[0];
        HtmlResource second = book.CreateEmptyHtmlFile();
        return (book, first, second);
    }

    private static (SettingsStore Settings, SpellChecker SpellChecker) NewEngine(
        TempDir dicts, TempDir userDicts, params string[] correctWords)
    {
        DictionaryFixture.Write(dicts.Path, "en_US", correctWords);
        SettingsStore settings = new(dicts.Combine("settings.xml"));
        settings.Dictionary = "en_US";
        SpellChecker spellChecker = new(settings, dicts.Path, userDicts.Path);
        return (settings, spellChecker);
    }

    [Fact]
    public void GetUniqueWords_counts_occurrences_across_all_html_files()
    {
        // Arrange
        using TempDir dicts = new();
        using TempDir userDicts = new();
        (SettingsStore settings, SpellChecker spellChecker) = NewEngine(dicts, userDicts, "hello", "world");
        (Book book, HtmlResource first, HtmlResource second) = NewTwoFileBook();
        first.SetText("<p>hello world</p>");
        second.SetText("<p>hello there</p>");

        // Act
        var words = SpellcheckEditorEngine.GetUniqueWords(book, spellChecker, settings);

        // Assert
        words.Single(w => w.Text == "hello").Count.Should().Be(2);
        words.Single(w => w.Text == "world").Count.Should().Be(1);
        words.Single(w => w.Text == "there").Count.Should().Be(1);
    }

    /// <summary>
    /// Every occurrence of a word is kept, with the file and the line it stands in: the Spelling Editor shows them
    /// under the word, so that each can be gone to and replaced on its own.
    /// </summary>
    [Fact]
    public void GetUniqueWords_records_every_occurrence_with_its_file_and_line()
    {
        // Arrange
        using TempDir dicts = new();
        using TempDir userDicts = new();
        (SettingsStore settings, SpellChecker spellChecker) = NewEngine(dicts, userDicts, "hello", "world");
        (Book book, HtmlResource first, HtmlResource second) = NewTwoFileBook();
        first.SetText("<p>hello world</p>\n<p>hello again</p>");
        second.SetText("<p>hello there</p>");

        // Act
        SpellcheckWord hello = SpellcheckEditorEngine.GetUniqueWords(book, spellChecker, settings)
            .Single(w => w.Text == "hello");

        // Assert
        hello.Occurrences.Should().HaveCount(3);
        hello.Occurrences.Select(o => o.Line).Should().Equal(1, 2, 1);
        hello.Occurrences.Select(o => o.BookPath).Should().Equal(first.BookPath, first.BookPath, second.BookPath);
        hello.Occurrences[0].Position.Should().Be(first.GetText().IndexOf("hello", StringComparison.Ordinal));
        hello.BookPath.Should().Be(first.BookPath, "the word's own place is its first occurrence");
        hello.Position.Should().Be(hello.Occurrences[0].Position);
    }

    /// <summary>"Replace" in the Spelling Editor changes one occurrence and leaves the others alone.</summary>
    [Fact]
    public void ReplaceOccurrence_replaces_only_the_occurrence_at_the_given_place()
    {
        // Arrange
        using TempDir dicts = new();
        using TempDir userDicts = new();
        (SettingsStore settings, SpellChecker spellChecker) = NewEngine(dicts, userDicts, "world");
        (Book book, HtmlResource first, _) = NewTwoFileBook();
        first.SetText("<p>wrold and wrold</p>");
        int second = first.GetText().LastIndexOf("wrold", StringComparison.Ordinal);

        // Act
        bool replaced = SpellcheckEditorEngine.ReplaceOccurrence(book, first.BookPath, second, "wrold", "world");

        // Assert
        replaced.Should().BeTrue();
        first.GetText().Should().Be("<p>wrold and world</p>");
    }

    /// <summary>
    /// A place that no longer holds the word is left alone: the file may have changed since the table was built.
    /// </summary>
    [Fact]
    public void ReplaceOccurrence_does_nothing_when_the_word_is_not_at_that_place()
    {
        // Arrange
        using TempDir dicts = new();
        using TempDir userDicts = new();
        (SettingsStore settings, SpellChecker spellChecker) = NewEngine(dicts, userDicts, "world");
        (Book book, HtmlResource first, _) = NewTwoFileBook();
        first.SetText("<p>wrold</p>");

        // Act & Assert
        SpellcheckEditorEngine.ReplaceOccurrence(book, first.BookPath, 0, "wrold", "world").Should().BeFalse();
        SpellcheckEditorEngine.ReplaceOccurrence(book, "Text/missing.xhtml", 3, "wrold", "world").Should().BeFalse();
        SpellcheckEditorEngine.ReplaceOccurrence(book, first.BookPath, 9999, "wrold", "world").Should().BeFalse();
        first.GetText().Should().Be("<p>wrold</p>");
    }

    [Fact]
    public void GetUniqueWords_marks_misspelled_words()
    {
        // Arrange
        using TempDir dicts = new();
        using TempDir userDicts = new();
        (SettingsStore settings, SpellChecker spellChecker) = NewEngine(dicts, userDicts, "hello");
        (Book book, HtmlResource first, _) = NewTwoFileBook();
        first.SetText("<p>hello wrold</p>");

        // Act
        var words = SpellcheckEditorEngine.GetUniqueWords(book, spellChecker, settings);

        // Assert
        words.Single(w => w.Text == "hello").Misspelled.Should().BeFalse();
        words.Single(w => w.Text == "wrold").Misspelled.Should().BeTrue();
    }

    [Fact]
    public void GetUniqueWords_records_first_occurrence_bookpath_and_position()
    {
        // Arrange
        using TempDir dicts = new();
        using TempDir userDicts = new();
        (SettingsStore settings, SpellChecker spellChecker) = NewEngine(dicts, userDicts, "hello");
        (Book book, HtmlResource first, HtmlResource second) = NewTwoFileBook();
        first.SetText("<p>one</p>");
        second.SetText("<p>hello</p>");

        // Act
        var words = SpellcheckEditorEngine.GetUniqueWords(book, spellChecker, settings);

        // Assert
        var hello = words.Single(w => w.Text == "hello");
        hello.BookPath.Should().Be(second.BookPath);
        second.GetText().Substring(hello.Position, "hello".Length).Should().Be("hello");
    }

    [Fact]
    public void ReplaceWordInAllFiles_replaces_all_occurrences_in_all_files()
    {
        // Arrange
        using TempDir dicts = new();
        using TempDir userDicts = new();
        (SettingsStore settings, SpellChecker spellChecker) = NewEngine(dicts, userDicts, "hello");
        (Book book, HtmlResource first, HtmlResource second) = NewTwoFileBook();
        first.SetText("<p>wrold is wrold</p>");
        second.SetText("<p>another wrold</p>");

        // Act
        int replaced = SpellcheckEditorEngine.ReplaceWordInAllFiles(book, spellChecker, settings, "wrold", "en", "world");

        // Assert
        replaced.Should().Be(3);
        first.GetText().Should().Be("<p>world is world</p>");
        second.GetText().Should().Be("<p>another world</p>");
    }

    [Fact]
    public void ReplaceWordInAllFiles_only_matches_exact_text_and_language()
    {
        // Arrange
        using TempDir dicts = new();
        using TempDir userDicts = new();
        (SettingsStore settings, SpellChecker spellChecker) = NewEngine(dicts, userDicts, "hello");
        (Book book, HtmlResource first, HtmlResource second) = NewTwoFileBook();
        first.SetText("<p>wrold</p>");
        second.SetText("<p lang=\"de\">wrold</p>");

        // Act
        int replaced = SpellcheckEditorEngine.ReplaceWordInAllFiles(book, spellChecker, settings, "wrold", "en", "world");

        // Assert
        replaced.Should().Be(1);
        first.GetText().Should().Be("<p>world</p>");
        second.GetText().Should().Be("<p lang=\"de\">wrold</p>");
    }

    [Fact]
    public void ReplaceWordInAllFiles_returns_zero_when_word_is_not_found()
    {
        // Arrange
        using TempDir dicts = new();
        using TempDir userDicts = new();
        (SettingsStore settings, SpellChecker spellChecker) = NewEngine(dicts, userDicts, "hello");
        (Book book, HtmlResource first, _) = NewTwoFileBook();
        const string original = "<p>hello</p>";
        first.SetText(original);

        // Act
        int replaced = SpellcheckEditorEngine.ReplaceWordInAllFiles(book, spellChecker, settings, "missing", "en", "world");

        // Assert
        replaced.Should().Be(0);
        first.GetText().Should().Be(original);
    }
}
