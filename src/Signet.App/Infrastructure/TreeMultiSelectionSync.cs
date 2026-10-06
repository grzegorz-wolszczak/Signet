using System;
using System.Collections.Generic;
using System.Linq;
using Avalonia.Threading;
using Signet.Controls.TreeDataGrid;

namespace Signet.App.Infrastructure;

/// <summary>
/// The multi-selection counterpart of <see cref="TreeSelectionSync{TModel}"/>: keeps the selected rows of a hierarchical
/// <c>TreeDataGrid</c> source (<c>SingleSelect = false</c>) and a view model's selected nodes in step.
/// </summary>
/// <remarks>
/// When the selection is emptied by the tree itself — a reset of the rows (e.g. by a <see cref="VisibleItemsView{T}"/>
/// after a node was added or the filter changed) or a moved node — while the view model's selected nodes still exist,
/// the view model keeps them and the tree selects again those it shows. A node hidden by a filter thus stays selected,
/// as in a <c>TreeView</c>, and is selected in the tree again when the filter shows it.
/// </remarks>
/// <typeparam name="TModel">The node type.</typeparam>
public sealed class TreeMultiSelectionSync<TModel>
    where TModel : class
{
    private readonly HierarchicalTreeDataGridSource<TModel> _source;
    private readonly IEnumerable<TModel> _roots;
    private readonly Func<TModel, IEnumerable<TModel>> _children;
    private readonly Func<IReadOnlyList<TModel>> _getSelected;
    private readonly Action<IReadOnlyList<TModel>> _setSelected;
    private readonly Func<TModel, bool>? _exists;
    private bool _syncing;
    private bool _resyncPosted;

    /// <summary>Starts keeping <paramref name="source"/>'s selection and the view model's selected nodes in step.</summary>
    /// <param name="source">The tree's source (multi-selection is switched on).</param>
    /// <param name="roots">The top-level nodes (the source's items).</param>
    /// <param name="children">The children of a node, as the source shows them.</param>
    /// <param name="getSelected">Reads the view model's selected nodes.</param>
    /// <param name="setSelected">Sets the view model's selected nodes.</param>
    /// <param name="exists">
    /// Whether a node still exists in the data, also when the tree does not show it (filtered out); by default, whether
    /// the tree shows it.
    /// </param>
    public TreeMultiSelectionSync(
        HierarchicalTreeDataGridSource<TModel> source,
        IEnumerable<TModel> roots,
        Func<TModel, IEnumerable<TModel>> children,
        Func<IReadOnlyList<TModel>> getSelected,
        Action<IReadOnlyList<TModel>> setSelected,
        Func<TModel, bool>? exists = null)
    {
        _source = source ?? throw new ArgumentNullException(nameof(source));
        _roots = roots ?? throw new ArgumentNullException(nameof(roots));
        _children = children ?? throw new ArgumentNullException(nameof(children));
        _getSelected = getSelected ?? throw new ArgumentNullException(nameof(getSelected));
        _setSelected = setSelected ?? throw new ArgumentNullException(nameof(setSelected));
        _exists = exists;
        _source.RowSelection!.SingleSelect = false;
        _source.RowSelection.SelectionChanged += (_, _) => OnTreeSelectionChanged();

        // Rows shown again (a filter relaxed, a reset after an edit) may be selected nodes of the view model.
        if (_source.Rows is System.Collections.Specialized.INotifyCollectionChanged rows)
        {
            rows.CollectionChanged += (_, _) => PostResync();
        }
    }

    /// <summary>Selects exactly the view model's selected nodes (those in the tree) in the tree.</summary>
    public void SelectFromViewModel()
    {
        if (_source.RowSelection is not { } selection)
        {
            return;
        }

        List<IReadOnlyList<int>> paths = _getSelected()
            .Select(FindPath)
            .Where(p => p is not null)
            .Select(p => p!)
            .ToList();
        _syncing = true;
        selection.BeginBatchUpdate();
        try
        {
            selection.Clear();
            foreach (IReadOnlyList<int> path in paths)
            {
                selection.Select(new IndexPath(path));
            }
        }
        finally
        {
            selection.EndBatchUpdate();
            _syncing = false;
        }
    }

    // Once the current changes are over: select in the tree the view model's shown nodes if the tree differs.
    private void PostResync()
    {
        if (_resyncPosted)
        {
            return;
        }

        _resyncPosted = true;
        Dispatcher.UIThread.Post(() =>
        {
            _resyncPosted = false;
            HashSet<object> shown = _getSelected().Where(n => FindPath(n) is not null)
                .ToHashSet<object>(ReferenceEqualityComparer.Instance);
            HashSet<object> selected = _source.RowSelection!.SelectedItems.OfType<TModel>()
                .ToHashSet<object>(ReferenceEqualityComparer.Instance);
            if (!shown.SetEquals(selected))
            {
                SelectFromViewModel();
            }
        });
    }

    /// <summary>The index path of <paramref name="node"/> in the tree, or <c>null</c> when it is not shown in it.</summary>
    public IReadOnlyList<int>? FindPath(TModel node) => FindPath(_roots, node, new List<int>());

    private void OnTreeSelectionChanged()
    {
        if (_syncing)
        {
            return;
        }

        List<TModel> selected = _source.RowSelection!.SelectedItems.OfType<TModel>().ToList();
        if (selected.Count == 0 && _getSelected().Any(n => _exists?.Invoke(n) ?? FindPath(n) is not null))
        {
            // Emptied by a reset / move while the selected nodes still exist: keep them in the view model and select
            // again those the tree shows once the collection change is over.
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
