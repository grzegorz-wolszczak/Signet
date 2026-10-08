using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Threading;
using System;
using CommunityToolkit.Mvvm.Input;
using Signet.App.Resources;
using Signet.App.Services;
using Signet.Core.BookManipulation;
using Signet.Core.Misc;
using Signet.Core.Preview;
using Signet.Core.Resources;

namespace Signet.App.ViewModels;

/// <summary>
/// View model of the preview panel. Holds the view-less state (the currently shown resource,
/// zoom, address) and asks the view to perform operations on the native WebView control through events.
/// Rendering comes from <see cref="PreviewMirror"/> (a copy of the publication in a temporary
/// directory, addressed through <c>file://</c>).
/// </summary>
/// <remarks>
/// It shows the active <see cref="HtmlResource"/>, refreshes on tab switch/save and on request
/// ("Reload"), supports zoom, and blocks navigation to external addresses. It also refreshes "live"
/// with a debounce (<see cref="NotifyContentChanged"/>) and synchronizes the position in both
/// directions (<see cref="SyncCaretToPreview"/> — Code View → Preview,
/// <see cref="HandlePreviewMessage"/> → <see cref="CodeCaretJumpRequested"/> — Preview → Code View)
/// through the <c>data-signet-loc</c> instrumentation (<see cref="PreviewInstrumentation"/>).
/// </remarks>
public sealed partial class PreviewViewModel : ViewModelBase, IDisposable
{
    /// <summary>Lower limit of the preview zoom.</summary>
    public const double MinZoom = 0.25;

    /// <summary>Upper limit of the preview zoom.</summary>
    public const double MaxZoom = 5.0;

    private static readonly TimeSpan DefaultDebounce = TimeSpan.FromMilliseconds(350);

    private readonly IStatusBarService _statusBar;

    private Func<string, bool> _externalFileOpener = path => ExternalOpen.TryOpen(path, out _);
    private Func<IReadOnlyDictionary<string, string>>? _workingTexts;
    private PreviewMirror? _mirror;
    private PreviewInstrumentation? _instrumentation;
    private Timer? _debounceTimer;
    private SynchronizationContext? _sync;
    private TimeSpan _debounceInterval = DefaultDebounce;
    private Book? _book;
    private string? _currentBookPath;
    private bool _lastSyncWellFormed = true;
    private int? _lastCaretOffset;
    private bool _restoreCaretAfterLoad;
    private string _currentUrlText = string.Empty;
    private double _zoomFactor = 1.0;
    private bool _disposed;

    // "Cycle Custom CSS Files": at most 2 files with fixed names in the prefs directory
    // (not an arbitrary, configurable list).
    private const string CustomCssFileName = "custom_preview_style.css";
    private const string CustomCssAltFileName = "custom_preview_style_alt.css";
    private readonly List<string> _customCssPaths;
    private int _cycleCssLevel;

    /// <summary>Creates the preview view model.</summary>
    /// <param name="statusBar">Status bar (messages about blocked navigation, etc.).</param>
    public PreviewViewModel(IStatusBarService statusBar)
        : this(statusBar, AppDirectories.PrefsDirectory)
    {
    }

    /// <summary>
    /// Like <see cref="PreviewViewModel(IStatusBarService)"/>, but with an explicitly given directory
    /// searched for custom CSS — for unit tests, so as not to depend on the user's real prefs directory.
    /// </summary>
    public PreviewViewModel(IStatusBarService statusBar, string customCssFolder)
    {
        _statusBar = statusBar ?? throw new ArgumentNullException(nameof(statusBar));
        ArgumentNullException.ThrowIfNull(customCssFolder);
        _customCssPaths = DiscoverCustomCssPaths(customCssFolder);
    }

    private static List<string> DiscoverCustomCssPaths(string folder)
    {
        List<string> found = new(2);
        foreach (string name in new[] { CustomCssFileName, CustomCssAltFileName })
        {
            string path = Path.Combine(folder, name);
            if (File.Exists(path))
            {
                found.Add(path);
            }
        }

        return found;
    }

    /// <summary>The view should navigate the WebView control to the given address.</summary>
    public event EventHandler<Uri>? NavigateRequested;

    /// <summary>The view should reload the current page in the WebView control.</summary>
    public event EventHandler? ReloadRequested;

    /// <summary>The zoom factor changed — the view should apply it to the control.</summary>
    public event EventHandler<double>? ZoomChanged;

