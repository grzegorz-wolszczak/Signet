using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Signet.App.Actions;
using Signet.App.Resources;
using Signet.App.Services;
using Signet.Core;
using Signet.Core.BookManipulation;
using Signet.Core.MainUI;
using Signet.Core.Misc;
using Signet.Core.Resources;
using Signet.Core.Semantics;
using Signet.Core.Toc;

namespace Signet.App.ViewModels;

/// <summary>
/// View model of the Book Browser panel: the book's resource tree built from
/// <see cref="OpfModel"/>, multi-selection, opening on double click (event handled by the
/// TabManager) and the context menu: Open / Rename / Delete and reordering of Text files.
/// </summary>
public sealed partial class BookBrowserViewModel : ObservableObject, IDisposable
{
    private readonly SettingsStore _settings;
    private readonly IStatusBarService _statusBar;
    private readonly bool _showFullPath;

    private Book? _book;
    private OpfModel? _model;
    private BookBrowserNode[] _selection = Array.Empty<BookBrowserNode>();

    // Expanded folders — survive tree rebuilds (folder items are stable, a refresh only
    // replaces their children). A new book expands only Text.
    private readonly HashSet<OpfModelGroupKind> _expandedFolders = new() { OpfModelGroupKind.Text };

    /// <summary>
    /// Panel context menu actions that can be assigned a shortcut in Preferences. Set by
    /// <c>MainWindowViewModel</c>; <c>BookBrowserView</c> creates its own <c>KeyBinding</c>s
    /// from them, so the shortcuts work only while the panel has focus.
    /// </summary>
    public IReadOnlyList<AppAction> ShortcutActions { get; set; } = Array.Empty<AppAction>();

    /// <summary>Creates an empty view model (no book loaded).</summary>
    public BookBrowserViewModel(SettingsStore settings, IStatusBarService statusBar)
    {
        ArgumentNullException.ThrowIfNull(settings);
        _settings = settings;
        _statusBar = statusBar ?? throw new ArgumentNullException(nameof(statusBar));
        _showFullPath = settings.ShowFullPathOn != 0;
    }

    /// <summary>Request: open the given resources in tabs (handled by the TabManager).</summary>
    public event EventHandler<IReadOnlyList<Resource>>? OpenResourceRequested;

    /// <summary>
    /// Creates an automatic "Before: …" checkpoint right before an operation that changes the
    /// book; returns whether one was created. Set by the main window.
    /// </summary>
    public Func<string, bool>? CheckpointBefore { get; set; }

    /// <summary>
    /// Rolls back the checkpoint created before an operation that changed nothing or failed.
    /// Set by the main window.
    /// </summary>
    public Action? RewindCheckpoint { get; set; }

    /// <summary>
    /// Request: in-place rename of an entry has started (<see cref="EditingNode"/> is already
    /// set) — the view moves focus to the editor in the tree. Committed by
    /// <see cref="CommitInPlaceRename"/>.
    /// </summary>
    public event EventHandler<OpfModelEntry>? RenameRequested;

    /// <summary>Request: confirm deleting the entries (the view then calls <see cref="ApplyDelete"/>).</summary>
    public event EventHandler<IReadOnlyList<OpfModelEntry>>? DeleteRequested;

    /// <summary>Request: show the file picker (the view then calls <see cref="ApplyAddExistingFiles"/>).</summary>
    public event EventHandler? AddExistingFilesRequested;

    /// <summary>
    /// Request: show the "Rename with template" dialog for the entries (the view then calls
    /// <see cref="ApplyRenameWithTemplate"/>).
    /// </summary>
    public event EventHandler<IReadOnlyList<OpfModelEntry>>? RenameWithTemplateRequested;

    /// <summary>
    /// Request: show the "Bulk rename regex" dialog for the entries (the view then calls
    /// <see cref="ApplyRenameList"/>).
    /// </summary>
    public event EventHandler<IReadOnlyList<OpfModelEntry>>? BulkRegexRenameRequested;

    /// <summary>
    /// Request: show the "Move Files" (SelectFolder) dialog for the selected entries (the view then
    /// calls <see cref="ApplyMove"/>).
    /// </summary>
    public event EventHandler<IReadOnlyList<OpfModelEntry>>? MoveRequested;

    /// <summary>
    /// Request: show the "Add Semantics" dialog for the selected (X)HTML file (the view then calls
    /// <see cref="ApplySemanticCode"/>). The semantics apply to the whole file (no target
    /// fragment selection).
    /// </summary>
    public event EventHandler<OpfModelEntry>? AddSemanticsRequested;

    /// <summary>
    /// Request: show the "Link Stylesheets" dialog for the selected (X)HTML files (the view then
    /// calls <see cref="ApplyLinkStylesheets"/>).
    /// </summary>
    public event EventHandler<IReadOnlyList<HtmlResource>>? LinkStylesheetsRequested;

    /// <summary>
    /// Request: show the "Link Javascripts" dialog for the selected (X)HTML files (the view then
    /// calls <see cref="ApplyLinkJavascripts"/>).
    /// </summary>
    public event EventHandler<IReadOnlyList<HtmlResource>>? LinkJavascriptsRequested;

    /// <summary>
    /// Request: show an information dialog with the metadata of the selected files (the view
    /// calls <c>MessageDialog.ShowAsync</c> with the prepared text). Only basic file metadata is
    /// shown — no word count or list of linked resources.
    /// </summary>
    public event EventHandler<string>? GetInfoRequested;

    /// <summary>
    /// Request: show the save-to-disk dialog for the selected entry (the view then calls
    /// <see cref="ApplySaveAs"/>). Only a single selected file is supported.
    /// </summary>
    public event EventHandler<OpfModelEntry>? SaveAsRequested;

