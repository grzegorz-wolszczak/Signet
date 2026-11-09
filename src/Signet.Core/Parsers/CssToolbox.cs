using System;
using System.Collections.Generic;

namespace Signet.Core.Parsers;

/// <summary>
/// Finding class names in CSS selectors (used by
/// <see cref="BookManipulation.ClassRenamer"/>).
/// </summary>
public static class CssToolbox
{
    /// <summary>
    /// The classes occurring in a selector text: the name position (the character after the dot) and the name. Skips
    /// the insides of <c>[...]</c> (attribute selectors) and of quoted strings.
    /// </summary>
    public static IReadOnlyList<(int Index, string Name)> FindClassesInSelector(string selector) =>
        FindMarkedNamesInSelector(selector, '.');

    /// <summary>
    /// The ids occurring in a selector text: <c>#name</c> (the name position is the character after the hash) and the value of an
    /// <c>[id=name]</c> / <c>[id="name"]</c> attribute selector (its position is the first character of the name, inside the quotes);
    /// the name is the id. Skips the other insides of <c>[...]</c> (attribute selectors) and of quoted strings.
    /// </summary>
    public static IReadOnlyList<(int Index, string Name)> FindIdsInSelector(string selector) =>
        FindMarkedNamesInSelector(selector, '#');

    private static List<(int Index, string Name)> FindMarkedNamesInSelector(string selector, char marker)
    {
        ArgumentNullException.ThrowIfNull(selector);
        List<(int, string)> result = new();
        int bracketDepth = 0;
        char quote = '\0';
        for (int i = 0; i < selector.Length; i++)
        {
            char c = selector[i];
            if (quote != '\0')
            {
                if (c == '\\')
                {
                    i++;
                }
                else if (c == quote)
                {
                    quote = '\0';
                }

                continue;
            }

            switch (c)
            {
                case '"' or '\'':
                    quote = c;
                    continue;
                case '[':
                    if (marker == '#' && bracketDepth == 0 && TryReadIdAttributeSelector(selector, i, out int valueStart, out string value, out int next))
                    {
                        result.Add((valueStart, value));
                        i = next;
                        continue;
                    }

                    bracketDepth++;
                    continue;
                case ']':
                    bracketDepth = Math.Max(0, bracketDepth - 1);
                    continue;
                case '\\':
                    i++;
                    continue;
            }

            if (c != marker || bracketDepth > 0 || i + 1 >= selector.Length || !IsIdentStart(selector, i + 1))
            {
                continue;
            }

            int start = i + 1;
            int end = start;
            while (end < selector.Length && IsIdentChar(selector[end]))
            {
                end += selector[end] == '\\' && end + 1 < selector.Length ? 2 : 1;
            }

            result.Add((start, selector[start..end]));
            i = end - 1;
        }

        return result;
    }

    // "[id=value]", "[ id = 'value' ]": an attribute selector that matches an id exactly (the name of the attribute in any letter case).
    // Gives where the value starts, the value and the position of the closing bracket (other operators, like ^=, are not an exact match).
    internal static bool TryReadIdAttributeSelector(string s, int open, out int valueStart, out string value, out int close)
    {
        valueStart = -1;
        value = string.Empty;
        close = -1;
        int i = SkipSpaces(s, open + 1);
        if (string.Compare(s, i, "id", 0, 2, StringComparison.OrdinalIgnoreCase) != 0)
        {
            return false;
        }

        i = SkipSpaces(s, i + 2);
        if (i >= s.Length || s[i] != '=')
        {
            return false;
        }

        i = SkipSpaces(s, i + 1);
        if (i >= s.Length)
        {
            return false;
        }

        int end;
        if (s[i] is '"' or '\'')
        {
            char quote = s[i];
            valueStart = i + 1;
            end = valueStart;
            while (end < s.Length && s[end] != quote && s[end] != '\\')
            {
                end++;
            }

            if (end >= s.Length || s[end] != quote)
            {
                return false;
            }

            value = s[valueStart..end];
            end++;
        }
        else
        {
            valueStart = i;
            end = i;
            while (end < s.Length && IsIdentChar(s[end]) && s[end] != '\\')
            {
                end++;
            }

            value = s[valueStart..end];
        }

        int closing = SkipSpaces(s, end);
        if (value.Length == 0 || closing >= s.Length || s[closing] != ']')
        {
            return false;
        }

        close = closing;
        return true;
    }

    private static int SkipSpaces(string s, int i)
    {
        while (i < s.Length && char.IsWhiteSpace(s[i]))
        {
            i++;
        }

        return i;
    }

    private static bool IsIdentStart(string s, int i)
    {
        char c = s[i];
        if (c == '-')
        {
            return i + 1 < s.Length && (IsNameStartChar(s[i + 1]) || s[i + 1] == '-');
        }

        return IsNameStartChar(c) || c == '\\';
    }

    private static bool IsNameStartChar(char c) => char.IsAsciiLetter(c) || c == '_' || c > 0x7F;

    private static bool IsIdentChar(char c) => IsNameStartChar(c) || char.IsAsciiDigit(c) || c == '-' || c == '\\';
}
