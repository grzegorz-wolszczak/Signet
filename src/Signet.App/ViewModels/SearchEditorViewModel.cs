using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Globalization;
using System.IO;
using System.Linq;
using System;
using CommunityToolkit.Mvvm.Input;
using Signet.App.Resources;
using Signet.Core.MiscEditors;

namespace Signet.App.ViewModels;

/// <summary>
/// View model of the "Saved Searches" panel: the tree of groups and entries, CRUD / reorder /
/// import / export operations and batch execution of saved searches through
/// <see cref="FindReplaceViewModel"/>.
/// </summary>
public sealed class SearchEditorViewModel : ViewModelBase
{
    private readonly SearchEditorModel _model = new();
    private readonly SavedSearchStore _store;
    private readonly FindReplaceViewModel _findReplace;

    private string _filterText = string.Empty;
    private bool _filterAll;
    private string _message = string.Empty;
    private string _reportSummary = string.Empty;
    private SearchEntryNodeViewModel? _selectedNode;
    private IReadOnlyList<SearchEntryNodeViewModel> _selectedNodes = Array.Empty<SearchEntryNodeViewModel>();

    /// <summary>Creates the view model and loads the saved searches from the store.</summary>
    public SearchEditorViewModel(SavedSearchStore store, FindReplaceViewModel findReplace)
    {
        _store = store ?? throw new ArgumentNullException(nameof(store));
        _findReplace = findReplace ?? throw new ArgumentNullException(nameof(findReplace));

        AddEntryCommand = new RelayCommand(AddEntry);
        AddGroupCommand = new RelayCommand(AddGroup);
        DeleteCommand = new RelayCommand(DeleteSelected);
        MoveUpCommand = new RelayCommand(() => Move(_model.MoveUp));
        MoveDownCommand = new RelayCommand(() => Move(_model.MoveDown));
        MoveLeftCommand = new RelayCommand(() => Move(_model.MoveLeft));
        MoveRightCommand = new RelayCommand(() => Move(_model.MoveRight));
        LoadSearchCommand = new RelayCommand(LoadSelected);
        FindCommand = new RelayCommand(() => RunBatch(_findReplace.RunSavedFind));
        ReplaceCurrentCommand = new RelayCommand(() => RunBatch(_findReplace.RunSavedReplaceCurrent));
        ReplaceCommand = new RelayCommand(() => RunBatch(_findReplace.RunSavedReplaceFind));
        ReplaceAllCommand = new RelayCommand(() => RunBatchInt(_findReplace.RunSavedReplaceAll));
        CountAllCommand = new RelayCommand(() => RunBatchInt(_findReplace.RunSavedCountAll));
        CountsReportCommand = new RelayCommand(CountsReport);
        CloseReportCommand = new RelayCommand(CloseReport);
        FillControlsCommand = new RelayCommand(FillControls);
        SaveCommand = new RelayCommand(Save);
        ReloadCommand = new RelayCommand(Reload);
        SaveCurrentSearchCommand = new RelayCommand(SaveCurrentSearch);
        CollapseAllCommand = new RelayCommand(() => SetExpandedAll(false));
        ExpandAllCommand = new RelayCommand(() => SetExpandedAll(true));
        ImportCommand = new RelayCommand(() => ImportRequested?.Invoke(this, EventArgs.Empty));
        ExportCommand = new RelayCommand(() => ExportRequested?.Invoke(this, false));
        ExportAllCommand = new RelayCommand(() => ExportRequested?.Invoke(this, true));

        Load();
    }

    /// <summary>Raised when the view should show the file picker for import.</summary>
    public event EventHandler? ImportRequested;

    /// <summary>Raised when the view should show the file picker for export (selected entries / everything).</summary>
    public event EventHandler<bool>? ExportRequested;

    /// <summary>Top-level tree nodes.</summary>
    public ObservableCollection<SearchEntryNodeViewModel> Nodes { get; } = new();