    /// <summary>
    /// Request: select the given nodes in the tree (the view sets <c>Tree.SelectedItems</c>).
    /// </summary>
    public event EventHandler<IReadOnlyList<BookBrowserNode>>? TreeSelectionRequested;

    /// <summary>
    /// Request: validate the given CSS stylesheets with W3C (handled by
    /// <c>MainWindowViewModel</c>, which has access to the validation profile settings and can
    /// open the file in a browser). Per-file context menu variant of the global action in the
    /// Tools menu.
    /// </summary>
    public event EventHandler<IReadOnlyList<CssResource>>? ValidateWithW3CRequested;

    /// <summary>Top-level tree nodes: group folders + loose files (OPF, NCX).</summary>
    public ObservableCollection<BookBrowserNode> Nodes { get; } = new();

    /// <summary>Whether a book is loaded.</summary>
    public bool HasBook => _book is not null;

    /// <summary>Node whose name is currently being edited in the tree (or <c>null</c>).</summary>
    [ObservableProperty]
    private BookBrowserNode? _editingNode;

    /// <summary>
    /// Finishes the in-place rename and — if the name changed — renames the file, then selects it
    /// again in the rebuilt tree. An empty or unchanged name does nothing.
    /// </summary>
    public void CommitInPlaceRename()
    {
        if (EditingNode is not { Entry: { } entry } node)
        {
            return;
        }

        string newFilename = node.EditText.Trim();
        EditingNode = null;
        if (newFilename.Length == 0 || string.Equals(newFilename, entry.Resource.Filename, StringComparison.Ordinal))
        {
            return;
        }

        ApplyRename(entry, newFilename);

        BookBrowserNode? renamed = Nodes
            .SelectMany(n => n.IsFolder ? n.Children : Enumerable.Repeat(n, 1))
            .FirstOrDefault(n => ReferenceEquals(n.Entry?.Resource, entry.Resource));
        if (renamed is not null)
        {
            TreeSelectionRequested?.Invoke(this, new[] { renamed });
        }
    }

    /// <summary>Abandons the in-place rename (Esc or focus loss).</summary>
    public void CancelInPlaceRename() => EditingNode = null;

    partial void OnEditingNodeChanged(BookBrowserNode? oldValue, BookBrowserNode? newValue)
    {
        if (oldValue is not null)
        {
            oldValue.IsEditing = false;
        }

        if (newValue is not null)
        {
            newValue.EditText = newValue.Entry?.Resource.Filename ?? newValue.Header;
            newValue.IsEditing = true;
        }
    }

    /// <summary>Sets (or clears) the book and rebuilds the tree.</summary>
    public void SetBook(Book? book)
    {
        if (_model is not null)
        {
            _model.Changed -= OnModelChanged;
            _model.Dispose();
            _model = null;
        }

        _book = book;
        if (book is not null)
        {
            _model = new OpfModel(book, _showFullPath);
            _model.Changed += OnModelChanged;
        }

        _expandedFolders.Clear();
        _expandedFolders.Add(OpfModelGroupKind.Text);
        Rebuild();
        OpenFirstHtmlFile();
        OnPropertyChanged(nameof(HasBook));
        AddBlankHtmlCommand.NotifyCanExecuteChanged();
        AddBlankCssCommand.NotifyCanExecuteChanged();
        AddBlankSvgCommand.NotifyCanExecuteChanged();
        AddBlankJsCommand.NotifyCanExecuteChanged();
        AddExistingFilesCommand.NotifyCanExecuteChanged();
        LinkStylesheetsSelectedCommand.NotifyCanExecuteChanged();
        LinkJavascriptsSelectedCommand.NotifyCanExecuteChanged();
    }

    /// <summary>
    /// Opens the first file of the Text folder so that a default tab is open right after a book
    /// is loaded or created. A book without HTML files opens nothing.
    /// </summary>
    private void OpenFirstHtmlFile()
    {
        if (_model?.GetFolder(OpfModelGroupKind.Text) is { Entries: [OpfModelEntry first, ..] })
        {
            OpenResourceRequested?.Invoke(this, new[] { first.Resource });
        }
    }

    /// <summary>
    /// Initial value for the "Rename with template" dialog: the template saved in settings, or —
    /// when empty — the first free name of the form <c>Section0001</c>.
    /// </summary>
    public string GetInitialRenameTemplate()
    {
        string template = _settings.RenameTemplate;
        if (string.IsNullOrEmpty(template) && _book is not null)
        {
            template = RenameTemplateNaming.GetFirstAvailableTemplateName(
                _book.GetFolderKeeper().GetAllFilenames(), "Section", "0001");
        }

        return template;
    }

    /// <summary>
    /// Resources currently selected in the tree (in tree order, without folders) — the source
    /// for the "Selected …" modes in Find &amp; Replace.
    /// </summary>
    public IReadOnlyList<Resource> SelectedResources =>
        _selection.Select(n => n.Entry!.Resource).ToList();

