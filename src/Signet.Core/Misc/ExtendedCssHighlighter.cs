using System;
using System.Collections.Frozen;
using System.Collections.Generic;
using System.Text.RegularExpressions;

namespace Signet.Core.Misc;

/// <summary>
/// Extended CSS highlighting: on the fragments delimited by the base state machine
/// (<see cref="CssHighlighter"/>, line state unchanged) it distinguishes <c>.class</c>/<c>#id</c>/
/// <c>:pseudo</c> selectors, <c>@</c> rules, <c>!important</c>, numbers and dimensions, <c>#hex</c> colors
/// and color names, single-quoted strings, <c>url(...)</c> targets (a link / a broken link) and unterminated
/// strings (an error).
/// </summary>
/// <remarks>
/// The refinement works only within selector/value fragments — comments, properties and strings
/// stay as the base highlighter produces them.
/// </remarks>
public sealed partial class ExtendedCssHighlighter : ILineSyntaxHighlighter
{
    private const int QuoteState = 6; // CssHighlighter.Quote

    // Named CSS colors (CSS Color Module Level 4) + transparent/currentcolor.
    private static readonly FrozenSet<string> ColorNames = FrozenSet.ToFrozenSet(
        new[]
        {
            "aliceblue", "antiquewhite", "aqua", "aquamarine", "azure", "beige", "bisque", "black",
            "blanchedalmond", "blue", "blueviolet", "brown", "burlywood", "cadetblue", "chartreuse",
            "chocolate", "coral", "cornflowerblue", "cornsilk", "crimson", "cyan", "darkblue", "darkcyan",
            "darkgoldenrod", "darkgray", "darkgreen", "darkgrey", "darkkhaki", "darkmagenta",
            "darkolivegreen", "darkorange", "darkorchid", "darkred", "darksalmon", "darkseagreen",
            "darkslateblue", "darkslategray", "darkslategrey", "darkturquoise", "darkviolet", "deeppink",
            "deepskyblue", "dimgray", "dimgrey", "dodgerblue", "firebrick", "floralwhite", "forestgreen",
            "fuchsia", "gainsboro", "ghostwhite", "gold", "goldenrod", "gray", "green", "greenyellow",
            "grey", "honeydew", "hotpink", "indianred", "indigo", "ivory", "khaki", "lavender",
            "lavenderblush", "lawngreen", "lemonchiffon", "lightblue", "lightcoral", "lightcyan",
            "lightgoldenrodyellow", "lightgray", "lightgreen", "lightgrey", "lightpink", "lightsalmon",
            "lightseagreen", "lightskyblue", "lightslategray", "lightslategrey", "lightsteelblue",
            "lightyellow", "lime", "limegreen", "linen", "magenta", "maroon", "mediumaquamarine",
            "mediumblue", "mediumorchid", "mediumpurple", "mediumseagreen", "mediumslateblue",
            "mediumspringgreen", "mediumturquoise", "mediumvioletred", "midnightblue", "mintcream",
            "mistyrose", "moccasin", "navajowhite", "navy", "oldlace", "olive", "olivedrab", "orange",
            "orangered", "orchid", "palegoldenrod", "palegreen", "paleturquoise", "palevioletred",
            "papayawhip", "peachpuff", "peru", "pink", "plum", "powderblue", "purple", "rebeccapurple",
            "red", "rosybrown", "royalblue", "saddlebrown", "salmon", "sandybrown", "seagreen", "seashell",
            "sienna", "silver", "skyblue", "slateblue", "slategray", "slategrey", "snow", "springgreen",
            "steelblue", "tan", "teal", "thistle", "tomato", "turquoise", "violet", "wheat", "white",
            "whitesmoke", "yellow", "yellowgreen", "transparent", "currentcolor",
        },
        StringComparer.OrdinalIgnoreCase);

    private readonly CssHighlighter _automaton = new();
    private readonly Func<string, bool>? _linkExists;
    private readonly List<SyntaxSpan> _baseSpans = new();

    /// <summary>Creates the highlighter.</summary>
    /// <param name="linkExists">
    /// Checks a <c>url(...)</c> target: <c>true</c> when the file is in the book (or the link is
    /// external). <c>null</c> — targets are not checked (every link is valid).
    /// </param>
    public ExtendedCssHighlighter(Func<string, bool>? linkExists = null)
    {
        _linkExists = linkExists;
    }

    /// <inheritdoc />
    public int InitialState => _automaton.InitialState;

    /// <inheritdoc />
    public int HighlightLine(string text, int previousState, List<SyntaxSpan>? spans)
    {
        ArgumentNullException.ThrowIfNull(text);
        if (spans is null)
        {
            return _automaton.HighlightLine(text, previousState, null);
        }

        _baseSpans.Clear();
        int endState = _automaton.HighlightLine(text, previousState, _baseSpans);
        spans.AddRange(_baseSpans);
        if (text.Length == 0)
        {
            return endState;
        }

        SyntaxFormat?[] formats = FormatMap(text.Length, _baseSpans);
        RefineSelectors(text, formats, spans);
        RefineValues(text, formats, spans);
        AddLinks(text, formats, spans);

        // A string left open to the end of the line (without a "\" continuation) — an error.
        if ((endState & 0xff) == QuoteState && !text.EndsWith('\\') &&
            _baseSpans.FindLast(s => s.Format == SyntaxFormat.CssQuote) is { Length: > 0 } quote)
        {
            spans.Add(new SyntaxSpan(quote.Start, quote.Length, SyntaxFormat.SyntaxError, SyntaxIssue.UnterminatedString));
        }

        return endState;
    }

