using System;
using System.IO;
using Avalonia.Threading;
using Microsoft.Extensions.Logging;
using Signet.App.Actions;
using Signet.App.Tabs;
using Signet.Core.BookManipulation;
using Signet.Core.MainUI;
using Signet.Core.Misc;
using Signet.Core.Resources;

namespace Signet.App.ViewModels;

/// <summary>
/// Navigate Back / Forward (modelled on the IntelliJ platform): the places that jumps leave — tab switches, clicks in
/// the editor, Go To Line, Find, links, the panels' jumps — form a history that the two actions walk; places in files
/// that no longer exist are skipped, renamed files are followed. The history of a saved book can be kept between
/// sessions (Preferences → General).
/// </summary>
public sealed partial class MainWindowViewModel
{
    private const string NavigationHistoryFileName = "navigation-history.json";

    private readonly NavigationHistory _navigation = new();
    private NavigationTracker? _navigationTracker;
    private NavigationHistoryStore? _navigationStore;
    private FolderKeeper? _navigationFolderKeeper;

    // The EPUB the history belongs to when there is no FileWorkflow (the path given to LoadBook / ApplyBook).
    private string? _navigationBookPath;

    /// <summary>The Navigate Back / Forward history of the current book.</summary>
    public NavigationHistory NavigationHistory => _navigation;

    /// <summary>
    /// Runs an action once the current user action has finished, returning <c>false</c> when it cannot — the end of a
    /// navigation command (<see cref="NavigationTracker"/>). <c>null</c> (the default) uses the UI dispatcher; tests
    /// set their own.
    /// </summary>
    public Func<Action, bool>? NavigationCommandScheduler { get; set; }

    /// <summary>Navigate Back: returns to the previous place in the history.</summary>
    public void NavigateBack() => Navigate(forward: false);

    /// <summary>Navigate Forward: returns to the place Navigate Back left.</summary>
    public void NavigateForward() => Navigate(forward: true);

    private NavigationTracker Tracker =>
        _navigationTracker ??= new NavigationTracker(_navigation, ScheduleNavigationCommandEnd);

    private void WireNavigationActions()
    {
        _navigationStore = new NavigationHistoryStore(Path.Combine(
            Path.GetDirectoryName(_settings.FilePath) ?? AppDirectories.PrefsDirectory, NavigationHistoryFileName));
        _actions.SetHandler(AppActionIds.NavigateBack, NavigateBack);
        _actions.SetHandler(AppActionIds.NavigateForward, NavigateForward);
        _navigation.Changed += (_, _) => RefreshNavigationActions();
        _tabManager.NavigationStarting += (_, _) => Tracker.NoteNavigation();
        _tabManager.DocumentEdited += OnTabDocumentEdited;
        RefreshNavigationActions();
    }

    private void RefreshNavigationActions()
    {
        _actions.SetEnabled(_navigation.CanGoBack, AppActionIds.NavigateBack);
        _actions.SetEnabled(_navigation.CanGoForward, AppActionIds.NavigateForward);
    }

    private bool ScheduleNavigationCommandEnd(Action endOfCommand)
    {
        if (NavigationCommandScheduler is { } scheduler)
        {
            return scheduler(endOfCommand);
        }

        if (!Dispatcher.UIThread.CheckAccess())
        {
            return false;
        }

        // Background priority: after the input, layout and caret updates of the current user action.
        Dispatcher.UIThread.Post(endOfCommand, DispatcherPriority.Background);
        return true;
    }

    private void Navigate(bool forward)
    {
        if (_currentBook is null)
        {
            return;
        }

        Tracker.Flush();
        NavigationPlace? current = CurrentNavigationPlace();
        NavigationPlace? target = forward
            ? _navigation.Forward(current, NavigationFileExists)
            : _navigation.Back(current, NavigationFileExists);
        if (target is null)
        {
            return;
        }

        using (Tracker.Suspend())
        {
            if (target.HasCaret)
            {
                _tabManager.OpenResourceAtOffset(target.BookPath, target.Offset);
            }
            else if (_currentBook.GetFolderKeeper().GetResourceByBookPathNoThrow(target.BookPath) is { } resource)
            {
                _tabManager.OpenResource(resource);
            }
        }

        Tracker.UpdateCurrentPlace(CurrentNavigationPlace());
    }

