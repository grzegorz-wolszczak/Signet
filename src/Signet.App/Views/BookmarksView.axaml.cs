using System.Diagnostics.CodeAnalysis;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Signet.App.Infrastructure;
using Signet.App.ViewModels;
using Signet.Controls.TreeDataGrid;

namespace Signet.App.Views;

/// <summary>
/// "Bookmarks" panel view (<see cref="BookmarksViewModel"/>): a left toolbar (delete the selected bookmark, delete
/// all) and the tree of the bookmarks by file; double-clicking or pressing Enter on a bookmark goes to it, Delete
/// removes it.
/// </summary>
/// <remarks>
/// The tree is a <see cref="TreeDataGrid"/> over <see cref="BookmarksViewModel.Roots"/>, built here (a TreeDataGrid
/// source is bound to the UI thread); the source follows the changes of the nodes itself.
/// </remarks>
[SuppressMessage(
    "Reliability",
    "CA1001:Types that own disposable fields should be disposable",
    Justification = "The tree source is disposed when the data context changes; the current one lives as long as the "
        + "view and the view model's Roots it observes. The view is not disposed on detach: Dock re-parents panels.")]
public partial class BookmarksView : UserControl
{
    // Keeps the column headers in the current UI language (held weakly by Strings).
    private LocalizedColumns<BookmarkNode>? _columns;
    private HierarchicalTreeDataGridSource<BookmarkNode>? _source;
    private TreeSelectionSync<BookmarkNode>? _selection;
    private BookmarksViewModel? _bound;

    /// <summary>Initializes the view.</summary>
    public BookmarksView()
    {
        InitializeComponent();
        DataContextChanged += (_, _) => OnViewModelChanged();
        Tree.DoubleTapped += OnActivate;
        Tree.AddHandler(KeyDownEvent, OnKeyDown, RoutingStrategies.Tunnel);
    }

    private void OnViewModelChanged()
    {
        _bound = DataContext as BookmarksViewModel;
        BuildTree();
    }

    private void BuildTree()
    {
        _source?.Dispose();
        _source = null;
        _selection = null;
        if (_bound is { } vm)
        {
            _columns = new LocalizedColumns<BookmarkNode>();
            _source = new HierarchicalTreeDataGridSource<BookmarkNode>(vm.Roots)
            {
                Columns =
                {
                    _columns.Expander(
                        _columns.Template("Panel_Bookmarks", "BookmarkTextCellTemplate", new GridLength(1, GridUnitType.Star)),
                        n => n.Children,
                        n => n.Children.Count > 0,
                        n => n.IsExpanded),
                },
            };
            _selection = new TreeSelectionSync<BookmarkNode>(_source, vm.Roots, n => n.Children, () => vm.Selected, n => vm.Selected = n);
        }

        Tree.Source = _source;
    }

    private void OnKeyDown(object? sender, KeyEventArgs e)
    {
        switch (e.Key)
        {
            case Key.Enter:
                OnActivate(sender, e);
                e.Handled = true;
                break;
            case Key.Delete:
                _bound?.DeleteCommand.Execute(null);
                e.Handled = true;
                break;
        }
    }

    private void OnActivate(object? sender, RoutedEventArgs e)
    {
        if (_bound is { } vm && _source?.RowSelection?.SelectedItem is { } node)
        {
            vm.Activate(node);
        }
    }
}
