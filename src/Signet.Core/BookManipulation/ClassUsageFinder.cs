using System;
using System.Collections.Generic;
using System.Linq;
using Signet.Core.Parsers;

namespace Signet.Core.BookManipulation;

/// <summary>Where a class is used.</summary>
public enum ClassUsageKind
{
    /// <summary>A token of a <c>class="…"</c> attribute.</summary>
    ClassAttribute,

    /// <summary>A <c>.name</c> in a CSS selector (a stylesheet or a <c>&lt;style&gt;</c> block).</summary>
    Selector,

    /// <summary>A match of a Find &amp; Replace search ("Count All"), not a class usage.</summary>
    Text,
}

/// <summary>A file searched by <see cref="ClassUsageFinder"/>.</summary>
/// <param name="BookPath">The bookpath of the file.</param>
/// <param name="Text">The text of the file.</param>
/// <param name="IsStyleSheet">A CSS stylesheet (otherwise a markup file: (X)HTML, SVG, XML).</param>
public sealed record ClassUsageSource(string BookPath, string Text, bool IsStyleSheet);

/// <summary>One usage of a class.</summary>
/// <param name="BookPath">The file.</param>
/// <param name="Offset">The offset of the first character of the class name (after the dot in a selector).</param>
/// <param name="Line">The line of <paramref name="Offset"/> (1-based).</param>
/// <param name="Column">The column of <paramref name="Offset"/> (1-based).</param>
/// <param name="Kind">Attribute or selector.</param>
/// <param name="Context">
/// The text of the line of the usage, without the leading whitespace, at most <see cref="ClassUsageFinder.MaxContextLength"/>
/// characters (longer lines are cut around the usage) — empty when not known.
/// </param>
/// <param name="Length">The length of the usage in characters when it is a range to select (a text match); 0 otherwise.</param>
/// <param name="ContextMatchStart">
/// Where the usage starts in <paramref name="Context"/> (the name of the class, the text matched); -1 when not known.
/// </param>
/// <param name="ContextMatchLength">The length of the usage in <paramref name="Context"/>; 0 when there is nothing to mark.</param>
public sealed record ClassUsage(
    string BookPath,
    int Offset,
    int Line,
    int Column,
    ClassUsageKind Kind,
    string Context = "",
    int Length = 0,
    int ContextMatchStart = -1,
    int ContextMatchLength = 0);

/// <summary>
/// "Find Usages" of a CSS class in the whole book: every token of a <c>class</c> attribute equal to the name
/// (case-sensitive) in the markup files, and every <c>.name</c> in the selectors of the stylesheets and of the
/// <c>&lt;style&gt;</c> blocks (also inside <c>@media</c> and <c>:not(…)</c>).
/// </summary>
/// <remarks>
/// Matching is literal (no entity or CSS escape decoding), so a usage is always a verbatim occurrence of the name in
/// the text: files and tags that do not contain the name are skipped without parsing. The files are searched in
/// parallel.
/// </remarks>
public static class ClassUsageFinder
{
    /// <summary>The longest <see cref="ClassUsage.Context"/> (a longer line is cut around the usage, with "…").</summary>
    public const int MaxContextLength = 200;

    /// <summary>
    /// The usages of <paramref name="className"/>, in the order of <paramref name="sources"/> and, within a file, in
    /// text order.
    /// </summary>
    public static IReadOnlyList<ClassUsage> Find(string className, IEnumerable<ClassUsageSource> sources)
    {
        ArgumentException.ThrowIfNullOrEmpty(className);
        ArgumentNullException.ThrowIfNull(sources);

        return sources
            .AsParallel()
            .AsOrdered()
            .SelectMany(source => FindInSource(className, source))
            .ToList();
    }

    /// <summary>
    /// The matches of a text search in <paramref name="text"/> as usages of the kind <see cref="ClassUsageKind.Text"/>
    /// (with the line, the column and the line of the match as context), in the order of <paramref name="matches"/>.
    /// </summary>
    /// <param name="bookPath">The bookpath of the file.</param>
    /// <param name="text">The text that was searched.</param>
    /// <param name="matches">The matches as offsets <c>[Start, End)</c> in <paramref name="text"/>.</param>
    public static IReadOnlyList<ClassUsage> FromTextMatches(
        string bookPath, string text, IReadOnlyList<(int Start, int End)> matches)
    {
        ArgumentNullException.ThrowIfNull(bookPath);
        ArgumentNullException.ThrowIfNull(text);
        ArgumentNullException.ThrowIfNull(matches);

        if (matches.Count == 0)
        {
            return Array.Empty<ClassUsage>();
        }

        LineIndex lines = new(text);
        List<ClassUsage> usages = new(matches.Count);
        foreach ((int start, int end) in matches)
        {
            int offset = Math.Clamp(start, 0, text.Length);
            (int line, int column) = lines.LocationOf(offset);
            int length = Math.Max(0, end - start);
            (string context, int matchStart, int matchLength) = Context(text, offset, column, length);
            usages.Add(new ClassUsage(
                bookPath, offset, line, column, ClassUsageKind.Text, context, length, matchStart, matchLength));
        }

        return usages;
    }

