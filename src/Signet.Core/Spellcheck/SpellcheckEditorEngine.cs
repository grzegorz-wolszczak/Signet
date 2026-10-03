using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Signet.Core.BookManipulation;
using Signet.Core.Misc;
using Signet.Core.Resources;

namespace Signet.Core.Spellcheck;

/// <summary>
/// A single unique word from the "Spellcheck Editor" dialog table — the number of occurrences in the whole book,
/// the spelling status and the position of the first occurrence (for "go to" navigation).
/// </summary>
/// <param name="Text">The word text.</param>
/// <param name="Lang">The language code the word was found with (see <see cref="HtmlWord.Lang"/>).</param>
/// <param name="Count">The number of occurrences of this (Text, Lang) pair across all (X)HTML files of the book.</param>
/// <param name="Misspelled">Whether the word is misspelled.</param>
/// <param name="BookPath">The bookpath of the file with the first occurrence (for navigation).</param>
/// <param name="Position">The 0-based offset of the first occurrence in <paramref name="BookPath"/>.</param>
public readonly record struct SpellcheckWord(string Text, string Lang, int Count, bool Misspelled, string BookPath, int Position);

/// <summary>
/// The data engine for the "Spellcheck Editor" dialog: the unique words of the whole book with the number of
/// occurrences and the spelling status, and a bulk replacement of one word across all (X)HTML files.
/// </summary>
public static class SpellcheckEditorEngine
{
    /// <summary>
    /// The unique words (a text+language pair) from all (X)HTML files of the book, with the number of occurrences,
    /// the spelling status and the position of the first occurrence.
    /// </summary>
    public static IReadOnlyList<SpellcheckWord> GetUniqueWords(Book book, SpellChecker spellChecker, SettingsStore settings)
    {
        ArgumentNullException.ThrowIfNull(book);
        ArgumentNullException.ThrowIfNull(spellChecker);
        ArgumentNullException.ThrowIfNull(settings);

        string defaultLang = book.GetOpf().GetPrimaryBookLanguage().Replace('_', '-');

        Dictionary<(string Text, string Lang), Accumulator> aggregate = new();
        foreach (HtmlResource html in book.GetHtmlResources())
        {
            foreach (HtmlWord word in HtmlSpellCheck.GetAllWords(spellChecker, settings, html.GetText(), defaultLang))
            {
                (string Text, string Lang) key = (word.Text, word.Lang);
                if (aggregate.TryGetValue(key, out Accumulator? existing))
                {
                    existing.Count++;
                }
                else
                {
                    aggregate[key] = new Accumulator(html.BookPath, word.Offset);
                }
            }
        }

        List<SpellcheckWord> result = new(aggregate.Count);
        foreach (KeyValuePair<(string Text, string Lang), Accumulator> entry in aggregate)
        {
            bool misspelled = !spellChecker.Check(entry.Key.Text, new[] { entry.Key.Lang });
            result.Add(new SpellcheckWord(
                entry.Key.Text, entry.Key.Lang, entry.Value.Count, misspelled, entry.Value.BookPath, entry.Value.Position));
        }

        return result;
    }

    /// <summary>
    /// Replaces all occurrences of a word (matched exactly by text and language) with
    /// <paramref name="newText"/> in all (X)HTML files of the book. Returns the number of replaced
    /// occurrences. Within a file the replacement goes from the end to the start so that
    /// the earlier offsets stay valid.
    /// </summary>
    public static int ReplaceWordInAllFiles(
        Book book, SpellChecker spellChecker, SettingsStore settings, string wordText, string wordLang, string newText)
    {
        ArgumentNullException.ThrowIfNull(book);
        ArgumentNullException.ThrowIfNull(spellChecker);
        ArgumentNullException.ThrowIfNull(settings);
        ArgumentNullException.ThrowIfNull(wordText);
        ArgumentNullException.ThrowIfNull(wordLang);
        ArgumentNullException.ThrowIfNull(newText);

        string defaultLang = book.GetOpf().GetPrimaryBookLanguage().Replace('_', '-');
        int totalReplacements = 0;

        foreach (HtmlResource html in book.GetHtmlResources())
        {
            string text = html.GetText();
            List<HtmlWord> matches = HtmlSpellCheck.GetAllWords(spellChecker, settings, text, defaultLang)
                .Where(w => w.Text == wordText && w.Lang == wordLang)
                .ToList();

            if (matches.Count == 0)
            {
                continue;
            }

            StringBuilder builder = new(text);
            for (int i = matches.Count - 1; i >= 0; i--)
            {
                HtmlWord match = matches[i];
                builder.Remove(match.Offset, match.Length);
                builder.Insert(match.Offset, newText);
            }

            html.SetText(builder.ToString());
            totalReplacements += matches.Count;
        }

        return totalReplacements;
    }

    private sealed class Accumulator
    {
        public Accumulator(string bookPath, int position)
        {
            Count = 1;
            BookPath = bookPath;
            Position = position;
        }

        public int Count { get; set; }

        public string BookPath { get; }

        public int Position { get; }
    }
}
