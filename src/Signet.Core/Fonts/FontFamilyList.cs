using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;

namespace Signet.Core.Fonts;

/// <summary>One family name in a <c>font-family</c> list, with its offsets in the scanned text.</summary>
/// <param name="Name">The family name (quotes removed, escapes resolved, whitespace collapsed).</param>
/// <param name="Start">The offset of the entry (including its quotes).</param>
/// <param name="End">The offset right after the entry.</param>
/// <param name="IsQuoted">Whether the name is a quoted string.</param>
/// <param name="IsGeneric">Whether the entry is a generic family or a CSS-wide keyword (<c>serif</c>, <c>inherit</c>…).</param>
public sealed record FontFamilyEntry(string Name, int Start, int End, bool IsQuoted, bool IsGeneric);

/// <summary>What the <c>font</c> shorthand sets, with the offsets of its family list.</summary>
/// <param name="Style">The style keyword (<c>italic</c>, <c>oblique…</c>) or <c>normal</c> when not given.</param>
/// <param name="Weight">The weight keyword or number, or <c>normal</c> when not given.</param>
/// <param name="FamilyStart">The offset of the family list.</param>
/// <param name="FamilyEnd">The offset right after the family list.</param>
public sealed record FontShorthand(string Style, string Weight, int FamilyStart, int FamilyEnd);

/// <summary>Parsing and rebuilding of <c>font-family</c> lists (also the family part of the <c>font</c> shorthand).</summary>
public static class FontFamilyList
{
    /// <summary>The generic families and the CSS-wide keywords — never names of embedded fonts.</summary>
    public static IReadOnlySet<string> GenericFamilies { get; } = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
    {
        "serif", "sans-serif", "monospace", "cursive", "fantasy", "system-ui", "ui-serif", "ui-sans-serif",
        "ui-monospace", "ui-rounded", "math", "emoji", "fangsong", "inherit", "initial", "unset", "revert",
        "revert-layer", "default",
    };

    private static readonly HashSet<string> SizeKeywords = new(StringComparer.OrdinalIgnoreCase)
    {
        "xx-small", "x-small", "small", "medium", "large", "x-large", "xx-large", "xxx-large", "larger", "smaller",
    };

