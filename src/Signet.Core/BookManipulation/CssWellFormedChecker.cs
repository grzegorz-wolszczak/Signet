using System;
using Signet.Core.Parsers;

namespace Signet.Core.BookManipulation;

/// <summary>
/// Structural validity checking of a CSS stylesheet — the "well-formed" equivalent for styles.
/// CSS has no well-formedness concept like XML, so only the structure is checked: unclosed
/// <c>{ }</c> blocks, strings and comments, and excess <c>}</c>. Detection is done
/// by the CSS structure scanner (<see cref="CssInfo.ParseErrors"/>).
/// </summary>
/// <remarks>
/// The error position is computed from the source after normalizing <c>\r\n</c> → <c>\n</c> (the way
/// <see cref="CssInfo"/> works) — the line number is accurate, while the column may differ by the number of
/// removed <c>\r</c> characters on that line. When an error has no position
/// (<see cref="CssInfo.ParseErrorPositions"/> = <c>null</c>, e.g. an unclosed block at the end of the input),
/// the end of the document is reported.
/// </remarks>
public static class CssWellFormedChecker
{
    /// <summary>
    /// Checks the structure of <paramref name="text"/>. Returns <see cref="WellFormedResult.Ok"/> when the
    /// scanner reported no error; otherwise a <see cref="WellFormedResult"/> with the position
    /// of the first problem and its message.
    /// </summary>
    public static WellFormedResult Check(string text)
    {
        ArgumentNullException.ThrowIfNull(text);

        CssInfo info = new(text);
        if (info.ParseErrors.Count == 0)
        {
            return WellFormedResult.Ok;
        }

        string message = info.ParseErrors[0];
        string normalized = text.Replace("\r\n", "\n", StringComparison.Ordinal);
        int position = info.ParseErrorPositions[0] ?? normalized.Length;
        (int line, int column) = OffsetToLineColumn(normalized, position);
        return new WellFormedResult(false, line, column, message);
    }

    private static (int Line, int Column) OffsetToLineColumn(string text, int offset)
    {
        int clamped = Math.Clamp(offset, 0, text.Length);
        int line = 1;
        int lineStart = 0;
        for (int i = 0; i < clamped; i++)
        {
            if (text[i] == '\n')
            {
                line++;
                lineStart = i + 1;
            }
        }

        return (line, clamped - lineStart + 1);
    }
}
