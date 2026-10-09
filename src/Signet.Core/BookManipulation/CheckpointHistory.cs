using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using SysPath = System.IO.Path;
using Signet.Core.Diff;
using Signet.Core.Localization;

namespace Signet.Core.BookManipulation;

/// <summary>
/// A single book state in <see cref="CheckpointHistory"/>: a folder with the complete set of the publication's files
/// plus a description (the checkpoint name).
/// </summary>
public sealed class CheckpointState
{
    internal CheckpointState(TempFolder folder)
    {
        Folder = folder;
    }

    /// <summary>The state identifier (stable for the whole lifetime of the state).</summary>
    public Ulid Id { get; } = Ulid.NewUlid();

    /// <summary>
    /// The state description — the checkpoint name given when it was created (e.g. "Before: Delete
    /// files"); <c>null</c> for a state without a name.
    /// </summary>
    public string? Message { get; internal set; }

    /// <summary>The full path of the folder holding this state's files.</summary>
    public string FolderPath => Folder.Path;

    internal TempFolder Folder { get; }

    // The previous description, restored by CheckpointHistory.Rewind.
    internal string? RewindMessage { get; set; }

    // Bookpaths of the files edited in the editors between the previous state and this one (for the live state:
    // since the previous state) — the edits that moving back to the previous state discards.
    internal HashSet<string> EditedBookPaths { get; } = new(StringComparer.Ordinal);
}

/// <summary>
/// The session history of whole-book checkpoints.
/// </summary>
/// <remarks>
/// <para>
/// The history is a list of states and a <see cref="Position"/> pointer. Each state is a folder with the
/// publication's files; the history owns all of these folders (it deletes them when truncating,
/// when the limit is exceeded and in <see cref="Dispose"/>). The current <see cref="Book"/> works
/// directly in the <see cref="Current"/> state's folder, but does not own it.
/// </para>
/// <para>
/// After moving to another state, further editing changes that state's folder, and the
/// "after" states stay available (<see cref="Redo"/>) until a new checkpoint is created —
/// then they are cut off. When a checkpoint is created, a clone of the folder
/// becomes the frozen state while editing continues in the existing folder, so the book does not
/// have to be reloaded. The copy is full (no hardlinks), because resource writes
/// overwrite files in place and would change the snapshot too.
/// </para>
/// </remarks>
public sealed class CheckpointHistory : IDisposable
{
    /// <summary>The default limit on the number of states.</summary>
    public const int DefaultMaxStates = 100;

    private readonly List<CheckpointState> _states = new();
    private readonly int _maxStates;

    // Bookpaths of the files edited in the editors since the current state was reached by a move — the edits
    // that moving forward (Redo) discards.
    private readonly HashSet<string> _editedSinceMove = new(StringComparer.Ordinal);
    private bool _canRewind;
    private bool _disposed;

