using System;
using System.Collections.Generic;

namespace Signet.Core.BookManipulation;

/// <summary>What is wrong with a comment (<see cref="HtmlCommentProblem.Kind"/>).</summary>
public enum HtmlCommentProblemKind
{
    /// <summary>A comment inside a comment (<c>&lt;!--</c> before the <c>--&gt;</c> that ends the first one).</summary>
    Nested,

    /// <summary>The text of the comment contains <c>--</c>, which XML forbids.</summary>
    DoubleHyphen,

    /// <summary>The comment ends with <c>-</c> before the <c>--&gt;</c> (<c>--- &gt;</c>), which XML forbids.</summary>
    EndsWithHyphen,

    /// <summary>The comment is never closed (no <c>--&gt;</c> after it).</summary>
    Unterminated,
}

/// <summary>A comment that XML does not allow, where it is in the text.</summary>
/// <param name="Kind">What is wrong.</param>
/// <param name="Offset">Where the problem is in the text (0-based): the nested <c>&lt;!--</c>, the first <c>--</c>, the last <c>-</c>, or the start of the comment.</param>
/// <param name="Line">The line of <paramref name="Offset"/> (1-based).</param>
/// <param name="Column">The column of <paramref name="Offset"/> (1-based).</param>
/// <param name="StartLine">The line the comment starts on (1-based).</param>
/// <param name="EndLine">The line the comment ends on (1-based; the last line of the text for an unterminated one).</param>
public sealed record HtmlCommentProblem(
    HtmlCommentProblemKind Kind, int Offset, int Line, int Column, int StartLine, int EndLine);

/// <summary>
/// Finds the comments of an (X)HTML text that XML does not allow: a comment inside a comment, <c>--</c> in the text of a
/// comment, a comment that ends with <c>-</c>, and one that is never closed. The XML parser stops at the first error of a
/// file and words it in its own way; this lists every such comment with its position, so that each can be found and fixed.
/// </summary>
/// <remarks>
/// A comment ends at the first <c>--&gt;</c>. CDATA sections are skipped: what is in them is not a comment. One problem is
/// reported per comment, the first of: nested, <c>--</c>, ends with <c>-</c>. A comment that holds a closing tag and nothing
/// forbidden (<c>&lt;!-- &lt;/aside --&gt;</c>) is legal XML and is not reported.
/// </remarks>
public static class HtmlCommentChecker
{
    private const string Open = "<!--";
    private const string Close = "-->";
    private const string CdataOpen = "<![CDATA[";
    private const string CdataClose = "]]>";

    /// <summary>The problems of the comments of <paramref name="text"/>, in the order of the text.</summary>
    public static IReadOnlyList<HtmlCommentProblem> Find(string text)
    {
        ArgumentNullException.ThrowIfNull(text);
        List<HtmlCommentProblem> problems = new();
        if (!text.Contains(Open, StringComparison.Ordinal))
        {
            return problems;
        }

        LineIndex lines = new(text);
        int i = 0;
        while (i < text.Length)
        {
            int lt = text.IndexOf('<', i);
            if (lt < 0)
            {
                break;
            }

            if (string.CompareOrdinal(text, lt, CdataOpen, 0, CdataOpen.Length) == 0)
            {
                int cdataEnd = text.IndexOf(CdataClose, lt + CdataOpen.Length, StringComparison.Ordinal);
                i = cdataEnd < 0 ? text.Length : cdataEnd + CdataClose.Length;
                continue;
            }

            if (string.CompareOrdinal(text, lt, Open, 0, Open.Length) != 0)
            {
                i = lt + 1;
                continue;
            }

            int bodyStart = lt + Open.Length;
            int end = text.IndexOf(Close, bodyStart, StringComparison.Ordinal);
            if (end < 0)
            {
                problems.Add(Problem(lines, HtmlCommentProblemKind.Unterminated, lt, lt, text.Length));
                break;
            }

            // The "--" of "-->" is not part of the text; a hyphen right before it ("--->") is.
            int nested = text.IndexOf(Open, bodyStart, end - bodyStart, StringComparison.Ordinal);
            int hyphens = text.IndexOf("--", bodyStart, end - bodyStart, StringComparison.Ordinal);
            if (nested >= 0)
            {
                problems.Add(Problem(lines, HtmlCommentProblemKind.Nested, lt, nested, end + Close.Length));
            }
            else if (hyphens >= 0)
            {
                problems.Add(Problem(lines, HtmlCommentProblemKind.DoubleHyphen, lt, hyphens, end + Close.Length));
            }
            else if (end > bodyStart && text[end - 1] == '-')
            {
                problems.Add(Problem(lines, HtmlCommentProblemKind.EndsWithHyphen, lt, end - 1, end + Close.Length));
            }

            i = end + Close.Length;
        }

        return problems;
    }

    private static HtmlCommentProblem Problem(LineIndex lines, HtmlCommentProblemKind kind, int start, int at, int end)
    {
        (int line, int column) = lines.Position(at);
        return new HtmlCommentProblem(kind, at, line, column, lines.Position(start).Line, lines.Position(Math.Max(start, end - 1)).Line);
    }

    // The offsets of the starts of the lines, to turn an offset into a line and a column.
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

        public (int Line, int Column) Position(int offset)
        {
            int index = _starts.BinarySearch(offset);
            if (index < 0)
            {
                index = ~index - 1;
            }

            return (index + 1, offset - _starts[index] + 1);
        }
    }
}
