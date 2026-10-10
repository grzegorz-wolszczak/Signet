using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Linq;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Signet.App.Infrastructure;
using Signet.App.ViewModels;
using Signet.Controls.TreeDataGrid;

namespace Signet.App.Views;

/// <summary>
/// "Table Of Contents" panel view — the TOC tree (Title, Target); double-clicking or pressing Enter on an entry
/// navigates to the target file and fragment.
/// </summary>
/// <remarks>
/// The tree is a <see cref="TreeDataGrid"/> over <see cref="TableOfContentsViewModel.Nodes"/>, built here (a TreeDataGrid
/// source is bound to the UI thread); every entry starts expanded (<see cref="TocEntryViewModel.IsExpanded"/>).
/// </remarks>
[SuppressMessage(
    "Reliability",
    "CA1001:Types that own disposable fields should be disposable",
    Justification = "The tree source is disposed when the data context changes; the current one lives as long as the "
        + "view and the view model's Nodes it observes. The view is not disposed on detach: Dock re-parents panels.")]
public partial class TableOfContentsView : UserControl
{
    // Keeps the column headers in the current UI language (held weakly by Strings).
    private LocalizedColumns<TocEntryViewModel>? _columns;
    private HierarchicalTreeDataGridSource<TocEntryViewModel>? _source;
    private TableOfContentsViewModel? _bound;

    /// <summary>Initializes the view.</summary>
    public TableOfContentsView()
    {
        InitializeComponent();
        DataContextChanged += (_, _) => BuildTree();
        Tree.DoubleTapped += OnActivate;
        Tree.AddHandler(KeyDownEvent, OnKeyDown, RoutingStrategies.Tunnel);
    }

    private void BuildTree()
    {
        _source?.Dispose();
        _source = null;
        if (_bound is not null)
        {
            _bound.RevealRequested -= OnRevealRequested;
        }

        _bound = DataContext as TableOfContentsViewModel;
        if (_bound is { } vm)
        {
            vm.RevealRequested += OnRevealRequested;
            _columns = new LocalizedColumns<TocEntryViewModel>();
            _source = new HierarchicalTreeDataGridSource<TocEntryViewModel>(vm.Nodes)
            {
                Columns =
                {
                    _columns.Expander(
                        _columns.Template("EditTocWindow_ColumnTitle", "TocTitleCellTemplate", new GridLength(2, GridUnitType.Star)),
                        n => n.Children,
                        n => n.Children.Count > 0,
                        n => n.IsExpanded),
                    _columns.Text("EditTocWindow_ColumnTarget", n => n.TargetDisplay, new GridLength(1, GridUnitType.Star)),
                },
            };
        }

        Tree.Source = _source;
    }

    // Expands the branches down to the entries and scrolls to the first one, without changing the selection.
    private void OnRevealRequested(object? sender, IReadOnlyList<IReadOnlyList<int>> paths)
    {
        if (_source is null || paths.Count == 0)
        {
            return;
        }

        foreach (IReadOnlyList<int> path in paths)
        {
            for (int depth = 1; depth < path.Count; depth++)
            {
                _source.Expand(new IndexPath(path.Take(depth)));
            }
        }

        if (Tree.Rows?.ModelIndexToRowIndex(new IndexPath(paths[0])) is >= 0 and var row)
        {
            Tree.RowsPresenter?.BringIntoView(row);
        }
    }

    private void OnKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter)
        {
            OnActivate(sender, e);
        }
    }

    private void OnActivate(object? sender, RoutedEventArgs e)
    {
        if (DataContext is TableOfContentsViewModel vm && _source?.RowSelection?.SelectedItem is { } node)
        {
            vm.Activate(node);
        }
    }
}
