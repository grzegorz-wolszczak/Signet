using System.Collections.Generic;

namespace Signet.Core.Metadata;

/// <summary>
/// An editable node of the OPF metadata tree used by the "Metadata Editor":
/// a top-level node is a recognized element (<c>dc:*</c>, a primary <c>meta</c> or a custom
/// element), and its children are attributes/refinements (always a flat list — in practice the tree has
/// exactly two levels, although the structure does not enforce that).
/// <see cref="Code"/> and <see cref="Content"/> operate on internal codes (e.g. <c>dc:creator</c>,
/// <c>role</c>, <c>aut</c>), not on display names — the code &#8596; name translation belongs
/// to the presentation layer.
/// </summary>
public sealed class MetadataEntry
{
    /// <summary>
    /// The element code (e.g. <c>dc:creator</c>, <c>meta</c>, a custom element name) for a
    /// top-level node, or the attribute/property code (e.g. <c>role</c>, <c>scheme</c>, <c>id</c>,
    /// <c>xml:lang</c>) for a child node.
    /// </summary>
    public string Code { get; set; } = string.Empty;

    /// <summary>The text value (decoded from XML entities) — the element content or the attribute value.</summary>
    public string Content { get; set; } = string.Empty;

    /// <summary>The attributes/refinements of this node (always a flat list, with no further nesting).</summary>
    public IList<MetadataEntry> Children { get; } = new List<MetadataEntry>();
}