    /// <summary>Stores the current selection from the view (in tree order).</summary>
    public void UpdateSelection(IEnumerable<object?> selectedItems)
    {
        _selection = selectedItems.OfType<BookBrowserNode>().Where(n => !n.IsFolder).ToArray();
        OpenSelectedCommand.NotifyCanExecuteChanged();
        RenameSelectedCommand.NotifyCanExecuteChanged();
        DeleteSelectedCommand.NotifyCanExecuteChanged();
        RenameSelectedWithTemplateCommand.NotifyCanExecuteChanged();
        BulkRegexRenameSelectedCommand.NotifyCanExecuteChanged();
        MoveSelectedCommand.NotifyCanExecuteChanged();
        MoveTextUpCommand.NotifyCanExecuteChanged();
        MoveTextDownCommand.NotifyCanExecuteChanged();
        SortTextCommand.NotifyCanExecuteChanged();
        MergeSelectedCommand.NotifyCanExecuteChanged();
        SplitSelectedCommand.NotifyCanExecuteChanged();
        AddSemanticsSelectedCommand.NotifyCanExecuteChanged();
        CoverImageSelectedCommand.NotifyCanExecuteChanged();
        LinkStylesheetsSelectedCommand.NotifyCanExecuteChanged();
        LinkJavascriptsSelectedCommand.NotifyCanExecuteChanged();
        AddCopySelectedCommand.NotifyCanExecuteChanged();
        RenumberTocSelectedCommand.NotifyCanExecuteChanged();
        GetInfoSelectedCommand.NotifyCanExecuteChanged();
        SaveAsSelectedCommand.NotifyCanExecuteChanged();
        SelectAllInFolderCommand.NotifyCanExecuteChanged();
        ValidateSelectedWithW3CCommand.NotifyCanExecuteChanged();
    }

    /// <summary>Renames a file (called by <see cref="CommitInPlaceRename"/>).</summary>
    public void ApplyRename(OpfModelEntry entry, string newFilename)
    {
        ArgumentNullException.ThrowIfNull(entry);
        if (_book is null || string.IsNullOrWhiteSpace(newFilename))
        {
            return;
        }

        if (entry.ResourceType == ResourceType.Opf)
        {
            _statusBar.ShowMessage(Strings.Get("BookBrowser_CannotRenameOpf"), TimeSpan.FromSeconds(4), NotificationLevel.Warning);
            return;
        }

        bool checkpoint = Checkpoint(Strings.Format("CheckpointOp_Rename", entry.Resource.Filename));
        try
        {
            if (_model is not null && !_model.RenameResource(entry.Resource, newFilename.Trim(), out string? error))
            {
                Rewind(checkpoint);
                _statusBar.ShowMessage(error ?? Strings.Get("BookBrowser_RenameFailed"), TimeSpan.FromSeconds(5), NotificationLevel.Warning);
            }
        }
        catch (Exception ex) when (ex is InvalidOperationException or ArgumentException or System.IO.IOException)
        {
            Rewind(checkpoint);
            _statusBar.ShowMessage(Strings.Format("BookBrowser_RenameFailedDetail", ex.Message), TimeSpan.FromSeconds(5), NotificationLevel.Warning);
        }

        _model?.Refresh();
    }

    /// <summary>Deletes the given files (called by the view after confirmation).</summary>
    public void ApplyDelete(IReadOnlyList<OpfModelEntry> entries)
    {
        ArgumentNullException.ThrowIfNull(entries);
        if (_book is null || entries.Count == 0)
        {
            return;
        }

        List<Resource> removable = entries
            .Where(e => e.ResourceType != ResourceType.Opf)
            .Select(e => e.Resource)
            .ToList();
        if (removable.Count == 0)
        {
            return;
        }

        OpfResource opf = _book.GetOpf();
        string navBookPath = opf.EpubVersion.StartsWith('3') ? opf.GetNavResourceBookPath() : string.Empty;
        if (navBookPath.Length > 0 && removable.Any(r => string.Equals(r.BookPath, navBookPath, StringComparison.Ordinal)))
        {
            _statusBar.ShowMessage(Strings.Get("BookBrowser_CannotDeleteNav"), TimeSpan.FromSeconds(4), NotificationLevel.Warning);
            return;
        }

        NcxResource? ncx = _book.GetNcx();
        if (ncx is not null && removable.Contains(ncx))
        {
            _statusBar.ShowMessage(Strings.Get("BookBrowser_CannotDeleteNcx"), TimeSpan.FromSeconds(4), NotificationLevel.Warning);
            return;
        }

        int htmlCountAfterRemoval = _book.GetHtmlResources().Count - removable.OfType<HtmlResource>().Count();
        if (htmlCountAfterRemoval < 1)
        {
            _statusBar.ShowMessage(Strings.Get("BookBrowser_CannotDeleteAllHtml"), TimeSpan.FromSeconds(5), NotificationLevel.Warning);
            return;
        }

        bool checkpoint = Checkpoint(Strings.Get("CheckpointOp_DeleteFiles"));
        try
        {
            _book.GetFolderKeeper().BulkRemoveResources(removable);
            _book.Modified = true;
        }
        catch (Exception ex) when (ex is InvalidOperationException or System.IO.IOException)
        {
            Rewind(checkpoint);
            _statusBar.ShowMessage(Strings.Format("BookBrowser_DeleteFailed", ex.Message), TimeSpan.FromSeconds(5), NotificationLevel.Warning);
        }

        _model?.Refresh();
    }

    /// <summary>
    /// Copies the given files from disk into the book (called by the view after the file picker
    /// closes). There is no duplicate replacement dialog — conflicting names automatically get a
    /// unique suffix, as with "Add Blank".
    /// </summary>
    public void ApplyAddExistingFiles(IReadOnlyList<string> filePaths)
    {
        ArgumentNullException.ThrowIfNull(filePaths);
        if (_book is null || filePaths.Count == 0)
        {
            return;
        }

        bool checkpoint = Checkpoint(filePaths.Count == 1
            ? Strings.Format("CheckpointOp_AddFile", System.IO.Path.GetFileName(filePaths[0]))
            : Strings.Get("CheckpointOp_AddFiles"));
        try
        {
            IReadOnlyList<Resource> added = _book.AddExistingFiles(filePaths);
            if (added.Count > 0)
            {
                _model?.Refresh();
                OpenResourceRequested?.Invoke(this, added);
            }
            else
            {
                Rewind(checkpoint);
            }
        }
        catch (Exception ex) when (ex is InvalidOperationException or ArgumentException or System.IO.IOException)
        {
            Rewind(checkpoint);
            _statusBar.ShowMessage(Strings.Format("BookBrowser_AddFilesFailed", ex.Message), TimeSpan.FromSeconds(5), NotificationLevel.Warning);
        }
    }

