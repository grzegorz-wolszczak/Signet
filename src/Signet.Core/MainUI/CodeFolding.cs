using System;
using System.Collections.Generic;
using System.Linq;

namespace Signet.Core.MainUI;

/// <summary>A foldable region of a Code View text: what is hidden when folded, and what is shown instead.</summary>
/// <param name="StartOffset">The 0-based offset where the hidden text starts.</param>
/// <param name="EndOffset">The 0-based offset where it ends (exclusive).</param>
/// <param name="Title">The placeholder shown instead of the hidden text (clicking it unfolds the region).</param>
public sealed record FoldRegion(int StartOffset, int EndOffset, string Title);

/// <summary>
/// The foldable regions of a Code View text, in the order of their start offsets (as AvaloniaEdit's folding manager
/// needs them):
/// <list type="bullet">
/// <item>markup (XHTML, HTML, XML): every element whose closing tag is on a later line than its opening tag — folded
/// from the end of the opening tag to the end of the closing tag, so <c>&lt;div class="a"&gt;[…]</c> stays; every
/// comment spanning several lines — folded after <c>&lt;!--</c>;</item>
/// <item>CSS: every <c>{ … }</c> block spanning several lines (rules, <c>@media</c>…), the braces included, so
/// <c>p.note {…}</c> stays. Braces in comments and strings do not count.</item>
/// </list>
/// An element whose opening and closing tag are on the same line is never foldable. Works on the source text
/// (<see cref="TagLister"/>), so a document that is not well-formed folds as far as its tags pair up.
/// </summary>
public static class CodeFolding
{
    /// <summary>The placeholder of a folded element or comment.</summary>
    public const string Ellipsis = "…";

    /// <summary>The placeholder of a folded CSS block.</summary>
    public const string CssBlock = "{…}";

    private const string CommentStart = "<!--";

    /// <summary>The foldable elements and multi-line comments of a markup text.</summary>
    public static IReadOnlyList<FoldRegion> ForMarkup(string text)
    {
        ArgumentNullException.ThrowIfNull(text);
        LineIndex lines = new(text);
        TagLister lister = new(text);
        IReadOnlyList<TagLister.TagInfo> tags = lister.Tags;
        List<FoldRegion> regions = new();

        for (int i = 0; i < tags.Count && tags[i].Pos >= 0; i++)
        {
            TagLister.TagInfo tag = tags[i];
            if (tag.Kind == TagKind.Begin)
            {
                int closeIndex = lister.FindCloseTagForOpen(i);
                if (closeIndex < 0 || tags[closeIndex] is not { Pos: >= 0 } close)
                {
                    continue;
                }

                int start = tag.Pos + tag.Len;
                int end = close.Pos + close.Len;
                if (end > start && lines.LineOf(tag.Pos) != lines.LineOf(close.Pos))
                {
                    regions.Add(new FoldRegion(start, end, Ellipsis));
                }
            }
            else if (tag.Kind == TagKind.Comment && tag.Len > CommentStart.Length
                     && lines.LineOf(tag.Pos) != lines.LineOf(tag.Pos + tag.Len - 1))
            {
                regions.Add(new FoldRegion(tag.Pos + CommentStart.Length, tag.Pos + tag.Len, Ellipsis));
            }
        }

        return regions.OrderBy(r => r.StartOffset).ToList();
    }

    /// <summary>The foldable <c>{ … }</c> blocks of a stylesheet.</summary>
    public static IReadOnlyList<FoldRegion> ForCss(string text)
    {
        ArgumentNullException.ThrowIfNull(text);
        LineIndex lines = new(text);
        Stack<int> open = new();
        List<FoldRegion> regions = new();

        for (int i = 0; i < text.Length; i++)
        {
            char c = text[i];
            if (c == '/' && i + 1 < text.Length && text[i + 1] == '*')
            {
                int end = text.IndexOf("*/", i + 2, StringComparison.Ordinal);
                i = end < 0 ? text.Length : end + 1;
            }
            else if (c is '"' or '\'')
            {
                i = EndOfString(text, i);
            }
            else if (c == '{')
            {
                open.Push(i);
            }
            else if (c == '}' && open.Count > 0)
            {
                int start = open.Pop();
                if (lines.LineOf(start) != lines.LineOf(i))
                {
                    regions.Add(new FoldRegion(start, i + 1, CssBlock));
                }
            }
        }

        return regions.OrderBy(r => r.StartOffset).ToList();
    }

    // The index of the closing quote of the string starting at "start" (or the end of the line / text).
    private static int EndOfString(string text, int start)
    {
        char quote = text[start];
        for (int i = start + 1; i < text.Length; i++)
        {
            if (text[i] == '\\')
            {
                i++;
            }
            else if (text[i] == quote || text[i] == '\n')
            {
                return i;
            }
        }

        return text.Length;
    }

    // The start offset of every line, for finding the line of an offset.
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

        public int LineOf(int offset)
        {
            int index = _starts.BinarySearch(offset);
            return index >= 0 ? index : ~index - 1;
        }
    }
}
