using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace Signet.Core.BookManipulation;

/// <summary>
/// A chain of directly nested <c>&lt;div&gt;</c>s with identical attributes, found by
/// <see cref="NestedDivCollapser.FindChains"/>.
/// </summary>
/// <param name="OuterPos">The 0-based offset of the outermost <c>&lt;div&gt;</c>'s opening tag.</param>
/// <param name="Depth">The number of <c>&lt;div&gt;</c>s in the chain (at least 2); collapsing removes <c>Depth - 1</c>.</param>
/// <param name="OpenTag">The text of the outermost opening tag (for the report).</param>
/// <param name="Separators">
/// The comments and CDATA sections between the chain's tags (kept by collapsing, but they make the chain risky).
/// </param>
internal sealed record NestedDivChain(int OuterPos, int Depth, string OpenTag, IReadOnlyList<NestedDivSeparator> Separators);

/// <summary>A comment or CDATA section between the tags of a <see cref="NestedDivChain"/>.</summary>
/// <param name="Pos">The 0-based offset in the file.</param>
/// <param name="Text">The full text (<c>&lt;!-- … --&gt;</c> or <c>&lt;![CDATA[ … ]]&gt;</c>).</param>
/// <param name="IsCData">Whether it is a CDATA section (otherwise a comment).</param>
internal sealed record NestedDivSeparator(int Pos, string Text, bool IsCData);

/// <summary>
/// "Remove unnecessary <c>&lt;div&gt;</c> nesting": finds chains of <c>&lt;div&gt;</c>s nested directly in each
/// other with identical attributes (the same set of attributes and values, attribute order ignored, the classes of
/// <c>class</c> compared as a set) and collapses each chain into a single <c>&lt;div&gt;</c>. Works on the source
/// text (<see cref="TagLister"/>), so nothing outside the removed tags changes except the indentation.
/// </summary>
/// <remarks>
/// <para>"Directly nested" means that between the opening tags (and between the closing tags) there is nothing but
/// whitespace, comments and CDATA sections. Anything else (text, another element) breaks the chain.</para>
/// <para>Collapsing keeps the outermost opening and closing tag (with their indentation), removes the inner ones
/// and keeps the comments/CDATA sections inside the remaining <c>&lt;div&gt;</c> in their order. The content lines
/// are shifted left by the indentation of the removed levels — only when every content line has that much leading
/// whitespace and the content contains no <c>&lt;pre&gt;</c>/<c>&lt;textarea&gt;</c>.</para>
/// </remarks>
internal static class NestedDivCollapser
{
    private static readonly string[] WhitespaceSensitiveTags = { "pre", "textarea" };

    /// <summary>All chains in <paramref name="text"/> (well-formed XHTML), in document order.</summary>
    public static IReadOnlyList<NestedDivChain> FindChains(string text)
    {
        ArgumentNullException.ThrowIfNull(text);
        TagLister lister = new(text);
        IReadOnlyList<TagLister.TagInfo> tags = lister.Tags;
        List<NestedDivChain> chains = new();
        HashSet<int> inner = new();

        for (int i = 0; i < tags.Count; i++)
        {
            if (inner.Contains(i) || !IsDivOpen(tags[i]))
            {
                continue;
            }

            Chain? chain = Walk(lister, i);
            if (chain is null)
            {
                continue;
            }

            foreach (int index in chain.Divs.Skip(1))
            {
                inner.Add(index);
            }

            TagLister.TagInfo outer = tags[i];
            chains.Add(new NestedDivChain(
                outer.Pos,
                chain.Divs.Count,
                text.Substring(outer.Pos, outer.Len),
                chain.Separators
                    .Order()
                    .Select(s => new NestedDivSeparator(tags[s].Pos, text.Substring(tags[s].Pos, tags[s].Len), tags[s].Kind == TagKind.CData))
                    .ToList()));
        }

        return chains;
    }

    /// <summary>
    /// Collapses the chain whose outermost <c>&lt;div&gt;</c> starts at <paramref name="outerPos"/>. Returns the new
    /// text, or <c>null</c> when there is no chain at that position.
    /// </summary>
    public static string? Collapse(string text, int outerPos)
    {
        ArgumentNullException.ThrowIfNull(text);
        TagLister lister = new(text);
        IReadOnlyList<TagLister.TagInfo> tags = lister.Tags;
        int outerIndex = -1;
        for (int i = 0; i < tags.Count; i++)
        {
            if (tags[i].Pos == outerPos && IsDivOpen(tags[i]))
            {
                outerIndex = i;
                break;
            }
        }

        if (outerIndex < 0 || Walk(lister, outerIndex) is not { } chain)
        {
            return null;
        }

        TagLister.TagInfo outerOpen = tags[chain.Divs[0]];
        TagLister.TagInfo innerOpen = tags[chain.Divs[^1]];
        TagLister.TagInfo innerClose = tags[lister.FindCloseTagForOpen(chain.Divs[^1])];
        TagLister.TagInfo outerClose = tags[lister.FindCloseTagForOpen(chain.Divs[0])];

        int openStart = outerOpen.Pos + outerOpen.Len;
        int openEnd = innerOpen.Pos + innerOpen.Len;
        int closeStart = WhitespaceStart(text, innerClose.Pos, openEnd);
        int closeEnd = WhitespaceStart(text, outerClose.Pos, closeStart);

        string openKept = KeptSeparators(text, chain.Separators.Select(s => tags[s]), openStart, openEnd);
        string content = text[openEnd..closeStart];
        string closeKept = KeptSeparators(text, chain.Separators.Select(s => tags[s]), closeStart, closeEnd);
        string middle = openKept + content + closeKept;

        int shift = IndentShift(text, outerOpen.Pos, innerOpen.Pos);
        if (shift > 0 && CanShift(content, shift) && !ContainsWhitespaceSensitiveTag(tags, openEnd, closeStart))
        {
            middle = ShiftLines(middle, shift);
        }

        return string.Concat(text.AsSpan(0, openStart), middle, text.AsSpan(closeEnd));
    }

