using System.Collections.Generic;

namespace Signet.Core.Toc;

/// <summary>
/// A table of contents entry prepared for <b>display</b> in the "Table Of Contents" panel
/// — hierarchical, read-only. The navigation target is already resolved to an
/// absolute <see cref="TargetBookPath"/> + <see cref="Fragment"/> (without <c>#</c>).
/// The editable TOC model (with a full round trip) is <see cref="TocEntry"/>.
/// </summary>
public sealed class TocDisplayEntry
{
    /// <summary>The entry text (the label from <c>nav</c>/<c>navLabel</c>).</summary>
    public string Title { get; init; } = string.Empty;

    /// <summary>The absolute bookpath of the target resource (empty if the entry has no link).</summary>
    public string TargetBookPath { get; init; } = string.Empty;

    /// <summary>The fragment identifier in the target file without <c>#</c> (empty = the start of the file).</summary>
    public string Fragment { get; init; } = string.Empty;

    /// <summary>The child entries.</summary>
    public IReadOnlyList<TocDisplayEntry> Children { get; init; } = System.Array.Empty<TocDisplayEntry>();
}
