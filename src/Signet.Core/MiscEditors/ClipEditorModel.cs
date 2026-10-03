using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;

namespace Signet.Core.MiscEditors;

/// <summary>
/// The view-independent clip tree. Holds the hierarchy of groups and clips and supports adding (with
/// expansion of a <c>Group/Subgroup/Name</c> path), deleting, renaming, moving,
/// numbering the Clip Bar slots and converting to/from a flat list of
/// <see cref="ClipEntry"/> (the save / import / export format).
/// </summary>
public sealed class ClipEditorModel
{
    /// <summary>The maximum number of top-level clips assignable to Clip Bar slots / shortcuts.</summary>
    public const int MaxSlots = 60;

    private static readonly Regex CollapseSlashes = new(@"\s*/+\s*", RegexOptions.Compiled);
    private static readonly Regex LeadingSlash = new("^/", RegexOptions.Compiled);

    /// <summary>The invisible root of the tree.</summary>
    public ClipEditorNode Root { get; } = new(isGroup: true, name: string.Empty, isRoot: true);

    /// <summary>Whether the data changed since the last <see cref="MarkSaved"/> / <see cref="LoadEntries"/>.</summary>
    public bool IsDataModified { get; private set; }

    /// <summary>Marks the data as saved (unmodified).</summary>
    public void MarkSaved() => IsDataModified = false;

    /// <summary>Clears the tree and loads entries from a flat list (order preserved).</summary>
    public void LoadEntries(IEnumerable<ClipEntry> entries)
    {
        ArgumentNullException.ThrowIfNull(entries);
        Root.MutableChildren.Clear();
        foreach (ClipEntry entry in entries)
        {
            AddFullNameEntry(entry);
        }

        IsDataModified = false;
    }

    /// <summary>A flat list of entries (leaves + empty groups) with computed full names — for saving / exporting.</summary>
    public IReadOnlyList<ClipEntry> ToEntries() =>
        GetNonParentItems(Root).Select(ToEntry).ToList();

    /// <summary>The node's full name with the group path (a group ends with <c>/</c>).</summary>
    public string FullNameOf(ClipEditorNode node)
    {
        ArgumentNullException.ThrowIfNull(node);
        var parts = new List<string>();
        for (ClipEditorNode? n = node; n is not null && !n.IsRoot; n = n.Parent)
        {
            parts.Insert(0, n.Name);
        }

        string full = string.Join('/', parts);
        return node.IsGroup && full.Length > 0 ? full + "/" : full;
    }

    /// <summary>Returns the <see cref="ClipEntry"/> corresponding to the node.</summary>
    public ClipEntry ToEntry(ClipEditorNode node)
    {
        ArgumentNullException.ThrowIfNull(node);
        return new ClipEntry(node.IsGroup, FullNameOf(node), node.Name, node.Text);
    }

    /// <summary>
    /// Adds an entry, expanding the group path contained in <see cref="ClipEntry.Name"/> / <see cref="ClipEntry.FullName"/>.
    /// Missing intermediate groups are created.
    /// </summary>
    public ClipEditorNode AddFullNameEntry(ClipEntry entry, ClipEditorNode? parent = null, int row = -1)
    {
        ArgumentNullException.ThrowIfNull(entry);
        parent ??= Root;

        if (!ReferenceEquals(parent, Root) && !parent.IsGroup)
        {
            row = IndexOf(parent) + 1;
            parent = parent.Parent!;
        }

        if (row < 0)
        {
            row = parent.Children.Count;
        }

        string path = NormalizeFullName(string.IsNullOrEmpty(entry.FullName) ? entry.Name : entry.FullName);
        bool isGroup = entry.IsGroup || path.EndsWith('/');
        string entryName = path.TrimEnd('/');

        if (entryName.Contains('/', StringComparison.Ordinal))
        {
            var groupNames = entryName.Split('/', StringSplitOptions.RemoveEmptyEntries).ToList();
            entryName = groupNames[^1];
            if (!isGroup)
            {
                groupNames.RemoveAt(groupNames.Count - 1);
            }

            foreach (string groupName in groupNames)
            {
                ClipEditorNode? found = parent.Children.FirstOrDefault(
                    c => c.IsGroup && string.Equals(c.Name, groupName, StringComparison.Ordinal));
                parent = found ?? AddEntry(
                    new ClipEntry(true, string.Empty, groupName, string.Empty),
                    isGroup: true,
                    parent,
                    parent.Children.Count);
            }

            row = parent.Children.Count;
        }

        if (isGroup)
        {
            // The intermediate groups were created; add the group explicitly only when it does not exist.
            ClipEditorNode? existing = parent.Children.FirstOrDefault(
                c => c.IsGroup && string.Equals(c.Name, entryName, StringComparison.Ordinal));
            return existing ?? AddEntry(
                new ClipEntry(true, string.Empty, entryName, string.Empty),
                isGroup: true,
                parent,
                row);
        }

        return AddEntry(entry with { Name = entryName, IsGroup = false }, isGroup: false, parent, row);
    }