    private sealed record Chain(List<int> Divs, List<int> Separators);

    // The chain starting at the opening tag with index "outer", or null when the div has no identical direct child.
    private static Chain? Walk(TagLister lister, int outer)
    {
        IReadOnlyList<TagLister.TagInfo> tags = lister.Tags;
        string text = lister.Source;
        List<int> divs = new() { outer };
        List<int> separators = new();
        Dictionary<string, string> attributes = ElementAttributes.Parse(text.Substring(tags[outer].Pos, tags[outer].Len));

        while (true)
        {
            int current = divs[^1];
            List<int> found = new();
            int next = NextSignificant(lister, current, found);
            if (next < 0 || !IsDivOpen(tags[next])
                || !ElementAttributes.Same(attributes, ElementAttributes.Parse(text.Substring(tags[next].Pos, tags[next].Len))))
            {
                break;
            }

            int innerClose = lister.FindCloseTagForOpen(next);
            int outerClose = lister.FindCloseTagForOpen(current);
            if (innerClose < 0 || outerClose < 0 || NextSignificant(lister, innerClose, found) != outerClose)
            {
                break;
            }

            divs.Add(next);
            separators.AddRange(found);
        }

        return divs.Count >= 2 ? new Chain(divs, separators) : null;
    }

    // The index of the first tag after tag "index" that is not a comment/CDATA, provided only whitespace, comments
    // and CDATA sections lie in between (those are added to "separators"); -1 otherwise.
    internal static int NextSignificant(TagLister lister, int index, List<int> separators)
    {
        IReadOnlyList<TagLister.TagInfo> tags = lister.Tags;
        string text = lister.Source;
        int end = tags[index].Pos + tags[index].Len;
        List<int> found = new();
        for (int k = index + 1; k < tags.Count && tags[k].Pos >= 0; k++)
        {
            if (!IsWhitespace(text, end, tags[k].Pos))
            {
                return -1;
            }

            if (tags[k].Kind is TagKind.Comment or TagKind.CData)
            {
                found.Add(k);
                end = tags[k].Pos + tags[k].Len;
                continue;
            }

            separators.AddRange(found);
            return k;
        }

        return -1;
    }

    internal static bool IsDivOpen(TagLister.TagInfo tag) =>
        tag.Kind == TagKind.Begin && string.Equals(tag.TagName, "div", StringComparison.OrdinalIgnoreCase);

    private static bool IsWhitespace(string text, int start, int end)
    {
        for (int i = start; i < end; i++)
        {
            if (!IsWhitespaceChar(text[i]))
            {
                return false;
            }
        }

        return true;
    }

    private static bool IsWhitespaceChar(char c) => c is ' ' or '\t' or '\r' or '\n' or '\f';

    // The start of the whitespace run that ends at "pos" (not before "limit").
    internal static int WhitespaceStart(string text, int pos, int limit)
    {
        while (pos > limit && IsWhitespaceChar(text[pos - 1]))
        {
            pos--;
        }

        return pos;
    }

    // The comments/CDATA sections inside [start, end), each with the whitespace in front of it.
    internal static string KeptSeparators(string text, IEnumerable<TagLister.TagInfo> separators, int start, int end)
    {
        StringBuilder kept = new();
        foreach (TagLister.TagInfo tag in separators.Where(t => t.Pos >= start && t.Pos + t.Len <= end).OrderBy(t => t.Pos))
        {
            int from = WhitespaceStart(text, tag.Pos, start);
            kept.Append(text, from, tag.Pos + tag.Len - from);
        }

        return kept.ToString();
    }

    // How far the content moves left: the indentation of the innermost opening tag minus that of the outermost one
    // (0 unless both tags start their lines).
    private static int IndentShift(string text, int outerPos, int innerPos)
    {
        int? outerIndent = LineIndent(text, outerPos);
        int? innerIndent = LineIndent(text, innerPos);
        return outerIndent is { } o && innerIndent is { } i && i > o ? i - o : 0;
    }

    private static int? LineIndent(string text, int pos)
    {
        int lineStart = pos == 0 ? 0 : text.LastIndexOf('\n', pos - 1) + 1;
        return IsWhitespace(text, lineStart, pos) ? pos - lineStart : null;
    }

    // Every non-blank content line (after the first line break) has at least "shift" leading whitespace characters.
    private static bool CanShift(string content, int shift)
    {
        string[] lines = content.Split('\n');
        return lines.Skip(1).All(line => line.Trim().Length == 0 || LeadingWhitespace(line) >= shift);
    }

    private static string ShiftLines(string text, int shift)
    {
        string[] lines = text.Split('\n');
        for (int i = 1; i < lines.Length; i++)
        {
            lines[i] = lines[i][Math.Min(shift, LeadingWhitespace(lines[i]))..];
        }

        return string.Join('\n', lines);
    }

    private static int LeadingWhitespace(string line)
    {
        int n = 0;
        while (n < line.Length && line[n] is ' ' or '\t' or '\f')
        {
            n++;
        }

        return n;
    }

    private static bool ContainsWhitespaceSensitiveTag(IReadOnlyList<TagLister.TagInfo> tags, int start, int end) =>
        tags.Any(t => t.Pos >= start && t.Pos < end && t.Kind == TagKind.Begin
            && WhitespaceSensitiveTags.Contains(t.TagName, StringComparer.OrdinalIgnoreCase));
}
