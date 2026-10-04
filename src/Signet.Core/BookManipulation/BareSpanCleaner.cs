using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;

namespace Signet.Core.BookManipulation;

/// <summary>The tags of a <c>&lt;span&gt;</c> in the source text (<see cref="BareSpanCleaner"/>, <see cref="SpanRemoval"/>).</summary>
/// <param name="OpenPos">The offset of the opening tag.</param>
/// <param name="OpenLen">Its length.</param>
/// <param name="ClosePos">The offset of the matching <c>&lt;/span&gt;</c>.</param>
/// <param name="CloseLen">Its length.</param>
/// <param name="Content">The text between the tags (for the report).</param>
public sealed record SpanTags(int OpenPos, int OpenLen, int ClosePos, int CloseLen, string Content);

/// <summary>
/// "Remove &lt;span&gt; tags without attributes": a bare <c>&lt;span&gt;</c> has no style of its own, so its tags can
/// go and its content stays (the opposite of wrapping). Works on the source text (<see cref="TagLister"/>); whether
/// the stylesheets still reach a bare span (<c>span { }</c>, <c>p &gt; em</c>, <c>:first-child</c>…) is decided by
/// <see cref="SpanRemovalRiskAnalyzer"/>.
/// </summary>
internal static class BareSpanCleaner
{
    private static readonly Regex BareOpenTag = new(@"^<span\s*>$", RegexOptions.IgnoreCase | RegexOptions.Compiled);

    /// <summary>All bare spans of the <c>&lt;body&gt;</c> in <paramref name="text"/>, in document order.</summary>
    public static IReadOnlyList<SpanTags> Find(string text)
    {
        ArgumentNullException.ThrowIfNull(text);
        TagLister lister = new(text);
        IReadOnlyList<TagLister.TagInfo> tags = lister.Tags;
        List<SpanTags> found = new();
        for (int i = 0; i < tags.Count; i++)
        {
            TagLister.TagInfo tag = tags[i];
            if (tag.Kind != TagKind.Begin || !lister.IsPositionInBody(tag.Pos)
                || !BareOpenTag.IsMatch(text.AsSpan(tag.Pos, tag.Len)))
            {
                continue;
            }

            int close = lister.FindCloseTagForOpen(i);
            if (close < 0)
            {
                continue;
            }

            TagLister.TagInfo closeTag = lister.At(close);
            found.Add(new SpanTags(tag.Pos, tag.Len, closeTag.Pos, closeTag.Len, text[(tag.Pos + tag.Len)..closeTag.Pos]));
        }

        return found;
    }

    /// <summary>Removes the tags of <paramref name="spans"/> (found in <paramref name="text"/>); their content stays.</summary>
    public static string RemoveTags(string text, IEnumerable<SpanTags> spans)
    {
        ArgumentNullException.ThrowIfNull(text);
        ArgumentNullException.ThrowIfNull(spans);
        foreach ((int pos, int len) in spans
                     .SelectMany(s => new[] { (s.OpenPos, s.OpenLen), (s.ClosePos, s.CloseLen) })
                     .OrderByDescending(r => r.Item1))
        {
            text = text.Remove(pos, len);
        }

        return text;
    }

    /// <summary>
    /// Removes the tags of the bare spans of <paramref name="text"/> that start at <paramref name="positions"/>
    /// (<see cref="SpanTags.OpenPos"/> values found in the same text); their content stays.
    /// </summary>
    public static string Remove(string text, IEnumerable<int> positions)
    {
        ArgumentNullException.ThrowIfNull(text);
        ArgumentNullException.ThrowIfNull(positions);
        HashSet<int> wanted = positions.ToHashSet();
        foreach ((int pos, int len) in Find(text)
                     .Where(s => wanted.Contains(s.OpenPos))
                     .SelectMany(s => new[] { (s.OpenPos, s.OpenLen), (s.ClosePos, s.CloseLen) })
                     .OrderByDescending(r => r.Item1))
        {
            text = text.Remove(pos, len);
        }

        return text;
    }
}
