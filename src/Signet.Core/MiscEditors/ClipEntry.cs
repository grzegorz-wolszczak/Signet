namespace Signet.Core.MiscEditors;

/// <summary>
/// A single clip library entry. An entry may be a group (<see cref="IsGroup"/> =
/// <see langword="true"/>) or a text clip.
/// </summary>
/// <param name="IsGroup">Whether the entry is a group (a container for other entries).</param>
/// <param name="FullName">
/// Full name with the group path separated by <c>/</c> (e.g. <c>Styles/Highlight Anchor IDs</c>);
/// ends with <c>/</c> for a group.
/// </param>
/// <param name="Name">Display name (the last segment of <see cref="FullName"/>).</param>
/// <param name="Text">
/// Clip content — may contain the <c>\1</c> placeholder, replaced with the selected text on
/// insertion (see <c>CodeInsertOperations.PasteClipText</c>). Empty for groups.
/// </param>
public sealed record ClipEntry(
    bool IsGroup,
    string FullName,
    string Name,
    string Text)
{
    /// <summary>An empty clip (not a group).</summary>
    public static ClipEntry EmptyClip { get; } =
        new(IsGroup: false, FullName: "Clip", Name: "Clip", Text: string.Empty);

    /// <summary>
    /// Builds an entry from a (normalized) full name: <see cref="Name"/> = the last segment,
    /// <see cref="IsGroup"/> is determined by a trailing <c>/</c>; Text is cleared for groups.
    /// </summary>
    public static ClipEntry FromFullName(string fullName, string text)
    {
        System.ArgumentNullException.ThrowIfNull(fullName);
        bool isGroup = fullName.EndsWith('/');
        string trimmed = fullName.TrimEnd('/');
        int slash = trimmed.LastIndexOf('/');
        string name = slash >= 0 ? trimmed[(slash + 1)..] : trimmed;
        return new ClipEntry(isGroup, fullName, name, isGroup ? string.Empty : text);
    }
}
