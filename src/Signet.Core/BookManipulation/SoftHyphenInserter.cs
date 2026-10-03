using System;
using System.Collections.Generic;
using System.Text;
using System.Text.RegularExpressions;

namespace Signet.Core.BookManipulation;

/// <summary>
/// "Add soft hyphens". Inserts the <c>&amp;shy;</c> entity (U+00AD, a soft hyphen) into sufficiently long words of the
/// XHTML content, so that readers without support for automatic CSS hyphenation can justify
/// text better. Works on the raw source text (like <see cref="TagLister"/>), NOT on a
/// parsed/re-serialized DOM — so the change is minimal (only the inserted
/// entities), without reformatting the rest of the document (unlike
/// <see cref="Book.PrettyPrintAllHtml"/>, which deliberately reformats everything).
/// </summary>
/// <remarks>
/// <b>A deliberate simplification:</b> words are not split using per-language dictionary hyphenation
/// patterns (the TeX/Franklin format) — no maintained, ready-made .NET package implementing that format
/// is available (and <c>WeCantSpell.Hunspell</c>, already used for spellchecking, uses an entirely different data
/// format, not meant for hyphenation). Instead of building a separate per-language pattern subsystem
/// from scratch, a simple, language-independent phonetic heuristic is used: a break is inserted after a vowel
/// that directly precedes a single consonant (the "V C" pattern → a break between V and C), keeping a
/// minimum fragment length on both sides of every break. This is NOT linguistically
/// correct syllabification, only a "better than nothing" approximation — a documented limitation.
/// </remarks>
public static class SoftHyphenInserter
{
    /// <summary>The entity inserted at possible line-break points.</summary>
    public const string SoftHyphenEntity = "&shy;";

    /// <summary>The literal soft hyphen character (U+00AD), in case it occurs outside an entity.</summary>
    public const char SoftHyphenChar = '­';

    private static readonly Regex SoftHyphenEntityRegex = new(
        "&shy;|&#0*173;|&#[xX]0*[aA][dD];",
        RegexOptions.Compiled | RegexOptions.IgnoreCase);

    private const int DefaultMinWordLength = 8;
    private const int DefaultMinSegmentLength = 3;

    private static readonly HashSet<string> SkippedTags = new(StringComparer.OrdinalIgnoreCase)
    {
        "pre", "code", "script", "style", "textarea", "title",
    };

    private static readonly HashSet<char> Vowels = new("aeiouyąęóAEIOUYĄĘÓ");

    /// <summary>
    /// Returns a copy of <paramref name="htmlSource"/> with soft hyphens inserted in the content
    /// (outside tags, attributes, comments and elements from <see cref="SkippedTags"/>).
    /// Idempotent — a word that already contains <see cref="SoftHyphenEntity"/> is not split
    /// again.
    /// </summary>
    public static string InsertSoftHyphens(
        string htmlSource,
        int minWordLength = DefaultMinWordLength,
        int minSegmentLength = DefaultMinSegmentLength)
    {
        ArgumentNullException.ThrowIfNull(htmlSource);

        TagLister lister = new(htmlSource);
        List<(int Pos, string Text)> insertions = new();
        Stack<string> openTags = new();

        for (int i = 0; i < lister.Count; i++)
        {
            TagLister.TagInfo tag = lister.At(i);
            UpdateStack(openTags, tag);

            if (i + 1 >= lister.Count)
            {
                break;
            }

            TagLister.TagInfo next = lister.At(i + 1);
            int gapStart = tag.Pos + tag.Len;
            int gapEnd = next.Pos;
            if (gapEnd <= gapStart || !lister.IsPositionInBody(gapStart))
            {
                continue;
            }

            if (IsInsideSkippedTag(openTags))
            {
                continue;
            }

            string gap = htmlSource.Substring(gapStart, gapEnd - gapStart);
            CollectInsertionsForGap(gap, gapStart, minWordLength, minSegmentLength, insertions);
        }

        if (insertions.Count == 0)
        {
            return htmlSource;
        }

        insertions.Sort((a, b) => b.Pos.CompareTo(a.Pos));
        StringBuilder result = new(htmlSource);
        foreach ((int pos, string text) in insertions)
        {
            result.Insert(pos, text);
        }

        return result.ToString();
    }

