using System;
using System.Collections.Generic;
using System.Linq;
using Signet.Core.BookManipulation;
using Signet.Core.Localization;

namespace Signet.Core.MainUI;

/// <summary>The <c>&lt;span&gt;</c> whose opening or closing tag is under the caret (<see cref="SpanUnwrapper.Find"/>).</summary>
/// <param name="OpenPos">The offset of the opening tag.</param>
/// <param name="ClassValue">The value of its <c>class</c> attribute as written, or <c>null</c> without one.</param>
/// <param name="HasId">Whether it has an <c>id</c> (such spans are never removed — they can be a link target).</param>
/// <param name="SameCount">
/// How many spans in the file are the same as this one (itself included): the same attributes and values, the
/// classes compared as a set, <c>style</c> identical (<see cref="ElementAttributes"/>), and no <c>id</c>.
/// </param>
public sealed record SpanAtCaret(int OpenPos, string? ClassValue, bool HasId, int SameCount);

/// <summary>
/// "Remove span" (Code View context menu): removes the tags of a <c>&lt;span&gt;</c> and keeps its content —
/// the opposite of wrapping text in a tag. Either the span under the caret, or every span of the file that is the
/// same as it (<see cref="SpanAtCaret.SameCount"/>). Works on the source text (<see cref="TagLister"/>).
/// </summary>
public static class SpanUnwrapper
{
    /// <summary>
    /// The span whose opening or closing tag contains <paramref name="offset"/> (the <c>&lt;</c> and <c>&gt;</c>
    /// included — a click right after the <c>&gt;</c> still counts), or <c>null</c>.
    /// </summary>
    public static SpanAtCaret? Find(string text, int offset)
    {
        ArgumentNullException.ThrowIfNull(text);
        TagLister lister = new(text);
        if (OpenTagIndexAt(lister, offset) is not { } open)
        {
            return null;
        }

        TagLister.TagInfo tag = lister.At(open);
        string openTag = text.Substring(tag.Pos, tag.Len);
        TagLister.AttInfo classAttribute = TagLister.ParseAttribute(openTag, "class");
        bool hasId = TagLister.ParseAttribute(openTag, "id").Pos >= 0;
        int same = hasId ? 0 : SameSpans(lister, ElementAttributes.Parse(openTag)).Count;
        return new SpanAtCaret(tag.Pos, classAttribute.Pos >= 0 ? classAttribute.AValue : null, hasId, same);
    }

    /// <summary>
    /// Removes the tags of the span under <paramref name="offset"/> or — with <paramref name="all"/> — of every span
    /// of the file that is the same as it; the content stays. The caret stays on the same text.
    /// </summary>
    public static FormatEdit Unwrap(string text, int offset, bool all, int caret)
    {
        ArgumentNullException.ThrowIfNull(text);
        TagLister lister = new(text);
        if (OpenTagIndexAt(lister, offset) is not { } open
            || TagLister.ParseAttribute(text.Substring(lister.At(open).Pos, lister.At(open).Len), "id").Pos >= 0)
        {
            return FormatEdit.Unchanged(text, caret, caret);
        }

        List<int> spans = all ? SameSpans(lister, ElementAttributes.Parse(text.Substring(lister.At(open).Pos, lister.At(open).Len))) : new List<int> { open };
        List<(int Pos, int Len)> ranges = spans
            .SelectMany(i => new[] { lister.At(i), lister.At(lister.FindCloseTagForOpen(i)) })
            .Select(t => (t.Pos, t.Len))
            .OrderByDescending(r => r.Pos)
            .ToList();

        string result = text;
        int newCaret = caret;
        foreach ((int pos, int len) in ranges)
        {
            result = result.Remove(pos, len);
            if (newCaret >= pos + len)
            {
                newCaret -= len;
            }
            else if (newCaret > pos)
            {
                newCaret = pos;
            }
        }

        return new FormatEdit(result, newCaret, newCaret, true, CoreStrings.Format("Status_SpansRemoved", spans.Count));
    }