    /// <summary>
    /// Applies "Rename with template" to the given entries (called by the view after the dialog
    /// closes with a template entered): validates the template, generates sequential names and
    /// saves the next template in <c>SettingsStore</c>.
    /// </summary>
    public void ApplyRenameWithTemplate(IReadOnlyList<OpfModelEntry> entries, string templateName)
    {
        ArgumentNullException.ThrowIfNull(entries);
        ArgumentNullException.ThrowIfNull(templateName);
        if (_book is null || _model is null || entries.Count == 0 || string.IsNullOrWhiteSpace(templateName))
        {
            return;
        }

        if (!RenameTemplateNaming.IsTemplateNameValid(templateName, out string? templateError))
        {
            _statusBar.ShowMessage(templateError ?? Strings.Get("BookBrowser_InvalidTemplate"), TimeSpan.FromSeconds(5), NotificationLevel.Warning);
            return;
        }

        _settings.RenameTemplate = templateName;

        List<Resource> resources = entries.Select(e => e.Resource).ToList();
        List<string> allFilenames = new(_book.GetFolderKeeper().GetAllFilenames());
        IReadOnlyList<string>? newFilenames = RenameTemplateNaming.BuildSequentialFilenames(
            resources, templateName, allFilenames, out string? buildError);

        if (newFilenames is null)
        {
            _statusBar.ShowMessage(buildError ?? Strings.Get("BookBrowser_NamesFailed"), TimeSpan.FromSeconds(5), NotificationLevel.Warning);
            return;
        }

        ApplyRenameList(resources, newFilenames);

        // Remember the next name in the sequence.
        (string basePart, string numberString, string extension) = RenameTemplateNaming.ParseTemplate(templateName);
        if (numberString.Length > 0)
        {
            int next = int.Parse(numberString, System.Globalization.CultureInfo.InvariantCulture) + resources.Count;
            string nextNumberString = next.ToString(System.Globalization.CultureInfo.InvariantCulture).PadLeft(numberString.Length, '0');
            string nextTemplate = RenameTemplateNaming.GetFirstAvailableTemplateName(
                _book.GetFolderKeeper().GetAllFilenames(), basePart, nextNumberString);
            _settings.RenameTemplate = nextTemplate + extension;
        }
    }

    /// <summary>
    /// Applies "Bulk rename regex" — renames the resources to the already computed and validated
    /// <paramref name="newFilenames"/> (preview built in the dialog). Also used by
    /// <see cref="ApplyRenameWithTemplate"/>.
    /// </summary>
    public void ApplyRenameList(IReadOnlyList<Resource> resources, IReadOnlyList<string> newFilenames)
    {
        ArgumentNullException.ThrowIfNull(resources);
        ArgumentNullException.ThrowIfNull(newFilenames);
        if (_model is null || resources.Count == 0)
        {
            return;
        }

        bool checkpoint = Checkpoint(Strings.Get("Operation_BulkRename"));
        bool ok = _model.RenameResourceList(resources, newFilenames, out IReadOnlyList<string> notRenamed, out IReadOnlyList<string> wellFormedErrors);
        if (!ok)
        {
            Rewind(checkpoint);
            string message = wellFormedErrors.Count > 0
                ? Strings.Format("BookBrowser_RenameRejectedXml", string.Join(", ", wellFormedErrors))
                : Strings.Format("BookBrowser_RenameFailedDetail", string.Join(", ", notRenamed));
            _statusBar.ShowMessage(message, TimeSpan.FromSeconds(6), NotificationLevel.Warning);
        }

        _model?.Refresh();
    }

    /// <summary>Rebuilds the tree (call after mutations made outside this view model, e.g. Add Cover / Nav in spine).</summary>
    public void Refresh() => _model?.Refresh();

    /// <summary>
    /// Folders of the media-type group of the given entries (for the suggestion list in the
    /// "Move Files" dialog). The group is taken from the first entry — the caller guarantees
    /// homogeneity through <see cref="MoveSelectedCommand"/> (<c>CanMove</c>).
    /// </summary>
    public IReadOnlyList<string> GetFoldersForSelection(IReadOnlyList<OpfModelEntry> entries)
    {
        if (_book is null || entries.Count == 0)
        {
            return Array.Empty<string>();
        }

        string group = MediaTypes.GetGroupFromMediaType(entries[0].Resource.MediaType, "other");
        return _book.GetFolderKeeper().GetFoldersForGroup(group);
    }

    /// <summary>Applies "Move Files" to the given entries, moving them to a common target folder.</summary>
    public void ApplyMove(IReadOnlyList<OpfModelEntry> entries, string folderPath)
    {
        ArgumentNullException.ThrowIfNull(entries);
        ArgumentNullException.ThrowIfNull(folderPath);
        if (_model is null || entries.Count == 0)
        {
            return;
        }

        // Moving a file is a rename of its path — hence the "Rename" / bulk rename checkpoint labels.
        bool checkpoint = Checkpoint(entries.Count == 1
            ? Strings.Format("CheckpointOp_Rename", entries[0].Resource.Filename)
            : Strings.Get("Operation_BulkRename"));
        bool ok = _model.MoveResourceList(entries.Select(e => e.Resource).ToList(), folderPath, out string? error);
        if (!ok)
        {
            Rewind(checkpoint);
            _statusBar.ShowMessage(error ?? Strings.Get("BookBrowser_MoveFailed"), TimeSpan.FromSeconds(5), NotificationLevel.Warning);
        }

        _model?.Refresh();
    }