    /// <summary>
    /// The view should scroll the preview to the element <c>[data-signet-loc="{arg}"]</c> (Code View → Preview).
    /// </summary>
    public event EventHandler<int>? ScrollToLocRequested;

    /// <summary>
    /// An element in the preview was clicked — the host should place the Code View caret at the source offset
    /// <c>{arg}</c> (Preview → Code View).
    /// </summary>
    public event EventHandler<int>? CodeCaretJumpRequested;

    /// <summary>
    /// The view should show the native, interactive print dialog of the current preview page
    /// (File → Print). It operates on the shared Preview panel.
    /// </summary>
    public event EventHandler? PrintRequested;

    /// <summary>
    /// The view should export the current preview page to the given PDF file (File →
    /// Print Preview). After success the view reports readiness through
    /// <see cref="NotifyPrintPreviewReady"/>, which opens the file in the system's default PDF
    /// viewer — the native WebView library has no "preview in browser" mode
    /// separate from the native print dialog (<see cref="PrintRequested"/>).
    /// </summary>
    public event EventHandler<string>? PrintToPdfRequested;

    /// <summary>
    /// The view should open the native inspector window (DevTools) for the current preview page
    /// (the "Inspect" button on the Preview bar). The WebView library opens DevTools only as a
    /// separate native system window, not as a dockable panel.
    /// </summary>
    public event EventHandler? InspectorRequested;

    /// <summary>
    /// The view should select the whole content of the preview page (the "Select All" action
    /// when it applies in Preview).
    /// </summary>
    public event EventHandler? SelectAllRequested;

    /// <summary>
    /// The view should copy the current selection of the preview page to the clipboard (the "Copy"
    /// action when it applies in Preview).
    /// </summary>
    public event EventHandler? CopyRequested;

    /// <summary>
    /// The view should attach (or detach, when <see langword="null"/>) a custom CSS stylesheet
    /// at the given path to the current preview page ("Cycle Custom CSS Files"). It must be
    /// applied again after every navigation, because the page is reloaded.
    /// </summary>
    public event EventHandler<string?>? CustomCssRequested;

    /// <summary>
    /// Debounce delay of the "live" refresh. <see cref="TimeSpan.Zero"/> = refresh
    /// synchronously (for tests).
    /// </summary>
    public TimeSpan DebounceInterval
    {
        get => _debounceInterval;
        set => _debounceInterval = value < TimeSpan.Zero ? TimeSpan.Zero : value;
    }

    /// <summary>
    /// Appearance of the caret position highlight in the preview (Preferences → Preview). Set by
    /// the host from <see cref="SettingsStore.PreviewHighlight"/>; the view applies it on every
    /// <see cref="ScrollToLocRequested"/>.
    /// </summary>
    public PreviewHighlight Highlight { get; set; } = PreviewHighlight.Default;

    /// <summary>Whether the preview currently has something to show (an (X)HTML resource is selected).</summary>
    public bool HasContent => _currentBookPath is not null;

    /// <summary>Book path of the currently shown resource (for the address label and tests).</summary>
    public string? CurrentBookPath => _currentBookPath;

    /// <summary>Address text shown on the preview bar.</summary>
    public string CurrentUrlText
    {
        get => _currentUrlText;
        private set => SetProperty(ref _currentUrlText, value);
    }

    /// <summary>Preview zoom factor (1.0 = 100%).</summary>
    public double ZoomFactor
    {
        get => _zoomFactor;
        private set
        {
            double clamped = Math.Clamp(value <= 0 ? 1.0 : value, MinZoom, MaxZoom);
            if (SetProperty(ref _zoomFactor, clamped))
            {
                OnPropertyChanged(nameof(ZoomPercentText));
                ZoomChanged?.Invoke(this, clamped);
            }
        }
    }

    /// <summary>Zoom as "NNN%" text.</summary>
    public string ZoomPercentText => $"{Math.Round(_zoomFactor * 100)}%";

    /// <summary>Working directory of the preview mirror (or <c>null</c> until something was shown) — for the view/tests.</summary>
    public string? MirrorRootPath => _mirror?.RootPath;

    /// <summary>Sets (or clears) the publication. Called from <c>ApplyBook</c>.</summary>
    /// <param name="book">The new publication, or <c>null</c>.</param>
    public void SetBook(Book? book)
    {
        ThrowIfDisposed();
        _book = book;
        _currentBookPath = null;
        _lastCaretOffset = null;
        _restoreCaretAfterLoad = false;
        _instrumentation = null;
        CurrentUrlText = string.Empty;
        OnPropertyChanged(nameof(HasContent));
        OnPropertyChanged(nameof(CurrentBookPath));
    }

