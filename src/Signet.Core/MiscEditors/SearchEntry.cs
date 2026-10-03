namespace Signet.Core.MiscEditors;

/// <summary>
/// A single entry of the saved searches library.
/// An entry can be a group (<see cref="IsGroup"/> = <see langword="true"/>) or a search.
/// </summary>
/// <param name="IsGroup">Whether the entry is a group (a container for other entries).</param>
/// <param name="FullName">
/// The full name with the group path separated by <c>/</c> (e.g. <c>Typography/Quotes/Opening</c>);
/// for a group it ends with <c>/</c>.
/// </param>
/// <param name="Name">The display name (the last segment of <see cref="FullName"/>).</param>
/// <param name="Find">The text / pattern from the Find field.</param>
/// <param name="Replace">The text from the Replace field.</param>
/// <param name="Controls">
/// The encoded search options (mode, direction, scope, regex flags) — see
/// <see cref="SearchControls"/>. Empty for groups.
/// </param>
public sealed record SearchEntry(
    bool IsGroup,
    string FullName,
    string Name,
    string Find,
    string Replace,
    string Controls)
{
    /// <summary>An empty search entry (not a group).</summary>
    public static SearchEntry EmptySearch { get; } =
        new(IsGroup: false, FullName: "Search", Name: "Search", Find: "", Replace: "", Controls: "");

    /// <summary>
    /// Builds an entry from a (normalized) full name: <see cref="Name"/> = the last segment,
    /// <see cref="IsGroup"/> determined by a trailing <c>/</c>; the Find/Replace/Controls fields are cleared for groups.
    /// </summary>
    public static SearchEntry FromFullName(string fullName, string find, string replace, string controls)
    {
        System.ArgumentNullException.ThrowIfNull(fullName);
        bool isGroup = fullName.EndsWith('/');
        string trimmed = fullName.TrimEnd('/');
        int slash = trimmed.LastIndexOf('/');
        string name = slash >= 0 ? trimmed[(slash + 1)..] : trimmed;
        return new SearchEntry(
            isGroup,
            fullName,
            name,
            isGroup ? string.Empty : find,
            isGroup ? string.Empty : replace,
            isGroup ? string.Empty : controls);
    }
}
