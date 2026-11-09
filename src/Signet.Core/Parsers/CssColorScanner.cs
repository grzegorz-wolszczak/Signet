using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Signet.Core.BookManipulation;

namespace Signet.Core.Parsers;

/// <summary>How a color is written in the text (<see cref="CssColorMatch.Notation"/>).</summary>
public enum CssColorNotation
{
    /// <summary><c>#rgb</c>, <c>#rrggbb</c>, <c>#rgba</c>, <c>#rrggbbaa</c>.</summary>
    Hex,

    /// <summary><c>rgb()</c> / <c>rgba()</c>.</summary>
    Rgb,

    /// <summary><c>hsl()</c> / <c>hsla()</c>.</summary>
    Hsl,

    /// <summary>A named color or <c>transparent</c>.</summary>
    Named,

    /// <summary><c>hwb()</c>, <c>lab()</c>, <c>lch()</c>, <c>oklab()</c>, <c>oklch()</c>, <c>color()</c>.</summary>
    Other,
}

/// <summary>A color value found in the text.</summary>
/// <param name="Start">The offset of the first character.</param>
/// <param name="Length">The length in the text.</param>
/// <param name="Notation">How it is written.</param>
/// <param name="Color">The color as 8-bit sRGB, or <c>null</c> for a notation that is recognized but not converted (<c>color(a98-rgb …)</c>).</param>
public sealed record CssColorMatch(int Start, int Length, CssColorNotation Notation, CssColor? Color);

/// <summary>
/// Finds the colors in the values of the declarations of a stylesheet, of the <c>&lt;style&gt;</c> elements and of the <c>style</c> attributes of an
/// (X)HTML file (for the preview under them and the picker of the Code Editor), and writes a new color in the notation of the old one. Selectors,
/// strings, <c>url()</c> and comments are never scanned; a named color counts only in a property that takes colors.
/// </summary>
public static class CssColorScanner
{
    private static readonly string[] ColorProperties =
    [
        "background", "border", "border-top", "border-right", "border-bottom", "border-left", "border-block", "border-inline",
        "border-block-start", "border-block-end", "border-inline-start", "border-inline-end", "outline", "box-shadow", "text-shadow",
        "text-decoration", "column-rule", "fill", "stroke", "filter", "-webkit-text-stroke", "mask", "drop-shadow",
    ];

    /// <summary>The colors in the declarations of the rules of a stylesheet (or of a <c>&lt;style&gt;</c> block).</summary>
    public static IReadOnlyList<CssColorMatch> InStylesheet(string css)
    {
        ArgumentNullException.ThrowIfNull(css);
        List<CssColorMatch> matches = new();
        AddStylesheet(css, 0, css.Length, matches);
        return matches;
    }

    /// <summary>The colors in <paramref name="text"/>[<paramref name="start"/>, <paramref name="end"/>) read as a list of declarations (a <c>style</c> attribute).</summary>
    public static IReadOnlyList<CssColorMatch> InDeclarations(string text, int start, int end)
    {
        ArgumentNullException.ThrowIfNull(text);
        List<CssColorMatch> matches = new();
        AddDeclarations(text, CssDeclarationScanner.ScanDeclarations(text, start, end), matches);
        return matches;
    }

    /// <summary>The colors of an (X)HTML file: in its <c>&lt;style&gt;</c> elements and its <c>style</c> attributes; the offsets are in the file.</summary>
    public static IReadOnlyList<CssColorMatch> InHtml(string html)
    {
        ArgumentNullException.ThrowIfNull(html);
        List<CssColorMatch> matches = new();
        foreach ((int offset, string css) in MarkupEdits.StyleElements(html))
        {
            AddStylesheet(html, offset, offset + css.Length, matches);
        }

        if (html.Contains("style", StringComparison.OrdinalIgnoreCase))
        {
            foreach ((int pos, int length) in TagLister.EnumerateOpeningTags(html))
            {
                TagLister.AttInfo style = TagLister.ParseAttribute(html.Substring(pos, length), "style");
                if (style.Pos >= 0 && style.VPos >= 0 && style.VLen > 0)
                {
                    matches.AddRange(InDeclarations(html, pos + style.VPos, pos + style.VPos + style.VLen));
                }
            }
        }

        return matches.OrderBy(m => m.Start).ToList();
    }

