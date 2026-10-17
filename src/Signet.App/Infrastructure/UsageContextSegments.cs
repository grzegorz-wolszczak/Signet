using System;
using System.Collections.Generic;
using Signet.Core.BookManipulation;
using Signet.Core.Misc;

namespace Signet.App.Infrastructure;

/// <summary>A piece of the context of a usage that looks the same all through.</summary>
/// <param name="Text">The text.</param>
/// <param name="Format">How the syntax highlighter formats it; <c>null</c> = plain text.</param>
/// <param name="IsMatch">Whether it is (a part of) what was searched for.</param>
public readonly record struct UsageContextSegment(string Text, SyntaxFormat? Format, bool IsMatch);

/// <summary>
/// Cuts the context of a usage (the line of the "Find Usages" panel's "Context" column) into pieces by syntax and by the
/// searched text, so the panel can color the line the way Code Editor does and mark the match as Find does.
/// </summary>
public static class UsageContextSegments
{
    /// <summary>The kind of file a context comes from, which decides the highlighter.</summary>
    private enum Syntax
    {
        None,
        Markup,
        Css,
    }

    /// <summary>The pieces of <see cref="ClassUsage.Context"/>, in text order; empty for an empty context.</summary>
    public static IReadOnlyList<UsageContextSegment> Of(ClassUsage usage)
    {
        ArgumentNullException.ThrowIfNull(usage);
        string text = usage.Context;
        if (text.Length == 0)
        {
            return Array.Empty<UsageContextSegment>();
        }

        // The highlighters work on a line and know nothing before it: a context cut out of the middle of a tag may start in
        // the wrong state — the colors are a help to the eye, not a parse.
        SyntaxFormat?[] formats = new SyntaxFormat?[text.Length];
        List<SyntaxSpan> spans = new();
        switch (SyntaxOf(usage))
        {
            case Syntax.Markup:
                new XhtmlHighlighter().HighlightLine(text, 0, spans);
                break;
            case Syntax.Css:
                new CssHighlighter().HighlightLine(text, 0, spans);
                break;
        }

        // In order: the later span wins, as in the editor.
        foreach (SyntaxSpan span in spans)
        {
            int end = Math.Min(text.Length, span.Start + span.Length);
            for (int i = Math.Max(0, span.Start); i < end; i++)
            {
                formats[i] = span.Format;
            }
        }

        int matchStart = usage.ContextMatchStart;
        int matchEnd = matchStart < 0 ? -1 : Math.Min(text.Length, matchStart + usage.ContextMatchLength);
        bool IsMatch(int index) => index >= matchStart && index < matchEnd;

        List<UsageContextSegment> segments = new();
        int from = 0;
        for (int i = 1; i <= text.Length; i++)
        {
            if (i < text.Length && formats[i] == formats[from] && IsMatch(i) == IsMatch(from))
            {
                continue;
            }

            segments.Add(new UsageContextSegment(text[from..i], formats[from], IsMatch(from)));
            from = i;
        }

        return segments;
    }

    // A stylesheet, a rule of a <style> block (the usage is a selector), or a markup file; other files are plain text.
    private static Syntax SyntaxOf(ClassUsage usage)
    {
        string path = usage.BookPath;
        if (usage.Kind == ClassUsageKind.Selector || path.EndsWith(".css", StringComparison.OrdinalIgnoreCase))
        {
            return Syntax.Css;
        }

        string[] markup = [".xhtml", ".html", ".htm", ".xml", ".svg", ".opf", ".ncx", ".smil"];
        foreach (string extension in markup)
        {
            if (path.EndsWith(extension, StringComparison.OrdinalIgnoreCase))
            {
                return Syntax.Markup;
            }
        }

        return Syntax.None;
    }
}
