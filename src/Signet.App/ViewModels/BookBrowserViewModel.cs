using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Signet.App.Actions;
using Signet.App.Infrastructure;
using Signet.App.Resources;
using Signet.App.Services;
using Signet.Core;
using Signet.Core.BookManipulation;
using Signet.Core.Fonts;
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
public sealed partial class BookBrowserViewModel : ObservableObject, IDisposable, IFontManagementHost
{
    private readonly SettingsStore _settings;
    private readonly IStatusBarService _statusBar;
    private readonly MissingDoctypeGuard _doctypeGuard;

    private Book? _book;
    private OpfModel? _model;
    private BookBrowserNode[] _selection = Array.Empty<BookBrowserNode>();

    // The group folders selected in the tree (a right click on a folder selects it).
    private OpfModelGroupKind[] _selectedFolders = Array.Empty<OpfModelGroupKind>();

    // Expanded folders — survive tree rebuilds (folder items are stable, a refresh only
    // replaces their children). A new book expands the folders chosen in Preferences (Text by default).
    private readonly HashSet<OpfModelGroupKind> _expandedFolders = new();

    /// <summary>
    /// Panel context menu actions that can be assigned a shortcut in Preferences. Set by
    /// <c>MainWindowViewModel</c>; <c>BookBrowserView</c> creates its own <c>KeyBinding</c>s
    /// from them, so the shortcuts work only while the panel has focus.
    /// </summary>
    public IReadOnlyList<AppAction> ShortcutActions { get; set; } = Array.Empty<AppAction>();

    /// <summary>Creates an empty view model (no book loaded).</summary>
    public BookBrowserViewModel(SettingsStore settings, IStatusBarService statusBar, MissingDoctypeGuard? doctypeGuard = null)
    {
        ArgumentNullException.ThrowIfNull(settings);
        _settings = settings;
        _statusBar = statusBar ?? throw new ArgumentNullException(nameof(statusBar));
        _doctypeGuard = doctypeGuard ?? new MissingDoctypeGuard(settings);
        settings.SettingChanged += OnSettingChanged;
    }

    private void OnSettingChanged(object? sender, SettingChangedEventArgs e)
    {
        if (e.QualifiedKey.Contains("book_browser_items_font", StringComparison.Ordinal)
            || e.QualifiedKey.Contains("book_browser_header_font", StringComparison.Ordinal)
            || e.QualifiedKey.Contains("book_browser_indent", StringComparison.Ordinal)
            || e.QualifiedKey.Contains("book_browser_cell_", StringComparison.Ordinal)
            || e.QualifiedKey.Contains("book_browser_show_folders", StringComparison.Ordinal)
            || e.QualifiedKey.Contains("book_browser_appearance", StringComparison.Ordinal))
        {
            // The names of the files may show their folders now (or not any more): the tree is rebuilt.
            if (_model is not null)
            {
                _model.ShowFolders = _settings.BookBrowserAppearance.ShowFolders;
            }

            AppearanceChanged?.Invoke(this, EventArgs.Empty);
        }
    }

    /// <summary>
    /// Whether this is the sample Book Browser of the Preferences window (a preview of the look): it shows all the optional
    /// columns, leaves the choice of the columns in the settings alone, and its view has no menus.
    /// </summary>
    public bool IsSample { get; init; }

    /// <summary>
    /// The look of the tree now: the settings (fonts, indentation, the height of the rows), with the fonts the theme sets while it
    /// uses its own fonts.
    /// </summary>
    public BookBrowserAppearance Appearance => _settings.BookBrowserAppearance;

    /// <summary>Raised when a setting of the look of the tree changed (the view applies <see cref="Appearance"/> again).</summary>
    public event EventHandler? AppearanceChanged;

    /// <summary>Request: open the given resources in tabs (handled by the TabManager).</summary>
    public event EventHandler<IReadOnlyList<Resource>>? OpenResourceRequested;

    /// <summary>
    /// Raised after deleting files also removed their entries from the table of contents (NAV / NCX) — the main window
    /// refreshes the open tabs and the table of contents panel.
    /// </summary>
    public event EventHandler? TocChanged;

    /// <summary>
    /// Request: Merge cannot be carried out for the selection (files other than HTML, the navigation document) — the
    /// argument is the message; the view shows it in an error window.
    /// </summary>
    public event EventHandler<string>? MergeErrorRequested;

    /// <summary>
    /// Runs the operations that change the book (each behind an automatic "Before: …" checkpoint, off the UI thread, under a
    /// progress window). Set by the main window; <c>null</c> (the tests): the work just runs, without a checkpoint.
    /// </summary>
    public IBookOperationHost? Operations { get; set; }

    /// <summary>
    /// Commits the content of the open tabs into the resources — called before an operation that edits files the tabs
    /// may show (deleting files also edits the table of contents). Set by the main window.
    /// </summary>
    public Action? SaveOpenTabs { get; set; }

    /// <summary>
    /// Rolls back the checkpoint created before an operation that changed nothing or failed.
    /// Set by the main window.
    /// </summary>
    public Action? RewindCheckpoint { get; set; }

    /// <summary>
    /// Reloads the open tabs and Preview after an operation that rewrote file contents behind the editors
    /// (Merge, Split At Markers) — otherwise a tab keeps showing the old text and writes it back on the next edit.
    /// Set by the main window.
    /// </summary>
    public Action? ReloadOpenTabs { get; set; }

    /// <summary>
    /// Request: in-place rename of an entry has started (<see cref="EditingNode"/> is already
    /// set) — the view moves focus to the editor in the tree. Committed by
    /// <see cref="CommitInPlaceRenameAsync"/>.
    /// </summary>
    public event EventHandler<OpfModelEntry>? RenameRequested;

    /// <summary>Request: confirm deleting the entries (the view then calls <see cref="ApplyDeleteAsync"/>).</summary>
    public event EventHandler<IReadOnlyList<OpfModelEntry>>? DeleteRequested;

    /// <summary>Request: show the file picker (the view then calls <see cref="ApplyAddExistingFilesAsync"/>).</summary>
    public event EventHandler? AddExistingFilesRequested;

    /// <summary>
    /// Request: show the file picker for font files (the view then adds them with <see cref="AddFilesAsync"/> and shows the
    /// "Add @font-face rules" dialog).
    /// </summary>
    public event EventHandler? AddFontFilesRequested;

    /// <summary>Request: open "Manage Fonts" (handled by the main window).</summary>
    public event EventHandler? ManageFontsRequested;

    /// <summary>
    /// Opens a file at an offset in the main window (navigation from the font dialogs). Set by the main window.
    /// </summary>
    public Action<string, int>? NavigateTo { get; set; }

    /// <summary>
    /// Request: show the "Mass Rename" window for the entries (the view then calls <see cref="ApplyMassRenameAsync"/>).
    /// </summary>
    public event EventHandler<IReadOnlyList<OpfModelEntry>>? MassRenameRequested;

    /// <summary>
    /// Request: show the "Bulk rename regex" dialog for the entries (the view then calls
    /// <see cref="ApplyRenameListAsync"/>).
    /// </summary>
    public event EventHandler<IReadOnlyList<OpfModelEntry>>? BulkRegexRenameRequested;

    /// <summary>
    /// Request: show the "Move Files" (SelectFolder) dialog for the selected entries (the view then
    /// calls <see cref="ApplyMoveAsync"/>).
    /// </summary>
    public event EventHandler<IReadOnlyList<OpfModelEntry>>? MoveRequested;

    /// <summary>
    /// Request: show the "Add Semantics" dialog for the selected (X)HTML file (the view then calls
    /// <see cref="ApplySemanticCodeAsync"/>). The semantics apply to the whole file (no target
    /// fragment selection).
    /// </summary>
    public event EventHandler<OpfModelEntry>? AddSemanticsRequested;

