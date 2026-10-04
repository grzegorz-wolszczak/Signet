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
/// View model of the "Cleanup" dialog: the steps grouped into independent tabs (<see cref="CleanupTabViewModel"/> —
/// CSS, HTML, Files), each with its own sections and its own "Clean" button. A section has a checkbox, a
/// description and — when enabled — a summary and an expandable list of the changes it will make.
/// Every toggle (of a section or of a single item) re-plans the tabs (<see cref="CleanupAnalysis.Plan"/>), because
/// the steps within a tab depend on each other. Each tab is planned only with its own steps, on the current state
/// of the book; cleaning a tab applies its plan and re-analyses the book, so the other tabs see the result.
/// Risky items (merges that would change the styling) are unchecked until the user checks them — that is the
/// explicit acceptance; the check state of every row always comes from the plan.
/// </summary>
public sealed partial class CleanupViewModel : ViewModelBase
{
    private readonly Action<string, int> _navigate;
    private readonly Func<CleanupPlan, IReadOnlyList<CleanupStep>, CleanupAnalysis?> _apply;
    private readonly HashSet<string> _excludedKeys = new(StringComparer.Ordinal);
    private readonly HashSet<string> _acceptedRiskyKeys = new(StringComparer.Ordinal);
    private CleanupAnalysis _analysis;
    private bool _updating;

    /// <summary>Creates the view model and computes the first plans.</summary>
    /// <param name="analysis">The prepared analysis of the book.</param>
    /// <param name="enabledSteps">The steps checked when the dialog opens (the remembered choice).</param>
    /// <param name="navigate">Opens a file at an offset (a double click on an item).</param>
    /// <param name="apply">
    /// Applies a tab's plan to the book and remembers the checked steps (the second argument); returns the analysis
    /// of the changed book, or <c>null</c> when it cannot be analysed any more (the dialog then closes).
    /// </param>
    public CleanupViewModel(
        CleanupAnalysis analysis,
        IEnumerable<CleanupStep> enabledSteps,
        Action<string, int> navigate,
        Func<CleanupPlan, IReadOnlyList<CleanupStep>, CleanupAnalysis?> apply)
    {
        ArgumentNullException.ThrowIfNull(analysis);
        ArgumentNullException.ThrowIfNull(enabledSteps);
        ArgumentNullException.ThrowIfNull(navigate);
        ArgumentNullException.ThrowIfNull(apply);

        _analysis = analysis;
        _navigate = navigate;
        _apply = apply;

        HashSet<CleanupStep> enabled = enabledSteps.ToHashSet();
        Tabs.Add(new CleanupTabViewModel(this, Strings.Get("CleanupWindow_Tab_Css"), CssSteps, enabled));
        Tabs.Add(new CleanupTabViewModel(this, Strings.Get("CleanupWindow_Tab_Html"), HtmlSteps, enabled));
        Tabs.Add(new CleanupTabViewModel(this, Strings.Get("CleanupWindow_Tab_Files"), FilesSteps, enabled));

        Replan();
    }

    /// <summary>Raised when the dialog has to close (the book could not be re-analysed after cleaning).</summary>
    public event EventHandler? CloseRequested;

    /// <summary>The steps of the CSS tab, in execution order.</summary>
    public static IReadOnlyList<CleanupStep> CssSteps { get; } = new[]
    {
        CleanupStep.UnreferencedStylesheets,
        CleanupStep.UnusedSelectors,
        CleanupStep.MergeSameSelectors,
        CleanupStep.MergeSameProperties,
    };

    /// <summary>The steps of the HTML tab, in execution order.</summary>
    public static IReadOnlyList<CleanupStep> HtmlSteps { get; } = new[] { CleanupStep.NestedDivs };

    /// <summary>The steps of the Files tab, in execution order.</summary>
    public static IReadOnlyList<CleanupStep> FilesSteps { get; } = new[] { CleanupStep.UnusedMedia };

    /// <summary>The tabs: CSS, HTML, Files.</summary>
    public ObservableCollection<CleanupTabViewModel> Tabs { get; } = new();

    /// <summary>All sections of all tabs, in execution order.</summary>
    public IEnumerable<CleanupSectionViewModel> Sections => Tabs.SelectMany(t => t.Sections);

    /// <summary>The steps currently checked in all tabs (remembered for the next time).</summary>
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

    internal void OnSectionToggled() => Replan();

    internal void OnItemToggled(CleanupItemViewModel item)
    {
        if (_updating)
        {
            return;
        }

        Remember(item, item.IsChecked);
        Replan();
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

        Replan();
    }

    /// <summary>Applies the plan of <paramref name="tab"/> and re-plans every tab on the changed book.</summary>
    internal void Clean(CleanupTabViewModel tab)
    {
        if (!tab.HasChanges)
        {
            return;
        }

        CleanupAnalysis? analysis = _apply(tab.Plan, EnabledSteps);
        if (analysis is null)
        {
            CloseRequested?.Invoke(this, EventArgs.Empty);
            return;
        }

        _analysis = analysis;
        Replan();
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

    // Every tab is planned with its own steps only, so the tabs stay independent of each other.
    private void Replan()
    {
        _updating = true;
        try
        {
            foreach (CleanupTabViewModel tab in Tabs)
            {
                HashSet<CleanupStep> enabled = tab.Sections.Where(s => s.IsEnabled).Select(s => s.Step).ToHashSet();
                tab.Update(_analysis.Plan(enabled, _excludedKeys, _acceptedRiskyKeys));
            }
        }
        finally
        {
            _updating = false;
        }
    }
}

/// <summary>
/// One tab of the Cleanup dialog: a group of steps planned and cleaned together, independently of the other tabs.
/// </summary>
public sealed partial class CleanupTabViewModel : ObservableObject
{
    [ObservableProperty]
    private bool _hasChanges;

    internal CleanupTabViewModel(CleanupViewModel owner, string header, IEnumerable<CleanupStep> steps, IReadOnlySet<CleanupStep> enabled)
    {
        Header = header;
        foreach (CleanupStep step in steps)
        {
            Sections.Add(new CleanupSectionViewModel(owner, step, enabled.Contains(step)));
        }

        CleanCommand = new RelayCommand(() => owner.Clean(this), () => HasChanges);
    }

    /// <summary>The tab header.</summary>
    public string Header { get; }

    /// <summary>The sections of the tab, in execution order.</summary>
    public ObservableCollection<CleanupSectionViewModel> Sections { get; } = new();

    /// <summary>The current plan of the tab — applied by <see cref="CleanCommand"/>.</summary>
    public CleanupPlan Plan { get; private set; } = null!;

    /// <summary>Applies <see cref="Plan"/> to the book ("Clean").</summary>
    public IRelayCommand CleanCommand { get; }

    partial void OnHasChangesChanged(bool value) => CleanCommand.NotifyCanExecuteChanged();

    internal void Update(CleanupPlan plan)
    {
        Plan = plan;
        foreach (CleanupSectionViewModel section in Sections)
        {
            section.Update(plan.GetStep(section.Step));
        }

        HasChanges = plan.HasChanges;
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
        Warning = step switch
        {
            CleanupStep.MergeSameSelectors or CleanupStep.MergeSameProperties => Strings.Get("Cleanup_MergeWarning"),
            CleanupStep.NestedDivs => Strings.Get("Cleanup_NestedDivsWarning"),
            _ => string.Empty,
        };
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

        string summary = Step is CleanupStep.MergeSameSelectors or CleanupStep.MergeSameProperties or CleanupStep.NestedDivs
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
