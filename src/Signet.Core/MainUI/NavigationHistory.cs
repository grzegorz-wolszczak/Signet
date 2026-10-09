using System;
using System.Collections.Generic;
using System.Linq;

namespace Signet.Core.MainUI;

/// <summary>
/// A place in the book that Navigate Back / Forward can return to: a file and, for a text file, the caret offset and
/// line. <paramref name="Offset"/> is negative for a tab without a caret (an image, a font…).
/// </summary>
/// <param name="BookPath">The bookpath of the file.</param>
/// <param name="Offset">The caret offset (0-based), or a negative value for a tab without a caret.</param>
/// <param name="Line">The caret line (1-based); 0 for a tab without a caret.</param>
/// <param name="Time">When the place was left.</param>
public sealed record NavigationPlace(string BookPath, int Offset, int Line, DateTimeOffset Time)
{
    /// <summary>Whether the place has a caret position (a text file).</summary>
    public bool HasCaret => Offset >= 0;
}

/// <summary>A saved state of a <see cref="NavigationHistory"/> (both stacks, oldest place first).</summary>
/// <param name="Back">The places Navigate Back returns to.</param>
/// <param name="Forward">The places Navigate Forward returns to.</param>
public sealed record NavigationHistoryState(IReadOnlyList<NavigationPlace> Back, IReadOnlyList<NavigationPlace> Forward);

/// <summary>
/// The Navigate Back / Forward history (modelled on the IntelliJ platform): the places the caret left by a jump — a
/// tab switch, a click, Go To Line, Find, a link… Going back moves the current place onto the Forward stack; a new
/// jump clears it. A place within <see cref="MergeLineDistance"/> lines of the previous one in the same file replaces
/// it, so small moves do not flood the history. Places in files that no longer exist are dropped when moving.
/// </summary>
public sealed class NavigationHistory
{
    /// <summary>The maximum number of places on each stack (the oldest ones are dropped).</summary>
    public const int Limit = 150;

    /// <summary>Two places in the same file closer than this many lines count as the same place.</summary>
    public const int MergeLineDistance = 4;

    private readonly List<NavigationPlace> _back = new();
    private readonly List<NavigationPlace> _forward = new();

    /// <summary>Raised after any change to the stacks.</summary>
    public event EventHandler? Changed;

    /// <summary>The places Navigate Back returns to, oldest first (the next one is the last).</summary>
    public IReadOnlyList<NavigationPlace> BackPlaces => _back;

    /// <summary>The places Navigate Forward returns to, oldest first (the next one is the last).</summary>
    public IReadOnlyList<NavigationPlace> ForwardPlaces => _forward;

    /// <summary>Whether Navigate Back has a place to go to.</summary>
    public bool CanGoBack => _back.Count > 0;

    /// <summary>Whether Navigate Forward has a place to go to.</summary>
    public bool CanGoForward => _forward.Count > 0;

    /// <summary>
    /// Whether two places count as one: the same file and, when both have a caret, fewer than
    /// <see cref="MergeLineDistance"/> lines apart.
    /// </summary>
    public static bool IsSame(NavigationPlace first, NavigationPlace second)
    {
        ArgumentNullException.ThrowIfNull(first);
        ArgumentNullException.ThrowIfNull(second);
        return string.Equals(first.BookPath, second.BookPath, StringComparison.Ordinal)
            && (!first.HasCaret || !second.HasCaret || Math.Abs(first.Line - second.Line) < MergeLineDistance);
    }

    /// <summary>
    /// Records the place a jump left: it goes onto the Back stack (replacing the previous place when they are the
    /// same) and the Forward stack is cleared.
    /// </summary>
    public void Push(NavigationPlace place)
    {
        ArgumentNullException.ThrowIfNull(place);
        PutLastOrMerge(_back, place);
        _forward.Clear();
        OnChanged();
    }

    /// <summary>
    /// Navigate Back: drops the places in files that no longer exist, takes the most recent place (skipping the ones
    /// that are the same as <paramref name="current"/>) and moves <paramref name="current"/> onto the Forward stack.
    /// </summary>
    /// <param name="current">The current place (<c>null</c> without an open tab).</param>
    /// <param name="exists">Whether a file with the given bookpath exists.</param>
    /// <returns>The place to go to, or <c>null</c> when there is none.</returns>
    public NavigationPlace? Back(NavigationPlace? current, Func<string, bool> exists) =>
        Move(_back, _forward, current, exists, mergeCurrent: false);

    /// <summary>
    /// Navigate Forward: like <see cref="Back"/> in the opposite direction; <paramref name="current"/> goes onto the
    /// Back stack.
    /// </summary>
    public NavigationPlace? Forward(NavigationPlace? current, Func<string, bool> exists) =>
        Move(_forward, _back, current, exists, mergeCurrent: true);