    private bool NavigationFileExists(string bookPath) =>
        _currentBook?.GetFolderKeeper().GetResourceByBookPathNoThrow(bookPath) is not null;

    // Where the caret is now: the caret of the active Code View tab, or just the file of another tab.
    private NavigationPlace? CurrentNavigationPlace()
    {
        if (ActiveCodeTab is { } code)
        {
            int offset = Math.Clamp(code.CaretOffset, 0, code.Document.TextLength);
            return new NavigationPlace(code.ResourceBookPath, offset, code.Document.GetLineByOffset(offset).LineNumber, DateTimeOffset.Now);
        }

        return ActiveTab is { } tab ? new NavigationPlace(tab.Resource.BookPath, -1, 0, DateTimeOffset.Now) : null;
    }

    // A tab switch is a jump: called before (the place being left is the current one) and after the switch.
    private void NoteNavigationTabSwitch() => Tracker.NoteNavigation();

    private void UpdateNavigationPlace() => Tracker.UpdateCurrentPlace(CurrentNavigationPlace());

    private void OnTabDocumentEdited(object? sender, TabDocumentEdit e) =>
        _navigation.ApplyTextChange(e.Resource.BookPath, e.Offset, e.Line, e.RemovedText, e.InsertedText);

    private void OnNavigationResourcePathChanged(object? sender, ResourceBookPathChangedEventArgs e)
    {
        _navigation.RenameBookPath(e.OldBookPath, e.Resource.BookPath);
        UpdateNavigationPlace();
    }

    // Follows the FolderKeeper of the current book (renames); called whenever the Book instance changes.
    private void WatchNavigationFolderKeeper()
    {
        if (_navigationFolderKeeper is not null)
        {
            _navigationFolderKeeper.ResourceBookPathChanged -= OnNavigationResourcePathChanged;
        }

        _navigationFolderKeeper = _currentBook?.GetFolderKeeper();
        if (_navigationFolderKeeper is not null)
        {
            _navigationFolderKeeper.ResourceBookPathChanged += OnNavigationResourcePathChanged;
        }
    }

    // The EPUB file of the current book: FileWorkflow follows Save As; before FileWorkflow takes a newly opened
    // file (ApplyBook runs first) it still names the previous one.
    private string? NavigationBookFilePath() =>
        _fileWorkflow?.CurrentFilePath is { Length: > 0 } path ? path : _navigationBookPath;

    // Keeps the history of the current (saved) book when Preferences say so.
    private void SaveNavigationHistory()
    {
        if (_currentBook is null || _navigationStore is null || !_settings.NavigationHistoryRemember
            || NavigationBookFilePath() is not { Length: > 0 } bookFilePath)
        {
            return;
        }

        Tracker.Flush();
        try
        {
            _navigationStore.Save(bookFilePath, _navigation.Capture(), _settings.NavigationHistoryDays);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            _logger.LogWarning(ex, "Failed to save the navigation history");
        }
    }

    // A new book: its kept history (Preferences) or an empty one.
    private void LoadNavigationHistory(string? sourcePath)
    {
        _navigationBookPath = string.IsNullOrEmpty(sourcePath) ? null : sourcePath;
        NavigationHistoryState? kept = _settings.NavigationHistoryRemember && _navigationBookPath is not null
            ? _navigationStore?.Load(_navigationBookPath, _settings.NavigationHistoryDays)
            : null;
        if (kept is not null)
        {
            _navigation.Restore(kept);
        }
        else
        {
            _navigation.Clear();
        }

        WatchNavigationFolderKeeper();
        UpdateNavigationPlace();
    }
}
