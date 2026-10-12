using System;
using System.Collections.Generic;

namespace Signet.Core.Parsers;

/// <summary>A declaration found by <see cref="CssDeclarationScanner"/>, with exact offsets in the scanned text.</summary>
/// <param name="Property">The property name (trimmed, as written).</param>
/// <param name="Start">The offset of the property name.</param>
/// <param name="ValueStart">The offset of the first character of the value (after the colon and whitespace).</param>
/// <param name="ValueEnd">The offset right after the last non-whitespace character of the value (including <c>!important</c>).</param>
/// <param name="End">The offset right after the terminating <c>;</c>, or <paramref name="ValueEnd"/> when there is none.</param>
public sealed record CssScannedDeclaration(string Property, int Start, int ValueStart, int ValueEnd, int End);

/// <summary>A rule with a block of declarations found by <see cref="CssDeclarationScanner"/>.</summary>
/// <param name="Prelude">The selector group or the at-rule prelude (e.g. <c>@font-face</c>), whitespace-trimmed.</param>
/// <param name="Start">The offset of the first character of the prelude.</param>
/// <param name="BlockStart">The offset of the opening <c>{</c>.</param>
/// <param name="End">The offset right after the closing <c>}</c> (the end of the text for an unclosed block).</param>
/// <param name="ParentAtRule">The prelude of the enclosing grouping rule (e.g. <c>@media screen</c>), or <c>null</c>.</param>
/// <param name="Declarations">The declarations of the block, in source order.</param>
public sealed record CssScannedRule(
    string Prelude,
    int Start,
    int BlockStart,
    int End,
    string? ParentAtRule,
    IReadOnlyList<CssScannedDeclaration> Declarations)
{
    /// <summary>Whether this is an at-rule (<c>@font-face</c>, <c>@page</c>…), not a style rule.</summary>
    public bool IsAtRule => Prelude.StartsWith('@');

    /// <summary>Whether this is an <c>@font-face</c> rule.</summary>
    public bool IsFontFace => Prelude.StartsWith("@font-face", StringComparison.OrdinalIgnoreCase);
}

/// <summary>
/// A lenient CSS scanner that keeps exact offsets into the original text (unlike <see cref="CssTokenizer"/>, it does
/// not normalize the text): the rules with a declaration block — also inside grouping rules such as <c>@media</c> —
/// and their declarations. Comments, strings, escapes and brackets are skipped correctly, so edits made at the
/// reported offsets leave the rest of the stylesheet byte for byte as it was.
/// </summary>
public static class CssDeclarationScanner
{
    /// <summary>The rules with a declaration block in <paramref name="text"/> (a stylesheet or a <c>&lt;style&gt;</c> block).</summary>
    public static IReadOnlyList<CssScannedRule> ScanRules(string text) => ScanRules(text, 0, text?.Length ?? 0);

    /// <summary>The rules with a declaration block in <paramref name="text"/>[<paramref name="start"/>, <paramref name="end"/>).</summary>
    public static IReadOnlyList<CssScannedRule> ScanRules(string text, int start, int end)
    {
        ArgumentNullException.ThrowIfNull(text);
        List<CssScannedRule> rules = new();
        ScanRuleList(text, start, Math.Min(end, text.Length), null, rules);
        return rules;
    }

    /// <summary>
    /// The declarations in <paramref name="text"/>[<paramref name="start"/>, <paramref name="end"/>) — the inside of a
    /// block, or the value of a <c>style</c> attribute. Nested blocks are skipped.
    /// </summary>
    public static IReadOnlyList<CssScannedDeclaration> ScanDeclarations(string text, int start, int end)
    {
        ArgumentNullException.ThrowIfNull(text);
        List<CssScannedDeclaration> declarations = new();
        end = Math.Min(end, text.Length);
        int pos = start;
        while (pos < end)
        {
            pos = SkipWhitespaceAndComments(text, pos, end);
            if (pos >= end)
            {
                break;
            }

            if (text[pos] == ';')
            {
                pos++;
                continue;
            }

            int declStart = pos;
            int stop = FindAtDepthZero(text, pos, end, ":;{}");
            if (stop >= end || text[stop] == ';')
            {
                pos = stop + 1;
                continue;
            }

            if (text[stop] == '}')
            {
                pos = stop + 1;
                continue;
            }

            if (text[stop] == '{')
            {
                pos = SkipBlock(text, stop, end);
                continue;
            }

            string property = text[declStart..stop].Trim();
            int valueEnd = FindAtDepthZero(text, stop + 1, end, ";{}");
            if (valueEnd < end && text[valueEnd] == '{')
            {
                // A nested rule whose selector contains a colon (a:hover { … }) — not a declaration.
                pos = SkipBlock(text, valueEnd, end);
                continue;
            }

            int valueStart = SkipWhitespace(text, stop + 1, valueEnd);
            int trimmedEnd = valueEnd;
            while (trimmedEnd > valueStart && char.IsWhiteSpace(text[trimmedEnd - 1]))
            {
                trimmedEnd--;
            }

            bool hasSemicolon = valueEnd < end && text[valueEnd] == ';';
            if (property.Length > 0 && !property.StartsWith("/*", StringComparison.Ordinal))
            {
                declarations.Add(new CssScannedDeclaration(
                    property, declStart, valueStart, trimmedEnd, hasSemicolon ? valueEnd + 1 : trimmedEnd));
            }

            pos = hasSemicolon ? valueEnd + 1 : valueEnd;
            if (valueEnd < end && text[valueEnd] == '}')
            {
                pos = valueEnd + 1;
            }
        }

        return declarations;
    }

