using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
using Avalonia.Controls;
using CommunityToolkit.Mvvm.ComponentModel;
using Signet.Core.Parsers;
using Signet.Core.Resources;

namespace Signet.App.Views;

/// <summary>
/// Result of the <see cref="CssCleanupWindow"/> dialog — the checked merge candidates and/or
/// unlinked stylesheets to delete.
/// </summary>
/// <param name="Merges">Checked groups of rules to merge.</param>
/// <param name="UnusedStylesheets">Checked, unlinked CSS stylesheets to delete.</param>
public sealed record CssCleanupSelection(IReadOnlyList<CssMergeCandidate> Merges, IReadOnlyList<CssResource> UnusedStylesheets);

/// <summary>
/// The modal "Merge/Remove Unused CSS Rules" dialog: two lists with checkboxes — candidates for merging
/// rules with an identical selector/properties, and CSS stylesheets not linked by any XHTML file —
/// with all rows checked by default, a select-all toggle and an Apply button.
/// Modeled on <see cref="DeleteUnusedStylesWindow"/>.
/// </summary>
public partial class CssCleanupWindow : Window
{
    /// <summary>Initializes the window.</summary>
    public CssCleanupWindow()
    {
        InitializeComponent();
        DataContext = this;

        ToggleSelectAllCheckBox.Click += (_, _) => SelectUnselectAll(ToggleSelectAllCheckBox.IsChecked == true);
        OkButton.Click += (_, _) => Close(BuildSelection());
        CancelButton.Click += (_, _) => Close(null);
    }

    /// <summary>Rows of merge candidates.</summary>
    public ObservableCollection<CssMergeCandidateRow> MergeRows { get; } = new();

    /// <summary>Rows of unlinked CSS stylesheets.</summary>
    public ObservableCollection<CssUnusedStylesheetRow> StylesheetRows { get; } = new();

    /// <summary>Shows the dialog; returns the checked items, or <c>null</c> after cancelling.</summary>
    public static async Task<CssCleanupSelection?> AskAsync(Window owner, Core.BookManipulation.CssCleanupResult candidates)
    {
        ArgumentNullException.ThrowIfNull(candidates);

        CssCleanupWindow window = new();
        foreach (CssMergeCandidate candidate in candidates.MergeCandidates)
        {
            window.MergeRows.Add(new CssMergeCandidateRow(candidate));
        }

        foreach (CssResource css in candidates.UnusedStylesheets)
        {
            window.StylesheetRows.Add(new CssUnusedStylesheetRow(css));
        }

        return await window.ShowDialog<CssCleanupSelection?>(owner);
    }

    private void SelectUnselectAll(bool value)
    {
        foreach (CssMergeCandidateRow row in MergeRows)
        {
            row.IsChecked = value;
        }

        foreach (CssUnusedStylesheetRow row in StylesheetRows)
        {
            row.IsChecked = value;
        }
    }

    private CssCleanupSelection BuildSelection() => new(
        MergeRows.Where(row => row.IsChecked).Select(row => row.Candidate).ToList(),
        StylesheetRows.Where(row => row.IsChecked).Select(row => row.Resource).ToList());
}

/// <summary>A row of the merge candidates list.</summary>
public partial class CssMergeCandidateRow : ObservableObject
{
    [ObservableProperty]
    private bool _isChecked = true;

    /// <summary>Creates a row for the given candidate.</summary>
    public CssMergeCandidateRow(CssMergeCandidate candidate) => Candidate = candidate;

    /// <summary>The merge candidate the row refers to.</summary>
    public CssMergeCandidate Candidate { get; }

    /// <summary>Book path of the CSS stylesheet.</summary>
    public string CssBookPath => Candidate.CssBookPath;

    /// <summary>Description of the group to merge.</summary>
    public string Description => Candidate.Description;
}

/// <summary>A row of the unlinked CSS stylesheets list.</summary>
public partial class CssUnusedStylesheetRow : ObservableObject
{
    [ObservableProperty]
    private bool _isChecked = true;

    /// <summary>Creates a row for the given resource.</summary>
    public CssUnusedStylesheetRow(CssResource resource) => Resource = resource;

    /// <summary>The unlinked CSS stylesheet the row refers to.</summary>
    public CssResource Resource { get; }

    /// <summary>Book path of the stylesheet (shown in the list).</summary>
    public string BookPath => Resource.BookPath;
}
