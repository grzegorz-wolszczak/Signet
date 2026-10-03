using System.Linq;
using AwesomeAssertions;
using Signet.App.Tests.TestSupport;
using Signet.App.ViewModels;
using Signet.Core.BookManipulation;
using Signet.Core.Misc;
using Signet.Core.Resources;
using Signet.Core.Spellcheck;
using Xunit;

namespace Signet.App.Tests;

/// <summary>
/// Tests for <see cref="SpellcheckEditorViewModel"/> (the "Spellcheck Editor" dialog), independent
/// of <see cref="MainWindowViewModel"/>. Uses the real bundled <c>en_US</c> dictionary: "wrold" is an
/// unambiguously misspelled test word, "hello"/"world" are unambiguously correct.
/// </summary>
public sealed class SpellcheckEditorViewModelTests
{
    private static (SpellcheckEditorViewModel Editor, Book Book, HtmlResource Html) New(string text)
    {
        (SettingsStore settings, SpellChecker spellChecker) = SpellCheckTestFactory.New();
        Book book = BookCreator.CreateNewBook("2.0");
        HtmlResource html = book.GetHtmlResources()[0];
        html.SetText(text);

        SpellcheckEditorViewModel editor = new(spellChecker, settings);
        editor.Refresh(book);
        return (editor, book, html);
    }

    [Fact]
    public void Refresh_shows_only_misspelled_words_by_default()
    {
        (SpellcheckEditorViewModel editor, _, _) = New("<p>hello wrold</p>");

        editor.Words.Select(w => w.Word).Should().Equal("wrold");
    }

    [Fact]
    public void ShowAllWords_true_also_shows_correctly_spelled_words()
    {
        (SpellcheckEditorViewModel editor, _, _) = New("<p>hello wrold</p>");

        editor.ShowAllWords = true;

        editor.Words.Select(w => w.Word).Should().Contain("hello").And.Contain("wrold");
    }

    [Fact]
    public void FilterText_filters_words_by_substring()
    {
        (SpellcheckEditorViewModel editor, _, _) = New("<p>wrold wrongg</p>");

        editor.FilterText = "wro";

        editor.Words.Select(w => w.Word).Should().Equal("wrold", "wrongg");

        editor.FilterText = "old";

        editor.Words.Select(w => w.Word).Should().Equal("wrold");
    }

    [Fact]
    public void Ignore_without_a_selection_sets_a_message_and_changes_nothing()
    {
        (SpellcheckEditorViewModel editor, _, _) = New("<p>wrold</p>");

        editor.IgnoreCommand.Execute(null);

        editor.Message.Should().NotBeEmpty();
        editor.Words.Should().ContainSingle();
    }

    [Fact]
    public void Ignore_marks_the_selected_word_correct_and_removes_it_from_the_misspelled_only_view()
    {
        (SpellcheckEditorViewModel editor, _, _) = New("<p>wrold</p>");
        editor.SetSelectedWords(editor.Words);

        editor.IgnoreCommand.Execute(null);

        editor.Words.Should().BeEmpty();
    }

    [Fact]
    public void Add_without_a_selection_sets_a_message_and_changes_nothing()
    {
        (SpellcheckEditorViewModel editor, _, _) = New("<p>wrold</p>");

        editor.AddCommand.Execute(null);

        editor.Message.Should().NotBeEmpty();
        editor.Words.Should().ContainSingle();
    }

    [Fact]
    public void Add_adds_the_selected_word_to_the_default_user_dictionary_and_marks_it_correct()
    {
        (SpellcheckEditorViewModel editor, _, _) = New("<p>wrold</p>");
        editor.SetSelectedWords(editor.Words);

        editor.AddCommand.Execute(null);

        editor.Words.Should().BeEmpty();
    }

    [Fact]
    public void ChangeAll_without_exactly_one_selected_word_sets_a_message_and_does_not_raise_the_event()
    {
        (SpellcheckEditorViewModel editor, _, _) = New("<p>wrold</p>");
        bool raised = false;
        editor.ChangeAllRequested += (_, _, _) => raised = true;

        editor.ChangeAllCommand.Execute(null);

        editor.Message.Should().NotBeEmpty();
        raised.Should().BeFalse();
    }

    [Fact]
    public void ChangeAll_rejects_replacement_text_containing_markup_characters()
    {
        (SpellcheckEditorViewModel editor, _, _) = New("<p>wrold</p>");
        editor.SetSelectedWords(editor.Words);
        editor.ChangeAllText = "wo<rld>";
        bool raised = false;
        editor.ChangeAllRequested += (_, _, _) => raised = true;

        editor.ChangeAllCommand.Execute(null);

        editor.Message.Should().NotBeEmpty();
        raised.Should().BeFalse();
    }

    [Fact]
    public void ChangeAll_raises_ChangeAllRequested_with_the_selected_word_language_and_new_text()
    {
        (SpellcheckEditorViewModel editor, _, _) = New("<p>wrold</p>");
        editor.SetSelectedWords(editor.Words);
        editor.ChangeAllText = "world";
        (string Word, string Lang, string NewWord)? received = null;
        editor.ChangeAllRequested += (word, lang, newWord) => received = (word, lang, newWord);

        editor.ChangeAllCommand.Execute(null);

        received.Should().Be(("wrold", "en", "world"));
    }

    [Fact]
    public void SetSelectedWords_with_a_single_row_seeds_suggestions_and_change_all_text()
    {
        (SpellcheckEditorViewModel editor, _, _) = New("<p>wrold</p>");

        editor.SetSelectedWords(editor.Words);

        editor.Suggestions.Should().Contain("world");
        editor.ChangeAllText.Should().Be(editor.Suggestions[0]);
    }

    [Fact]
    public void SetSelectedWords_with_multiple_rows_clears_change_all_text()
    {
        (SpellcheckEditorViewModel editor, _, _) = New("<p>wrold wrongg</p>");

        editor.SetSelectedWords(editor.Words);

        editor.ChangeAllText.Should().BeEmpty();
    }

    [Fact]
    public void RequestNavigation_raises_NavigationRequested_with_the_row_bookpath_and_position()
    {
        (SpellcheckEditorViewModel editor, _, HtmlResource html) = New("<p>wrold</p>");
        SpellcheckWordRow row = editor.Words.Single();
        (string BookPath, int Offset)? received = null;
        editor.NavigationRequested += (bookPath, offset) => received = (bookPath, offset);

        editor.RequestNavigation(row);

        received.Should().Be((html.BookPath, row.Position));
    }
}
