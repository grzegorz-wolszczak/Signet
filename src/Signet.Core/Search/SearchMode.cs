namespace Signet.Core.Search;

/// <summary>Search mode.</summary>
public enum SearchMode
{
    /// <summary>Plain text, case-insensitive (escaped pattern + <c>(?i)</c>).</summary>
    Normal = 0,

    /// <summary>Plain text, case-sensitive (pattern only escaped).</summary>
    CaseSensitive = 1,

    /// <summary>A PCRE2 regular expression given verbatim.</summary>
    Regex = 2,
}

/// <summary>Search direction.</summary>
public enum SearchDirection
{
    /// <summary>From the caret downwards (towards the end of the text).</summary>
    Down = 0,

    /// <summary>From the caret upwards (towards the start of the text).</summary>
    Up = 1,
}
