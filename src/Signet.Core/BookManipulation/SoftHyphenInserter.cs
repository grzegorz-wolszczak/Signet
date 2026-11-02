using System;
using System.Collections.Generic;
using System.Text;
using Signet.Core.Hyphenation;

namespace Signet.Core.BookManipulation;

/// <summary>How a soft hyphen is written in the source.</summary>
public enum SoftHyphenForm
{
    /// <summary>
    /// The character itself (U+00AD): the shortest; invisible in the source, which Code Editor can show as ¬
    /// (Preferences › Appearance › Code Editor).
    /// </summary>
    Character,

    /// <summary>The numeric character reference <c>&amp;#173;</c>: visible in the source as text.</summary>
    Reference,
}

/// <summary>
/// "Add / Remove Soft Hyphens" in one XHTML file: soft hyphens (<c>&amp;#173;</c>, U+00AD) are added where the
/// hyphenation patterns of the language of the text allow a break, and removed from the text of chosen languages and
/// from chosen places outside the text. Works on the raw source (<see cref="SoftHyphenScanner"/>), so nothing but the
/// soft hyphens changes — the rest of the file is not reformatted.
/// </summary>
/// <remarks>
/// A word is only ever broken by the patterns of its language (<see cref="HyphenationPatterns"/>, read from the
/// <c>hyph_*.dic</c> files of the installed dictionaries); a language without patterns is not hyphenated at all. There
/// is deliberately no language-independent guess: a wrong break in a printed word is worse than none.
/// </remarks>
public static class SoftHyphenInserter
{
    /// <summary>
    /// The character reference inserted at possible line-break points. Numeric, not the named <c>&amp;shy;</c>:
    /// XHTML is parsed as XML without its DTD, where only the five predefined entities exist — a named
    /// <c>&amp;shy;</c> makes the file not well-formed (and is invalid in EPUB 3 regardless of the DOCTYPE).
    /// </summary>
    public const string SoftHyphenEntity = "&#173;";

    /// <summary>The literal soft hyphen character (U+00AD), in case it occurs outside an entity.</summary>
    public const char SoftHyphenChar = '­';

    /// <summary>The shortest word that is hyphenated, unless the user chooses otherwise.</summary>
    public const int DefaultMinWordLength = 8;

    /// <summary>How a soft hyphen of <paramref name="form"/> is written.</summary>
    public static string TextOf(SoftHyphenForm form) => form == SoftHyphenForm.Reference ? SoftHyphenEntity : SoftHyphenChar.ToString();

    /// <summary>
    /// The form a soft hyphen written as <paramref name="text"/> is in, or <c>null</c> for one written otherwise
    /// (<c>&amp;shy;</c>, <c>&amp;#xAD;</c>, <c>&amp;#0173;</c>…).
    /// </summary>
    public static SoftHyphenForm? FormOf(string text)
    {
        ArgumentNullException.ThrowIfNull(text);
        return text == SoftHyphenEntity ? SoftHyphenForm.Reference
            : text.Length == 1 && text[0] == SoftHyphenChar ? SoftHyphenForm.Character
            : null;
    }

    /// <summary>
    /// Adds soft hyphens to the words of the text whose language has patterns. A word that already has a soft hyphen
    /// (in any form) is left as it is, so running it again adds nothing; so are words shorter than
    /// <paramref name="minWordLength"/> letters, and the text of elements that are not prose (<c>pre</c>, <c>code</c>…),
    /// of the head, of attributes and of comments.
    /// </summary>
    /// <param name="html">The source of the file.</param>
    /// <param name="defaultLanguage">The language of text no element states one for (the OPF's); empty = unknown.</param>
    /// <param name="patternsFor">
    /// The patterns of a language, by its key (<see cref="SoftHyphenLanguage.KeyOf"/>); <c>null</c> = the language is not
    /// hyphenated (no patterns, or not chosen).
    /// </param>
    /// <param name="minWordLength">The fewest letters of a word that is hyphenated.</param>
    /// <param name="form">How the soft hyphens are written.</param>
    public static string InsertSoftHyphens(
        string html,
        string defaultLanguage,
        Func<string, HyphenationPatterns?> patternsFor,
        int minWordLength = DefaultMinWordLength,
        SoftHyphenForm form = SoftHyphenForm.Character)
    {
        ArgumentNullException.ThrowIfNull(html);
        ArgumentNullException.ThrowIfNull(defaultLanguage);
        ArgumentNullException.ThrowIfNull(patternsFor);

        List<int> insertions = new();
        Dictionary<string, HyphenationPatterns?> patterns = new(StringComparer.Ordinal);
        foreach (SoftHyphenSegment segment in SoftHyphenScanner.Scan(html, defaultLanguage))
        {
            if (!segment.IsText)
            {
                continue;
            }

            string key = SoftHyphenLanguage.KeyOf(segment.Language);
            if (!patterns.TryGetValue(key, out HyphenationPatterns? language))
            {
                language = patternsFor(key);
                patterns[key] = language;
            }

            if (language is null)
            {
                continue;
            }

            foreach (SoftHyphenWord word in SoftHyphenScanner.Words(html, segment))
            {
                if (word.HasSoftHyphen || word.Letters < minWordLength || word.Letters != word.End - word.Start)
                {
                    continue;
                }

                foreach (int offset in language.BreakPoints(html.Substring(word.Start, word.End - word.Start)))
                {
                    insertions.Add(word.Start + offset);
                }
            }
        }

        if (insertions.Count == 0)
        {
            return html;
        }

        string softHyphen = TextOf(form);
        StringBuilder result = new(html.Length + (insertions.Count * softHyphen.Length));
        int copied = 0;
        foreach (int at in insertions)
        {
            result.Append(html, copied, at - copied).Append(softHyphen);
            copied = at;
        }

        return result.Append(html, copied, html.Length - copied).ToString();
    }

