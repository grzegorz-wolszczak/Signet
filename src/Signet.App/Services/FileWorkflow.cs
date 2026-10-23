using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System;
using Microsoft.Extensions.Logging;
using Signet.App.Infrastructure;
using Signet.App.Resources;
using Signet.Core.BookManipulation;
using Signet.Core.Importers;
using Signet.Core.Misc;
using Signet.Core;

namespace Signet.App.Services;

/// <summary>The user's choice in the "unsaved changes" dialog.</summary>
public enum SaveChangesChoice
{
    /// <summary>Abort the operation (also the default when the dialog is closed with the X button).</summary>
    Cancel,

    /// <summary>Save the changes, then continue.</summary>
    Save,

    /// <summary>Discard the changes and continue.</summary>
    Discard,
}

/// <summary>
/// The user's choice when the file to load is already open in another instance of the application (see
/// <see cref="IFileWorkflowPrompts.AskFileInUseAsync"/>).
/// </summary>
public enum FileInUseChoice
{
    /// <summary>Close this instance (also the default when the dialog is closed with the X button).</summary>
    Close,

    /// <summary>Do not load the file: start with an empty book.</summary>
    OpenEmpty,

    /// <summary>Load the file anyway — the two instances can overwrite each other's changes.</summary>
    OpenAnyway,

    /// <summary>Pick another file to load.</summary>
    ChooseAnother,
}

/// <summary>UI operations needed by the file workflow (dialogs). Implemented by the main window view.</summary>
public interface IFileWorkflowPrompts
{
    /// <summary>
    /// The "already open in another instance" dialog: the file to load is open in another instance of the application.
    /// </summary>
    /// <param name="fileName">Name of the file.</param>
    Task<FileInUseChoice> AskFileInUseAsync(string fileName);

    /// <summary>Closes the application (the main window), as the window's close button does.</summary>
    void CloseApplication();

    /// <summary>File-to-open picker dialog. Returns the path, or <c>null</c> when cancelled.</summary>
    Task<string?> AskOpenPathAsync(string startFolder);

    /// <summary>Picker dialog for the <c>.epub</c> save path. Returns the path, or <c>null</c> when cancelled.</summary>
    Task<string?> AskSavePathAsync(string suggestedPath);

    /// <summary>Modal "document changed — save?" dialog.</summary>
    Task<SaveChangesChoice> AskSaveChangesAsync();

    /// <summary>Shows an error message.</summary>
    Task ShowErrorAsync(string title, string message);

    /// <summary>
    /// Shows the warnings collected while loading a file.
    /// </summary>
    Task ShowLoadWarningsAsync(string fileName, IReadOnlyList<LoadWarning> warnings);

    /// <summary>Asks whether to remove a nonexistent file from the "Recent Files" list (OK = remove).</summary>
    Task<bool> ConfirmRemoveMissingRecentAsync(string path);

    /// <summary>"Custom Epub Layout" wizard. Returns a list of book paths, or <c>null</c> when cancelled.</summary>
    Task<IReadOnlyList<string>?> DesignCustomLayoutAsync(string version);

    /// <summary>
    /// Runs <paramref name="work"/> (started at once) with a progress window headed <paramref name="title"/> (the name of the
    /// operation) and showing <paramref name="message"/> until the work reports, when it lasts; the window goes when the work is done
    /// and its result is on screen. The work tells how far it is (<see cref="WorkProgress"/>) through the progress it is given.
    /// Returns the result of the work.
    /// </summary>
    Task<T> ShowProgressAsync<T>(string title, string message, Func<IProgress<WorkProgress>, Task<T>> work);
}

/// <summary>The main window surface as seen by <see cref="FileWorkflow"/> (swapping the book, change state).</summary>
public interface IBookWorkspace
{
    /// <summary>The currently loaded publication, or <c>null</c>.</summary>
    Book? CurrentBook { get; }

    /// <summary>Whether there are unsaved changes (the book or any of the open tabs).</summary>
    bool HasUnsavedChanges { get; }

    /// <summary>Flushes the buffered content of the open tabs into the resources.</summary>
    void SaveOpenTabs();

    /// <summary>Hands a freshly built publication over to the views.</summary>
    void ApplyBook(Book book, string? sourcePath);

