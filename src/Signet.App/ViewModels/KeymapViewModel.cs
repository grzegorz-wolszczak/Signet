using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using CommunityToolkit.Mvvm.ComponentModel;
using Signet.App.Actions;
using Signet.App.Input;
using Signet.App.Menu;
using Signet.App.Resources;

namespace Signet.App.ViewModels;

/// <summary>
/// The keymap editor (Preferences, modelled on IntelliJ's Keymap page): a tree of the actions — "Main Menu" with the
/// menu's own submenus, the panel groups (Book Browser, Clip Editor) and "Other" for the actions outside the menu —
/// with their shortcuts; a filter by name or shortcut text and "Find Shortcut" (the actions having a given shortcut).
/// Every action may have several keyboard (one or two strokes) and mouse shortcuts; the view asks for a new
/// shortcut and, on a conflict, whether to take it away from the other actions.
/// </summary>
public sealed partial class KeymapViewModel : ViewModelBase
{
    private readonly AppActionRegistry _actions;
    private readonly KeyboardShortcutManager _shortcuts;
    private readonly Dictionary<string, List<KeymapNode>> _nodesById = new(StringComparer.Ordinal);

    /// <summary>Builds the tree from the menu layout and the registered actions.</summary>
    public KeymapViewModel(AppActionRegistry actions, KeyboardShortcutManager shortcuts)
    {
        _actions = actions ?? throw new ArgumentNullException(nameof(actions));
        _shortcuts = shortcuts ?? throw new ArgumentNullException(nameof(shortcuts));

        KeymapNode mainMenu = Group(Strings.Get("Keymap_MainMenu"), path: null);
        mainMenu.IsExpanded = true;
        foreach (MenuNode node in MenuLayout.TopLevel)
        {
            AddMenuNode(mainMenu, node);
        }

        Roots.Add(mainMenu);

        foreach (IGrouping<string, AppAction> panel in _actions.Actions
                     .Where(a => AppActionIds.IsPanelCategory(a.Category))
                     .GroupBy(a => a.CategoryDisplayName, StringComparer.CurrentCulture)
                     .OrderBy(g => g.Key, StringComparer.CurrentCulture))
        {
            KeymapNode group = Group(panel.Key, path: null);
            foreach (AppAction action in panel.OrderBy(NameOf, StringComparer.CurrentCulture))
            {
                group.Children.Add(ActionNode(action, group));
            }

            Roots.Add(group);
        }

        KeymapNode other = Group(Strings.Get("Keymap_Other"), path: null);
        foreach (AppAction action in _actions.Actions
                     .Where(a => !_nodesById.ContainsKey(a.Id))
                     .OrderBy(NameOf, StringComparer.CurrentCulture))
        {
            other.Children.Add(ActionNode(action, other));
        }

        if (other.Children.Count > 0)
        {
            Roots.Add(other);
        }

        _shortcuts.ShortcutChanged += (_, id) => RefreshAction(id);
    }

    /// <summary>The top-level groups.</summary>
    public ObservableCollection<KeymapNode> Roots { get; } = new();

    /// <summary>The selected node.</summary>
    [ObservableProperty]
    private KeymapNode? _selected;

    /// <summary>The filter typed in the search box: an action stays when its name or a shortcut contains it.</summary>
    [ObservableProperty]
    private string _filter = string.Empty;

    /// <summary>"Find Shortcut": only the actions having this shortcut (<c>null</c> — no such filter).</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasShortcutFilter))]
    private Shortcut? _shortcutFilter;

    /// <summary>Whether "Find Shortcut" filters the tree.</summary>
    public bool HasShortcutFilter => ShortcutFilter is not null;

    /// <summary>All action nodes (each action once, in tree order).</summary>
    public IEnumerable<KeymapNode> ActionNodes => _nodesById.Values.Select(nodes => nodes[0]);

    /// <summary>Expands every group.</summary>
    public void ExpandAll() => ForEachGroup(g => g.IsExpanded = true);

    /// <summary>Collapses every group.</summary>
    public void CollapseAll() => ForEachGroup(g => g.IsExpanded = false);

