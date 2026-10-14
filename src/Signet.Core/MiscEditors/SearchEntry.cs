using System;
using System.Collections.Generic;
using System.Linq;

namespace Signet.Core.MiscEditors;

/// <summary>
/// A single entry of the saved searches library.
/// An entry can be a group (<see cref="IsGroup"/> = <see langword="true"/>) or a search.
/// </summary>
/// <param name="IsGroup">Whether the entry is a group (a container for other entries).</param>
/// <param name="GroupPath">
/// The names of the enclosing groups, outermost first (empty for an entry at the top level). Names may contain any
/// character, also <c>/</c>.
/// </param>
/// <param name="Name">The display name of the entry.</param>
/// <param name="Find">The text / pattern from the Find field.</param>
/// <param name="Replace">The text from the Replace field.</param>
/// <param name="Controls">
/// The encoded search options (mode, direction, scope, regex flags) — see
/// <see cref="SearchControls"/>. Empty for groups.
/// </param>
public sealed record SearchEntry(
    bool IsGroup,
    IReadOnlyList<string> GroupPath,
    string Name,
    string Find,
    string Replace,
    string Controls)
{
    /// <summary>An empty search entry (not a group).</summary>
    public static SearchEntry EmptySearch { get; } =
        new(IsGroup: false, GroupPath: Array.Empty<string>(), Name: "Search", Find: "", Replace: "", Controls: "");

    /// <summary>
    /// The full name for display and for the delimited (TSV/CSV) formats: the group path and the name joined with
    /// <c>/</c> (e.g. <c>Typography/Quotes/Opening</c>); ends with <c>/</c> for a group.
    /// </summary>
    public string FullName => GroupedXml.FullName(GroupPath, Name, IsGroup);

    /// <summary>
    /// Builds an entry from a (normalized) <c>/</c>-separated full name — the form of the delimited (TSV/CSV) formats:
    /// <see cref="Name"/> = the last segment, <see cref="IsGroup"/> determined by a trailing <c>/</c>; the
    /// Find/Replace/Controls fields are cleared for groups.
    /// </summary>
    public static SearchEntry FromFullName(string fullName, string find, string replace, string controls)
    {
        ArgumentNullException.ThrowIfNull(fullName);
        bool isGroup = fullName.EndsWith('/');
        string[] segments = fullName.TrimEnd('/').Split('/');
        return new SearchEntry(
            isGroup,
            segments[..^1].Where(s => s.Length > 0).ToArray(),
            segments[^1],
            isGroup ? string.Empty : find,
            isGroup ? string.Empty : replace,
            isGroup ? string.Empty : controls);
    }
}
