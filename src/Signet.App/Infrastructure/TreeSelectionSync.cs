using System;
using System.Collections.Generic;
using Avalonia.Threading;
using Signet.Controls.TreeDataGrid;

namespace Signet.App.Infrastructure;

/// <summary>
/// Keeps the single row selection of a hierarchical <c>TreeDataGrid</c> source and a "selected node" property of a view
/// model in step (what <c>TreeView.SelectedItem</c> bound two-way used to do): a row selected in the tree sets the
/// property, and a node set by the view model (e.g. a newly added one) is selected in the tree by its index path.
/// </summary>
/// <remarks>
/// When the selected row disappears only for a moment — a node moved with <c>ObservableCollection.Move</c>, which the
/// selection model treats as a removal — and the view model still points at a node that is in the tree, that node is
/// selected again.
/// </remarks>
/// <typeparam name="TModel">The node type.</typeparam>
public sealed class TreeSelectionSync<TModel>
    where TModel : class
{
    private readonly HierarchicalTreeDataGridSource<TModel> _source;
    private readonly IEnumerable<TModel> _roots;
    private readonly Func<TModel, IEnumerable<TModel>> _children;
    private readonly Func<TModel?> _getSelected;
    private readonly Action<TModel?> _setSelected;
    private bool _syncing;

    /// <summary>Starts keeping <paramref name="source"/>'s selection and the view model's selected node in step.</summary>
    /// <param name="source">The tree's source.</param>
    /// <param name="roots">The top-level nodes (the source's items).</param>
    /// <param name="children">The children of a node.</param>
    /// <param name="getSelected">Reads the view model's selected node.</param>
    /// <param name="setSelected">Sets the view model's selected node.</param>
    public TreeSelectionSync(
        HierarchicalTreeDataGridSource<TModel> source,
        IEnumerable<TModel> roots,
        Func<TModel, IEnumerable<TModel>> children,
        Func<TModel?> getSelected,
        Action<TModel?> setSelected)
    {
        _source = source ?? throw new ArgumentNullException(nameof(source));
        _roots = roots ?? throw new ArgumentNullException(nameof(roots));
        _children = children ?? throw new ArgumentNullException(nameof(children));
        _getSelected = getSelected ?? throw new ArgumentNullException(nameof(getSelected));
        _setSelected = setSelected ?? throw new ArgumentNullException(nameof(setSelected));
        _source.RowSelection!.SelectionChanged += (_, _) => OnTreeSelectionChanged();
    }

    /// <summary>Selects the view model's selected node in the tree (call when that property changes).</summary>
    public void SelectFromViewModel()
    {
        TModel? node = _getSelected();
        if (_source.RowSelection is not { } selection || ReferenceEquals(selection.SelectedItem, node))
        {
            return;
        }

        _syncing = true;
        try
        {
            if (node is not null && FindPath(node) is { } path)
            {
                selection.SelectedIndex = new IndexPath(path);
            }
            else
            {
                selection.Clear();
            }
        }
        finally
        {
            _syncing = false;
        }
    }

    /// <summary>The index path of <paramref name="node"/> in the tree, or <c>null</c> when it is not in it.</summary>
    public IReadOnlyList<int>? FindPath(TModel node) => FindPath(_roots, node, new List<int>());

    private void OnTreeSelectionChanged()
    {
        if (_syncing)
        {
            return;
        }

        TModel? selected = _source.RowSelection!.SelectedItem;
        if (selected is null && _getSelected() is { } current && FindPath(current) is not null)
        {
            // The row went away while its node is still in the tree (moved): select it again once the
            // collection change is over.
            Dispatcher.UIThread.Post(SelectFromViewModel);
            return;
        }

        _setSelected(selected);
    }

    private List<int>? FindPath(IEnumerable<TModel> nodes, TModel target, List<int> prefix)
    {
        int index = 0;
        foreach (TModel node in nodes)
        {
            List<int> path = new(prefix) { index };
            if (ReferenceEquals(node, target))
            {
                return path;
            }

            if (FindPath(_children(node), target, path) is { } found)
            {
                return found;
            }

            index++;
        }

        return null;
    }
}