    /// <summary>
    /// Shows the given resource in the preview. Resources other than (X)HTML are ignored (the preview
    /// stays on the previous page — the preview follows the active HTML tab).
    /// </summary>
    /// <param name="resource">The active tab's resource.</param>
    public void ShowResource(Resource? resource)
    {
        ThrowIfDisposed();
        if (_book is null || resource is not HtmlResource html)
        {
            return;
        }

        if (!string.Equals(_currentBookPath, html.BookPath, StringComparison.Ordinal))
        {
            _lastCaretOffset = null;
        }

        _currentBookPath = html.BookPath;
        OnPropertyChanged(nameof(HasContent));
        OnPropertyChanged(nameof(CurrentBookPath));
        SyncAndNavigate();
    }

    /// <summary>Re-synchronizes the mirror and reloads the preview (the "Reload" button, tab save).</summary>
    public void Refresh()
    {
        ThrowIfDisposed();
        RefreshCore(liveEdit: false);
    }

    /// <summary>
    /// Reports a content change of the active resource (typing in Code View / editing CSS). The preview
    /// refresh is delayed by <see cref="DebounceInterval"/> — further reports within this window
    /// reset the timer.
    /// </summary>
    /// <remarks>
    /// When the source of the shown file (the tab's working text) is temporarily not well-formed —
    /// typical while typing, e.g. <c>&lt;p</c> without <c>&gt;</c> — the refresh is skipped and the preview
    /// stays on the last valid version (the WebView parses <c>.xhtml</c> as XML and would show the parser
    /// error page). An explicit <see cref="Refresh"/> (Reload, save) always refreshes.
    /// </remarks>
    public void NotifyContentChanged()
    {
        if (_disposed || _book is null || _currentBookPath is null)
        {
            return;
        }

        if (_debounceInterval <= TimeSpan.Zero)
        {
            RefreshCore(liveEdit: true);
            return;
        }

        _sync ??= SynchronizationContext.Current;
        _debounceTimer ??= new Timer(OnDebounceElapsed, state: null, Timeout.Infinite, Timeout.Infinite);
        _debounceTimer.Change(_debounceInterval, Timeout.InfiniteTimeSpan);
    }

    /// <summary>
    /// The Code View caret is at offset <paramref name="sourceOffset"/> — scroll the preview to the
    /// corresponding element (Code View → Preview).
    /// </summary>
    /// <param name="sourceOffset">Caret offset in the source of the active file.</param>
    public void SyncCaretToPreview(int sourceOffset)
    {
        if (_disposed)
        {
            return;
        }

        _lastCaretOffset = sourceOffset;
        if (_instrumentation is null)
        {
            return;
        }

        if (!_lastSyncWellFormed)
        {
            // When the source in Code View is not well-formed, the caret position in the preview's
            // leniently re-parsed (HTML5) DOM is uncertain/misleading — instead of computing the nearest
            // loc, force a scroll to the earliest instrumented element (the start of the document).
            if (_instrumentation.Locs.Count > 0)
            {
                ScrollToLocRequested?.Invoke(this, _instrumentation.Locs[0]);
            }

            return;
        }

        int loc = _instrumentation.NearestLocAtOrBefore(sourceOffset);
        if (loc >= 0)
        {
            ScrollToLocRequested?.Invoke(this, loc);
        }
    }

    /// <summary>
    /// The view reports the end of page loading in the WebView. After a reload from <see cref="Refresh"/> /
    /// a "live" refresh, scrolls the preview back to the Code View caret location instead of
    /// leaving it at the top of the page.
    /// </summary>
    public void NotifyPageLoaded()
    {
        if (_disposed || !_restoreCaretAfterLoad)
        {
            return;
        }

        _restoreCaretAfterLoad = false;
        if (_lastCaretOffset is { } offset)
        {
            SyncCaretToPreview(offset);
        }
    }

