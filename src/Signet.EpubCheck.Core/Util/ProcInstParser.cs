using System;
using System.Globalization;
using System.Text;

namespace Signet.EpubCheck.Core.Util;

/// <summary>
/// Reads the pseudo-attributes of a processing instruction such as <c>&lt;?xml-stylesheet href="a.css" type="text/css"?&gt;</c>
/// (Saxon's <c>ProcInstParser</c>, which upstream calls for <c>xml-stylesheet</c>).
/// </summary>
public static class ProcInstParser
{
    /// <summary>
    /// The value of the pseudo-attribute <paramref name="name"/> in <paramref name="content"/>, or <c>null</c> when there is none;
    /// the predefined entities and character references in the value are replaced.
    /// </summary>
    public static string? GetPseudoAttribute(string content, string name)
    {
        ArgumentNullException.ThrowIfNull(content);
        ArgumentNullException.ThrowIfNull(name);
        int position = 0;
        while (true)
        {
            int found = content.IndexOf(name, position, StringComparison.Ordinal);
            if (found < 0)
            {
                return null;
            }

            position = found + name.Length;
            if (found > 0 && !IsSpace(content[found - 1]))
            {
                continue;
            }

            int index = SkipSpace(content, position);
            if (index >= content.Length || content[index] != '=')
            {
                continue;
            }

            index = SkipSpace(content, index + 1);
            if (index >= content.Length || content[index] is not ('"' or '\''))
            {
                continue;
            }

            char quote = content[index];
            int end = content.IndexOf(quote, index + 1);
            if (end < 0)
            {
                return null;
            }

            return Unescape(content[(index + 1)..end]);
        }
    }

    private static bool IsSpace(char c) => c is ' ' or '\t' or '\r' or '\n';

    private static int SkipSpace(string content, int index)
    {
        while (index < content.Length && IsSpace(content[index]))
        {
            index++;
        }

        return index;
    }

    private static string Unescape(string value)
    {
        if (!value.Contains('&', StringComparison.Ordinal))
        {
            return value;
        }

        StringBuilder result = new();
        int i = 0;
        while (i < value.Length)
        {
            int semicolon = value[i] == '&' ? value.IndexOf(';', i) : -1;
            if (semicolon < 0)
            {
                result.Append(value[i++]);
                continue;
            }

            string entity = value[(i + 1)..semicolon];
            string? replacement = entity switch
            {
                "lt" => "<",
                "gt" => ">",
                "amp" => "&",
                "quot" => "\"",
                "apos" => "'",
                _ => CharacterReference(entity),
            };
            if (replacement is null)
            {
                result.Append(value[i++]);
            }
            else
            {
                result.Append(replacement);
                i = semicolon + 1;
            }
        }

        return result.ToString();
    }

    private static string? CharacterReference(string entity)
    {
        if (entity.Length < 2 || entity[0] != '#')
        {
            return null;
        }

        bool hex = entity[1] is 'x' or 'X';
        string digits = entity[(hex ? 2 : 1)..];
        NumberStyles style = hex ? NumberStyles.AllowHexSpecifier : NumberStyles.None;
        return digits.Length > 0 && int.TryParse(digits, style, CultureInfo.InvariantCulture, out int codePoint) && codePoint is > 0 and <= 0x10FFFF and not (>= 0xD800 and <= 0xDFFF)
            ? char.ConvertFromUtf32(codePoint)
            : null;
    }
}
