using System;
using System.Collections.Generic;

namespace Signet.Core.Search;

/// <summary>
/// View-independent "Find &amp; Replace in the current file" engine (<c>FindNext</c>, <c>Count</c>,
/// <c>ReplaceSelected</c>, <c>ReplaceAll</c>) with support for the "marked text" region.
/// </summary>
/// <remarks>
/// <para>
/// Each method receives the current editor state (full text, selection, caret position)
/// and returns a description of the change for the view layer to apply. The "last match" state
/// (needed by <see cref="ReplaceSelected"/>) and the <see cref="Marked"/> region are kept by the
/// engine itself — one instance per Code View tab.
/// </para>
/// <para>
/// Not handled here: searching for misspelled words and scrolling/centering the view (view layer).
/// The <c>splitAt</c> parameter covers the remainder of the starting file after a wrap in
/// multi-file mode.
/// </para>
/// </remarks>
public sealed class CodeViewSearch
{
    private string? _lastFindPattern;
    private Spcre.MatchInfo? _lastMatch;

    /// <summary>The "marked text" region of this tab.</summary>
    public MarkedTextRegion Marked { get; } = new();

    /// <summary>Whether a remembered match is available for <see cref="ReplaceSelected"/>.</summary>
    public bool HasLastMatch => _lastMatch is { Success: true };

    /// <summary>
    /// Finds the next (or previous) match of <paramref name="pattern"/> relative to the
    /// current selection/caret. The <c>splitAt</c> parameter (when &#8805; 0) limits the
    /// search to <c>[0, splitAt)</c> for the Down direction or <c>[splitAt, end)</c>
    /// for Up, representing the "remainder" of the starting file after a wrap in a multi-file
    /// search; it is not combined with the "marked text" region.
    /// </summary>
    public FindResult FindNext(
        string text,
        int selectionStart,
        int selectionEnd,
        int caret,
        string pattern,
        SearchDirection direction,
        bool wrap,
        bool ignoreSelectionOffset = false,
        int splitAt = -1)
    {
        ArgumentNullException.ThrowIfNull(text);
        ArgumentNullException.ThrowIfNull(pattern);
        Marked.ClampTo(text.Length);

        Spcre spcre = PcreCache.Instance.GetObject(pattern);
        if (!spcre.IsValid)
        {
            return FindResult.NotFound;
        }

        int startOffset = 0;
        int start = 0;
        int end = text.Length;
        bool markedText = Marked.IsMarked;

        if (markedText)
        {
            (bool ok, int ms, int me, int mc) = MoveToMarkedText(direction, wrap, caret);
            if (!ok)
            {
                return FindResult.NotFound;
            }

            selectionStart = ms;
            selectionEnd = me;
            caret = mc;
            start = Marked.Start;
            end = Marked.End;
            startOffset = Marked.Start;
        }

        if (splitAt >= 0 && !markedText)
        {
            if (direction == SearchDirection.Up)
            {
                start = splitAt;
            }
            else
            {
                end = splitAt;
            }

            startOffset = start;

            // No usable region → no match.
            if (start < 0 || end <= 0 || start >= end)
            {
                return FindResult.NotFound;
            }
        }

        int selectionOffset = GetSelectionOffset(
            direction, ignoreSelectionOffset, markedText, selectionStart, selectionEnd, text.Length);

        Spcre.MatchInfo matchInfo;
        if (direction == SearchDirection.Up)
        {
            matchInfo = spcre.GetLastMatchInfo(Slice(text, start, selectionOffset));
        }
        else
        {
            matchInfo = spcre.GetFirstMatchInfo(Slice(text, selectionOffset, end));
            startOffset = selectionOffset;
        }

        bool inRange = matchInfo.Success;
        if (markedText && matchInfo.Success)
        {
            if (matchInfo.Offset.End + startOffset > Marked.End ||
                matchInfo.Offset.Start + startOffset < Marked.Start)
            {
                inRange = false;
            }
        }
        else if (splitAt >= 0 && matchInfo.Success)
        {
            if (matchInfo.Offset.End + startOffset > end ||
                matchInfo.Offset.Start + startOffset < start)
            {
                inRange = false;
            }
        }

        if (inRange)
        {
            int matchStart = matchInfo.Offset.Start + startOffset;
            int matchEnd = matchInfo.Offset.End + startOffset;
            _lastFindPattern = pattern;
            _lastMatch = ShiftMatch(matchInfo, startOffset);
            return new FindResult(true, matchStart, matchEnd, false);
        }

        if (wrap)
        {
            FindResult wrapped = FindNext(
                text, selectionStart, selectionEnd, caret, pattern, direction,
                wrap: false, ignoreSelectionOffset: true);
            if (wrapped.Found)
            {
                return wrapped with { Wrapped = true };
            }
        }

        return FindResult.NotFound;
    }