    /// <summary>
    /// Handles a message from the preview page script. Recognizes <c>signet-loc:{offset}</c>
    /// (an element click) → <see cref="CodeCaretJumpRequested"/>, and <c>signet-zoom:in</c> / <c>out</c> /
    /// <c>reset</c> (Ctrl+wheel, Ctrl+Plus/Minus/0 in the page, whose native zoom the script suppresses) → the
    /// preview's own zoom.
    /// </summary>
    /// <param name="message">The message content from <c>WebMessageReceived</c>.</param>
    public void HandlePreviewMessage(string? message)
    {
        if (_disposed || string.IsNullOrEmpty(message))
        {
            return;
        }

        switch (message)
        {
            case "signet-zoom:in":
                ZoomIn();
                return;
            case "signet-zoom:out":
                ZoomOut();
                return;
            case "signet-zoom:reset":
                ZoomReset();
                return;
        }

        const string prefix = "signet-loc:";
        if (message.StartsWith(prefix, StringComparison.Ordinal)
            && int.TryParse(
                message.AsSpan(prefix.Length),
                NumberStyles.Integer,
                CultureInfo.InvariantCulture,
                out int offset)
            && offset >= 0)
        {
            CodeCaretJumpRequested?.Invoke(this, offset);
        }
    }

    private void OnDebounceElapsed(object? state)
    {
        if (_disposed)
        {
            return;
        }

        if (_sync is not null)
        {
            _sync.Post(_ => SafeRefresh(), null);
        }
        else
        {
            SafeRefresh();
        }
    }

    private void SafeRefresh()
    {
        if (!_disposed)
        {
            RefreshCore(liveEdit: true);
        }
    }

    private void RefreshCore(bool liveEdit)
    {
        if (_book is null || _currentBookPath is null)
        {
            return;
        }

        IReadOnlyDictionary<string, string>? overrides = _workingTexts?.Invoke();
        if (liveEdit && CurrentSourceText(overrides) is { } source && !WellFormedChecker.IsWellFormed(source))
        {
            return;
        }

        if (TrySync(overrides))
        {
            _restoreCaretAfterLoad = true;
            ReloadRequested?.Invoke(this, EventArgs.Empty);
        }
    }

    /// <summary>
    /// Decision about a navigation reported by the WebView: addresses inside the mirror are allowed,
    /// the rest are blocked (with a message on the status bar).
    /// </summary>
    /// <param name="target">The navigation target address (or <c>null</c>).</param>
    /// <returns><c>true</c> when the navigation should be allowed; <c>false</c> when it should be blocked.</returns>
    public bool AllowNavigation(Uri? target)
    {
        ThrowIfDisposed();

        if (target is null || _mirror is null)
        {
            return true;
        }

        if (target.Scheme == "about" || target.Scheme == "data")
        {
            return true;
        }

        if (_mirror.IsInsideMirror(target))
        {
            return true;
        }

        _statusBar.ShowMessage(
            Strings.Format("Preview_ExternalNavigationBlocked", target), TimeSpan.FromSeconds(5));
        return false;
    }

    /// <summary>Replaces the way files are opened in an external program (tests — default <see cref="ExternalOpen.TryOpen(string, out string?)"/>).</summary>
    public void AttachExternalFileOpener(Func<string, bool> opener) =>
        _externalFileOpener = opener ?? throw new ArgumentNullException(nameof(opener));

    /// <summary>
    /// Attaches the source of unsaved working text of the code tabs (book path → text). Every
    /// mirror synchronization takes the content from it instead of the resource text, so edits in Code View
    /// (HTML, CSS…) are visible in the preview without saving the tab.
    /// </summary>
    /// <param name="provider">The working text provider (e.g. <c>TabManager.ModifiedCodeTexts</c>).</param>
    public void AttachWorkingTextProvider(Func<IReadOnlyDictionary<string, string>> provider) =>
        _workingTexts = provider ?? throw new ArgumentNullException(nameof(provider));

    /// <summary>Requests the native print dialog of the current page (File → Print). Without content — no action.</summary>
    public void RequestPrint()
    {
        if (HasContent)
        {
            PrintRequested?.Invoke(this, EventArgs.Empty);
        }
    }

    /// <summary>Requests the export of the current page to a temporary PDF (File → Print Preview). Without content — no action.</summary>
    public void RequestPrintPreview()
    {
        if (!HasContent)
        {
            return;
        }

        string path = Path.Combine(Path.GetTempPath(), $"signet-print-preview-{Guid.NewGuid():N}.pdf");
        PrintToPdfRequested?.Invoke(this, path);
    }

    /// <summary>The view reports the print preview PDF file as ready — open it in an external program.</summary>
    public void NotifyPrintPreviewReady(string path) => _externalFileOpener(path);

