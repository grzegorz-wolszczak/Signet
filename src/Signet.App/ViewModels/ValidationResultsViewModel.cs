using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using Avalonia.Controls;
using Signet.App.Infrastructure;
using Signet.Controls.TreeDataGrid;
using Signet.Core.BookManipulation;

namespace Signet.App.ViewModels;

/// <summary>
/// View model of the "Validation Results" panel — a shared list of validation results
/// (currently fed only by Well-Formed Check EPUB). The main window performs the navigation when
/// an entry is activated.
/// </summary>
public sealed class ValidationResultsViewModel : ViewModelBase
{
    // Keeps the column headers in the current UI language (held weakly by Strings).
    private readonly LocalizedColumns<ValidationResultRow> _columns = new();
    private bool _hasResults;

    /// <summary>Creates the view model with an empty results table.</summary>
    public ValidationResultsViewModel()
    {
        Source = new FlatTreeDataGridSource<ValidationResultRow>(Rows)
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

    /// <summary>Raised when an entry is activated (double click) — handled by the main window.</summary>
    public event EventHandler<ValidationResult>? EntryActivated;

    /// <summary>Rows of the current validation results.</summary>
    public ObservableCollection<ValidationResultRow> Rows { get; } = new();

    /// <summary>The table of <see cref="Rows"/> (File, Line, Offset, Message) for the view's <c>TreeDataGrid</c>.</summary>
    public FlatTreeDataGridSource<ValidationResultRow> Source { get; }

    /// <summary>
    /// Whether there are any results (to switch the view between the table and the "No problems found!" message).
    /// </summary>
    public bool HasResults
    {
        get => _hasResults;
        private set => SetProperty(ref _hasResults, value);
    }

    /// <summary>Replaces the current results with new ones (clears and fills again).</summary>
    public void LoadResults(IReadOnlyList<ValidationResult> results)
    {
        ArgumentNullException.ThrowIfNull(results);

        Rows.Clear();
        foreach (ValidationResult result in results)
        {
            Rows.Add(new ValidationResultRow(result));
        }

        HasResults = Rows.Count > 0;
    }

    /// <summary>Clears the results.</summary>
    public void ClearResults()
    {
        Rows.Clear();
        HasResults = false;
    }

    /// <summary>Raises a request to navigate to a result (called from the view after a double click).</summary>
    public void Activate(ValidationResultRow row) => EntryActivated?.Invoke(this, row.Result);
}

/// <summary>A row of the "Validation Results" panel table — a projection of <see cref="ValidationResult"/> for display.</summary>
public sealed class ValidationResultRow
{
    /// <summary>Creates a row from a validation result.</summary>
    public ValidationResultRow(ValidationResult result)
    {
        Result = result;
        int lastSlash = result.BookPath.LastIndexOf('/');
        FileName = lastSlash >= 0 ? result.BookPath[(lastSlash + 1)..] : result.BookPath;
    }

    /// <summary>The source result (the navigation target).</summary>
    public ValidationResult Result { get; }

    /// <summary>File name (without the path) to show in the "File" column.</summary>
    public string FileName { get; }

    /// <summary>Line number or "N/A".</summary>
    public string LineText => Result.Line > 0 ? Result.Line.ToString(System.Globalization.CultureInfo.InvariantCulture) : "N/A";

    /// <summary>Character offset or "N/A".</summary>
    public string OffsetText => Result.CharOffset >= 0
        ? Result.CharOffset.ToString(System.Globalization.CultureInfo.InvariantCulture)
        : "N/A";

    /// <summary>The message.</summary>
    public string Message => Result.Message;
}
