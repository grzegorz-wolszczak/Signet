using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace Signet.Core.BookManipulation;

/// <summary>
/// A run of consecutive sibling <c>&lt;div&gt;</c>s with identical attributes, found by
/// <see cref="AdjacentDivMerger.FindGroups"/>.
/// </summary>
/// <param name="FirstPos">The 0-based offset of the first <c>&lt;div&gt;</c>'s opening tag.</param>
/// <param name="Count">The number of <c>&lt;div&gt;</c>s in the run (at least 2); merging removes <c>Count - 1</c>.</param>
/// <param name="Level">
/// The nesting level of the run: the number of elements between it and <c>&lt;body&gt;</c>, plus one — a
/// <c>&lt;div&gt;</c> directly in <c>&lt;body&gt;</c> has level 1, in <c>body &gt; section</c> level 2.
/// </param>
/// <param name="OpenTag">The text of the first opening tag (for the report).</param>
/// <param name="DivPositions">The offsets of the opening tags of all the <c>&lt;div&gt;</c>s, in document order.</param>
/// <param name="Separators">
/// The comments and CDATA sections between the <c>&lt;div&gt;</c>s (kept inside the merged one, but they make the run
/// risky).
/// </param>
internal sealed record AdjacentDivGroup(
    int FirstPos, int Count, int Level, string OpenTag, IReadOnlyList<int> DivPositions, IReadOnlyList<NestedDivSeparator> Separators);

/// <summary>
/// "Merge adjacent <c>&lt;div&gt;</c> tags with identical attributes": finds runs of sibling <c>&lt;div&gt;</c>s that
/// follow each other with identical attributes (compared like <see cref="NestedDivCollapser"/>: the same set of
/// attributes and values, the classes of <c>class</c> as a set) and merges each run into its first
/// <c>&lt;div&gt;</c> — the counterpart of the Code View's "Merge Content" for the whole book. Works on the source
/// text (<see cref="TagLister"/>).
/// </summary>
/// <remarks>
/// <para>Only whitespace, comments and CDATA sections may lie between the <c>&lt;div&gt;</c>s (between a closing tag
/// and the next opening tag); text or another element ends the run.</para>
/// <para>Merging keeps the first opening tag and the last closing tag; every closing tag in between (with the
/// whitespace in front of it) and the following opening tag are removed, so the contents follow each other with
/// their own line breaks and indentation. Comments/CDATA sections between the <c>&lt;div&gt;</c>s stay in place, inside
/// the merged one. When the next content does not start with whitespace and nothing else is left at the joint, the
/// whitespace before the removed closing tag (or else the whitespace between the <c>&lt;div&gt;</c>s) is kept, so two
/// words do not run into one.</para>
/// </remarks>
internal static class AdjacentDivMerger
{
    /// <summary>All runs in <paramref name="text"/> (well-formed XHTML), in document order.</summary>
    public static IReadOnlyList<AdjacentDivGroup> FindGroups(string text)
    {
        ArgumentNullException.ThrowIfNull(text);
        TagLister lister = new(text);
        IReadOnlyList<TagLister.TagInfo> tags = lister.Tags;
        Dictionary<int, int> levels = DivLevels(tags);
        HashSet<int> taken = new();
        List<AdjacentDivGroup> groups = new();

        for (int i = 0; i < tags.Count && tags[i].Pos >= 0; i++)
        {
            if (taken.Contains(i) || !levels.TryGetValue(i, out int level) || level < 1)
            {
                continue;
            }

            Run? run = Walk(lister, i);
            if (run is null)
            {
                continue;
            }

            taken.UnionWith(run.Divs);
            groups.Add(new AdjacentDivGroup(
                tags[i].Pos,
                run.Divs.Count,
                level,
                text.Substring(tags[i].Pos, tags[i].Len),
                run.Divs.Select(d => tags[d].Pos).ToList(),
                run.Separators
                    .Order()
                    .Select(s => new NestedDivSeparator(tags[s].Pos, text.Substring(tags[s].Pos, tags[s].Len), tags[s].Kind == TagKind.CData))
                    .ToList()));
        }

        return groups;
    }