    /// <summary>The offset of the first character at or after <paramref name="pos"/> that is not whitespace or a comment.</summary>
    public static int SkipWhitespaceAndComments(string text, int pos, int end)
    {
        ArgumentNullException.ThrowIfNull(text);
        while (pos < end)
        {
            if (char.IsWhiteSpace(text[pos]))
            {
                pos++;
            }
            else if (pos + 1 < end && text[pos] == '/' && text[pos + 1] == '*')
            {
                int close = text.IndexOf("*/", pos + 2, end - pos - 2, StringComparison.Ordinal);
                pos = close < 0 ? end : close + 2;
            }
            else if (string.CompareOrdinal(text, pos, "<!--", 0, 4) == 0)
            {
                pos += 4;
            }
            else if (string.CompareOrdinal(text, pos, "-->", 0, 3) == 0)
            {
                pos += 3;
            }
            else
            {
                break;
            }
        }

        return Math.Min(pos, end);
    }

    private static void ScanRuleList(string text, int start, int end, string? parentAtRule, List<CssScannedRule> rules)
    {
        int pos = start;
        while (pos < end)
        {
            pos = SkipWhitespaceAndComments(text, pos, end);
            if (pos >= end)
            {
                break;
            }

            if (text[pos] is '}' or ';')
            {
                pos++;
                continue;
            }

            int ruleStart = pos;
            int stop = FindAtDepthZero(text, pos, end, ";{");
            if (stop >= end || text[stop] == ';')
            {
                // A statement (@import, @charset…) or garbage.
                pos = stop + 1;
                continue;
            }

            string prelude = text[ruleStart..stop].Trim();
            int blockEnd = SkipBlock(text, stop, end);
            int innerEnd = blockEnd > stop + 1 && blockEnd <= end && text[blockEnd - 1] == '}' ? blockEnd - 1 : blockEnd;
            if (prelude.StartsWith('@') && ContainsNestedRules(text, stop + 1, innerEnd))
            {
                ScanRuleList(text, stop + 1, innerEnd, prelude, rules);
            }
            else
            {
                rules.Add(new CssScannedRule(prelude, ruleStart, stop, blockEnd, parentAtRule, ScanDeclarations(text, stop + 1, innerEnd)));
            }

            pos = blockEnd;
        }
    }

    // Whether the first statement of a block is a nested rule (@media { a { … } }) rather than a declaration.
    private static bool ContainsNestedRules(string text, int start, int end)
    {
        int pos = SkipWhitespaceAndComments(text, start, end);
        if (pos >= end)
        {
            return false;
        }

        int stop = FindAtDepthZero(text, pos, end, ";{}");
        return stop < end && text[stop] == '{';
    }

    // The offset right after the '}' matching the '{' at openBrace (or end when unclosed).
    private static int SkipBlock(string text, int openBrace, int end)
    {
        int depth = 0;
        int pos = openBrace;
        while (pos < end)
        {
            char c = text[pos];
            if (c is '"' or '\'')
            {
                pos = SkipString(text, pos, end);
                continue;
            }

            if (c == '/' && pos + 1 < end && text[pos + 1] == '*')
            {
                int close = text.IndexOf("*/", pos + 2, end - pos - 2, StringComparison.Ordinal);
                pos = close < 0 ? end : close + 2;
                continue;
            }

            if (c == '\\')
            {
                pos += 2;
                continue;
            }

            if (c == '{')
            {
                depth++;
            }
            else if (c == '}')
            {
                depth--;
                if (depth == 0)
                {
                    return pos + 1;
                }
            }

            pos++;
        }

        return end;
    }

    // The offset of the first of the stop characters outside strings, comments and ()/[] — or end.
    private static int FindAtDepthZero(string text, int pos, int end, string stops)
    {
        int depth = 0;
        while (pos < end)
        {
            char c = text[pos];
            if (c is '"' or '\'')
            {
                pos = SkipString(text, pos, end);
                continue;
            }

            if (c == '/' && pos + 1 < end && text[pos + 1] == '*')
            {
                int close = text.IndexOf("*/", pos + 2, end - pos - 2, StringComparison.Ordinal);
                pos = close < 0 ? end : close + 2;
                continue;
            }

            if (c == '\\')
            {
                pos += 2;
                continue;
            }

            if (c is '(' or '[')
            {
                depth++;
            }
            else if (c is ')' or ']')
            {
                depth = Math.Max(0, depth - 1);
            }
            else if (depth == 0 && stops.Contains(c, StringComparison.Ordinal))
            {
                return pos;
            }
            else if (depth > 0 && c is '{' or '}' or ';' && stops.Contains(c, StringComparison.Ordinal))
            {
                // An unclosed bracket must not swallow the rest of the stylesheet.
                return pos;
            }

            pos++;
        }

        return end;
    }

    // The offset right after the string starting at pos (an unterminated string ends at the line end).
    private static int SkipString(string text, int pos, int end)
    {
        char quote = text[pos];
        pos++;
        while (pos < end)
        {
            char c = text[pos];
            if (c == '\\')
            {
                pos += 2;
                continue;
            }

            if (c == quote)
            {
                return pos + 1;
            }

            if (c == '\n')
            {
                return pos;
            }

            pos++;
        }

        return end;
    }

    private static int SkipWhitespace(string text, int pos, int end)
    {
        while (pos < end && char.IsWhiteSpace(text[pos]))
        {
            pos++;
        }

        return pos;
    }
}
