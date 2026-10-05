using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Signet.App.Resources;
using Signet.Core.BookManipulation;
using Signet.Core.Misc;

namespace Signet.App.ViewModels;

/// <summary>
/// View model of the "Find Usages" panel: the usages of a CSS class as a tree, like the Find tool window of the
/// JetBrains IDEs. The root "Found usages — N results" holds either a flat list of usages (<c>path:line:column</c>)
/// or — grouped by file (<see cref="GroupByFile"/>, remembered in the settings) — one node per file with its usages
/// (<c>line, column</c>). Double-clicking a usage raises <see cref="UsageActivated"/>; "Refresh" raises
/// <see cref="RefreshRequested"/> to search for the same class again.
/// </summary>
public sealed partial class FindUsagesViewModel : ViewModelBase, ILanguageAware
{
    private readonly SettingsStore _settings;
    private IReadOnlyList<ClassUsage> _usages = Array.Empty<ClassUsage>();

    [ObservableProperty]
    private bool _groupByFile;

    /// <summary>Creates the view model with the remembered grouping.</summary>
    public FindUsagesViewModel(SettingsStore settings)
    {
        _settings = settings ?? throw new ArgumentNullException(nameof(settings));
        _groupByFile = settings.FindUsagesGroupByFile;
        RefreshCommand = new RelayCommand(() => RefreshRequested?.Invoke(this, EventArgs.Empty), () => ClassName is not null);
        ExpandAllCommand = new RelayCommand(() => SetExpanded(true), () => ClassName is not null);
        CollapseAllCommand = new RelayCommand(() => SetExpanded(false), () => ClassName is not null);
    }

    /// <summary>Raised by "Refresh" — the host searches for <see cref="ClassName"/> again and calls <see cref="Load"/>.</summary>
    public event EventHandler? RefreshRequested;

    /// <summary>Raised when a usage is double-clicked — the host opens the file with the caret on the usage.</summary>
    public event EventHandler<ClassUsage>? UsageActivated;

    /// <summary>The class searched for, or <c>null</c> before the first search.</summary>
    public string? ClassName { get; private set; }

    /// <summary>Whether there is a search to show (otherwise a hint).</summary>
    public bool HasSearch => ClassName is not null;

    /// <summary>The heading: "Usages of class 'name'" or the hint how to search.</summary>
    public string Header => ClassName is null ? Strings.Get("FindUsages_Empty") : Strings.Format("FindUsages_Header", ClassName);

    /// <summary>The tree: a single root "Found usages".</summary>
    public ObservableCollection<FindUsagesNode> Roots { get; } = new();

    /// <summary>Searches for the same class again.</summary>
    public IRelayCommand RefreshCommand { get; }

    /// <summary>Expands every node of the tree.</summary>
    public IRelayCommand ExpandAllCommand { get; }

    /// <summary>Collapses every node of the tree, down to the root "Found usages".</summary>
    public IRelayCommand CollapseAllCommand { get; }

    /// <summary>Shows the usages of <paramref name="className"/>.</summary>
    public void Load(string className, IReadOnlyList<ClassUsage> usages)
    {
        ArgumentException.ThrowIfNullOrEmpty(className);
        ArgumentNullException.ThrowIfNull(usages);
        ClassName = className;
        _usages = usages;
        Rebuild();
        OnPropertyChanged(nameof(ClassName));
        OnPropertyChanged(nameof(HasSearch));
        OnPropertyChanged(nameof(Header));
        NotifyCommands();
    }

    /// <summary>Forgets the search (e.g. another book was opened).</summary>
    public void Clear()
    {
        ClassName = null;
        _usages = Array.Empty<ClassUsage>();
        Roots.Clear();
        OnPropertyChanged(nameof(ClassName));
        OnPropertyChanged(nameof(HasSearch));
        OnPropertyChanged(nameof(Header));
        NotifyCommands();
    }

    /// <summary>Opens the usage of <paramref name="node"/>; nodes without a location (root, file) do nothing.</summary>
    public void Activate(FindUsagesNode? node)
    {
        if (node?.Usage is { } usage)
        {
            UsageActivated?.Invoke(this, usage);
        }
    }

    /// <inheritdoc/>
    public void OnLanguageChanged()
    {
        if (ClassName is not null)
        {
            Rebuild();
        }

        OnPropertyChanged(nameof(Header));
    }

    /// <summary>"1 result" / "2 results" — with the Polish plural forms (1 wynik, 2–4 wyniki, 5 wyników).</summary>
    public static string ResultsText(int count)
    {
        int lastTwo = count % 100;
        int last = count % 10;
        string form = count == 1 ? "One"
            : last is >= 2 and <= 4 && lastTwo is < 12 or > 14 ? "Few"
            : "Many";
        return Strings.Format("FindUsages_Results_" + form, count);
    }

    partial void OnGroupByFileChanged(bool value)
    {
        _settings.FindUsagesGroupByFile = value;
        if (ClassName is not null)
        {
            Rebuild();
        }
    }

    private void NotifyCommands()
    {
        RefreshCommand.NotifyCanExecuteChanged();
        ExpandAllCommand.NotifyCanExecuteChanged();
        CollapseAllCommand.NotifyCanExecuteChanged();
    }

    // Only the nodes with children (the root and the files): a usage has nothing to expand.
    private void SetExpanded(bool expanded)
    {
        Stack<FindUsagesNode> pending = new(Roots);
        while (pending.TryPop(out FindUsagesNode? node))
        {
            if (node.Children.Count == 0)
            {
                continue;
            }

            node.IsExpanded = expanded;
            foreach (FindUsagesNode child in node.Children)
            {
                pending.Push(child);
            }
        }
    }

    private void Rebuild()
    {
        FindUsagesNode root = new(Strings.Get("FindUsages_Found"), ResultsText(_usages.Count), null, null);
        if (GroupByFile)
        {
            foreach (IGrouping<string, ClassUsage> file in _usages.GroupBy(u => u.BookPath, StringComparer.Ordinal))
            {
                FindUsagesNode fileNode = new(file.Key, ResultsText(file.Count()), null, file.Key);
                foreach (ClassUsage usage in file)
                {
                    fileNode.Children.Add(new FindUsagesNode(
                        Strings.Format("FindUsages_LineColumn", usage.Line, usage.Column), string.Empty, usage, usage.BookPath));
                }

                root.Children.Add(fileNode);
            }
        }
        else
        {
            foreach (ClassUsage usage in _usages)
            {
                root.Children.Add(new FindUsagesNode(
                    $"{usage.BookPath}:{usage.Line}:{usage.Column}", string.Empty, usage, usage.BookPath));
            }
        }

        Roots.Clear();
        Roots.Add(root);
    }
}

/// <summary>A node of the "Find Usages" tree: the root, a file or a usage (a location to open).</summary>
public sealed partial class FindUsagesNode : ObservableObject
{
    [ObservableProperty]
    private bool _isExpanded = true;

    internal FindUsagesNode(string text, string countText, ClassUsage? usage, string? toolTip)
    {
        Text = text;
        CountText = countText;
        Usage = usage;
        ToolTip = toolTip;
    }

    /// <summary>The label.</summary>
    public string Text { get; }

    /// <summary>"N results" after the label of the root and of a file; empty for a usage.</summary>
    public string CountText { get; }

    /// <summary>The usage (a location to open), or <c>null</c> for the root and a file.</summary>
    public ClassUsage? Usage { get; }

    /// <summary>The tooltip (the bookpath of the file).</summary>
    public string? ToolTip { get; }

    /// <summary>The child nodes.</summary>
    public ObservableCollection<FindUsagesNode> Children { get; } = new();
}