    /// <summary>Adds a new empty entry (after the selected item).</summary>
    public IRelayCommand AddEntryCommand { get; }

    /// <summary>Adds a new group.</summary>
    public IRelayCommand AddGroupCommand { get; }

    /// <summary>Deletes the selected entries.</summary>
    public IRelayCommand DeleteCommand { get; }

    /// <summary>Moves the selected entry up.</summary>
    public IRelayCommand MoveUpCommand { get; }

    /// <summary>Moves the selected entry down.</summary>
    public IRelayCommand MoveDownCommand { get; }

    /// <summary>Moves the selected entry to the parent's level.</summary>
    public IRelayCommand MoveLeftCommand { get; }

    /// <summary>Moves the selected entry into the group above it.</summary>
    public IRelayCommand MoveRightCommand { get; }

    /// <summary>Loads the selected entry into the Find &amp; Replace panel.</summary>
    public IRelayCommand LoadSearchCommand { get; }

    /// <summary>Runs Find for the selected entries.</summary>
    public IRelayCommand FindCommand { get; }

    /// <summary>Runs Replace for the first selected entry.</summary>
    public IRelayCommand ReplaceCurrentCommand { get; }

    /// <summary>Runs Replace/Find for the selected entries.</summary>
    public IRelayCommand ReplaceCommand { get; }

    /// <summary>Runs Replace All for the selected entries sequentially.</summary>
    public IRelayCommand ReplaceAllCommand { get; }

    /// <summary>Runs Count All for the selected entries sequentially.</summary>
    public IRelayCommand CountAllCommand { get; }

    /// <summary>Builds a report of match counts per entry.</summary>
    public IRelayCommand CountsReportCommand { get; }

    /// <summary>Closes (clears) the match count report.</summary>
    public IRelayCommand CloseReportCommand { get; }

    /// <summary>Copies the options from the first selected entry to the others.</summary>
    public IRelayCommand FillControlsCommand { get; }

    /// <summary>Saves the search library to a file.</summary>
    public IRelayCommand SaveCommand { get; }

    /// <summary>Reloads the library from the file (discards unsaved changes).</summary>
    public IRelayCommand ReloadCommand { get; }

    /// <summary>Saves the current state of the Find &amp; Replace panel as a new entry ("Save Search").</summary>
    public IRelayCommand SaveCurrentSearchCommand { get; }

    /// <summary>Collapses all groups.</summary>
    public IRelayCommand CollapseAllCommand { get; }

    /// <summary>Expands all groups.</summary>
    public IRelayCommand ExpandAllCommand { get; }

    /// <summary>Opens the file picker for import.</summary>
    public IRelayCommand ImportCommand { get; }

    /// <summary>Opens the file picker for exporting the selected entries.</summary>
    public IRelayCommand ExportCommand { get; }

    /// <summary>Opens the file picker for exporting the whole library.</summary>
    public IRelayCommand ExportAllCommand { get; }

    /// <summary>Filter text (show only entries containing this text).</summary>
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

    /// <summary>Whether the filter applies to all columns (not just the name).</summary>
    public bool FilterAll
    {
        get => _filterAll;
        set
        {
            if (SetProperty(ref _filterAll, value))
            {
                ApplyFilter();
            }
        }
    }

    /// <summary>Result message of the last operation.</summary>
    public string Message
    {
        get => _message;
        private set => SetProperty(ref _message, value);
    }

    /// <summary>Rows of the "Counts Report" (entry name — match count).</summary>
    public ObservableCollection<string> ReportRows { get; } = new();

    /// <summary>Summary of the "Counts Report" (empty when there is no report).</summary>
    public string ReportSummary
    {
        get => _reportSummary;
        private set
        {
            if (SetProperty(ref _reportSummary, value))
            {
                OnPropertyChanged(nameof(HasReport));
            }
        }
    }

    /// <summary>Whether there is a report to show.</summary>
    public bool HasReport => _reportSummary.Length > 0;

