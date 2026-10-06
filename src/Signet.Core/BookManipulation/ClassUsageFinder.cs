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
public sealed record ClassUsage(string BookPath, int Offset, int Line, int Column, ClassUsageKind Kind, string Context = "");

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
            usages.Add(new ClassUsage(source.BookPath, offset, line, column, kind, Context(source.Text, offset, column)));
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
    // to MaxContextLength around the usage.
    private static string Context(string text, int offset, int column)
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

        if (lineEnd - start <= MaxContextLength)
        {
            return text[start..lineEnd];
        }

        // Keep the usage in view: a window from a little before it.
        int from = Math.Max(start, offset - (MaxContextLength / 4));
        int to = Math.Min(lineEnd, from + MaxContextLength);
        return (from > start ? "…" : string.Empty) + text[from..to] + (to < lineEnd ? "…" : string.Empty);
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
