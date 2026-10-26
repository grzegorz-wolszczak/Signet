using System.Threading.Tasks;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System;
using CommunityToolkit.Mvvm.Input;
using Signet.App.Resources;
using Signet.Core.Misc;
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
    private readonly SettingsStore _settings;
    private readonly FindReplaceViewModel _findReplace;
    private readonly FindReplaceViewModel _editFindReplace;

    private string _filterText = string.Empty;
    private bool _filterAll;
    private string _message = string.Empty;
    private SearchEntryNodeViewModel? _selectedNode;
    private IReadOnlyList<SearchEntryNodeViewModel> _selectedNodes = Array.Empty<SearchEntryNodeViewModel>();
    private bool _isEditing;
    private string _editingName = string.Empty;
    private SearchEditorNode? _editingNode;
    private SearchEntry? _editBaseline;

    /// <summary>Creates the view model and loads the saved searches from the store.</summary>
    /// <param name="store">Where the library is saved.</param>
    /// <param name="settings">The application settings (the action button look, the fonts).</param>
    /// <param name="findReplace">
    /// The real Find &amp; Replace panel: "Save current search" reads its current state, "Load Search" and the
    /// batch actions (Find Next, Replace, …) write into it.
    /// </param>
    /// <param name="editFindReplace">
    /// A separate instance (the same book, its own Find/Replace fields) for the Add/Edit view: editing a search
    /// tries it out live without changing what is typed in <paramref name="findReplace"/>.
    /// </param>
    public SearchEditorViewModel(
        SavedSearchStore store, SettingsStore settings, FindReplaceViewModel findReplace, FindReplaceViewModel editFindReplace)
    {
        _store = store ?? throw new ArgumentNullException(nameof(store));
        _settings = settings ?? throw new ArgumentNullException(nameof(settings));
        _findReplace = findReplace ?? throw new ArgumentNullException(nameof(findReplace));
        _editFindReplace = editFindReplace ?? throw new ArgumentNullException(nameof(editFindReplace));
        _settings.SettingChanged += OnSettingChanged;

        AddEntryCommand = new RelayCommand(BeginAdd);
        EditSearchCommand = new RelayCommand(BeginEdit);
        DoneEditCommand = new RelayCommand(DoneEdit);
        CancelEditCommand = new RelayCommand(CancelEdit);
        AddGroupCommand = new RelayCommand(AddGroup);
        DeleteCommand = new RelayCommand(RequestDelete);
        MoveUpCommand = new RelayCommand(() => Move(_model.MoveUp));
        MoveDownCommand = new RelayCommand(() => Move(_model.MoveDown));
        MoveLeftCommand = new RelayCommand(() => Move(_model.MoveLeft));
        MoveRightCommand = new RelayCommand(() => Move(_model.MoveRight));
        LoadSearchCommand = new RelayCommand(LoadSelected, CanLoadSelected);
        FindCommand = new RelayCommand(() => RunBatch(_findReplace.RunSavedFind), HasSelectedEntries);
        ReplaceCurrentCommand = new RelayCommand(() => RunBatch(_findReplace.RunSavedReplaceCurrent), HasSelectedEntries);
        ReplaceCommand = new RelayCommand(() => RunBatch(_findReplace.RunSavedReplaceFind), HasSelectedEntries);
        ReplaceAllCommand = new AsyncRelayCommand(() => RunBatchIntAsync(_findReplace.RunSavedReplaceAllAsync), HasSelectedEntries);
        CountAllCommand = new RelayCommand(() => RunBatchInt(_findReplace.RunSavedCountAll), HasSelectedEntries);
        FillControlsCommand = new RelayCommand(FillControls);
        SaveCommand = new RelayCommand(Save);
        ReloadCommand = new RelayCommand(Reload);
        SaveCurrentSearchCommand = new RelayCommand(SaveCurrentSearch);
        CollapseAllCommand = new RelayCommand(() => SetExpandedAll(false));
        ExpandAllCommand = new RelayCommand(() => SetExpandedAll(true));
        ImportCommand = new RelayCommand(() => ImportRequested?.Invoke(this, EventArgs.Empty));
        ExportCommand = new RelayCommand(() => ExportRequested?.Invoke(this, false));
        ExportAllCommand = new RelayCommand(() => ExportRequested?.Invoke(this, true));
        ClearFilterCommand = new RelayCommand(() => FilterText = string.Empty);

        Load();
    }

    /// <summary>Raised when the view should show the file picker for import.</summary>
    public event EventHandler? ImportRequested;

    /// <summary>Raised when the view should show the file picker for export (selected entries / everything).</summary>
    public event EventHandler<bool>? ExportRequested;

    /// <summary>
    /// Raised when Delete is pressed with a non-empty selection — the view asks the user to confirm
    /// (how many groups / entries) and, if confirmed, calls <see cref="DeleteConfirmed"/>.
    /// </summary>
    public event EventHandler<SearchDeleteCounts>? DeleteConfirmRequested;

    /// <summary>Top-level tree nodes.</summary>
    public ObservableCollection<SearchEntryNodeViewModel> Nodes { get; } = new();

    /// <summary>Opens the Add/Edit view on a new, blank entry.</summary>
    public IRelayCommand AddEntryCommand { get; }

    /// <summary>Opens the Add/Edit view on the selected entry.</summary>
    public IRelayCommand EditSearchCommand { get; }

    /// <summary>Saves the Add/Edit view's state (new or edited entry) and returns to the list.</summary>
    public IRelayCommand DoneEditCommand { get; }

    /// <summary>Discards the Add/Edit view's state and returns to the list.</summary>
    public IRelayCommand CancelEditCommand { get; }

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
    public IAsyncRelayCommand ReplaceAllCommand { get; }

    /// <summary>Runs Count All for the selected entries sequentially.</summary>
    public IRelayCommand CountAllCommand { get; }

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

    /// <summary>Clears <see cref="FilterText"/> (the filter box's "✕" button).</summary>
    public IRelayCommand ClearFilterCommand { get; }

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

    /// <summary>Whether the data differs from what is saved on disk.</summary>
    public bool IsDataModified => _model.IsDataModified;

    /// <summary>
    /// Whether the Add/Edit view (Calibre-style: Name + an embedded Find &amp; Replace panel to test the
    /// search right away) is shown instead of the list.
    /// </summary>
    public bool IsEditing
    {
        get => _isEditing;
        private set => SetProperty(ref _isEditing, value);
    }

    /// <summary>The Name field of the Add/Edit view.</summary>
    public string EditingName
    {
        get => _editingName;
        set => SetProperty(ref _editingName, value ?? string.Empty);
    }

    /// <summary>
    /// The Add/Edit view's own Find &amp; Replace panel: the same book as the real one, so a search can be tried out
    /// (Find / Replace / Replace All, …) before it is saved, but its own Find/Replace fields — editing a search
    /// never changes what is typed in the real Find &amp; Replace panel ("Load Search" is the only thing that does).
    /// </summary>
    public FindReplaceViewModel FindReplace => _editFindReplace;

    /// <summary>Raised when <see cref="Appearance"/> changed (Preferences › Appearance › Saved Searches › Fonts).</summary>
    public event EventHandler? AppearanceChanged;

    /// <summary>
    /// How the batch action buttons on the right (Load Search, Find Next, Replace, …) show their icon and text
    /// (Preferences › Appearance › Saved Searches).
    /// </summary>
    public FindActionButtonDisplay ActionButtonDisplay
    {
        get => _settings.SearchEditorActionButtonDisplay;
        set
        {
            if (_settings.SearchEditorActionButtonDisplay != value)
            {
                _settings.SearchEditorActionButtonDisplay = value;
                OnPropertyChanged();
            }
        }
    }

    /// <summary>
    /// The fonts of the window (Preferences › Appearance › Saved Searches › Fonts): the "Name" column header and
    /// the rows / Details preview / Name field.
    /// </summary>
    public SearchEditorAppearance Appearance => _settings.SearchEditorAppearance;

    private void OnSettingChanged(object? sender, SettingChangedEventArgs e)
    {
        if (e.QualifiedKey.Contains("search_editor_font", StringComparison.Ordinal)
            || e.QualifiedKey.Contains("search_editor_appearance", StringComparison.Ordinal))
        {
            AppearanceChanged?.Invoke(this, EventArgs.Empty);
        }
        else if (e.QualifiedKey.Contains("search_editor_action_button_display", StringComparison.Ordinal))
        {
            OnPropertyChanged(nameof(ActionButtonDisplay));
        }
    }

    /// <summary>
    /// Whether the Add/Edit view actually differs from what it started from (Name, Find, Replace or any of the
    /// 9 Controls options) — opening Edit Search and closing it again without touching anything is not a change.
    /// </summary>
    public bool IsEditDirty
    {
        get
        {
            if (!_isEditing || _editBaseline is not { } baseline)
            {
                return false;
            }

            SearchEntry current = _editFindReplace.CurrentAsEntry(EditingName);
            return current.Name != baseline.Name || current.Find != baseline.Find
                || current.Replace != baseline.Replace || current.Controls != baseline.Controls;
        }
    }

    /// <summary>The currently selected (single) node — set from the view.</summary>
    public SearchEntryNodeViewModel? SelectedNode
    {
        get => _selectedNode;
        set => SetProperty(ref _selectedNode, value);
    }

    /// <summary>The selected nodes (several can be selected in the tree).</summary>
    public IReadOnlyList<SearchEntryNodeViewModel> SelectedNodes => _selectedNodes;

    /// <summary>
    /// Whether the read-only details preview (Find / Replace / Controls of the selected entry) should show —
    /// a single entry is selected (a group has no such fields; several selected shows nothing, like Calibre).
    /// </summary>
    public bool ShowDetails => _selectedNode is { IsGroup: false } && _selectedNodes.Count == 1;

    /// <summary>Sets the selected nodes (from the view).</summary>
    public void SetSelectedNodes(IEnumerable<SearchEntryNodeViewModel> nodes)
    {
        _selectedNodes = nodes?.ToList() ?? new List<SearchEntryNodeViewModel>();
        _selectedNode = _selectedNodes.Count > 0 ? _selectedNodes[^1] : null;
        OnPropertyChanged(nameof(SelectedNode));
        OnPropertyChanged(nameof(ShowDetails));
        NotifySelectionCommandsCanExecuteChanged();
    }

    /// <summary>
    /// Raised by <see cref="SetSelectedNodes"/> and <see cref="RefreshBookAvailability"/>: the batch actions on the
    /// right (Load Search, Find Next, Replace, Replace/Find, Replace All, Count All) only make sense once a book
    /// is loaded and the selection resolves to at least one entry — a group counts as all the entries under it
    /// (<see cref="SelectedLeafNodes"/>).
    /// </summary>
    private void NotifySelectionCommandsCanExecuteChanged()
    {
        LoadSearchCommand.NotifyCanExecuteChanged();
        FindCommand.NotifyCanExecuteChanged();
        ReplaceCurrentCommand.NotifyCanExecuteChanged();
        ReplaceCommand.NotifyCanExecuteChanged();
        ReplaceAllCommand.NotifyCanExecuteChanged();
        CountAllCommand.NotifyCanExecuteChanged();
    }

    /// <summary>
    /// Called by <see cref="MainWindowViewModel"/> whenever a book opens or closes (it owns this view model as a
    /// single, long-lived instance), so the batch actions on the right reflect that at once, even while the
    /// Saved Searches window is already open.
    /// </summary>
    public void RefreshBookAvailability() => NotifySelectionCommandsCanExecuteChanged();

    private bool CanLoadSelected() =>
        _findReplace.HasBook && _selectedNode?.Node is { IsGroup: false } && _selectedNodes.Count == 1;

    private bool HasSelectedEntries() => _findReplace.HasBook && SelectedLeafNodes().Count > 0;

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
                _ => SavedSearchIo.ReadXml(content),
            };

            if (imported.Count == 0)
            {
                Message = Strings.Get("SearchEditor_NothingImported");
                return;
            }

            SearchEditorNode group = _model.AddEntry(
                new SearchEntry(true, Array.Empty<string>(), Strings.Get("SearchEditor_ImportedGroup"), string.Empty, string.Empty, string.Empty),
                isGroup: true,
                parent: null,
                row: -1);

            foreach (SearchEntry entry in imported)
            {
                _model.AddPathEntry(entry, group);
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
                _ => SavedSearchIo.WriteXml(entries),
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

    private void BeginAdd()
    {
        _editingNode = null;
        EditingName = string.Empty;
        _editFindReplace.LoadSearch(SearchEntry.EmptySearch);
        _editBaseline = _editFindReplace.CurrentAsEntry(string.Empty);
        IsEditing = true;
    }

    private void BeginEdit()
    {
        if (_selectedNode?.Node is not { IsGroup: false } node)
        {
            return;
        }

        _editingNode = node;
        EditingName = node.Name;
        _editFindReplace.LoadSearch(_model.ToEntry(node));
        _editBaseline = _editFindReplace.CurrentAsEntry(node.Name);
        IsEditing = true;
    }

    private void DoneEdit()
    {
        string name = string.IsNullOrWhiteSpace(EditingName) ? Strings.Get("SearchEditor_UnnamedSearch") : EditingName;
        SearchEntry current = _editFindReplace.CurrentAsEntry(name);

        if (_editingNode is { } existing)
        {
            _model.Rename(existing, current.Name);
            _model.SetFind(existing, current.Find);
            _model.SetReplace(existing, current.Replace);
            _model.SetControls(existing, current.Controls);
            RebuildNodes();
            SelectNode(existing);
            Message = Strings.Get("SearchEditor_EntryUpdated");
        }
        else
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

            SearchEditorNode added = _model.AddEntry(current, isGroup: false, parent, row);
            RebuildNodes();
            SelectNode(added);
            Message = Strings.Get("SearchEditor_EntryAdded");
        }

        IsEditing = false;
        _editingNode = null;
        _editBaseline = null;
    }

    private void CancelEdit()
    {
        IsEditing = false;
        _editingNode = null;
        _editBaseline = null;
    }

    private void AddGroup()
    {
        SearchEditorNode parent = _selectedNode?.Node is { IsGroup: true } g ? g : _model.Root;
        _model.AddEntry(
            new SearchEntry(true, Array.Empty<string>(), Strings.Get("SearchEditor_NewGroup"), string.Empty, string.Empty, string.Empty),
            isGroup: true,
            parent,
            row: -1);
        RebuildNodes();
        Message = Strings.Get("SearchEditor_GroupAdded");
    }

    private void RequestDelete()
    {
        if (_selectedNodes.Count == 0)
        {
            return;
        }

        int groups = _selectedNodes.Count(n => n.IsGroup);
        int entries = _selectedNodes.Count(n => !n.IsGroup);
        DeleteConfirmRequested?.Invoke(this, new SearchDeleteCounts(groups, entries));
    }

    /// <summary>Deletes the selection after the view confirmed it with the user (see <see cref="DeleteConfirmRequested"/>).</summary>
    public void DeleteConfirmed()
    {
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

    private async Task RunBatchIntAsync(Func<IReadOnlyList<SearchEntry>, Task<int>> run)
    {
        IReadOnlyList<SearchEntry> entries = SelectedEntries();
        if (entries.Count == 0)
        {
            Message = Strings.Get("SearchEditor_NothingSelected");
            return;
        }

        await run(entries).ConfigureAwait(true);
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

    // Keywords: every word of the filter must occur, in any order ("quote fix" finds "Fix smart quotes") — in the name,
    // or with "filter all" in any of the name, Find, Replace and Controls fields.
    private bool Matches(SearchEntryNodeViewModel node, string filter)
    {
        string[] keywords = filter.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
        bool searchAll = _filterAll && !node.IsGroup;
        return keywords.All(keyword =>
            node.Name.Contains(keyword, StringComparison.OrdinalIgnoreCase)
            || (searchAll
                && (node.Find.Contains(keyword, StringComparison.OrdinalIgnoreCase)
                    || node.Replace.Contains(keyword, StringComparison.OrdinalIgnoreCase)
                    || node.Controls.Contains(keyword, StringComparison.OrdinalIgnoreCase))));
    }
}

/// <summary>How many groups / entries Delete would remove — carried by <see cref="SearchEditorViewModel.DeleteConfirmRequested"/>.</summary>
public sealed record SearchDeleteCounts(int GroupCount, int EntryCount);