    /// <summary>
    /// Initial data for the "Add Semantics" dialog: whether the book is EPUB 3 (nav landmarks) or
    /// EPUB 2 (guide), the entry's current semantic code and the dictionary of available codes.
    /// </summary>
    public (bool IsEpub3, string CurrentCode, IReadOnlyDictionary<string, DescriptiveInfo> CodeMap) GetSemanticsInfo(
        OpfModelEntry entry)
    {
        ArgumentNullException.ThrowIfNull(entry);
        if (_book is null || entry.Resource is not HtmlResource html)
        {
            return (false, string.Empty, GuideItems.GetCodeMap());
        }

        if (_book.IsEpub3)
        {
            HtmlResource? nav = _book.GetNavResource();
            string current = nav is not null ? new NavProcessor(nav).GetLandmarkCodeForResource(html) : string.Empty;
            return (true, current, Landmarks.GetCodeMap());
        }

        return (false, _book.GetOpf().GetGuideSemanticCodeForResource(html), GuideItems.GetCodeMap());
    }

    /// <summary>
    /// Applies the semantics chosen in the "Add Semantics" dialog (toggles the code — choosing
    /// the code that is already set removes it).
    /// </summary>
    public void ApplySemanticCode(OpfModelEntry entry, string code)
    {
        ArgumentNullException.ThrowIfNull(entry);
        ArgumentNullException.ThrowIfNull(code);
        if (_book is null || code.Length == 0 || entry.Resource is not HtmlResource html)
        {
            return;
        }

        if (_book.IsEpub3)
        {
            HtmlResource? nav = _book.GetNavResource();
            if (nav is null)
            {
                return;
            }

            // The nav may only be marked with "toc" — other landmarks are assigned to content files.
            if (ReferenceEquals(html, nav) && !string.Equals(code, "toc", StringComparison.Ordinal))
            {
                _statusBar.ShowMessage(Strings.Get("BookBrowser_NavOnlyToc"), TimeSpan.FromSeconds(4));
                return;
            }

            Checkpoint(Strings.Get("CheckpointOp_SetSemantics"));
            new NavProcessor(nav).AddLandmarkCode(html, code, toggle: true);
        }
        else
        {
            Checkpoint(Strings.Get("CheckpointOp_SetSemantics"));
            _book.GetOpf().AddGuideSemanticCode(html, code, toggle: true);
        }

        _book.Modified = true;
        _model?.Refresh();
    }

    /// <inheritdoc />
    public void Dispose()
    {
        if (_model is not null)
        {
            _model.Changed -= OnModelChanged;
            _model.Dispose();
            _model = null;
        }
    }

    [RelayCommand(CanExecute = nameof(HasFileSelection))]
    private void OpenSelected()
    {
        List<Resource> resources = _selection.Select(n => n.Entry!.Resource).ToList();
        if (resources.Count > 0)
        {
            OpenResourceRequested?.Invoke(this, resources);
        }
    }

    [RelayCommand(CanExecute = nameof(HasSingleSelection))]
    private void RenameSelected()
    {
        // Only a single file is edited in the tree; the OPF cannot be renamed.
        if (_selection[0].Entry!.ResourceType == ResourceType.Opf)
        {
            _statusBar.ShowMessage(Strings.Get("BookBrowser_CannotRenameOpf"), TimeSpan.FromSeconds(4), NotificationLevel.Warning);
            return;
        }

        EditingNode = _selection[0];
        RenameRequested?.Invoke(this, _selection[0].Entry!);
    }

    [RelayCommand(CanExecute = nameof(HasFileSelection))]
    private void DeleteSelected() =>
        DeleteRequested?.Invoke(this, _selection.Select(n => n.Entry!).ToArray());

    [RelayCommand(CanExecute = nameof(HasFileSelection))]
    private void RenameSelectedWithTemplate() =>
        RenameWithTemplateRequested?.Invoke(this, _selection.Select(n => n.Entry!).ToArray());

    [RelayCommand(CanExecute = nameof(HasFileSelection))]
    private void BulkRegexRenameSelected() =>
        BulkRegexRenameRequested?.Invoke(this, _selection.Select(n => n.Entry!).ToArray());

    [RelayCommand(CanExecute = nameof(CanMove))]
    private void MoveSelected() =>
        MoveRequested?.Invoke(this, _selection.Select(n => n.Entry!).ToArray());

    private bool CanMove =>
        _selection.Length > 0 &&
        _selection.Select(n => MediaTypes.GetGroupFromMediaType(n.Entry!.Resource.MediaType, "other"))
            .Distinct(StringComparer.Ordinal).Count() == 1;

    [RelayCommand(CanExecute = nameof(HasBook))]
    private void AddBlankHtml() => AddBlank(() => _book!.CreateEmptyHtmlFile());

    [RelayCommand(CanExecute = nameof(HasBook))]
    private void AddBlankCss() => AddBlank(() => _book!.CreateEmptyCssFile());

    [RelayCommand(CanExecute = nameof(HasBook))]
    private void AddBlankSvg() => AddBlank(() => _book!.CreateEmptySvgFile());

    [RelayCommand(CanExecute = nameof(CanAddBlankJs))]
    private void AddBlankJs() => AddBlank(() => _book!.CreateEmptyJsFile());

    private bool CanAddBlankJs => _book is not null && _book.IsEpub3;

    [RelayCommand(CanExecute = nameof(HasBook))]
    private void AddExistingFiles() => AddExistingFilesRequested?.Invoke(this, EventArgs.Empty);

    private void AddBlank(Func<Resource> create)
    {
        if (_book is null)
        {
            return;
        }

        Checkpoint(Strings.Get("CheckpointOp_AddNewFile"));
        Resource resource = create();
        _model?.Refresh();
        OpenResourceRequested?.Invoke(this, new[] { resource });
    }

