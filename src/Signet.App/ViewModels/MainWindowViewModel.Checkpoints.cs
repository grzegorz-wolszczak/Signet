using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.IO;
using Microsoft.Extensions.Logging;
using Signet.App.Actions;
using Signet.App.Docking;
using Signet.App.Resources;
using Signet.App.Tabs;
using Signet.Core;
using Signet.Core.BookManipulation;
using Signet.Core.Diff;
using Signet.Core.Resources;

namespace Signet.App.ViewModels;

/// <summary>
/// Book checkpoints: creating a checkpoint, automatic "Before: …" savepoints and rewinding
/// them, undo/redo across checkpoints, reverting to a chosen state, and updating the
/// checkpoint-related actions.
/// </summary>
[SuppressMessage(
    "Reliability",
    "CA1001:Types that own disposable fields should be disposable",
    Justification = "The checkpoint history is disposed in PersistState — the window-closing path, like the book and the tabs.")]
public sealed partial class MainWindowViewModel
{
    private readonly CheckpointHistory _checkpoints = new();
    private CheckpointsViewModel? _checkpointsPanel;

    /// <summary>Raised after "Create Checkpoint…" — the view asks for a name and calls <see cref="CreateCheckpoint"/>.</summary>
    public event EventHandler? CreateCheckpointRequested;

    /// <summary>
    /// Raised after "Compare" in the checkpoints panel — the view shows the diff window.
    /// </summary>
    public event EventHandler<DiffViewModel>? CompareCheckpointRequested;

    /// <summary>Checkpoint history of the current book.</summary>
    public CheckpointHistory CheckpointHistory => _checkpoints;

    /// <summary>View model of the "Checkpoints" panel.</summary>
    public CheckpointsViewModel CheckpointsPanel =>
        _checkpointsPanel ?? throw new InvalidOperationException("The checkpoints panel is not initialized.");

    /// <summary>
    /// Creates a manual checkpoint with the book's current state.
    /// </summary>
    /// <param name="name">Name given by the user (empty — an unnamed checkpoint).</param>
    /// <returns><c>true</c> when the checkpoint was created.</returns>
    public bool CreateCheckpoint(string name)
    {
        ArgumentNullException.ThrowIfNull(name);
        if (!AddCheckpoint(name.Trim()))
        {
            return false;
        }

        _statusBar.ShowMessage(
            Strings.Format("Status_CheckpointCreated", CheckpointsViewModel.LabelFor(_checkpoints.States[_checkpoints.Position - 1], isCurrent: false)),
            TimeSpan.FromSeconds(3));
        return true;
    }

    /// <summary>
    /// An automatic "Before: <paramref name="operation"/>" checkpoint created right before
    /// an operation that changes the book.
    /// </summary>
    /// <param name="operation">Operation name (e.g. the action label without mnemonics).</param>
    /// <returns><c>true</c> when the checkpoint was created (<see cref="RewindCheckpoint"/> can then roll it back).</returns>
    public bool AddCheckpointBefore(string operation)
    {
        ArgumentNullException.ThrowIfNull(operation);
        return AddCheckpoint(Strings.Format("Checkpoint_Before", operation));
    }

    /// <summary>
    /// Rolls back the checkpoint created right before an operation that was cancelled or
    /// changed nothing.
    /// </summary>
    public void RewindCheckpoint() => _checkpoints.Rewind();

    /// <summary>Restores the state before the current one.</summary>
    public void RevertToBeforeCheckpoint() => MoveToCheckpoint(_checkpoints.Undo);

    /// <summary>Restores the state after the current one.</summary>
    public void RevertToAfterCheckpoint() => MoveToCheckpoint(_checkpoints.Redo);

    /// <summary>Restores the given state.</summary>
    public void RevertToCheckpoint(CheckpointState state)
    {
        ArgumentNullException.ThrowIfNull(state);
        MoveToCheckpoint(live => _checkpoints.RevertTo(live, state));
    }

    /// <inheritdoc/>
    bool IMultiFileSearchHost.CheckpointBefore(string operation) => AddCheckpointBefore(operation);

    /// <inheritdoc/>
    void IMultiFileSearchHost.RewindCheckpoint() => RewindCheckpoint();

