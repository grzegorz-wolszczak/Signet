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
/// JetBrains IDEs. The root "Found usages — N results in M files" holds either a flat list of usages
/// (<c>path:line:column</c>) or — grouped by file (<see cref="GroupByFile"/>, remembered in the settings) — one node per
/// file with its usages (<c>line, column</c>). Double-clicking a usage raises <see cref="UsageActivated"/>; "Refresh"
/// raises <see cref="RefreshRequested"/> to search for the same class again.
/// </summary>
/// <remarks>
/// Besides the usages of a class the panel shows the matches of a Find &amp; Replace search ("Count All",
/// <see cref="LoadText"/>): the same tree, with a header that describes the search; "Refresh" runs that search again.
/// The view shows the tree in a virtualizing <c>TreeDataGrid</c> (a <c>TreeView</c> creates a control for every node,
/// which takes minutes for tens of thousands of usages) with the columns Usage, Line, Column, Kind and Context of
/// <see cref="FindUsagesNode"/>.
/// </remarks>
public sealed partial class FindUsagesViewModel : ViewModelBase, ILanguageAware
{
    private readonly SettingsStore _settings;
    private IReadOnlyList<ClassUsage> _usages = Array.Empty<ClassUsage>();

    // The search shown when it is a text search (Find & Replace): the header (built in the current language) and how to
    // run it again. Both null for a class search and when there is no search.
    private Func<string>? _textHeader;
    private Func<IReadOnlyList<ClassUsage>>? _rerunText;
    private string _textSearchKey = string.Empty;

    [ObservableProperty]
    private bool _groupByFile;

    /// <summary>
    /// Whether the text of a cell that does not fit wraps onto more lines (the row grows) instead of being cut with an
    /// ellipsis; remembered in the settings.
    /// </summary>
    [ObservableProperty]
    private bool _wrapRows;

    /// <summary>
    /// Whether the "Usage" column is as wide as its widest entry (Preferences › Appearance and Behavior › Find Usages
    /// Window); re-read from the settings by <see cref="ReloadSettings"/>.
    /// </summary>
    [ObservableProperty]
    private bool _fitUsageColumn;

    /// <summary>
    /// The extra space (px) between the rows, outside the backgrounds of the cells (Preferences › Appearance and Behavior ›
    /// Find Usages Window); re-read from the settings by <see cref="ReloadSettings"/>.
    /// </summary>
    [ObservableProperty]
    private int _rowSpacing;

    /// <summary>Creates the view model with the remembered grouping, wrapping and column width.</summary>
    public FindUsagesViewModel(SettingsStore settings)
    {
        _settings = settings ?? throw new ArgumentNullException(nameof(settings));
        _groupByFile = settings.FindUsagesGroupByFile;
        _wrapRows = settings.FindUsagesWrapRows;
        _fitUsageColumn = settings.FindUsagesFitUsageColumn;
        _rowSpacing = settings.FindUsagesRowSpacing;
        RefreshCommand = new RelayCommand(Refresh, () => HasSearch);
        ExpandAllCommand = new RelayCommand(() => SetExpanded(true), () => HasSearch);
        CollapseAllCommand = new RelayCommand(() => SetExpanded(false), () => HasSearch);
        settings.SettingChanged += (_, e) =>
        {
            if (e.QualifiedKey.Contains("find_usages_font", StringComparison.Ordinal)
                || e.QualifiedKey.Contains("find_usages_appearance", StringComparison.Ordinal))
            {
                AppearanceChanged?.Invoke(this, EventArgs.Empty);
            }
        };
    }

    /// <summary>The fonts of the panel now (Preferences › Appearance and Behavior › Find Usages Window › Fonts).</summary>
    public FindUsagesAppearance Appearance => _settings.FindUsagesAppearance;

    /// <summary>Raised when a font of the panel changed (the view applies <see cref="Appearance"/> again).</summary>
    public event EventHandler? AppearanceChanged;

