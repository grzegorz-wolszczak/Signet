using System.ComponentModel;
using System.Diagnostics.CodeAnalysis;
using System.Linq;
using Avalonia.Controls;
using Avalonia.Input;
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
/// its multi-selection and the view model's selected nodes follow each other.
/// </remarks>
[SuppressMessage(
    "Reliability",
    "CA1001:Types that own disposable fields should be disposable",
    Justification = "The tree source is disposed when the data context changes; the current one lives as long as the "
        + "view and the view model's nodes it observes. The view is not disposed on detach: Dock re-parents panels.")]
public partial class ClipsPanelView : UserControl
{
    private ClipsViewModel? _bound;

    // Keeps the column headers in the current UI language (held weakly by Strings).
    private LocalizedColumns<ClipNodeViewModel>? _columns;
    private HierarchicalTreeDataGridSource<ClipNodeViewModel>? _source;
    private TreeMultiSelectionSync<ClipNodeViewModel>? _selection;

    /// <summary>Initializes the view.</summary>
    public ClipsPanelView()
    {
        InitializeComponent();
        DataContextChanged += (_, _) => BuildTree();
        Tree.DoubleTapped += OnDoubleTapped;
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
                        _columns.Text("ReportsWindow_Name", n => n.Name, new GridLength(1, GridUnitType.Star)),
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
        }

        Tree.Source = _source;
        _selection?.SelectFromViewModel();
    }

    private void OnViewModelPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(ClipsViewModel.SelectedNode))
        {
            _selection?.SelectFromViewModel();
        }
    }

    private void OnDoubleTapped(object? sender, TappedEventArgs e)
    {
        if (DataContext is ClipsViewModel vm && vm.PasteCommand.CanExecute(null))
        {
            vm.PasteCommand.Execute(null);
        }
    }
}