    /// <summary>
    /// The actions (as "name (group path)") that <paramref name="shortcut"/> would conflict with if added to
    /// <paramref name="action"/>.
    /// </summary>
    public IReadOnlyList<string> ConflictsFor(KeymapNode action, Shortcut shortcut)
    {
        ArgumentNullException.ThrowIfNull(action);
        ArgumentNullException.ThrowIfNull(shortcut);
        return action.ActionId is null
            ? Array.Empty<string>()
            : _shortcuts.FindConflicts(action.ActionId, shortcut).Select(LabelOf).ToList();
    }

    /// <summary>Adds a shortcut to the action, first taking it away from the conflicting actions when asked to.</summary>
    public void AddShortcut(KeymapNode action, Shortcut shortcut, bool removeConflicts)
    {
        ArgumentNullException.ThrowIfNull(action);
        ArgumentNullException.ThrowIfNull(shortcut);
        if (action.ActionId is not { } id)
        {
            return;
        }

        if (removeConflicts)
        {
            _shortcuts.RemoveConflicts(id, shortcut);
        }

        _shortcuts.AddShortcut(id, shortcut);
    }

    /// <summary>Removes one shortcut of the action.</summary>
    public void RemoveShortcut(KeymapNode action, Shortcut shortcut)
    {
        ArgumentNullException.ThrowIfNull(action);
        if (action.ActionId is { } id)
        {
            _shortcuts.RemoveShortcut(id, shortcut);
        }
    }

    /// <summary>Removes all shortcuts of the action.</summary>
    public void RemoveAllShortcuts(KeymapNode action)
    {
        ArgumentNullException.ThrowIfNull(action);
        if (action.ActionId is { } id)
        {
            _shortcuts.RemoveAllShortcuts(id);
        }
    }

    /// <summary>Restores the default shortcuts of the action.</summary>
    public void ResetShortcuts(KeymapNode action)
    {
        ArgumentNullException.ThrowIfNull(action);
        if (action.ActionId is { } id)
        {
            _shortcuts.ResetToDefault(id);
        }
    }

    /// <summary>Restores the default shortcuts of all actions.</summary>
    public void ResetAll() => _shortcuts.ResetAll();

    /// <summary>The action as "name (group path)" — for conflicts.</summary>
    public string LabelOf(string actionId) =>
        _nodesById.TryGetValue(actionId, out List<KeymapNode>? nodes)
            ? nodes[0].Path is { Length: > 0 } path ? $"{nodes[0].Name} ({path})" : nodes[0].Name
            : actionId;

    partial void OnFilterChanged(string value) => ApplyFilter();

    partial void OnShortcutFilterChanged(Shortcut? value) => ApplyFilter();

    private void ApplyFilter()
    {
        string needle = Filter.Trim();
        HashSet<string>? withShortcut = ShortcutFilter is { } filter
            ? _shortcuts.FindActions(filter).ToHashSet(StringComparer.Ordinal)
            : null;
        bool filtering = needle.Length > 0 || withShortcut is not null;
        foreach (KeymapNode root in Roots)
        {
            Apply(root);
        }

        bool Apply(KeymapNode node)
        {
            if (!node.IsGroup)
            {
                node.IsVisible = node.Matches(needle) && (withShortcut is null || withShortcut.Contains(node.ActionId!));
                return node.IsVisible;
            }

            bool any = false;
            foreach (KeymapNode child in node.Children)
            {
                any |= Apply(child);
            }

            node.IsVisible = any || !filtering;
            if (filtering && any)
            {
                node.IsExpanded = true;
            }

            return node.IsVisible;
        }
    }

    private void AddMenuNode(KeymapNode parent, MenuNode node)
    {
        switch (node)
        {
            case MenuNode.ActionNode { Id: var id } when _actions.Get(id) is { } action:
                parent.Children.Add(ActionNode(action, parent));
                break;
            case MenuNode.SubmenuNode submenu when submenu.Children.Count > 0:
                KeymapNode group = Group(
                    ActionTexts.MenuHeader(submenu.Header).Replace("&", string.Empty, StringComparison.Ordinal),
                    parent.Path is null ? parent.Name : $"{parent.Path} | {parent.Name}");
                foreach (MenuNode child in submenu.Children)
                {
                    AddMenuNode(group, child);
                }

                if (group.Children.Count > 0)
                {
                    parent.Children.Add(group);
                }

                break;
        }
    }

    private static KeymapNode Group(string name, string? path) => new(name, actionId: null, path);