    [RelayCommand]
    private void Inspect()
    {
        if (HasContent)
        {
            InspectorRequested?.Invoke(this, EventArgs.Empty);
        }
    }

    [RelayCommand]
    private void Reload() => Refresh();

    // The "Print Preview View" button on the bottom Preview bar.
    [RelayCommand]
    private void Print() => RequestPrint();

    [RelayCommand]
    private void SelectAllContent()
    {
        if (HasContent)
        {
            SelectAllRequested?.Invoke(this, EventArgs.Empty);
        }
    }

    [RelayCommand]
    private void CopySelection()
    {
        if (HasContent)
        {
            CopyRequested?.Invoke(this, EventArgs.Empty);
        }
    }

    /// <summary>Whether at least one custom CSS file was found in the prefs directory.</summary>
    public bool HasCustomCss => _customCssPaths.Count > 0;

    /// <summary>
    /// Cycles the custom CSS stylesheet attached to the preview: none → the first
    /// file → (the second file, if it exists) → back to none.
    /// </summary>
    [RelayCommand(CanExecute = nameof(HasCustomCss))]
    private void CycleCustomCss()
    {
        _cycleCssLevel++;
        if (_cycleCssLevel > _customCssPaths.Count)
        {
            _cycleCssLevel = 0;
        }

        CustomCssRequested?.Invoke(this, _cycleCssLevel > 0 ? _customCssPaths[_cycleCssLevel - 1] : null);
    }

    [RelayCommand]
    private void ZoomIn() => ZoomFactor *= 1.1;

    [RelayCommand]
    private void ZoomOut() => ZoomFactor /= 1.1;

    [RelayCommand]
    private void ZoomReset() => ZoomFactor = 1.0;

    /// <summary>Multiplies the zoom by <paramref name="factor"/> (View → Zoom menu when Preview has focus).</summary>
    public void ApplyZoom(double factor) => ZoomFactor *= factor;

    /// <summary>Restores the zoom to 100% (View → Zoom Reset menu when Preview has focus).</summary>
    public void ResetZoom() => ZoomFactor = 1.0;

    /// <summary>Sets the zoom (clamped to <see cref="MinZoom"/>…<see cref="MaxZoom"/>) — the remembered zoom at startup.</summary>
    public void SetZoom(double factor) => ZoomFactor = factor;

    private void SyncAndNavigate()
    {
        if (!TrySync(_workingTexts?.Invoke()))
        {
            return;
        }

        Uri url = _mirror!.UrlForBookPath(_currentBookPath!);
        CurrentUrlText = _currentBookPath!;
        NavigateRequested?.Invoke(this, url);
    }

    private bool TrySync(IReadOnlyDictionary<string, string>? overrides)
    {
        _mirror ??= new PreviewMirror();

        try
        {
            _instrumentation = _mirror.Sync(_book!, _currentBookPath, overrides);
            _lastSyncWellFormed = CurrentSourceText(overrides) is { } source
                && WellFormedChecker.IsWellFormed(source);
            return true;
        }
        catch (Exception ex) when (ex is System.IO.IOException or UnauthorizedAccessException)
        {
            _statusBar.ShowMessage(Strings.Format("Preview_PrepareFailed", ex.Message), TimeSpan.FromSeconds(5), NotificationLevel.Warning);
            return false;
        }
    }

    // Source of the shown file: the tab's working text if present, otherwise the resource content.
    private string? CurrentSourceText(IReadOnlyDictionary<string, string>? overrides)
    {
        if (_currentBookPath is null)
        {
            return null;
        }

        if (overrides is not null && overrides.TryGetValue(_currentBookPath, out string? working))
        {
            return working;
        }

        return _book!.GetFolderKeeper().GetResourceByBookPathNoThrow(_currentBookPath) is HtmlResource html
            ? html.GetText()
            : null;
    }

    private void ThrowIfDisposed() => ObjectDisposedException.ThrowIf(_disposed, this);

    /// <inheritdoc/>
    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _debounceTimer?.Dispose();
        _debounceTimer = null;
        _mirror?.Dispose();
        _mirror = null;
        _instrumentation = null;
    }

    /// <summary>A view request for a fresh start address (when the control initialized after <see cref="ShowResource"/>).</summary>
    /// <returns>The current page address, or <c>null</c>.</returns>
    public Uri? CurrentUrlOrNull()
    {
        if (_mirror is null || _currentBookPath is null)
        {
            return null;
        }

        return _mirror.UrlForBookPath(_currentBookPath);
    }
}
