using System;
using System.Linq;
using AwesomeAssertions;
using Signet.App.Tests.TestSupport;
using Signet.App.ViewModels;
using Signet.Core.BookManipulation;
using Signet.Core.Misc;
using Signet.Core.Resources;
using Signet.Core.Spellcheck;
using Signet.Core.Tests.TestSupport;
using Xunit;

namespace Signet.App.Tests;

/// <summary>
/// Tests for <see cref="SpellcheckEditorViewModel"/> (the "Spellcheck Editor" dialog), independent
/// of <see cref="MainWindowViewModel"/>. Uses the real bundled <c>en_US</c> dictionary: "wrold" is an
/// unambiguously misspelled test word, "hello"/"world" are unambiguously correct.
/// </summary>
public sealed class SpellcheckEditorViewModelTests : IDisposable
{
    private readonly Disposables _disposables = new();

    /// <inheritdoc />
    public void Dispose() => _disposables.Dispose();

    private (SpellcheckEditorViewModel Editor, Book Book, HtmlResource Html) New(string text)
    {
        (SettingsStore settings, SpellChecker spellChecker) = _disposables.Add(SpellCheckTestFactory.New());
        Book book = _disposables.Add(BookCreator.CreateNewBook("2.0"));
        HtmlResource html = book.GetHtmlResources()[0];
        html.SetText(text);

        SpellcheckEditorViewModel editor = _disposables.Add(new SpellcheckEditorViewModel(spellChecker, settings));
        editor.Refresh(book);
        return (editor, book, html);
    }

    [Fact]
    public void Refresh_shows_only_misspelled_words_by_default()
    {
        // Arrange
        (SpellcheckEditorViewModel editor, _, _) = New("<p>hello wrold</p>");

        // Act & Assert
        editor.Words.Select(w => w.Word).Should().Equal("wrold");
    }

    /// <summary>
    /// A word that stands in the book more than once has a row per occurrence under it, so that each can be gone to
    /// and replaced on its own; a word that stands there once is its own occurrence and gets no expander.
    /// </summary>
    [Fact]
    public void A_word_of_several_occurrences_has_a_row_for_each_of_them()
    {
        // Arrange
        (SpellcheckEditorViewModel editor, _, HtmlResource html) = New("<p>wrold and wrold</p>\n<p>tzzz</p>");

        // Act
        SpellcheckWordRow repeated = editor.Words.Single(w => w.Word == "wrold");
        SpellcheckWordRow once = editor.Words.Single(w => w.Word == "tzzz");

        // Assert
        once.Occurrences.Should().BeEmpty();
        once.IsSinglePlace.Should().BeTrue();
        repeated.Occurrences.Should().HaveCount(2);
        repeated.IsSinglePlace.Should().BeFalse("a word of several occurrences is no single place in the book");
        repeated.Occurrences.Select(o => o.OccurrenceNumber).Should().Equal(1, 2);
        repeated.Occurrences.Should().OnlyContain(o => o.Word == "wrold" && o.IsOccurrence && o.IsSinglePlace);
        repeated.Occurrences[1].Position.Should().Be(html.GetText().LastIndexOf("wrold", StringComparison.Ordinal));
        repeated.Occurrences[0].Display.Should().Be(html.BookPath + " : 1");
        repeated.Occurrences[1].CountDisplay.Should().Be("2", "an occurrence row counts itself, not the word");
    }

    /// <summary>
    /// "Replace" works on one place in the book: an occurrence row, or a word that occurs once. With a word of several
    /// occurrences selected there is nothing to say which place is meant, so it is unavailable.
    /// </summary>
    [Fact]
    public void Replace_works_on_one_occurrence_only()
    {
        // Arrange
        (SpellcheckEditorViewModel editor, _, HtmlResource html) = New("<p>wrold and wrold</p>");
        SpellcheckWordRow word = editor.Words.Single(w => w.Word == "wrold");
        SpellcheckOccurrenceReplacement? asked = null;
        editor.ReplaceOneRequested += replacement => asked = replacement;

        // Act
        editor.SetSelectedWords(new[] { word });
        editor.ChangeAllText = "world";

        // Assert
        editor.ReplaceOneCommand.CanExecute(null).Should().BeFalse("the word stands in two places");

        // Act
        editor.SetSelectedWords(new[] { word.Occurrences[1] });
        editor.ChangeAllText = "world";

        // Assert
        editor.ReplaceOneCommand.CanExecute(null).Should().BeTrue();

        // Act
        editor.ReplaceOneCommand.Execute(null);

        // Assert
        asked.Should().NotBeNull();
        asked!.Value.Word.Should().Be("wrold");
        asked.Value.NewText.Should().Be("world");
        asked.Value.BookPath.Should().Be(html.BookPath);
        asked.Value.Position.Should().Be(html.GetText().LastIndexOf("wrold", StringComparison.Ordinal));
    }

