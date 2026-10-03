using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Input;
using CommunityToolkit.Mvvm.ComponentModel;
using Signet.Core.Parsers;

namespace Signet.App.Views;

/// <summary>
/// The modal "Delete Unused Stylesheet Selectors" dialog:
/// a list of unused CSS selectors (file + selector text) with a checkbox (all checked
/// by default), a "select/unselect all" toggle and a double click
/// that opens the file at the selector's location.
/// </summary>
public partial class DeleteUnusedStylesWindow : Window
{
    /// <summary>Initializes the window.</summary>
    public DeleteUnusedStylesWindow()
    {
        InitializeComponent();
        DataContext = this;

        ToggleSelectAllCheckBox.Click += (_, _) => SelectUnselectAll(ToggleSelectAllCheckBox.IsChecked == true);
        RowsList.DoubleTapped += OnRowsListDoubleTapped;
        OkButton.Click += (_, _) => Close(CheckedSelectors());
        CancelButton.Click += (_, _) => Close(null);
    }

    /// <summary>List rows (stylesheet/file book path, selector text, offset, check state).</summary>
    public ObservableCollection<DeleteUnusedStylesRow> Rows { get; } = new();

    /// <summary>
    /// Shows the dialog; returns the checked selectors to delete, or <c>null</c> after cancelling.
    /// <paramref name="openFileAtOffset"/> is called after a double click on a row.
    /// </summary>
    public static async Task<IReadOnlyList<CssSelectorUsage>?> AskAsync(
        Window owner, IReadOnlyList<CssSelectorUsage> candidates, Action<string, int> openFileAtOffset)
    {
        ArgumentNullException.ThrowIfNull(candidates);
        ArgumentNullException.ThrowIfNull(openFileAtOffset);

        DeleteUnusedStylesWindow window = new();
        foreach (CssSelectorUsage usage in candidates)
        {
            window.Rows.Add(new DeleteUnusedStylesRow(usage));
        }

        window._openFileAtOffset = openFileAtOffset;
        return await window.ShowDialog<IReadOnlyList<CssSelectorUsage>?>(owner);
    }

    private Action<string, int>? _openFileAtOffset;

    private void OnRowsListDoubleTapped(object? sender, TappedEventArgs e)
    {
        if (RowsList.SelectedItem is DeleteUnusedStylesRow row)
        {
            _openFileAtOffset?.Invoke(row.CssBookPath, row.Usage.Position);
        }
    }

    private void SelectUnselectAll(bool value)
    {
        foreach (DeleteUnusedStylesRow row in Rows)
        {
            row.IsChecked = value;
        }
    }

    private List<CssSelectorUsage> CheckedSelectors() =>
        Rows.Where(row => row.IsChecked).Select(row => row.Usage).ToList();
}

/// <summary>A row of the "Delete Unused Stylesheet Selectors" window.</summary>
public partial class DeleteUnusedStylesRow : ObservableObject
{
    [ObservableProperty]
    private bool _isChecked = true;

    /// <summary>Creates a row for the given (unused) selector.</summary>
    public DeleteUnusedStylesRow(CssSelectorUsage usage) => Usage = usage;

    /// <summary>The unused selector the row refers to.</summary>
    public CssSelectorUsage Usage { get; }

    /// <summary>Book path of the CSS stylesheet or XHTML file (a <c>&lt;style&gt;</c> block).</summary>
    public string CssBookPath => Usage.CssBookPath;

    /// <summary>Selector text.</summary>
    public string SelectorText => Usage.SelectorText;
}