    private static readonly Regex SizeToken = new(
        @"^(?:[+-]?(?:\d+\.?\d*|\.\d+)(?:[a-z]+|%)?|(?:calc|clamp|min|max)\(.*\))(?:/.*)?$",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    private static readonly Regex ImportantSuffix = new(@"!\s*important\s*$", RegexOptions.IgnoreCase | RegexOptions.Compiled);

    /// <summary>
    /// The entries of the family list in <paramref name="text"/>[<paramref name="start"/>, <paramref name="end"/>)
    /// (a trailing <c>!important</c> is not part of the list). Empty entries are skipped.
    /// </summary>
    public static IReadOnlyList<FontFamilyEntry> Parse(string text, int start, int end)
    {
        ArgumentNullException.ThrowIfNull(text);
        end = ListEnd(text, start, end);
        List<FontFamilyEntry> entries = new();
        int pos = start;
        while (pos <= end)
        {
            int comma = FindComma(text, pos, end);
            int itemStart = pos;
            int itemEnd = comma;
            while (itemStart < itemEnd && char.IsWhiteSpace(text[itemStart]))
            {
                itemStart++;
            }

            while (itemEnd > itemStart && char.IsWhiteSpace(text[itemEnd - 1]))
            {
                itemEnd--;
            }

            if (itemEnd > itemStart)
            {
                string raw = text[itemStart..itemEnd];
                bool quoted = raw[0] is '"' or '\'';
                string name = FontFaceDescriptors.NormalizeFamily(raw);
                entries.Add(new FontFamilyEntry(name, itemStart, itemEnd, quoted, !quoted && GenericFamilies.Contains(name)));
            }

            pos = comma + 1;
        }

        return entries;
    }

    /// <summary>
    /// The style, weight and family list of a <c>font</c> shorthand value in
    /// <paramref name="text"/>[<paramref name="start"/>, <paramref name="end"/>); <c>null</c> for a system font
    /// keyword (<c>caption</c>…), a value with <c>var()</c>, or one without a size.
    /// </summary>
    public static FontShorthand? ParseShorthand(string text, int start, int end)
    {
        ArgumentNullException.ThrowIfNull(text);
        end = ListEnd(text, start, end);
        if (text.AsSpan(start, end - start).Contains("var(", StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        string style = "normal";
        string weight = "normal";
        int pos = SkipSpaces(text, start, end);
        while (pos < end)
        {
            int tokenEnd = TokenEnd(text, pos, end);
            string token = text[pos..tokenEnd];
            if (token[0] is '"' or '\'' || token.Contains(',', StringComparison.Ordinal))
            {
                return null;
            }

            string lower = token.ToLowerInvariant();
            int slash = lower.IndexOf('/', StringComparison.Ordinal);
            if (SizeKeywords.Contains(slash >= 0 ? lower[..slash] : lower) || (SizeToken.IsMatch(token) && !IsWeightNumber(token)))
            {
                // The size, optionally followed by "/line-height" — written as "12px/1.5", "12px/ 1.5",
                // "12px /1.5" or "12px / 1.5".
                pos = SkipSpaces(text, tokenEnd, end);
                bool slashPending = token.EndsWith('/');
                bool lineHeightDone = token.Contains('/', StringComparison.Ordinal) && !slashPending;
                if (!lineHeightDone && !slashPending && pos < end && text[pos] == '/')
                {
                    slashPending = text[pos..TokenEnd(text, pos, end)] == "/";
                    pos = slashPending ? SkipSpaces(text, pos + 1, end) : SkipSpaces(text, TokenEnd(text, pos, end), end);
                }

                if (slashPending && pos < end)
                {
                    pos = SkipSpaces(text, TokenEnd(text, pos, end), end);
                }

                return pos < end ? new FontShorthand(style, weight, pos, end) : null;
            }

            if (lower is "italic" || lower.StartsWith("oblique", StringComparison.Ordinal))
            {
                style = lower.StartsWith("oblique", StringComparison.Ordinal) ? "oblique" : "italic";
            }
            else if (lower is "bold" or "bolder" or "lighter" || IsWeightNumber(token))
            {
                weight = lower;
            }

            pos = SkipSpaces(text, tokenEnd, end);
        }

        return null;
    }

    /// <summary>
    /// The text of a family list without the entries for which <paramref name="remove"/> is true, joined with
    /// <c>", "</c> and keeping each remaining entry as written; <c>""</c> when nothing remains.
    /// </summary>
    public static string Without(string text, IReadOnlyList<FontFamilyEntry> entries, Func<FontFamilyEntry, bool> remove)
    {
        ArgumentNullException.ThrowIfNull(text);
        ArgumentNullException.ThrowIfNull(entries);
        ArgumentNullException.ThrowIfNull(remove);
        return string.Join(", ", entries.Where(e => !remove(e)).Select(e => text[e.Start..e.End]));
    }

    /// <summary>
    /// A family name written for a stylesheet: quoted with <paramref name="quote"/> (inner quotes and backslashes
    /// escaped), as is usual for embedded fonts.
    /// </summary>
    public static string Quote(string name, char quote = '"')
    {
        ArgumentNullException.ThrowIfNull(name);
        StringBuilder sb = new(name.Length + 2);
        sb.Append(quote);
        foreach (char c in name)
        {
            if (c == quote || c == '\\')
            {
                sb.Append('\\');
            }

            sb.Append(c);
        }

        return sb.Append(quote).ToString();
    }

    private static int SkipSpaces(string text, int pos, int end)
    {
        while (pos < end && char.IsWhiteSpace(text[pos]))
        {
            pos++;
        }

        return pos;
    }

    private static bool IsWeightNumber(string token) =>
        int.TryParse(token, System.Globalization.NumberStyles.None, System.Globalization.CultureInfo.InvariantCulture, out int n)
        && n is >= 1 and <= 1000;

    // The end of the list: before a trailing !important and whitespace.
    private static int ListEnd(string text, int start, int end)
    {
        end = Math.Min(end, text.Length);
        Match important = ImportantSuffix.Match(text[start..end]);
        if (important.Success)
        {
            end = start + important.Index;
        }

        while (end > start && char.IsWhiteSpace(text[end - 1]))
        {
            end--;
        }

        return end;
    }

    // The next comma outside strings at or after pos — or end.
    private static int FindComma(string text, int pos, int end)
    {
        while (pos < end)
        {
            char c = text[pos];
            if (c is '"' or '\'')
            {
                pos = StringEnd(text, pos, end);
                continue;
            }

            if (c == '\\')
            {
                pos += 2;
                continue;
            }

            if (c == ',')
            {
                return pos;
            }

            pos++;
        }

        return end;
    }

    // The end of a whitespace-separated token of a shorthand (strings and brackets kept whole).
    private static int TokenEnd(string text, int pos, int end)
    {
        int depth = 0;
        while (pos < end)
        {
            char c = text[pos];
            if (c is '"' or '\'')
            {
                pos = StringEnd(text, pos, end);
                continue;
            }

            if (c == '(')
            {
                depth++;
            }
            else if (c == ')')
            {
                depth = Math.Max(0, depth - 1);
            }
            else if (depth == 0 && char.IsWhiteSpace(c))
            {
                break;
            }

            pos++;
        }

        return pos;
    }

    private static int StringEnd(string text, int pos, int end)
    {
        char quote = text[pos++];
        while (pos < end)
        {
            if (text[pos] == '\\')
            {
                pos += 2;
                continue;
            }

            if (text[pos++] == quote)
            {
                break;
            }
        }

        return Math.Min(pos, end);
    }
}
