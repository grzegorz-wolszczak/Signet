using System;
using System.Collections.Generic;
using System.Linq;
using Signet.Core.Parsers;

namespace Signet.Core.BookManipulation;

/// <summary>
/// "Find Usages" of an <c>id</c> in the whole book: every <c>id="…"</c> attribute equal to the name
/// (case-sensitive) in the markup files, and every <c>#name</c> in the selectors of the stylesheets and of the
/// <c>&lt;style&gt;</c> block (also inside <c>@media</c> and <c>:not(…)</c>) — the same shape of result as
/// <see cref="ClassUsageFinder"/>, so both searches share the "Find Usages" panel.
/// </summary>
/// <remarks>
/// Matching is literal (no entity or CSS escape decoding), so a usage is always a verbatim occurrence of the name in
/// the text: files and tags that do not contain the name are skipped without parsing. The files are searched in
/// parallel.
/// </remarks>
public static class IdUsageFinder
{
    /// <summary>
    /// The usages of <paramref name="id"/>, in the order of <paramref name="sources"/> and, within a file, in
    /// text order.
    /// </summary>
    public static IReadOnlyList<ClassUsage> Find(string id, IEnumerable<ClassUsageSource> sources)
    {
        ArgumentException.ThrowIfNullOrEmpty(id);
        ArgumentNullException.ThrowIfNull(sources);

        return sources
            .AsParallel()
            .AsOrdered()
            .SelectMany(source => FindInSource(id, source))
            .ToList();
    }

    /// <summary>
    /// The id under the caret in the <c>id</c> attribute value of an opening or empty tag (inside the value or
    /// right at its left/right edge). <c>null</c> when the caret is not on an id value.
    /// </summary>
    public static string? FindIdAtCaret(string text, int caretOffset)
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

        TagLister.AttInfo attribute = TagLister.ParseAttribute(text.Substring(tag.Pos, tag.Len), "id");
        if (attribute.Pos == -1 || attribute.VLen <= 0)
        {
            return null;
        }

        int relativeCaret = pos - (tag.Pos + attribute.VPos);
        return relativeCaret >= 0 && relativeCaret <= attribute.VLen ? attribute.AValue : null;
    }

    /// <summary>
    /// The id under the caret in a rule selector (<c>#name</c>) — in a CSS stylesheet (<paramref name="isStyleSheet"/>)
    /// or in a <c>&lt;style&gt;</c> block of an XHTML file. The caret may be inside the name, right after it, or
    /// before/on the hash. <c>null</c> when the caret is not on an id in a selector.
    /// </summary>
    public static string? FindIdAtCaretInSelector(string text, int caretOffset, bool isStyleSheet)
    {
        ArgumentNullException.ThrowIfNull(text);

        IReadOnlyList<CssInfo> blocks = isStyleSheet ? [new CssInfo(text)] : new HtmlStyleInfo(text).Styles;
        foreach (CssInfo block in blocks)
        {
            foreach (CssRule rule in block.Rules)
            {
                if (rule.SelectorText.Length == 0 || caretOffset < rule.SelectorStart - 1 || caretOffset > rule.BlockStart)
                {
                    continue;
                }

                string region = text[rule.SelectorStart..rule.BlockStart];
                foreach ((int index, string name) in CssToolbox.FindIdsInSelector(region))
                {
                    int start = rule.SelectorStart + index;
                    if (caretOffset >= start - 1 && caretOffset <= start + name.Length)
                    {
                        return name;
                    }
                }
            }
        }

        return null;
    }

    private static List<ClassUsage> FindInSource(string id, ClassUsageSource source)
    {
        List<ClassUsage> usages = new();
        if (!source.Text.Contains(id, StringComparison.Ordinal))
        {
            return usages;
        }

        SortedDictionary<int, ClassUsageKind> found = new();
        if (source.IsStyleSheet)
        {
            AddSelectorUsages(found, source.Text, new CssInfo(source.Text).Rules, id);
        }
        else
        {
            AddAttributeUsages(found, source.Text, id);
            foreach (CssInfo block in new HtmlStyleInfo(source.Text).Styles)
            {
                AddSelectorUsages(found, source.Text, block.Rules, id);
            }
        }

        if (found.Count == 0)
        {
            return usages;
        }

        ClassUsageFinder.LineIndex lines = new(source.Text);
        foreach ((int offset, ClassUsageKind kind) in found)
        {
            (int line, int column) = lines.LocationOf(offset);
            (string context, int matchStart, int matchLength) = ClassUsageFinder.Context(source.Text, offset, column, id.Length);
            usages.Add(new ClassUsage(source.BookPath, offset, line, column, kind, context, 0, matchStart, matchLength));
        }

        return usages;
    }

    private static void AddAttributeUsages(SortedDictionary<int, ClassUsageKind> found, string text, string id)
    {
        foreach ((int pos, int len) in TagLister.EnumerateOpeningTags(text))
        {
            if (!text.AsSpan(pos, len).Contains(id, StringComparison.Ordinal))
            {
                continue;
            }

            TagLister.AttInfo attribute = TagLister.ParseAttribute(text.Substring(pos, len), "id");
            if (attribute.Pos == -1 || attribute.VLen <= 0)
            {
                continue;
            }

            if (string.Equals(attribute.AValue, id, StringComparison.Ordinal))
            {
                found.TryAdd(pos + attribute.VPos, ClassUsageKind.IdAttribute);
            }
        }
    }

    private static void AddSelectorUsages(SortedDictionary<int, ClassUsageKind> found, string text, IEnumerable<CssRule> rules, string id)
    {
        foreach (CssRule rule in rules)
        {
            if (rule.SelectorText.Length == 0 || rule.BlockStart <= rule.SelectorStart || rule.BlockStart > text.Length)
            {
                continue;
            }

            string region = text[rule.SelectorStart..rule.BlockStart];
            foreach ((int index, string name) in CssToolbox.FindIdsInSelector(region))
            {
                if (string.Equals(name, id, StringComparison.Ordinal))
                {
                    found.TryAdd(rule.SelectorStart + index, ClassUsageKind.Selector);
                }
            }
        }
    }
}
