using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Signet.Core.BookManipulation;
using Signet.Core.Misc;
using Signet.Core.Resources;

namespace Signet.Core.Spellcheck;

/// <summary>One occurrence of a word in the book — where it is, for "go to" navigation and for replacing it alone.</summary>
/// <param name="BookPath">The bookpath of the file it is in.</param>
/// <param name="Position">Its 0-based offset in that file.</param>
/// <param name="Line">The 1-based line of that offset — what the list of occurrences shows beside the file.</param>
public readonly record struct SpellcheckOccurrence(string BookPath, int Position, int Line);

/// <summary>
/// A single unique word from the "Spellcheck Editor" dialog table — every occurrence of it in the book and the
/// spelling status.
/// </summary>
/// <param name="Text">The word text.</param>
/// <param name="Lang">The language code the word was found with (see <see cref="HtmlWord.Lang"/>).</param>
/// <param name="Misspelled">Whether the word is misspelled.</param>
/// <param name="Occurrences">
/// Every occurrence of this (Text, Lang) pair across all (X)HTML files of the book, in reading order.
/// </param>
public readonly record struct SpellcheckWord(
    string Text, string Lang, bool Misspelled, IReadOnlyList<SpellcheckOccurrence> Occurrences)
{
    /// <summary>The number of occurrences in the whole book.</summary>
    public int Count => Occurrences.Count;

    /// <summary>The bookpath of the file with the first occurrence (empty when there is none).</summary>
    public string BookPath => Occurrences.Count > 0 ? Occurrences[0].BookPath : string.Empty;

    /// <summary>The offset of the first occurrence.</summary>
    public int Position => Occurrences.Count > 0 ? Occurrences[0].Position : 0;
}

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

        Dictionary<(string Text, string Lang), List<SpellcheckOccurrence>> aggregate = new();
        string[] langBuffer = new string[1];
        foreach (HtmlResource html in book.GetHtmlResources())
        {
            string text = html.GetText();

            // The words come in reading order, so the line of each is counted on from the previous one instead of
            // walking the file again for every word.
            int line = 1;
            int counted = 0;
            foreach (HtmlWord word in HtmlSpellCheck.GetAllWords(spellChecker, settings, text, defaultLang))
            {
                for (; counted < word.Offset && counted < text.Length; counted++)
                {
                    if (text[counted] == '\n')
                    {
                        line++;
                    }
                }

                (string Text, string Lang) key = (word.Text, word.Lang);
                if (!aggregate.TryGetValue(key, out List<SpellcheckOccurrence>? occurrences))
                {
                    occurrences = new List<SpellcheckOccurrence>();
                    aggregate[key] = occurrences;
                }

                occurrences.Add(new SpellcheckOccurrence(html.BookPath, word.Offset, line));
            }
        }

        List<SpellcheckWord> result = new(aggregate.Count);
        foreach (KeyValuePair<(string Text, string Lang), List<SpellcheckOccurrence>> entry in aggregate)
        {
            langBuffer[0] = entry.Key.Lang;
            bool misspelled = !spellChecker.Check(entry.Key.Text, langBuffer);
            result.Add(new SpellcheckWord(entry.Key.Text, entry.Key.Lang, misspelled, entry.Value));
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

    /// <summary>
    /// Replaces one occurrence of a word: the one at <paramref name="position"/> of <paramref name="bookPath"/>, and
    /// only when exactly <paramref name="wordText"/> stands there (the file may have changed since the table was
    /// built). Returns whether it was replaced.
    /// </summary>
    public static bool ReplaceOccurrence(Book book, string bookPath, int position, string wordText, string newText)
    {
        ArgumentNullException.ThrowIfNull(book);
        ArgumentNullException.ThrowIfNull(bookPath);
        ArgumentNullException.ThrowIfNull(wordText);
        ArgumentNullException.ThrowIfNull(newText);

        if (book.GetFolderKeeper().GetResourceByBookPathNoThrow(bookPath) is not HtmlResource html)
        {
            return false;
        }

        string text = html.GetText();
        if (position < 0 || position + wordText.Length > text.Length
            || !string.Equals(text.Substring(position, wordText.Length), wordText, StringComparison.Ordinal))
        {
            return false;
        }

        html.SetText(text[..position] + newText + text[(position + wordText.Length)..]);
        return true;
    }
}
