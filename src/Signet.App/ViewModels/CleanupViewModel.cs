using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Signet.App.Resources;
using Signet.Core.BookManipulation;

namespace Signet.App.ViewModels;

/// <summary>
/// View model of the "Cleanup" dialog: one section per <see cref="CleanupStep"/> (in execution order), each with a
/// checkbox, a description and — when enabled — a summary and an expandable list of the changes it will make.
/// Every toggle (of a section or of a single item) re-plans the whole sequence
/// (<see cref="CleanupAnalysis.Plan"/>), because the steps depend on each other. Risky items (merges that would
/// change the styling) are unchecked until the user checks them — that is the explicit acceptance; the check state
/// of every row always comes from the plan.
/// </summary>
public sealed partial class CleanupViewModel : ViewModelBase
{
    private readonly CleanupAnalysis _analysis;
    private readonly Action<string, int> _navigate;
    private readonly HashSet<string> _excludedKeys = new(StringComparer.Ordinal);
    private readonly HashSet<string> _acceptedRiskyKeys = new(StringComparer.Ordinal);
    private bool _updating;

    [ObservableProperty]
    private bool _hasChanges;

    /// <summary>Creates the view model and computes the first plan.</summary>
    /// <param name="analysis">The prepared analysis of the book.</param>
    /// <param name="enabledSteps">The steps checked when the dialog opens (the remembered choice).</param>
    /// <param name="navigate">Opens a file at an offset (a double click on an item).</param>
    public CleanupViewModel(CleanupAnalysis analysis, IEnumerable<CleanupStep> enabledSteps, Action<string, int> navigate)
    {
        ArgumentNullException.ThrowIfNull(analysis);
        ArgumentNullException.ThrowIfNull(enabledSteps);
        ArgumentNullException.ThrowIfNull(navigate);

        _analysis = analysis;
        _navigate = navigate;

        HashSet<CleanupStep> enabled = enabledSteps.ToHashSet();
        foreach (CleanupStep step in Enum.GetValues<CleanupStep>())
        {
            Sections.Add(new CleanupSectionViewModel(this, step, enabled.Contains(step)));
        }

        Plan = Replan();
    }

    /// <summary>The sections, in execution order.</summary>
    public ObservableCollection<CleanupSectionViewModel> Sections { get; } = new();

    /// <summary>The current plan — applied by OK.</summary>
    public CleanupPlan Plan { get; private set; }

    /// <summary>The steps currently checked (remembered for the next time).</summary>
    public IReadOnlyList<CleanupStep> EnabledSteps =>
        Sections.Where(s => s.IsEnabled).Select(s => s.Step).ToList();

    /// <summary>Opens the file of <paramref name="item"/> at its location.</summary>
    public void Navigate(CleanupItemViewModel item)
    {
        ArgumentNullException.ThrowIfNull(item);
        _navigate(item.BookPath, item.Offset);
    }

    /// <summary>Opens the file of <paramref name="consequence"/> at the affected element (or rule).</summary>
    public void Navigate(CleanupConsequence consequence)
    {
        ArgumentNullException.ThrowIfNull(consequence);
        _navigate(consequence.BookPath, consequence.Offset);
    }

    internal void OnSectionToggled() => Plan = Replan();

    internal void OnItemToggled(CleanupItemViewModel item)
    {
        if (_updating)
        {
            return;
        }

        Remember(item, item.IsChecked);
        Plan = Replan();
    }

    internal void SetAllItems(CleanupSectionViewModel section, bool isChecked)
    {
        _updating = true;
        try
        {
            // "Select all" never accepts a risky item — that stays a deliberate, per-item decision.
            foreach (CleanupItemViewModel item in section.Items.Where(i => !isChecked || !i.IsRisky))
            {
                Remember(item, isChecked);
            }
        }
        finally
        {
            _updating = false;
        }

        Plan = Replan();
    }

    private void Remember(CleanupItemViewModel item, bool isChecked)
    {
        if (isChecked)
        {
            _excludedKeys.Remove(item.Key);
            if (item.IsRisky)
            {
                _acceptedRiskyKeys.Add(item.Key);
            }
        }
        else
        {
            _excludedKeys.Add(item.Key);
            _acceptedRiskyKeys.Remove(item.Key);
        }
    }

    private CleanupPlan Replan()
    {
        HashSet<CleanupStep> enabled = Sections.Where(s => s.IsEnabled).Select(s => s.Step).ToHashSet();
        CleanupPlan plan = _analysis.Plan(enabled, _excludedKeys, _acceptedRiskyKeys);

        _updating = true;
        try
        {
            foreach (CleanupSectionViewModel section in Sections)
            {
                section.Update(plan.GetStep(section.Step));
            }
        }
        finally
        {
            _updating = false;
        }

        HasChanges = plan.HasChanges;
        return plan;
    }
}

/// <summary>One section (step) of the Cleanup dialog.</summary>
public sealed partial class CleanupSectionViewModel : ObservableObject
{
    private readonly CleanupViewModel _owner;

    [ObservableProperty]
    private bool _isEnabled;

    [ObservableProperty]
    private bool _isExpanded;

    [ObservableProperty]
    private string _summary = string.Empty;

    [ObservableProperty]
    private bool _hasItems;