    /// <summary>Whether exactly one (X)HTML or CSS file is selected — target of "Add Copy".</summary>
    private bool CanAddCopy =>
        _selection.Length == 1
        && _selection[0].Entry!.Resource is HtmlResource or CssResource;

    /// <summary>
    /// Duplicates the selected (X)HTML/CSS file with its current content and opens the copy.
    /// The new file goes to the end of its group, like the other "Add Blank *" actions.
    /// </summary>
    [RelayCommand(CanExecute = nameof(CanAddCopy))]
    private void AddCopySelected()
    {
        if (_book is null || _selection.Length != 1)
        {
            return;
        }

        switch (_selection[0].Entry!.Resource)
        {
            case HtmlResource source:
                AddBlank(() =>
                {
                    HtmlResource copy = _book.CreateEmptyHtmlFile();
                    copy.SetText(source.GetText());
                    return copy;
                });
                break;
            case CssResource source:
                AddBlank(() =>
                {
                    CssResource copy = _book.CreateEmptyCssFile();
                    copy.SetText(source.GetText());
                    return copy;
                });
                break;
        }
    }

    /// <summary>Whether exactly one NCX resource is selected — target of "Renumber TOC Entries".</summary>
    private bool CanRenumberToc =>
        _selection.Length == 1 && _selection[0].Entry!.Resource is NcxResource;

    /// <summary>
    /// Recomputes <c>playOrder</c> of all NCX entries and writes it back (EPUB 2). An EPUB 3 nav
    /// document has no <c>playOrder</c> numbering, so the action is limited to the NCX resource.
    /// </summary>
    [RelayCommand(CanExecute = nameof(CanRenumberToc))]
    private void RenumberTocSelected()
    {
        if (_selection.Length != 1 || _selection[0].Entry!.Resource is not NcxResource ncx)
        {
            return;
        }

        // SetNcxDocument(GetNcxDocument()) parses the current XML and writes it back —
        // NcxDocument.ToXml() always recomputes playOrder (RecomputePlayOrder).
        ncx.SetNcxDocument(ncx.GetNcxDocument());
        if (_book is not null)
        {
            _book.Modified = true;
        }

        _statusBar.ShowMessage(Strings.Get("BookBrowser_NcxRenumbered"), TimeSpan.FromSeconds(3));
    }

    /// <summary>
    /// Builds the information text about the selected files — bookpath, name, folder,
    /// media-type, size, EPUB version.
    /// </summary>
    [RelayCommand(CanExecute = nameof(HasFileSelection))]
    private void GetInfoSelected()
    {
        if (_selection.Length == 0)
        {
            return;
        }

        var lines = new List<string>();
        foreach (BookBrowserNode node in _selection)
        {
            Resource resource = node.Entry!.Resource;
            long size = 0;
            try
            {
                size = new System.IO.FileInfo(resource.FullPath).Length;
            }
            catch (System.IO.IOException)
            {
                // The file may not have been written to disk yet — skip the size.
            }

            string folder = resource.Folder.Length == 0 ? Strings.Get("BookBrowser_InfoRootFolder") : resource.Folder;
            lines.Add(Strings.Format(
                "BookBrowser_InfoTemplate",
                resource.BookPath,
                resource.Filename,
                folder,
                resource.MediaType,
                (size / 1024.0).ToString("F2", System.Globalization.CultureInfo.CurrentCulture),
                resource.EpubVersion));
        }

        GetInfoRequested?.Invoke(this, string.Join("\n\n", lines));
    }

    /// <summary>Applies "Save As" to the given entry — copies the file's current state to disk.</summary>
    public void ApplySaveAs(OpfModelEntry entry, string destinationPath)
    {
        ArgumentNullException.ThrowIfNull(entry);
        ArgumentException.ThrowIfNullOrWhiteSpace(destinationPath);

        try
        {
            entry.Resource.SaveToDisk();
            System.IO.File.Copy(entry.Resource.FullPath, destinationPath, overwrite: true);
        }
        catch (Exception ex) when (ex is System.IO.IOException or UnauthorizedAccessException)
        {
            _statusBar.ShowMessage(Strings.Format("BookBrowser_SaveFailed", ex.Message), TimeSpan.FromSeconds(5), NotificationLevel.Warning);
        }
    }

    [RelayCommand(CanExecute = nameof(HasSingleSelection))]
    private void SaveAsSelected() => SaveAsRequested?.Invoke(this, _selection[0].Entry!);

    /// <summary>
    /// Selects all files in the group (folder) containing the current selection. Requires at
    /// least one selected file in that group (folders have no selection state of their own in
    /// this model).
    /// </summary>
    [RelayCommand(CanExecute = nameof(HasFileSelection))]
    private void SelectAllInFolder()
    {
        BookBrowserNode? folder = Nodes.FirstOrDefault(n => n.IsFolder && n.Children.Contains(_selection[0]));
        if (folder is null)
        {
            return;
        }

        TreeSelectionRequested?.Invoke(this, folder.Children.ToArray());
    }

    [RelayCommand(CanExecute = nameof(CanMoveText))]
    private void MoveTextUp() => MoveSelectedText(-1);

    [RelayCommand(CanExecute = nameof(CanMoveText))]
    private void MoveTextDown() => MoveSelectedText(1);

    [RelayCommand(CanExecute = nameof(CanSortText))]
    private void SortText()
    {
        Checkpoint(Strings.Get("CheckpointOp_ReorderText"));
        _model?.SortTextByFilename(_selection.Select(n => n.Entry!).Where(IsText));
    }