    /// <summary>Whether the data differs from what is saved on disk.</summary>
    public bool IsDataModified => _model.IsDataModified;

    /// <summary>The currently selected (single) node — set from the view.</summary>
    public SearchEntryNodeViewModel? SelectedNode
    {
        get => _selectedNode;
        set => SetProperty(ref _selectedNode, value);
    }

    /// <summary>The selected nodes (several can be selected in the tree).</summary>
    public IReadOnlyList<SearchEntryNodeViewModel> SelectedNodes => _selectedNodes;

    /// <summary>Sets the selected nodes (from the view).</summary>
    public void SetSelectedNodes(IEnumerable<SearchEntryNodeViewModel> nodes)
    {
        _selectedNodes = nodes?.ToList() ?? new List<SearchEntryNodeViewModel>();
        _selectedNode = _selectedNodes.Count > 0 ? _selectedNodes[^1] : null;
        OnPropertyChanged(nameof(SelectedNode));
    }

    /// <summary>Saves the library if it was modified (called when the application closes).</summary>
    public void PersistIfModified()
    {
        if (_model.IsDataModified)
        {
            Save();
        }
    }

    /// <summary>Imports entries from a file (called from the view after a file is chosen). Creates an "Imported" group.</summary>
    public void ImportFile(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        string ext = System.IO.Path.GetExtension(path).ToLowerInvariant();
        try
        {
            string content = File.ReadAllText(path);
            IReadOnlyList<SearchEntry> imported = ext switch
            {
                ".txt" => SavedSearchIo.ReadDelimited(content, '\t', System.IO.Path.GetFileName(path)),
                ".csv" => SavedSearchIo.ReadDelimited(content, ',', System.IO.Path.GetFileName(path)),
                _ => SavedSearchIo.ReadJson(content),
            };

            if (imported.Count == 0)
            {
                Message = Strings.Get("SearchEditor_NothingImported");
                return;
            }

            SearchEditorNode group = _model.AddEntry(
                new SearchEntry(true, string.Empty, Strings.Get("SearchEditor_ImportedGroup"), string.Empty, string.Empty, string.Empty),
                isGroup: true,
                parent: null,
                row: -1);

            foreach (SearchEntry entry in imported)
            {
                _model.AddFullNameEntry(entry, group);
            }

            RebuildNodes();
            Message = Strings.Format("SearchEditor_Imported", imported.Count(e => !e.IsGroup));
        }
        catch (IOException ex)
        {
            Message = Strings.Format("SearchEditor_ReadError", ex.Message);
        }
    }

    /// <summary>Exports entries to a file (called from the view after a file is chosen).</summary>
    public void ExportFile(string path, bool all)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        IReadOnlyList<SearchEntry> entries = all || _selectedNodes.Count == 0
            ? _model.ToEntries()
            : _model.GetEntries(_model.GetNonGroupItems(_selectedNodes.Select(n => n.Node)));