    /// <summary>
    /// Runs <paramref name="work"/>, which works on the current publication, off the UI thread as a book operation: under a progress
    /// window headed <paramref name="title"/> and with every other operation on the book off meanwhile. The work tells how far it is
    /// through the progress it is given. Returns <c>false</c> when it did not run (another operation is running). An exception of the
    /// work is thrown here.
    /// </summary>
    Task<bool> RunBookWorkAsync(string title, Action<IProgress<WorkProgress>> work);

    /// <summary>Closes the current publication: the window is left without a book (the start screen).</summary>
    void CloseBook();

    /// <summary>
    /// Refreshes the views after the book was changed outside of them (the edition page written on save): reloads the
    /// open tabs, the Book Browser, the table of contents and the preview.
    /// </summary>
    void RefreshAfterBookEdit();
}

/// <summary>
/// The File menu workflow: New / Open / Save / Save As / Save A Copy, the "Recent Files" list,
/// and the unsaved-changes guard.
/// </summary>
public sealed class FileWorkflow : IDisposable
{
    /// <summary>Default file name for a new publication.</summary>
    public const string DefaultFileName = "untitled.epub";

    // We store up to the upper bound of the start screen limit — the start screen and the menu show only the first
    // SettingsStore.StartScreenRecentFilesLimit / RecentFilesLimit entries, so lowering a limit does not lose history.
    private const int MaxRecentFiles = SettingsStore.RecentFilesMax;

    private readonly SettingsStore _settings;
    private readonly IFileWorkflowPrompts _prompts;
    private readonly IBookWorkspace _workspace;
    private readonly ILogger _logger;
    private readonly BookFileLocks? _locks;

    // The lock on the file of the current book, taken so that another instance finds the file in use; null for a book
    // without a file, a file another instance has open too (loaded anyway) and when there are no locks.
    private BookFileLock? _bookLock;

    private string _currentFilePath = string.Empty;
    private string _saveACopyFilename = string.Empty;

    // The book whose edition page was already written in this session (null until the first save after opening).
    private Book? _editionStampedBook;

