using System;
using System.Collections.Generic;
using Signet.Core.Resources;

namespace Signet.App.ViewModels;

/// <summary>
/// One place of the preview's history: a page, the fragment (<c>#id</c>) it was shown at and the source offset the
/// reader was at in it (<see langword="null"/> when unknown).
/// </summary>
/// <param name="Resource">The shown (X)HTML file; a rename does not change which file it is.</param>
/// <param name="Fragment">The fragment with its <c>#</c>, or <see langword="null"/>.</param>
/// <param name="Offset">The source offset, or <see langword="null"/>.</param>
public sealed record PreviewHistoryEntry(HtmlResource Resource, string? Fragment, int? Offset);

/// <summary>Limits of <see cref="PreviewHistory"/> and of what the preview records in it.</summary>
public static class PreviewHistoryConstants
{
    /// <summary>The most places kept; the oldest is forgotten first.</summary>
    public const int MaxEntries = 100;

    /// <summary>
    /// How far (in characters of the source) the caret has to move within a page for the new place to be remembered
    /// on its own: a jump to a definition or a "go to line" counts, typing and arrow keys do not.
    /// </summary>
    public const int LargeCaretJumpChars = 4000;
}

/// <summary>
/// The places the preview has shown, like the history of a browser: <see cref="Visit"/> adds a place after the current
/// one (and forgets the places that were ahead of it), <see cref="Move"/> goes back or forward.
/// </summary>
public sealed class PreviewHistory
{
    private readonly List<PreviewHistoryEntry> _entries = new();
    private int _index = -1;

    /// <summary>The place that is shown now, or <see langword="null"/> when there is none.</summary>
    public PreviewHistoryEntry? Current => _index >= 0 ? _entries[_index] : null;

    /// <summary>Whether there is a place before the current one.</summary>
    public bool CanGoBack => _index > 0;

    /// <summary>Whether there is a place after the current one.</summary>
    public bool CanGoForward => _index >= 0 && _index < _entries.Count - 1;

    /// <summary>The number of places kept.</summary>
    public int Count => _entries.Count;

    /// <summary>Forgets every place.</summary>
    public void Clear()
    {
        _entries.Clear();
        _index = -1;
    }

    /// <summary>
    /// A place is shown. It becomes the current one unless it is the one already there (the same file and fragment,
    /// without an offset of its own or with the same one).
    /// </summary>
    /// <param name="entry">The place.</param>
    /// <param name="leavingOffset">Where the reader was in the current place, remembered for the way back.</param>
    /// <returns><see langword="true"/> when the history changed.</returns>
    public bool Visit(PreviewHistoryEntry entry, int? leavingOffset)
    {
        ArgumentNullException.ThrowIfNull(entry);
        if (Current is { } current
            && ReferenceEquals(current.Resource, entry.Resource)
            && string.Equals(current.Fragment, entry.Fragment, StringComparison.Ordinal)
            && (entry.Offset is null || entry.Offset == current.Offset))
        {
            return false;
        }

        RememberLeavingOffset(leavingOffset);
        if (_index >= 0 && _index < _entries.Count - 1)
        {
            _entries.RemoveRange(_index + 1, _entries.Count - _index - 1);
        }

        _entries.Add(entry);
        if (_entries.Count > PreviewHistoryConstants.MaxEntries)
        {
            _entries.RemoveAt(0);
        }

        _index = _entries.Count - 1;
        return true;
    }

    /// <summary>
    /// Goes to the nearest place in a direction that <paramref name="isValid"/> accepts; the places it does not accept
    /// (a file that was deleted) are dropped on the way.
    /// </summary>
    /// <param name="direction"><c>-1</c> for back, <c>1</c> for forward.</param>
    /// <param name="leavingOffset">Where the reader was in the current place, remembered for the way back.</param>
    /// <param name="isValid">Whether a place can still be shown.</param>
    /// <returns>The place now current, or <see langword="null"/> when there is none in that direction.</returns>
    public PreviewHistoryEntry? Move(int direction, int? leavingOffset, Func<PreviewHistoryEntry, bool> isValid)
    {
        ArgumentNullException.ThrowIfNull(isValid);
        if (direction is not (-1 or 1) || _index < 0)
        {
            return null;
        }

        int target = _index + direction;
        while (target >= 0 && target < _entries.Count && !isValid(_entries[target]))
        {
            _entries.RemoveAt(target);
            if (direction < 0)
            {
                // The current place moved one down; so did the way to go on.
                _index--;
                target--;
            }
        }

        if (target < 0 || target >= _entries.Count)
        {
            return null;
        }

        RememberLeavingOffset(leavingOffset);
        _index = target;
        return _entries[_index];
    }

    private void RememberLeavingOffset(int? leavingOffset)
    {
        if (leavingOffset is not null && _index >= 0)
        {
            _entries[_index] = _entries[_index] with { Offset = leavingOffset };
        }
    }
}