    [Fact]
    public void ShowAllWords_true_also_shows_correctly_spelled_words()
    {
        // Arrange
        (SpellcheckEditorViewModel editor, _, _) = New("<p>hello wrold</p>");

        // Act
        editor.ShowAllWords = true;

        // Assert
        editor.Words.Select(w => w.Word).Should().Contain("hello").And.Contain("wrold");
    }

    [Fact]
    public void FilterText_filters_words_by_substring()
    {
        // Arrange
        (SpellcheckEditorViewModel editor, _, _) = New("<p>wrold wrongg</p>");

        // Act
        editor.FilterText = "wro";

        // Assert
        editor.Words.Select(w => w.Word).Should().Equal("wrold", "wrongg");

        // Act
        editor.FilterText = "old";

        // Assert
        editor.Words.Select(w => w.Word).Should().Equal("wrold");
    }

    [Theory]
    [InlineData(nameof(SpellcheckEditorViewModel.HideAllCaps), "WRLDX")]
    [InlineData(nameof(SpellcheckEditorViewModel.HideCamelCase), "wrldXq")]
    [InlineData(nameof(SpellcheckEditorViewModel.HideSnakeCase), "wrl_dxq")]
    public void A_hide_option_removes_its_kind_of_words_from_the_list_and_is_remembered(string option, string word)
    {
        // Arrange
        (SpellcheckEditorViewModel editor, _, _) = New($"<p>wrold {word}</p>");

        // Act & Assert
        editor.Words.Select(w => w.Word).Should().Contain(word);
        int before = editor.VisibleWordCount;

        // Act
        typeof(SpellcheckEditorViewModel).GetProperty(option)!.SetValue(editor, true);

        // Assert
        editor.Words.Select(w => w.Word).Should().Equal("wrold");
        editor.VisibleWordCount.Should().Be(before - 1);
        typeof(SpellcheckEditorViewModel).GetProperty(option)!.GetValue(editor).Should().Be(true);
    }

    [Fact]
    public void BuildCsv_lists_the_visible_words_with_a_header()
    {
        // Arrange
        (SpellcheckEditorViewModel editor, _, _) = New("<p>wrold wrold wrongg</p>");
        editor.FilterText = "wrold";

        // Act
        string[] lines = editor.BuildCsv().Split('\n', System.StringSplitOptions.RemoveEmptyEntries);

        // Assert
        lines.Should().HaveCount(2);
        lines[1].Should().StartWith("wrold,2,");
    }

    [Fact]
    public void Ignore_selects_the_word_that_moves_into_the_row_of_the_ignored_one()
    {
        // Arrange
        (SpellcheckEditorViewModel editor, _, _) = New("<p>wrold wrongg wrongx</p>");
        string[] before = editor.Words.Select(w => w.Word).ToArray();
        int? requested = null;
        editor.SelectRowRequested += (_, index) => requested = index;
        editor.SetSelectedWords(new[] { editor.Words[1] });

        // Act
        editor.IgnoreCommand.Execute(null);

        // Assert
        requested.Should().Be(1);
        editor.SingleSelectedRow!.Word.Should().Be(before[2]);
    }

    [Fact]
    public void Refresh_drops_the_selection_of_the_rebuilt_rows()
    {
        // Arrange
        (SpellcheckEditorViewModel editor, Book book, _) = New("<p>wrold wrongg</p>");
        editor.SetSelectedWords(new[] { editor.Words[0] });

        // Act
        editor.Refresh(book);

        // Assert
        editor.SingleSelectedRow.Should().BeNull("a stale row would make Change All act on a word the table no longer shows");
    }

    [Fact]
    public void Ignore_without_a_selection_sets_a_message_and_changes_nothing()
    {
        // Arrange
        (SpellcheckEditorViewModel editor, _, _) = New("<p>wrold</p>");

        // Act
        editor.IgnoreCommand.Execute(null);

        // Assert
        editor.Message.Should().NotBeEmpty();
        editor.Words.Should().ContainSingle();
    }

    [Fact]
    public void Ignore_marks_the_selected_word_correct_and_removes_it_from_the_misspelled_only_view()
    {
        // Arrange
        (SpellcheckEditorViewModel editor, _, _) = New("<p>wrold</p>");
        editor.SetSelectedWords(editor.Words);

        // Act
        editor.IgnoreCommand.Execute(null);

        // Assert
        editor.Words.Should().BeEmpty();
    }

