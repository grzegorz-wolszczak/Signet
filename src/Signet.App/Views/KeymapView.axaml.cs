using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Linq;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Signet.App.Infrastructure;
using Signet.App.Input;
using Signet.App.Resources;
using Signet.App.ViewModels;
using Signet.Controls.TreeDataGrid;

namespace Signet.App.Views;

/// <summary>
/// The keymap editor (<see cref="KeymapViewModel"/>). The tree's source is built here (TreeDataGrid sources belong to
/// the views); the dialogs for a new shortcut and the conflict question are shown from here.
/// </summary>
[SuppressMessage(
    "Reliability",
    "CA1001:Types that own disposable fields should be disposable",
    Justification = "The tree source is disposed when the DataContext changes, like in the other TreeDataGrid views.")]
public partial class KeymapView : UserControl
{
    private KeymapViewModel? _bound;
    private LocalizedColumns<KeymapNode>? _columns;
    private HierarchicalTreeDataGridSource<KeymapNode>? _source;
    private TreeSelectionSync<KeymapNode>? _selection;

    /// <summary>Initializes the view.</summary>
    public KeymapView()
    {
        InitializeComponent();
        DataContextChanged += (_, _) => BuildTree();
        Tree.AddHandler(KeyDownEvent, OnTreeKeyDown, RoutingStrategies.Tunnel);
        Tree.ContextRequested += OnTreeContextRequested;
        FindFirstStroke.GestureChanged += (_, _) => ApplyKeyboardFilter();
        FindSecondStroke.GestureChanged += (_, _) => ApplyKeyboardFilter();
        FindSecondEnabled.IsCheckedChanged += (_, _) => ApplyKeyboardFilter();
        FindMousePad.Hint = Strings.Get("Keymap_MouseShortcutPad");
        FindMousePad.ShortcutChanged += (_, _) =>
        {
            if (_bound is not null && FindMousePad.Shortcut is { } mouse)
            {
                FindFirstStroke.SetGesture(null);
                _bound.ShortcutFilter = mouse;
            }
        };
    }

    /// <summary>The visible children of a node (the filter's result).</summary>
    private static VisibleItemsView<KeymapNode> Visible(System.Collections.ObjectModel.ObservableCollection<KeymapNode> nodes) =>
        VisibleItems.For(nodes, n => n.IsVisible, nameof(KeymapNode.IsVisible));

    private void BuildTree()
    {
        if (_bound is not null)
        {
            _bound.PropertyChanged -= OnViewModelPropertyChanged;
        }

        _source?.Dispose();
        _source = null;
        _selection = null;
        _bound = DataContext as KeymapViewModel;
        if (_bound is not null)
        {
            KeymapViewModel vm = _bound;
            vm.PropertyChanged += OnViewModelPropertyChanged;
            _columns = new LocalizedColumns<KeymapNode>();
            VisibleItemsView<KeymapNode> roots = Visible(vm.Roots);
            _source = new HierarchicalTreeDataGridSource<KeymapNode>(roots)
            {
                Columns =
                {
                    _columns.Expander(
                        _columns.Template("Keymap_ActionColumn", "KeymapNameCellTemplate", new GridLength(1, GridUnitType.Star)),
                        n => Visible(n.Children),
                        n => n.IsGroup,
                        n => n.IsExpanded),
                },
            };
            _selection = new TreeSelectionSync<KeymapNode>(_source, roots, n => Visible(n.Children), () => vm.Selected, n => vm.Selected = n);
        }

        Tree.Source = _source;
        _selection?.SelectFromViewModel();
    }

