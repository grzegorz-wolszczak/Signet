using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Signet.Core.Fonts;

namespace Signet.App.ViewModels;

/// <summary>
/// The window shown before font files are deleted: the files, the <c>@font-face</c> rules that will be removed or
/// shortened, the places that use the removed families (each change can be unchecked) and the notes about what cannot
/// be fixed automatically or which faces a family loses.
/// </summary>
public sealed partial class FontRemovalViewModel : ObservableObject
{
    private readonly Action<string, int>? _navigate;

    /// <summary>Creates the window for <paramref name="plan"/>; <paramref name="fileNames"/> are all the files being deleted.</summary>
    public FontRemovalViewModel(FontRemovalPlan plan, IReadOnlyList<string> fileNames, Action<string, int>? navigate)
    {
        ArgumentNullException.ThrowIfNull(plan);
        ArgumentNullException.ThrowIfNull(fileNames);
        Plan = plan;
        _navigate = navigate;
        FilesText = string.Join("\n", fileNames.Select(f => "  • " + f));
        foreach (FontRemovalItem item in plan.RuleItems)
        {
            RuleItems.Add(new FontRemovalItemViewModel(item));
        }

        foreach (FontRemovalItem item in plan.UsageItems)
        {
            UsageItems.Add(new FontRemovalItemViewModel(item));
        }

        foreach (FontRemovalItem item in plan.Notes)
        {
            Notes.Add(new FontRemovalItemViewModel(item));
        }
    }

    /// <summary>The plan the window shows.</summary>
    public FontRemovalPlan Plan { get; }

    /// <summary>The files being deleted, one per line.</summary>
    public string FilesText { get; }

    /// <summary>The changes of <c>@font-face</c> rules.</summary>
    public ObservableCollection<FontRemovalItemViewModel> RuleItems { get; } = new();

    /// <summary>The changes of the places that use the removed families.</summary>
    public ObservableCollection<FontRemovalItemViewModel> UsageItems { get; } = new();

    /// <summary>The families that lose some faces.</summary>
    public ObservableCollection<FontRemovalItemViewModel> Notes { get; } = new();

    /// <summary>Whether there are rule changes.</summary>
    public bool HasRuleItems => RuleItems.Count > 0;

    /// <summary>Whether there are usage changes.</summary>
    public bool HasUsageItems => UsageItems.Count > 0;

    /// <summary>Whether there are notes.</summary>
    public bool HasNotes => Notes.Count > 0;

    /// <summary>The keys of the unchecked changes (they are not applied).</summary>
    public IReadOnlySet<string> ExcludedKeys() =>
        RuleItems.Concat(UsageItems)
            .Where(i => i.IsActionable && !i.IsChecked)
            .Select(i => i.Item.Key)
            .ToHashSet(StringComparer.Ordinal);

    /// <summary>Opens the place of an item in the main window.</summary>
    public void Navigate(FontRemovalItemViewModel item)
    {
        ArgumentNullException.ThrowIfNull(item);
        _navigate?.Invoke(item.Item.BookPath, item.Item.Offset);
    }

    [RelayCommand]
    private void SelectAllUsages() => SetUsages(true);

    [RelayCommand]
    private void SelectNoUsages() => SetUsages(false);

    private void SetUsages(bool value)
    {
        foreach (FontRemovalItemViewModel item in UsageItems.Where(i => i.IsActionable))
        {
            item.IsChecked = value;
        }
    }
}

/// <summary>One change (or note) in the font removal window.</summary>
public sealed partial class FontRemovalItemViewModel : ObservableObject
{
    /// <summary>Wraps <paramref name="item"/>; actionable items start checked.</summary>
    public FontRemovalItemViewModel(FontRemovalItem item)
    {
        ArgumentNullException.ThrowIfNull(item);
        Item = item;
        _isChecked = item.IsActionable;
    }

    /// <summary>The item.</summary>
    public FontRemovalItem Item { get; }

    /// <summary>Whether the item changes the book (a note has no check box).</summary>
    public bool IsActionable => Item.IsActionable;

    /// <summary>Whether the item is only a note (shown with ⚠).</summary>
    public bool IsNote => !Item.IsActionable;

    /// <summary>The description.</summary>
    public string Text => Item.Text;

    /// <summary>The file name of the place.</summary>
    public string FileText => Path.GetFileName(Item.BookPath);

    /// <summary>Whether the change is applied.</summary>
    [ObservableProperty]
    private bool _isChecked;
}
