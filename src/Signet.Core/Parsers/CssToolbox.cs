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
    public static IReadOnlyList<(int Index, string Name)> FindClassesInSelector(string selector)
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
                    bracketDepth++;
                    continue;
                case ']':
                    bracketDepth = Math.Max(0, bracketDepth - 1);
                    continue;
                case '\\':
                    i++;
                    continue;
            }

            if (c != '.' || bracketDepth > 0 || i + 1 >= selector.Length || !IsIdentStart(selector, i + 1))
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