    /// <summary>The opening tag (text) of the span under <paramref name="offset"/> (as in <see cref="Find"/>), or <c>null</c>.</summary>
    public static string? OpenTagAt(string text, int offset)
    {
        ArgumentNullException.ThrowIfNull(text);
        TagLister lister = new(text);
        return OpenTagIndexAt(lister, offset) is { } open ? text.Substring(lister.At(open).Pos, lister.At(open).Len) : null;
    }

    /// <summary>
    /// The spans to remove for the span under <paramref name="offset"/>: just it, or — with <paramref name="all"/> —
    /// every span of the file that is the same as it. Empty when there is no span there or it has an <c>id</c>.
    /// </summary>
    public static IReadOnlyList<SpanTags> SpansAt(string text, int offset, bool all)
    {
        ArgumentNullException.ThrowIfNull(text);
        TagLister lister = new(text);
        if (OpenTagIndexAt(lister, offset) is not { } open)
        {
            return Array.Empty<SpanTags>();
        }

        string openTag = text.Substring(lister.At(open).Pos, lister.At(open).Len);
        if (TagLister.ParseAttribute(openTag, "id").Pos >= 0)
        {
            return Array.Empty<SpanTags>();
        }

        return (all ? SameSpans(lister, ElementAttributes.Parse(openTag)) : new List<int> { open })
            .Select(i => ToSpanTags(lister, i))
            .ToList();
    }

    /// <summary>
    /// The spans of <paramref name="text"/> (any file) that are the same as the span with the opening tag
    /// <paramref name="openTag"/> and have no <c>id</c>.
    /// </summary>
    public static IReadOnlyList<SpanTags> SameAs(string text, string openTag)
    {
        ArgumentNullException.ThrowIfNull(text);
        ArgumentNullException.ThrowIfNull(openTag);
        TagLister lister = new(text);
        return SameSpans(lister, ElementAttributes.Parse(openTag)).Select(i => ToSpanTags(lister, i)).ToList();
    }

    private static SpanTags ToSpanTags(TagLister lister, int open)
    {
        TagLister.TagInfo openTag = lister.At(open);
        TagLister.TagInfo closeTag = lister.At(lister.FindCloseTagForOpen(open));
        return new SpanTags(openTag.Pos, openTag.Len, closeTag.Pos, closeTag.Len, lister.Source[(openTag.Pos + openTag.Len)..closeTag.Pos]);
    }

    // The index of the opening tag of the span whose opening or closing tag contains the offset.
    private static int? OpenTagIndexAt(TagLister lister, int offset)
    {
        IReadOnlyList<TagLister.TagInfo> tags = lister.Tags;
        for (int i = 0; i < tags.Count; i++)
        {
            TagLister.TagInfo tag = tags[i];
            if (tag.Pos < 0 || offset < tag.Pos || offset > tag.Pos + tag.Len
                || !string.Equals(tag.TagName, "span", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            if (tag.Kind == TagKind.Begin && lister.FindCloseTagForOpen(i) >= 0)
            {
                return i;
            }

            if (tag.Kind == TagKind.End && lister.FindOpenTagForClose(i) is var open and >= 0)
            {
                return open;
            }
        }

        return null;
    }

    // The opening tags of the spans with these attributes and no id.
    private static List<int> SameSpans(TagLister lister, Dictionary<string, string> attributes)
    {
        string text = lister.Source;
        List<int> same = new();
        IReadOnlyList<TagLister.TagInfo> tags = lister.Tags;
        for (int i = 0; i < tags.Count; i++)
        {
            TagLister.TagInfo tag = tags[i];
            if (tag.Kind != TagKind.Begin || !string.Equals(tag.TagName, "span", StringComparison.OrdinalIgnoreCase)
                || lister.FindCloseTagForOpen(i) < 0)
            {
                continue;
            }

            Dictionary<string, string> other = ElementAttributes.Parse(text.Substring(tag.Pos, tag.Len));
            if (!other.ContainsKey("id") && ElementAttributes.Same(attributes, other))
            {
                same.Add(i);
            }
        }

        return same;
    }
}
