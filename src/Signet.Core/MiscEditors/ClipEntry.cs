using System;
using System.Collections.Generic;

namespace Signet.Core.MiscEditors;

/// <summary>
/// A single clip library entry. An entry may be a group (<see cref="IsGroup"/> =
/// <see langword="true"/>) or a text clip.
/// </summary>
/// <param name="IsGroup">Whether the entry is a group (a container for other entries).</param>
/// <param name="GroupPath">
/// The names of the enclosing groups, outermost first (empty for an entry at the top level). Names may contain any
/// character, also <c>/</c>.
/// </param>
/// <param name="Name">Display name of the entry.</param>
/// <param name="Text">
/// Clip content — may contain the <c>\1</c> placeholder, replaced with the selected text on
/// insertion (see <c>CodeInsertOperations.PasteClipText</c>). Empty for groups.
/// </param>
public sealed record ClipEntry(
    bool IsGroup,
    IReadOnlyList<string> GroupPath,
    string Name,
    string Text)
{
    /// <summary>An empty clip (not a group).</summary>
    public static ClipEntry EmptyClip { get; } =
        new(IsGroup: false, GroupPath: Array.Empty<string>(), Name: "Clip", Text: string.Empty);

    /// <summary>
    /// The full name for display: the group path and the name joined with <c>/</c> (e.g.
    /// <c>Styles/Highlight Anchor IDs</c>); ends with <c>/</c> for a group. Not unambiguous when a name contains
    /// <c>/</c> — the entry is identified by <see cref="GroupPath"/> and <see cref="Name"/>.
    /// </summary>
    public string FullName => GroupedXml.FullName(GroupPath, Name, IsGroup);
}
