using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Input.Platform;
using Avalonia.Interactivity;
using Signet.App.Infrastructure;
using Signet.App.Resources;
using Signet.App.Services;
using Signet.App.ViewModels;
using Signet.Controls.TreeDataGrid;
using Signet.Core.BookManipulation;

namespace Signet.App.Views;

/// <summary>
/// Warnings collected while loading a publication (<c>Book.LoadWarningDetails</c>) as a tree (<see cref="LoadWarningsViewModel"/>):
/// a warning, then — for a warning about many files — one row per file with the reason and the line:column. A non-modal
/// window: double-clicking (or Enter on) a file opens it in Code Editor at the problem while the window stays open, so the
/// files can be visited one by one; so can a warning about a single file. A warning with files is expanded / collapsed
/// instead (also by the Expand All / Collapse All buttons of the left toolbar). "Copy" copies the tree as text.
/// </summary>
[SuppressMessage(
    "Reliability",
    "CA1001:Types that own disposable fields should be disposable",
    Justification = "The tree source is disposed when the window closes (and when the data context changes).")]
public partial class LoadWarningsDialog : Window
{
    // Keeps the column headers in the current UI language (held weakly by Strings).
    private LocalizedColumns<LoadWarningNode>? _columns;
    private HierarchicalTreeDataGridSource<LoadWarningNode>? _source;

    /// <summary>Initializes the window.</summary>
    public LoadWarningsDialog()
    {
        InitializeComponent();
        OkButton.Click += (_, _) => Close();
        ExpandAllButton.Click += (_, _) => ViewModel?.ExpandAll();
        CollapseAllButton.Click += (_, _) => ViewModel?.CollapseAll();
        CopyButton.Click += async (_, _) => await CopyAsync().ConfigureAwait(true);
        DataContextChanged += (_, _) => BuildTree();
        Closed += (_, _) => DisposeSource();

        // Tunnel: before the grid's own key handling.
        WarningsTree.AddHandler(KeyDownEvent, OnTreeKeyDown, RoutingStrategies.Tunnel);
    }

    private LoadWarningsViewModel? ViewModel => DataContext as LoadWarningsViewModel;

    private LoadWarningNode? SelectedNode => _source?.RowSelection?.SelectedItem;

    /// <summary>Shows the window (non-modal, above <paramref name="owner"/>) and returns it.</summary>
    /// <param name="owner">Owner window.</param>
    /// <param name="fileName">Name of the loaded file (for the header).</param>
    /// <param name="warnings">The warnings (already translated).</param>
    /// <param name="navigate">Opens a file at a line and column (<c>bookPath</c>, <c>line</c>, <c>column</c>).</param>
    public static LoadWarningsDialog Show(
        Window owner, string fileName, IReadOnlyList<LoadWarning> warnings, Action<string, int, int>? navigate)
    {
        LoadWarningsDialog dialog = Build(fileName, warnings, navigate);
        dialog.Show(owner);
        return dialog;
    }

    /// <summary>
    /// Shows the report of a file-by-file operation that skipped some files (non-modal, above <paramref name="owner"/>)
    /// and returns the window. Double-clicking a file opens it at the problem, like for the load warnings.
    /// </summary>
    public static LoadWarningsDialog ShowReport(
        Window owner, SkippedFilesReport report, Action<string, int, int>? navigate)
    {
        LoadWarningsDialog dialog = BuildReport(report, navigate);
        dialog.Show(owner);
        return dialog;
    }

    /// <summary>Configures the report window without showing it (for render tests).</summary>
    public static LoadWarningsDialog BuildReport(SkippedFilesReport report, Action<string, int, int>? navigate = null)
    {
        ArgumentNullException.ThrowIfNull(report);
        LoadWarningsDialog dialog = Build(report.OperationName, report.ToWarnings(), navigate, report.Header);
        dialog.Title = Strings.Get("SkippedFilesDialog_Title");
        return dialog;
    }

    /// <summary>Configures the window without showing it (for render tests).</summary>
    public static LoadWarningsDialog Build(
        string fileName, IReadOnlyList<LoadWarning> warnings, Action<string, int, int>? navigate = null, string? header = null)
    {
        ArgumentNullException.ThrowIfNull(warnings);
        LoadWarningsDialog dialog = new();
        LoadWarningsViewModel vm = new(fileName, warnings, navigate, header);
        dialog.HeaderText.Text = vm.Header;
        dialog.DataContext = vm;
        return dialog;
    }

    private void BuildTree()
    {
        DisposeSource();
        if (DataContext is LoadWarningsViewModel vm)
        {
            _columns = new LocalizedColumns<LoadWarningNode>();
            _source = new HierarchicalTreeDataGridSource<LoadWarningNode>(vm.Roots)
            {
                Columns =
                {
                    _columns.Expander(
                        _columns.Text("LoadWarningsDialog_ColumnFile", n => n.Text, new GridLength(3, GridUnitType.Star)),
                        n => n.Children,
                        n => n.HasChildren,
                        n => n.IsExpanded),
                    _columns.Text("LoadWarningsDialog_ColumnReason", n => n.Reason, new GridLength(3, GridUnitType.Star)),
                    _columns.Text("LoadWarningsDialog_ColumnLocation", n => n.Location, new GridLength(90)),
                },
            };
        }

        WarningsTree.Source = _source;
    }

    private void DisposeSource()
    {
        _source?.Dispose();
        _source = null;
    }

    private void OnTreeDoubleTapped(object? sender, TappedEventArgs e)
    {
        if (SelectedNode is { HasChildren: true } node)
        {
            node.IsExpanded = !node.IsExpanded;
            return;
        }

        ViewModel?.Activate(SelectedNode);
    }

    private void OnTreeKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.KeyModifiers == KeyModifiers.None && e.Key == Key.Enter && SelectedNode is { } node)
        {
            if (node.HasChildren)
            {
                node.IsExpanded = !node.IsExpanded;
                e.Handled = true;
            }
            else
            {
                e.Handled = ViewModel?.Activate(node) == true;
            }
        }
    }

    private async System.Threading.Tasks.Task CopyAsync()
    {
        if (Clipboard is { } clipboard && ViewModel is { } vm)
        {
            await clipboard.SetTextAsync(vm.CopyText()).ConfigureAwait(true);
        }
    }
}
