using System.Collections.Generic;

namespace Signet.Core.MiscEditors;

/// <summary>
/// A node of the saved searches tree.
/// Mutated only by <see cref="SearchEditorModel"/>.
/// </summary>
public sealed class SearchEditorNode
{
    private readonly List<SearchEditorNode> _children = new();

    internal SearchEditorNode(bool isGroup, string name, bool isRoot = false)
    {
        IsGroup = isGroup;
        IsRoot = isRoot;
        Name = name;
    }

    /// <summary>Whether the node is a group (a container).</summary>
    public bool IsGroup { get; }

    /// <summary>Whether this is the invisible root of the tree.</summary>
    public bool IsRoot { get; }

    /// <summary>The display name (a single path segment).</summary>
    public string Name { get; internal set; }

    /// <summary>The Find text / pattern (empty for groups).</summary>
    public string Find { get; internal set; } = string.Empty;

    /// <summary>The Replace text (empty for groups).</summary>
    public string Replace { get; internal set; } = string.Empty;

    /// <summary>The encoded search options (see <see cref="SearchControls"/>).</summary>
    public string Controls { get; internal set; } = string.Empty;

    /// <summary>The parent in the tree (<see langword="null"/> only for the root).</summary>
    public SearchEditorNode? Parent { get; internal set; }

    /// <summary>The node's children (for groups).</summary>
    public IReadOnlyList<SearchEditorNode> Children => _children;

    internal List<SearchEditorNode> MutableChildren => _children;
}
