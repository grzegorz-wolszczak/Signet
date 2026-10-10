using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics.CodeAnalysis;
using System.Linq;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Signet.App.Infrastructure;
using Signet.App.Resources;
using Signet.App.ViewModels;
using Signet.Controls.TreeDataGrid;
using Signet.Controls.TreeDataGrid.Models;

namespace Signet.App.Views;

/// <summary>
/// "Validation Results" panel view — a results table; double-clicking a row navigates to the file/line. The left
/// toolbar and the context menu fix problems and skip or restore whole types of problems.
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

    private ValidationResultsViewModel? ViewModel => DataContext as ValidationResultsViewModel;

    private IReadOnlyList<ValidationResultRow> SelectedRows =>
        _source?.RowSelection?.SelectedItems.OfType<ValidationResultRow>().ToList() ?? new List<ValidationResultRow>();

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
                    // Shown as text ("N/A" when unknown) but sorted as numbers, "N/A" last.
                    _columns.Text("ValidationResultsView_Line", r => r.LineText, new GridLength(70),
                        new TextColumnOptions<ValidationResultRow>().SortedBy(r => r.Result.Line > 0 ? r.Result.Line : int.MaxValue)),
                    _columns.Text("DryRunReplaceWindow_OffsetColumn", r => r.OffsetText, new GridLength(70),
                        new TextColumnOptions<ValidationResultRow>()
                            .SortedBy(r => r.Result.CharOffset >= 0 ? r.Result.CharOffset : int.MaxValue)),
                    _columns.Text("ValidationResultsView_Fixable", r => r.FixText, new GridLength(70)),
                    _columns.Text("ValidationResultsView_Message", r => r.Message, new GridLength(1, GridUnitType.Star)),
                },
            };
            _source.RowSelection!.SingleSelect = false;
            _source.RowSelection.SelectionChanged += (_, _) => vm.SetSelection(SelectedRows);
        }

        Grid.Source = _source;
    }

    private void OnDoubleTapped(object? sender, TappedEventArgs e)
    {
        if (ViewModel is { } vm && _source?.RowSelection?.SelectedItem is { } row)
        {
            vm.Activate(row);
        }
    }

    private void OnFixClick(object? sender, RoutedEventArgs e) => ViewModel?.FixSelected();

    private void OnFixAllClick(object? sender, RoutedEventArgs e) => ViewModel?.FixAll();

    private void OnSkipRuleClick(object? sender, RoutedEventArgs e)
    {
        if (ViewModel is { } vm && SelectedRows.FirstOrDefault() is { } row)
        {
            vm.SkipRule(row);
        }
    }

    private void OnRowMenuOpening(object? sender, CancelEventArgs e)
    {
        IReadOnlyList<ValidationResultRow> rows = SelectedRows;
        if (rows.Count == 0)
        {
            e.Cancel = true;
            return;
        }

        FixMenuItem.IsEnabled = rows.Any(r => r.CanFix);
        SkipRuleMenuItem.IsEnabled = rows[0].Result.Code.Length > 0;
    }

    // One "Restore: <rule>" item per skipped rule, then "Restore all".
    private void OnSkippedRulesOpening(object? sender, EventArgs e)
    {
        if (sender is not MenuFlyout flyout)
        {
            return;
        }

        flyout.Items.Clear();
        if (ViewModel is not { } vm)
        {
            return;
        }

        foreach (SkippedRule rule in vm.SkippedRules)
        {
            MenuItem item = new() { Header = Strings.Format("ValidationResultsView_RestoreRule", rule.Name) };
            item.Click += (_, _) => vm.RestoreRules(new[] { rule.Code });
            flyout.Items.Add(item);
        }

        flyout.Items.Add(new Separator());
        MenuItem all = new() { Header = Strings.Get("ValidationResultsView_RestoreAllRules") };
        all.Click += (_, _) => vm.RestoreRules(vm.SkippedRules.Select(r => r.Code).ToList());
        flyout.Items.Add(all);
    }
}
