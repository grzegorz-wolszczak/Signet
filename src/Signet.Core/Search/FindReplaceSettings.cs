using System;
using System.Collections.Generic;

namespace Signet.Core.Search;

/// <summary>
/// Persisted state of the Find &amp; Replace panel (the <c>find_replace</c> settings group).
/// </summary>
public sealed record FindReplaceSettings
{
    /// <summary>Maximum number of history entries for the Find/Replace fields.</summary>
    public const int MaxHistory = 25;

    /// <summary>History of the Find field, newest first.</summary>
    public IReadOnlyList<string> FindHistory { get; init; } = Array.Empty<string>();

    /// <summary>History of the Replace field, newest first.</summary>
    public IReadOnlyList<string> ReplaceHistory { get; init; } = Array.Empty<string>();

    /// <summary>Search mode.</summary>
    public SearchMode Mode { get; init; } = SearchMode.Normal;

    /// <summary>Search direction.</summary>
    public SearchDirection Direction { get; init; } = SearchDirection.Down;

    /// <summary>Search scope (Current File / All HTML / …).</summary>
    public LookWhere LookWhere { get; init; } = LookWhere.CurrentFile;

    /// <summary>Wrap the search (enabled by default).</summary>
    public bool OptionWrap { get; init; } = true;

    /// <summary>Regex: <c>(?s)</c> DotAll.</summary>
    public bool RegexDotAll { get; init; }

    /// <summary>Regex: <c>(?U)</c> Minimal-Match.</summary>
    public bool RegexMinimalMatch { get; init; }

    /// <summary>Regex: <c>(*UCP)</c> Unicode-Property.</summary>
    public bool RegexUnicodeProperty { get; init; }

    /// <summary>"Search in text, not tags" (<c>&lt;[^&lt;&gt;]*&gt;(*SKIP)(*F)|</c>).</summary>
    public bool RegexTextOnly { get; init; }

    /// <summary>
    /// "Auto Tokenise" — in Regex mode, automatically escapes metacharacters when the selected
    /// text is inserted into the Find field.
    /// </summary>
    public bool RegexAutoTokenise { get; init; }

    /// <summary>
    /// "Highlight all matches" — besides the current match (the selection), highlights the
    /// remaining matches in the active file. Disabled by default.
    /// </summary>
    public bool HighlightAllMatches { get; init; }

    /// <summary>Whether the panel was visible when the application was closed.</summary>
    public bool PanelVisible { get; init; }
}