    // The format of each line character per the base state machine (null — a character outside the fragments, e.g. "{", ":").
    private static SyntaxFormat?[] FormatMap(int length, List<SyntaxSpan> baseSpans)
    {
        SyntaxFormat?[] map = new SyntaxFormat?[length];
        foreach (SyntaxSpan span in baseSpans)
        {
            int end = Math.Min(span.Start + span.Length, length);
            for (int i = Math.Max(span.Start, 0); i < end; i++)
            {
                map[i] = span.Format;
            }
        }

        return map;
    }

    // The match lies within an area of the given kind: every character has that format or none does, and at least
    // one has that format.
    private static bool Within(SyntaxFormat?[] formats, int start, int length, SyntaxFormat format)
    {
        bool any = false;
        for (int i = start; i < start + length; i++)
        {
            if (formats[i] is { } f)
            {
                if (f != format)
                {
                    return false;
                }

                any = true;
            }
        }

        return any;
    }

    private static void RefineSelectors(string text, SyntaxFormat?[] formats, List<SyntaxSpan> spans)
    {
        foreach (Match m in SelectorTokenRegex().Matches(text))
        {
            if (!Within(formats, m.Index, m.Length, SyntaxFormat.CssSelector))
            {
                continue;
            }

            SyntaxFormat format = m.Value[0] == '@' ? SyntaxFormat.CssAtRule : SyntaxFormat.CssSpecialSelector;
            spans.Add(new SyntaxSpan(m.Index, m.Length, format));
        }
    }

    private static void RefineValues(string text, SyntaxFormat?[] formats, List<SyntaxSpan> spans)
    {
        foreach (Match m in ValueTokenRegex().Matches(text))
        {
            if (!Within(formats, m.Index, m.Length, SyntaxFormat.CssValue))
            {
                continue;
            }

            if (m.Groups["important"].Success)
            {
                spans.Add(new SyntaxSpan(m.Index, m.Length, SyntaxFormat.CssAtRule));
            }
            else if (m.Groups["string"].Success)
            {
                spans.Add(new SyntaxSpan(m.Index, m.Length, SyntaxFormat.CssQuote));
                if (m.Length == 1 || m.Value[^1] != '\'')
                {
                    spans.Add(new SyntaxSpan(m.Index, m.Length, SyntaxFormat.SyntaxError, SyntaxIssue.UnterminatedString));
                }
            }
            else if (m.Groups["ident"].Success)
            {
                if (ColorNames.Contains(m.Value))
                {
                    spans.Add(new SyntaxSpan(m.Index, m.Length, SyntaxFormat.CssConstant));
                }
            }
            else
            {
                spans.Add(new SyntaxSpan(m.Index, m.Length, SyntaxFormat.CssConstant));
            }
        }
    }

    private void AddLinks(string text, SyntaxFormat?[] formats, List<SyntaxSpan> spans)
    {
        foreach (Match m in UrlRegex().Matches(text))
        {
            // url( … ) in a value or in a selector (@import url(…)), not in a comment.
            if (formats[m.Index] is not (SyntaxFormat.CssValue or SyntaxFormat.CssSelector))
            {
                continue;
            }

            Group target = m.Groups["target"];
            AddLink(spans, target.Value, target.Index, target.Length);
        }
    }

    private void AddLink(List<SyntaxSpan> spans, string reference, int start, int length)
    {
        string trimmed = reference.Trim();
        if (trimmed.Length == 0 || trimmed.StartsWith("data:", StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        bool exists = _linkExists?.Invoke(trimmed) ?? true;
        spans.Add(exists
            ? new SyntaxSpan(start, length, SyntaxFormat.Link, SyntaxIssue.Link)
            : new SyntaxSpan(start, length, SyntaxFormat.BadLink, SyntaxIssue.BrokenLink));
    }

    // @rule, .class, #id, :pseudo / ::pseudo-element.
    [GeneratedRegex(@"@[A-Za-z_-][\w-]*|[.#][A-Za-z_\\-][\w\\-]*|::?[A-Za-z_-][\w-]*")]
    private static partial Regex SelectorTokenRegex();

    // The order of the alternatives matters: #hex before a number, a string before an identifier.
    [GeneratedRegex(
        @"(?<important>!\s*important\b)" +
        @"|(?<hex>#[0-9A-Fa-f]{3,8}\b)" +
        @"|(?<string>'[^']*'?)" +
        @"|(?<number>(?<![\w#.-])[-+]?(?:\d+(?:\.\d*)?|\.\d+)(?:%|[A-Za-z]+)?)" +
        @"|(?<ident>(?<![\w#.-])[A-Za-z][\w-]*)",
        RegexOptions.IgnoreCase)]
    private static partial Regex ValueTokenRegex();

    // url( "target" ) / url('target') / url(target) — the target group without quotes and spaces.
    [GeneratedRegex(@"url\(\s*(?<q>['""]?)(?<target>[^'"")]*?)\k<q>\s*\)", RegexOptions.IgnoreCase)]
    private static partial Regex UrlRegex();
}
