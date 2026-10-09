using System;

namespace Signet.Core.MainUI;

/// <summary>
/// Feeds a <see cref="NavigationHistory"/> with the places that jumps leave, the way the IntelliJ platform groups
/// one user action into one command: the first <see cref="NoteNavigation"/> of a command remembers the place the caret
/// is at, later ones in the same command are ignored (opening a tab and then moving its caret is one jump), and at the
/// end of the command the remembered place is recorded when the caret really moved — to another file or line.
/// </summary>
/// <remarks>
/// The host reports the caret through <see cref="UpdateCurrentPlace"/> after every move and tab switch, and calls
/// <see cref="NoteNavigation"/> before a jump moves the caret (for a tab switch: before reporting the new tab's place).
/// The end of a command is signalled by the scheduler given to the constructor (the UI dispatcher); when it cannot
/// schedule (no dispatcher), a pending jump is committed by the next <see cref="NoteNavigation"/> or by
/// <see cref="Flush"/>.
/// </remarks>
public sealed class NavigationTracker
{
    private readonly NavigationHistory _history;
    private readonly Func<Action, bool> _scheduleEndOfCommand;
    private readonly Func<DateTimeOffset> _clock;

    private NavigationPlace? _currentPlace;
    private NavigationPlace? _pendingStart;
    private bool _pending;
    private bool _pendingScheduled;
    private int _suspended;

    /// <summary>Creates a tracker for <paramref name="history"/>.</summary>
    /// <param name="history">The history to record into.</param>
    /// <param name="scheduleEndOfCommand">
    /// Runs the action once the current command (user action) has finished; returns <c>false</c> when it cannot.
    /// </param>
    /// <param name="clock">The current time (for <see cref="NavigationPlace.Time"/>); <see cref="DateTimeOffset.Now"/> by default.</param>
    public NavigationTracker(NavigationHistory history, Func<Action, bool> scheduleEndOfCommand, Func<DateTimeOffset>? clock = null)
    {
        _history = history ?? throw new ArgumentNullException(nameof(history));
        _scheduleEndOfCommand = scheduleEndOfCommand ?? throw new ArgumentNullException(nameof(scheduleEndOfCommand));
        _clock = clock ?? (() => DateTimeOffset.Now);
    }

    /// <summary>The place the caret is at now (as last reported).</summary>
    public NavigationPlace? CurrentPlace => _currentPlace;

    /// <summary>Reports where the caret is now (after a move or a tab switch); <c>null</c> without an open tab.</summary>
    public void UpdateCurrentPlace(NavigationPlace? place) => _currentPlace = place;

    /// <summary>A jump is about to move the caret: starts a navigation command unless one is already running.</summary>
    public void NoteNavigation()
    {
        if (_suspended > 0)
        {
            return;
        }

        if (_pending)
        {
            if (_pendingScheduled)
            {
                return;
            }

            Flush();
        }

        _pending = true;
        _pendingStart = _currentPlace;
        _pendingScheduled = _scheduleEndOfCommand(Commit);
    }

    /// <summary>Commits a pending navigation command now.</summary>
    public void Flush()
    {
        if (_pending)
        {
            Commit();
        }
    }

    /// <summary>
    /// Ignores jumps until the returned scope is disposed — for moves that must not enter the history: Navigate Back /
    /// Forward themselves, loading a book, reverting to a checkpoint.
    /// </summary>
    public IDisposable Suspend()
    {
        Flush();
        _suspended++;
        return new SuspendScope(this);
    }

    private void Commit()
    {
        if (!_pending)
        {
            return;
        }

        NavigationPlace? start = _pendingStart;
        _pending = false;
        _pendingScheduled = false;
        _pendingStart = null;
        if (start is not null && _currentPlace is { } now && HasMoved(start, now))
        {
            _history.Push(start with { Time = _clock() });
        }
    }

    // IntelliJ records a jump only when the caret changed its file or line.
    private static bool HasMoved(NavigationPlace from, NavigationPlace to) =>
        !string.Equals(from.BookPath, to.BookPath, StringComparison.Ordinal) || (from.HasCaret && to.HasCaret && from.Line != to.Line);

    private sealed class SuspendScope(NavigationTracker tracker) : IDisposable
    {
        private bool _disposed;

        public void Dispose()
        {
            if (!_disposed)
            {
                _disposed = true;
                tracker._suspended--;
            }
        }
    }
}
