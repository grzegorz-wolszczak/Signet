using System.Collections.Generic;
using System.ComponentModel;
using System.Globalization;
using System.Linq;
using System;
using Dock.Model.Controls;
using Dock.Model.Core.Events;
using Signet.App.Docking;
using Signet.App.Infrastructure;
using Signet.App.Resources;
using Signet.App.Services;
using Signet.App.ViewModels.Tabs;
using Signet.Core.BookManipulation;
using Signet.Core.MainUI;
using Signet.Core.Misc;
using Signet.Core.Parsers;
using Signet.Core.Resources;
using Signet.Core.Spellcheck;

namespace Signet.App.Tabs;

/// <summary>
/// App layer of the tab manager: connects the view-less <see cref="TabManagerModel"/>
/// to the <c>DocumentDock</c> area (Dock.Avalonia). Creates the tab views, inserts/removes them
/// in the dock and keeps the active tab consistent in both directions (model ↔ dock).
/// </summary>
public sealed class TabManager : IDisposable
{
    private const string CursorOffsetsGroup = "session_cursor_offsets";
    private const double CodeZoomMin = 0.5;
    private const double CodeZoomMax = 4.0;

    private readonly TabManagerModel _model = new();
    private readonly MainDockFactory _dockFactory;
    private readonly IStatusBarService _statusBar;
    private readonly SettingsStore _settings;
    private readonly SpellChecker _spellChecker;
    private readonly Dictionary<OpenTab, ContentTabViewModel> _views = new();

    private Book? _book;
    private ContentTabViewModel? _current;
    private bool _bridging;
    private bool _syncingZoom;
    private bool _disposed;

    /// <summary>Creates the tab manager on top of the dock factory, status bar, settings and spell-check engine.</summary>
    public TabManager(MainDockFactory dockFactory, IStatusBarService statusBar, SettingsStore settings, SpellChecker spellChecker)
    {
        _dockFactory = dockFactory ?? throw new ArgumentNullException(nameof(dockFactory));
        _statusBar = statusBar ?? throw new ArgumentNullException(nameof(statusBar));
        _settings = settings ?? throw new ArgumentNullException(nameof(settings));
        _spellChecker = spellChecker ?? throw new ArgumentNullException(nameof(spellChecker));

        _model.TabOpened += OnModelTabOpened;
        _model.TabClosed += OnModelTabClosed;
        _model.TabActivated += OnModelTabActivated;
        _model.TabCaptionChanged += OnModelTabCaptionChanged;

        _dockFactory.ActiveDockableChanged += OnDockActiveDockableChanged;
        _dockFactory.DockableClosed += OnDockableClosed;
    }

    /// <summary>
    /// Main window services for the Code View context menu — passed to every new code
    /// tab (set by <c>MainWindowViewModel</c>).
    /// </summary>
    public ICodeTabHost? CodeTabHost { get; set; }

    /// <summary>
    /// Raised when the "unsaved changes" state of any open tab changes (window title,
    /// "Save" icon).
    /// </summary>
    public event EventHandler? AnyTabModifiedChanged;

    /// <summary>Raised after the active tab changes (for the status bar, Undo/Redo actions, etc.).</summary>
    public event EventHandler? ActiveTabChanged;

    /// <summary>The view-less tab model (for higher layers and tests).</summary>
    public TabManagerModel Model => _model;

    /// <summary>View model of the active tab, or <c>null</c>.</summary>
    public ContentTabViewModel? ActiveTab =>
        _model.ActiveTab is { } tab ? _views.GetValueOrDefault(tab) : null;

    /// <summary>Number of open tabs.</summary>
    public int Count => _model.Count;

    /// <summary>View models of all open tabs.</summary>
    public IReadOnlyCollection<ContentTabViewModel> OpenTabViews => _views.Values;

    /// <summary>View models of the tabs in their display order (for the "Tabbed …" modes in Find &amp; Replace).</summary>
    public IReadOnlyList<ContentTabViewModel> OrderedTabViews
    {
        get
        {
            var result = new List<ContentTabViewModel>(_model.Tabs.Count);
            foreach (OpenTab tab in _model.Tabs)
            {
                if (_views.TryGetValue(tab, out ContentTabViewModel? view))
                {
                    result.Add(view);
                }
            }

            return result;
        }
    }

    /// <summary>Whether any of the open tabs has unsaved changes.</summary>
    public bool AnyTabModified
    {
        get
        {
            foreach (ContentTabViewModel view in _views.Values)
            {
                if (view.IsModified)
                {
                    return true;
                }
            }

            return false;
        }
    }