    // An automatic checkpoint named with the menu action's label (without the mnemonic and the ellipsis).
    private bool CheckpointBeforeAction(string actionId) => AddCheckpointBefore(ActionLabel(actionId));

    private static string ActionLabel(string actionId) =>
        Strings.Get("Action_" + actionId.Replace('.', '_'))
            .Replace("&", string.Empty, StringComparison.Ordinal)
            .TrimEnd('.', '…')
            .Trim();

    /// <summary>
    /// Compares the checkpoint state (left side) with the current one (right side) and raises
    /// <see cref="CompareCheckpointRequested"/> with the diff window model.
    /// </summary>
    /// <returns>The window model, or <c>null</c> when there is no book or the comparison failed.</returns>
    public DiffViewModel? CompareWithCheckpoint(CheckpointState state)
    {
        ArgumentNullException.ThrowIfNull(state);
        if (_currentBook is not { } book || !_checkpoints.IsOpen)
        {
            return null;
        }

        // Commit all editors first: we compare what the user sees in the editors.
        _tabManager.SaveAllTabs();
        IReadOnlyList<BookFileDiff> files;
        try
        {
            CheckpointHistory.Freeze(book);
            files = BookComparer.Compare(state.FolderPath, _checkpoints.Current.FolderPath);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            _logger.LogError(ex, "Failed to compare the checkpoint");
            _statusBar.ShowMessage(Strings.Format("Status_CheckpointError", ex.Message), TimeSpan.FromSeconds(6));
            return null;
        }

        DiffViewModel diff = new(
            files,
            CheckpointsViewModel.LabelFor(state, isCurrent: false),
            Strings.Get("Checkpoint_CurrentState"));
        diff.OpenInEditorRequested += (_, target) => OpenBookPathAtLine(target.BookPath, target.Line);
        CompareCheckpointRequested?.Invoke(this, diff);
        return diff;
    }

    // Double click in the diff window: the current book's file in Code View at the line.
    private void OpenBookPathAtLine(string bookPath, int line)
    {
        if (_currentBook?.GetFolderKeeper().GetResourceByBookPathNoThrow(bookPath) is not { } resource)
        {
            return;
        }

        _tabManager.OpenResources(new Resource[] { resource });
        ActiveCodeTab?.GoToLine(line);
    }

    // Called from the constructor BEFORE the dock layout is created (the panel is created in CreateLayout).
    private void InitCheckpoints()
    {
        _checkpointsPanel = new CheckpointsViewModel(_checkpoints);
        BookBrowser.CheckpointBefore = AddCheckpointBefore;
        BookBrowser.RewindCheckpoint = RewindCheckpoint;
        _checkpointsPanel.RevertRequested += (_, state) => RevertToCheckpoint(state);
        _checkpointsPanel.CompareRequested += (_, state) => CompareWithCheckpoint(state);
        _dockFactory.Checkpoints = _checkpointsPanel;
        _checkpoints.Changed += (_, _) => RefreshCheckpointActions();
    }

    private void WireCheckpointActions()
    {
        _actions.SetHandler(AppActionIds.CreateCheckpoint, () =>
        {
            if (_currentBook is not null)
            {
                CreateCheckpointRequested?.Invoke(this, EventArgs.Empty);
            }
        });
        _actions.SetHandler(AppActionIds.RevertToBefore, RevertToBeforeCheckpoint);
        _actions.SetHandler(AppActionIds.RevertToAfter, RevertToAfterCheckpoint);
        _actions.SetHandler(AppActionIds.ToggleCheckpoints, () => _dockFactory.ToggleTool(DockableIds.Checkpoints));
        RefreshCheckpointActions();
    }

    // Opens the history and adds the "Start of editing session" checkpoint.
    private void StartCheckpointHistory(Book book)
    {
        try
        {
            _checkpoints.Open(book);
            _checkpoints.AddCheckpoint(book, Strings.Get("Checkpoint_StartOfSession"));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidOperationException)
        {
            _logger.LogError(ex, "Failed to start the checkpoint history");
            _statusBar.ShowMessage(Strings.Format("Status_CheckpointError", ex.Message), TimeSpan.FromSeconds(6));
        }

        RefreshCheckpointActions();
    }

