using System.Diagnostics.CodeAnalysis;
using Avalonia.Controls;
using Avalonia.Input;
using Signet.App.Infrastructure;
using Signet.App.ViewModels;
using Signet.Controls.TreeDataGrid;

namespace Signet.App.Views;

/// <summary>
/// "Validation Results" panel view — a results table; double-clicking a row navigates to the
/// file/line.
/// </summary>
/// <remarks>
/// The table is a <see cref="TreeDataGrid"/> whose source and columns are built here, over the view model's
/// <see cref="ValidationResultsViewModel.Rows"/>: a TreeDataGrid source is bound to the UI thread, so it does not
/// belong in the (thread-agnostic, unit-tested) view model.
/// </remarks>
[SuppressMessage(
    "Reliability",
    "CA1001:Types that own disposable fields should be disposable",
    Justification = "The table source is disposed when the data context changes; the current one lives as long as the "
        + "view and the view model's Rows it observes. The view is not disposed on detach: Dock re-parents panels.")]
public partial class ValidationResultsView : UserControl
{
    // Keeps the column headers in the current UI language (held weakly by Strings).
    private LocalizedColumns<ValidationResultRow>? _columns;
    private FlatTreeDataGridSource<ValidationResultRow>? _source;

    /// <summary>Initializes the view.</summary>
    public ValidationResultsView()
    {
        InitializeComponent();
        DataContextChanged += (_, _) => BuildTable();
    }

    private void BuildTable()
    {
        _source?.Dispose();
        _source = null;
        if (DataContext is ValidationResultsViewModel vm)
        {
            _columns = new LocalizedColumns<ValidationResultRow>();
            _source = new FlatTreeDataGridSource<ValidationResultRow>(vm.Rows)
            {
                Columns =
                {
                    _columns.Text("ReportsWindow_File", r => r.FileName, new GridLength(220)),
                    _columns.Text("ValidationResultsView_Line", r => r.LineText, new GridLength(70)),
                    _columns.Text("DryRunReplaceWindow_OffsetColumn", r => r.OffsetText, new GridLength(70)),
                    _columns.Text("ValidationResultsView_Message", r => r.Message, new GridLength(1, GridUnitType.Star)),
                },
            };
        }

        Grid.Source = _source;
    }

    private void OnDoubleTapped(object? sender, TappedEventArgs e)
    {
        if (DataContext is ValidationResultsViewModel vm && _source?.RowSelection?.SelectedItem is { } row)
        {
            vm.Activate(row);
        }
    }
}
