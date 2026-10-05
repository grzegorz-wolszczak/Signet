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
/// View model of the "Standardize EPUB" dialog: one section per <see cref="StandardizationStep"/>, in the order the
/// steps run, each with a checkbox, a detailed description (the ⓘ tooltip) and — when checked — a summary and an
/// expandable list of the changes it will make. Every toggle re-plans all steps (<see cref="EpubStandardization.Plan"/>),
/// because each step sees the result of the steps before it. "Apply" runs the checked steps and closes the dialog.
/// </summary>
public sealed partial class StandardizationViewModel : ViewModelBase
{
    private readonly Func<IReadOnlyList<StandardizationStep>, StandardizationPlan> _plan;
    private readonly Action<IReadOnlyList<StandardizationStep>> _apply;
    private bool _togglingSections;

    [ObservableProperty]
    private bool _hasChanges;

    /// <summary>Creates the view model and computes the first plan.</summary>
    /// <param name="enabledSteps">The steps checked when the dialog opens (the remembered choice).</param>
    /// <param name="plan">Plans the given steps on the book without changing it.</param>
    /// <param name="apply">Runs the given steps on the book (and remembers them as the checked steps).</param>
    public StandardizationViewModel(
        IEnumerable<StandardizationStep> enabledSteps,
        Func<IReadOnlyList<StandardizationStep>, StandardizationPlan> plan,
        Action<IReadOnlyList<StandardizationStep>> apply)
    {
        ArgumentNullException.ThrowIfNull(enabledSteps);
        ArgumentNullException.ThrowIfNull(plan);
        ArgumentNullException.ThrowIfNull(apply);

        _plan = plan;
        _apply = apply;
        HashSet<StandardizationStep> enabled = enabledSteps.ToHashSet();
        foreach (StandardizationStep step in EpubStandardization.AllSteps)
        {
            Sections.Add(new StandardizationSectionViewModel(this, step, enabled.Contains(step)));
        }

        ApplyCommand = new RelayCommand(Apply, () => HasChanges);
        SelectAllCommand = new RelayCommand(() => SetAllSections(true));
        SelectNoneCommand = new RelayCommand(() => SetAllSections(false));
        Replan();
    }

    /// <summary>Raised when the dialog has to close (after "Apply").</summary>
    public event EventHandler? CloseRequested;

    /// <summary>The sections, in the order the steps run.</summary>
    public ObservableCollection<StandardizationSectionViewModel> Sections { get; } = new();

    /// <summary>The steps currently checked.</summary>
    public IReadOnlyList<StandardizationStep> EnabledSteps =>
        Sections.Where(s => s.IsEnabled).Select(s => s.Step).ToList();

    /// <summary>Runs the checked steps on the book and closes the dialog.</summary>
    public IRelayCommand ApplyCommand { get; }

    /// <summary>Checks every step.</summary>
    public IRelayCommand SelectAllCommand { get; }

    /// <summary>Unchecks every step.</summary>
    public IRelayCommand SelectNoneCommand { get; }

    partial void OnHasChangesChanged(bool value) => ApplyCommand.NotifyCanExecuteChanged();

    internal void OnSectionToggled()
    {
        if (!_togglingSections)
        {
            Replan();
        }
    }

    private void SetAllSections(bool isEnabled)
    {
        _togglingSections = true;
        try
        {
            foreach (StandardizationSectionViewModel section in Sections)
            {
                section.IsEnabled = isEnabled;
            }
        }
        finally
        {
            _togglingSections = false;
        }

        Replan();
    }

    private void Apply()
    {
        if (!HasChanges)
        {
            return;
        }

        _apply(EnabledSteps);
        CloseRequested?.Invoke(this, EventArgs.Empty);
    }

    private void Replan()
    {
        StandardizationPlan plan = _plan(EnabledSteps);
        foreach (StandardizationSectionViewModel section in Sections)
        {
            section.Update(plan.For(section.Step));
        }

        HasChanges = plan.HasChanges;
    }
}

/// <summary>One step of the "Standardize EPUB" dialog.</summary>
public sealed partial class StandardizationSectionViewModel : ObservableObject
{
    private readonly StandardizationViewModel _owner;

    [ObservableProperty]
    private bool _isEnabled;

    [ObservableProperty]
    private bool _isExpanded;

    [ObservableProperty]
    private string _summary = string.Empty;

    [ObservableProperty]
    private bool _hasItems;

    internal StandardizationSectionViewModel(StandardizationViewModel owner, StandardizationStep step, bool isEnabled)
    {
        _owner = owner;
        Step = step;
        _isEnabled = isEnabled;
        Title = Strings.Get($"StandardizeStep_{step}_Title");
        Description = Strings.Get($"StandardizeStep_{step}_Description");
        Info = Strings.Get($"StandardizeStep_{step}_Info");
    }

    /// <summary>The step of this section.</summary>
    public StandardizationStep Step { get; }

    /// <summary>The checkbox label.</summary>
    public string Title { get; }

    /// <summary>What the step does, in one sentence.</summary>
    public string Description { get; }

    /// <summary>What the step does in detail — the ⓘ tooltip.</summary>
    public string Info { get; }

    /// <summary>The changes the step will make (once the steps before it have run).</summary>
    public ObservableCollection<StandardizationItemViewModel> Items { get; } = new();

    /// <summary>The kind of change the step is about (the others are side effects, e.g. IDs of renamed files).</summary>
    private StandardizationChangeKind MainKind => Step switch
    {
        StandardizationStep.StandardFolders or StandardizationStep.StandardFileExtensions => StandardizationChangeKind.Path,
        StandardizationStep.RebaseManifestIds => StandardizationChangeKind.ManifestId,
        _ => StandardizationChangeKind.MediaType,
    };

    partial void OnIsEnabledChanged(bool value) => _owner.OnSectionToggled();

    internal void Update(StandardizationStepPlan? plan)
    {
        IReadOnlyList<StandardizationChange> changes = plan?.Changes ?? Array.Empty<StandardizationChange>();
        Items.Clear();
        foreach (StandardizationChange change in changes)
        {
            Items.Add(new StandardizationItemViewModel(change));
        }

        HasItems = Items.Count > 0;
        Summary = plan is null ? string.Empty : FormatSummary(changes);
    }

    private string FormatSummary(IReadOnlyList<StandardizationChange> changes)
    {
        if (changes.Count == 0)
        {
            return Strings.Get("StandardizeWindow_NothingToDo");
        }

        int main = changes.Count(c => c.Kind == MainKind);
        int sideIds = changes.Count - main;
        string summary = Strings.Format($"StandardizeStep_{Step}_Summary", main);
        return sideIds > 0 ? summary + Strings.Format("StandardizeWindow_Summary_SideIds", sideIds) : summary;
    }
}

/// <summary>One change in a step's report.</summary>
public sealed class StandardizationItemViewModel
{
    internal StandardizationItemViewModel(StandardizationChange change)
    {
        Change = change;
        Text = change.Kind == StandardizationChangeKind.ManifestId
            ? Strings.Format("StandardizeWindow_Row_Id", change.Before, change.After)
            : change.Before + " → " + change.After;
        FileText = change.Kind == StandardizationChangeKind.Path ? string.Empty : change.BookPath;
    }

    /// <summary>The planned change.</summary>
    public StandardizationChange Change { get; }

    /// <summary>"before → after".</summary>
    public string Text { get; }

    /// <summary>The file the change concerns; empty when <see cref="Text"/> already names it (a moved or renamed file).</summary>
    public string FileText { get; }
}
