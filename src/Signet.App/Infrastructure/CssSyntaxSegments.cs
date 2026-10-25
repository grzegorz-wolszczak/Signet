using System;
using System.Collections.Generic;
using Signet.Core.Misc;

namespace Signet.App.Infrastructure;

/// <summary>
/// Cuts a piece of CSS shown outside Code Editor (the declarations and selectors of the Live CSS panel) into pieces by
/// syntax, so that it can be colored the way the editor colors it. The same highlighter
/// (<see cref="CssHighlighter"/>) and therefore the same colors of the color scheme.
/// </summary>
public static class CssSyntaxSegments
{
    // Enough of a rule to put the highlighter in the state the text is really in: a selector on its own
    // ("a:hover") looks like a declaration to a highlighter that starts a line with no context.
    private const string SelectorTail = " {";

    /// <summary>
    /// The pieces of <paramref name="text"/>, in text order; empty for empty text.
    /// <paramref name="isSelector"/> says the text is a selector, not a declaration.
    /// </summary>
    public static IReadOnlyList<UsageContextSegment> Of(string text, bool isSelector = false)
    {
        ArgumentNullException.ThrowIfNull(text);
        if (text.Length == 0)
        {
            return Array.Empty<UsageContextSegment>();
        }

        string line = isSelector ? text + SelectorTail : text;
        List<SyntaxSpan> spans = new();
        new CssHighlighter().HighlightLine(line, new CssHighlighter().InitialState, spans);

        SyntaxFormat?[] formats = new SyntaxFormat?[text.Length];
        foreach (SyntaxSpan span in spans)
        {
            int end = Math.Min(text.Length, span.Start + span.Length);
            for (int i = Math.Max(0, span.Start); i < end; i++)
            {
                formats[i] = span.Format;
            }
        }

        List<UsageContextSegment> segments = new();
        int from = 0;
        for (int i = 1; i <= text.Length; i++)
        {
            if (i < text.Length && formats[i] == formats[from])
            {
                continue;
            }

            segments.Add(new UsageContextSegment(text[from..i], formats[from], false));
            from = i;
        }

        return segments;
    }
}