    private void OnViewModelPropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(KeymapViewModel.Selected))
        {
            _selection?.SelectFromViewModel();
        }
    }

    private void OnExpandAllClicked(object? sender, RoutedEventArgs e) => _bound?.ExpandAll();

    private void OnCollapseAllClicked(object? sender, RoutedEventArgs e) => _bound?.CollapseAll();

    private void OnEditClicked(object? sender, RoutedEventArgs e)
    {
        if (_bound?.Selected is { IsGroup: false } node && sender is Control button)
        {
            ShowEditMenu(node, button);
        }
    }

    private void OnFindShortcutClicked(object? sender, RoutedEventArgs e)
    {
        FindPopup.IsOpen = true;
        FindFirstStroke.Focus();
    }

    private void OnClearShortcutFilterClicked(object? sender, RoutedEventArgs e)
    {
        FindFirstStroke.SetGesture(null);
        FindSecondStroke.SetGesture(null);
        FindMousePad.SetShortcut(null);
        if (_bound is not null)
        {
            _bound.ShortcutFilter = null;
        }
    }

    private async void OnResetAllClicked(object? sender, RoutedEventArgs e)
    {
        if (_bound is not null && TopLevel.GetTopLevel(this) is Window owner
            && await ConfirmWindow.AskAsync(owner, Strings.Get("PreferencesWindow_ResetAll"), Strings.Get("Keymap_ResetAllConfirm")))
        {
            _bound.ResetAll();
        }
    }

    private void ApplyKeyboardFilter()
    {
        if (_bound is null || FindFirstStroke.Gesture is not { } first)
        {
            return;
        }

        FindMousePad.SetShortcut(null);
        _bound.ShortcutFilter = new KeyStrokeShortcut(
            first, FindSecondEnabled.IsChecked == true ? FindSecondStroke.Gesture : null);
    }

    // A double click on an action opens "Edit Shortcuts" (on a group it only expands / collapses it).
    private void OnNodeDoubleTapped(object? sender, TappedEventArgs e)
    {
        if ((sender as Control)?.DataContext is KeymapNode { IsGroup: false } node && sender is Control anchor)
        {
            e.Handled = true;
            ShowEditMenu(node, anchor);
        }
    }

    private void OnTreeKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter && _bound?.Selected is { IsGroup: false } node)
        {
            e.Handled = true;
            ShowEditMenu(node, Tree);
        }
    }

    private void OnTreeContextRequested(object? sender, ContextRequestedEventArgs e)
    {
        if ((e.Source as StyledElement)?.DataContext is KeymapNode { IsGroup: false } node && e.Source is Control anchor)
        {
            e.Handled = true;
            if (_bound is not null)
            {
                _bound.Selected = node;
            }

            ShowEditMenu(node, anchor);
        }
    }

    // IntelliJ's "Edit Shortcuts" popup: add a keyboard / mouse shortcut, remove one or all, reset to the defaults.
    private void ShowEditMenu(KeymapNode node, Control anchor)
    {
        if (_bound is null)
        {
            return;
        }

        List<Control> items = new()
        {
            Item(Strings.Get("Keymap_AddKeyboardShortcut"), () => _ = AddAsync(node, keyboard: true)),
            Item(Strings.Get("Keymap_AddMouseShortcut"), () => _ = AddAsync(node, keyboard: false)),
        };

        if (node.Shortcuts.Count > 0)
        {
            items.Add(new Separator());
            foreach (Shortcut shortcut in node.Shortcuts)
            {
                Shortcut captured = shortcut;
                items.Add(Item(Strings.Format("Keymap_RemoveShortcut", shortcut.DisplayText), () => _bound.RemoveShortcut(node, captured)));
            }
        }

        if (node.Shortcuts.Count > 2 || node.CanReset)
        {
            items.Add(new Separator());
        }

        if (node.Shortcuts.Count > 2)
        {
            items.Add(Item(Strings.Get("Keymap_RemoveAllShortcuts"), () => _bound.RemoveAllShortcuts(node)));
        }

        if (node.CanReset)
        {
            items.Add(Item(Strings.Get("Keymap_ResetShortcuts"), () => _bound.ResetShortcuts(node)));
        }

        ContextMenu menu = new() { ItemsSource = items, Placement = PlacementMode.Pointer };
        menu.Open(anchor);

        static MenuItem Item(string header, Action run)
        {
            MenuItem item = new() { Header = header.Replace("_", "__", StringComparison.Ordinal) };
            item.Click += (_, _) => run();
            return item;
        }
    }

    // Asks for the shortcut and, when it conflicts with other actions, whether to take it away from them
    // (Remove), keep it on both (Leave) or give up (Cancel) — as IntelliJ does.
    private async Task AddAsync(KeymapNode node, bool keyboard)
    {
        if (_bound is not { } vm || TopLevel.GetTopLevel(this) is not Window owner)
        {
            return;
        }

        Func<Shortcut, IReadOnlyList<string>> conflicts = s => vm.ConflictsFor(node, s);
        Shortcut? shortcut = keyboard
            ? await KeyboardShortcutDialog.AskAsync(owner, node.Name, conflicts)
            : await MouseShortcutDialog.AskAsync(owner, node.Name, conflicts);
        if (shortcut is null)
        {
            return;
        }

        IReadOnlyList<string> found = vm.ConflictsFor(node, shortcut);
        bool removeConflicts = false;
        if (found.Count > 0)
        {
            int answer = await ChoiceWindow.AskAsync(
                owner,
                Strings.Get("Keymap_ConflictTitle"),
                Strings.Format("Keymap_ConflictMessage", shortcut.DisplayText, string.Join(Environment.NewLine, found.Select(f => "  • " + f))),
                Strings.Get("Keymap_ConflictRemove"),
                Strings.Get("Keymap_ConflictLeave"),
                Strings.Get("Common_Cancel"));
            if (answer is < 0 or 2)
            {
                return;
            }

            removeConflicts = answer == 0;
        }

        vm.AddShortcut(node, shortcut, removeConflicts);
    }
}