    /// <summary>Creates the workflow.</summary>
    /// <param name="settings">The settings.</param>
    /// <param name="prompts">The dialogs.</param>
    /// <param name="workspace">The main window surface.</param>
    /// <param name="logger">The logger.</param>
    /// <param name="locks">Tells the instances apart that have the same file open; <c>null</c> = not checked.</param>
    public FileWorkflow(
        SettingsStore settings,
        IFileWorkflowPrompts prompts,
        IBookWorkspace workspace,
        ILogger logger,
        BookFileLocks? locks = null)
    {
        _locks = locks;
        _settings = settings ?? throw new ArgumentNullException(nameof(settings));
        _prompts = prompts ?? throw new ArgumentNullException(nameof(prompts));
        _workspace = workspace ?? throw new ArgumentNullException(nameof(workspace));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <summary>
    /// Lets go of the lock on the file of the current book (what the system does anyway when the application ends), so that
    /// another instance can open the file.
    /// </summary>
    public void Dispose() => ReleaseLock();

    /// <summary>Raised when the path/name of the current file changes (to refresh the window title).</summary>
    public event EventHandler? CurrentFileChanged;

    /// <summary>Raised when the "Recent Files" list changes.</summary>
    public event EventHandler? RecentFilesChanged;

    /// <summary>Full path of the current file (empty for a new, unsaved publication).</summary>
    public string CurrentFilePath => _currentFilePath;

    /// <summary>Name of the current file, or <see cref="DefaultFileName"/> when the publication has no path yet.</summary>
    public string CurrentFileName =>
        _currentFilePath.Length == 0 ? DefaultFileName : Path.GetFileName(_currentFilePath);

    /// <summary>Whether the current publication already has a saved <c>.epub</c> file.</summary>
    public bool HasSavedFile =>
        _currentFilePath.Length > 0 && IsEpubExtension(_currentFilePath);

    /// <summary>List of recently opened files (newest first).</summary>
    public IReadOnlyList<string> RecentFiles => _settings.RecentFiles;

    // ----------------------------------------------------------------- New --- //

    /// <summary>File → New (New Default / ePub2 / ePub3). An empty <paramref name="version"/> = the default from the settings.</summary>
    public async Task NewAsync(string? version)
    {
        if (!await MaybeSaveDialogSaysProceedAsync().ConfigureAwait(true))
        {
            return;
        }

        LoadFreshBook(CreateDefaultBook(version));
    }

    // A new book of the given version (empty = the default from the settings) with the default layout.
    private Book CreateDefaultBook(string? version)
    {
        string epubVersion = ResolveVersion(version);
        IReadOnlyList<string> defaultLayout = EmptyEpubLayout.ReadDefault();
        return BookCreator.CreateNewBook(
            epubVersion,
            defaultLayout.Count > 0 ? defaultLayout : null);
    }

    /// <summary>File → New → the "Custom Epub Layout" wizard.</summary>
    public async Task NewWithCustomLayoutAsync(string? version)
    {
        string epubVersion = ResolveVersion(version);
        IReadOnlyList<string>? bookPaths = await _prompts.DesignCustomLayoutAsync(epubVersion).ConfigureAwait(true);
        if (bookPaths is null)
        {
            return;
        }

        if (!await MaybeSaveDialogSaysProceedAsync().ConfigureAwait(true))
        {
            return;
        }

        Book book = BookCreator.CreateNewBook(epubVersion, bookPaths);
        LoadFreshBook(book);
    }

    // --------------------------------------------------------------- Close --- //

    /// <summary>File → Close: closes the book (after the unsaved-changes guard); the window is left without a book.</summary>
    public async Task CloseAsync()
    {
        if (_workspace.CurrentBook is null || !await MaybeSaveDialogSaysProceedAsync().ConfigureAwait(true))
        {
            return;
        }

        DebugLog.Write("File", "close book");
        _workspace.CloseBook();
        ReleaseLock();
        _currentFilePath = string.Empty;
        _saveACopyFilename = string.Empty;
        CurrentFileChanged?.Invoke(this, EventArgs.Empty);
    }

    // ---------------------------------------------------------------- Open --- //

    /// <summary>File → Open.</summary>
    public async Task OpenAsync()
    {
        if (!await MaybeSaveDialogSaysProceedAsync().ConfigureAwait(true))
        {
            return;
        }

        string? path = await _prompts.AskOpenPathAsync(OpenStartFolder()).ConfigureAwait(true);
        if (string.IsNullOrEmpty(path))
        {
            return;
        }

        await LoadFileAsync(path).ConfigureAwait(true);
    }

    // The folder the open dialog starts in: the last one used, or the home folder.
    private string OpenStartFolder()
    {
        string startFolder = _settings.LastFolderOpen;
        return startFolder.Length == 0 || !Directory.Exists(startFolder)
            ? Environment.GetFolderPath(Environment.SpecialFolder.UserProfile)
            : startFolder;
    }

    /// <summary>
    /// Opens a file dragged onto the window (drag &amp; drop): like File → Open, but without the file
    /// picker — first the unsaved-changes guard, then <see cref="LoadFileAsync"/>.
    /// </summary>
    public async Task OpenDroppedFileAsync(string path)
    {
        ArgumentException.ThrowIfNullOrEmpty(path);

        if (!await MaybeSaveDialogSaysProceedAsync().ConfigureAwait(true))
        {
            return;
        }

        await LoadFileAsync(path).ConfigureAwait(true);
    }

    /// <summary>Opens a file from the "Recent Files" list (handles the file missing on disk).</summary>
    public async Task OpenRecentAsync(string path)
    {
        ArgumentException.ThrowIfNullOrEmpty(path);

        if (!File.Exists(path))
        {
            if (await _prompts.ConfirmRemoveMissingRecentAsync(path).ConfigureAwait(true))
            {
                RemoveRecentFile(path);
            }

            return;
        }

        if (!await MaybeSaveDialogSaysProceedAsync().ConfigureAwait(true))
        {
            return;
        }

        await LoadFileAsync(path).ConfigureAwait(true);
    }

    /// <summary>
    /// Loads the given file (EPUB / XHTML / HTML / HTM / TXT) and makes it the current publication.
    /// The unsaved-changes guard must be called beforehand.
    /// </summary>
    public async Task<bool> LoadFileAsync(string path)
    {
        ArgumentException.ThrowIfNullOrEmpty(path);
        long start = System.Diagnostics.Stopwatch.GetTimestamp();
        bool loaded = await LoadFileCoreAsync(path).ConfigureAwait(true);
        DebugLog.Write("File", $"open {DebugLog.MaskPath(path)} → {(loaded ? "ok" : "failed")} ({DebugLog.Elapsed(start)})");
        return loaded;
    }

    private async Task<bool> LoadFileCoreAsync(string path)
    {
        if (!File.Exists(path))
        {
            await _prompts.ShowErrorAsync("Signet", Strings.Format("File_DoesNotExist", path)).ConfigureAwait(true);
            return false;
        }

        ImporterOptions options = new(
            _settings.DefaultVersion,
            (_settings.CleanOn & CleanOn.Open) != 0,
            _settings.MendAddMissingDoctype);

        IImporter? importer = ImporterFactory.GetImporter(path, options);
        if (importer is null)
        {
            await _prompts.ShowErrorAsync(
                "Signet",
                Strings.Format("File_UnsupportedType", Path.GetExtension(path))).ConfigureAwait(true);
            return false;
        }

        // Another instance with the same file open: the user decides before anything is loaded. The file of the book that
        // is open here is no conflict (it is loaded again, the lock stays).
        BookFileLock? acquired = null;
        bool ownFile = _bookLock is not null && _bookLock.Key == BookFileLocks.KeyOf(path);
        if (_locks is not null && !ownFile)
        {
            acquired = _locks.TryAcquire(path);
            if (acquired is null && await HandleFileInUseAsync(path).ConfigureAwait(true) is { } handled)
            {
                return handled;
            }
        }

        // The check of the file, the import and showing the book take seconds for a big book: they run with a progress
        // window (when they last), the messages come after it.
        (Book? book, string? error) = await _prompts.ShowProgressAsync(
            Strings.Get("File_LoadingTitle"),
            Strings.Format("File_Loading", Path.GetFileName(path)),
            progress => ImportAndApplyAsync(importer, path, progress)).ConfigureAwait(true);
        if (book is null)
        {
            acquired?.Dispose();
            await _prompts.ShowErrorAsync("Signet", error ?? string.Empty).ConfigureAwait(true);
            return false;
        }

        if (!ownFile)
        {
            _bookLock?.Dispose();
            _bookLock = acquired;
        }

        if (book.LoadWarnings.Count > 0)
        {
            await _prompts.ShowLoadWarningsAsync(Path.GetFileName(path), book.LoadWarningDetails).ConfigureAwait(true);
        }

        return true;
    }

    // The file to load is open in another instance: asks what to do until the user has chosen something that ends the
    // question. Returns null to load the file anyway, otherwise the result of the load (false: nothing was loaded from
    // this file — an empty book, another file, or the application is closing).
    private async Task<bool?> HandleFileInUseAsync(string path)
    {
        DebugLog.Write("File", $"{DebugLog.MaskPath(path)} is open in another instance");
        while (true)
        {
            switch (await _prompts.AskFileInUseAsync(Path.GetFileName(path)).ConfigureAwait(true))
            {
                case FileInUseChoice.OpenAnyway:
                    return null;
                case FileInUseChoice.OpenEmpty:
                    LoadFreshBook(CreateDefaultBook(null));
                    return false;
                case FileInUseChoice.ChooseAnother:
                    string? other = await _prompts.AskOpenPathAsync(OpenStartFolder()).ConfigureAwait(true);
                    if (!string.IsNullOrEmpty(other))
                    {
                        return await LoadFileCoreAsync(other).ConfigureAwait(true);
                    }

                    break;
                default:
                    _prompts.CloseApplication();
                    return false;
            }
        }
    }

    // Checks and imports the file off the UI thread and makes it the current publication; the book, or why it could not
    // be loaded.
    private async Task<(Book? Book, string? Error)> ImportAndApplyAsync(IImporter importer, string path, IProgress<WorkProgress> progress)
    {
        Book book;
        BookLoadMap loading = new(progress);
        try
        {
            Core.BookManipulation.WellFormedResult? invalid = await Task.Run(() => importer.CheckValidToLoad()).ConfigureAwait(true);
            if (invalid is { IsWellFormed: false } err)
            {
                return (null, Strings.Format("File_NotWellFormedOnOpen", err.Line, err.Message));
            }

            book = await Task.Run(() => importer.GetBook(progress: loading)).ConfigureAwait(true);
        }
        catch (EpubLoadException ex)
        {
            return (null, Strings.Format("File_CannotLoadEpub", path, ex.Message));
        }
        catch (IOException ex)
        {
            return (null, Strings.Format("File_IoError", ex.Message));
        }

        // Only an importer that reports (EPUB) has phases; the others keep the one message of the window.
        if (loading.Reported)
        {
            progress.Report(new WorkProgress(null, null, BookLoadMap.Phase(LoadPhaseCount, Strings.Get("BookLoad_Phase_Displaying"))));
        }

        _workspace.ApplyBook(book, path);
        SetCurrentFile(path);
        _saveACopyFilename = string.Empty;
        _settings.LastFolderOpen = Path.GetDirectoryName(Path.GetFullPath(path)) ?? string.Empty;
        AddRecentFile(path);
        _settings.Save();
        _logger.LogInformation("Loaded file {Path} ({Warnings} warnings)", path, book.LoadWarnings.Count);
        return (book, null);
    }

    // What the export reports as what the progress window shows: the files copied and the entries written are counted in a phase each;
    // the preparation before them has only a bar that works.
    private sealed class BookSaveMap : IProgress<BookSaveProgress>
    {
        private readonly IProgress<WorkProgress> _target;

        public BookSaveMap(IProgress<WorkProgress> target) => _target = target;

        public void Report(BookSaveProgress value)
        {
            PhaseInfo phase = new((int)value.Stage + 1, Enum.GetValues<BookSaveStage>().Length, Strings.Get("BookSave_Phase_" + value.Stage));
            _target.Report(WorkProgress.InPhase(phase, Strings.Format("Progress_Detail_File", value.Done, value.Total), value.Done, value.Total));
        }
    }

    // The phases of loading an EPUB: the stages of the importer and, after them, showing the book (swapping it into the workspace).
    private static int LoadPhaseCount => Enum.GetValues<BookLoadStage>().Length + 1;

    // What the importer reports as what the progress window shows: the entries unpacked and the files read and checked are counted
    // in a phase each; the rest (fonts, navigation) has only a bar that works.
    private sealed class BookLoadMap : IProgress<BookLoadProgress>
    {
        private readonly IProgress<WorkProgress> _target;

        public BookLoadMap(IProgress<WorkProgress> target) => _target = target;

        /// <summary>Whether the importer reported at all.</summary>
        public bool Reported { get; private set; }

        public static PhaseInfo Phase(int index, string name) => new(index, LoadPhaseCount, name);

        public void Report(BookLoadProgress value)
        {
            Reported = true;
            PhaseInfo phase = Phase((int)value.Stage + 1, Strings.Get("BookLoad_Phase_" + value.Stage));
            _target.Report(value.Stage == BookLoadStage.Finishing
                ? new WorkProgress(null, null, phase)
                : WorkProgress.InPhase(phase, Strings.Format("Progress_Detail_File", value.Done, value.Total), value.Done, value.Total));
        }
    }

    // ---------------------------------------------------------------- Save --- //

    /// <summary>File → Save. Returns <c>true</c> on success.</summary>
    public Task<bool> SaveAsync()
    {
        if (!HasSavedFile)
        {
            return SaveAsAsync();
        }

        return SaveFile(_currentFilePath, updateCurrentFilename: true);
    }

    /// <summary>File → Save As.</summary>
    public async Task<bool> SaveAsAsync()
    {
        string suggested = BuildSuggestedSavePath();
        string? filename = await _prompts.AskSavePathAsync(suggested).ConfigureAwait(true);

        if (string.IsNullOrEmpty(filename))
        {
            return false;
        }

        if (Path.GetExtension(filename).Length == 0)
        {
            filename += ".epub";
        }

        _settings.LastFolderOpen = Path.GetDirectoryName(Path.GetFullPath(filename)) ?? string.Empty;

        bool ok = await SaveFile(filename, updateCurrentFilename: true).ConfigureAwait(true);
        _settings.Save();
        return ok;
    }

    /// <summary>File → Save A Copy (saves without changing the current path).</summary>
    public async Task<bool> SaveACopyAsync()
    {
        string basePath = _currentFilePath.Length == 0 ? DefaultFileName : _currentFilePath;
        string suggested = _saveACopyFilename.Length > 0
            ? _saveACopyFilename
            : Path.Combine(
                FolderOrHome(),
                Path.GetFileNameWithoutExtension(basePath) + "_copy.epub");

        string? filename = await _prompts.AskSavePathAsync(suggested).ConfigureAwait(true);
        if (string.IsNullOrEmpty(filename))
        {
            return false;
        }

        if (Path.GetExtension(filename).Length == 0)
        {
            filename += ".epub";
        }

        _saveACopyFilename = filename;
        return await SaveFile(filename, updateCurrentFilename: false).ConfigureAwait(true);
    }

    /// <summary>
    /// The unsaved-changes guard. Returns <c>true</c> when it is OK to continue (saved / discarded),
    /// <c>false</c> when the user cancelled.
    /// </summary>
    public async Task<bool> MaybeSaveDialogSaysProceedAsync()
    {
        if (!_workspace.HasUnsavedChanges)
        {
            return true;
        }

        SaveChangesChoice choice = await _prompts.AskSaveChangesAsync().ConfigureAwait(true);
        return choice switch
        {
            SaveChangesChoice.Save => await SaveAsync().ConfigureAwait(true),
            SaveChangesChoice.Cancel => false,
            _ => true,
        };
    }

    // -------------------------------------------------------- Recent Files --- //

    /// <summary>Puts the path at the top of the "Recent Files" list (no duplicates, with a limit).</summary>
    public void AddRecentFile(string path)
    {
        ArgumentException.ThrowIfNullOrEmpty(path);
        string full = Path.GetFullPath(path);

        List<string> list = _settings.RecentFiles
            .Where(p => !string.Equals(p, full, StringComparison.OrdinalIgnoreCase))
            .ToList();
        list.Insert(0, full);
        if (list.Count > MaxRecentFiles)
        {
            list.RemoveRange(MaxRecentFiles, list.Count - MaxRecentFiles);
        }

        _settings.RecentFiles = list;
        RecentFilesChanged?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>Removes the path from the "Recent Files" list.</summary>
    public void RemoveRecentFile(string path)
    {
        List<string> list = _settings.RecentFiles
            .Where(p => !string.Equals(p, path, StringComparison.OrdinalIgnoreCase)
                        && !string.Equals(p, Path.GetFullPath(path), StringComparison.OrdinalIgnoreCase))
            .ToList();
        _settings.RecentFiles = list;
        _settings.Save();
        RecentFilesChanged?.Invoke(this, EventArgs.Empty);
    }

    // -------------------------------------------------------------- helpers --- //

    private async Task<bool> SaveFile(string fullFilePath, bool updateCurrentFilename)
    {
        long start = System.Diagnostics.Stopwatch.GetTimestamp();
        bool saved = await SaveFileCore(fullFilePath, updateCurrentFilename).ConfigureAwait(true);
        DebugLog.Write(
            "File",
            $"{(updateCurrentFilename ? "save" : "save a copy")} {DebugLog.MaskPath(fullFilePath)} → {(saved ? "ok" : "failed")} ({DebugLog.Elapsed(start)})");
        return saved;
    }

    private async Task<bool> SaveFileCore(string fullFilePath, bool updateCurrentFilename)
    {
        Book? book = _workspace.CurrentBook;
        if (book is null)
        {
            return false;
        }

        if (!IsEpubExtension(fullFilePath))
        {
            _ = _prompts.ShowErrorAsync(
                "Signet",
                Strings.Format("File_CannotSaveType", Path.GetExtension(fullFilePath).TrimStart('.')));
            return false;
        }

        try
        {
            _workspace.SaveOpenTabs();
            bool stamped = StampEditionPage(book);
            // Writing the archive of a big book takes a while: off the UI thread, under a progress window.
            if (!await _workspace.RunBookWorkAsync(
                    Strings.Get("File_SavingTitle"),
                    progress => new ExportEpub(book).WriteBook(fullFilePath, progress: new BookSaveMap(progress)))
                    .ConfigureAwait(true))
            {
                return false;
            }

            if (stamped)
            {
                _workspace.RefreshAfterBookEdit();
            }

            if (updateCurrentFilename)
            {
                book.Modified = false;
                SetCurrentFile(fullFilePath);
                MoveLock(fullFilePath);
                AddRecentFile(fullFilePath);
            }

            _logger.LogInformation("Saved EPUB {Path}", fullFilePath);
            return true;
        }
        catch (Exception ex) when (ex is IOException or EpubExportException or UnauthorizedAccessException)
        {
            _ = _prompts.ShowErrorAsync("Signet", Strings.Format("File_CannotSave", fullFilePath, ex.Message));
            _logger.LogError(ex, "Error saving EPUB {Path}", fullFilePath);
            return false;
        }
    }

    // Preferences → Edition page: the page is created or refreshed once per session — right before the first save after
    // the book was opened or created (also "Save a Copy"); later saves leave it as it is, so the revision counts editing
    // sessions, not saves. A failure does not stop the save — the book is saved without the refreshed page (and the next
    // save tries again).
    private bool StampEditionPage(Book book)
    {
        EditionPageSettings settings = _settings.EditionPage;
        if (!settings.Enabled || ReferenceEquals(book, _editionStampedBook))
        {
            return false;
        }

        try
        {
            EditionStampResult result = EditionPage.Stamp(
                book, settings, DateTime.UtcNow, CultureInfo.CurrentUICulture.TwoLetterISOLanguageName, AppVersion.Text);
            DebugLog.Write("File", $"edition page {(result.Created ? "created" : "updated")}, revision {result.Revision}");
            _editionStampedBook = book;
            return true;
        }
        catch (Exception ex) when (ex is IOException or InvalidOperationException or ArgumentException or UnauthorizedAccessException)
        {
            _logger.LogError(ex, "Error writing the edition page");
            _ = _prompts.ShowErrorAsync("Signet", Strings.Format("File_EditionPageFailed", ex.Message));
            return false;
        }
    }

    // The book now lives in another file (Save As, or the first save of a new book): the lock follows it. A file another
    // instance has open too stays without a lock.
    private void MoveLock(string path)
    {
        if (_locks is null || (_bookLock is not null && _bookLock.Key == BookFileLocks.KeyOf(path)))
        {
            return;
        }

        BookFileLock? next = _locks.TryAcquire(path);
        _bookLock?.Dispose();
        _bookLock = next;
    }

    private void ReleaseLock()
    {
        _bookLock?.Dispose();
        _bookLock = null;
    }

    private void LoadFreshBook(Book book)
    {
        DebugLog.Write("File", "new book");
        _workspace.ApplyBook(book, null);
        ReleaseLock();
        _currentFilePath = string.Empty;
        _saveACopyFilename = string.Empty;
        CurrentFileChanged?.Invoke(this, EventArgs.Empty);
    }

    private void SetCurrentFile(string path)
    {
        _currentFilePath = Path.GetFullPath(path);
        CurrentFileChanged?.Invoke(this, EventArgs.Empty);
    }

    private string ResolveVersion(string? version)
    {
        string v = (version ?? string.Empty).Trim();
        if (v.Length > 0)
        {
            return v;
        }

        string fromSettings = (_settings.DefaultVersion ?? string.Empty).Trim();
        return fromSettings.Length > 0 ? fromSettings : "2.0";
    }

    private string BuildSuggestedSavePath()
    {
        string folder = FolderOrHome();
        Book? book = _workspace.CurrentBook;
        string title = book is null ? string.Empty : CleanFileName(book.GetOpf().GetPrimaryBookTitle());

        string name;
        if ((CurrentFileName == DefaultFileName) && title.Length > 0 && title[0] != '[')
        {
            name = title + ".epub";
        }
        else if (_currentFilePath.Length > 0)
        {
            name = Path.GetFileNameWithoutExtension(_currentFilePath) + ".epub";
        }
        else
        {
            name = DefaultFileName;
        }

        return Path.Combine(folder, name);
    }

    private string FolderOrHome()
    {
        string folder = _settings.LastFolderOpen;
        return folder.Length > 0 && Directory.Exists(folder)
            ? folder
            : Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
    }

    private static bool IsEpubExtension(string path) =>
        string.Equals(Path.GetExtension(path), ".epub", StringComparison.OrdinalIgnoreCase);

    private static string CleanFileName(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return string.Empty;
        }

        char[] invalid = Path.GetInvalidFileNameChars();
        string cleaned = new(value.Select(c => Array.IndexOf(invalid, c) >= 0 ? '_' : c).ToArray());
        return cleaned.Trim();
    }
}
