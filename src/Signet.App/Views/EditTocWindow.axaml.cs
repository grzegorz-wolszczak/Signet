using System;
using System.Collections.Generic;
using System.Linq;
using System.Diagnostics.CodeAnalysis;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Input;
using Signet.App.Infrastructure;
using Signet.App.ViewModels;
using Signet.Controls.TreeDataGrid;
using Signet.Controls.TreeDataGrid.Models;

namespace Signet.App.Views;

/// <summary>
/// The modal "Edit Table Of Contents" dialog.
/// An editable table of contents as a table (title / level / target, resizable columns) with multi-selection, moving of contiguous ranges,
/// a context menu and the "Select Target" dialog.
/// </summary>
/// <remarks>
/// The table is a <see cref="TreeDataGrid"/> over <see cref="EditTocViewModel.Rows"/>, built here (a TreeDataGrid source
/// is bound to the UI thread). The title is edited with F2 or a click on an already selected entry (as in the former
/// DataGrid), or from "Rename".
/// </remarks>
[SuppressMessage(
    "Reliability",
    "CA1001:Types that own disposable fields should be disposable",
    Justification = "The table source is disposed when the data context changes; the current one lives as long as the "
        + "dialog and the view model's Rows it observes.")]
public partial class EditTocWindow : Window
{
    private EditTocViewModel? _bound;

    // Keeps the column headers in the current UI language (held weakly by Strings).
    private LocalizedColumns<EditTocNodeViewModel>? _columns;
    private FlatTreeDataGridSource<EditTocNodeViewModel>? _source;

    /// <summary>Initializes the window.</summary>
    public EditTocWindow()
    {
        InitializeComponent();
        DataContextChanged += OnDataContextChanged;
        AddHandler(KeyDownEvent, OnKeyDown, Avalonia.Interactivity.RoutingStrategies.Tunnel);
    }

    /// <summary>Shows the dialog modally; returns <c>true</c> when the user accepted and saving succeeded.</summary>
    public static async Task<bool> RunAsync(Window owner, EditTocViewModel viewModel)
    {
        var window = new EditTocWindow { DataContext = viewModel };
        await window.ShowDialog(owner);
        return viewModel.Accepted;
    }

    private void OnDataContextChanged(object? sender, EventArgs e)
    {
        if (_bound is not null)
        {
            _bound.CloseRequested -= OnCloseRequested;
            _bound.SelectTargetRequested -= OnSelectTargetRequested;
            _bound.RenameRequested -= OnRenameRequested;
            _bound.SelectionChangeRequested -= OnSelectionChangeRequested;
        }

        if (_source is not null)
        {
            _source.RowSelection!.SelectionChanged -= OnSelectionChanged;
            _source.Dispose();
            _source = null;
        }

        _bound = DataContext as EditTocViewModel;

        if (_bound is not null)
        {
            _bound.CloseRequested += OnCloseRequested;
            _bound.SelectTargetRequested += OnSelectTargetRequested;
            _bound.RenameRequested += OnRenameRequested;
            _bound.SelectionChangeRequested += OnSelectionChangeRequested;
            _source = BuildSource(_bound);
            _source.RowSelection!.SelectionChanged += OnSelectionChanged;
        }

        Grid.Source = _source;
    }