    private static void UpdateStack(Stack<string> openTags, TagLister.TagInfo tag)
    {
        switch (tag.Kind)
        {
            case TagKind.Begin:
                openTags.Push(tag.TagName);
                break;
            case TagKind.End:
                if (openTags.Count > 0
                    && string.Equals(openTags.Peek(), tag.TagName, StringComparison.OrdinalIgnoreCase))
                {
                    openTags.Pop();
                }

                break;
        }
    }

    private static bool IsInsideSkippedTag(Stack<string> openTags)
    {
        foreach (string tagName in openTags)
        {
            if (SkippedTags.Contains(tagName))
            {
                return true;
            }
        }

        return false;
    }

    private static void CollectInsertionsForGap(
        string gap,
        int gapStart,
        int minWordLength,
        int minSegmentLength,
        List<(int Pos, string Text)> insertions)
    {
        // The "&"/";" characters inside an already inserted &shy; entity do NOT end a "word" run — so
        // a previously split word is seen as a single run on a re-run
        // (idempotence, see the SoftHyphenEntity check in CollectInsertionsForWord) instead of
        // falling apart into short fragments between entities that could be split further.
        int wordStart = -1;
        int i = 0;
        while (i <= gap.Length)
        {
            if (i < gap.Length && IsSoftHyphenEntityAt(gap, i))
            {
                if (wordStart < 0)
                {
                    wordStart = i;
                }

                i += SoftHyphenEntity.Length;
                continue;
            }

            bool isLetter = i < gap.Length && char.IsLetter(gap[i]);
            if (isLetter)
            {
                if (wordStart < 0)
                {
                    wordStart = i;
                }

                i++;
                continue;
            }

            if (wordStart >= 0)
            {
                CollectInsertionsForWord(gap, wordStart, i - wordStart, gapStart, minWordLength, minSegmentLength, insertions);
                wordStart = -1;
            }

            i++;
        }
    }

    private static bool IsSoftHyphenEntityAt(string gap, int index) =>
        index + SoftHyphenEntity.Length <= gap.Length
        && string.Compare(gap, index, SoftHyphenEntity, 0, SoftHyphenEntity.Length, StringComparison.OrdinalIgnoreCase) == 0;

    private static void CollectInsertionsForWord(
        string gap,
        int wordStart,
        int wordLength,
        int gapStart,
        int minWordLength,
        int minSegmentLength,
        List<(int Pos, string Text)> insertions)
    {
        if (wordLength < minWordLength)
        {
            return;
        }

        string word = gap.Substring(wordStart, wordLength);
        if (word.Contains(SoftHyphenEntity, StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        int lastBreak = 0;
        for (int i = 0; i < word.Length - 1; i++)
        {
            bool current = Vowels.Contains(word[i]);
            bool next = Vowels.Contains(word[i + 1]);
            if (!current || next)
            {
                continue;
            }

            int breakOffset = i + 1;
            if (breakOffset < minSegmentLength
                || word.Length - breakOffset < minSegmentLength
                || breakOffset - lastBreak < minSegmentLength)
            {
                continue;
            }

            insertions.Add((gapStart + wordStart + breakOffset, SoftHyphenEntity));
            lastBreak = breakOffset;
        }
    }

    /// <summary>
    /// "Remove soft hyphens" — the inverse of <see cref="InsertSoftHyphens"/>.
    /// Removes all soft hyphens from <paramref name="htmlSource"/>: the <see cref="SoftHyphenEntity"/> entity
    /// (together with its numeric variants <c>&amp;#173;</c>/<c>&amp;#xAD;</c>) and the literal
    /// <see cref="SoftHyphenChar"/> character (U+00AD), regardless of whether they were inserted by
    /// <see cref="InsertSoftHyphens"/> or come from another source (e.g. an import). A purely
    /// textual operation — no markup awareness is needed (the removed characters are never part of a valid
    /// tag/attribute name).
    /// </summary>
    public static string RemoveSoftHyphens(string htmlSource)
    {
        ArgumentNullException.ThrowIfNull(htmlSource);

        string withoutEntities = SoftHyphenEntityRegex.Replace(htmlSource, string.Empty);
        return withoutEntities.IndexOf(SoftHyphenChar) < 0
            ? withoutEntities
            : withoutEntities.Replace(SoftHyphenChar.ToString(), string.Empty);
    }
}
