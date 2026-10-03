namespace Signet.Core.Search;

/// <summary>
/// Options affecting how the search pattern is built from the Find field (regex options
/// and "tags/text", used by <see cref="SearchRegexBuilder"/>).
/// </summary>
/// <param name="DotAll">Regex: <c>.</c> also matches a newline (prefix <c>(?s)</c>).</param>
/// <param name="MinimalMatch">Regex: quantifiers are lazy by default (prefix <c>(?U)</c>).</param>
/// <param name="UnicodeProperty">Regex: <c>\w \d \s</c> etc. use Unicode properties (prefix <c>(*UCP)</c>).</param>
/// <param name="TextOnly">
/// "Search in text, not tags" — skips the content of <c>&lt;...&gt;</c> tags
/// (prefix <c>&lt;[^&lt;&gt;]*&gt;(*SKIP)(*F)|</c>); applied only when the searched resource is XML.
/// </param>
public readonly record struct SearchOptions(
    bool DotAll = false,
    bool MinimalMatch = false,
    bool UnicodeProperty = false,
    bool TextOnly = false)
{
    /// <summary>All options disabled.</summary>
    public static SearchOptions None => default;
}