    /// <summary>Counts the matches of <paramref name="pattern"/>.</summary>
    public int Count(string text, int caret, string pattern, SearchDirection direction, bool wrap)
    {
        ArgumentNullException.ThrowIfNull(text);
        ArgumentNullException.ThrowIfNull(pattern);
        Marked.ClampTo(text.Length);

        Spcre spcre = PcreCache.Instance.GetObject(pattern);
        if (!spcre.IsValid)
        {
            return 0;
        }

        int start = 0;
        int end = text.Length;
        bool markedText = Marked.IsMarked;

        if (markedText)
        {
            (bool ok, int _, int _, int mc) = MoveToMarkedText(direction, wrap, caret);
            if (!ok)
            {
                return 0;
            }

            caret = mc;
            start = Marked.Start;
            end = Marked.End;
        }

        string haystack = text;
        if (!wrap)
        {
            haystack = direction == SearchDirection.Up
                ? Slice(text, start, caret)
                : Slice(text, caret, end);
        }
        else if (markedText)
        {
            haystack = Slice(text, start, end);
        }

        return spcre.GetEveryMatchInfo(haystack).Count;
    }

    /// <summary>
    /// If the selection corresponds to the last match, replaces it. The <c>replaceCurrent</c>
    /// parameter: <c>true</c> for "Replace" (stay on the replaced text), <c>false</c> for
    /// "Replace &amp; Find" (position the caret for the next Find).
    /// </summary>
    public ReplaceResult ReplaceSelected(
        string text,
        int selectionStart,
        int selectionEnd,
        int caret,
        string pattern,
        string replacement,
        SearchDirection direction,
        bool replaceCurrent)
    {
        ArgumentNullException.ThrowIfNull(text);
        ArgumentNullException.ThrowIfNull(pattern);
        ArgumentNullException.ThrowIfNull(replacement);
        Marked.ClampTo(text.Length);

        // Safe only when neither the selection nor the pattern has changed since the last Find.
        if (pattern != _lastFindPattern || _lastMatch is not { Success: true } lastMatch)
        {
            return ReplaceResult.NotReplaced;
        }

        Spcre spcre = PcreCache.Instance.GetObject(pattern);
        string selectedText = Slice(text, selectionStart, selectionEnd);
        bool inMarked = Marked.IsMarked &&
                        selectionStart >= Marked.Start && selectionEnd <= Marked.End;
        int originalLength = text.Length;

        if (!spcre.ReplaceText(selectedText, lastMatch.CaptureGroupsOffsets, replacement, out string replaced))
        {
            return ReplaceResult.NotReplaced;
        }

        string newText = string.Concat(text.AsSpan(0, selectionStart), replaced, text.AsSpan(selectionEnd));
        int delta = newText.Length - originalLength;

        int newSelectionStart;
        int newSelectionEnd;
        if (replaceCurrent)
        {
            newSelectionStart = selectionStart;
            newSelectionEnd = selectionStart + replaced.Length;
        }
        else
        {
            int pos = direction == SearchDirection.Up
                ? selectionStart
                : selectionStart + replaced.Length;
            newSelectionStart = pos;
            newSelectionEnd = pos;
        }

        int markedEndDelta = 0;
        if (inMarked)
        {
            markedEndDelta = delta;
            Marked.End += delta;
        }

        // Changing the content/selection invalidates the remembered match.
        ResetLastMatch();

        return new ReplaceResult(true, newText, newSelectionStart, newSelectionEnd, markedEndDelta);
    }

    /// <summary>Replaces all matches.</summary>
    public ReplaceAllResult ReplaceAll(
        string text,
        int caret,
        string pattern,
        string replacement,
        SearchDirection direction,
        bool wrap)
    {
        ArgumentNullException.ThrowIfNull(text);
        ArgumentNullException.ThrowIfNull(pattern);
        ArgumentNullException.ThrowIfNull(replacement);
        Marked.ClampTo(text.Length);

        Spcre spcre = PcreCache.Instance.GetObject(pattern);
        if (!spcre.IsValid)
        {
            return new ReplaceAllResult(0, text, Math.Clamp(caret, 0, text.Length));
        }

        bool markedText = Marked.IsMarked;
        string work = text;
        int position = caret;

        if (markedText)
        {
            (bool ok, int _, int _, int mc) = MoveToMarkedText(direction, wrap, caret);
            if (!ok)
            {
                return new ReplaceAllResult(0, text, Math.Clamp(caret, 0, text.Length));
            }

            caret = mc;
            work = Slice(text, Marked.Start, Marked.End);
            position = caret - Marked.Start;
        }

        int markedLength = work.Length;
        IReadOnlyList<Spcre.MatchInfo> matches = spcre.GetEveryMatchInfo(work);

        int count = 0;

        // In reverse order — length changes do not shift offsets that are still to be processed.
        for (int i = matches.Count - 1; i >= 0; i--)
        {
            Spcre.MatchInfo match = matches[i];

            if (!wrap)
            {
                if (direction == SearchDirection.Up)
                {
                    if (match.Offset.Start > position)
                    {
                        break;
                    }
                }
                else if (match.Offset.End < position)
                {
                    break;
                }
            }

            if (spcre.ReplaceText(
                    Slice(work, match.Offset.Start, match.Offset.End),
                    match.CaptureGroupsOffsets,
                    replacement,
                    out string replaced))
            {
                work = string.Concat(work.AsSpan(0, match.Offset.Start), replaced, work.AsSpan(match.Offset.End));
                count++;
            }
        }

        string finalText;
        if (markedText)
        {
            finalText = string.Concat(text.AsSpan(0, Marked.Start), work, text.AsSpan(Marked.End));
            Marked.End += work.Length - markedLength;
        }
        else
        {
            finalText = work;
        }

        ResetLastMatch();
        return new ReplaceAllResult(count, finalText, Math.Clamp(caret, 0, finalText.Length));
    }