    private static List<ClassUsage> FindInSource(string className, ClassUsageSource source)
    {
        List<ClassUsage> usages = new();
        if (!source.Text.Contains(className, StringComparison.Ordinal))
        {
            return usages;
        }

        SortedDictionary<int, ClassUsageKind> found = new();
        if (source.IsStyleSheet)
        {
            AddSelectorUsages(found, source.Text, new CssInfo(source.Text).Rules, className);
        }
        else
        {
            AddAttributeUsages(found, source.Text, className);
            foreach (CssInfo block in new HtmlStyleInfo(source.Text).Styles)
            {
                AddSelectorUsages(found, source.Text, block.Rules, className);
            }
        }

        if (found.Count == 0)
        {
            return usages;
        }

        LineIndex lines = new(source.Text);
        foreach ((int offset, ClassUsageKind kind) in found)
        {
            (int line, int column) = lines.LocationOf(offset);
            (string context, int matchStart, int matchLength) = Context(source.Text, offset, column, className.Length);
            usages.Add(new ClassUsage(source.BookPath, offset, line, column, kind, context, 0, matchStart, matchLength));
        }

        return usages;
    }

    private static void AddAttributeUsages(SortedDictionary<int, ClassUsageKind> found, string text, string className)
    {
        foreach ((int pos, int len) in TagLister.EnumerateOpeningTags(text))
        {
            if (!text.AsSpan(pos, len).Contains(className, StringComparison.Ordinal))
            {
                continue;
            }

            TagLister.AttInfo attribute = TagLister.ParseAttribute(text.Substring(pos, len), "class");
            if (attribute.Pos == -1 || attribute.VLen <= 0)
            {
                continue;
            }

            string value = attribute.AValue;
            int i = 0;
            while (i < value.Length)
            {
                while (i < value.Length && char.IsWhiteSpace(value[i]))
                {
                    i++;
                }

                int start = i;
                while (i < value.Length && !char.IsWhiteSpace(value[i]))
                {
                    i++;
                }

                if (i - start == className.Length && string.CompareOrdinal(value, start, className, 0, className.Length) == 0)
                {
                    found.TryAdd(pos + attribute.VPos + start, ClassUsageKind.ClassAttribute);
                }
            }
        }
    }

    private static void AddSelectorUsages(SortedDictionary<int, ClassUsageKind> found, string text, IEnumerable<CssRule> rules, string className)
    {
        foreach (CssRule rule in rules)
        {
            if (rule.SelectorText.Length == 0 || rule.BlockStart <= rule.SelectorStart || rule.BlockStart > text.Length)
            {
                continue;
            }

            string region = text[rule.SelectorStart..rule.BlockStart];
            foreach ((int index, string name) in CssToolbox.FindClassesInSelector(region))
            {
                if (string.Equals(name, className, StringComparison.Ordinal))
                {
                    found.TryAdd(rule.SelectorStart + index, ClassUsageKind.Selector);
                }
            }
        }
    }

    // The line of the usage at offset (whose column is 1-based): without the line break and the leading whitespace, cut
    // to MaxContextLength around the usage. Also where the usage (matchLength characters) lies in that text.
    private static (string Text, int MatchStart, int MatchLength) Context(string text, int offset, int column, int matchLength)
    {
        int lineStart = offset - (column - 1);
        int lineEnd = text.IndexOf('\n', offset);
        if (lineEnd < 0)
        {
            lineEnd = text.Length;
        }

        if (lineEnd > lineStart && text[lineEnd - 1] == '\r')
        {
            lineEnd--;
        }

        int start = lineStart;
        while (start < offset && char.IsWhiteSpace(text[start]))
        {
            start++;
        }

        // The usage may reach past the end of its line (a match over several lines): only the part in the line is marked.
        int MarkedLength(int to) => Math.Clamp(matchLength, 0, Math.Max(0, to - offset));

        if (lineEnd - start <= MaxContextLength)
        {
            return (text[start..lineEnd], offset - start, MarkedLength(lineEnd));
        }

        // Keep the usage in view: a window from a little before it.
        int from = Math.Max(start, offset - (MaxContextLength / 4));
        int to = Math.Min(lineEnd, from + MaxContextLength);
        string prefix = from > start ? "…" : string.Empty;
        string cut = prefix + text[from..to] + (to < lineEnd ? "…" : string.Empty);
        return (cut, prefix.Length + offset - from, MarkedLength(to));
    }

    /// <summary>Line starts of a text, for offset → (line, column).</summary>
    private sealed class LineIndex
    {
        private readonly List<int> _starts = new() { 0 };

        public LineIndex(string text)
        {
            for (int i = 0; i < text.Length; i++)
            {
                if (text[i] == '\n')
                {
                    _starts.Add(i + 1);
                }
            }
        }

        public (int Line, int Column) LocationOf(int offset)
        {
            int index = _starts.BinarySearch(offset);
            int line = index >= 0 ? index : ~index - 1;
            return (line + 1, offset - _starts[line] + 1);
        }
    }
}