    /// <summary>
    /// Flushes the buffered content of all open tabs into the resources.
    /// When any tab had unsaved changes, the book is marked as modified —
    /// otherwise after the flush (e.g. before a checkpoint) the changes would be visible neither in the
    /// tab nor in the book, and closing the program would not ask about saving.
    /// </summary>
    public void SaveAllTabs()
    {
        bool anyModified = false;
        foreach (ContentTabViewModel view in _views.Values)
        {
            anyModified |= view.IsModified;
            view.Save();
        }

        if (anyModified && _book is not null)
        {
            _book.Modified = true;
        }
    }

    /// <summary>Sets (or clears) the publication: closes all tabs of the previous book.</summary>
    public void SetBook(Book? book)
    {
        _model.CloseAllTabs();
        _book = book;
    }

    /// <summary>Opens a resource in a tab (or activates the existing one).</summary>
    public void OpenResource(Resource resource)
    {
        ArgumentNullException.ThrowIfNull(resource);
        if (ContentTabKindMap.ForResource(resource) == ContentTabKind.Unsupported)
        {
            _statusBar.ShowMessage(
                Strings.Format("Tabs_CannotOpenFile", resource.Filename), TimeSpan.FromSeconds(4), NotificationLevel.Warning);
            return;
        }

        _model.OpenResource(resource);
    }

    /// <summary>Opens several resources one by one (the last one becomes active).</summary>
    public void OpenResources(IEnumerable<Resource> resources)
    {
        ArgumentNullException.ThrowIfNull(resources);
        foreach (Resource resource in resources)
        {
            OpenResource(resource);
        }
    }

    /// <summary>Activates the next tab (wraps around).</summary>
    public void NextTab() => _model.NextTab();

    /// <summary>Activates the previous tab (wraps around).</summary>
    public void PreviousTab() => _model.PreviousTab();

    /// <summary>Closes the active tab.</summary>
    public void CloseActiveTab() => _model.CloseActiveTab();

    /// <summary>Closes all tabs except the active one.</summary>
    public void CloseOtherTabs() => _model.CloseOtherTabs();

    /// <summary>Closes all tabs.</summary>
    public void CloseAllTabs() => _model.CloseAllTabs();

    /// <summary>Returns to the previously active tab (activation history).</summary>
    public void Back() => _model.Back();

    /// <summary>
    /// Saves the session of open tabs (including the caret position of every Code View tab)
    /// to the settings (without persisting to disk).
    /// </summary>
    public void CaptureSession(SettingsStore settings)
    {
        ArgumentNullException.ThrowIfNull(settings);
        TabSnapshot snapshot = CaptureSnapshot();
        settings.SessionOpenTabs = snapshot.Session.OpenBookPaths;
        settings.SessionActiveTab = snapshot.Session.ActiveBookPath ?? string.Empty;
        settings.SetStringMap(
            CursorOffsetsGroup,
            snapshot.CaretOffsets.ToDictionary(
                pair => pair.Key,
                pair => pair.Value.ToString(CultureInfo.InvariantCulture),
                StringComparer.Ordinal));
    }

    /// <summary>
    /// Restores the tab session (including the caret position) from the settings for the current book.
    /// Unknown book paths are skipped.
    /// </summary>
    public void RestoreSession(SettingsStore settings)
    {
        ArgumentNullException.ThrowIfNull(settings);

        string active = settings.SessionActiveTab;
        Dictionary<string, int> offsets = new(StringComparer.Ordinal);
        foreach ((string bookPath, string raw) in settings.GetStringMap(CursorOffsetsGroup))
        {
            if (int.TryParse(raw, NumberStyles.Integer, CultureInfo.InvariantCulture, out int offset))
            {
                offsets[bookPath] = offset;
            }
        }

        RestoreSnapshot(new TabSnapshot(
            new TabSession(settings.SessionOpenTabs, string.IsNullOrEmpty(active) ? null : active),
            offsets));
    }

    /// <summary>
    /// In-memory snapshot of the open tabs (order, active one, caret positions of Code View tabs)
    /// — to be restored after the book is replaced with a checkpoint state.
    /// </summary>
    public TabSnapshot CaptureSnapshot()
    {
        Dictionary<string, int> offsets = new(StringComparer.Ordinal);
        foreach (OpenTab tab in _model.Tabs)
        {
            if (_views.TryGetValue(tab, out ContentTabViewModel? view) && view is CodeTabViewModel code)
            {
                offsets[tab.Resource.BookPath] = code.CaretOffset;
            }
        }

        return new TabSnapshot(_model.CaptureSession(), offsets);
    }