    /// <summary>
    /// The width (px) the user gave the "Usage" column, remembered between sessions and used while
    /// <see cref="FitUsageColumn"/> is off; 0 = none yet.
    /// </summary>
    public int UsageColumnWidth
    {
        get => _settings.FindUsagesUsageColumnWidth;
        set => _settings.FindUsagesUsageColumnWidth = value;
    }

    /// <summary>Raised by "Refresh" — the host searches for <see cref="ClassName"/> again and calls <see cref="Load"/>.</summary>
    public event EventHandler? RefreshRequested;

    /// <summary>Raised when a usage is double-clicked — the host opens the file with the caret on the usage.</summary>
    public event EventHandler<ClassUsage>? UsageActivated;

    /// <summary>The class searched for, or <c>null</c> before the first search and for a text search.</summary>
    public string? ClassName { get; private set; }

    /// <summary>Whether the panel shows the matches of a text search (<see cref="LoadText"/>).</summary>
    public bool HasTextSearch => _textHeader is not null;

    /// <summary>Whether there is a search to show (otherwise a hint).</summary>
    public bool HasSearch => ClassName is not null || HasTextSearch;

    /// <summary>
    /// The heading: "Usages of class 'name'", the description of the text search, or the hint how to search.
    /// </summary>
    public string Header => ClassName is not null
        ? Strings.Format("FindUsages_Header", ClassName)
        : _textHeader?.Invoke() ?? Strings.Get("FindUsages_Empty");

    /// <summary>The tree: a single root "Found usages".</summary>
    public ObservableCollection<FindUsagesNode> Roots { get; } = new();

    /// <summary>Searches for the same class again.</summary>
    public IRelayCommand RefreshCommand { get; }

    /// <summary>Expands every node of the tree.</summary>
    public IRelayCommand ExpandAllCommand { get; }

    /// <summary>Collapses every node of the tree, down to the root "Found usages".</summary>
    public IRelayCommand CollapseAllCommand { get; }

    /// <summary>
    /// Shows the usages of <paramref name="className"/>. For the class already shown (e.g. "Refresh") the collapsed
    /// nodes stay collapsed; another class starts fully expanded.
    /// </summary>
    public void Load(string className, IReadOnlyList<ClassUsage> usages)
    {
        ArgumentException.ThrowIfNullOrEmpty(className);
        ArgumentNullException.ThrowIfNull(usages);
        bool sameClass = string.Equals(className, ClassName, StringComparison.Ordinal);
        ClassName = className;
        _textHeader = null;
        _rerunText = null;
        _textSearchKey = string.Empty;
        _usages = usages;
        Rebuild(keepCollapsed: sameClass);
        NotifySearchChanged();
    }

    /// <summary>
    /// Shows the matches of a text search (Find &amp; Replace "Count All"). For the search already shown (e.g. "Refresh")
    /// the collapsed nodes stay collapsed; another search starts fully expanded.
    /// </summary>
    /// <param name="header">The description of the search (built in the current language).</param>
    /// <param name="usages">The matches (<see cref="ClassUsageKind.Text"/>).</param>
    /// <param name="rerun">Runs the search again, for "Refresh".</param>
    public void LoadText(Func<string> header, IReadOnlyList<ClassUsage> usages, Func<IReadOnlyList<ClassUsage>> rerun)
    {
        ArgumentNullException.ThrowIfNull(header);
        ArgumentNullException.ThrowIfNull(usages);
        ArgumentNullException.ThrowIfNull(rerun);
        string key = header();
        bool sameSearch = HasTextSearch && string.Equals(key, _textSearchKey, StringComparison.Ordinal);
        ClassName = null;
        _textHeader = header;
        _rerunText = rerun;
        _textSearchKey = key;
        _usages = usages;
        Rebuild(keepCollapsed: sameSearch);
        NotifySearchChanged();
    }

    /// <summary>
    /// Runs the shown search again. A text search is run here; a class search is left to the host
    /// (<see cref="RefreshRequested"/>), which knows the book.
    /// </summary>
    public void Refresh()
    {
        if (_textHeader is { } header && _rerunText is { } rerun)
        {
            LoadText(header, rerun(), rerun);
        }
        else
        {
            RefreshRequested?.Invoke(this, EventArgs.Empty);
        }
    }

