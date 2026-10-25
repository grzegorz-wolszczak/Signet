using System;
using System.Collections.Generic;
using System.Linq;

namespace Signet.Core.BookManipulation;

/// <summary>
/// "Find Usages" of a language in the whole book: every <c>lang</c> and <c>xml:lang</c> attribute that states it,
/// in the markup files — the same shape of result as <see cref="ClassUsageFinder"/>, so the search shares the
/// "Find Usages" panel. What the panel then shows is every place where the language is set in so many words, as
/// against the places that merely inherit it.
/// </summary>
/// <remarks>
/// Language tags do not care about case (BCP 47), so <c>pl-PL</c> and <c>pl-pl</c> are one language here. A
/// stylesheet has no language to state, so only the markup files are searched; the files are searched in parallel.
/// </remarks>
public static class LanguageUsageFinder
{
    private static readonly string[] LanguageAttributes = ["lang", "xml:lang"];

    /// <summary>
    /// The places that state <paramref name="language"/>, in the order of <paramref name="sources"/> and, within a
    /// file, in text order. An empty <paramref name="language"/> means every language: each place is listed with
    /// whatever it states.
    /// </summary>
    public static IReadOnlyList<ClassUsage> Find(string language, IEnumerable<ClassUsageSource> sources)
    {
        ArgumentNullException.ThrowIfNull(language);
        ArgumentNullException.ThrowIfNull(sources);

        return sources
            .AsParallel()
            .AsOrdered()
            .SelectMany(source => FindInSource(language, source))
            .ToList();
    }

    /// <summary>
    /// The language under the caret in the value of a <c>lang</c> or <c>xml:lang</c> attribute of an opening or
    /// empty tag (inside the value or right at its left/right edge). <c>null</c> when the caret is not on one.
    /// </summary>
    public static string? FindLanguageAtCaret(string text, int caretOffset)
    {
        ArgumentNullException.ThrowIfNull(text);
        if (text.Length == 0)
        {
            return null;
        }

        int pos = Math.Clamp(caretOffset, 0, text.Length);
        TagLister lister = TagLister.For(text);
        TagLister.TagInfo tag = lister.At(lister.FindFirstTagOnOrAfter(pos));
        if (pos < tag.Pos || pos >= tag.Pos + tag.Len || tag.Kind is not (TagKind.Begin or TagKind.SelfClosing))
        {
            return null;
        }

        string openTag = text.Substring(tag.Pos, tag.Len);
        foreach (string name in LanguageAttributes)
        {
            TagLister.AttInfo attribute = TagLister.ParseAttribute(openTag, name);
            if (attribute.Pos == -1 || attribute.VLen <= 0)
            {
                continue;
            }

            int relativeCaret = pos - (tag.Pos + attribute.VPos);
            if (relativeCaret >= 0 && relativeCaret <= attribute.VLen)
            {
                return attribute.AValue;
            }
        }

        return null;
    }

    private static List<ClassUsage> FindInSource(string language, ClassUsageSource source)
    {
        List<ClassUsage> usages = new();
        if (source.IsStyleSheet)
        {
            return usages;
        }

        // Offset of the value -> what it states; sorted, because the two attributes of one tag are found apart.
        SortedDictionary<int, (int Length, string Value)> found = new();
        foreach ((int pos, int len) in TagLister.EnumerateOpeningTags(source.Text))
        {
            string openTag = source.Text.Substring(pos, len);
            foreach (string name in LanguageAttributes)
            {
                TagLister.AttInfo attribute = TagLister.ParseAttribute(openTag, name);
                if (attribute.Pos == -1 || attribute.VLen <= 0)
                {
                    continue;
                }

                if (language.Length == 0 || string.Equals(attribute.AValue, language, StringComparison.OrdinalIgnoreCase))
                {
                    found[pos + attribute.VPos] = (attribute.VLen, attribute.AValue);
                }
            }
        }

        if (found.Count == 0)
        {
            return usages;
        }

        ClassUsageFinder.LineIndex lines = new(source.Text);
        foreach ((int offset, (int length, string _)) in found)
        {
            (int line, int column) = lines.LocationOf(offset);
            (string context, int matchStart, int matchLength) = ClassUsageFinder.Context(source.Text, offset, column, length);
            usages.Add(new ClassUsage(
                source.BookPath, offset, line, column, ClassUsageKind.LanguageAttribute, context, length, matchStart, matchLength));
        }

        return usages;
    }
}