    /// <summary>
    /// The text of <paramref name="color"/> in the notation of <paramref name="original"/> (found in <paramref name="text"/>) when it has one that
    /// can say it: hex stays hex (short when the old one was and the color has a short form), <c>rgb()</c>/<c>hsl()</c> keep their separators and gain
    /// an alpha only when the color is not opaque, a name stays a name when the color has one; any other notation becomes hex.
    /// </summary>
    public static string Format(CssColor color, CssColorMatch original, string text)
    {
        ArgumentNullException.ThrowIfNull(original);
        ArgumentNullException.ThrowIfNull(text);
        string old = text.Substring(original.Start, original.Length);
        switch (original.Notation)
        {
            case CssColorNotation.Hex:
                return old.Length is 4 or 5 ? color.ToShortHex() : color.ToHex();
            case CssColorNotation.Named:
                return CssColorParser.NameOf(color) ?? color.ToHex();
            case CssColorNotation.Rgb:
                return Function(
                    "rgb", color, old, color.R.ToString(CultureInfo.InvariantCulture), color.G.ToString(CultureInfo.InvariantCulture),
                    color.B.ToString(CultureInfo.InvariantCulture));
            case CssColorNotation.Hsl:
            {
                (int h, int s, int l) = ToHsl(color);
                return Function("hsl", color, old, h.ToString(CultureInfo.InvariantCulture), s.ToString(CultureInfo.InvariantCulture) + "%", l.ToString(CultureInfo.InvariantCulture) + "%");
            }

            default:
                return color.ToHex();
        }
    }

    // rgb(r, g, b) / rgb(r g b), with the alpha as a fourth value (rgba in the legacy syntax) or after a slash; the separators of the old text.
    private static string Function(string name, CssColor color, string old, string first, string second, string third)
    {
        bool legacy = old.Contains(',', StringComparison.Ordinal);
        string separator = legacy ? (old.Contains(", ", StringComparison.Ordinal) ? ", " : ",") : " ";
        string body = first + separator + second + separator + third;
        if (color.A == 255)
        {
            return $"{name}({body})";
        }

        string alpha = Math.Round(color.A / 255.0, 2).ToString("0.##", CultureInfo.InvariantCulture);
        return legacy ? $"{name}a({body}{separator}{alpha})" : $"{name}({body} / {alpha})";
    }

    private static (int H, int S, int L) ToHsl(CssColor color)
    {
        double r = color.R / 255.0;
        double g = color.G / 255.0;
        double b = color.B / 255.0;
        double max = Math.Max(r, Math.Max(g, b));
        double min = Math.Min(r, Math.Min(g, b));
        double lightness = (max + min) / 2;
        double delta = max - min;
        if (delta == 0)
        {
            return (0, 0, (int)Math.Round(lightness * 100));
        }

        double saturation = delta / (1 - Math.Abs((2 * lightness) - 1));
        double hue = max == r ? ((g - b) / delta) % 6 : max == g ? ((b - r) / delta) + 2 : ((r - g) / delta) + 4;
        hue *= 60;
        if (hue < 0)
        {
            hue += 360;
        }

        return ((int)Math.Round(hue) % 360, (int)Math.Round(saturation * 100), (int)Math.Round(lightness * 100));
    }

    // --- scanning ---

    private static void AddStylesheet(string text, int start, int end, List<CssColorMatch> matches)
    {
        foreach (CssScannedRule rule in CssDeclarationScanner.ScanRules(text, start, end))
        {
            AddDeclarations(text, rule.Declarations, matches);
        }
    }

    private static void AddDeclarations(string text, IReadOnlyList<CssScannedDeclaration> declarations, List<CssColorMatch> matches)
    {
        foreach (CssScannedDeclaration declaration in declarations)
        {
            bool namesCount = TakesNamedColors(declaration.Property);
            ScanValue(text, declaration.ValueStart, declaration.ValueEnd, namesCount, matches);
        }
    }