    /// <summary>Forgets the search (e.g. another book was opened).</summary>
    public void Clear()
    {
        ClassName = null;
        _textHeader = null;
        _rerunText = null;
        _textSearchKey = string.Empty;
        _usages = Array.Empty<ClassUsage>();
        Roots.Clear();
        NotifySearchChanged();
    }

    /// <summary>Opens the usage of <paramref name="node"/>; nodes without a location (root, file) do nothing.</summary>
    public void Activate(FindUsagesNode? node)
    {
        if (node?.Usage is { } usage)
        {
            UsageActivated?.Invoke(this, usage);
        }
    }

    /// <summary>
    /// Reads the settings of Preferences again (after "Save" there): whether the "Usage" column fits its content and the
    /// space between the rows.
    /// </summary>
    public void ReloadSettings()
    {
        FitUsageColumn = _settings.FindUsagesFitUsageColumn;
        RowSpacing = _settings.FindUsagesRowSpacing;
    }

    /// <inheritdoc/>
    public void OnLanguageChanged()
    {
        if (HasSearch)
        {
            Rebuild(keepCollapsed: true);
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

    /// <summary>"in 1 file" / "in 2 files" — with the Polish forms (w 1 pliku, w 2 plikach).</summary>
    public static string FilesText(int count) =>
        Strings.Format(count == 1 ? "FindUsages_InFiles_One" : "FindUsages_InFiles_Many", count);

    partial void OnWrapRowsChanged(bool value) => _settings.FindUsagesWrapRows = value;

    partial void OnGroupByFileChanged(bool value)
    {
        _settings.FindUsagesGroupByFile = value;
        if (HasSearch)
        {
            Rebuild(keepCollapsed: true);
        }
    }

    private void NotifySearchChanged()
    {
        OnPropertyChanged(nameof(ClassName));
        OnPropertyChanged(nameof(HasTextSearch));
        OnPropertyChanged(nameof(HasSearch));
        OnPropertyChanged(nameof(Header));
        NotifyCommands();
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
            if (!node.HasChildren)
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

    // Identifies a node with children across rebuilds: the root, or a file node by its bookpath.
    private static string ExpansionKey(FindUsagesNode node) => node.Parent is null ? string.Empty : node.ToolTip ?? node.Text;

    // With keepCollapsed, the root / file nodes collapsed in the current tree are collapsed in the new one too.
    private void Rebuild(bool keepCollapsed)
    {
        HashSet<string> collapsed = new(StringComparer.Ordinal);
        if (keepCollapsed)
        {
            Stack<FindUsagesNode> pending = new(Roots);
            while (pending.TryPop(out FindUsagesNode? node))
            {
                if (!node.HasChildren)
                {
                    continue;
                }

                if (!node.IsExpanded)
                {
                    collapsed.Add(ExpansionKey(node));
                }

                foreach (FindUsagesNode child in node.Children)
                {
                    pending.Push(child);
                }
            }
        }

        int fileCount = _usages.Select(u => u.BookPath).Distinct(StringComparer.Ordinal).Count();
        FindUsagesNode root = new(
            null, Strings.Get("FindUsages_Found"), $"{ResultsText(_usages.Count)} {FilesText(fileCount)}", null, null);
        if (GroupByFile)
        {
            foreach (IGrouping<string, ClassUsage> file in _usages.GroupBy(u => u.BookPath, StringComparer.Ordinal))
            {
                FindUsagesNode fileNode = new(root, file.Key, ResultsText(file.Count()), null, file.Key);
                foreach (ClassUsage usage in file)
                {
                    _ = new FindUsagesNode(
                        fileNode, Strings.Format("FindUsages_LineColumn", usage.Line, usage.Column), string.Empty, usage, usage.BookPath);
                }
            }
        }
        else
        {
            foreach (ClassUsage usage in _usages)
            {
                _ = new FindUsagesNode(root, $"{usage.BookPath}:{usage.Line}:{usage.Column}", string.Empty, usage, usage.BookPath);
            }
        }

        foreach (FindUsagesNode node in root.Children.Prepend(root))
        {
            if (node.HasChildren && collapsed.Contains(ExpansionKey(node)))
            {
                node.IsExpanded = false;
            }
        }

        Roots.Clear();
        Roots.Add(root);
    }
}

/// <summary>
/// A node of the "Find Usages" tree: the root, a file or a usage (a location to open). The usage columns (line, column,
/// kind, context) are empty for the root and a file.
/// </summary>
public sealed partial class FindUsagesNode : ObservableObject
{
    private readonly List<FindUsagesNode> _children = new();

    [ObservableProperty]
    private bool _isExpanded = true;

    /// <summary>Creates the node and appends it to the children of <paramref name="parent"/>.</summary>
    internal FindUsagesNode(FindUsagesNode? parent, string text, string countText, ClassUsage? usage, string? toolTip)
    {
        Parent = parent;
        Text = text;
        CountText = countText;
        Usage = usage;
        ToolTip = toolTip;
        parent?._children.Add(this);
    }

    /// <summary>The parent node, or <c>null</c> for the root.</summary>
    public FindUsagesNode? Parent { get; }

    /// <summary>Whether the node has children (and so an expander).</summary>
    public bool HasChildren => _children.Count > 0;

    /// <summary>The label.</summary>
    public string Text { get; }

    /// <summary>"N results" after the label of the root and of a file; empty for a usage.</summary>
    public string CountText { get; }

    /// <summary>The dimmed text after the label (<see cref="CountText"/> with a gap); empty for a usage.</summary>
    public string CountSuffix => CountText.Length == 0 ? string.Empty : "  " + CountText;

    /// <summary>The usage (a location to open), or <c>null</c> for the root and a file.</summary>
    public ClassUsage? Usage { get; }

    /// <summary>The tooltip (the bookpath of the file).</summary>
    public string? ToolTip { get; }

    /// <summary>The "Line" column (1-based).</summary>
    public int? Line => Usage?.Line;

    /// <summary>The "Col." column (1-based).</summary>
    public int? Column => Usage?.Column;

    /// <summary>The "Kind" column: a class attribute or a CSS selector.</summary>
    public string KindText => Usage?.Kind switch
    {
        ClassUsageKind.ClassAttribute => Strings.Get("FindUsages_KindAttribute"),
        ClassUsageKind.Selector => Strings.Get("FindUsages_KindSelector"),
        ClassUsageKind.Text => Strings.Get("FindUsages_KindText"),
        _ => string.Empty,
    };

    /// <summary>The "Context" column: the text of the usage's line.</summary>
    public string Context => Usage?.Context ?? string.Empty;

    /// <summary>The nesting of the node: 0 for the root, 1 for a file (or a usage of a flat list), 2 for a usage in a file.</summary>
    public int Level => Parent is null ? 0 : Parent.Level + 1;

    /// <summary>The child nodes.</summary>
    public IReadOnlyList<FindUsagesNode> Children => _children;

    /// <summary>
    /// The sort order of the "Usage" column: by file (bookpath), then by line and column, compared as numbers — the
    /// labels ("12:5", "Text/ch1.xhtml:100:3") would put line 100 before line 20 as text. The root and the files have
    /// no line, so a file sorts before its usages.
    /// </summary>
    public static int CompareByLocation(FindUsagesNode? x, FindUsagesNode? y)
    {
        if (ReferenceEquals(x, y))
        {
            return 0;
        }

        if (x is null || y is null)
        {
            return x is null ? -1 : 1;
        }

        int byFile = string.CompareOrdinal(x.Usage?.BookPath ?? x.ToolTip, y.Usage?.BookPath ?? y.ToolTip);
        if (byFile != 0)
        {
            return byFile;
        }

        int byLine = (x.Line ?? 0).CompareTo(y.Line ?? 0);
        return byLine != 0 ? byLine : (x.Column ?? 0).CompareTo(y.Column ?? 0);
    }
}
