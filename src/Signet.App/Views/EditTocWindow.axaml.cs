using Signet.App.Resources;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Diagnostics.CodeAnalysis;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.VisualTree;
using Signet.App.Infrastructure;
using Signet.App.ViewModels;
using Signet.Controls.TreeDataGrid;
using Signet.Controls.TreeDataGrid.Models;
using Signet.Controls.TreeDataGrid.Primitives;

namespace Signet.App.Views;

/// <summary>
/// The modal "Edit Table Of Contents" dialog.
/// An editable table of contents tree (title / level / target, resizable columns) with multi-selection, moving of contiguous ranges,
/// a context menu and the "Select Target" dialog.
/// </summary>
/// <remarks>
/// The tree is a <see cref="TreeDataGrid"/> over <see cref="EditTocViewModel.Nodes"/>, built here (a TreeDataGrid source
/// is bound to the UI thread). Its selection follows the view model's selected nodes. The title is edited with F2 or a
/// click on an already selected entry, or from "Rename".
/// </remarks>
[SuppressMessage(
    "Reliability",
    "CA1001:Types that own disposable fields should be disposable",
    Justification = "The table source is disposed when the data context changes; the current one lives as long as the "
        + "dialog and the view model's Nodes it observes.")]
public partial class EditTocWindow : Window
{
    private EditTocViewModel? _bound;

    // Keeps the column headers in the current UI language (held weakly by Strings).
    private LocalizedColumns<EditTocNodeViewModel>? _columns;
    private HierarchicalTreeDataGridSource<EditTocNodeViewModel>? _source;
    private TreeMultiSelectionSync<EditTocNodeViewModel>? _selection;

    /// <summary>Initializes the window.</summary>
    public EditTocWindow()
    {
        InitializeComponent();
        DataContextChanged += OnDataContextChanged;
        if (Grid.ContextMenu is { } menu)
        {
            MenuTitle.AddTo(menu, () => MenuTitle.ForSelection(
                _bound?.SelectedNodes.Select(n => n.Text) ?? Enumerable.Empty<string>(), Strings.Get("EditTocWindow_Title")));
        }
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

        _selection = null;
        if (_source is not null)
        {
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
            EditTocViewModel vm = _bound;
            _source = BuildSource(vm);
            _selection = new TreeMultiSelectionSync<EditTocNodeViewModel>(
                _source, vm.Nodes, n => n.Children, () => vm.SelectedNodes, vm.SetSelectedNodes);
        }

        Grid.Source = _source;
        _selection?.SelectFromViewModel();
    }

    // Title (editable and expandable), Level, Target — not sortable: the row order is the TOC order.
    private HierarchicalTreeDataGridSource<EditTocNodeViewModel> BuildSource(EditTocViewModel vm)
    {
        _columns = new LocalizedColumns<EditTocNodeViewModel>();
        HierarchicalTreeDataGridSource<EditTocNodeViewModel> source = new(vm.Nodes)
        {
            Columns =
            {
                _columns.Expander(
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
                    n => n.Children,
                    n => n.Children.Count > 0,
                    n => n.IsExpanded),
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
        return source;
    }

    private void OnCloseRequested(object? sender, EventArgs e) => Close();

    private void OnSelectionChangeRequested(object? sender, IReadOnlyList<EditTocNodeViewModel> nodes)
    {
        if (_selection is null || _source?.Rows is not { } rows)
        {
            return;
        }

        _selection.SelectFromViewModel();

        if (nodes.Count > 0 && _selection.FindPath(nodes[0]) is { } path)
        {
            int rowIndex = rows.ModelIndexToRowIndex(new IndexPath(path));
            if (rowIndex >= 0)
            {
                Grid.RowsPresenter?.BringIntoView(rowIndex);
            }
        }
    }

    private void OnTargetDoubleTapped(object? sender, TappedEventArgs e)
    {
        if (_bound is null || (sender as Control)?.DataContext is not EditTocNodeViewModel node)
        {
            return;
        }

        e.Handled = true;
        _bound.SetSelectedNodes(new[] { node });
        _selection?.SelectFromViewModel();
        Execute(_bound.SelectTargetCommand);
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
        if (_bound?.SelectedNode is not { } node ||
            _selection?.FindPath(node) is not { } path ||
            _source?.Rows is not { } rows)
        {
            return;
        }

        int rowIndex = rows.ModelIndexToRowIndex(new IndexPath(path));
        if (rowIndex < 0)
        {
            return;
        }

        // The title cell of the row: realized by scrolling it into view, then put in edit mode.
        Grid.RowsPresenter?.BringIntoView(rowIndex);
        if (Grid.TryGetCell(0, rowIndex) is { } cell)
        {
            TreeDataGridTemplateCell? titleCell = cell as TreeDataGridTemplateCell
                ?? cell.GetVisualDescendants().OfType<TreeDataGridTemplateCell>().FirstOrDefault();
            titleCell?.BeginEdit();
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