    /// <summary>Creates an empty history.</summary>
    /// <param name="maxStates">The maximum number of states; the oldest ones over the limit are deleted.</param>
    public CheckpointHistory(int maxStates = DefaultMaxStates)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(maxStates, 2);
        _maxStates = maxStates;
    }

    /// <summary>Raised after every change to the list of states or to <see cref="Position"/>.</summary>
    public event EventHandler? Changed;

    /// <summary>The states, oldest first.</summary>
    public IReadOnlyList<CheckpointState> States => _states;

    /// <summary>The index of the current state in <see cref="States"/>.</summary>
    public int Position { get; private set; }

    /// <summary>Whether the history is tracking a book (after <see cref="Open"/>, before <see cref="Clear"/>).</summary>
    public bool IsOpen => _states.Count > 0;

    /// <summary>The current state — the one in whose folder the current book works.</summary>
    public CheckpointState Current =>
        IsOpen ? _states[Position] : throw new InvalidOperationException(CoreStrings.Get("Error_CheckpointHistoryEmpty"));

    /// <summary>Whether it is possible to go back to the state before the current one.</summary>
    public bool CanUndo => IsOpen && Position > 0;

    /// <summary>Whether it is possible to move to the state after the current one.</summary>
    public bool CanRedo => IsOpen && Position < _states.Count - 1;

    /// <summary>The description of the state <see cref="Undo"/> leads to (<c>null</c> when there is none or it has no name).</summary>
    public string? UndoMessage => CanUndo ? _states[Position - 1].Message : null;

    /// <summary>The description of the state <see cref="Redo"/> leads to (<c>null</c> when there is none or it has no name).</summary>
    public string? RedoMessage => CanRedo ? _states[Position + 1].Message : null;

    /// <summary>
    /// Starts the history for a freshly loaded book: deletes the previous
    /// states and takes over <paramref name="book"/>'s working folder as the only, current state.
    /// </summary>
    public void Open(Book book)
    {
        ArgumentNullException.ThrowIfNull(book);
        ObjectDisposedException.ThrowIf(_disposed, this);

        TempFolder folder = book.GetFolderKeeper().ReleaseTempFolderOwnership();
        DisposeStates(_states);
        _states.Clear();
        _states.Add(new CheckpointState(folder));
        Position = 0;
        _editedSinceMove.Clear();
        _canRewind = false;
        OnChanged();
    }

    /// <summary>
    /// Records that the file <paramref name="bookPath"/> was edited in an editor (the text of a tab written to the
    /// resource) — used by <see cref="UndoImpact"/> / <see cref="RedoImpact"/> to tell which disappearing files
    /// carry edits that would be lost. Does nothing when the history is not open.
    /// </summary>
    public void MarkEdited(string bookPath)
    {
        ArgumentNullException.ThrowIfNull(bookPath);
        if (!IsOpen)
        {
            return;
        }

        Current.EditedBookPaths.Add(bookPath);
        _editedSinceMove.Add(bookPath);
    }

    /// <summary>
    /// Creates a checkpoint of the book's current state: saves
    /// the book to disk, copies its folder as a frozen state described by <paramref name="message"/>,
    /// cuts off the "after" states and enforces the limit. Editing continues in the existing folder.
    /// </summary>
    /// <param name="liveBook">The current book (working in the <see cref="Current"/> folder).</param>
    /// <param name="message">The checkpoint name.</param>
    public void AddCheckpoint(Book liveBook, string message)
    {
        ArgumentNullException.ThrowIfNull(message);
        CheckpointState live = RequireLive(liveBook);

        Freeze(liveBook);
        TempFolder snapshot = new();
        try
        {
            CopyStateFolder(live.FolderPath, snapshot.Path);
        }
        catch
        {
            snapshot.Dispose();
            throw;
        }

        Truncate();

        live.RewindMessage = live.Message;
        live.Message = null;
        CheckpointState frozen = new(snapshot) { Message = message };
        frozen.EditedBookPaths.UnionWith(live.EditedBookPaths);
        live.EditedBookPaths.Clear();
        _states.Insert(Position, frozen);
        Position++;

        if (_states.Count > _maxStates)
        {
            int excess = _states.Count - _maxStates;
            DisposeStates(_states.GetRange(0, excess));
            _states.RemoveRange(0, excess);
            Position -= excess;
        }

        _canRewind = true;
        OnChanged();
    }

    /// <summary>
    /// Withdraws a checkpoint created just before an operation that was cancelled or changed
    /// nothing. Works only directly after
    /// <see cref="AddCheckpoint"/> (with no moves between states in the meantime); otherwise does nothing.
    /// </summary>
    /// <remarks>
    /// The current book is left unchanged — call this only when the operation did not change it
    /// (the current state's folder is the one from before the checkpoint, so partial changes are not undone).
    /// </remarks>
    /// <returns><c>true</c> when the checkpoint was withdrawn.</returns>
    public bool Rewind()
    {
        if (!_canRewind || Position == 0 || Position != _states.Count - 1)
        {
            return false;
        }

        CheckpointState frozen = _states[Position - 1];
        _states.RemoveAt(Position - 1);
        frozen.Folder.Dispose();
        Position--;

        CheckpointState live = _states[Position];
        live.Message = live.RewindMessage;
        live.RewindMessage = null;
        live.EditedBookPaths.UnionWith(frozen.EditedBookPaths);
        _canRewind = false;
        OnChanged();
        return true;
    }

    /// <summary>
    /// Moves to the state before the current one. The current book is
    /// first saved to its state's folder (which stays available through <see cref="Redo"/>).
    /// </summary>
    /// <returns>The book loaded from the new current state's folder or <c>null</c> when there is nowhere to go back to.</returns>
    /// <exception cref="EpubLoadException">The state could not be loaded; <see cref="Position"/> is unchanged.</exception>
    public Book? Undo(Book liveBook) => CanUndo ? MoveTo(liveBook, Position - 1) : null;

    /// <summary>Moves to the state after the current one; like <see cref="Undo"/>.</summary>
    public Book? Redo(Book liveBook) => CanRedo ? MoveTo(liveBook, Position + 1) : null;

    /// <summary>
    /// Moves to the given state; like
    /// <see cref="Undo"/>. For the current state it returns <c>null</c>.
    /// </summary>
    public Book? RevertTo(Book liveBook, CheckpointState target)
    {
        ArgumentNullException.ThrowIfNull(target);
        int index = _states.IndexOf(target);
        if (index < 0)
        {
            throw new ArgumentException(CoreStrings.Get("Error_CheckpointStateNotInHistory"), nameof(target));
        }

        return index == Position ? null : MoveTo(liveBook, index);
    }

    /// <summary>
    /// The files that <see cref="Undo"/> would remove, bring back or rename (files whose content only changes are
    /// not listed). A removed file is flagged when it was edited in an editor since the previous state — those
    /// edits are lost. Saves the current book to its folder first (as <see cref="Undo"/> does).
    /// </summary>
    /// <returns>The affected files sorted by path; empty when <see cref="CanUndo"/> is <c>false</c>.</returns>
    public IReadOnlyList<CheckpointFileImpact> UndoImpact(Book liveBook) =>
        CanUndo ? ImpactOfMoveTo(liveBook, Position - 1, Current.EditedBookPaths) : Array.Empty<CheckpointFileImpact>();

    /// <summary>
    /// The files that <see cref="Redo"/> would remove, bring back or rename; like <see cref="UndoImpact"/>, but a
    /// removed file is flagged when it was edited since the current state was reached.
    /// </summary>
    /// <returns>The affected files sorted by path; empty when <see cref="CanRedo"/> is <c>false</c>.</returns>
    public IReadOnlyList<CheckpointFileImpact> RedoImpact(Book liveBook) =>
        CanRedo ? ImpactOfMoveTo(liveBook, Position + 1, _editedSinceMove) : Array.Empty<CheckpointFileImpact>();

    /// <summary>
    /// Saves the book to its working folder in full (resources + <see cref="BookStateFile"/>)
    /// — so that the folder is enough to load the book again.
    /// </summary>
    public static void Freeze(Book book)
    {
        ArgumentNullException.ThrowIfNull(book);
        book.SaveAllResourcesToDisk();
        BookStateFile.Write(book);
    }

    /// <summary>Deletes all states and their folders (e.g. when the book is closed).</summary>
    public void Clear()
    {
        if (_states.Count == 0)
        {
            return;
        }

        DisposeStates(_states);
        _states.Clear();
        Position = 0;
        _editedSinceMove.Clear();
        _canRewind = false;
        OnChanged();
    }

    /// <inheritdoc />
    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        DisposeStates(_states);
        _states.Clear();
        Position = 0;
        _disposed = true;
    }

    private Book MoveTo(Book liveBook, int index)
    {
        RequireLive(liveBook);
        Freeze(liveBook);

        Book loaded = ImportEpub.LoadWorkingFolder(_states[index].Folder);
        Position = index;
        _editedSinceMove.Clear();
        _canRewind = false;
        OnChanged();
        return loaded;
    }

    // The current state is on the right side of the comparison: its added files disappear after the move, its removed
    // ones come back, and a rename is undone.
    private List<CheckpointFileImpact> ImpactOfMoveTo(Book liveBook, int index, HashSet<string> edited)
    {
        RequireLive(liveBook);
        Freeze(liveBook);

        return BookComparer.CompareFileSets(_states[index].FolderPath, Current.FolderPath)
            .Select(diff => diff.Change switch
            {
                BookFileChange.Added => new CheckpointFileImpact(
                    CheckpointFileChange.Removed, diff.RightPath!, null, edited.Contains(diff.RightPath!)),
                BookFileChange.Removed => new CheckpointFileImpact(CheckpointFileChange.Restored, diff.LeftPath!, null, false),
                _ => new CheckpointFileImpact(CheckpointFileChange.Renamed, diff.RightPath!, diff.LeftPath, false),
            })
            .ToList();
    }

    private CheckpointState RequireLive(Book liveBook)
    {
        ArgumentNullException.ThrowIfNull(liveBook);
        ObjectDisposedException.ThrowIf(_disposed, this);

        CheckpointState live = Current;
        if (!string.Equals(
                SysPath.GetFullPath(liveBook.GetFolderKeeper().MainFolderPath),
                SysPath.GetFullPath(live.FolderPath),
                StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException(CoreStrings.Get("Error_CheckpointWrongFolder"));
        }

        return live;
    }

    // Cuts off the states after the current one.
    private void Truncate()
    {
        int from = Position + 1;
        if (from >= _states.Count)
        {
            return;
        }

        DisposeStates(_states.GetRange(from, _states.Count - from));
        _states.RemoveRange(from, _states.Count - from);
    }

    private static void DisposeStates(IEnumerable<CheckpointState> states)
    {
        foreach (CheckpointState state in states)
        {
            state.Folder.Dispose();
        }
    }

    // A copy of the state folder without the TempFolder lock file (each folder has its own).
    private static void CopyStateFolder(string sourceRoot, string destinationRoot)
    {
        foreach (string directory in Directory.EnumerateDirectories(sourceRoot, "*", SearchOption.AllDirectories))
        {
            Directory.CreateDirectory(SysPath.Combine(destinationRoot, SysPath.GetRelativePath(sourceRoot, directory)));
        }

        foreach (string file in Directory.EnumerateFiles(sourceRoot, "*", SearchOption.AllDirectories))
        {
            string relative = SysPath.GetRelativePath(sourceRoot, file);
            if (string.Equals(relative, TempFolder.LockFileName, StringComparison.Ordinal))
            {
                continue;
            }

            File.Copy(file, SysPath.Combine(destinationRoot, relative), overwrite: true);
        }
    }

    private void OnChanged() => Changed?.Invoke(this, EventArgs.Empty);
}
