using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using CommunityToolkit.Mvvm.Input;
using Signet.App.Infrastructure;
using Signet.App.ViewModels;
using Signet.Controls.TreeDataGrid;
using Signet.Controls.TreeDataGrid.Models;

namespace Signet.App.Views;

/// <summary>
/// "Find Usages" panel view (<see cref="FindUsagesViewModel"/>): a left toolbar (Refresh, Group By, Expand All, Collapse
/// All) and the tree of the usages (Usage, Line, Col., Kind, Context); double-clicking (or Enter on) a usage opens the
/// file with the caret on it. Ctrl+NumPad + / Ctrl+NumPad - in the panel expand / collapse all, as in the JetBrains
/// IDEs.
/// </summary>
/// <remarks>
/// The tree is a virtualizing <see cref="TreeDataGrid"/> over <see cref="FindUsagesViewModel.Roots"/>, built here (a
/// TreeDataGrid source is bound to the UI thread). The keys of a tree view are handled here: Right / NumPad + expand the
/// selected node (Right on an expanded one goes to its first child), Left / NumPad - collapse it (Left on a collapsed
/// node or a usage goes to the parent); double-clicking a file or the root expands / collapses it.
/// </remarks>
[SuppressMessage(
    "Reliability",
    "CA1001:Types that own disposable fields should be disposable",
    Justification = "The tree source is disposed when the data context changes; the current one lives as long as the "
        + "view and the view model's Roots it observes. The view is not disposed on detach: Dock re-parents panels.")]
public partial class FindUsagesView : UserControl
{
    // Keeps the column headers in the current UI language (held weakly by Strings).
    private LocalizedColumns<FindUsagesNode>? _columns;
    private HierarchicalTreeDataGridSource<FindUsagesNode>? _source;

    /// <summary>Initializes the view.</summary>
    public FindUsagesView()
    {
        InitializeComponent();
        DataContextChanged += (_, _) => BuildTree();

        // Tunnel: the tree handles NumPad +/- itself (expanding / collapsing the selected node).
        AddHandler(KeyDownEvent, OnPanelKeyDown, RoutingStrategies.Tunnel);

        // Tunnel: before the grid's own key handling.
        UsageList.AddHandler(KeyDownEvent, OnListKeyDown, RoutingStrategies.Tunnel);
    }

    private FindUsagesNode? SelectedNode => _source?.RowSelection?.SelectedItem;

    private void BuildTree()
    {
        _source?.Dispose();
        _source = null;
        if (DataContext is FindUsagesViewModel vm)
        {
            _columns = new LocalizedColumns<FindUsagesNode>();
            _source = new HierarchicalTreeDataGridSource<FindUsagesNode>(vm.Roots)
            {
                Columns =
                {
                    _columns.Expander(
                        _columns.Template(
                            "FindUsages_ColumnUsage",
                            "UsageCellTemplate",
                            new GridLength(2, GridUnitType.Star),
                            new TemplateColumnOptions<FindUsagesNode>
                            {
                                CompareAscending = FindUsagesNode.CompareByLocation,
                                CompareDescending = (a, b) => FindUsagesNode.CompareByLocation(b, a),
                            }),
                        n => n.Children,
                        n => n.HasChildren,
                        n => n.IsExpanded),
                    _columns.Text("ValidationResultsView_Line", n => n.Line, new GridLength(70)),
                    _columns.Text("FindUsages_ColumnColumn", n => n.Column, new GridLength(60)),
                    _columns.Text("FindUsages_ColumnKind", n => n.KindText, new GridLength(120)),
                    _columns.Text("FindUsages_ColumnContext", n => n.Context, new GridLength(3, GridUnitType.Star)),
                },
            };
        }

        UsageList.Source = _source;
    }

    private void OnListDoubleTapped(object? sender, TappedEventArgs e)
    {
        if (SelectedNode is { HasChildren: true } node)
        {
            node.IsExpanded = !node.IsExpanded;
            return;
        }

        ActivateSelected();
    }

    private void OnListKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.KeyModifiers != KeyModifiers.None || SelectedNode is not { } node)
        {
            return;
        }

        switch (e.Key)
        {
            case Key.Enter:
                e.Handled = ActivateSelected();
                break;
            case Key.Right or Key.Add when node.HasChildren:
                if (node.IsExpanded && e.Key == Key.Right)
                {
                    Select(node.Children[0]);
                }

                node.IsExpanded = true;
                e.Handled = true;
                break;
            case Key.Left or Key.Subtract:
                if (node is { HasChildren: true, IsExpanded: true })
                {
                    node.IsExpanded = false;
                }
                else if (e.Key == Key.Left && node.Parent is { } parent)
                {
                    Select(parent);
                }

                e.Handled = true;
                break;
        }
    }

    // Selects a node (by its index path), scrolls it into view and moves the keyboard focus to it, so that Up / Down
    // continue from the new row.
    private void Select(FindUsagesNode node)
    {
        if (_source?.RowSelection is not { } selection)
        {
            return;
        }

        List<int> path = new();
        for (FindUsagesNode current = node; current.Parent is { } parent; current = parent)
        {
            path.Insert(0, IndexOf(parent.Children, current));
        }

        path.Insert(0, 0);
        IndexPath index = new(path);
        selection.SelectedIndex = index;
        if (UsageList.Rows?.ModelIndexToRowIndex(index) is >= 0 and var row)
        {
            UsageList.RowsPresenter?.BringIntoView(row);
            (UsageList.TryGetRow(row)?.TryGetCell(0) as Control)?.Focus(NavigationMethod.Directional);
        }
    }

    private static int IndexOf(IReadOnlyList<FindUsagesNode> nodes, FindUsagesNode node)
    {
        for (int i = 0; i < nodes.Count; i++)
        {
            if (ReferenceEquals(nodes[i], node))
            {
                return i;
            }
        }

        return -1;
    }

    private void OnPanelKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.KeyModifiers == KeyModifiers.Control && e.Key is Key.Add or Key.Subtract
            && DataContext is FindUsagesViewModel vm)
        {
            IRelayCommand command = e.Key == Key.Add ? vm.ExpandAllCommand : vm.CollapseAllCommand;
            command.Execute(null);
            e.Handled = true;
        }
    }

    private bool ActivateSelected()
    {
        if (DataContext is FindUsagesViewModel vm && SelectedNode is { Usage: not null } node)
        {
            vm.Activate(node);
            return true;
        }

        return false;
    }
}