        string ext = System.IO.Path.GetExtension(path).ToLowerInvariant();
        try
        {
            string content = ext switch
            {
                ".txt" => SavedSearchIo.WriteDelimited(entries, '\t'),
                ".csv" => SavedSearchIo.WriteDelimited(entries, ','),
                _ => SavedSearchIo.WriteJson(entries),
            };

            File.WriteAllText(path, content);
            Message = Strings.Format("SearchEditor_Exported", entries.Count(e => !e.IsGroup));
        }
        catch (IOException ex)
        {
            Message = Strings.Format("SearchEditor_WriteError", ex.Message);
        }
    }

    private void Load()
    {
        _model.LoadEntries(_store.Load());
        RebuildNodes();
    }

    private void Save()
    {
        _store.Save(_model.ToEntries());
        _model.MarkSaved();
        OnPropertyChanged(nameof(IsDataModified));
        Message = Strings.Get("SearchEditor_Saved");
    }

    private void Reload()
    {
        Load();
        Message = Strings.Get("SearchEditor_Reloaded");
    }

    private void SaveCurrentSearch()
    {
        SearchEntry entry = _findReplace.CurrentAsEntry(Strings.Get("SearchEditor_UnnamedSearch"));
        SearchEditorNode node = _model.AddEntry(entry, isGroup: false, parent: null, row: -1);
        RebuildNodes();
        SelectNode(node);
        Message = Strings.Get("SearchEditor_CurrentSaved");
    }

    private void AddEntry()
    {
        SearchEditorNode parent = _model.Root;
        int row = -1;
        if (_selectedNode is { } sel)
        {
            if (sel.Node.IsGroup)
            {
                parent = sel.Node;
            }
            else
            {
                parent = sel.Node.Parent ?? _model.Root;
                row = parent.Children.ToList().IndexOf(sel.Node) + 1;
            }
        }

        _model.AddEntry(SearchEntry.EmptySearch, isGroup: false, parent, row);
        RebuildNodes();
        Message = Strings.Get("SearchEditor_EntryAdded");
    }

    private void AddGroup()
    {
        SearchEditorNode parent = _selectedNode?.Node is { IsGroup: true } g ? g : _model.Root;
        _model.AddEntry(
            new SearchEntry(true, string.Empty, Strings.Get("SearchEditor_NewGroup"), string.Empty, string.Empty, string.Empty),
            isGroup: true,
            parent,
            row: -1);
        RebuildNodes();
        Message = Strings.Get("SearchEditor_GroupAdded");
    }

    private void DeleteSelected()
    {
        if (_selectedNodes.Count == 0)
        {
            return;
        }

        foreach (SearchEntryNodeViewModel node in _selectedNodes.ToList())
        {
            _model.Delete(node.Node);
        }

        RebuildNodes();
        Message = Strings.Get("SearchEditor_Deleted");
    }

    private void Move(Func<SearchEditorNode, bool> move)
    {
        if (_selectedNode is not { } node)
        {
            return;
        }

        if (move(node.Node))
        {
            RebuildNodes();
            SelectNode(node.Node);
        }
    }

    private void LoadSelected()
    {
        if (_selectedNode?.Node is { IsGroup: false } node)
        {
            _findReplace.LoadSearch(_model.ToEntry(node));
            Message = Strings.Format("SearchEditor_LoadedIntoFindReplace", node.Name);
        }
    }

    private void FillControls()
    {
        List<SearchEditorNode> nodes = SelectedLeafNodes();
        if (nodes.Count < 2)
        {
            Message = Strings.Get("SearchEditor_SelectTwo");
            return;
        }

        _model.FillControls(nodes);
        RebuildNodes();
        Message = Strings.Get("SearchEditor_OptionsCopied");
    }

    private void CountsReport()
    {
        IReadOnlyList<SearchEntry> entries = SelectedEntries();
        if (entries.Count == 0)
        {
            Message = Strings.Get("SearchEditor_NothingSelected");
            return;
        }

        ReportRows.Clear();
        int total = 0;
        foreach (SearchEntry entry in entries)
        {
            int count = _findReplace.CountForEntry(entry);
            total += count;
            ReportRows.Add(string.Format(
                CultureInfo.CurrentCulture, "{0} — {1}", entry.FullName, count));
        }

        ReportSummary = Strings.Format("SearchEditor_CountsSummary", total, entries.Count);
        Message = ReportSummary;
    }

    private void CloseReport()
    {
        ReportRows.Clear();
        ReportSummary = string.Empty;
    }

    private void RunBatch(Func<IReadOnlyList<SearchEntry>, bool> run)
    {
        IReadOnlyList<SearchEntry> entries = SelectedEntries();
        if (entries.Count == 0)
        {
            Message = Strings.Get("SearchEditor_NothingSelected");
            return;
        }

        run(entries);
        Message = _findReplace.Message;
    }

    private void RunBatchInt(Func<IReadOnlyList<SearchEntry>, int> run)
    {
        IReadOnlyList<SearchEntry> entries = SelectedEntries();
        if (entries.Count == 0)
        {
            Message = Strings.Get("SearchEditor_NothingSelected");
            return;
        }

        run(entries);
        Message = _findReplace.Message;
    }

    private IReadOnlyList<SearchEntry> SelectedEntries()
    {
        List<SearchEditorNode> leaves = SelectedLeafNodes();
        return leaves.Count == 0 ? Array.Empty<SearchEntry>() : _model.GetEntries(leaves);
    }

    private List<SearchEditorNode> SelectedLeafNodes()
    {
        var leaves = new List<SearchEditorNode>();
        var seen = new HashSet<SearchEditorNode>();
        foreach (SearchEntryNodeViewModel node in _selectedNodes)
        {
            foreach (SearchEditorNode leaf in _model.GetNonGroupItems(node.Node))
            {
                if (seen.Add(leaf))
                {
                    leaves.Add(leaf);
                }
            }
        }

        return leaves;
    }

    private void RebuildNodes()
    {
        var expanded = new HashSet<SearchEditorNode>();
        CollectExpanded(Nodes, expanded);

        Nodes.Clear();
        foreach (SearchEditorNode child in _model.Root.Children)
        {
            Nodes.Add(new SearchEntryNodeViewModel(child, _model, expanded.Contains(child)));
        }

        ApplyFilter();
        OnPropertyChanged(nameof(IsDataModified));
    }

    private static void CollectExpanded(
        IEnumerable<SearchEntryNodeViewModel> nodes, HashSet<SearchEditorNode> into)
    {
        foreach (SearchEntryNodeViewModel node in nodes)
        {
            if (node.IsExpanded)
            {
                into.Add(node.Node);
            }

            CollectExpanded(node.Children, into);
        }
    }

    private void SelectNode(SearchEditorNode target)
    {
        SearchEntryNodeViewModel? match = FindNode(Nodes, target);
        if (match is not null)
        {
            SetSelectedNodes(new[] { match });
        }
    }

    private static SearchEntryNodeViewModel? FindNode(
        IEnumerable<SearchEntryNodeViewModel> nodes, SearchEditorNode target)
    {
        foreach (SearchEntryNodeViewModel node in nodes)
        {
            if (ReferenceEquals(node.Node, target))
            {
                return node;
            }

            SearchEntryNodeViewModel? nested = FindNode(node.Children, target);
            if (nested is not null)
            {
                return nested;
            }
        }

        return null;
    }

    private void SetExpandedAll(bool expanded) => SetExpandedAll(Nodes, expanded);

    private static void SetExpandedAll(IEnumerable<SearchEntryNodeViewModel> nodes, bool expanded)
    {
        foreach (SearchEntryNodeViewModel node in nodes)
        {
            if (node.IsGroup)
            {
                node.IsExpanded = expanded;
            }

            SetExpandedAll(node.Children, expanded);
        }
    }

    private void ApplyFilter() => ApplyFilter(Nodes);

    private bool ApplyFilter(IEnumerable<SearchEntryNodeViewModel> nodes)
    {
        bool anyVisible = false;
        string filter = _filterText.Trim();

        foreach (SearchEntryNodeViewModel node in nodes)
        {
            bool childVisible = ApplyFilter(node.Children);
            bool selfMatch = filter.Length == 0 || Matches(node, filter);
            node.IsVisible = selfMatch || childVisible;
            anyVisible |= node.IsVisible;
        }

        return anyVisible;
    }

    private bool Matches(SearchEntryNodeViewModel node, string filter)
    {
        if (node.Name.Contains(filter, StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        if (!_filterAll || node.IsGroup)
        {
            return false;
        }

        return node.Find.Contains(filter, StringComparison.OrdinalIgnoreCase)
            || node.Replace.Contains(filter, StringComparison.OrdinalIgnoreCase)
            || node.Controls.Contains(filter, StringComparison.OrdinalIgnoreCase);
    }
}