    [RelayCommand(CanExecute = nameof(CanMergeText))]
    private void MergeSelected()
    {
        if (_book is null)
        {
            return;
        }

        List<HtmlResource> resources = _selection.Select(n => (HtmlResource)n.Entry!.Resource).ToList();
        bool checkpoint = Checkpoint(Strings.Format("CheckpointOp_MergeFiles", resources[0].Filename));
        HtmlResource? rejected = _book.MergeResources(resources);
        if (rejected is not null)
        {
            Rewind(checkpoint);
            _statusBar.ShowMessage(Strings.Get("BookBrowser_CannotMergeNav"), TimeSpan.FromSeconds(4), NotificationLevel.Warning);
            return;
        }

        _model?.Refresh();
    }

    [RelayCommand(CanExecute = nameof(CanSplitText))]
    private void SplitSelected()
    {
        if (_book is null)
        {
            return;
        }

        bool checkpoint = Checkpoint(Strings.Format(
            "CheckpointOp_Split",
            string.Join(", ", _selection.Where(n => IsText(n.Entry!)).Select(n => n.Entry!.Resource.Filename))));
        List<Resource> created = new();
        foreach (BookBrowserNode node in _selection.Where(n => IsText(n.Entry!)))
        {
            created.AddRange(_book.SplitOnSectionMarkers((HtmlResource)node.Entry!.Resource));
        }

        if (created.Count > 0)
        {
            _model?.Refresh();
        }
        else
        {
            Rewind(checkpoint);
            _statusBar.ShowMessage(Strings.Get("BookBrowser_NoSplitMarkers"), TimeSpan.FromSeconds(4));
        }
    }

    [RelayCommand(CanExecute = nameof(CanAddSemantics))]
    private void AddSemanticsSelected() => AddSemanticsRequested?.Invoke(this, _selection[0].Entry!);

    [RelayCommand(CanExecute = nameof(CanLinkStylesheets))]
    private void LinkStylesheetsSelected() =>
        LinkStylesheetsRequested?.Invoke(this, SelectedHtmlResources());

    [RelayCommand(CanExecute = nameof(CanLinkJavascripts))]
    private void LinkJavascriptsSelected() =>
        LinkJavascriptsRequested?.Invoke(this, SelectedHtmlResources());

    /// <summary>Whether all selected entries are CSS stylesheets — target of "Validate With W3C".</summary>
    private bool CanValidateSelectedWithW3C =>
        _selection.Length > 0 && _selection.All(n => n.Entry!.Resource is CssResource);

    [RelayCommand(CanExecute = nameof(CanValidateSelectedWithW3C))]
    private void ValidateSelectedWithW3C() =>
        ValidateWithW3CRequested?.Invoke(
            this, _selection.Select(n => (CssResource)n.Entry!.Resource).ToArray());

    /// <summary>Builds the "Link Stylesheets" map for the selected (X)HTML files (for the dialog).</summary>
    public IReadOnlyList<LinkableResourceEntry> GetStylesheetsMap(IReadOnlyList<HtmlResource> resources) =>
        _book?.GetStylesheetsMap(resources) ?? Array.Empty<LinkableResourceEntry>();

    /// <summary>Builds the "Link Javascripts" map for the selected (X)HTML files (for the dialog).</summary>
    public IReadOnlyList<LinkableResourceEntry> GetJavascriptsMap(IReadOnlyList<HtmlResource> resources) =>
        _book?.GetJavascriptsMap(resources) ?? Array.Empty<LinkableResourceEntry>();

    /// <summary>
    /// Applies the choice from the "Link Stylesheets" dialog: relinks CSS in the selected files to
    /// exactly <paramref name="orderedBookPaths"/>.
    /// </summary>
    public void ApplyLinkStylesheets(
        IReadOnlyList<HtmlResource> resources, IReadOnlyList<string> orderedBookPaths)
    {
        ArgumentNullException.ThrowIfNull(resources);
        ArgumentNullException.ThrowIfNull(orderedBookPaths);
        if (_book is null)
        {
            return;
        }

        bool checkpoint = Checkpoint(Strings.Get("CheckpointOp_LinkStylesheets"));
        LinkResourcesResult result = _book.LinkStylesheetsToResources(resources, orderedBookPaths);
        if (!result.Applied)
        {
            Rewind(checkpoint);
        }

        ReportLinkResult(result, Strings.Get("BookBrowserView_LinkStylesheetsTitle"));
    }

    /// <summary>
    /// Applies the choice from the "Link Javascripts" dialog: relinks scripts in the selected files.
    /// </summary>
    public void ApplyLinkJavascripts(
        IReadOnlyList<HtmlResource> resources, IReadOnlyList<string> orderedBookPaths)
    {
        ArgumentNullException.ThrowIfNull(resources);
        ArgumentNullException.ThrowIfNull(orderedBookPaths);
        if (_book is null)
        {
            return;
        }

        LinkResourcesResult result = _book.LinkJavascriptsToResources(resources, orderedBookPaths);
        ReportLinkResult(result, Strings.Get("BookBrowserView_LinkJavascriptsTitle"));
    }

    private void ReportLinkResult(LinkResourcesResult result, string operationName)
    {
        if (!result.Applied)
        {
            _statusBar.ShowMessage(
                Strings.Format("Status_CancelledNotWellFormed", operationName, result.NotWellFormed?.Filename),
                TimeSpan.FromSeconds(6), NotificationLevel.Warning);
            return;
        }

        _model?.Refresh();
    }

    private List<HtmlResource> SelectedHtmlResources() =>
        _selection.Select(n => (HtmlResource)n.Entry!.Resource).ToList();

    [RelayCommand(CanExecute = nameof(CanSetCoverImage))]
    private void CoverImageSelected()
    {
        if (_book is null || _selection[0].Entry!.Resource is not ImageResource image)
        {
            return;
        }

        Checkpoint(Strings.Get("CheckpointOp_SetSemantics"));
        _book.GetOpf().SetResourceAsCoverImage(image);
        _book.Modified = true;
        _model?.Refresh();
    }

