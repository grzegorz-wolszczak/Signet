using System;
using System.Collections.Generic;
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
public sealed record ClassUsage(string BookPath, int Offset, int Line, int Column, ClassUsageKind Kind);

/// <summary>
/// "Find Usages" of a CSS class in the whole book: every token of a <c>class</c> attribute equal to the name
/// (case-sensitive) in the markup files, and every <c>.name</c> in the selectors of the stylesheets and of the
/// <c>&lt;style&gt;</c> blocks (also inside <c>@media</c> and <c>:not(…)</c>).
/// </summary>
public static class ClassUsageFinder
{
    /// <summary>
    /// The usages of <paramref name="className"/>, in the order of <paramref name="sources"/> and, within a file, in
    /// text order.
    /// </summary>
    public static IReadOnlyList<ClassUsage> Find(string className, IEnumerable<ClassUsageSource> sources)
    {
        ArgumentException.ThrowIfNullOrEmpty(className);
        ArgumentNullException.ThrowIfNull(sources);

        List<ClassUsage> usages = new();
        foreach (ClassUsageSource source in sources)
        {
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
                continue;
            }

            LineIndex lines = new(source.Text);
            foreach ((int offset, ClassUsageKind kind) in found)
            {
                (int line, int column) = lines.LocationOf(offset);
                usages.Add(new ClassUsage(source.BookPath, offset, line, column, kind));
            }
        }

        return usages;
    }

    private static void AddAttributeUsages(SortedDictionary<int, ClassUsageKind> found, string text, string className)
    {
        TagLister lister = new(text);
        foreach (TagLister.TagInfo tag in lister.Tags)
        {
            if (tag.Pos < 0 || tag.Kind is not (TagKind.Begin or TagKind.SelfClosing))
            {
                continue;
            }

            TagLister.AttInfo attribute = TagLister.ParseAttribute(text.Substring(tag.Pos, tag.Len), "class");
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
                    found.TryAdd(tag.Pos + attribute.VPos + start, ClassUsageKind.ClassAttribute);
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
