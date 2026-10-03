using System.Text.RegularExpressions;

namespace Signet.Core.Search;

/// <summary>
/// "Tokenise" — turns literal text into a regex pattern, escaping metacharacters while
/// keeping it readable (punctuation that does not need escaping stays as is,
/// whitespace runs become <c>\s+</c>). Also used by "Auto Tokenise".
/// </summary>
public static class RegexTokeniser
{
    private static readonly Regex WhitespaceRun = new(@"\s{2,}", RegexOptions.CultureInvariant);
    private static readonly Regex Digits = new(@"\d+", RegexOptions.CultureInvariant);

    /// <summary>
    /// Converts <paramref name="text"/> into a regex pattern that matches it literally.
    /// If the text already contains a backslash, it is assumed to be tokenised already and is
    /// not escaped again.
    /// </summary>
    /// <param name="text">Input text (literal or already partly a regex).</param>
    /// <param name="includeNumerics">
    /// When <see langword="true"/>, digit runs are replaced with <c>\d+</c> (the explicit
    /// "Tokenise" action on the whole Find field). Auto Tokenise, when inserting a selection from
    /// Code View, uses <see langword="false"/> — numbers usually should stay literal.
    /// </param>
    public static string TokeniseForRegex(string text, bool includeNumerics)
    {
        if (string.IsNullOrEmpty(text))
        {
            return string.Empty;
        }

        // Any line break / tab -> two spaces (before any escaping).
        string newText = SearchRegexBuilderLineBreaks.Replace(text, "  ").Replace("\\t", "  ");

        if (!newText.Contains('\\'))
        {
            newText = SearchRegexBuilder.PcreEscape(newText);
        }

        // Restore readability of selected characters that do not need escaping.
        newText = newText
            .Replace("\\ ", " ")
            .Replace("\\<", "<")
            .Replace("\\>", ">")
            .Replace("\\/", "/")
            .Replace("\\;", ";")
            .Replace("\\:", ":")
            .Replace("\\&", "&")
            .Replace("\\=", "=");

        newText = WhitespaceRun.Replace(newText, @"\s+");

        if (includeNumerics)
        {
            newText = Digits.Replace(newText, @"\d+");
        }

        return newText;
    }

    private static readonly Regex SearchRegexBuilderLineBreaks =
        new("\r\n|[\r\n\v\f\u0085\u2028\u2029]", RegexOptions.CultureInvariant);
}
