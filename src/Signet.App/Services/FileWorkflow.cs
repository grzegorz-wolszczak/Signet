using System.Collections.Generic;
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

/// <summary>UI operations needed by the file workflow (dialogs). Implemented by the main window view.</summary>
public interface IFileWorkflowPrompts
{
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
    Task ShowLoadWarningsAsync(string fileName, IReadOnlyList<string> warnings);

    /// <summary>Asks whether to remove a nonexistent file from the "Recent Files" list (OK = remove).</summary>
    Task<bool> ConfirmRemoveMissingRecentAsync(string path);

    /// <summary>"Custom Epub Layout" wizard. Returns a list of book paths, or <c>null</c> when cancelled.</summary>
    Task<IReadOnlyList<string>?> DesignCustomLayoutAsync(string version);
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
}

/// <summary>
/// The File menu workflow: New / Open / Save / Save As / Save A Copy, the "Recent Files" list,
/// and the unsaved-changes guard.
/// </summary>
public sealed class FileWorkflow
{
    /// <summary>Default file name for a new publication.</summary>
    public const string DefaultFileName = "untitled.epub";

    // We store up to the upper bound of the Preferences limit — the menu shows only the first
    // SettingsStore.RecentFilesLimit entries, so lowering the limit does not lose history.
    private const int MaxRecentFiles = SettingsStore.RecentFilesMax;

    private readonly SettingsStore _settings;
    private readonly IFileWorkflowPrompts _prompts;
    private readonly IBookWorkspace _workspace;
    private readonly ILogger _logger;

    private string _currentFilePath = string.Empty;
    private string _saveACopyFilename = string.Empty;

    /// <summary>Creates the workflow.</summary>
    public FileWorkflow(
        SettingsStore settings,
        IFileWorkflowPrompts prompts,
        IBookWorkspace workspace,
        ILogger logger)
    {
        _settings = settings ?? throw new ArgumentNullException(nameof(settings));
        _prompts = prompts ?? throw new ArgumentNullException(nameof(prompts));
        _workspace = workspace ?? throw new ArgumentNullException(nameof(workspace));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

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

        string epubVersion = ResolveVersion(version);
        IReadOnlyList<string> defaultLayout = EmptyEpubLayout.ReadDefault();
        Book book = BookCreator.CreateNewBook(
            epubVersion,
            defaultLayout.Count > 0 ? defaultLayout : null);
        LoadFreshBook(book);
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

    // ---------------------------------------------------------------- Open --- //

    /// <summary>File → Open.</summary>
    public async Task OpenAsync()
    {
        if (!await MaybeSaveDialogSaysProceedAsync().ConfigureAwait(true))
        {
            return;
        }

        string startFolder = _settings.LastFolderOpen;
        if (startFolder.Length == 0 || !Directory.Exists(startFolder))
        {
            startFolder = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        }

        string? path = await _prompts.AskOpenPathAsync(startFolder).ConfigureAwait(true);
        if (string.IsNullOrEmpty(path))
        {
            return;
        }

        await LoadFileAsync(path).ConfigureAwait(true);
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

        Core.BookManipulation.WellFormedResult? invalid = importer.CheckValidToLoad();
        if (invalid is { IsWellFormed: false } err)
        {
            await _prompts.ShowErrorAsync(
                "Signet",
                Strings.Format("File_NotWellFormedOnOpen", err.Line, err.Message)).ConfigureAwait(true);
            return false;
        }

        Book book;
        try
        {
            book = await Task.Run(() => importer.GetBook()).ConfigureAwait(true);
        }
        catch (EpubLoadException ex)
        {
            await _prompts.ShowErrorAsync("Signet", Strings.Format("File_CannotLoadEpub", path, ex.Message)).ConfigureAwait(true);
            return false;
        }
        catch (IOException ex)
        {
            await _prompts.ShowErrorAsync("Signet", Strings.Format("File_IoError", ex.Message)).ConfigureAwait(true);
            return false;
        }

        _workspace.ApplyBook(book, path);
        SetCurrentFile(path);
        _saveACopyFilename = string.Empty;
        _settings.LastFolderOpen = Path.GetDirectoryName(Path.GetFullPath(path)) ?? string.Empty;
        AddRecentFile(path);
        _settings.Save();
        _logger.LogInformation("Loaded file {Path} ({Warnings} warnings)", path, book.LoadWarnings.Count);
        if (book.LoadWarnings.Count > 0)
        {
            await _prompts.ShowLoadWarningsAsync(Path.GetFileName(path), book.LoadWarnings).ConfigureAwait(true);
        }

        return true;
    }

    // ---------------------------------------------------------------- Save --- //

    /// <summary>File → Save. Returns <c>true</c> on success.</summary>
    public Task<bool> SaveAsync()
    {
        if (!HasSavedFile)
        {
            return SaveAsAsync();
        }

        return Task.FromResult(SaveFile(_currentFilePath, updateCurrentFilename: true));
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

        bool ok = SaveFile(filename, updateCurrentFilename: true);
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
        return SaveFile(filename, updateCurrentFilename: false);
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

    private bool SaveFile(string fullFilePath, bool updateCurrentFilename)
    {
        long start = System.Diagnostics.Stopwatch.GetTimestamp();
        bool saved = SaveFileCore(fullFilePath, updateCurrentFilename);
        DebugLog.Write(
            "File",
            $"{(updateCurrentFilename ? "save" : "save a copy")} {DebugLog.MaskPath(fullFilePath)} → {(saved ? "ok" : "failed")} ({DebugLog.Elapsed(start)})");
        return saved;
    }

    private bool SaveFileCore(string fullFilePath, bool updateCurrentFilename)
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
            new ExportEpub(book).WriteBook(fullFilePath);

            if (updateCurrentFilename)
            {
                book.Modified = false;
                SetCurrentFile(fullFilePath);
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

    private void LoadFreshBook(Book book)
    {
        DebugLog.Write("File", "new book");
        _workspace.ApplyBook(book, null);
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
