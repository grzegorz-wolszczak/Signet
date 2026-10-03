using System.Collections.Generic;
using System.Collections.ObjectModel;
using System;
using CommunityToolkit.Mvvm.Input;
using Signet.App.Resources;
using Signet.Core.Resources;
using Signet.Core.Search;

namespace Signet.App.ViewModels;

/// <summary>
/// View model of the "Dry Run Replace All" window — a read-only table of all matches
/// with the proposed replacement and context.
/// </summary>
public sealed class DryRunReplaceViewModel : ViewModelBase
{
    private readonly IReadOnlyList<TextResource> _resources;
    private readonly string _searchRegex;
    private readonly string _replaceText;
    private readonly Action<string, int>? _openFile;

    private IReadOnlyList<ReplacePreviewRow> _allRows = Array.Empty<ReplacePreviewRow>();
    private int _contextAmount = ReplacePreview.DefaultContextAmount;
    private string _filterText = string.Empty;

    /// <summary>Creates the view model and builds the first table.</summary>
    /// <param name="resources">Resources to search (from <see cref="FindReplaceViewModel.TryBuildReplacePreviewRequest"/>).</param>
    /// <param name="searchRegex">The ready PCRE2 pattern.</param>
    /// <param name="replaceText">The replacement pattern.</param>
    /// <param name="openFile">Called on a row double click — open the file at the offset.</param>
    public DryRunReplaceViewModel(
        IReadOnlyList<TextResource> resources,
        string searchRegex,
        string replaceText,
        Action<string, int>? openFile = null)
    {
        _resources = resources ?? throw new ArgumentNullException(nameof(resources));
        _searchRegex = searchRegex ?? throw new ArgumentNullException(nameof(searchRegex));
        _replaceText = replaceText ?? throw new ArgumentNullException(nameof(replaceText));
        _openFile = openFile;

        ContextOptions = new ObservableCollection<int>(ReplacePreview.ContextAmounts);
        RefreshCommand = new RelayCommand(Rebuild);
        OpenRowCommand = new RelayCommand<ReplacePreviewRow?>(OpenRow);

        Rebuild();
    }

    /// <summary>Visible rows (after filtering).</summary>
    public ObservableCollection<ReplacePreviewRow> Rows { get; } = new();

    /// <summary>Allowed context sizes (10/20/30/40/50).</summary>
    public ObservableCollection<int> ContextOptions { get; }

    /// <summary>Rebuilds the table (the "Refresh" button).</summary>
    public IRelayCommand RefreshCommand { get; }

    /// <summary>Opens the row's file at the match offset (double click).</summary>
    public IRelayCommand OpenRowCommand { get; }

    /// <summary>Number of context characters before and after the match; changing it rebuilds the table.</summary>
    public int ContextAmount
    {
        get => _contextAmount;
        set
        {
            if (value > 0 && SetProperty(ref _contextAmount, value))
            {
                Rebuild();
            }
        }
    }

    /// <summary>Row filter (path / "before" context / "after" context, case-insensitive).</summary>
    public string FilterText
    {
        get => _filterText;
        set
        {
            if (SetProperty(ref _filterText, value ?? string.Empty))
            {
                ApplyFilter();
            }
        }
    }

    /// <summary>Total number of matches (before filtering).</summary>
    public int MatchCount => _allRows.Count;

    /// <summary>Number of rows visible after the filter.</summary>
    public int VisibleCount => Rows.Count;

    /// <summary>Summary above the table.</summary>
    public string Summary => _filterText.Trim().Length == 0
        ? Strings.Format("DryRun_Summary", _allRows.Count)
        : Strings.Format("DryRun_SummaryFiltered", _allRows.Count, Rows.Count);

    /// <summary>Whether the replacement pattern is a Python function <c>\F&lt;…&gt;</c> (unsupported — the "after" column = "before").</summary>
    public bool HasFunctionReplacement => ReplacePreview.IsFunctionReplacement(_replaceText);

    private void Rebuild()
    {
        _allRows = ReplacePreview.BuildRows(_resources, _searchRegex, _replaceText, _contextAmount);
        OnPropertyChanged(nameof(MatchCount));
        ApplyFilter();
    }

    private void ApplyFilter()
    {
        string filter = _filterText.Trim();
        Rows.Clear();

        foreach (ReplacePreviewRow row in _allRows)
        {
            if (filter.Length == 0
                || row.BookPath.Contains(filter, StringComparison.OrdinalIgnoreCase)
                || row.BeforeSnippet.Contains(filter, StringComparison.OrdinalIgnoreCase)
                || row.AfterSnippet.Contains(filter, StringComparison.OrdinalIgnoreCase))
            {
                Rows.Add(row);
            }
        }

        OnPropertyChanged(nameof(VisibleCount));
        OnPropertyChanged(nameof(Summary));
    }

    private void OpenRow(ReplacePreviewRow? row)
    {
        if (row is not null)
        {
            _openFile?.Invoke(row.BookPath, row.Offset);
        }
    }
}
