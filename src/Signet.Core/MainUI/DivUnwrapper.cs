using System;
using System.Collections.Generic;
using System.Linq;
using Signet.Core.Localization;

namespace Signet.Core.MainUI;

/// <summary>Why "Unwrap div" cannot be used at a given place.</summary>
public enum DivUnwrapProblem
{
    /// <summary>It can be used.</summary>
    None,

    /// <summary>The caret is not inside any <c>&lt;div&gt;</c>.</summary>
    NotInDiv,

    /// <summary>The caret is in a <c>&lt;div&gt;</c> tag that has no matching closing tag.</summary>
    NoClosingTag,
}

/// <summary>The <c>&lt;div&gt;</c> that "Unwrap div" would remove.</summary>
/// <param name="OpenPos">The offset of its opening tag.</param>
/// <param name="Id">The value of its <c>id</c> attribute, or <c>null</c> without one.</param>
/// <param name="ClassValue">The value of its <c>class</c> attribute as written, or <c>null</c> without one.</param>
public sealed record DivAtCaret(int OpenPos, string? Id, string? ClassValue);

/// <summary>The outcome of <see cref="DivUnwrapper.Check"/>: the div, or the reason there is none.</summary>
public readonly record struct DivUnwrapCheck(DivAtCaret? Div, DivUnwrapProblem Problem);

/// <summary>
/// "Unwrap div" (Code Editor): removes the opening and the closing tag of the innermost <c>&lt;div&gt;</c> around
/// the caret and keeps its content one level up. When the tags stand on their own lines, those lines go away
/// and the content is shifted back by one level of indentation.
/// </summary>
public static class DivUnwrapper
{
    /// <summary>
    /// The innermost <c>&lt;div&gt;</c> with a closing tag whose text — from the <c>&lt;</c> of the opening tag
    /// to the <c>&gt;</c> of the closing one — contains <paramref name="offset"/>.
    /// </summary>
    public static DivUnwrapCheck Check(string text, int offset)
    {
        ArgumentNullException.ThrowIfNull(text);
        TagLister lister = TagLister.For(text);
        if (FindDiv(lister, offset) is { } found)
        {
            string openTag = text.Substring(found.Open.Pos, found.Open.Len);
            TagLister.AttInfo id = TagLister.ParseAttribute(openTag, "id");
            TagLister.AttInfo cls = TagLister.ParseAttribute(openTag, "class");
            return new DivUnwrapCheck(
                new DivAtCaret(found.Open.Pos, id.Pos >= 0 ? id.AValue : null, cls.Pos >= 0 ? cls.AValue : null),
                DivUnwrapProblem.None);
        }

        IReadOnlyList<TagLister.TagInfo> tags = lister.Tags;
        for (int i = 0; i < tags.Count; i++)
        {
            TagLister.TagInfo tag = tags[i];
            if (tag.Pos >= 0 && offset >= tag.Pos && offset <= tag.Pos + tag.Len
                && string.Equals(tag.TagName, "div", StringComparison.OrdinalIgnoreCase)
                && tag.Kind is TagKind.Begin or TagKind.SelfClosing or TagKind.End)
            {
                return new DivUnwrapCheck(null, DivUnwrapProblem.NoClosingTag);
            }
        }

        return new DivUnwrapCheck(null, DivUnwrapProblem.NotInDiv);
    }

    /// <summary>
    /// Removes the tags of the div found by <see cref="Check"/>; the content stays (only its indentation changes).
    /// The caret stays on the same text.
    /// </summary>
    public static FormatEdit Unwrap(string text, int offset, int caret)
    {
        ArgumentNullException.ThrowIfNull(text);
        TagLister lister = TagLister.For(text);
        if (FindDiv(lister, offset) is not { } found)
        {
            return FormatEdit.Unchanged(text, caret, caret);
        }

        TagLister.TagInfo open = found.Open;
        TagLister.TagInfo close = found.Close;
        (int Start, int End, bool Alone) openRange = TagRange(text, open);
        (int Start, int End, bool Alone) closeRange = TagRange(text, close);

        List<(int Pos, int Len)> removals = new()
        {
            (openRange.Start, openRange.End - openRange.Start),
            (closeRange.Start, closeRange.End - closeRange.Start),
        };

        if (openRange.Alone && closeRange.Alone)
        {
            string parentIndent = text[openRange.Start..open.Pos];
            removals.AddRange(Outdent(lister, openRange.End, closeRange.Start, parentIndent));
        }

        string result = text;
        int newCaret = caret;
        foreach ((int pos, int len) in removals.OrderByDescending(r => r.Pos))
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

        return new FormatEdit(result, newCaret, newCaret, true, CoreStrings.Format("Status_DivUnwrapped"));
    }

