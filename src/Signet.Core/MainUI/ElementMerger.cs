using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using Signet.Core.Localization;

namespace Signet.Core.MainUI;

/// <summary>
/// A selection whose elements can be merged by the "Merge Content" action.
/// </summary>
/// <param name="ElementName">The name of the merged elements as in the source (e.g. <c>p</c>, <c>h2</c>).</param>
/// <param name="Count">The number of merged elements (at least 2).</param>
/// <param name="FirstOpenTag">The opening tag of the first element — its attributes are kept.</param>
/// <param name="DifferingOpenTags">
/// The opening tags of the following elements whose attributes differ from the first one's (their
/// attributes are lost). An empty list = all attributes are the same.
/// </param>
/// <param name="TextBetween">
/// The text lying between the elements (whitespace collapsed to a space) — it goes into the merged element.
/// </param>
/// <param name="RemovedNodes">
/// Comments, CDATA sections and <c>&lt;?…?&gt;</c> instructions between the elements — they are removed.
/// </param>
public sealed record ElementMergeCandidate(
    string ElementName,
    int Count,
    string FirstOpenTag,
    IReadOnlyList<string> DifferingOpenTags,
    IReadOnlyList<string> TextBetween,
    IReadOnlyList<string> RemovedNodes)
{
    /// <summary>Whether the attributes of any element differ from those of the first.</summary>
    public bool AttributesDiffer => DifferingOpenTags.Count > 0;

    /// <summary>Whether a warning is needed before merging (lost attributes, text between the elements, removed nodes).</summary>
    public bool NeedsConfirmation => AttributesDiffer || TextBetween.Count > 0 || RemovedNodes.Count > 0;
}

/// <summary>
/// "Merge Content": a selection covering at least two elements
/// of the same kind (e.g. <c>&lt;p&gt;…&lt;/p&gt; &lt;p&gt;…&lt;/p&gt;</c>) is turned into a single element
/// with the first one's attributes and the contents of all of them in order.
/// </summary>
/// <remarks>
/// Conditions: the start of the selection (ignoring whitespace at the edges) lies anywhere in the first
/// element (from its opening <c>&lt;</c> to its closing <c>&gt;</c>), and the end — in the last one;
/// the selection is expanded to whole elements. The first and the last are direct
/// siblings (children of the deepest element enclosing the whole selection); an edge in text outside
/// them blocks the merge. All elements from the first to the last have the same name; there are
/// no other tags between them. The text between the elements
/// goes into the result, while comments / CDATA / <c>&lt;?…?&gt;</c> are removed; what remains between
/// the elements has its whitespace collapsed to a single space (a gap alone = a space, elements that touch
/// = no separator). The content of the elements themselves is not changed. Attributes are compared as
/// name=value pairs regardless of order; <c>class</c> as a set of class names.
/// </remarks>
public static partial class ElementMerger
{
    private sealed record Part(string OpenTag, bool SelfClosing, string Content, string CloseTag, string GapBefore);

    private sealed record Collected(string Name, List<Part> Parts, List<string> RemovedNodes, int Start, int End);

    /// <summary>
    /// Checks whether the selection <c>[selectionStart, selectionEnd)</c> can be merged. <c>null</c>
    /// when it does not meet the conditions.
    /// </summary>
    public static ElementMergeCandidate? Analyze(string text, int selectionStart, int selectionEnd)
    {
        ArgumentNullException.ThrowIfNull(text);
        if (Collect(text, selectionStart, selectionEnd) is not { } found)
        {
            return null;
        }

        Dictionary<string, string> first = ParseAttributes(found.Parts[0].OpenTag);
        List<string> differing = found.Parts
            .Skip(1)
            .Where(p => !SameAttributes(first, ParseAttributes(p.OpenTag)))
            .Select(p => p.OpenTag)
            .ToList();
        List<string> textBetween = found.Parts
            .Select(p => p.GapBefore.Trim())
            .Where(g => g.Length > 0)
            .ToList();

        return new ElementMergeCandidate(
            found.Name, found.Parts.Count, found.Parts[0].OpenTag, differing, textBetween, found.RemovedNodes);
    }