    private KeymapNode ActionNode(AppAction action, KeymapNode parent)
    {
        KeymapNode node = new(NameOf(action), action.Id, parent.Path is null ? parent.Name : $"{parent.Path} | {parent.Name}");
        node.Update(_shortcuts.Get(action.Id));
        if (!_nodesById.TryGetValue(action.Id, out List<KeymapNode>? nodes))
        {
            nodes = new List<KeymapNode>();
            _nodesById[action.Id] = nodes;
        }

        nodes.Add(node);
        return node;
    }

    // The live text (Clip Bar slots carry the clip's name), without the mnemonic and the ellipsis.
    private static string NameOf(AppAction action) =>
        action.Text.Replace("_", string.Empty, StringComparison.Ordinal).TrimEnd('.', '…').Trim();

    private void RefreshAction(string id)
    {
        if (_nodesById.TryGetValue(id, out List<KeymapNode>? nodes))
        {
            foreach (KeymapNode node in nodes)
            {
                node.Update(_shortcuts.Get(id));
            }
        }

        if (ShortcutFilter is not null)
        {
            ApplyFilter();
        }
    }

    private void ForEachGroup(Action<KeymapNode> apply)
    {
        foreach (KeymapNode root in Roots)
        {
            Walk(root);
        }

        void Walk(KeymapNode node)
        {
            if (!node.IsGroup)
            {
                return;
            }

            apply(node);
            foreach (KeymapNode child in node.Children)
            {
                Walk(child);
            }
        }
    }
}

/// <summary>A node of the keymap tree: a group or an action with its shortcuts.</summary>
public sealed partial class KeymapNode : ObservableObject
{
    internal KeymapNode(string name, string? actionId, string? path)
    {
        Name = name;
        ActionId = actionId;
        Path = path;
    }

    /// <summary>The group name or the action name.</summary>
    public string Name { get; }

    /// <summary>The action id; <c>null</c> for a group.</summary>
    public string? ActionId { get; }

    /// <summary>The names of the enclosing groups ("Main Menu | Edit"); <c>null</c> for a top-level group.</summary>
    public string? Path { get; }

    /// <summary>Whether the node is a group.</summary>
    public bool IsGroup => ActionId is null;

    /// <summary>The children of a group.</summary>
    public ObservableCollection<KeymapNode> Children { get; } = new();

    /// <summary>The action's current shortcuts.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ShortcutsText))]
    private IReadOnlyList<Shortcut> _shortcuts = Array.Empty<Shortcut>();

    /// <summary>Whether the action's shortcuts differ from the defaults (shown in the accent colour, as in IntelliJ).</summary>
    [ObservableProperty]
    private bool _isModified;

    /// <summary>Whether the action's shortcuts can be reset (they differ from the defaults).</summary>
    public bool CanReset => IsModified;

    /// <summary>Whether a group is expanded.</summary>
    [ObservableProperty]
    private bool _isExpanded;

    /// <summary>Whether the node passes the filters.</summary>
    [ObservableProperty]
    private bool _isVisible = true;

    /// <summary>The shortcuts as text, e.g. <c>Ctrl+S   Ctrl+K, Ctrl+S</c>.</summary>
    public string ShortcutsText => string.Join("   ", Shortcuts.Select(s => s.DisplayText));

    /// <summary>
    /// Whether the action matches the search text: its name or one of its shortcuts contains it (case-insensitive;
    /// spaces are ignored in the shortcut, so "ctrl + s" finds "Ctrl+S").
    /// </summary>
    public bool Matches(string needle)
    {
        if (string.IsNullOrEmpty(needle))
        {
            return true;
        }

        if (Name.Contains(needle, StringComparison.CurrentCultureIgnoreCase))
        {
            return true;
        }

        string compact = needle.Replace(" ", string.Empty, StringComparison.Ordinal);
        return Shortcuts.Any(s => s.DisplayText.Replace(" ", string.Empty, StringComparison.Ordinal)
            .Contains(compact, StringComparison.OrdinalIgnoreCase));
    }

    internal void Update(KeyboardShortcut? shortcut)
    {
        Shortcuts = shortcut?.Shortcuts ?? Array.Empty<Shortcut>();
        IsModified = shortcut?.IsOverridden ?? false;
        OnPropertyChanged(nameof(CanReset));
    }
}
