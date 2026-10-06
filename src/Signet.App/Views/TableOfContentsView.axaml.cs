using System.Diagnostics.CodeAnalysis;
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
        if (DataContext is TableOfContentsViewModel vm)
        {
            _columns = new LocalizedColumns<TocEntryViewModel>();
            _source = new HierarchicalTreeDataGridSource<TocEntryViewModel>(vm.Nodes)
            {
                Columns =
                {
                    _columns.Expander(
                        _columns.Text("EditTocWindow_ColumnTitle", n => n.Title, new GridLength(2, GridUnitType.Star)),
                        n => n.Children,
                        n => n.Children.Count > 0,
                        n => n.IsExpanded),
                    _columns.Text("EditTocWindow_ColumnTarget", n => n.TargetDisplay, new GridLength(1, GridUnitType.Star)),
                },
            };
        }

        Tree.Source = _source;
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