    /// <summary>
    /// Merges the elements of the selection into one (the first one's attributes) and selects the result. When the selection
    /// does not meet the conditions — unchanged, with a message.
    /// </summary>
    public static FormatEdit Merge(string text, int selectionStart, int selectionEnd)
    {
        ArgumentNullException.ThrowIfNull(text);
        if (Collect(text, selectionStart, selectionEnd) is not { } found)
        {
            return FormatEdit.Unchanged(text, selectionStart, selectionEnd) with
            {
                StatusMessage = CoreStrings.Get("Status_NothingToMerge"),
            };
        }

        StringBuilder content = new();
        bool pendingSpace = false;
        foreach (Part part in found.Parts)
        {
            // The gap / text before the element (already collapsed) — spaces at the edges only between contents.
            string gap = part.GapBefore;
            string core = gap.Trim();
            if (core.Length == 0)
            {
                pendingSpace |= gap.Length > 0;
            }
            else
            {
                if ((pendingSpace || gap[0] == ' ') && content.Length > 0)
                {
                    content.Append(' ');
                }

                content.Append(core);
                pendingSpace = gap[^1] == ' ';
            }

            if (part.Content.Length > 0)
            {
                if (pendingSpace && content.Length > 0)
                {
                    content.Append(' ');
                }

                content.Append(part.Content);
                pendingSpace = false;
            }
        }

        Part head = found.Parts[0];
        string merged;
        if (content.Length == 0 && head.SelfClosing)
        {
            merged = head.OpenTag;
        }
        else
        {
            string open = head.SelfClosing ? SelfClosingEndRegex().Replace(head.OpenTag, ">") : head.OpenTag;
            string close = head.SelfClosing ? $"</{found.Name}>" : head.CloseTag;
            merged = open + content + close;
        }

        string result = string.Concat(text.AsSpan(0, found.Start), merged, text.AsSpan(found.End));
        return new FormatEdit(result, found.Start, found.Start + merged.Length, true);
    }

    private static Collected? Collect(string text, int selectionStart, int selectionEnd)
    {
        int start = Math.Clamp(Math.Min(selectionStart, selectionEnd), 0, text.Length);
        int end = Math.Clamp(Math.Max(selectionStart, selectionEnd), 0, text.Length);
        while (start < end && char.IsWhiteSpace(text[start]))
        {
            start++;
        }

        while (end > start && char.IsWhiteSpace(text[end - 1]))
        {
            end--;
        }

        if (end <= start)
        {
            return null;
        }

        var lister = new TagLister(text);
        if (ExpandToSiblings(lister, start, end) is not { } range)
        {
            return null;
        }

        (start, end) = range;
        var parts = new List<Part>();
        var removed = new List<string>();
        string? name = null;
        int pos = start;
        string gapBefore = string.Empty;

        while (true)
        {
            int i = lister.FindFirstTagOnOrAfter(pos);
            TagLister.TagInfo open = lister.At(i);
            if (open.Len <= 0 || open.Pos != pos || open.Kind is not (TagKind.Begin or TagKind.SelfClosing))
            {
                return null;
            }

            string openTag = text.Substring(open.Pos, open.Len);
            string tagName = TagNameRegex().Match(openTag) is { Success: true } m ? m.Groups[1].Value : open.TagName;
            if (name is null)
            {
                name = tagName;
            }
            else if (!string.Equals(name, tagName, StringComparison.Ordinal))
            {
                return null;
            }

            int elementEnd;
            if (open.Kind == TagKind.SelfClosing)
            {
                elementEnd = open.Pos + open.Len;
                parts.Add(new Part(openTag, true, string.Empty, string.Empty, gapBefore));
            }
            else
            {
                TagLister.TagInfo close = lister.At(lister.FindCloseTagForOpen(i));
                if (close.Len <= 0 || close.Pos < open.Pos + open.Len)
                {
                    return null;
                }

                elementEnd = close.Pos + close.Len;
                parts.Add(new Part(
                    openTag, false, text[(open.Pos + open.Len)..close.Pos], text.Substring(close.Pos, close.Len), gapBefore));
            }

            if (elementEnd == end)
            {
                break;
            }

            if (elementEnd > end)
            {
                return null; // the selection cuts an element in half
            }

            // Between elements: text stays, comments / CDATA / <?…?> are dropped, another tag blocks.
            StringBuilder gap = new();
            pos = elementEnd;
            while (true)
            {
                TagLister.TagInfo next = lister.At(lister.FindFirstTagOnOrAfter(pos));
                if (next.Len <= 0 || next.Pos >= end)
                {
                    return null; // the selection ends with text, not an element
                }

                gap.Append(text, pos, next.Pos - pos);
                pos = next.Pos;
                if (next.Kind is not (TagKind.Comment or TagKind.CData or TagKind.ProcessingInstruction))
                {
                    break;
                }

                if (next.Pos + next.Len > end)
                {
                    return null;
                }

                removed.Add(text.Substring(next.Pos, next.Len));
                pos = next.Pos + next.Len;
            }

            gapBefore = WhitespaceRunRegex().Replace(gap.ToString(), " ");
        }

        return parts.Count >= 2 ? new Collected(name!, parts, removed, start, end) : null;
    }