    internal CleanupSectionViewModel(CleanupViewModel owner, CleanupStep step, bool isEnabled)
    {
        _owner = owner;
        Step = step;
        _isEnabled = isEnabled;
        Title = Strings.Get($"Cleanup_{step}_Title");
        Description = Strings.Get($"Cleanup_{step}_Description");
        Warning = step is CleanupStep.MergeSameSelectors or CleanupStep.MergeSameProperties
            ? Strings.Get("Cleanup_MergeWarning")
            : string.Empty;
        SelectAllCommand = new RelayCommand(() => _owner.SetAllItems(this, true));
        SelectNoneCommand = new RelayCommand(() => _owner.SetAllItems(this, false));
    }

    /// <summary>The step of this section.</summary>
    public CleanupStep Step { get; }

    /// <summary>The checkbox label.</summary>
    public string Title { get; }

    /// <summary>What the step does.</summary>
    public string Description { get; }

    /// <summary>A caution (merge steps), otherwise empty.</summary>
    public string Warning { get; }

    /// <summary>Whether <see cref="Warning"/> is shown.</summary>
    public bool HasWarning => Warning.Length > 0;

    /// <summary>The changes the step will make (in the context of the steps before it).</summary>
    public ObservableCollection<CleanupItemViewModel> Items { get; } = new();

    /// <summary>Checks every item of the section.</summary>
    public IRelayCommand SelectAllCommand { get; }

    /// <summary>Unchecks every item of the section.</summary>
    public IRelayCommand SelectNoneCommand { get; }

    partial void OnIsEnabledChanged(bool value) => _owner.OnSectionToggled();

    /// <summary>
    /// Synchronizes <see cref="Items"/> with the new result, keeping the row objects of items that are still there
    /// (by key), so the list does not jump while the user toggles items.
    /// </summary>
    internal void Update(CleanupStepResult? result)
    {
        IReadOnlyList<CleanupItem> items = result?.Items ?? Array.Empty<CleanupItem>();
        Dictionary<string, CleanupItemViewModel> existing = Items.ToDictionary(i => i.Key, StringComparer.Ordinal);

        List<CleanupItemViewModel> rows = items
            .Select(item => existing.TryGetValue(item.Key, out CleanupItemViewModel? row)
                ? row.With(item)
                : new CleanupItemViewModel(_owner, item))
            .ToList();

        if (!rows.SequenceEqual(Items))
        {
            Items.Clear();
            foreach (CleanupItemViewModel row in rows)
            {
                Items.Add(row);
            }
        }

        HasItems = Items.Count > 0;
        Summary = result is null ? string.Empty : FormatSummary(result);
    }

    private string FormatSummary(CleanupStepResult result)
    {
        if (result.Items.Count == 0)
        {
            return Strings.Get("Cleanup_NothingFound");
        }

        string summary = Step is CleanupStep.MergeSameSelectors or CleanupStep.MergeSameProperties
            ? Strings.Format($"Cleanup_{Step}_Summary", result.AppliedCount, result.Items.Count, result.AppliedRuleCount)
            : Strings.Format($"Cleanup_{Step}_Summary", result.AppliedCount, result.Items.Count);
        return result.RiskyCount > 0 ? summary + Strings.Format("Cleanup_RiskySummary", result.RiskyCount) : summary;
    }
}

/// <summary>One change in a section's report, with its own checkbox and — for a risky item — its consequences.</summary>
public sealed partial class CleanupItemViewModel : ObservableObject
{
    private readonly CleanupViewModel _owner;

    [ObservableProperty]
    private bool _isChecked;

    internal CleanupItemViewModel(CleanupViewModel owner, CleanupItem item)
    {
        _owner = owner;
        Key = item.Key;
        Apply(item);
    }

    /// <summary>The stable key of the item (<see cref="CleanupItem.Key"/>).</summary>
    public string Key { get; }

    /// <summary>The description (selector, merge description, file path).</summary>
    public string Text { get; private set; } = string.Empty;

    /// <summary>The file the item concerns.</summary>
    public string BookPath { get; private set; } = string.Empty;

    /// <summary>Where in <see cref="BookPath"/> the item is.</summary>
    public int Offset { get; private set; }

    /// <summary>The file shown next to the text — empty when the text already is the path.</summary>
    public string FileText => string.Equals(Text, BookPath, StringComparison.Ordinal) ? string.Empty : BookPath;

    /// <summary>What applying the item would change (shown under the row); empty for a safe item.</summary>
    public IReadOnlyList<CleanupConsequence> Consequences { get; private set; } = Array.Empty<CleanupConsequence>();

    /// <summary>Whether the item would change the book's styling (marked ⚠, unchecked until accepted).</summary>
    public bool IsRisky => Consequences.Count > 0;

    partial void OnIsCheckedChanged(bool value) => _owner.OnItemToggled(this);

    internal CleanupItemViewModel With(CleanupItem item)
    {
        Apply(item);
        OnPropertyChanged(nameof(Text));
        OnPropertyChanged(nameof(BookPath));
        OnPropertyChanged(nameof(FileText));
        OnPropertyChanged(nameof(Consequences));
        OnPropertyChanged(nameof(IsRisky));
        return this;
    }

    // Called while the owner is re-planning, so setting IsChecked does not trigger another re-plan.
    private void Apply(CleanupItem item)
    {
        Text = item.Text;
        BookPath = item.BookPath;
        Offset = item.Offset;
        Consequences = item.Consequences;
        IsChecked = item.IsApplied;
    }
}
