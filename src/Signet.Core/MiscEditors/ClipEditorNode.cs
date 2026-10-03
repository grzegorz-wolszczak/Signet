using System.Collections.Generic;

namespace Signet.Core.MiscEditors;

/// <summary>
/// A node of the clip tree. Mutated only by <see cref="ClipEditorModel"/>.
/// </summary>
public sealed class ClipEditorNode
{
    private readonly List<ClipEditorNode> _children = new();

    internal ClipEditorNode(bool isGroup, string name, bool isRoot = false)
    {
        IsGroup = isGroup;
        IsRoot = isRoot;
        Name = name;
    }

    /// <summary>Whether the node is a group (container).</summary>
    public bool IsGroup { get; }

    /// <summary>Whether this is the invisible root of the tree.</summary>
    public bool IsRoot { get; }

    /// <summary>Display name (a single path segment).</summary>
    public string Name { get; internal set; }

    /// <summary>Clip content (empty for groups); may contain the <c>\1</c> placeholder.</summary>
    public string Text { get; internal set; } = string.Empty;

    /// <summary>Parent in the tree (<see langword="null"/> only for the root).</summary>
    public ClipEditorNode? Parent { get; internal set; }

    /// <summary>Children of the node (for groups).</summary>
    public IReadOnlyList<ClipEditorNode> Children => _children;

    internal List<ClipEditorNode> MutableChildren => _children;
}