    private readonly record struct DivTags(TagLister.TagInfo Open, TagLister.TagInfo Close);

    // The innermost paired div whose tags (the opening one to the closing one, both included) contain the offset.
    private static DivTags? FindDiv(TagLister lister, int offset)
    {
        DivTags? best = null;
        IReadOnlyList<TagLister.TagInfo> tags = lister.Tags;
        for (int i = 0; i < tags.Count; i++)
        {
            TagLister.TagInfo tag = tags[i];
            if (tag.Pos < 0 || tag.Kind != TagKind.Begin || offset < tag.Pos
                || !string.Equals(tag.TagName, "div", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            int closeIndex = lister.FindCloseTagForOpen(i);
            if (closeIndex < 0)
            {
                continue;
            }

            TagLister.TagInfo close = lister.At(closeIndex);
            if (offset <= close.Pos + close.Len && (best is null || tag.Pos > best.Value.Open.Pos))
            {
                best = new DivTags(tag, close);
            }
        }

        return best;
    }

    // The range to cut for a tag: the whole line (with its line break) when only whitespace shares the line, else the tag.
    private static (int Start, int End, bool Alone) TagRange(string text, TagLister.TagInfo tag)
    {
        int lineStart = text.LastIndexOf('\n', Math.Max(tag.Pos - 1, 0)) + 1;
        if (tag.Pos == 0)
        {
            lineStart = 0;
        }

        int tagEnd = tag.Pos + tag.Len;
        int newline = text.IndexOf('\n', tagEnd);
        int lineEnd = newline < 0 ? text.Length : newline;
        if (IsBlank(text, lineStart, tag.Pos) && IsBlank(text, tagEnd, lineEnd))
        {
            return (lineStart, newline < 0 ? text.Length : newline + 1, true);
        }

        return (tag.Pos, tagEnd, false);
    }

    private static bool IsBlank(string text, int start, int end)
    {
        for (int i = start; i < end; i++)
        {
            if (text[i] is not (' ' or '\t' or '\r'))
            {
                return false;
            }
        }

        return true;
    }

    // The indentation to take away from each content line: the one level the content is indented deeper than the div.
    private static List<(int Pos, int Len)> Outdent(TagLister lister, int contentStart, int contentEnd, string parentIndent)
    {
        string text = lister.Source;
        List<(int Start, int End)> verbatim = VerbatimRanges(lister, contentStart, contentEnd);
        List<(int LineStart, int IndentLength)> lines = new();
        string? common = null;
        for (int ls = contentStart; ls < contentEnd;)
        {
            int newline = text.IndexOf('\n', ls);
            int lineEnd = newline < 0 || newline >= contentEnd ? contentEnd : newline;
            int ws = ls;
            while (ws < lineEnd && text[ws] is ' ' or '\t')
            {
                ws++;
            }

            bool blank = IsBlank(text, ws, lineEnd);
            if (!blank && !verbatim.Any(v => ls >= v.Start && ls < v.End))
            {
                string indent = text[ls..ws];
                common = common is null ? indent : CommonPrefix(common, indent);
                lines.Add((ls, indent.Length));
            }

            ls = lineEnd + 1;
        }

        List<(int Pos, int Len)> removals = new();
        if (common is null || common.Length <= parentIndent.Length || !common.StartsWith(parentIndent, StringComparison.Ordinal))
        {
            return removals;
        }

        int delta = common.Length - parentIndent.Length;
        foreach ((int lineStart, _) in lines)
        {
            removals.Add((lineStart + parentIndent.Length, delta));
        }

        return removals;
    }

    // The text of <pre> and <textarea> in the range: its whitespace is content, so it is never re-indented.
    private static List<(int Start, int End)> VerbatimRanges(TagLister lister, int from, int to)
    {
        List<(int Start, int End)> ranges = new();
        IReadOnlyList<TagLister.TagInfo> tags = lister.Tags;
        for (int i = 0; i < tags.Count; i++)
        {
            TagLister.TagInfo tag = tags[i];
            if (tag.Pos < from || tag.Pos >= to || tag.Kind != TagKind.Begin
                || !(string.Equals(tag.TagName, "pre", StringComparison.OrdinalIgnoreCase)
                    || string.Equals(tag.TagName, "textarea", StringComparison.OrdinalIgnoreCase)))
            {
                continue;
            }

            int closeIndex = lister.FindCloseTagForOpen(i);
            if (closeIndex >= 0)
            {
                ranges.Add((tag.Pos + tag.Len, lister.At(closeIndex).Pos));
            }
        }

        return ranges;
    }

    private static string CommonPrefix(string a, string b)
    {
        int n = 0;
        while (n < a.Length && n < b.Length && a[n] == b[n])
        {
            n++;
        }

        return a[..n];
    }
}