    /// <summary>Follows a renamed or moved file: its places get the new bookpath.</summary>
    public void RenameBookPath(string oldBookPath, string newBookPath)
    {
        ArgumentNullException.ThrowIfNull(oldBookPath);
        ArgumentNullException.ThrowIfNull(newBookPath);
        if (Replace(p => string.Equals(p.BookPath, oldBookPath, StringComparison.Ordinal) ? p with { BookPath = newBookPath } : p))
        {
            OnChanged();
        }
    }

    /// <summary>
    /// Keeps the places of <paramref name="bookPath"/> on the same text after an edit of the file: places after the
    /// edited range move with it, places inside the removed range go to its start.
    /// </summary>
    /// <param name="bookPath">The edited file.</param>
    /// <param name="offset">The start of the edit.</param>
    /// <param name="line">The line (1-based) of <paramref name="offset"/>.</param>
    /// <param name="removedText">The removed text.</param>
    /// <param name="insertedText">The inserted text.</param>
    public void ApplyTextChange(string bookPath, int offset, int line, string removedText, string insertedText)
    {
        ArgumentNullException.ThrowIfNull(bookPath);
        ArgumentNullException.ThrowIfNull(removedText);
        ArgumentNullException.ThrowIfNull(insertedText);
        int removedEnd = offset + removedText.Length;
        int delta = insertedText.Length - removedText.Length;
        int lineDelta = CountLineBreaks(insertedText) - CountLineBreaks(removedText);
        Replace(p =>
        {
            if (!p.HasCaret || !string.Equals(p.BookPath, bookPath, StringComparison.Ordinal) || p.Offset <= offset)
            {
                return p;
            }

            return p.Offset >= removedEnd
                ? p with { Offset = p.Offset + delta, Line = p.Line + lineDelta }
                : p with { Offset = offset, Line = line };
        });
    }

    /// <summary>Forgets all places.</summary>
    public void Clear()
    {
        if (_back.Count == 0 && _forward.Count == 0)
        {
            return;
        }

        _back.Clear();
        _forward.Clear();
        OnChanged();
    }

    /// <summary>The current state of both stacks (for saving).</summary>
    public NavigationHistoryState Capture() => new(_back.ToList(), _forward.ToList());

    /// <summary>Replaces both stacks with <paramref name="state"/> (the newest places over <see cref="Limit"/>).</summary>
    public void Restore(NavigationHistoryState state)
    {
        ArgumentNullException.ThrowIfNull(state);
        _back.Clear();
        _back.AddRange(state.Back.TakeLast(Limit));
        _forward.Clear();
        _forward.AddRange(state.Forward.TakeLast(Limit));
        OnChanged();
    }

    private NavigationPlace? Move(
        List<NavigationPlace> from, List<NavigationPlace> to, NavigationPlace? current, Func<string, bool> exists, bool mergeCurrent)
    {
        ArgumentNullException.ThrowIfNull(exists);
        _back.RemoveAll(p => !exists(p.BookPath));
        _forward.RemoveAll(p => !exists(p.BookPath));

        NavigationPlace? target = null;
        while (from.Count > 0 && target is null)
        {
            NavigationPlace candidate = from[^1];
            from.RemoveAt(from.Count - 1);
            if (current is null || !IsSame(candidate, current))
            {
                target = candidate;
            }
        }

        if (target is not null && current is not null)
        {
            if (mergeCurrent)
            {
                PutLastOrMerge(to, current);
            }
            else
            {
                to.Add(current);
                TrimToLimit(to);
            }
        }

        OnChanged();
        return target;
    }

    private static void PutLastOrMerge(List<NavigationPlace> places, NavigationPlace place)
    {
        if (places.Count > 0 && IsSame(places[^1], place))
        {
            places.RemoveAt(places.Count - 1);
        }

        places.Add(place);
        TrimToLimit(places);
    }

    private static void TrimToLimit(List<NavigationPlace> places)
    {
        if (places.Count > Limit)
        {
            places.RemoveRange(0, places.Count - Limit);
        }
    }

    // Applies the mapping to both stacks; returns whether any place changed.
    private bool Replace(Func<NavigationPlace, NavigationPlace> map)
    {
        bool changed = false;
        foreach (List<NavigationPlace> places in new[] { _back, _forward })
        {
            for (int i = 0; i < places.Count; i++)
            {
                NavigationPlace mapped = map(places[i]);
                if (!ReferenceEquals(mapped, places[i]))
                {
                    places[i] = mapped;
                    changed = true;
                }
            }
        }

        return changed;
    }

    private static int CountLineBreaks(string text) => text.Count(c => c == '\n');

    private void OnChanged() => Changed?.Invoke(this, EventArgs.Empty);
}
