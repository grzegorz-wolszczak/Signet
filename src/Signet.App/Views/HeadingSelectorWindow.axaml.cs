using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics.CodeAnalysis;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Input;
using Signet.App.Infrastructure;
using Signet.App.ViewModels;
using Signet.Controls.TreeDataGrid;

namespace Signet.App.Views;

/// <summary>
/// The modal "Generate Table Of Contents" dialog — choosing the headings for the table of contents.
/// </summary>
/// <remarks>
/// The headings tree is a <see cref="TreeDataGrid"/> over <see cref="HeadingSelectorViewModel.Nodes"/> (Title edited
/// in place, Level, In TOC), built here (a TreeDataGrid source is bound to the UI thread); its selection and
/// <see cref="HeadingSelectorViewModel.SelectedNode"/> follow each other.
/// </remarks>
[SuppressMessage(
    "Reliability",
    "CA1001:Types that own disposable fields should be disposable",
    Justification = "The tree source is disposed when the data context changes; the current one lives as long as the "
        + "dialog and the view model's Nodes it observes.")]
public partial class HeadingSelectorWindow : Window
{
    private HeadingSelectorViewModel? _bound;

    // Keeps the column headers in the current UI language (held weakly by Strings).
    private LocalizedColumns<HeadingNodeViewModel>? _columns;
    private HierarchicalTreeDataGridSource<HeadingNodeViewModel>? _source;
    private bool _syncingSelection;

    /// <summary>Initializes the window.</summary>
    public HeadingSelectorWindow()
    {
        InitializeComponent();
        DataContextChanged += OnDataContextChanged;
        KeyDown += OnKeyDown;
    }

    /// <summary>
    /// Shows the dialog modally and returns <c>true</c> when the user accepted ("OK").
    /// </summary>
    public static async Task<bool> RunAsync(Window owner, HeadingSelectorViewModel viewModel)
    {
        var window = new HeadingSelectorWindow { DataContext = viewModel };
        await window.ShowDialog(owner);
        return viewModel.Accepted;
    }

    private void OnDataContextChanged(object? sender, EventArgs e)
    {
        if (_bound is not null)
        {
            _bound.CloseRequested -= OnCloseRequested;
            _bound.PropertyChanged -= OnViewModelPropertyChanged;
        }

        _source?.Dispose();
        _source = null;
        _bound = DataContext as HeadingSelectorViewModel;

        if (_bound is not null)
        {
            _bound.CloseRequested += OnCloseRequested;
            _bound.PropertyChanged += OnViewModelPropertyChanged;
            _source = BuildSource(_bound);
        }

        Tree.Source = _source;
        SelectInTree(_bound?.SelectedNode);
    }

    private HierarchicalTreeDataGridSource<HeadingNodeViewModel> BuildSource(HeadingSelectorViewModel vm)
    {
        _columns = new LocalizedColumns<HeadingNodeViewModel>();
        HierarchicalTreeDataGridSource<HeadingNodeViewModel> source = new(vm.Nodes)
        {
            Columns =
            {
                _columns.Expander(
                    _columns.Template("EditTocWindow_ColumnTitle", "TitleCellTemplate", new GridLength(1, GridUnitType.Star)),
                    n => n.Children,
                    n => n.Children.Count > 0,
                    n => n.IsExpanded),
                _columns.Text("EditTocWindow_ColumnLevel", n => n.LevelLabel, new GridLength(70)),
                _columns.Template("HeadingSelectorWindow_ColumnInToc", "IncludeCellTemplate", new GridLength(110)),
            },
        };
        source.RowSelection!.SelectionChanged += (_, _) =>
        {
            if (!_syncingSelection && _bound is not null)
            {
                _bound.SelectedNode = source.RowSelection.SelectedItem;
            }
        };
        return source;
    }

    private void OnViewModelPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(HeadingSelectorViewModel.SelectedNode))
        {
            SelectInTree(_bound?.SelectedNode);
        }
    }

    // Selects the row of a node (found by its index path in the tree), or nothing.
    private void SelectInTree(HeadingNodeViewModel? node)
    {
        if (_bound is null || _source?.RowSelection is not { } selection || ReferenceEquals(selection.SelectedItem, node))
        {
            return;
        }

        _syncingSelection = true;
        try
        {
            if (node is not null && FindPath(_bound.Nodes, node, new List<int>()) is { } path)
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
            _syncingSelection = false;
        }
    }

    private static List<int>? FindPath(ObservableCollection<HeadingNodeViewModel> nodes, HeadingNodeViewModel target, List<int> prefix)
    {
        for (int i = 0; i < nodes.Count; i++)
        {
            List<int> path = new(prefix) { i };
            if (ReferenceEquals(nodes[i], target))
            {
                return path;
            }

            if (FindPath(nodes[i].Children, target, path) is { } found)
            {
                return found;
            }
        }

        return null;
    }

    private void OnCloseRequested(object? sender, EventArgs e) => Close();

    private void OnKeyDown(object? sender, KeyEventArgs e)
    {
        if (_bound is null)
        {
            return;
        }

        // Left/right arrows = change the level of the selected heading.
        if (e.Key == Key.Left && _bound.DecreaseLevelCommand.CanExecute(null))
        {
            _bound.DecreaseLevelCommand.Execute(null);
            e.Handled = true;
        }
        else if (e.Key == Key.Right && _bound.IncreaseLevelCommand.CanExecute(null))
        {
            _bound.IncreaseLevelCommand.Execute(null);
            e.Handled = true;
        }
    }
}
