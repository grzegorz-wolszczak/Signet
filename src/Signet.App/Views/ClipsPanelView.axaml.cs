using System.ComponentModel;
using System.Diagnostics.CodeAnalysis;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Signet.App.Infrastructure;
using Signet.App.ViewModels;
using Signet.Controls.TreeDataGrid;

namespace Signet.App.Views;

/// <summary>
/// The docked "Clips" panel. Browsing the tree of groups/clips (Name, Text);
/// a double click pastes the clip content into the active document (a double click, so that
/// keyboard navigation / a single selecting click do not paste by accident, consistent
/// with the "Load Search" convention in <see cref="SearchEditorView"/>).
/// </summary>
/// <remarks>
/// The tree is a <see cref="TreeDataGrid"/> built here (a TreeDataGrid source is bound to the UI thread) over the
/// visible nodes (<see cref="VisibleItemsView{T}"/> — the filter sets <see cref="ClipNodeViewModel.IsVisible"/>);
/// its multi-selection and the view model's selected nodes follow each other. A name is renamed in place with the
/// "Clip Editor → Rename" action (F2 by default, configurable in Preferences), only while this tree has the focus.
/// </remarks>
[SuppressMessage(
    "Reliability",
    "CA1001:Types that own disposable fields should be disposable",
    Justification = "The tree source is disposed when the data context changes; the current one lives as long as the "
        + "view and the view model's nodes it observes. The view is not disposed on detach: Dock re-parents panels.")]
public partial class ClipsPanelView : UserControl
{
    private const string RenameEditorClass = "inPlaceRename";

    private ClipsViewModel? _bound;
    private ClipNodeViewModel? _editingNode;
    private readonly ShortcutKeyBindings _keys;

    // Keeps the column headers in the current UI language (held weakly by Strings).
    private LocalizedColumns<ClipNodeViewModel>? _columns;
    private HierarchicalTreeDataGridSource<ClipNodeViewModel>? _source;
    private TreeMultiSelectionSync<ClipNodeViewModel>? _selection;

    /// <summary>Initializes the view.</summary>
    public ClipsPanelView()
    {
        InitializeComponent();
        _keys = new ShortcutKeyBindings(this);
        DataContextChanged += (_, _) => BuildTree();
        Tree.DoubleTapped += OnDoubleTapped;
        Tree.AddHandler(KeyDownEvent, OnTreeKeyDown, RoutingStrategies.Tunnel);
        Tree.AddHandler(LostFocusEvent, OnRenameEditorLostFocus, RoutingStrategies.Bubble);
    }

    /// <summary>The visible children of a node (the filter's result).</summary>
    internal static VisibleItemsView<ClipNodeViewModel> Visible(System.Collections.ObjectModel.ObservableCollection<ClipNodeViewModel> nodes) =>
        VisibleItems.For(nodes, n => n.IsVisible, nameof(ClipNodeViewModel.IsVisible));

    /// <summary>Whether <paramref name="node"/> is in the clips tree (also when the filter hides it).</summary>
    internal static bool Contains(System.Collections.Generic.IEnumerable<ClipNodeViewModel> nodes, ClipNodeViewModel node) =>
        nodes.Any(n => ReferenceEquals(n, node) || Contains(n.Children, node));