    [Fact]
    public void Add_without_a_selection_sets_a_message_and_changes_nothing()
    {
        // Arrange
        (SpellcheckEditorViewModel editor, _, _) = New("<p>wrold</p>");

        // Act
        editor.AddCommand.Execute(null);

        // Assert
        editor.Message.Should().NotBeEmpty();
        editor.Words.Should().ContainSingle();
    }

    [Fact]
    public void Add_adds_the_selected_word_to_the_default_user_dictionary_and_marks_it_correct()
    {
        // Arrange
        (SpellcheckEditorViewModel editor, _, _) = New("<p>wrold</p>");
        editor.SetSelectedWords(editor.Words);

        // Act
        editor.AddCommand.Execute(null);

        // Assert
        editor.Words.Should().BeEmpty();
    }

    [Fact]
    public void ChangeAll_without_exactly_one_selected_word_sets_a_message_and_does_not_raise_the_event()
    {
        // Arrange
        (SpellcheckEditorViewModel editor, _, _) = New("<p>wrold</p>");
        bool raised = false;
        editor.ChangeAllRequested += (_, _, _) => raised = true;

        // Act
        editor.ChangeAllCommand.Execute(null);

        // Assert
        editor.Message.Should().NotBeEmpty();
        raised.Should().BeFalse();
    }

    [Fact]
    public void ChangeAll_rejects_replacement_text_containing_markup_characters()
    {
        // Arrange
        (SpellcheckEditorViewModel editor, _, _) = New("<p>wrold</p>");
        editor.SetSelectedWords(editor.Words);
        editor.ChangeAllText = "wo<rld>";
        bool raised = false;
        editor.ChangeAllRequested += (_, _, _) => raised = true;

        // Act
        editor.ChangeAllCommand.Execute(null);

        // Assert
        editor.Message.Should().NotBeEmpty();
        raised.Should().BeFalse();
    }

    [Fact]
    public void ChangeAll_raises_ChangeAllRequested_with_the_selected_word_language_and_new_text()
    {
        // Arrange
        (SpellcheckEditorViewModel editor, _, _) = New("<p>wrold</p>");
        editor.SetSelectedWords(editor.Words);
        editor.ChangeAllText = "world";
        (string Word, string Lang, string NewWord)? received = null;
        editor.ChangeAllRequested += (word, lang, newWord) => received = (word, lang, newWord);

        // Act
        editor.ChangeAllCommand.Execute(null);

        // Assert
        received.Should().Be(("wrold", "en", "world"));
    }

    [Fact]
    public void ChangeAll_is_disabled_while_another_book_operation_is_busy_and_enabled_again_once_it_is_not()
    {
        // Arrange
        (SpellcheckEditorViewModel editor, _, _) = New("<p>wrold</p>");
        editor.SetSelectedWords(editor.Words);
        editor.ChangeAllText = "world";
        editor.ChangeAllCommand.CanExecute(null).Should().BeTrue("a precondition of this test: one word is selected");

        // Act
        editor.IsBookOperationBusy = true;

        // Assert
        editor.ChangeAllCommand.CanExecute(null).Should().BeFalse();

        // Act
        editor.IsBookOperationBusy = false;

        // Assert
        editor.ChangeAllCommand.CanExecute(null).Should().BeTrue();
    }

    [Fact]
    public void SetSelectedWords_with_a_single_row_seeds_suggestions_and_change_all_text()
    {
        // Arrange
        (SpellcheckEditorViewModel editor, _, _) = New("<p>wrold</p>");

        // Act
        editor.SetSelectedWords(editor.Words);

        // Assert
        editor.Suggestions.Should().Contain("world");
        editor.ChangeAllText.Should().Be(editor.Suggestions[0]);
    }

    [Fact]
    public void SetSelectedWords_with_multiple_rows_clears_change_all_text()
    {
        // Arrange
        (SpellcheckEditorViewModel editor, _, _) = New("<p>wrold wrongg</p>");

        // Act
        editor.SetSelectedWords(editor.Words);

        // Assert
        editor.ChangeAllText.Should().BeEmpty();
    }

    [Fact]
    public void RequestNavigation_raises_NavigationRequested_with_the_row_bookpath_and_position()
    {
        // Arrange
        (SpellcheckEditorViewModel editor, _, HtmlResource html) = New("<p>wrold</p>");
        SpellcheckWordRow row = editor.Words.Single();
        (string BookPath, int Offset)? received = null;
        editor.NavigationRequested += (bookPath, offset) => received = (bookPath, offset);

        // Act
        editor.RequestNavigation(row);

        // Assert
        received.Should().Be((html.BookPath, row.Position));
    }
}