    /// <summary>
    /// Expands the selection to whole sibling elements: the one containing the start and the one
    /// containing the end (from the opening tag's <c>&lt;</c> to the closing tag's <c>&gt;</c>).
    /// Both must be direct children of the deepest element enclosing the whole selection;
    /// an edge in text outside them (or both edges in one element) = <c>null</c>.
    /// </summary>
    private static (int Start, int End)? ExpandToSiblings(TagLister lister, int start, int end)
    {
        // Element ranges in document order (parent before child) — closings from OpenPos.
        var closeEnds = new Dictionary<int, int>();
        foreach (TagLister.TagInfo tag in lister.Tags)
        {
            if (tag.Kind == TagKind.End && tag.OpenPos >= 0)
            {
                closeEnds[tag.OpenPos] = tag.Pos + tag.Len;
            }
        }

        var startChain = new List<(int Pos, int End)>();
        var endChain = new List<(int Pos, int End)>();
        foreach (TagLister.TagInfo tag in lister.Tags)
        {
            if (tag.Len <= 0 || tag.Pos >= end)
            {
                continue;
            }

            int elementEnd;
            if (tag.Kind == TagKind.SelfClosing)
            {
                elementEnd = tag.Pos + tag.Len;
            }
            else if (tag.Kind != TagKind.Begin || !closeEnds.TryGetValue(tag.Pos, out elementEnd))
            {
                continue;
            }

            if (tag.Pos <= start && start < elementEnd)
            {
                startChain.Add((tag.Pos, elementEnd));
            }

            if (tag.Pos < end && end <= elementEnd)
            {
                endChain.Add((tag.Pos, elementEnd));
            }
        }

        int common = 0;
        while (common < startChain.Count && common < endChain.Count && startChain[common] == endChain[common])
        {
            common++;
        }

        if (common == startChain.Count || common == endChain.Count)
        {
            return null;
        }

        return (startChain[common].Pos, endChain[common].End);
    }

    private static Dictionary<string, string> ParseAttributes(string openTag)
    {
        var attributes = new Dictionary<string, string>(StringComparer.Ordinal);
        Match nameMatch = TagNameRegex().Match(openTag);
        int from = nameMatch.Success ? nameMatch.Index + nameMatch.Length : 0;
        foreach (Match m in AttributeRegex().Matches(openTag, from))
        {
            attributes[m.Groups["name"].Value] = m.Groups["value"].Success ? m.Groups["value"].Value : string.Empty;
        }

        return attributes;
    }

    private static bool SameAttributes(Dictionary<string, string> a, Dictionary<string, string> b)
    {
        if (a.Count != b.Count)
        {
            return false;
        }

        foreach ((string key, string value) in a)
        {
            if (!b.TryGetValue(key, out string? other))
            {
                return false;
            }

            bool same = string.Equals(key, "class", StringComparison.Ordinal)
                ? ClassSet(value).SetEquals(ClassSet(other))
                : string.Equals(value, other, StringComparison.Ordinal);
            if (!same)
            {
                return false;
            }
        }

        return true;
    }

    private static HashSet<string> ClassSet(string value) =>
        new(value.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries), StringComparer.Ordinal);

    [GeneratedRegex(@"^<\s*([^\s/>]+)")]
    private static partial Regex TagNameRegex();

    [GeneratedRegex(@"(?<name>[^\s=/>""']+)(?:\s*=\s*(?:""(?<value>[^""]*)""|'(?<value>[^']*)'|(?<value>[^\s>]+)))?")]
    private static partial Regex AttributeRegex();

    [GeneratedRegex(@"\s*/\s*>$")]
    private static partial Regex SelfClosingEndRegex();

    [GeneratedRegex(@"\s+")]
    private static partial Regex WhitespaceRunRegex();
}