    /// <summary>
    /// Merges the run whose first <c>&lt;div&gt;</c> starts at <paramref name="firstPos"/>. Returns the new text, or
    /// <c>null</c> when there is no run at that position.
    /// </summary>
    public static string? Merge(string text, int firstPos)
    {
        ArgumentNullException.ThrowIfNull(text);
        TagLister lister = new(text);
        IReadOnlyList<TagLister.TagInfo> tags = lister.Tags;
        int first = -1;
        for (int i = 0; i < tags.Count && tags[i].Pos >= 0; i++)
        {
            if (tags[i].Pos == firstPos && NestedDivCollapser.IsDivOpen(tags[i]))
            {
                first = i;
                break;
            }
        }

        if (first < 0 || Walk(lister, first) is not { } run)
        {
            return null;
        }

        List<TagLister.TagInfo> separators = run.Separators.Select(s => tags[s]).ToList();
        StringBuilder result = new();
        int copied = 0;
        for (int k = 0; k < run.Divs.Count - 1; k++)
        {
            TagLister.TagInfo open = tags[run.Divs[k]];
            TagLister.TagInfo close = tags[lister.FindCloseTagForOpen(run.Divs[k])];
            TagLister.TagInfo nextOpen = tags[run.Divs[k + 1]];

            int contentStart = open.Pos + open.Len;
            int removeStart = NestedDivCollapser.WhitespaceStart(text, close.Pos, contentStart);
            int gapStart = close.Pos + close.Len;
            int removeEnd = nextOpen.Pos + nextOpen.Len;

            string kept = NestedDivCollapser.KeptSeparators(text, separators, gapStart, nextOpen.Pos);
            if (kept.Length == 0 && !StartsWithWhitespace(text, removeEnd))
            {
                // Nothing would be left at the joint ("one </div><div>two", "one</div> <div>two"): keep the whitespace
                // before the closing tag, or else the gap between the divs, so the two words stay apart.
                kept = removeStart < close.Pos ? text[removeStart..close.Pos] : text[gapStart..nextOpen.Pos];
            }

            result.Append(text, copied, removeStart - copied).Append(kept);
            copied = removeEnd;
        }

        result.Append(text, copied, text.Length - copied);
        return result.ToString();
    }

    private sealed record Run(List<int> Divs, List<int> Separators);

    // The run starting at the opening tag with index "first", or null when the next sibling is not an identical div.
    private static Run? Walk(TagLister lister, int first)
    {
        IReadOnlyList<TagLister.TagInfo> tags = lister.Tags;
        string text = lister.Source;
        if (!NestedDivCollapser.IsDivOpen(tags[first]))
        {
            return null;
        }

        Dictionary<string, string> attributes = ElementAttributes.Parse(text.Substring(tags[first].Pos, tags[first].Len));
        List<int> divs = new() { first };
        List<int> separators = new();

        while (true)
        {
            int close = lister.FindCloseTagForOpen(divs[^1]);
            if (close < 0)
            {
                break;
            }

            List<int> found = new();
            int next = NestedDivCollapser.NextSignificant(lister, close, found);
            if (next < 0 || !NestedDivCollapser.IsDivOpen(tags[next])
                || !ElementAttributes.Same(attributes, ElementAttributes.Parse(text.Substring(tags[next].Pos, tags[next].Len))))
            {
                break;
            }

            divs.Add(next);
            separators.AddRange(found);
        }

        return divs.Count >= 2 ? new Run(divs, separators) : null;
    }

    // The level of every <div> opening tag (by tag index): the number of open elements above it counted from <body>
    // (a div directly in <body> = 1); -1 outside <body>.
    private static Dictionary<int, int> DivLevels(IReadOnlyList<TagLister.TagInfo> tags)
    {
        Dictionary<int, int> levels = new();
        List<string> open = new();
        for (int i = 0; i < tags.Count && tags[i].Pos >= 0; i++)
        {
            TagLister.TagInfo tag = tags[i];
            if (tag.Kind == TagKind.Begin)
            {
                if (NestedDivCollapser.IsDivOpen(tag))
                {
                    int body = open.FindLastIndex(n => string.Equals(n, "body", StringComparison.OrdinalIgnoreCase));
                    levels[i] = body < 0 ? -1 : open.Count - body;
                }

                open.Add(tag.TagName);
            }
            else if (tag.Kind == TagKind.End && open.Count > 0)
            {
                open.RemoveAt(open.Count - 1);
            }
        }

        return levels;
    }

    private static bool StartsWithWhitespace(string text, int pos) =>
        pos < text.Length && text[pos] is ' ' or '\t' or '\r' or '\n' or '\f';
}