    /// <summary>
    /// Adds a single node. <paramref name="entry"/>
    /// = <see langword="null"/> creates an empty entry / group.
    /// </summary>
    public ClipEditorNode AddEntry(ClipEntry? entry, bool isGroup, ClipEditorNode? parent, int row)
    {
        parent ??= Root;
        bool group = entry?.IsGroup ?? isGroup;
        string name = entry?.Name ?? (group ? "Group" : "Text");

        var node = new ClipEditorNode(group, name) { Parent = parent };
        if (!group && entry is not null)
        {
            node.Text = entry.Text;
        }

        if (row < 0 || row >= parent.Children.Count)
        {
            parent.MutableChildren.Add(node);
        }
        else
        {
            parent.MutableChildren.Insert(row, node);
        }

        IsDataModified = true;
        return node;
    }

    /// <summary>Renames a node (a path segment). Names cannot contain <c>/</c>.</summary>
    public void Rename(ClipEditorNode node, string name)
    {
        ArgumentNullException.ThrowIfNull(node);
        ArgumentNullException.ThrowIfNull(name);
        string clean = name.Replace("/", string.Empty, StringComparison.Ordinal).Trim();
        if (clean.Length == 0 || string.Equals(clean, node.Name, StringComparison.Ordinal))
        {
            return;
        }

        node.Name = clean;
        IsDataModified = true;
    }

    /// <summary>Sets the clip content of an entry node (ignored for groups).</summary>
    public void SetText(ClipEditorNode node, string value)
    {
        ArgumentNullException.ThrowIfNull(node);
        ArgumentNullException.ThrowIfNull(value);
        if (node.IsGroup || string.Equals(node.Text, value, StringComparison.Ordinal))
        {
            return;
        }

        node.Text = value;
        IsDataModified = true;
    }

    /// <summary>Removes a node (with its subtree). Returns whether anything was removed.</summary>
    public bool Delete(ClipEditorNode node)
    {
        ArgumentNullException.ThrowIfNull(node);
        if (node.Parent is null || node.IsRoot)
        {
            return false;
        }

        bool removed = node.Parent.MutableChildren.Remove(node);
        if (removed)
        {
            IsDataModified = true;
        }

        return removed;
    }

    /// <summary>Moves a node one position up among its siblings.</summary>
    public bool MoveUp(ClipEditorNode node) => MoveVertical(node, moveDown: false);

    /// <summary>Moves a node one position down.</summary>
    public bool MoveDown(ClipEditorNode node) => MoveVertical(node, moveDown: true);

    /// <summary>Moves a node out to the parent's level, right after the parent.</summary>
    public bool MoveLeft(ClipEditorNode node)
    {
        ArgumentNullException.ThrowIfNull(node);
        ClipEditorNode parent = node.Parent ?? Root;
        if (ReferenceEquals(parent, Root))
        {
            return false;
        }

        ClipEditorNode destination = parent.Parent ?? Root;
        int destinationRow = IndexOf(parent) + 1;
        return Reparent(node, destination, destinationRow);
    }

    /// <summary>
    /// Moves a node into the group located directly above it in the tree view.
    /// </summary>
    public bool MoveRight(ClipEditorNode node)
    {
        ArgumentNullException.ThrowIfNull(node);
        ClipEditorNode parent = node.Parent ?? Root;
        ClipEditorNode? above = NodeAbove(node);
        if (above is null || ReferenceEquals(above, parent))
        {
            return false;
        }

        ClipEditorNode destination;
        if (above.IsGroup)
        {
            destination = above;
        }
        else if (above.Parent is { } aboveParent && !ReferenceEquals(aboveParent, parent) && !aboveParent.IsRoot)
        {
            destination = aboveParent;
        }
        else
        {
            return false;
        }

        return Reparent(node, destination, destination.Children.Count);
    }

    /// <summary>The node with the given full name or <see langword="null"/>.</summary>
    public ClipEditorNode? GetNodeFromFullName(string fullName)
    {
        ArgumentNullException.ThrowIfNull(fullName);
        string target = NormalizeFullName(fullName).TrimEnd('/');
        return Descendants(Root)
            .FirstOrDefault(n => string.Equals(FullNameOf(n).TrimEnd('/'), target, StringComparison.Ordinal));
    }

    /// <summary>All leaves (non-groups) in the subtrees of the given nodes, in tree order.</summary>
    public IReadOnlyList<ClipEditorNode> GetNonGroupItems(IEnumerable<ClipEditorNode> nodes)
    {
        ArgumentNullException.ThrowIfNull(nodes);
        return nodes.SelectMany(GetNonGroupItems).ToList();
    }

    /// <summary>The leaves (non-groups) in the node's subtree.</summary>
    public IReadOnlyList<ClipEditorNode> GetNonGroupItems(ClipEditorNode node)
    {
        ArgumentNullException.ThrowIfNull(node);
        var result = new List<ClipEditorNode>();
        CollectNonGroup(node, result);
        return result;
    }