    private static bool TakesNamedColors(string property)
    {
        string name = property.Trim().ToLowerInvariant();
        return name.StartsWith("--", StringComparison.Ordinal) || name.EndsWith("color", StringComparison.Ordinal)
            || Array.IndexOf(ColorProperties, name) >= 0;
    }

    // The colors of a value: hex, color functions and (where they count) names; strings, url() and comments are skipped, other functions are looked into.
    private static void ScanValue(string text, int start, int end, bool namesCount, List<CssColorMatch> matches)
    {
        int i = start;
        while (i < end)
        {
            char c = text[i];
            if (c == '/' && i + 1 < end && text[i + 1] == '*')
            {
                int close = text.IndexOf("*/", i + 2, StringComparison.Ordinal);
                i = close < 0 || close + 2 > end ? end : close + 2;
                continue;
            }

            if (c is '"' or '\'')
            {
                i++;
                while (i < end && text[i] != c)
                {
                    i += text[i] == '\\' ? 2 : 1;
                }

                i++;
                continue;
            }

            if (c == '#')
            {
                int digits = i + 1;
                while (digits < end && IsIdentifierCharacter(text[digits]))
                {
                    digits++;
                }

                if (CssColorParser.TryParse(text.Substring(i, digits - i), out CssColor hex))
                {
                    matches.Add(new CssColorMatch(i, digits - i, CssColorNotation.Hex, hex));
                }

                i = digits;
                continue;
            }

            if (!char.IsAsciiLetter(c) && c != '-' && c != '_')
            {
                i++;
                continue;
            }

            int wordEnd = i;
            while (wordEnd < end && IsIdentifierCharacter(text[wordEnd]))
            {
                wordEnd++;
            }

            string word = text.Substring(i, wordEnd - i);
            if (wordEnd < end && text[wordEnd] == '(')
            {
                int close = MatchingParenthesis(text, wordEnd, end);
                if (word.Equals("url", StringComparison.OrdinalIgnoreCase))
                {
                    i = close < 0 ? end : close + 1;
                    continue;
                }

                string lower = word.ToLowerInvariant();
                if (close >= 0 && lower is "rgb" or "rgba" or "hsl" or "hsla" or "hwb" or "lab" or "lch" or "oklab" or "oklch" or "color")
                {
                    string function = text.Substring(i, close + 1 - i);
                    CssColorNotation notation = lower is "rgb" or "rgba" ? CssColorNotation.Rgb
                        : lower is "hsl" or "hsla" ? CssColorNotation.Hsl
                        : CssColorNotation.Other;
                    bool parsed = CssColorParser.TryParse(function, out CssColor color);

                    // rgb(var(--r) 0 0) is not a color; color(a98-rgb …) is one this parser does not convert (no color, but it is shown and can be replaced).
                    if (parsed || (notation == CssColorNotation.Other && CssColorParser.IsColorFunction(function)))
                    {
                        matches.Add(new CssColorMatch(i, function.Length, notation, parsed ? color : null));
                    }

                    i = close + 1;
                    continue;
                }

                // Another function (a gradient, var(), calc()): its arguments are scanned as part of the value.
                i = wordEnd + 1;
                continue;
            }

            if (namesCount && CssColorParser.IsColorName(word) && CssColorParser.TryParse(word, out CssColor named))
            {
                matches.Add(new CssColorMatch(i, word.Length, CssColorNotation.Named, named));
            }

            i = wordEnd;
        }
    }

    private static bool IsIdentifierCharacter(char c) => char.IsAsciiLetterOrDigit(c) || c is '-' or '_';

    // The offset of the ')' that closes the '(' at open, or -1.
    private static int MatchingParenthesis(string text, int open, int end)
    {
        int depth = 0;
        for (int i = open; i < end; i++)
        {
            if (text[i] == '(')
            {
                depth++;
            }
            else if (text[i] == ')' && --depth == 0)
            {
                return i;
            }
        }

        return -1;
    }
}