    /// <summary>
    /// Opens the tabs from <paramref name="snapshot"/> in the current book and sets the caret in them.
    /// Book paths that the book does not have are skipped.
    /// </summary>
    public void RestoreSnapshot(TabSnapshot snapshot)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        if (_book is null)
        {
            return;
        }

        FolderKeeper folderKeeper = _book.GetFolderKeeper();
        _model.RestoreSession(snapshot.Session, folderKeeper.GetResourceByBookPathNoThrow);

        foreach (OpenTab tab in _model.Tabs)
        {
            if (snapshot.CaretOffsets.TryGetValue(tab.Resource.BookPath, out int offset)
                && _views.TryGetValue(tab, out ContentTabViewModel? view)
                && view is CodeTabViewModel code)
            {
                code.GoToOffset(offset);
            }
        }
    }

    /// <inheritdoc />
    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _model.TabOpened -= OnModelTabOpened;
        _model.TabClosed -= OnModelTabClosed;
        _model.TabActivated -= OnModelTabActivated;
        _model.TabCaptionChanged -= OnModelTabCaptionChanged;
        _dockFactory.ActiveDockableChanged -= OnDockActiveDockableChanged;
        _dockFactory.DockableClosed -= OnDockableClosed;

        _model.Dispose();
        _views.Clear();
        _disposed = true;
    }

    private IDocumentDock? DocumentDock => _dockFactory.DocumentDock;

    /// <summary>
    /// Creates the proper tab view model for a resource. Code View is used for
    /// (X)HTML / XML / NCX / OPF / plain text / JS / JSON / CSS / SVG (Code View in XML mode);
    /// Image, Font and Audio / Video / PDF get their own tabs. A placeholder remains only for
    /// unrecognized resources.
    /// </summary>
    private ContentTabViewModel CreateTab(OpenTab tab)
    {
        bool codeView = tab.Resource is TextResource && tab.Kind is
            ContentTabKind.Flow or
            ContentTabKind.Css or
            ContentTabKind.MiscText or
            ContentTabKind.Svg or
            ContentTabKind.Opf or
            ContentTabKind.Ncx or
            ContentTabKind.Xml or
            ContentTabKind.Text;

        if (codeView)
        {
            var codeTab = new CodeTabViewModel(tab, _statusBar, _settings, _spellChecker)
            {
                BookPathExists = bookPath => _book?.GetFolderKeeper().GetResourceByBookPathNoThrow(bookPath) is not null,
                BookFiles = () => _book is { } book
                    ? book.GetAllResources().Select(r => new LinkCompletionFile(r.BookPath, r.MediaType)).ToList()
                    : Array.Empty<LinkCompletionFile>(),
                DocumentTextOf = DocumentTextOf,
                Host = CodeTabHost,
            };
            codeTab.CssClassJumpRequested += className => OnCssClassJumpRequested(codeTab, className);
            codeTab.LinkJumpRequested += reference => OnLinkJumpRequested(codeTab, reference);
            codeTab.ZoomFactor = Math.Clamp(_settings.ZoomText, CodeZoomMin, CodeZoomMax);
            codeTab.PropertyChanged += OnCodeTabPropertyChanged;
            return codeTab;
        }

        return tab.Kind switch
        {
            ContentTabKind.Image => new ImageTabViewModel(tab, _statusBar, _book),
            ContentTabKind.Font => new FontTabViewModel(tab, _book),
            ContentTabKind.AudioVideo or ContentTabKind.Pdf => new MediaTabViewModel(tab),
            _ => new PlaceholderContentTabViewModel(tab),
        };
    }

    /// <summary>
    /// Handles Ctrl+click on a class name in Code View: resolves the first matching CSS rule
    /// (linked stylesheets, then inline styles) and opens / scrolls to the file containing it.
    /// No effect when no book is loaded, the source tab is not editing an (X)HTML file, or no
    /// rule matches.
    /// </summary>
    private void OnCssClassJumpRequested(CodeTabViewModel sourceTab, string className)
    {
        if (_book is not { } book || sourceTab.Resource is not HtmlResource html)
        {
            return;
        }

        FolderKeeper folderKeeper = book.GetFolderKeeper();
        CssClassDefinition? definition = CssClassDefinitionLocator.Find(
            html,
            className,
            cssBookPath => folderKeeper.GetResourceByBookPathNoThrow(cssBookPath) is CssResource css
                ? new CssInfo(css.GetText())
                : null);

        if (definition is not { } target)
        {
            _statusBar.ShowMessage(
                Strings.Format("Tabs_NoCssRuleForClass", className), TimeSpan.FromSeconds(4));
            return;
        }

        OpenResourceAtOffset(target.BookPath, target.Offset);
    }

    // The Code View zoom is shared by all code tabs and persistent (stored in the settings
    // as zoom_text). A change in one tab propagates to the other open ones, and newly
    // opened tabs start from the saved value.
    private void OnCodeTabPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (_syncingZoom || e.PropertyName != nameof(ContentTabViewModel.ZoomFactor) || sender is not CodeTabViewModel source)
        {
            return;
        }

        _syncingZoom = true;
        try
        {
            double factor = source.ZoomFactor;
            _settings.ZoomText = (float)factor;
            foreach (ContentTabViewModel view in _views.Values)
            {
                if (view is CodeTabViewModel code && !ReferenceEquals(code, source))
                {
                    code.ZoomFactor = factor;
                }
            }
        }
        finally
        {
            _syncingZoom = false;
        }
    }

    private void OnViewPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(ContentTabViewModel.IsModified))
        {
            AnyTabModifiedChanged?.Invoke(this, EventArgs.Empty);
        }
    }

    /// <summary>
    /// Unsaved working text of the open, modified code tabs, keyed by the resource's book path —
    /// for the "live" preview (<c>PreviewMirror.Sync</c> with overrides). Unmodified tabs
    /// are skipped: their text equals the resource content.
    /// </summary>
    public IReadOnlyDictionary<string, string> ModifiedCodeTexts()
    {
        Dictionary<string, string> texts = new(StringComparer.Ordinal);
        foreach (ContentTabViewModel view in _views.Values)
        {
            if (view is CodeTabViewModel { IsModified: true } code)
            {
                texts[code.ResourceBookPath] = code.DocumentText;
            }
        }

        return texts;
    }

    // Document text for anchor completion: from the open tab (unsaved changes),
    // otherwise from the resource.
    private string? DocumentTextOf(string bookPath)
    {
        foreach (ContentTabViewModel view in _views.Values)
        {
            if (view is CodeTabViewModel code && string.Equals(code.ResourceBookPath, bookPath, StringComparison.Ordinal))
            {
                return code.Document.Text;
            }
        }

        return _book?.GetFolderKeeper().GetResourceByBookPathNoThrow(bookPath) is TextResource text ? text.GetText() : null;
    }

    /// <summary>
    /// Handles Ctrl+click / "Go To Link Or Style" on a link inside the book: opens the target
    /// file (relative to the source file's directory; a lone <c>#fragment</c> — the same file) and,
    /// when there is a fragment, places the caret on the tag with that <c>id</c>/<c>name</c>.
    /// A missing file — a message in the status bar.
    /// </summary>
    private void OnLinkJumpRequested(CodeTabViewModel sourceTab, string reference)
    {
        if (_book is not { } book)
        {
            return;
        }

        (string path, string fragment) = LinkReference.Split(reference);
        string targetBookPath = path.Length == 0
            ? sourceTab.ResourceBookPath
            : Core.BookPath.BuildBookPath(path, Core.BookPath.StartingDir(sourceTab.ResourceBookPath));

        Resource? target = book.GetFolderKeeper().GetResourceByBookPathNoThrow(targetBookPath);
        if (target is null)
        {
            _statusBar.ShowMessage(Strings.Format("Tabs_FileNotInBook", targetBookPath), TimeSpan.FromSeconds(4), NotificationLevel.Warning);
            return;
        }

        OpenResource(target);
        if (fragment.Length == 0 ||
            ActiveTab is not CodeTabViewModel targetTab ||
            !string.Equals(targetTab.ResourceBookPath, targetBookPath, StringComparison.Ordinal))
        {
            return;
        }

        int offset = LinkReference.FindAnchorOffset(targetTab.Document.Text, fragment);
        if (offset < 0)
        {
            _statusBar.ShowMessage(Strings.Format("Tabs_FragmentNotFound", fragment, targetBookPath), TimeSpan.FromSeconds(4));
            return;
        }

        targetTab.SelectMatch(offset, offset);
    }

    /// <summary>
    /// Opens (or activates) the tab of the given book path and places the caret at <paramref name="offset"/>
    /// — the shared "jump" mechanism used by Jump to CSS class definition and the Live CSS
    /// Panel (clicking a rule jumps to its declaration in Code View). No effect when the
    /// book is not loaded or the book path does not match any resource.
    /// </summary>
    public void OpenResourceAtOffset(string bookPath, int offset)
    {
        if (_book is not { } book)
        {
            return;
        }

        Resource? targetResource = book.GetFolderKeeper().GetResourceByBookPathNoThrow(bookPath);
        if (targetResource is null)
        {
            return;
        }

        OpenResource(targetResource);

        if (ActiveTab is CodeTabViewModel targetTab &&
            string.Equals(targetTab.ResourceBookPath, bookPath, StringComparison.Ordinal))
        {
            targetTab.SelectMatch(offset, offset);
        }
    }

    private void OnModelTabOpened(object? sender, TabEventArgs e)
    {
        DebugLog.Write("Tab", $"opened {e.Tab.Resource.BookPath} ({e.Tab.Kind})");
        ContentTabViewModel view = CreateTab(e.Tab);
        _views[e.Tab] = view;
        view.PropertyChanged += OnViewPropertyChanged;

        if (DocumentDock is not { } dock)
        {
            return;
        }

        _bridging = true;
        try
        {
            int count = dock.VisibleDockables?.Count ?? 0;
            if (e.Index >= count)
            {
                _dockFactory.AddDockable(dock, view);
            }
            else
            {
                _dockFactory.InsertDockable(dock, view, e.Index);
            }
        }
        finally
        {
            _bridging = false;
        }
    }

    private void OnModelTabActivated(object? sender, TabEventArgs e)
    {
        ContentTabViewModel? outgoing = _current;
        _views.TryGetValue(e.Tab, out ContentTabViewModel? incoming);
        _current = incoming;

        if (!ReferenceEquals(outgoing, incoming))
        {
            DebugLog.Write("Tab", $"activated {e.Tab.Resource.BookPath}");
            outgoing?.Save();
            ActiveTabChanged?.Invoke(this, EventArgs.Empty);
        }

        if (_bridging || incoming is null || DocumentDock is null)
        {
            return;
        }

        _bridging = true;
        try
        {
            _dockFactory.SetActiveDockable(incoming);
        }
        finally
        {
            _bridging = false;
        }
    }

    private void OnModelTabClosed(object? sender, TabEventArgs e)
    {
        DebugLog.Write("Tab", $"closed {e.Tab.Resource.BookPath}");
        if (!_views.Remove(e.Tab, out ContentTabViewModel? view))
        {
            return;
        }

        view.PropertyChanged -= OnCodeTabPropertyChanged;
        view.PropertyChanged -= OnViewPropertyChanged;
        AnyTabModifiedChanged?.Invoke(this, EventArgs.Empty);

        if (ReferenceEquals(_current, view))
        {
            _current = null;
            ActiveTabChanged?.Invoke(this, EventArgs.Empty);
        }

        view.Save();

        if (_bridging || DocumentDock is null)
        {
            return;
        }

        _bridging = true;
        try
        {
            _dockFactory.CloseDockable(view);
        }
        finally
        {
            _bridging = false;
        }
    }

    private void OnModelTabCaptionChanged(object? sender, TabEventArgs e)
    {
        if (_views.TryGetValue(e.Tab, out ContentTabViewModel? view))
        {
            view.RefreshCaption();
        }
    }

    private void OnDockActiveDockableChanged(object? sender, ActiveDockableChangedEventArgs e)
    {
        if (_bridging || e.Dockable is not ContentTabViewModel view)
        {
            return;
        }

        _bridging = true;
        try
        {
            _model.ActivateResource(view.Resource);
        }
        finally
        {
            _bridging = false;
        }
    }

    private void OnDockableClosed(object? sender, DockableClosedEventArgs e)
    {
        if (_bridging || e.Dockable is not ContentTabViewModel view)
        {
            return;
        }

        _bridging = true;
        try
        {
            _model.CloseResource(view.Resource, force: true);
        }
        finally
        {
            _bridging = false;
        }
    }
}

/// <summary>In-memory snapshot of the open tabs (<see cref="TabManager.CaptureSnapshot"/>).</summary>
/// <param name="Session">Open book paths in order and the active tab.</param>
/// <param name="CaretOffsets">Caret positions of Code View tabs (book path -&gt; offset).</param>
public sealed record TabSnapshot(TabSession Session, IReadOnlyDictionary<string, int> CaretOffsets);