    private bool HasFileSelection => _selection.Length > 0;

    private bool HasSingleSelection => _selection.Length == 1;

    private bool CanAddSemantics => _selection.Length == 1 && IsText(_selection[0].Entry!);

    private bool HasHtmlSelection =>
        _selection.Length > 0 && _selection.All(n => IsText(n.Entry!));

    private bool CanLinkStylesheets =>
        _book is not null && HasHtmlSelection && _book.GetCssResources().Count > 0;

    private bool CanLinkJavascripts =>
        _book is not null && HasHtmlSelection && _book.GetJavascriptResources().Count > 0;

    private bool CanSetCoverImage =>
        _selection.Length == 1 && _selection[0].Entry!.ResourceType == ResourceType.Image;

    private bool CanMoveText => _selection.Length == 1 && IsText(_selection[0].Entry!);

    private bool CanSortText => _selection.Length >= 2 && _selection.All(n => IsText(n.Entry!));

    private bool CanMergeText => _selection.Length >= 2 && _selection.All(n => IsText(n.Entry!));

    private bool CanSplitText => _selection.Length >= 1 && _selection.All(n => IsText(n.Entry!));

    private static bool IsText(OpfModelEntry entry) => entry.ResourceType == ResourceType.Html;

    private void MoveSelectedText(int delta)
    {
        if (_selection.Length == 1 && IsText(_selection[0].Entry!))
        {
            Checkpoint(Strings.Get("CheckpointOp_ReorderText"));
            _model?.MoveText(_selection[0].Entry!, delta);
        }
    }

    private bool Checkpoint(string operation) => CheckpointBefore?.Invoke(operation) ?? false;

    private void Rewind(bool checkpointCreated)
    {
        if (checkpointCreated)
        {
            RewindCheckpoint?.Invoke();
        }
    }

    private void OnModelChanged(object? sender, EventArgs e) => Rebuild();

    private void OnFolderNodePropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (e.PropertyName != nameof(BookBrowserNode.IsExpanded) || sender is not BookBrowserNode { FolderKind: { } kind } node)
        {
            return;
        }

        if (node.IsExpanded)
        {
            _expandedFolders.Add(kind);
        }
        else
        {
            _expandedFolders.Remove(kind);
        }
    }

    private void Rebuild()
    {
        foreach (BookBrowserNode node in Nodes)
        {
            node.PropertyChanged -= OnFolderNodePropertyChanged;
        }

        // A rebuild replaces the nodes — an ongoing rename no longer has a target.
        EditingNode = null;
        Nodes.Clear();
        if (_model is null)
        {
            return;
        }

        foreach (OpfModelFolder folder in _model.Folders)
        {
            // Folder tooltip: "<group default folder> N file(s)".
            string toolTip = Strings.Format("BookBrowserView_FolderTip", folder.DefaultFolder, folder.Entries.Count);
            BookBrowserNode folderNode = BookBrowserNode.Folder(folder.Kind, folder.Name, toolTip);
            foreach (OpfModelEntry entry in folder.Entries)
            {
                folderNode.Children.Add(BookBrowserNode.File(entry));
            }

            folderNode.IsExpanded = _expandedFolders.Contains(folder.Kind);
            folderNode.PropertyChanged += OnFolderNodePropertyChanged;
            Nodes.Add(folderNode);
        }

        foreach (OpfModelEntry entry in _model.TopLevelFiles)
        {
            Nodes.Add(BookBrowserNode.File(entry));
        }
    }
}

/// <summary>Book Browser tree node: a group folder or a single file.</summary>
public sealed partial class BookBrowserNode : ObservableObject
{
    private readonly string? _folderToolTip;

    private BookBrowserNode(string header, OpfModelGroupKind? folderKind, string? folderToolTip, OpfModelEntry? entry)
    {
        Header = header;
        FolderKind = folderKind;
        _folderToolTip = folderToolTip;
        Entry = entry;
    }

    /// <summary>Node text.</summary>
    public string Header { get; }

    /// <summary>Whether the node is a group folder.</summary>
    public bool IsFolder => FolderKind is not null;

    /// <summary>Group kind for a folder (or <c>null</c> for a file).</summary>
    public OpfModelGroupKind? FolderKind { get; }

    /// <summary>Whether the folder is expanded in the tree (two-way bound to <c>TreeViewItem</c>).</summary>
    [ObservableProperty]
    private bool _isExpanded;

    /// <summary>Whether the file name is being edited in place (driven by <see cref="BookBrowserViewModel.EditingNode"/>).</summary>
    [ObservableProperty]
    private bool _isEditing;

    /// <summary>Text of the in-place name editor (file name only, without path).</summary>
    [ObservableProperty]
    private string _editText = string.Empty;

    /// <summary>Model entry for a file (or <c>null</c> for a folder).</summary>
    public OpfModelEntry? Entry { get; }

    /// <summary>Child nodes (files in the folder).</summary>
    public ObservableCollection<BookBrowserNode> Children { get; } = new();

    /// <summary>Tooltip: file description, or "folder + file count" for a group.</summary>
    public string ToolTip => Entry?.ToolTip ?? _folderToolTip ?? Header;

    /// <summary>Whether the (X)HTML file is not well-formed.</summary>
    public bool IsMalformed => Entry?.IsWellFormed == false;

    /// <summary>Creates a group folder node.</summary>
    public static BookBrowserNode Folder(OpfModelGroupKind kind, string name, string toolTip) =>
        new(name, kind, toolTip, entry: null);

    /// <summary>Creates a file node.</summary>
    public static BookBrowserNode File(OpfModelEntry entry) =>
        new(entry.DisplayName, folderKind: null, folderToolTip: null, entry);
}