    /// <summary>
    /// Request: show the "Link Stylesheets" dialog for the selected (X)HTML files (the view then
    /// calls <see cref="ApplyLinkStylesheetsAsync"/>).
    /// </summary>
    public event EventHandler<IReadOnlyList<HtmlResource>>? LinkStylesheetsRequested;

    /// <summary>
    /// Request: show the "Link Javascripts" dialog for the selected (X)HTML files (the view then
    /// calls <see cref="ApplyLinkJavascriptsAsync"/>).
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

    /// <summary>
    /// The optional columns shown next to the name (comma-separated ids: "ReadingOrder", "Semantics", "Properties"),
    /// remembered in the settings.
    /// </summary>
    public string OptionalColumns
    {
        get => IsSample ? "ReadingOrder,Semantics,Properties" : _settings.BookBrowserColumns;
        set
        {
            if (!IsSample)
            {
                _settings.BookBrowserColumns = value ?? string.Empty;
            }
        }
    }

    /// <summary>Node whose name is currently being edited in the tree (or <c>null</c>).</summary>
    [ObservableProperty]
    private BookBrowserNode? _editingNode;

    /// <summary>
    /// Finishes the in-place rename and — if the name changed — renames the file, then selects it
    /// again in the rebuilt tree. An empty or unchanged name does nothing.
    /// </summary>
    public async Task CommitInPlaceRenameAsync()
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

        await ApplyRenameAsync(entry, newFilename).ConfigureAwait(true);

