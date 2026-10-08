using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System;
using CommunityToolkit.Mvvm.Input;
using Signet.App.Actions;
using Signet.App.Resources;
using Signet.App.Services;
using Signet.App.ViewModels.Tabs;
using Signet.Core.BookManipulation;
using Signet.Core.MiscEditors;

namespace Signet.App.ViewModels;

/// <summary>
/// View model of the clip library. A single instance serves both the docked "Clips" panel
/// (<see cref="Docking.ClipsTool"/>, browsing + pasting) and the "Clip Editor" window (full
/// CRUD) — both bind to the same tree, so changes in one are immediately visible in the other.
/// </summary>
public sealed class ClipsViewModel : ViewModelBase
{
    private readonly ClipEditorModel _model = new();
    private readonly ClipStore _store;
    private readonly Func<CodeTabViewModel?> _activeCodeTab;
    private readonly Func<Book?> _currentBook;
    private readonly IStatusBarService _statusBar;

    private string _filterText = string.Empty;
    private bool _filterAll;
    private string _message = string.Empty;
    private ClipNodeViewModel? _selectedNode;
    private ClipNodeViewModel? _editingNode;
    private IReadOnlyList<ClipNodeViewModel> _selectedNodes = Array.Empty<ClipNodeViewModel>();

    /// <summary>Creates the view model and loads the clip library from the store.</summary>
    public ClipsViewModel(
        ClipStore store, Func<CodeTabViewModel?> activeCodeTab, Func<Book?> currentBook, IStatusBarService statusBar)
    {
        _store = store ?? throw new ArgumentNullException(nameof(store));
        _activeCodeTab = activeCodeTab ?? throw new ArgumentNullException(nameof(activeCodeTab));
        _currentBook = currentBook ?? throw new ArgumentNullException(nameof(currentBook));
        _statusBar = statusBar ?? throw new ArgumentNullException(nameof(statusBar));

        PasteCommand = new RelayCommand(PasteSelected);
        RenameSelectedCommand = new RelayCommand(RenameSelected, () => _selectedNode is not null);
        AddEntryCommand = new RelayCommand(AddEntry);
        AddGroupCommand = new RelayCommand(AddGroup);
        DeleteCommand = new RelayCommand(DeleteSelected);
        MoveUpCommand = new RelayCommand(() => Move(_model.MoveUp));
        MoveDownCommand = new RelayCommand(() => Move(_model.MoveDown));
        MoveLeftCommand = new RelayCommand(() => Move(_model.MoveLeft));
        MoveRightCommand = new RelayCommand(() => Move(_model.MoveRight));
        AutoFillCommand = new RelayCommand(AutoFill);
        SaveCommand = new RelayCommand(Save);
        ReloadCommand = new RelayCommand(Reload);
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

    /// <summary>
    /// Raised after every tree change that may have altered the Clip Bar slot assignment
    /// (add/delete/rename/text change/move/load) — <c>MainWindowViewModel</c> then refreshes the
    /// 60 actions <c>MainWindow.Clip1..Clip60</c>.
    /// </summary>
    public event EventHandler? ClipsChanged;

    /// <summary>Top-level tree nodes.</summary>
    public ObservableCollection<ClipNodeViewModel> Nodes { get; } = new();

    /// <summary>Pastes the selected clip's text into the active document.</summary>
    public IRelayCommand PasteCommand { get; }

    /// <summary>Starts renaming the selected node in the tree (the "Clip Editor → Rename" action, F2 by default).</summary>
    public IRelayCommand RenameSelectedCommand { get; }

    /// <summary>
    /// The panel-scoped Clip Editor actions (<see cref="AppActionIds.ClipEditorCategory"/>) — the views create their own
    /// key bindings from them, so that the shortcuts work only while a clips tree has the focus.
    /// </summary>
    public IReadOnlyList<AppAction> ShortcutActions { get; set; } = Array.Empty<AppAction>();

    /// <summary>Adds a new empty clip (after the selected item).</summary>
    public IRelayCommand AddEntryCommand { get; }

    /// <summary>Adds a new group.</summary>
    public IRelayCommand AddGroupCommand { get; }

    /// <summary>Deletes the selected entries.</summary>
    public IRelayCommand DeleteCommand { get; }

    /// <summary>Moves the selected entry up.</summary>
    public IRelayCommand MoveUpCommand { get; }

    /// <summary>Moves the selected entry down.</summary>
    public IRelayCommand MoveDownCommand { get; }

    /// <summary>Moves the selected entry to its parent's level.</summary>
    public IRelayCommand MoveLeftCommand { get; }

    /// <summary>Moves the selected entry into the group above it.</summary>
    public IRelayCommand MoveRightCommand { get; }

    /// <summary>Generates an "Autofill" group of clips from the current book's CSS.</summary>
    public IRelayCommand AutoFillCommand { get; }

    /// <summary>Saves the clip library to a file.</summary>
    public IRelayCommand SaveCommand { get; }

    /// <summary>Reloads the library from the file (discards unsaved changes).</summary>
    public IRelayCommand ReloadCommand { get; }

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

    /// <summary>Whether the filter applies to both name and text (not just the name) — the "Name or Text" mode.</summary>
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

    /// <summary>"*" appended to the Clip Editor window title when there are unsaved changes.</summary>
    public string ModifiedMarker => _model.IsDataModified ? "*" : string.Empty;

    /// <summary>
    /// Discards unsaved changes — reloads the library from disk (the "Discard" choice of the
    /// save prompt).
    /// </summary>
    public void DiscardChanges()
    {
        Load();
        Message = string.Empty;
    }

    /// <summary>Currently selected (single) node — set from the view.</summary>
    public ClipNodeViewModel? SelectedNode
    {
        get => _selectedNode;
        set
        {
            if (SetProperty(ref _selectedNode, value))
            {
                RenameSelectedCommand.NotifyCanExecuteChanged();
            }
        }
    }

    /// <summary>The selected nodes (several in the trees with multi-selection).</summary>
    public IReadOnlyList<ClipNodeViewModel> SelectedNodes => _selectedNodes;

    /// <summary>Sets the set of selected nodes (from the view).</summary>
    public void SetSelectedNodes(IEnumerable<ClipNodeViewModel> nodes)
    {
        _selectedNodes = nodes?.ToList() ?? new List<ClipNodeViewModel>();
        _selectedNode = _selectedNodes.Count > 0 ? _selectedNodes[^1] : null;
        OnPropertyChanged(nameof(SelectedNode));
        RenameSelectedCommand.NotifyCanExecuteChanged();
    }

    /// <summary>Node whose name is being edited in the Clip Editor tree (null = no edit in progress).</summary>
    public ClipNodeViewModel? EditingNode
    {
        get => _editingNode;
        private set
        {
            ClipNodeViewModel? old = _editingNode;
            if (!SetProperty(ref _editingNode, value))
            {
                return;
            }

            if (old is not null)
            {
                old.IsEditing = false;
            }

            if (value is not null)
            {
                value.EditText = value.Name;
                value.IsEditing = true;
            }
        }
    }

    private void RenameSelected()
    {
        if (_selectedNode is { } node)
        {
            BeginRename(node);
        }
    }

    /// <summary>Starts renaming a node in the tree (double click or the Rename action, F2 by default).</summary>
    public void BeginRename(ClipNodeViewModel node)
    {
        ArgumentNullException.ThrowIfNull(node);
        if (!ReferenceEquals(_editingNode, node))
        {
            CommitRename();
        }

        EditingNode = node;
    }

    /// <summary>
    /// Commits the typed name (Enter or focus loss). An empty or unchanged name does nothing
    /// (validated in <see cref="ClipEditorModel.Rename"/>).
    /// </summary>
    public void CommitRename()
    {
        if (_editingNode is not { } node)
        {
            return;
        }

        string newName = node.EditText;
        EditingNode = null;
        node.Name = newName;
    }

    /// <summary>Abandons the rename (Esc).</summary>
    public void CancelRename() => EditingNode = null;

    /// <summary>Root of the clip library — for building the "Clips" submenu in the Code View context menu.</summary>
    public ClipEditorNode LibraryRoot => _model.Root;

    /// <summary>
    /// "Add To Clips..." from the Code View context menu: adds an "Unnamed Entry" clip with the
    /// given text at the start of the library and selects it (for renaming in the Clip Editor).
    /// </summary>
    public ClipNodeViewModel? AddPendingEntry(string text)
    {
        ArgumentNullException.ThrowIfNull(text);
        ClipEditorNode node = _model.AddEntry(
            new ClipEntry(IsGroup: false, FullName: Strings.Get("Clips_UnnamedEntry"), Name: Strings.Get("Clips_UnnamedEntry"), Text: text),
            isGroup: false,
            parent: null,
            row: 0);
        RebuildNodes();
        ClipNodeViewModel? added = Nodes.FirstOrDefault(n => ReferenceEquals(n.Node, node));
        if (added is not null)
        {
            SetSelectedNodes(new[] { added });
        }

        Message = Strings.Get("Clips_Added");
        return added;
    }

    /// <summary>Clip assigned to a Clip Bar slot (1-based) — for refreshing the <c>MainWindow.Clip1..60</c> actions.</summary>
    public ClipEditorNode? GetSlot(int slotNumber) => _model.GetSlot(slotNumber);

    /// <summary>
    /// All clips (leaves, without groups) as full name/text pairs — for the "Insert Clip" dialog
    /// (<c>SelectClipWindow</c>).
    /// </summary>
    public IReadOnlyList<(string FullName, string Text)> GetLeafClips() =>
        _model.ToEntries().Where(e => !e.IsGroup).Select(e => (e.FullName, e.Text)).ToList();

    /// <summary>
    /// Pastes clip text into the active document. Returns whether anything was pasted.
    /// </summary>
    public bool PasteText(string clipText)
    {
        ArgumentNullException.ThrowIfNull(clipText);
        if (clipText.Length == 0)
        {
            return false;
        }

        CodeTabViewModel? tab = _activeCodeTab();
        if (tab is null)
        {
            _statusBar.ShowMessage(Strings.Get("Clips_ChooseInsertPoint"), TimeSpan.FromSeconds(4));
            return false;
        }

        tab.PasteClipText(clipText);
        return true;
    }

    /// <summary>Saves the library if it was modified (called when the application closes).</summary>
    public void PersistIfModified()
    {
        if (_model.IsDataModified)
        {
            Save();
        }
    }

    /// <summary>Imports entries from a file (called by the view after a file is chosen). Creates an "Imported" group.</summary>
    public void ImportFile(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        try
        {
            IReadOnlyList<ClipEntry> imported = ClipIo.ReadJson(File.ReadAllText(path));
            if (imported.Count == 0)
            {
                Message = Strings.Get("Clips_NothingImported");
                return;
            }

            ClipEditorNode group = _model.AddEntry(
                new ClipEntry(true, string.Empty, Strings.Get("SearchEditor_ImportedGroup"), string.Empty), isGroup: true, parent: null, row: -1);

            foreach (ClipEntry entry in imported)
            {
                _model.AddFullNameEntry(entry, group);
            }

            RebuildNodes();
            Message = Strings.Format("Clips_Imported", imported.Count(e => !e.IsGroup));
        }
        catch (IOException ex)
        {
            Message = Strings.Format("SearchEditor_ReadError", ex.Message);
        }
    }

    /// <summary>Exports entries to a file (called by the view after a file is chosen).</summary>
    public void ExportFile(string path, bool all)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        IReadOnlyList<ClipEntry> entries = all || _selectedNodes.Count == 0
            ? _model.ToEntries()
            : _model.GetEntries(_model.GetNonGroupItems(_selectedNodes.Select(n => n.Node)));

        try
        {
            File.WriteAllText(path, ClipIo.WriteJson(entries));
            Message = Strings.Format("Clips_Exported", entries.Count(e => !e.IsGroup));
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
        OnPropertyChanged(nameof(ModifiedMarker));
        Message = Strings.Get("Clips_Saved");
    }

    private void Reload()
    {
        Load();
        Message = Strings.Get("Clips_Reloaded");
    }

    private void PasteSelected()
    {
        if (_selectedNode?.Node is { IsGroup: false } node && node.Text.Length > 0)
        {
            PasteText(node.Text);
        }
    }

    private void AddEntry()
    {
        ClipEditorNode parent = _model.Root;
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

        _model.AddEntry(ClipEntry.EmptyClip, isGroup: false, parent, row);
        RebuildNodes();
        Message = Strings.Get("Clips_Added");
    }

    private void AddGroup()
    {
        ClipEditorNode parent = _selectedNode?.Node is { IsGroup: true } g ? g : _model.Root;
        _model.AddEntry(
            new ClipEntry(true, string.Empty, Strings.Get("SearchEditor_NewGroup"), string.Empty), isGroup: true, parent, row: -1);
        RebuildNodes();
        Message = Strings.Get("SearchEditor_GroupAdded");
    }

    private void DeleteSelected()
    {
        if (_selectedNodes.Count == 0)
        {
            return;
        }

        foreach (ClipNodeViewModel node in _selectedNodes.ToList())
        {
            _model.Delete(node.Node);
        }

        RebuildNodes();
        Message = Strings.Get("Clips_Deleted");
    }

    private void Move(Func<ClipEditorNode, bool> move)
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

    private void AutoFill()
    {
        Book? book = _currentBook();
        if (book is null)
        {
            Message = Strings.Get("Clips_OpenBookForCss");
            return;
        }

        IReadOnlyList<ClipEntry> entries = ClipAutoFill.BuildAutofillEntries(book);
        foreach (ClipEntry entry in entries)
        {
            _model.AddFullNameEntry(entry);
        }

        RebuildNodes();
        Message = Strings.Format("Clips_CssAdded", entries.Count);
    }

    private void RebuildNodes()
    {
        var expanded = new HashSet<ClipEditorNode>();
        CollectExpanded(Nodes, expanded);

        // Clicking a button commits the edit earlier (focus loss); here the old nodes go away.
        CancelRename();
        Nodes.Clear();
        foreach (ClipEditorNode child in _model.Root.Children)
        {
            Nodes.Add(new ClipNodeViewModel(child, _model, expanded.Contains(child), NotifyModified));
        }

        // The new view nodes replace the old ones — the selection is moved onto them, and nodes
        // no longer in the tree (e.g. a deleted group) are dropped. Otherwise "Add Entry" after
        // deleting the selected group would add a clip to a detached node not visible in the tree.
        SetSelectedNodes(_selectedNodes
            .Select(old => FindNode(Nodes, old.Node))
            .OfType<ClipNodeViewModel>()
            .ToList());

        ApplyFilter();
        NotifyModified();
    }

    // Called not only on structural changes (Add/Delete/Move) but also on name/text edits made
    // directly in the tree (see ClipNodeViewModel.Name/Text). Refreshes IsDataModified and
    // (through ClipsChanged) the 60 Clip Bar actions in MainWindowViewModel.
    private void NotifyModified()
    {
        OnPropertyChanged(nameof(IsDataModified));
        OnPropertyChanged(nameof(ModifiedMarker));
        ClipsChanged?.Invoke(this, EventArgs.Empty);
    }

    private static void CollectExpanded(IEnumerable<ClipNodeViewModel> nodes, HashSet<ClipEditorNode> into)
    {
        foreach (ClipNodeViewModel node in nodes)
        {
            if (node.IsExpanded)
            {
                into.Add(node.Node);
            }

            CollectExpanded(node.Children, into);
        }
    }

    private void SelectNode(ClipEditorNode target)
    {
        ClipNodeViewModel? match = FindNode(Nodes, target);
        if (match is not null)
        {
            SetSelectedNodes(new[] { match });
        }
    }

    private static ClipNodeViewModel? FindNode(IEnumerable<ClipNodeViewModel> nodes, ClipEditorNode target)
    {
        foreach (ClipNodeViewModel node in nodes)
        {
            if (ReferenceEquals(node.Node, target))
            {
                return node;
            }

            ClipNodeViewModel? nested = FindNode(node.Children, target);
            if (nested is not null)
            {
                return nested;
            }
        }

        return null;
    }

    private void SetExpandedAll(bool expanded) => SetExpandedAll(Nodes, expanded);

    private static void SetExpandedAll(IEnumerable<ClipNodeViewModel> nodes, bool expanded)
    {
        foreach (ClipNodeViewModel node in nodes)
        {
            if (node.IsGroup)
            {
                node.IsExpanded = expanded;
            }

            SetExpandedAll(node.Children, expanded);
        }
    }

    private void ApplyFilter() => ApplyFilter(Nodes);

    private bool ApplyFilter(IEnumerable<ClipNodeViewModel> nodes)
    {
        bool anyVisible = false;
        string filter = _filterText.Trim();

        foreach (ClipNodeViewModel node in nodes)
        {
            bool childVisible = ApplyFilter(node.Children);
            bool selfMatch = filter.Length == 0 || Matches(node, filter);
            node.IsVisible = selfMatch || childVisible;
            anyVisible |= node.IsVisible;
        }

        return anyVisible;
    }

    private bool Matches(ClipNodeViewModel node, string filter)
    {
        if (node.Name.Contains(filter, StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        return _filterAll && !node.IsGroup && node.Text.Contains(filter, StringComparison.OrdinalIgnoreCase);
    }
}
