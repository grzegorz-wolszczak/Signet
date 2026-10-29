using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics.CodeAnalysis;
using System.Linq;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.VisualTree;
using Signet.App.Infrastructure;
using Signet.App.Resources;
using Signet.App.ViewModels;
using Signet.Controls.TreeDataGrid;
using Signet.Controls.TreeDataGrid.Models;
using Signet.Controls.TreeDataGrid.Primitives;
using Signet.Core.BookManipulation;

namespace Signet.App.Views;

/// <summary>
/// The modal "Generate Table Of Contents" dialog — choosing the headings for the table of contents.
/// </summary>
/// <remarks>
/// The headings tree is a <see cref="TreeDataGrid"/> over <see cref="HeadingSelectorViewModel.Nodes"/> (Title, Level,
/// In TOC), built here (a TreeDataGrid source is bound to the UI thread); its selection and
/// <see cref="HeadingSelectorViewModel.SelectedNode"/> follow each other. The Title cell enters edit mode from the
/// edit button or a double-tap.
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
    private TreeSelectionSync<HeadingNodeViewModel>? _selection;

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
        try
        {
            await window.ShowDialog(owner);
        }
        finally
        {
            window.DetachConfirmationHandlers();
        }

        return viewModel.Accepted;
    }

    private void OnDataContextChanged(object? sender, EventArgs e)
    {
        if (_bound is not null)
        {
            _bound.CloseRequested -= OnCloseRequested;
            _bound.EditTitleRequested -= OnEditTitleRequested;
            _bound.PropertyChanged -= OnViewModelPropertyChanged;
            _bound.ConfirmHeadingLevelOverflowAsync = null;
            _bound.ConfirmHeadingLevelChangesAsync = null;
        }

        _source?.Dispose();
        _source = null;
        _selection = null;
        _bound = DataContext as HeadingSelectorViewModel;

        if (_bound is not null)
        {
            _bound.CloseRequested += OnCloseRequested;
            _bound.EditTitleRequested += OnEditTitleRequested;
            _bound.PropertyChanged += OnViewModelPropertyChanged;
            _bound.ConfirmHeadingLevelOverflowAsync = ConfirmHeadingLevelOverflowAsync;
            _bound.ConfirmHeadingLevelChangesAsync = ConfirmHeadingLevelChangesAsync;
            HeadingSelectorViewModel vm = _bound;
            _source = BuildSource(vm);
            _selection = new TreeSelectionSync<HeadingNodeViewModel>(
                _source, vm.Nodes, n => n.Children, () => vm.SelectedNode, n => vm.SelectedNode = n);
        }

        Tree.Source = _source;
        _selection?.SelectFromViewModel();
    }

    private async Task<bool> ConfirmHeadingLevelOverflowAsync(IReadOnlyList<Heading> headings)
    {
        HeadingLevelOverflowDecision? decision = await HeadingLevelOverflowWindow.AskAsync(this, headings);
        if (decision?.HeadingToOpen is { } heading)
        {
            _bound?.NavigateToSource?.Invoke(heading);
        }

        return decision?.ContinueWithUnchangedHeadings == true;
    }

    private Task<bool> ConfirmHeadingLevelChangesAsync() =>
        ConfirmWindow.AskAsync(
            this,
            Strings.Get("HeadingSelectorWindow_HeadingLevelsChangedTitle"),
            Strings.Get("HeadingSelectorWindow_HeadingLevelsChangedMessage"));

    private void DetachConfirmationHandlers()
    {
        if (_bound is null)
        {
            return;
        }

        _bound.ConfirmHeadingLevelOverflowAsync = null;
        _bound.ConfirmHeadingLevelChangesAsync = null;
    }

    private HierarchicalTreeDataGridSource<HeadingNodeViewModel> BuildSource(HeadingSelectorViewModel vm)
    {
        _columns = new LocalizedColumns<HeadingNodeViewModel>();
        HierarchicalTreeDataGridSource<HeadingNodeViewModel> source = new(vm.Nodes)
        {
            Columns =
            {
                _columns.Expander(
                    _columns.Template(
                        "EditTocWindow_ColumnTitle",
                        "TitleCellTemplate",
                        new GridLength(1, GridUnitType.Star),
                        new TemplateColumnOptions<HeadingNodeViewModel>
                        {
                            CanUserSortColumn = false,
                            BeginEditGestures = BeginEditGestures.F2 | BeginEditGestures.DoubleTap,
                        },
                        cellEditingTemplateResourceKey: "TitleEditingTemplate"),
                    n => n.Children,
                    n => n.Children.Count > 0,
                    n => n.IsExpanded),
                _columns.Text("EditTocWindow_ColumnLevel", n => n.LevelLabel, new GridLength(70)),
                _columns.Template("HeadingSelectorWindow_ColumnInToc", "IncludeCellTemplate", new GridLength(110)),
            },
        };
        return source;
    }

    private void OnViewModelPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(HeadingSelectorViewModel.SelectedNode))
        {
            _selection?.SelectFromViewModel();
        }
    }

    private void OnCloseRequested(object? sender, EventArgs e) => Close();

    private void OnEditTitleRequested(object? sender, EventArgs e)
    {
        if (_bound is null ||
            _bound.SelectedNode is not { } node ||
            _selection?.FindPath(node) is not { } path ||
            _source?.Rows is not { } rows)
        {
            return;
        }

        int rowIndex = rows.ModelIndexToRowIndex(new IndexPath(path));
        Tree.RowsPresenter?.BringIntoView(rowIndex);
        if (Tree.TryGetCell(0, rowIndex) is { } cell)
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