        BookBrowserNode? renamed = Nodes
            .SelectMany(n => n.IsFolder ? n.Children : Enumerable.Repeat(n, 1))
            .FirstOrDefault(n => ReferenceEquals(n.Entry?.Resource, entry.Resource));
        if (renamed is not null)
        {
            TreeSelectionRequested?.Invoke(this, new[] { renamed });
        }
    }

    /// <summary>
    /// Selects the file in the tree (and shows it: its folder is expanded) — the Book Browser follows the active tab. A file
    /// that is part of the selection already leaves it as it is, so opening several selected files keeps the selection.
    /// </summary>
    public void SelectResource(Resource resource)
    {
        ArgumentNullException.ThrowIfNull(resource);
        if (_selection.Any(n => ReferenceEquals(n.Entry?.Resource, resource)))
        {
            return;
        }

        foreach (BookBrowserNode root in Nodes)
        {
            BookBrowserNode? node = root.IsFolder
                ? root.Children.FirstOrDefault(n => ReferenceEquals(n.Entry?.Resource, resource))
                : ReferenceEquals(root.Entry?.Resource, resource) ? root : null;
            if (node is null)
            {
                continue;
            }

            if (root.IsFolder)
            {
                root.IsExpanded = true;
            }

            TreeSelectionRequested?.Invoke(this, new[] { node });
            return;
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
            _model = new OpfModel(book, _settings.BookBrowserAppearance.ShowFolders);
            _model.Changed += OnModelChanged;
        }

        _expandedFolders.Clear();
        foreach (OpfModelGroupKind kind in Enum.GetValues<OpfModelGroupKind>())
        {
            if (_settings.GetBookBrowserExpandOnLoad(kind))
            {
                _expandedFolders.Add(kind);
            }
        }

        Rebuild();
        OpenFirstHtmlFile();
        OnPropertyChanged(nameof(HasBook));
        RefreshAddItems();
        AddBlankHtmlCommand.NotifyCanExecuteChanged();
        AddBlankCssCommand.NotifyCanExecuteChanged();
        AddBlankSvgCommand.NotifyCanExecuteChanged();
        AddBlankJsCommand.NotifyCanExecuteChanged();
        AddExistingFilesCommand.NotifyCanExecuteChanged();
        AddFontFilesCommand.NotifyCanExecuteChanged();
        ManageFontsCommand.NotifyCanExecuteChanged();
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

    /// <summary>The values the "Mass Rename" window starts with: the ones used last time.</summary>
    public MassRenameOptions GetInitialMassRename() => _settings.MassRename;

    /// <summary>
    /// Whether the files can be numbered in the order of the book: only when all of them are (X)HTML files. The ones that are
    /// not in the spine (e.g. the navigation document) come last.
    /// </summary>
    public static bool CanUseBookOrder(IReadOnlyList<OpfModelEntry> entries)
    {
        ArgumentNullException.ThrowIfNull(entries);
        return entries.Count > 0 && entries.All(e => e.ResourceType == ResourceType.Html);
    }

    /// <summary>
    /// What "Mass Rename" would do with the entries and the options (the preview of the window, and the plan
    /// <see cref="ApplyMassRenameAsync"/> carries out): the resources in the order they are numbered and their new names, or the
    /// message why there are none. The option "in the order of the book" counts only when <see cref="CanUseBookOrder"/>.
    /// </summary>
    public MassRenamePlan PlanMassRename(IReadOnlyList<OpfModelEntry> entries, MassRenameOptions options)
    {
        ArgumentNullException.ThrowIfNull(entries);
        IEnumerable<OpfModelEntry> ordered = options.BookOrder && CanUseBookOrder(entries)
            ? entries.OrderBy(e => e.ReadingOrder >= 0 ? e.ReadingOrder : int.MaxValue)
            : entries;
        List<Resource> resources = ordered.Select(e => e.Resource).ToList();
        if (_book is null)
        {
            return new MassRenamePlan(resources, null, null);
        }

        IReadOnlyList<string>? names = MassRenameNaming.BuildFilenames(
            resources, options.Normalized(), new List<string>(_book.GetFolderKeeper().GetAllFilenames()), out string? error);
        return new MassRenamePlan(resources, names, error);
    }

    /// <summary>
    /// Resources currently selected in the tree (in tree order, without folders) — the source
    /// for the "Selected …" modes in Find &amp; Replace.
    /// </summary>
    public IReadOnlyList<Resource> SelectedResources =>
        _selection.Select(n => n.Entry!.Resource).ToList();

    /// <summary>
    /// Whether the context menu is the fonts one: only the Fonts folder and/or font files are selected. The view then
    /// shows only the actions that make sense for fonts.
    /// </summary>
    public bool IsFontMenu =>
        (_selection.Length > 0 || _selectedFolders.Length > 0)
        && _selection.All(n => n.Entry!.ResourceType == ResourceType.Font)
        && _selectedFolders.All(k => k == OpfModelGroupKind.Fonts);

    /// <summary>Whether only the Fonts folder is selected (its menu has no actions on files).</summary>
    public bool IsFontFolderMenu => IsFontMenu && _selection.Length == 0;

    /// <summary>
    /// The folder the "add" actions of the context menu apply to: the one folder selected, when no file is selected;
    /// <c>null</c> on files (or several folders, or nothing), where the menu has no "add" actions.
    /// </summary>
    public OpfModelGroupKind? AddTarget => _selection.Length == 0 && _selectedFolders.Length == 1 ? _selectedFolders[0] : null;

    /// <summary>Whether the "add" actions are in the context menu (a folder is selected).</summary>
    public bool ShowAddItems => AddTarget is not null;

    /// <summary>Whether "Add Blank HTML File" fits the selected folder (Text).</summary>
    public bool CanAddBlankHtmlHere => AddTarget == OpfModelGroupKind.Text;

    /// <summary>Whether "Add Blank Stylesheet" fits the selected folder (Styles).</summary>
    public bool CanAddBlankCssHere => AddTarget == OpfModelGroupKind.Styles;

    /// <summary>Whether "Add Blank SVG Image" fits the selected folder (Images).</summary>
    public bool CanAddBlankSvgHere => AddTarget == OpfModelGroupKind.Images;

    /// <summary>
    /// Whether "Add Blank Javascript" fits the selected folder: Misc, where scripts are kept, and only in an EPUB 3 book (an
    /// EPUB 2 book has no scripts).
    /// </summary>
    public bool CanAddBlankJsHere => AddTarget == OpfModelGroupKind.Misc && _book is { IsEpub3: true };

    /// <summary>
    /// Why "Add Blank Javascript" cannot be used now, for its tooltip: scripts are an EPUB 3 feature (the book is EPUB 2), or
    /// they are kept in the Misc folder; <c>null</c> while it can be used.
    /// </summary>
    public string? AddBlankJsTip => _book is null || CanAddBlankJsHere
        ? null
        : Strings.Get(_book.IsEpub3 ? "BookBrowserView_AddBlankJsNotHere" : "BookBrowserView_AddBlankJsEpub2");

    /// <summary>Whether "Add Existing Files" fits the selected folder (every folder but Fonts, which has its own action).</summary>
    public bool CanAddExistingFilesHere => AddTarget is { } kind && kind != OpfModelGroupKind.Fonts;

    /// <summary>The text of the "add existing files" action for the selected folder (e.g. "Add Images from Files…").</summary>
    public string AddExistingFilesHeader => Strings.Get(AddTarget switch
    {
        OpfModelGroupKind.Text => "BookBrowserView_AddHtmlFiles",
        OpfModelGroupKind.Styles => "BookBrowserView_AddStylesheetFiles",
        OpfModelGroupKind.Images => "BookBrowserView_AddImageFiles",
        OpfModelGroupKind.Audio => "BookBrowserView_AddAudioFiles",
        OpfModelGroupKind.Video => "BookBrowserView_AddVideoFiles",
        _ => "BookBrowserView_AddExistingFiles",
    });

    /// <summary>
    /// The file patterns (e.g. <c>*.css</c>) of the files the selected folder takes, for the file picker of "add existing
    /// files"; empty for Misc (any file).
    /// </summary>
    public IReadOnlyList<string> AddExistingFilePatterns
    {
        get
        {
            if (AddTarget is not { } kind || kind == OpfModelGroupKind.Misc)
            {
                return Array.Empty<string>();
            }

            return MediaTypes.ExtensionToMediaType
                .Where(p => MediaTypes.MediaTypeToGroup.TryGetValue(p.Value, out string? group)
                            && string.Equals(group, kind.ToString(), StringComparison.Ordinal))
                .Select(p => "*." + p.Key)
                .OrderBy(x => x, StringComparer.Ordinal)
                .ToList();
        }
    }

    /// <summary>
    /// Announces that context-menu items may have changed (the selection, the book or the language changed); the view
    /// calls it when the menu opens.
    /// </summary>
    public void RefreshAddItems()
    {
        OnPropertyChanged(nameof(AddTarget));
        OnPropertyChanged(nameof(ShowAddItems));
        OnPropertyChanged(nameof(CanAddBlankHtmlHere));
        OnPropertyChanged(nameof(CanAddBlankCssHere));
        OnPropertyChanged(nameof(CanAddBlankSvgHere));
        OnPropertyChanged(nameof(CanAddBlankJsHere));
        OnPropertyChanged(nameof(AddBlankJsTip));
        OnPropertyChanged(nameof(CanAddExistingFilesHere));
        OnPropertyChanged(nameof(AddExistingFilesHeader));
        RemoveSemanticsSelectedCommand.NotifyCanExecuteChanged();
    }

    /// <summary>Stores the current selection from the view (in tree order).</summary>
    public void UpdateSelection(IEnumerable<object?> selectedItems)
    {
        List<BookBrowserNode> nodes = selectedItems.OfType<BookBrowserNode>().ToList();
        _selection = nodes.Where(n => !n.IsFolder).ToArray();
        _selectedFolders = nodes.Where(n => n.FolderKind is not null).Select(n => n.FolderKind!.Value).ToArray();
        OnPropertyChanged(nameof(IsFontMenu));
        OnPropertyChanged(nameof(IsFontFolderMenu));
        RefreshAddItems();
        OpenSelectedCommand.NotifyCanExecuteChanged();
        RenameSelectedCommand.NotifyCanExecuteChanged();
        DeleteSelectedCommand.NotifyCanExecuteChanged();
        RenameSelectedWithTemplateCommand.NotifyCanExecuteChanged();
        BulkRegexRenameSelectedCommand.NotifyCanExecuteChanged();
        MoveSelectedCommand.NotifyCanExecuteChanged();
        MoveTextUpCommand.NotifyCanExecuteChanged();
        MoveTextDownCommand.NotifyCanExecuteChanged();
        MoveTextToTopCommand.NotifyCanExecuteChanged();
        MoveTextToBottomCommand.NotifyCanExecuteChanged();
        SortTextCommand.NotifyCanExecuteChanged();
        MergeSelectedCommand.NotifyCanExecuteChanged();
        SplitSelectedCommand.NotifyCanExecuteChanged();
        AddSemanticsSelectedCommand.NotifyCanExecuteChanged();
        CoverImageSelectedCommand.NotifyCanExecuteChanged();
        MarkAsNavSelectedCommand.NotifyCanExecuteChanged();
        LinkStylesheetsSelectedCommand.NotifyCanExecuteChanged();
        LinkJavascriptsSelectedCommand.NotifyCanExecuteChanged();
        AddCopySelectedCommand.NotifyCanExecuteChanged();
        RenumberTocSelectedCommand.NotifyCanExecuteChanged();
        GetInfoSelectedCommand.NotifyCanExecuteChanged();
        SaveAsSelectedCommand.NotifyCanExecuteChanged();
        SelectAllInFolderCommand.NotifyCanExecuteChanged();
        ValidateSelectedWithW3CCommand.NotifyCanExecuteChanged();
    }

    /// <summary>Renames a file (called by <see cref="CommitInPlaceRenameAsync"/>).</summary>
    public async Task ApplyRenameAsync(OpfModelEntry entry, string newFilename)
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

        string title = Strings.Format("CheckpointOp_Rename", entry.Resource.Filename);
        BookOperationOutcome<string?> run = await RunOperationAsync<string?>(title, title, () =>
        {
            try
            {
                return _model is not null && !_model.RenameResource(entry.Resource, newFilename.Trim(), out string? error)
                    ? error ?? Strings.Get("BookBrowser_RenameFailed")
                    : null;
            }
            catch (Exception ex) when (ex is InvalidOperationException or ArgumentException or System.IO.IOException)
            {
                return Strings.Format("BookBrowser_RenameFailedDetail", ex.Message);
            }
        }).ConfigureAwait(true);
        if (!run.Completed)
        {
            return;
        }

        if (run.Result is { } failure)
        {
            Rewind(run.Checkpoint);
            _statusBar.ShowMessage(failure, TimeSpan.FromSeconds(5), NotificationLevel.Warning);
        }

        _model?.Refresh();
    }

    /// <summary>Deletes the given files (called by the view after confirmation).</summary>
    public Task ApplyDeleteAsync(IReadOnlyList<OpfModelEntry> entries)
    {
        ArgumentNullException.ThrowIfNull(entries);
        return DeleteResourcesAsync(entries.Select(e => e.Resource).ToList());
    }

    /// <inheritdoc/>
    public async Task DeleteResourcesAsync(
        IReadOnlyList<Resource> resources, FontRemovalPlan? plan = null, IReadOnlySet<string>? excludedKeys = null)
    {
        ArgumentNullException.ThrowIfNull(resources);
        if (_book is null || resources.Count == 0)
        {
            return;
        }

        List<Resource> removable = resources
            .Where(r => r.Type != ResourceType.Opf)
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

        string title = Strings.Get("CheckpointOp_DeleteFiles");
        SaveOpenTabs?.Invoke();
        Book book = _book;
        BookOperationOutcome<(bool TocChanged, bool StylesChanged, string? Error)> run = await RunOperationAsync(title, title, () =>
        {
            try
            {
                // The stylesheets lose the rules and family names of the deleted fonts (as checked in the font window).
                bool styles = plan is not null && book.ApplyTexts(plan.BuildNewTexts(excludedKeys ?? new HashSet<string>()));

                // Entries of the table of contents pointing to the deleted files would be dead links.
                bool toc = TocFileEntries.RemoveEntriesForFiles(book, removable.OfType<HtmlResource>().Select(r => r.BookPath));
                book.GetFolderKeeper().BulkRemoveResources(removable);
                book.Modified = true;
                return (toc, styles, (string?)null);
            }
            catch (Exception ex) when (ex is InvalidOperationException or System.IO.IOException)
            {
                return (false, false, Strings.Format("BookBrowser_DeleteFailed", ex.Message));
            }
        }).ConfigureAwait(true);
        if (!run.Completed)
        {
            return;
        }

        (bool tocChanged, bool stylesChanged, string? error) = run.Result;
        if (error is not null)
        {
            Rewind(run.Checkpoint);
            _statusBar.ShowMessage(error, TimeSpan.FromSeconds(5), NotificationLevel.Warning);
        }

        _model?.Refresh();
        if (stylesChanged)
        {
            ReloadOpenTabs?.Invoke();
        }

        if (tocChanged)
        {
            TocChanged?.Invoke(this, EventArgs.Empty);
        }
    }

    /// <summary>
    /// Copies the given files from disk into the book (called by the view after the file picker
    /// closes). There is no duplicate replacement dialog — conflicting names automatically get a
    /// unique suffix, as with "Add Blank". Returns the new resources (the view offers <c>@font-face</c> rules for the
    /// fonts among them).
    /// </summary>
    public async Task<IReadOnlyList<Resource>> ApplyAddExistingFilesAsync(IReadOnlyList<string> filePaths)
    {
        IReadOnlyList<Resource> added = await AddFilesAsync(filePaths).ConfigureAwait(true);
        if (added.Count > 0)
        {
            OpenResourceRequested?.Invoke(this, added);
        }

        return added;
    }

    /// <inheritdoc/>
    public async Task<IReadOnlyList<Resource>> AddFilesAsync(IReadOnlyList<string> filePaths)
    {
        ArgumentNullException.ThrowIfNull(filePaths);
        if (_book is null || filePaths.Count == 0)
        {
            return Array.Empty<Resource>();
        }

        Book book = _book;
        string title = filePaths.Count == 1
            ? Strings.Format("CheckpointOp_AddFile", System.IO.Path.GetFileName(filePaths[0]))
            : Strings.Get("CheckpointOp_AddFiles");
        BookOperationOutcome<(IReadOnlyList<Resource> Added, string? Error)> run = await RunOperationAsync(title, title, () =>
        {
            try
            {
                return (book.AddExistingFiles(filePaths), (string?)null);
            }
            catch (Exception ex) when (ex is InvalidOperationException or ArgumentException or System.IO.IOException)
            {
                return (Array.Empty<Resource>(), Strings.Format("BookBrowser_AddFilesFailed", ex.Message));
            }
        }).ConfigureAwait(true);
        if (!run.Completed)
        {
            return Array.Empty<Resource>();
        }

        if (run.Result.Error is { } error)
        {
            Rewind(run.Checkpoint);
            _statusBar.ShowMessage(error, TimeSpan.FromSeconds(5), NotificationLevel.Warning);
            return Array.Empty<Resource>();
        }

        if (run.Result.Added.Count > 0)
        {
            _model?.Refresh();
        }
        else
        {
            Rewind(run.Checkpoint);
        }

        return run.Result.Added;
    }

    // =====================================================================
    //  Fonts (IFontManagementHost)
    // =====================================================================

    /// <inheritdoc/>
    public FontManagerReport? BuildFontReport()
    {
        if (_book is null)
        {
            return null;
        }

        SaveOpenTabs?.Invoke();
        return FontManagerReport.Build(_book);
    }

    /// <inheritdoc/>
    public FontFaceDialogViewModel? PrepareFontFaces(IReadOnlyList<FontResource> fonts)
    {
        ArgumentNullException.ThrowIfNull(fonts);
        if (_book is null || fonts.Count == 0)
        {
            return null;
        }

        SaveOpenTabs?.Invoke();
        bool isEpub3 = _book.IsEpub3;
        return new FontFaceDialogViewModel(
            BookFontIndex.Build(_book),
            fonts.Select(f => FontFaceAddition.Describe(f, isEpub3)).ToList(),
            _book.GetCssResources());
    }

    /// <inheritdoc/>
    public async Task<CssResource?> ApplyFontFacesAsync(FontFaceDialogResult result)
    {
        ArgumentNullException.ThrowIfNull(result);
        if (_book is null || (result.Requests.Count == 0 && result.BodyFamily is null))
        {
            return null;
        }

        string title = Strings.Get("CheckpointOp_AddFontFaces");
        SaveOpenTabs?.Invoke();
        Book book = _book;
        BookOperationOutcome<(CssResource? Stylesheet, IReadOnlyList<HtmlResource> NotLinked, string? Error)> run =
            await RunOperationAsync(title, title, () =>
            {
                try
                {
                    (CssResource stylesheet, IReadOnlyList<HtmlResource> notLinked) =
                        book.AddFontFaces(result.Stylesheet, result.Requests, result.BodyFamily, result.BodyGeneric);
                    return ((CssResource?)stylesheet, notLinked, (string?)null);
                }
                catch (Exception ex) when (ex is InvalidOperationException or ArgumentException or System.IO.IOException)
                {
                    return (null, Array.Empty<HtmlResource>(), Strings.Format("BookBrowser_FontFacesFailed", ex.Message));
                }
            }).ConfigureAwait(true);
        if (!run.Completed)
        {
            return null;
        }

        if (run.Result.Error is { } error)
        {
            Rewind(run.Checkpoint);
            _statusBar.ShowMessage(error, TimeSpan.FromSeconds(5), NotificationLevel.Warning);
            return null;
        }

        CssResource stylesheet = run.Result.Stylesheet!;
        IReadOnlyList<HtmlResource> notLinked = run.Result.NotLinked;
        _model?.Refresh();
        ReloadOpenTabs?.Invoke();
        if (notLinked.Count > 0)
        {
            _statusBar.ShowMessage(
                Strings.Format("BookBrowser_FontsCssNotLinked", stylesheet.Filename, string.Join(", ", notLinked.Select(h => h.Filename))),
                TimeSpan.FromSeconds(8),
                NotificationLevel.Warning);
        }
        else
        {
            _statusBar.ShowMessage(Strings.Format("BookBrowser_FontFacesAdded", result.Requests.Count, stylesheet.Filename), TimeSpan.FromSeconds(4));
        }

        return stylesheet;
    }

    /// <inheritdoc/>
    public FontRemovalViewModel? PrepareFontRemoval(IReadOnlyList<Resource> resources)
    {
        ArgumentNullException.ThrowIfNull(resources);
        List<string> fonts = resources.OfType<FontResource>().Select(f => f.BookPath).ToList();
        if (_book is null || fonts.Count == 0)
        {
            return null;
        }

        SaveOpenTabs?.Invoke();
        FontRemovalPlan plan = FontRemovalPlan.Create(BookFontIndex.Build(_book), fonts);
        return plan.IsEmpty
            ? null
            : new FontRemovalViewModel(plan, resources.Select(r => r.Filename).ToList(), NavigateTo);
    }

    /// <inheritdoc/>
    public async Task<bool> RenameFontFamilyAsync(string oldFamily, string newFamily)
    {
        ArgumentNullException.ThrowIfNull(oldFamily);
        ArgumentNullException.ThrowIfNull(newFamily);
        if (_book is null)
        {
            return false;
        }

        SaveOpenTabs?.Invoke();
        IReadOnlyDictionary<string, string> texts = FontFamilyRename.Rename(BookFontIndex.Build(_book), oldFamily, newFamily);
        if (texts.Count == 0)
        {
            return false;
        }

        Book book = _book;
        string title = Strings.Format("CheckpointOp_RenameFontFamily", oldFamily);
        BookOperationOutcome<bool> run = await RunOperationAsync(title, title, () => book.ApplyTexts(texts)).ConfigureAwait(true);
        if (!run.Completed)
        {
            return false;
        }

        if (!run.Result)
        {
            Rewind(run.Checkpoint);
            return false;
        }

        ReloadOpenTabs?.Invoke();
        _statusBar.ShowMessage(Strings.Format("BookBrowser_FontFamilyRenamed", oldFamily, newFamily.Trim(), texts.Count), TimeSpan.FromSeconds(4));
        return true;
    }

    /// <inheritdoc/>
    public void OpenResources(IReadOnlyList<Resource> resources)
    {
        ArgumentNullException.ThrowIfNull(resources);
        if (resources.Count > 0)
        {
            OpenResourceRequested?.Invoke(this, resources);
        }
    }

    [RelayCommand(CanExecute = nameof(HasBook))]
    private void AddFontFiles() => AddFontFilesRequested?.Invoke(this, EventArgs.Empty);

    [RelayCommand(CanExecute = nameof(HasBook))]
    private void ManageFonts() => ManageFontsRequested?.Invoke(this, EventArgs.Empty);

    /// <summary>
    /// Applies "Mass Rename" to the given entries (called by the view after the window closes with Rename): remembers the
    /// options, numbers the files (in the order of the book when asked and possible) and renames them, updating the links.
    /// Nothing changes (and a warning is shown) when the options are not valid or a name would collide with another file.
    /// </summary>
    public async Task ApplyMassRenameAsync(IReadOnlyList<OpfModelEntry> entries, MassRenameOptions options)
    {
        ArgumentNullException.ThrowIfNull(entries);
        if (_book is null || _model is null || entries.Count == 0)
        {
            return;
        }

        MassRenamePlan plan = PlanMassRename(entries, options);
        if (plan.NewFilenames is null)
        {
            _statusBar.ShowMessage(plan.Error ?? Strings.Get("BookBrowser_NamesFailed"), TimeSpan.FromSeconds(5), NotificationLevel.Warning);
            return;
        }

        _settings.MassRename = options;
        await ApplyRenameListAsync(plan.Resources, plan.NewFilenames).ConfigureAwait(true);
    }

    /// <summary>
    /// Applies "Bulk rename regex" — renames the resources to the already computed and validated
    /// <paramref name="newFilenames"/> (preview built in the dialog). Also used by
    /// <see cref="ApplyMassRenameAsync"/>.
    /// </summary>
    public async Task ApplyRenameListAsync(IReadOnlyList<Resource> resources, IReadOnlyList<string> newFilenames)
    {
        ArgumentNullException.ThrowIfNull(resources);
        ArgumentNullException.ThrowIfNull(newFilenames);
        if (_model is not { } model || resources.Count == 0)
        {
            return;
        }

        string title = Strings.Get("Operation_BulkRename");
        BookOperationOutcome<string?> run = await RunOperationAsync<string?>(title, title, () =>
        {
            bool ok = model.RenameResourceList(resources, newFilenames, out IReadOnlyList<string> notRenamed, out IReadOnlyList<string> wellFormedErrors);
            if (ok)
            {
                return null;
            }

            return wellFormedErrors.Count > 0
                ? Strings.Format("BookBrowser_RenameRejectedXml", string.Join(", ", wellFormedErrors))
                : Strings.Format("BookBrowser_RenameFailedDetail", string.Join(", ", notRenamed));
        }).ConfigureAwait(true);
        if (!run.Completed)
        {
            return;
        }

        if (run.Result is { } message)
        {
            Rewind(run.Checkpoint);
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
    public async Task ApplyMoveAsync(IReadOnlyList<OpfModelEntry> entries, string folderPath)
    {
        ArgumentNullException.ThrowIfNull(entries);
        ArgumentNullException.ThrowIfNull(folderPath);
        if (_model is not { } model || entries.Count == 0)
        {
            return;
        }

        // Moving a file is a rename of its path — hence the "Rename" / bulk rename checkpoint labels.
        string title = entries.Count == 1
            ? Strings.Format("CheckpointOp_Rename", entries[0].Resource.Filename)
            : Strings.Get("Operation_BulkRename");
        List<Resource> moving = entries.Select(e => e.Resource).ToList();
        BookOperationOutcome<string?> run = await RunOperationAsync<string?>(title, title, () =>
            model.MoveResourceList(moving, folderPath, out string? error) ? null : error ?? Strings.Get("BookBrowser_MoveFailed")).ConfigureAwait(true);
        if (!run.Completed)
        {
            return;
        }

        if (run.Result is { } message)
        {
            Rewind(run.Checkpoint);
            _statusBar.ShowMessage(message, TimeSpan.FromSeconds(5), NotificationLevel.Warning);
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
    public async Task ApplySemanticCodeAsync(OpfModelEntry entry, string code)
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

            string title = Strings.Get("CheckpointOp_SetSemantics");
            if (!(await RunOperationAsync(title, title, () =>
                {
                    new NavProcessor(nav).AddLandmarkCode(html, code, toggle: true);
                    return true;
                }).ConfigureAwait(true)).Completed)
            {
                return;
            }
        }
        else
        {
            string title = Strings.Get("CheckpointOp_SetSemantics");
            Book book = _book;
            if (!(await RunOperationAsync(title, title, () =>
                {
                    book.GetOpf().AddGuideSemanticCode(html, code, toggle: true);
                    return true;
                }).ConfigureAwait(true)).Completed)
            {
                return;
            }
        }

        _book.Modified = true;
        _model?.Refresh();
        RemoveSemanticsSelectedCommand.NotifyCanExecuteChanged();
    }

    /// <inheritdoc />
    public void Dispose()
    {
        _settings.SettingChanged -= OnSettingChanged;
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
        MassRenameRequested?.Invoke(this, _selection.Select(n => n.Entry!).ToArray());

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
    private Task AddBlankHtml() => AddBlank(() => _book!.CreateEmptyHtmlFile());

    [RelayCommand(CanExecute = nameof(HasBook))]
    private Task AddBlankCss() => AddBlank(() => _book!.CreateEmptyCssFile());

    [RelayCommand(CanExecute = nameof(HasBook))]
    private Task AddBlankSvg() => AddBlank(() => _book!.CreateEmptySvgFile());

    [RelayCommand(CanExecute = nameof(CanAddBlankJs))]
    private Task AddBlankJs() => AddBlank(() => _book!.CreateEmptyJsFile());

    private bool CanAddBlankJs => _book is not null && _book.IsEpub3;

    [RelayCommand(CanExecute = nameof(HasBook))]
    private void AddExistingFiles() => AddExistingFilesRequested?.Invoke(this, EventArgs.Empty);

    private async Task AddBlank(Func<Resource> create)
    {
        if (_book is null)
        {
            return;
        }

        string title = Strings.Get("CheckpointOp_AddNewFile");
        BookOperationOutcome<Resource> run = await RunOperationAsync(title, title, create).ConfigureAwait(true);
        if (!run.Completed)
        {
            return;
        }

        _model?.Refresh();
        OpenResourceRequested?.Invoke(this, new[] { run.Result! });
    }

    /// <summary>Whether exactly one (X)HTML, CSS or font file is selected — target of "Add Copy".</summary>
    private bool CanAddCopy =>
        _selection.Length == 1
        && _selection[0].Entry!.Resource is HtmlResource or CssResource or FontResource;

    /// <summary>
    /// Duplicates the selected (X)HTML/CSS file with its current content and opens the copy (a font file is copied
    /// next to the original under a unique name). The new file goes to the end of its group, like the other
    /// "Add Blank *" actions.
    /// </summary>
    [RelayCommand(CanExecute = nameof(CanAddCopy))]
    private async Task AddCopySelected()
    {
        if (_book is null || _selection.Length != 1)
        {
            return;
        }

        switch (_selection[0].Entry!.Resource)
        {
            case FontResource font:
                await AddBlank(() =>
                {
                    Resource copy = _book.GetFolderKeeper().AddContentFileToFolder(font.FullPath, folderPath: font.Folder);
                    _book.Modified = true;
                    return copy;
                });
                break;
            case HtmlResource source:
                await AddBlank(() =>
                {
                    HtmlResource copy = _book.CreateEmptyHtmlFile();
                    copy.SetText(source.GetText());
                    return copy;
                });
                break;
            case CssResource source:
                await AddBlank(() =>
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
    /// Selects all files in the group (folder) containing the current selection — or in the selected folder itself.
    /// </summary>
    [RelayCommand(CanExecute = nameof(CanSelectAllInFolder))]
    private void SelectAllInFolder()
    {
        if (SelectAllTarget() is not { } folder)
        {
            return;
        }

        folder.IsExpanded = true;
        TreeSelectionRequested?.Invoke(this, folder.Children.ToArray());
    }

    // The folder "Select All" works on: the one the selected files are in, or the selected folder itself.
    private BookBrowserNode? SelectAllTarget() => _selection.Length > 0
        ? Nodes.FirstOrDefault(n => n.IsFolder && n.Children.Contains(_selection[0]))
        : _selectedFolders.Length == 1 ? Nodes.FirstOrDefault(n => n.FolderKind == _selectedFolders[0]) : null;

    // Only when there is something to select: a folder with no file or with just one has nothing more to select.
    private bool CanSelectAllInFolder => SelectAllTarget() is { Children.Count: > 1 };

    [RelayCommand(CanExecute = nameof(CanMoveText))]
    private Task MoveTextUp() => MoveSelectedText(-1);

    [RelayCommand(CanExecute = nameof(CanMoveText))]
    private Task MoveTextDown() => MoveSelectedText(1);

    [RelayCommand(CanExecute = nameof(CanMoveTextToEdge))]
    private Task MoveTextToTop() => MoveSelectedTextToEdge(toTop: true);

    [RelayCommand(CanExecute = nameof(CanMoveTextToEdge))]
    private Task MoveTextToBottom() => MoveSelectedTextToEdge(toTop: false);

    private async Task MoveSelectedTextToEdge(bool toTop)
    {
        OpfModel? model = _model;
        List<OpfModelEntry> entries = _selection.Select(n => n.Entry!).ToList();
        string[] selectedIdentifiers = entries.Select(entry => entry.Identifier).ToArray();
        string title = Strings.Get("CheckpointOp_ReorderText");
        BookOperationOutcome<bool> run = await RunOperationAsync(title, title, () =>
        {
            return model?.MoveTextToEdge(entries, toTop) ?? false;
        }, delayedProgressWindow: true).ConfigureAwait(true);
        if (run.Completed && run.Result)
        {
            ReorderTextNodes(model, selectedIdentifiers);
        }
    }

    [RelayCommand(CanExecute = nameof(CanSortText))]
    private async Task SortText()
    {
        OpfModel? model = _model;
        List<OpfModelEntry> entries = _selection.Select(n => n.Entry!).Where(IsText).ToList();
        string[] selectedIdentifiers = entries.Select(entry => entry.Identifier).ToArray();
        string title = Strings.Get("CheckpointOp_ReorderText");
        BookOperationOutcome<bool> run = await RunOperationAsync(title, title, () =>
        {
            return model?.SortTextByFilename(entries) ?? false;
        }, delayedProgressWindow: true).ConfigureAwait(true);
        if (run.Completed && run.Result)
        {
            ReorderTextNodes(model, selectedIdentifiers);
        }
    }

    private void ReorderTextNodes(OpfModel? model, IReadOnlyList<string> selectedIdentifiers)
    {
        if (model?.GetFolder(OpfModelGroupKind.Text) is not { } text)
        {
            return;
        }

        BookBrowserNode? folderNode = Nodes.FirstOrDefault(node => node.FolderKind == OpfModelGroupKind.Text);
        if (folderNode is null)
        {
            Rebuild();
            folderNode = Nodes.FirstOrDefault(node => node.FolderKind == OpfModelGroupKind.Text);
        }

        if (folderNode is null)
        {
            return;
        }

        Dictionary<string, BookBrowserNode> nodesByIdentifier = folderNode.Children
            .Where(node => node.Entry is not null)
            .ToDictionary(node => node.Entry!.Identifier, StringComparer.Ordinal);
        if (nodesByIdentifier.Count != text.Entries.Count
            || text.Entries.Any(entry => !nodesByIdentifier.ContainsKey(entry.Identifier)))
        {
            Rebuild();
            folderNode = Nodes.FirstOrDefault(node => node.FolderKind == OpfModelGroupKind.Text);
            if (folderNode is null)
            {
                return;
            }

            nodesByIdentifier = folderNode.Children
                .Where(node => node.Entry is not null)
                .ToDictionary(node => node.Entry!.Identifier, StringComparer.Ordinal);
        }

        for (int i = 0; i < text.Entries.Count; i++)
        {
            BookBrowserNode node = nodesByIdentifier[text.Entries[i].Identifier];
            node.NotifyReadingOrderChanged();

            int currentIndex = folderNode.Children.IndexOf(node);
            if (currentIndex != i)
            {
                folderNode.Children.Move(currentIndex, i);
            }
        }

        BookBrowserNode[] selectedNodes = selectedIdentifiers
            .Where(nodesByIdentifier.ContainsKey)
            .Select(identifier => nodesByIdentifier[identifier])
            .ToArray();
        UpdateSelection(selectedNodes);
        TreeSelectionRequested?.Invoke(this, selectedNodes);
    }

    [RelayCommand(CanExecute = nameof(CanMergeText))]
    private async Task MergeSelected()
    {
        if (_book is null)
        {
            return;
        }

        if (MergeError() is { } error)
        {
            MergeErrorRequested?.Invoke(this, error);
            return;
        }

        List<HtmlResource> resources = _selection.Select(n => (HtmlResource)n.Entry!.Resource).ToList();
        Book book = _book;
        string title = Strings.Format("CheckpointOp_MergeFiles", resources[0].Filename);
        BookOperationOutcome<HtmlResource?> run = await RunOperationAsync(title, title, () => book.MergeResources(resources)).ConfigureAwait(true);
        if (!run.Completed)
        {
            return;
        }

        if (run.Result is not null)
        {
            Rewind(run.Checkpoint);
            _statusBar.ShowMessage(Strings.Get("BookBrowser_CannotMergeNav"), TimeSpan.FromSeconds(4), NotificationLevel.Warning);
            return;
        }

        _model?.Refresh();
        ReloadOpenTabs?.Invoke();
    }

    [RelayCommand(CanExecute = nameof(CanSplitText))]
    private async Task SplitSelected()
    {
        if (_book is null)
        {
            return;
        }

        Book book = _book;
        List<HtmlResource> files = _selection.Where(n => IsText(n.Entry!)).Select(n => (HtmlResource)n.Entry!.Resource).ToList();
        string title = Strings.Format("CheckpointOp_Split", string.Join(", ", files.Select(f => f.Filename)));
        BookOperationOutcome<int> run = await RunOperationAsync(title, title, () =>
        {
            int created = 0;
            foreach (HtmlResource file in files)
            {
                created += book.SplitOnSectionMarkers(file).Count;
            }

            return created;
        }).ConfigureAwait(true);
        if (!run.Completed)
        {
            return;
        }

        if (run.Result > 0)
        {
            _model?.Refresh();
            ReloadOpenTabs?.Invoke();
        }
        else
        {
            Rewind(run.Checkpoint);
            _statusBar.ShowMessage(Strings.Get("BookBrowser_NoSplitMarkers"), TimeSpan.FromSeconds(4));
        }
    }

    [RelayCommand(CanExecute = nameof(CanAddSemantics))]
    private void AddSemanticsSelected() => AddSemanticsRequested?.Invoke(this, _selection[0].Entry!);

    [RelayCommand(CanExecute = nameof(CanRemoveSemantics))]
    private async Task RemoveSemanticsSelectedAsync()
    {
        if (_book is not { } book
            || _selection.Length != 1
            || _selection[0].Entry?.Resource is not HtmlResource html
            || GetWholeFileSemanticCode(book, html).Length == 0)
        {
            return;
        }

        // EPUB 3 keeps the semantics as landmarks of the nav, EPUB 2 as guide references of the OPF. Only the entries
        // for the whole file go; those pointing at a fragment of it (a section) are a different target and stay.
        HtmlResource? nav = book.IsEpub3 ? book.GetNavResource() : null;
        string title = Strings.Get("CheckpointOp_SetSemantics");
        BookOperationOutcome<bool> run = await RunOperationAsync(
            title,
            title,
            () => nav is not null
                ? new NavProcessor(nav).RemoveWholeFileLandmarksForResource(html)
                : book.GetOpf().RemoveWholeFileGuideReferencesForResource(html)).ConfigureAwait(true);
        if (!run.Completed || !run.Result)
        {
            return;
        }

        book.Modified = true;
        _model?.Refresh();
        RemoveSemanticsSelectedCommand.NotifyCanExecuteChanged();
    }

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

    /// <summary>
    /// Warns about the selected (X)HTML files without a DOCTYPE before "Link Stylesheets" /
    /// "Link Javascripts" rewrites them. Returns <c>false</c> when the user cancelled.
    /// </summary>
    public Task<bool> ConfirmMissingDoctypeAsync(IReadOnlyList<HtmlResource> resources, string operationName) =>
        _book is null
            ? Task.FromResult(true)
            : _doctypeGuard.ConfirmAsync(operationName, _book.FindHtmlMissingDoctype(resources));

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
    public async Task ApplyLinkStylesheetsAsync(
        IReadOnlyList<HtmlResource> resources, IReadOnlyList<string> orderedBookPaths)
    {
        ArgumentNullException.ThrowIfNull(resources);
        ArgumentNullException.ThrowIfNull(orderedBookPaths);
        if (_book is null)
        {
            return;
        }

        Book book = _book;
        string title = Strings.Get("CheckpointOp_LinkStylesheets");
        BookOperationOutcome<FileBatchResult> run = await RunOperationAsync(
            title, title, () => book.LinkStylesheetsToResources(resources, orderedBookPaths)).ConfigureAwait(true);
        if (!run.Completed)
        {
            return;
        }

        if (run.Result!.NothingProcessed)
        {
            Rewind(run.Checkpoint);
        }

        ReportLinkResult(run.Result, Strings.Get("BookBrowserView_LinkStylesheetsTitle"));
    }

    /// <summary>
    /// Applies the choice from the "Link Javascripts" dialog: relinks scripts in the selected files.
    /// </summary>
    public async Task ApplyLinkJavascriptsAsync(
        IReadOnlyList<HtmlResource> resources, IReadOnlyList<string> orderedBookPaths)
    {
        ArgumentNullException.ThrowIfNull(resources);
        ArgumentNullException.ThrowIfNull(orderedBookPaths);
        if (_book is null)
        {
            return;
        }

        // No checkpoint (it never had one): a book operation only for the window and for the other operations to wait.
        Book book = _book;
        BookOperationOutcome<FileBatchResult> run = await RunOperationAsync(
            Strings.Get("BookBrowserView_LinkJavascriptsTitle"), null, () => book.LinkJavascriptsToResources(resources, orderedBookPaths))
            .ConfigureAwait(true);
        if (run.Completed)
        {
            ReportLinkResult(run.Result!, Strings.Get("BookBrowserView_LinkJavascriptsTitle"));
        }
    }

    /// <summary>
    /// Raised when "Link Stylesheets" / "Link Javascripts" skipped some files (not well-formed) — the main window
    /// shows the report window with the skipped files.
    /// </summary>
    public event EventHandler<SkippedFilesReport>? SkippedFilesReported;

    private void ReportLinkResult(FileBatchResult result, string operationName)
    {
        if (!result.NothingProcessed)
        {
            _model?.Refresh();
        }

        // Nothing is announced on success (as before).
        if (!result.HasSkipped)
        {
            return;
        }

        if (FileBatchReporter.ShowStatus(_statusBar, operationName, result, string.Empty) is { } report)
        {
            SkippedFilesReported?.Invoke(this, report);
        }
    }

    private List<HtmlResource> SelectedHtmlResources() =>
        _selection.Select(n => (HtmlResource)n.Entry!.Resource).ToList();

    [RelayCommand(CanExecute = nameof(CanSetCoverImage))]
    private async Task CoverImageSelected()
    {
        if (_book is not { } book || _selection[0].Entry!.Resource is not ImageResource image)
        {
            return;
        }

        string title = Strings.Get("CheckpointOp_SetSemantics");
        if (!(await RunOperationAsync(title, title, () =>
            {
                book.GetOpf().SetResourceAsCoverImage(image);
                book.Modified = true;
                return true;
            }).ConfigureAwait(true)).Completed)
        {
            return;
        }

        _model?.Refresh();
    }

    [RelayCommand(CanExecute = nameof(CanMarkAsNav))]
    private async Task MarkAsNavSelected()
    {
        if (_book is not { } book || _selection[0].Entry!.Resource is not HtmlResource html)
        {
            return;
        }

        string title = Strings.Get("CheckpointOp_SetSemantics");
        BookOperationOutcome<bool> run = await RunOperationAsync(title, title, () => book.SetNavDocument(html)).ConfigureAwait(true);
        if (!run.Completed)
        {
            return;
        }

        if (run.Result)
        {
            _model?.Refresh();
        }
        else
        {
            Rewind(run.Checkpoint);
        }
    }

    // EPUB 3 only: one (X)HTML file that is not the navigation document yet.
    private bool CanMarkAsNav =>
        _book is { IsEpub3: true }
        && _selection.Length == 1
        && _selection[0].Entry is { Resource: HtmlResource, IsNav: false };

    private bool HasFileSelection => _selection.Length > 0;

    private bool HasSingleSelection => _selection.Length == 1;

    private bool CanAddSemantics => _selection.Length == 1 && IsText(_selection[0].Entry!);

    private bool CanRemoveSemantics =>
        _selection.Length == 1
        && _selection[0].Entry?.Resource is HtmlResource html
        && _book is { } book
        && GetWholeFileSemanticCode(book, html).Length > 0;

    private static string GetWholeFileSemanticCode(Book book, HtmlResource html)
    {
        if (book.IsEpub3)
        {
            HtmlResource? nav = book.GetNavResource();
            return nav is not null ? new NavProcessor(nav).GetLandmarkCodeForResource(html) : string.Empty;
        }

        return book.GetOpf().GetGuideSemanticCodeForResource(html);
    }

    private bool HasHtmlSelection =>
        _selection.Length > 0 && _selection.All(n => IsText(n.Entry!));

    private bool CanLinkStylesheets =>
        _book is not null && HasHtmlSelection && _book.GetCssResources().Count > 0;

    private bool CanLinkJavascripts =>
        _book is not null && HasHtmlSelection && _book.GetJavascriptResources().Count > 0;

    private bool CanSetCoverImage =>
        _selection.Length == 1 && _selection[0].Entry!.ResourceType == ResourceType.Image;

    private bool CanMoveText => _selection.Length == 1 && IsText(_selection[0].Entry!);

    private bool CanMoveTextToEdge => _selection.Length >= 1 && _selection.All(n => IsText(n.Entry!));

    private bool CanSortText => _selection.Length >= 2 && _selection.All(n => IsText(n.Entry!));

    // Any two or more files: a selection that cannot be merged gets an error window (MergeError) instead of a
    // silently disabled command.
    private bool CanMergeText => _selection.Length >= 2;

    private bool CanSplitText => _selection.Length >= 1 && _selection.All(n => IsText(n.Entry!));

    private static bool IsText(OpfModelEntry entry) => entry.ResourceType == ResourceType.Html;

    // Why the selected files cannot be merged (only HTML files other than the navigation document can), or null.
    private string? MergeError()
    {
        List<OpfModelEntry> entries = _selection.Select(n => n.Entry!).ToList();
        List<OpfModelEntry> others = entries.Where(e => !IsText(e)).ToList();
        if (others.Count > 0)
        {
            List<OpfModelEntry> html = entries.Where(IsText).ToList();
            return html.Count > 0
                ? Strings.Format("BookBrowser_MergeMixedTypes", FileList(others), FileList(html))
                : Strings.Format("BookBrowser_MergeOnlyHtml", FileList(others));
        }

        return entries.FirstOrDefault(e => e.IsNav) is { } nav
            ? Strings.Format("BookBrowser_MergeNav", System.IO.Path.GetFileName(nav.BookPath))
            : null;
    }

    private static string FileList(IEnumerable<OpfModelEntry> entries) =>
        string.Join("\n", entries.Select(e => "  • " + System.IO.Path.GetFileName(e.BookPath)));

    private async Task MoveSelectedText(int delta)
    {
        if (_selection.Length == 1 && IsText(_selection[0].Entry!))
        {
            OpfModel? model = _model;
            OpfModelEntry entry = _selection[0].Entry!;
            string[] selectedIdentifiers = { entry.Identifier };
            string title = Strings.Get("CheckpointOp_ReorderText");
            BookOperationOutcome<bool> run = await RunOperationAsync(title, title, () =>
            {
                return model?.MoveText(entry, delta) ?? false;
            }, delayedProgressWindow: true).ConfigureAwait(true);
            if (run.Completed && run.Result)
            {
                ReorderTextNodes(model, selectedIdentifiers);
            }
        }
    }

    /// <summary>Moves one selected Text file to a zero-based position in the Text folder.</summary>
    internal async Task MoveTextToIndexAsync(BookBrowserNode node, int targetIndex)
    {
        ArgumentNullException.ThrowIfNull(node);
        if (_model is not { } model
            || _selection.Length != 1
            || _selectedFolders.Length != 0
            || !ReferenceEquals(_selection[0], node)
            || node.Entry is not { } entry
            || !IsText(entry))
        {
            return;
        }

        string[] selectedIdentifiers = { entry.Identifier };
        string title = Strings.Get("CheckpointOp_ReorderText");
        BookOperationOutcome<bool> run = await RunOperationAsync(
            title, title, () => model.MoveTextToIndex(entry, targetIndex), delayedProgressWindow: true).ConfigureAwait(true);
        if (run.Completed && run.Result)
        {
            ReorderTextNodes(model, selectedIdentifiers);
        }
    }

    // Runs `work`, which changes the book, as a book operation behind the checkpoint `checkpointName` (see IBookOperationHost). A work that
    // fails catches its exceptions and returns them in its result, so that the caller can rewind the checkpoint.
    private async Task<BookOperationOutcome<T>> RunOperationAsync<T>(
        string title, string? checkpointName, Func<T> work, bool delayedProgressWindow = false) =>
        Operations is null
            ? new BookOperationOutcome<T>(true, work(), false)
            : await Operations.RunAsync(title, checkpointName, work, delayedProgressWindow).ConfigureAwait(true);

    private void Rewind(bool checkpointCreated)
    {
        if (checkpointCreated)
        {
            RewindCheckpoint?.Invoke();
        }
    }

    private bool _suspendRebuilds;

    /// <summary>
    /// Whether the tree ignores the changes of the book: set while the book is edited off the UI thread (the changes are raised
    /// there, and the tree is bound to the UI); the caller refreshes the tree when it is done (<see cref="Refresh"/>).
    /// </summary>
    public bool SuspendRebuilds
    {
        get => _suspendRebuilds;
        set
        {
            if (_suspendRebuilds != value)
            {
                DebugLog.Write("BookBrowser", value ? "SuspendRebuilds: on" : "SuspendRebuilds: off");
            }

            _suspendRebuilds = value;
        }
    }

    private void OnModelChanged(object? sender, EventArgs e)
    {
        if (!SuspendRebuilds)
        {
            Rebuild();
        }
        else
        {
            DebugLog.Write("BookBrowser", "model changed while suspended, rebuild deferred");
        }
    }

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
        long start = System.Diagnostics.Stopwatch.GetTimestamp();
        foreach (BookBrowserNode node in Nodes)
        {
            node.PropertyChanged -= OnFolderNodePropertyChanged;
        }

        // A rebuild replaces the nodes — an ongoing rename no longer has a target.
        EditingNode = null;
        Nodes.Clear();
        if (_model is null)
        {
            DebugLog.Write("BookBrowser", $"Rebuild: no book ({DebugLog.Elapsed(start)})");
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

        DebugLog.Write(
            "BookBrowser", $"Rebuild: {_model.Folders.Sum(f => f.Entries.Count) + _model.TopLevelFiles.Count} files ({DebugLog.Elapsed(start)})");
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

    /// <summary>
    /// The icon key of a folder that has a calibre icon (Text, Styles, Images, Fonts, Misc, as in calibre's file list);
    /// <c>null</c> for the folders without one (Audio, Video) and for files.
    /// </summary>
    public string? FolderIconKey => FolderKind switch
    {
        OpfModelGroupKind.Text => "keyboard-prefs",
        OpfModelGroupKind.Styles => "lookfeel",
        OpfModelGroupKind.Images => "view-image",
        OpfModelGroupKind.Fonts => "font",
        OpfModelGroupKind.Misc => "dir",
        _ => null,
    };

    /// <summary>Whether the node is a folder with a calibre icon (<see cref="FolderIconKey"/>).</summary>
    public bool HasFolderIcon => FolderIconKey is not null;

    /// <summary>Whether the node is a folder drawn with the plain folder icon of the theme (no calibre icon).</summary>
    public bool IsPlainFolder => IsFolder && !HasFolderIcon;

    /// <summary>Whether the folder is expanded in the tree (two-way bound to the tree's expander column).</summary>
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

    /// <summary>The "#" column: the 1-based position in the reading order (spine), or <c>null</c> outside it.</summary>
    public int? ReadingOrderNumber => Entry is { ReadingOrder: >= 0 } entry ? entry.ReadingOrder + 1 : null;

    internal void NotifyReadingOrderChanged() => OnPropertyChanged(nameof(ReadingOrderNumber));

    /// <summary>The "Semantics" column: the landmark / guide types of the file.</summary>
    public string SemanticsText => Entry is null ? string.Empty : string.Join(", ", Entry.SemanticTypes);

    /// <summary>The "Properties" column: the manifest properties of the file (nav, cover-image, …).</summary>
    public string PropertiesText => Entry?.ManifestProperties ?? string.Empty;

    /// <summary>Creates a group folder node.</summary>
    public static BookBrowserNode Folder(OpfModelGroupKind kind, string name, string toolTip) =>
        new(name, kind, toolTip, entry: null);

    /// <summary>Creates a file node.</summary>
    public static BookBrowserNode File(OpfModelEntry entry) =>
        new(entry.DisplayName, folderKind: null, folderToolTip: null, entry);
}

/// <summary>
/// What "Mass Rename" would do (<see cref="BookBrowserViewModel.PlanMassRename"/>): the resources in the order they are
/// numbered and their new file names; <see cref="NewFilenames"/> is <c>null</c> when it cannot be done, with the reason in
/// <see cref="Error"/> (no message while there is no book).
/// </summary>
/// <param name="Resources">The resources, in the order they are numbered.</param>
/// <param name="NewFilenames">Their new file names (same order), or <c>null</c>.</param>
/// <param name="Error">Why there are no names.</param>
public sealed record MassRenamePlan(IReadOnlyList<Resource> Resources, IReadOnlyList<string>? NewFilenames, string? Error);
