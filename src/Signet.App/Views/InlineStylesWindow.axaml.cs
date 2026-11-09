using System;
using System.Collections.Specialized;
using System.Diagnostics.CodeAnalysis;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Input;
using Signet.App.Infrastructure;
using Signet.App.ViewModels;
using Signet.Controls.TreeDataGrid;

namespace Signet.App.Views;

/// <summary>
/// The modal "Extract Inline Styles to Classes" dialog (<see cref="InlineStylesViewModel"/>): the values of the <c>style</c>
/// attribute that repeat, as a tree of the classes they would become (with the files and elements they come from) with check
/// boxes. "Extract" turns the checked classes into classes of the stylesheet and closes the dialog; a double click opens
/// the place in the main window.
/// </summary>
[SuppressMessage(
    "Reliability",
    "CA1001:Types that own disposable fields should be disposable",
    Justification = "The tree source is disposed when the tree is rebuilt and when the window closes, like in the other TreeDataGrid views.")]
public partial class InlineStylesWindow : Window
{
    private InlineStylesViewModel? _bound;
    private LocalizedColumns<InlineStyleNode>? _columns;
    private HierarchicalTreeDataGridSource<InlineStyleNode>? _source;

    /// <summary>Initializes the window.</summary>
    public InlineStylesWindow()
    {
        InitializeComponent();
        CloseButton.Click += (_, _) => Close();
        DataContextChanged += (_, _) => Bind();
        Closed += (_, _) =>
        {
            Unbind();
            Tree.Source = null;
            _source?.Dispose();
            _source = null;
        };
    }

    /// <summary>Shows the dialog over <paramref name="owner"/>.</summary>
    public static Task ShowAsync(Window owner, InlineStylesViewModel viewModel) =>
        new InlineStylesWindow { DataContext = viewModel }.ShowDialog(owner);

    private void Bind()
    {
        Unbind();
        _bound = DataContext as InlineStylesViewModel;
        if (_bound is not null)
        {
            _bound.Roots.CollectionChanged += OnRootsChanged;
            _bound.Done += OnDone;
        }

        BuildTree();
    }

    private void Unbind()
    {
        if (_bound is not null)
        {
            _bound.Roots.CollectionChanged -= OnRootsChanged;
            _bound.Done -= OnDone;
            _bound = null;
        }
    }

    private void OnDone(object? sender, EventArgs e) => Close();

    // A new minimum builds the tree again (the rows expand as they are created).
    private void OnRootsChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        if (e.Action == NotifyCollectionChangedAction.Reset)
        {
            BuildTree();
        }
    }

    // The source of the tree (TreeDataGrid sources belong to the views).
    private void BuildTree()
    {
        HierarchicalTreeDataGridSource<InlineStyleNode>? previous = _source;
        _source = null;
        if (_bound is { } vm)
        {
            _columns ??= new LocalizedColumns<InlineStyleNode>();
            _source = new HierarchicalTreeDataGridSource<InlineStyleNode>(vm.Roots)
            {
                Columns =
                {
                    _columns.Expander(
                        _columns.Template("InlineStylesWindow_ColumnClass", "InlineStylesTitleCellTemplate", new GridLength(380)),
                        n => n.Children,
                        n => n.HasChildren,
                        n => n.IsExpanded),
                    _columns.Template("InlineStylesWindow_ColumnValue", "InlineStylesPlaceCellTemplate", new GridLength(1, GridUnitType.Star)),
                },
            };
        }

        Tree.Source = _source;
        previous?.Dispose();
    }

    private void OnRowDoubleTapped(object? sender, TappedEventArgs e)
    {
        if (sender is Control { DataContext: InlineStyleNode node })
        {
            node.Open();
            e.Handled = true;
        }
    }
}
