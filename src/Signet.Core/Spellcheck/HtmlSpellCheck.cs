using System;
using System.Collections.Generic;
using Signet.Core.Misc;

namespace Signet.Core.Spellcheck;

/// <summary>A single word found in XHTML content, with its position and inherited language.</summary>
/// <param name="Text">The word text.</param>
/// <param name="Offset">The position of the first character in the source document.</param>
/// <param name="Length">The word length (characters).</param>
/// <param name="Lang">
/// The language code inherited from the nearest ancestor with <c>lang</c>/<c>xml:lang</c> (or the default
/// passed to the call) — used to choose the dictionary through <see cref="SpellChecker.Check"/>.
/// </param>
public readonly record struct HtmlWord(string Text, int Offset, int Length, string Lang);

/// <summary>
/// Extracts words from XHTML content for spell checking, taking the per-fragment
/// language into account (<c>xml:lang</c>/<c>lang</c>). It skips
/// tags/attributes and the contents of <c>&lt;style&gt;</c>/<c>&lt;script&gt;</c>. Multilingual
/// fragments are handled correctly, with each word carrying the language of its fragment.
/// </summary>
public static class HtmlSpellCheck
{
    private const int MaxWordLength = 90;
    private const string EntityWordChars = ";#01234567890abcdefABCDEFxX";

    /// <summary>All words in the document.</summary>
    /// <param name="spellChecker">The spell checker — supplies extra word characters (<c>WORDCHARS</c>).</param>
    /// <param name="settings">The settings — <see cref="SettingsStore.SpellCheckNumbers"/> and the default metadata language.</param>
    /// <param name="html">The XHTML content.</param>
    /// <param name="defaultLang">
    /// The language used until a tag sets <c>lang</c>/<c>xml:lang</c>. Empty —
    /// <see cref="SettingsStore.DefaultMetadataLang"/> (underscores replaced with hyphens).
    /// </param>
    public static IReadOnlyList<HtmlWord> GetAllWords(SpellChecker spellChecker, SettingsStore settings, string html, string defaultLang = "")
    {
        return Scan(spellChecker, settings, html, defaultLang, misspelledOnly: false);
    }

    /// <summary>The misspelled words in the document.</summary>
    public static IReadOnlyList<HtmlWord> GetMisspelledWords(SpellChecker spellChecker, SettingsStore settings, string html, string defaultLang = "")
    {
        return Scan(spellChecker, settings, html, defaultLang, misspelledOnly: true);
    }

    private static List<HtmlWord> Scan(SpellChecker spellChecker, SettingsStore settings, string html, string defaultLang, bool misspelledOnly)
    {
        ArgumentNullException.ThrowIfNull(spellChecker);
        ArgumentNullException.ThrowIfNull(settings);
        ArgumentNullException.ThrowIfNull(html);
        ArgumentNullException.ThrowIfNull(defaultLang);

        string lang = defaultLang.Length > 0 ? defaultLang : settings.DefaultMetadataLang.Replace('_', '-');
        string wordChars = spellChecker.GetWordChars() + '­';
        bool useNums = settings.SpellCheckNumbers;

        List<HtmlWord> words = new();
        MarkupTokenizer tokenizer = new(html, lang);
        string[] langBuffer = new string[1];
        MarkupToken? token;
        while ((token = tokenizer.ParseNext()) is not null)
        {
            if (token.Text.Length == 0)
            {
                continue;
            }

            if (token.TagPath.EndsWith(".style", StringComparison.Ordinal) || token.TagPath.EndsWith("script", StringComparison.Ordinal))
            {
                continue;
            }

            ParseTextIntoWords(words, wordChars, useNums, token.Lang, token.Text, token.Pos, spellChecker, misspelledOnly, langBuffer);
        }

        return words;
    }

    private static void ParseTextIntoWords(
        List<HtmlWord> words,
        string wordChars,
        bool useNums,
        string lang,
        string text,
        int pos,
        SpellChecker spellChecker,
        bool misspelledOnly,
        string[] langBuffer)
    {
        bool inEntity = false;
        bool inInvalidWord = false;
        int wordStart = 0;
        string padded = " " + text + " ";

        for (int i = 0; i < padded.Length; i++)
        {
            char c = padded[i];
            char prevC = i > 0 ? padded[i - 1] : ' ';
            char nextC = i < padded.Length - 1 ? padded[i + 1] : ' ';

            if (IsBoundary(prevC, c, nextC, wordChars, useNums))
            {
                if (inEntity && !EntityWordChars.Contains(c))
                {
                    inEntity = false;
                }

                if (!inInvalidWord && !inEntity && i - wordStart > 0)
                {
                    string word = padded.Substring(wordStart, i - wordStart);
                    if (word.Length > 0 && (!misspelledOnly || !IsCorrect(spellChecker, word, lang, langBuffer)))
                    {
                        words.Add(new HtmlWord(word, pos + wordStart - 1, i - wordStart, lang));
                    }
                }

                wordStart = i + 1;
                inInvalidWord = false;
            }
            else if (!inInvalidWord && i - wordStart > MaxWordLength)
            {
                inInvalidWord = true;
            }

            if (c == '&')
            {
                inEntity = true;
            }

            if (c == ';')
            {
                inEntity = false;
            }
        }
    }

    private static bool IsCorrect(SpellChecker spellChecker, string word, string lang, string[] langBuffer)
    {
        langBuffer[0] = lang;
        return spellChecker.Check(word, langBuffer);
    }

    private static bool IsValidChar(char c, bool useNums) => useNums ? char.IsLetterOrDigit(c) : char.IsLetter(c);

    private static bool IsBoundary(char prevC, char c, char nextC, string wordChars, bool useNums)
    {
        if (IsValidChar(c, useNums))
        {
            return false;
        }

        if (c == '.' && wordChars.Contains('.'))
        {
            return false;
        }

        // '_' joins word characters like a hyphen does ("snake_case" is one word, as in calibre).
        bool isPotentialBoundary = c is '-' or '‒' or '\'' or '’' or '_' || (wordChars.Length > 0 && wordChars.Contains(c));

        if (isPotentialBoundary && (!IsValidChar(prevC, useNums) || !IsValidChar(nextC, useNums)))
        {
            return true;
        }

        return !(isPotentialBoundary && (IsValidChar(prevC, useNums) || IsValidChar(nextC, useNums)));
    }
}