    /// <summary>
    /// Removes soft hyphens (any form: <c>&amp;shy;</c>, <c>&amp;#173;</c>, <c>&amp;#xAD;</c>, the character) from the
    /// text of the chosen languages and from the chosen places outside the text; the others stay.
    /// </summary>
    /// <param name="html">The source of the file.</param>
    /// <param name="defaultLanguage">The language of text no element states one for (the OPF's); empty = unknown.</param>
    /// <param name="removeInLanguage">Whether to remove them from the text of a language, by its key (empty = unknown).</param>
    /// <param name="outsideOffsets">The offsets of the soft hyphens outside the text to remove (<see cref="SoftHyphenPlace.Offset"/>).</param>
    public static string RemoveSoftHyphens(
        string html, string defaultLanguage, Func<string, bool> removeInLanguage, IReadOnlySet<int> outsideOffsets) =>
        Replace(html, defaultLanguage, removeInLanguage, outsideOffsets, string.Empty);

    /// <summary>
    /// Writes the soft hyphens of the text of the chosen languages and of the chosen places outside the text in
    /// <paramref name="form"/> (<c>&amp;shy;</c>, <c>&amp;#xAD;</c>, <c>&amp;#173;</c> or the character, whichever they
    /// were); the others stay as they are.
    /// </summary>
    /// <param name="html">The source of the file.</param>
    /// <param name="defaultLanguage">The language of text no element states one for (the OPF's); empty = unknown.</param>
    /// <param name="convertInLanguage">Whether to convert them in the text of a language, by its key (empty = unknown).</param>
    /// <param name="outsideOffsets">The offsets of the soft hyphens outside the text to convert (<see cref="SoftHyphenPlace.Offset"/>).</param>
    /// <param name="form">The form to write them in.</param>
    public static string ConvertSoftHyphens(
        string html, string defaultLanguage, Func<string, bool> convertInLanguage, IReadOnlySet<int> outsideOffsets, SoftHyphenForm form) =>
        Replace(html, defaultLanguage, convertInLanguage, outsideOffsets, TextOf(form));

    // Writes the soft hyphens of the chosen languages and places as replacement (empty = removes them); one written as
    // replacement already stays.
    private static string Replace(
        string html, string defaultLanguage, Func<string, bool> inLanguage, IReadOnlySet<int> outsideOffsets, string replacement)
    {
        ArgumentNullException.ThrowIfNull(html);
        ArgumentNullException.ThrowIfNull(defaultLanguage);
        ArgumentNullException.ThrowIfNull(inLanguage);
        ArgumentNullException.ThrowIfNull(outsideOffsets);

        List<(int Offset, int Length)> found = new();
        foreach (SoftHyphenSegment segment in SoftHyphenScanner.Scan(html, defaultLanguage))
        {
            bool inText = segment.IsText && inLanguage(SoftHyphenLanguage.KeyOf(segment.Language));
            if (segment.IsText && !inText)
            {
                continue;
            }

            foreach ((int offset, int length) in SoftHyphenScanner.SoftHyphens(html, segment.Start, segment.End))
            {
                if ((inText || outsideOffsets.Contains(offset))
                    && string.CompareOrdinal(html, offset, replacement, 0, Math.Max(length, replacement.Length)) != 0)
                {
                    found.Add((offset, length));
                }
            }
        }

        if (found.Count == 0)
        {
            return html;
        }

        found.Sort((a, b) => a.Offset.CompareTo(b.Offset));
        StringBuilder result = new(html.Length);
        int copied = 0;
        foreach ((int offset, int length) in found)
        {
            if (offset < copied)
            {
                continue;
            }

            result.Append(html, copied, offset - copied).Append(replacement);
            copied = offset + length;
        }

        return result.Append(html, copied, html.Length - copied).ToString();
    }
}