    /// <summary>The <see cref="ClipEntry"/> entries for the given nodes.</summary>
    public IReadOnlyList<ClipEntry> GetEntries(IEnumerable<ClipEditorNode> nodes)
    {
        ArgumentNullException.ThrowIfNull(nodes);
        return nodes.Select(ToEntry).ToList();
    }

    /// <summary>
    /// Top-level clips (direct children of the root, skipping groups), in tree order,
    /// truncated to <see cref="MaxSlots"/> — the slot numbering (Clip Bar slots /
    /// keyboard shortcuts <c>Clip1..Clip60</c> are assigned only to clips
    /// not wrapped in a group).
    /// </summary>
    public IReadOnlyList<ClipEditorNode> TopLevelSlots() =>
        Root.Children.Where(n => !n.IsGroup).Take(MaxSlots).ToList();

    /// <summary>The clip assigned to slot <paramref name="slotNumber"/> (1-based) or <see langword="null"/>.</summary>
    public ClipEditorNode? GetSlot(int slotNumber)
    {
        if (slotNumber < 1 || slotNumber > MaxSlots)
        {
            return null;
        }

        IReadOnlyList<ClipEditorNode> slots = TopLevelSlots();
        return slotNumber <= slots.Count ? slots[slotNumber - 1] : null;
    }

    /// <summary>Normalizes a full name: collapses whitespace around <c>/</c>, removes a leading <c>/</c>.</summary>
    public static string NormalizeFullName(string fullName)
    {
        ArgumentNullException.ThrowIfNull(fullName);
        string collapsed = CollapseSlashes.Replace(fullName, "/");
        return LeadingSlash.Replace(collapsed, string.Empty);
    }

    private bool MoveVertical(ClipEditorNode node, bool moveDown)
    {
        ArgumentNullException.ThrowIfNull(node);
        ClipEditorNode parent = node.Parent ?? Root;
        int row = parent.MutableChildren.IndexOf(node);
        if (row < 0)
        {
            return false;
        }

        ClipEditorNode destination = parent;
        int destinationRow;

        if (moveDown)
        {
            if (row >= parent.Children.Count - 1)
            {
                if (ReferenceEquals(parent, Root))
                {
                    return false;
                }

                destination = parent.Parent ?? Root;
                destinationRow = IndexOf(parent) + 1;
            }
            else
            {
                destinationRow = row + 1;
            }
        }
        else
        {
            if (row == 0)
            {
                if (ReferenceEquals(parent, Root))
                {
                    return false;
                }

                destination = parent.Parent ?? Root;
                destinationRow = IndexOf(parent);
            }
            else
            {
                destinationRow = row - 1;
            }
        }

        // The target index is computed before removal.
        // For a move within the same parent this swaps places with the neighbor.
        parent.MutableChildren.RemoveAt(row);
        destinationRow = Math.Clamp(destinationRow, 0, destination.Children.Count);
        node.Parent = destination;
        destination.MutableChildren.Insert(destinationRow, node);
        IsDataModified = true;
        return true;
    }

    private bool Reparent(ClipEditorNode node, ClipEditorNode destination, int destinationRow)
    {
        ClipEditorNode parent = node.Parent ?? Root;
        if (ReferenceEquals(destination, node) || IsAncestor(node, destination))
        {
            return false;
        }

        parent.MutableChildren.Remove(node);
        destinationRow = Math.Clamp(destinationRow, 0, destination.Children.Count);
        node.Parent = destination;
        destination.MutableChildren.Insert(destinationRow, node);
        IsDataModified = true;
        return true;
    }

    private int IndexOf(ClipEditorNode node) =>
        (node.Parent ?? Root).MutableChildren.IndexOf(node);

    private static bool IsAncestor(ClipEditorNode maybeAncestor, ClipEditorNode node)
    {
        for (ClipEditorNode? n = node.Parent; n is not null; n = n.Parent)
        {
            if (ReferenceEquals(n, maybeAncestor))
            {
                return true;
            }
        }

        return false;
    }

    private ClipEditorNode? NodeAbove(ClipEditorNode node)
    {
        var order = Descendants(Root).ToList();
        int idx = order.IndexOf(node);
        return idx > 0 ? order[idx - 1] : null;
    }

    private static IEnumerable<ClipEditorNode> Descendants(ClipEditorNode node)
    {
        foreach (ClipEditorNode child in node.Children)
        {
            yield return child;
            foreach (ClipEditorNode d in Descendants(child))
            {
                yield return d;
            }
        }
    }

    private static void CollectNonGroup(ClipEditorNode node, List<ClipEditorNode> into)
    {
        if (!node.IsGroup && !node.IsRoot)
        {
            into.Add(node);
        }

        foreach (ClipEditorNode child in node.Children)
        {
            CollectNonGroup(child, into);
        }
    }

    private static IEnumerable<ClipEditorNode> GetNonParentItems(ClipEditorNode node)
    {
        if (node.Children.Count == 0)
        {
            if (!node.IsRoot)
            {
                yield return node;
            }

            yield break;
        }

        foreach (ClipEditorNode child in node.Children)
        {
            foreach (ClipEditorNode d in GetNonParentItems(child))
            {
                yield return d;
            }
        }
    }
}