    // Title (editable, indented by the level), Level, Target — not sortable: the row order is the TOC order.
    private FlatTreeDataGridSource<EditTocNodeViewModel> BuildSource(EditTocViewModel vm)
    {
        _columns = new LocalizedColumns<EditTocNodeViewModel>();
        FlatTreeDataGridSource<EditTocNodeViewModel> source = new(vm.Rows)
        {
            Columns =
            {
                _columns.Template(
                    "EditTocWindow_ColumnTitle",
                    "TitleCellTemplate",
                    new GridLength(2, GridUnitType.Star),
                    new TemplateColumnOptions<EditTocNodeViewModel>
                    {
                        MinWidth = new GridLength(80),
                        CanUserSortColumn = false,
                        BeginEditGestures = BeginEditGestures.F2 | BeginEditGestures.Tap | BeginEditGestures.WhenSelected,
                    },
                    cellEditingTemplateResourceKey: "TitleEditingTemplate"),
                _columns.Text(
                    "EditTocWindow_ColumnLevel",
                    r => r.Level,
                    GridLength.Auto,
                    new TextColumnOptions<EditTocNodeViewModel> { CanUserSortColumn = false }),
                _columns.Template(
                    "EditTocWindow_ColumnTarget",
                    "TargetCellTemplate",
                    new GridLength(3, GridUnitType.Star),
                    new TemplateColumnOptions<EditTocNodeViewModel>
                    {
                        MinWidth = new GridLength(80),
                        CanUserSortColumn = false,
                    }),
            },
        };
        source.RowSelection!.SingleSelect = false;
        return source;
    }

    private void OnCloseRequested(object? sender, EventArgs e) => Close();

    private void OnSelectionChanged(object? sender, EventArgs e)
    {
        if (_source is not null)
        {
            _bound?.SetSelectedNodes(_source.RowSelection!.SelectedItems.OfType<EditTocNodeViewModel>());
        }
    }

    private void OnSelectionChangeRequested(object? sender, IReadOnlyList<EditTocNodeViewModel> nodes)
    {
        if (_bound is null || _source?.RowSelection is not { } selection)
        {
            return;
        }

        selection.BeginBatchUpdate();
        try
        {
            selection.Clear();
            foreach (EditTocNodeViewModel node in nodes)
            {
                if (_bound.Rows.IndexOf(node) is var index and >= 0)
                {
                    selection.Select(new IndexPath(index));
                }
            }
        }
        finally
        {
            selection.EndBatchUpdate();
        }

        if (nodes.Count > 0 && _bound.Rows.IndexOf(nodes[0]) is var first and >= 0)
        {
            Grid.RowsPresenter?.BringIntoView(first);
        }
    }

    private async void OnSelectTargetRequested(object? sender, EventArgs e)
    {
        if (_bound is null || _bound.SelectedNode is null)
        {
            return;
        }

        string? href = await SelectHyperlinkWindow.AskAsync(
            this, _bound.GetTargetItems(), _bound.CurrentTargetHref());

        if (href is not null)
        {
            _bound.ApplyTarget(href);
        }
    }

    private void OnRenameRequested(object? sender, EventArgs e)
    {
        if (_bound is null || _source?.RowSelection?.SelectedItem is not { } node
            || _bound.Rows.IndexOf(node) is not (var index and >= 0))
        {
            return;
        }

        // The title cell of the row: realized by scrolling it into view, then put in edit mode.
        Grid.RowsPresenter?.BringIntoView(index);
        if (Grid.TryGetCell(0, index) is Signet.Controls.TreeDataGrid.Primitives.TreeDataGridCell cell)
        {
            cell.BeginEdit();
        }
    }

    private void OnKeyDown(object? sender, KeyEventArgs e)
    {
        if (_bound is null)
        {
            return;
        }

        bool editingText = e.Source is TextBox;

        if (e.KeyModifiers == KeyModifiers.Control && e.Key == Key.Up)
        {
            Execute(_bound.MoveUpCommand);
            e.Handled = true;
        }
        else if (e.KeyModifiers == KeyModifiers.Control && e.Key == Key.Down)
        {
            Execute(_bound.MoveDownCommand);
            e.Handled = true;
        }
        else if (!editingText && e.Key == Key.Left)
        {
            Execute(_bound.MoveLeftCommand);
            e.Handled = true;
        }
        else if (!editingText && e.Key == Key.Right)
        {
            Execute(_bound.MoveRightCommand);
            e.Handled = true;
        }
    }

    private static void Execute(CommunityToolkit.Mvvm.Input.RelayCommand command)
    {
        if (command.CanExecute(null))
        {
            command.Execute(null);
        }
    }
}