    /// <summary>
    /// Sets the "marked text" region to the given selection.
    /// An empty selection clears the region.
    /// </summary>
    /// <returns><c>true</c> when the region was set.</returns>
    public bool MarkSelection(int selectionStart, int selectionEnd)
    {
        if (selectionEnd > selectionStart)
        {
            Marked.Set(selectionStart, selectionEnd);
            return true;
        }

        ClearMarkedText();
        return false;
    }

    /// <summary>Clears the "marked text" region.</summary>
    /// <returns><c>true</c> when the region was previously set.</returns>
    public bool ClearMarkedText()
    {
        bool wasMarked = Marked.IsMarked;
        Marked.Clear();
        return wasMarked;
    }

    /// <summary>
    /// Notification that the document content changed: clears the "marked text" region
    /// (unless the change comes from a replace inside it) and invalidates the remembered match.
    /// </summary>
    public void NotifyTextChanged(bool replacingInMarkedText = false)
    {
        if (!replacingInMarkedText && Marked.IsMarked)
        {
            ClearMarkedText();
        }

        ResetLastMatch();
    }

    /// <summary>Invalidates the remembered match.</summary>
    public void ResetLastMatch() => _lastMatch = null;

    /// <summary>
    /// Remembers the match <c>[start, end)</c> found outside this tab — by a multi-file
    /// Find Next that opens the file and selects the result — so that a subsequent Replace
    /// operates on that match.
    /// </summary>
    /// <returns><c>false</c> (and no remembered match) when the pattern does not match exactly there.</returns>
    public bool RememberMatch(string text, string pattern, int start, int end)
    {
        ArgumentNullException.ThrowIfNull(text);
        ArgumentNullException.ThrowIfNull(pattern);

        Spcre.MatchInfo matchInfo = PcreCache.Instance.GetObject(pattern).GetFirstMatchInfo(text, start);
        if (!matchInfo.Success || matchInfo.Offset.Start != start || matchInfo.Offset.End != end)
        {
            ResetLastMatch();
            return false;
        }

        _lastFindPattern = pattern;
        _lastMatch = matchInfo;
        return true;
    }

    private (bool Ok, int SelectionStart, int SelectionEnd, int Caret) MoveToMarkedText(
        SearchDirection direction, bool wrap, int caret)
    {
        if (!Marked.IsMarked)
        {
            return (false, 0, 0, caret);
        }

        if (caret >= Marked.Start && caret <= Marked.End)
        {
            return (true, caret, caret, caret);
        }

        bool moved = false;
        int pos = caret;

        if (direction == SearchDirection.Up)
        {
            if (wrap || pos > Marked.End)
            {
                pos = Marked.End;
                moved = true;
            }
        }
        else if (wrap || pos < Marked.Start)
        {
            pos = Marked.Start;
            moved = true;
        }

        return moved ? (true, pos, pos, pos) : (false, caret, caret, caret);
    }

    private int GetSelectionOffset(
        SearchDirection direction,
        bool ignoreSelectionOffset,
        bool markedText,
        int selectionStart,
        int selectionEnd,
        int textLength)
    {
        if (direction == SearchDirection.Up)
        {
            if (ignoreSelectionOffset)
            {
                return markedText ? Marked.End : textLength;
            }

            return selectionStart;
        }

        if (ignoreSelectionOffset)
        {
            return markedText ? Marked.Start : 0;
        }

        return selectionEnd;
    }

    private static Spcre.MatchInfo ShiftMatch(Spcre.MatchInfo matchInfo, int delta) =>
        new(new SpcreCapture(matchInfo.Offset.Start + delta, matchInfo.Offset.End + delta),
            matchInfo.CaptureGroupsOffsets);

    // The range [start, end) clamped to the text bounds.
    private static string Slice(string text, int start, int end)
    {
        if (start < 0)
        {
            start = 0;
        }

        if (end > text.Length)
        {
            end = text.Length;
        }

        if (start >= end)
        {
            return string.Empty;
        }

        return text.Substring(start, end - start);
    }
}
