using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using System;
using CommunityToolkit.Mvvm.Input;
using Signet.App.Resources;
using Signet.Core.Resources;
using Signet.Core.Search;

namespace Signet.App.ViewModels;

/// <summary>
/// View model of the "Filter Replacements" window — a table of all matches with a checkbox per
/// row; "Apply" applies only the checked ones.
/// </summary>
public sealed class ReplacementChooserViewModel : ViewModelBase
{
    private readonly IReadOnlyList<TextResource> _resources;
    private readonly string _searchRegex;
    private readonly string _replaceText;
    private readonly Func<IReadOnlyList<ReplacePreviewRow>, IReadOnlyList<TextResource>, int> _apply;

    private int _contextAmount = ReplacePreview.DefaultContextAmount;
    private bool _selectAll = true;
    private bool _syncingChecks;

    /// <summary>Creates the view model and builds the initial table (all rows checked).</summary>
    /// <param name="resources">Resources to search (from <see cref="FindReplaceViewModel.TryBuildReplacePreviewRequest"/>).</param>
    /// <param name="searchRegex">The final PCRE2 pattern.</param>
    /// <param name="replaceText">The replacement pattern.</param>
    /// <param name="apply">
    /// Applies the selected rows and returns the number of replacements — usually
    /// <see cref="FindReplaceViewModel.ApplyChosenReplacements"/>.
    /// </param>
    public ReplacementChooserViewModel(
        IReadOnlyList<TextResource> resources,
        string searchRegex,
        string replaceText,
        Func<IReadOnlyList<ReplacePreviewRow>, IReadOnlyList<TextResource>, int> apply)
    {
        _resources = resources ?? throw new ArgumentNullException(nameof(resources));
        _searchRegex = searchRegex ?? throw new ArgumentNullException(nameof(searchRegex));
        _replaceText = replaceText ?? throw new ArgumentNullException(nameof(replaceText));
        _apply = apply ?? throw new ArgumentNullException(nameof(apply));

        ContextOptions = new ObservableCollection<int>(ReplacePreview.ContextAmounts);
        ApplyCommand = new RelayCommand(Apply);

        Rebuild(Array.Empty<bool>());
    }

    /// <summary>Table rows (document order — sorting is disabled because the offset matters).</summary>
    public ObservableCollection<ChooserRowViewModel> Rows { get; } = new();

    /// <summary>Allowed context sizes (10/20/30/40/50).</summary>
    public ObservableCollection<int> ContextOptions { get; }

    /// <summary>Applies the checked replacements and raises <see cref="CloseRequested"/>.</summary>
    public IRelayCommand ApplyCommand { get; }

    /// <summary>Raised after "Apply" — the window should close.</summary>
    public event EventHandler? CloseRequested;

    /// <summary>Number of context characters; changing it rebuilds the table, keeping the checks by position.</summary>
    public int ContextAmount
    {
        get => _contextAmount;
        set
        {
            if (value > 0 && SetProperty(ref _contextAmount, value))
            {
                Rebuild(Rows.Select(r => r.IsChecked).ToList());
            }
        }
    }

    /// <summary>Checks / unchecks all rows.</summary>
    public bool SelectAll
    {
        get => _selectAll;
        set
        {
            if (!SetProperty(ref _selectAll, value))
            {
                return;
            }

            _syncingChecks = true;
            foreach (ChooserRowViewModel row in Rows)
            {
                row.IsChecked = value;
            }

            _syncingChecks = false;
            RaiseSelectionChanged();
        }
    }

    /// <summary>Number of checked rows.</summary>
    public int SelectedCount => Rows.Count(r => r.IsChecked);

    /// <summary>Summary shown above the table.</summary>
    public string Summary => Strings.Format("ReplacementChooser_Summary", SelectedCount, Rows.Count);

    /// <summary>Number of replacements actually performed after "Apply" (0 until accepted).</summary>
    public int ReplacementCount { get; private set; }

    /// <summary>Whether the replacement pattern is a Python function <c>\F&lt;…&gt;</c> (unsupported).</summary>
    public bool HasFunctionReplacement => ReplacePreview.IsFunctionReplacement(_replaceText);

    private void Rebuild(IReadOnlyList<bool> previousChecks)
    {
        foreach (ChooserRowViewModel row in Rows)
        {
            row.PropertyChanged -= OnRowPropertyChanged;
        }

        Rows.Clear();

        IReadOnlyList<ReplacePreviewRow> built =
            ReplacePreview.BuildRows(_resources, _searchRegex, _replaceText, _contextAmount);

        for (int i = 0; i < built.Count; i++)
        {
            bool isChecked = i < previousChecks.Count ? previousChecks[i] : true;
            var row = new ChooserRowViewModel(built[i], isChecked);
            row.PropertyChanged += OnRowPropertyChanged;
            Rows.Add(row);
        }

        RaiseSelectionChanged();
    }

    private void OnRowPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (_syncingChecks || e.PropertyName != nameof(ChooserRowViewModel.IsChecked))
        {
            return;
        }

        bool allChecked = Rows.Count > 0 && Rows.All(r => r.IsChecked);
        if (_selectAll != allChecked)
        {
            _selectAll = allChecked;
            OnPropertyChanged(nameof(SelectAll));
        }

        RaiseSelectionChanged();
    }

    private void RaiseSelectionChanged()
    {
        OnPropertyChanged(nameof(SelectedCount));
        OnPropertyChanged(nameof(Summary));
    }

    private void Apply()
    {
        List<ReplacePreviewRow> selected = Rows.Where(r => r.IsChecked).Select(r => r.Row).ToList();
        ReplacementCount = _apply(selected, _resources);
        OnPropertyChanged(nameof(ReplacementCount));
        CloseRequested?.Invoke(this, EventArgs.Empty);
    }
}

/// <summary>A row of the "Filter Replacements" window — <see cref="ReplacePreviewRow"/> plus the checkbox state.</summary>
public sealed class ChooserRowViewModel : ViewModelBase
{
    private bool _isChecked;

    /// <summary>Creates a row with the given check state.</summary>
    public ChooserRowViewModel(ReplacePreviewRow row, bool isChecked)
    {
        Row = row ?? throw new ArgumentNullException(nameof(row));
        _isChecked = isChecked;
    }

    /// <summary>The underlying match.</summary>
    public ReplacePreviewRow Row { get; }

    /// <summary>Whether the replacement from this row is applied on "Apply".</summary>
    public bool IsChecked
    {
        get => _isChecked;
        set => SetProperty(ref _isChecked, value);
    }

    /// <summary>Book path (for table binding).</summary>
    public string BookPath => Row.BookPath;

    /// <summary>Match offset (for table binding).</summary>
    public int Offset => Row.Offset;

    /// <summary>"Before" snippet (for table binding).</summary>
    public string BeforeSnippet => Row.BeforeSnippet;

    /// <summary>"After" snippet (for table binding).</summary>
    public string AfterSnippet => Row.AfterSnippet;
}
