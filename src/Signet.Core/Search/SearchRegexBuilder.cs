using System;
using System.Text;
using System.Text.RegularExpressions;

namespace Signet.Core.Search;

/// <summary>
/// Builds the actual PCRE2 pattern from the Find field text, the mode and the options.
/// </summary>
public static class SearchRegexBuilder
{
    private const string OptionUnicodeProperty = "(*UCP)";
    private const string OptionIgnoreCase = "(?i)";
    private const string OptionDotAll = "(?s)";
    private const string OptionMinimalMatch = "(?U)";
    private const string OptionTextOnly = "<[^<>]*>(*SKIP)(*F)|";

    // Equivalent of \R in PCRE2: CRLF, CR, LF, VT (U+000B), FF (U+000C), NEL (U+0085),
    // LS (U+2028), PS (U+2029).
    private static readonly Regex AnyLineBreak =
        new("\r\n|[\r\n\u000B\u000C\u0085\u2028\u2029]", RegexOptions.CultureInvariant);

    /// <summary>
    /// Returns the PCRE2 pattern for the given Find text.
    /// </summary>
    /// <param name="findText">Content of the Find field.</param>
    /// <param name="mode">Search mode.</param>
    /// <param name="options">Regex / "tags-text" options.</param>
    /// <param name="searchIsXml">
    /// Whether the searched resource is an XML document (decides whether the "TextOnly" option applies).
    /// </param>
    public static string BuildSearchRegex(
        string findText,
        SearchMode mode,
        SearchOptions options,
        bool searchIsXml)
    {
        // Normalize line breaks to '\n'.
        string search = AnyLineBreak.Replace(findText ?? string.Empty, "\n");

        if (mode is SearchMode.Normal or SearchMode.CaseSensitive)
        {
            search = PcreEscape(search);

            if (options.TextOnly && searchIsXml)
            {
                search = Prepend(OptionTextOnly, search);
            }

            if (mode == SearchMode.Normal)
            {
                search = Prepend(OptionIgnoreCase, search);
            }

            return search;
        }

        // SearchMode.Regex
        if (options.TextOnly && searchIsXml)
        {
            search = Prepend(OptionTextOnly, search);
        }

        if (options.DotAll)
        {
            search = Prepend(OptionDotAll, search);
        }

        if (options.MinimalMatch)
        {
            search = Prepend(OptionMinimalMatch, search);
        }

        if (options.UnicodeProperty)
        {
            search = Prepend(OptionUnicodeProperty, search);
        }

        return search;
    }

    /// <summary>
    /// Escapes text for literal matching in PCRE2: every character outside <c>[A-Za-z0-9_]</c>
    /// is prefixed with a backslash (the NUL character is written as <c>\x00</c>).
    /// </summary>
    public static string PcreEscape(string text)
    {
        if (string.IsNullOrEmpty(text))
        {
            return string.Empty;
        }

        var sb = new StringBuilder(text.Length + 8);
        foreach (char c in text)
        {
            if ((c >= 'a' && c <= 'z') || (c >= 'A' && c <= 'Z') || (c >= '0' && c <= '9') || c == '_')
            {
                sb.Append(c);
            }
            else if (c == '\0')
            {
                sb.Append("\\x00");
            }
            else
            {
                sb.Append('\\').Append(c);
            }
        }

        return sb.ToString();
    }

    // (*UCP) must always precede all other directives.
    private static string Prepend(string option, string search)
    {
        if (search.StartsWith(OptionUnicodeProperty, StringComparison.Ordinal))
        {
            if (option == OptionUnicodeProperty)
            {
                return search;
            }

            return string.Concat(OptionUnicodeProperty, option, search.AsSpan(OptionUnicodeProperty.Length));
        }

        return option + search;
    }
}