    private void BuildTree()
    {
        if (_bound is not null)
        {
            _bound.PropertyChanged -= OnViewModelPropertyChanged;
            _keys.Detach();
        }

        _source?.Dispose();
        _source = null;
        _selection = null;
        _bound = DataContext as ClipsViewModel;
        if (_bound is not null)
        {
            ClipsViewModel vm = _bound;
            _columns = new LocalizedColumns<ClipNodeViewModel>();
            VisibleItemsView<ClipNodeViewModel> roots = Visible(vm.Nodes);
            _source = new HierarchicalTreeDataGridSource<ClipNodeViewModel>(roots)
            {
                Columns =
                {
                    _columns.Expander(
                        _columns.Template("ReportsWindow_Name", "NameCellTemplate", new GridLength(1, GridUnitType.Star)),
                        n => Visible(n.Children),
                        n => n.IsGroup,
                        n => n.IsExpanded),
                    _columns.Text("ReportsWindow_Text", n => n.TextPreview, new GridLength(2, GridUnitType.Star)),
                },
            };
            _selection = new TreeMultiSelectionSync<ClipNodeViewModel>(
                _source, roots, n => Visible(n.Children), () => vm.SelectedNodes, vm.SetSelectedNodes,
                n => ClipsPanelView.Contains(vm.Nodes, n));
            vm.PropertyChanged += OnViewModelPropertyChanged;
            _keys.Attach(vm.ShortcutActions);
        }

        Tree.Source = _source;
        _selection?.SelectFromViewModel();
    }

    private void OnViewModelPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(ClipsViewModel.SelectedNode))
        {
            _selection?.SelectFromViewModel();
            return;
        }

        if (e.PropertyName != nameof(ClipsViewModel.EditingNode) || _bound is null)
        {
            return;
        }

        ClipNodeViewModel? finished = _editingNode;
        _editingNode = _bound.EditingNode;

        // Panel shortcuts must not act on the tree while a name is being typed.
        _keys.SetSuspended(_editingNode is not null);
        if (_editingNode is not null)
        {
            // The view model is shared with the Clip Editor window: only the tree with the focus (where the rename was
            // started) shows the editor — the other one must not cancel the rename.
            if (Tree.IsKeyboardFocusWithin)
            {
                // The editor becomes visible only after a layout pass — the focus is set afterwards.
                Dispatcher.UIThread.Post(FocusRenameEditor, DispatcherPriority.Loaded);
            }
        }
        else if (finished is not null && IsInRenameEditor(TopLevel.GetTopLevel(this)?.FocusManager?.GetFocusedElement()))
        {
            // The focus returns to the node row so that the arrows / the shortcut keep working in the tree.
            Tree.Focus();
        }
    }

    private void FocusRenameEditor()
    {
        TextBox? editor = Tree.GetVisualDescendants()
            .OfType<TextBox>()
            .FirstOrDefault(t => t.Classes.Contains(RenameEditorClass) && t.DataContext is ClipNodeViewModel { IsEditing: true });
        if (editor is null)
        {
            _bound?.CancelRename();
            return;
        }

        editor.Focus();
        editor.SelectAll();
    }

    private void OnTreeKeyDown(object? sender, KeyEventArgs e)
    {
        if (_bound is null || !IsInRenameEditor(e.Source))
        {
            return;
        }

        switch (e.Key)
        {
            case Key.Enter:
                e.Handled = true;
                _bound.CommitRename();
                break;
            case Key.Escape:
                e.Handled = true;
                _bound.CancelRename();
                break;
            case Key.Up or Key.Down or Key.PageUp or Key.PageDown:
                // Without this the tree would move the selection under the editor.
                e.Handled = true;
                break;
        }
    }

    private void OnRenameEditorLostFocus(object? sender, RoutedEventArgs e)
    {
        if (IsInRenameEditor(e.Source))
        {
            _bound?.CommitRename();
        }
    }

    private static bool IsInRenameEditor(object? source) =>
        source is Visual visual &&
        (visual as TextBox ?? visual.FindAncestorOfType<TextBox>())?.Classes.Contains(RenameEditorClass) == true;

    private void OnDoubleTapped(object? sender, TappedEventArgs e)
    {
        // A double click in the name editor selects a word — it does not paste.
        if (IsInRenameEditor(e.Source))
        {
            return;
        }

        if (DataContext is ClipsViewModel vm && vm.PasteCommand.CanExecute(null))
        {
            vm.PasteCommand.Execute(null);
        }
    }
}