    private bool AddCheckpoint(string message)
    {
        if (_currentBook is not { } book || !_checkpoints.IsOpen)
        {
            return false;
        }

        // Commit all editors: unsaved tab content goes into the resources.
        _tabManager.SaveAllTabs();
        try
        {
            _checkpoints.AddCheckpoint(book, message);
            return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidOperationException)
        {
            _logger.LogError(ex, "Failed to create a checkpoint");
            _statusBar.ShowMessage(Strings.Format("Status_CheckpointError", ex.Message), TimeSpan.FromSeconds(6));
            return false;
        }
    }

    private void MoveToCheckpoint(Func<Book, Book?> move)
    {
        if (_currentBook is not { } live || !_checkpoints.IsOpen)
        {
            return;
        }

        _tabManager.SaveAllTabs();
        TabSnapshot tabs = _tabManager.CaptureSnapshot();

        Book? loaded;
        try
        {
            loaded = move(live);
        }
        catch (Exception ex) when (ex is EpubLoadException or IOException or UnauthorizedAccessException or InvalidOperationException)
        {
            _logger.LogError(ex, "Failed to restore the checkpoint");
            _statusBar.ShowMessage(Strings.Format("Status_CheckpointError", ex.Message), TimeSpan.FromSeconds(6));
            return;
        }

        if (loaded is null)
        {
            return;
        }

        SwapToCheckpointBook(loaded, tabs);
        CheckpointState current = _checkpoints.Current;
        _statusBar.ShowMessage(
            Strings.Format(
                "Status_CheckpointReverted",
                string.IsNullOrEmpty(current.Message) ? Strings.Get("Checkpoint_Unnamed") : current.Message),
            TimeSpan.FromSeconds(3));
    }

    // Replaces the book in all views while keeping the open tabs (tabs of files that the
    // state does not have disappear) and marking the book as changed.
    private void SwapToCheckpointBook(Book book, TabSnapshot tabs)
    {
        _tabManager.SetBook(null);
        if (_currentBook is not null)
        {
            _currentBook.ModifiedStateChanged -= OnBookModifiedStateChanged;
            _currentBook.Dispose();
        }

        _currentBook = book;
        _currentBook.ModifiedStateChanged += OnBookModifiedStateChanged;

        _tabManager.SetBook(book);
        BookBrowser.SetBook(book);
        _preview.SetBook(book);
        _toc.SetBook(book);

        // BookBrowser.SetBook opens the first HTML file — the tabs are restored exactly from the snapshot.
        if (tabs.Session.OpenBookPaths.Count > 0)
        {
            _tabManager.CloseAllTabs();
            _tabManager.RestoreSnapshot(tabs);
        }

        book.Modified = true;
        RefreshTitle();
    }

    // Updates the "Revert to before/after "…"" actions with the name of the target state.
    private void RefreshCheckpointActions()
    {
        bool hasBook = _currentBook is not null && _checkpoints.IsOpen;
        _actions.SetEnabled(hasBook, AppActionIds.CreateCheckpoint);
        _actions.SetEnabled(hasBook && _checkpoints.CanUndo, AppActionIds.RevertToBefore);
        _actions.SetEnabled(hasBook && _checkpoints.CanRedo, AppActionIds.RevertToAfter);

        if (_actions.Get(AppActionIds.RevertToBefore) is { } before)
        {
            before.Text = hasBook && _checkpoints.CanUndo
                ? EscapeMnemonics(Strings.Format(
                    "Checkpoint_RevertToBeforeNamed",
                    string.IsNullOrEmpty(_checkpoints.UndoMessage) ? "…" : _checkpoints.UndoMessage))
                : AppAction.ConvertMnemonics(before.DefaultText);
        }

        if (_actions.Get(AppActionIds.RevertToAfter) is { } after)
        {
            after.Text = hasBook && _checkpoints.CanRedo
                ? EscapeMnemonics(Strings.Format(
                    "Checkpoint_RevertToAfterNamed",
                    string.IsNullOrEmpty(_checkpoints.RedoMessage) ? Strings.Get("Checkpoint_Unnamed") : _checkpoints.RedoMessage))
                : AppAction.ConvertMnemonics(after.DefaultText);
        }
    }

    // A label with a user-supplied name must not have a mnemonic ("_" in Avalonia).
    private static string EscapeMnemonics(string text) => text.Replace("_", "__", StringComparison.Ordinal);
}
