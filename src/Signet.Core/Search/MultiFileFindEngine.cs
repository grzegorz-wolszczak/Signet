using System;
using System.Collections.Generic;

namespace Signet.Core.Search;

/// <summary>A single file in the scope of a multi-file search: its book path and full text.</summary>
/// <param name="BookPath">File identifier (book path).</param>
/// <param name="Text">Current content of the file.</param>
public sealed record MultiFileSearchFile(string BookPath, string Text);

/// <summary>A multi-file "Find Next" request — the full context for a single call.</summary>
public sealed record MultiFileSearchRequest
{
    /// <summary>Files in scope (in Book Browser / spine order), including the current file.</summary>
    public IReadOnlyList<MultiFileSearchFile> Files { get; init; } = Array.Empty<MultiFileSearchFile>();

    /// <summary>Book path of the active tab's file (may be absent from <see cref="Files"/>).</summary>
    public string CurrentBookPath { get; init; } = string.Empty;

    /// <summary>Caret position in the current file.</summary>
    public int CurrentCaret { get; init; }

    /// <summary>Selection start in the current file.</summary>
    public int CurrentSelectionStart { get; init; }

    /// <summary>Selection end in the current file.</summary>
    public int CurrentSelectionEnd { get; init; }

    /// <summary>The final PCRE2 pattern (from <see cref="SearchRegexBuilder"/>).</summary>
    public string Pattern { get; init; } = string.Empty;

    /// <summary>Search direction.</summary>
    public SearchDirection Direction { get; init; } = SearchDirection.Down;

    /// <summary>
    /// Search signature (Find text + scope + direction) — a change means a "new search"
    /// and a new starting file is chosen.
    /// </summary>
    public string Signature { get; init; } = string.Empty;
}

/// <summary>Result of a multi-file "Find Next".</summary>
/// <param name="Found">Whether a match was found.</param>
/// <param name="BookPath">The file containing the match.</param>
/// <param name="Start">Start of the match (code units).</param>
/// <param name="End">End of the match.</param>
public sealed record MultiFileFindResult(bool Found, string BookPath, int Start, int End)
{
    /// <summary>The "not found" result.</summary>
    public static MultiFileFindResult NotFound { get; } = new(false, string.Empty, -1, -1);
}

/// <summary>
/// View-independent multi-file "Find Next" engine. One instance per Find &amp; Replace panel; it keeps
/// state between successive <see cref="FindNext"/> calls (starting file, starting position, "remainder").
/// </summary>
/// <remarks>
/// File traversal uses the full <see cref="MultiFileSearchRequest.Files"/> list (not only the trimmed
/// list of files still to search), which simplifies the termination condition; whether a file belongs
/// to the scope is decided purely by its membership in that list. Wrapping across files is always
/// active (the wrap option does not apply to multi-file Find Next).
/// </remarks>
public sealed class MultiFileFindEngine
{
    private string? _startingBookPath;
    private int _startingPos = -1;
    private bool _inRemainder;
    private string? _previousSignature;

    /// <summary>Clears the state (forces a "new search" on the next <see cref="FindNext"/>).</summary>
    public void Reset()
    {
        _startingBookPath = null;
        _startingPos = -1;
        _inRemainder = false;
        _previousSignature = null;
    }

    /// <summary>Finds the next match among the files in scope.</summary>
    public MultiFileFindResult FindNext(MultiFileSearchRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);

        if (request.Files.Count == 0)
        {
            return MultiFileFindResult.NotFound;
        }

        if (!PcreCache.Instance.GetObject(request.Pattern).IsValid)
        {
            return MultiFileFindResult.NotFound;
        }

        if (_previousSignature is null || !string.Equals(_previousSignature, request.Signature, StringComparison.Ordinal))
        {
            SetStartingResource(request);
            _previousSignature = request.Signature;
        }

        return FindInAllFiles(request);
    }

    private void SetStartingResource(MultiFileSearchRequest request)
    {
        _startingBookPath = null;
        _startingPos = -1;
        _inRemainder = false;

        IReadOnlyList<MultiFileSearchFile> files = request.Files;
        if (files.Count == 0)
        {
            return;
        }

        _startingBookPath = ContainsPath(files, request.CurrentBookPath)
            ? request.CurrentBookPath
            : request.Direction == SearchDirection.Down ? files[0].BookPath : files[^1].BookPath;

        if (string.Equals(_startingBookPath, request.CurrentBookPath, StringComparison.Ordinal))
        {
            _startingPos = request.CurrentCaret;
        }
    }

    private MultiFileFindResult FindInAllFiles(MultiFileSearchRequest request)
    {
        string current = request.CurrentBookPath;

        if (string.Equals(current, _startingBookPath, StringComparison.Ordinal) &&
            TryGetFile(request.Files, current, out MultiFileSearchFile startFile))
        {
            if (!_inRemainder)
            {
                MultiFileFindResult inStart = FindWithinFile(
                    startFile, request,
                    request.CurrentSelectionStart, request.CurrentSelectionEnd, request.CurrentCaret,
                    ignoreSelectionOffset: false, splitAt: -1);
                if (inStart.Found)
                {
                    return inStart;
                }

                _inRemainder = true;
            }

            if (_inRemainder && _startingPos != -1)
            {
                // Remainder of the starting file before the starting position — searched from the actual
                // selection/caret (not from the edge), so successive Find Next calls keep advancing.
                MultiFileFindResult remainder = FindWithinFile(
                    startFile, request,
                    request.CurrentSelectionStart, request.CurrentSelectionEnd, request.CurrentCaret,
                    ignoreSelectionOffset: false, splitAt: _startingPos);
                if (remainder.Found)
                {
                    return remainder;
                }
            }
        }
        else if (ContainsPath(request.Files, current) &&
                 TryGetFile(request.Files, current, out MultiFileSearchFile curFile))
        {
            MultiFileFindResult inCurrent = FindWithinFile(
                curFile, request,
                request.CurrentSelectionStart, request.CurrentSelectionEnd, request.CurrentCaret,
                ignoreSelectionOffset: false, splitAt: -1);
            if (inCurrent.Found)
            {
                return inCurrent;
            }
        }

        string? containing = GetNextContainingResource(request);
        if (containing is not null && TryGetFile(request.Files, containing, out MultiFileSearchFile next))
        {
            int edge = request.Direction == SearchDirection.Up ? next.Text.Length : 0;
            return FindWithinFile(next, request, edge, edge, edge, ignoreSelectionOffset: true, splitAt: -1);
        }

        return MultiFileFindResult.NotFound;
    }

    // Scans the trimmed range of "files not yet searched" (with wrapping) from the scan's starting
    // file until it finds a file with a match or returns to the start.
    private string? GetNextContainingResource(MultiFileSearchRequest request)
    {
        IReadOnlyList<MultiFileSearchFile> files = GetFilesToSearch(request, forceAll: false);
        if (files.Count == 0)
        {
            return null;
        }

        string current = request.CurrentBookPath;
        bool currentInList = ContainsPath(files, current);

        string scanStart;
        bool checkScanStartItself;
        if (currentInList)
        {
            scanStart = current;
            checkScanStartItself = false;
        }
        else
        {
            scanStart = request.Direction == SearchDirection.Down ? files[0].BookPath : files[^1].BookPath;
            checkScanStartItself = true;
        }

        if (checkScanStartItself)
        {
            if (ResourceContainsPattern(request.Files, scanStart, request.Pattern))
            {
                return scanStart;
            }

            if (files.Count == 1)
            {
                return null;
            }
        }

        string cursor = scanStart;
        for (int guard = 0; guard <= files.Count; guard++)
        {
            string? nextCursor = GetNextResource(files, cursor, request.Direction);
            if (nextCursor is null || string.Equals(nextCursor, scanStart, StringComparison.Ordinal))
            {
                return null;
            }

            cursor = nextCursor;
            if (ResourceContainsPattern(request.Files, cursor, request.Pattern))
            {
                return cursor;
            }
        }

        return null;
    }

    // The list of "files still to search", computed relative to the starting and current files
    // (taking wrapping and direction into account).
    private IReadOnlyList<MultiFileSearchFile> GetFilesToSearch(MultiFileSearchRequest request, bool forceAll)
    {
        IReadOnlyList<MultiFileSearchFile> all = request.Files;
        string current = request.CurrentBookPath;

        if (forceAll ||
            all.Count == 0 ||
            _startingBookPath is null ||
            !ContainsPath(all, current) ||
            !ContainsPath(all, _startingBookPath) ||
            string.Equals(_startingBookPath, current, StringComparison.Ordinal))
        {
            return all;
        }

        int c = IndexOfPath(all, current);
        int s = IndexOfPath(all, _startingBookPath);
        var result = new List<MultiFileSearchFile>();

        if (request.Direction == SearchDirection.Down)
        {
            bool skip = c < s;
            foreach (MultiFileSearchFile file in all)
            {
                if (string.Equals(file.BookPath, _startingBookPath, StringComparison.Ordinal))
                {
                    skip = true;
                }

                if (string.Equals(file.BookPath, current, StringComparison.Ordinal))
                {
                    skip = false;
                }

                if (!skip)
                {
                    result.Add(file);
                }
            }
        }
        else
        {
            bool skip = s < c;
            foreach (MultiFileSearchFile file in all)
            {
                if (!skip)
                {
                    result.Add(file);
                }

                if (string.Equals(file.BookPath, _startingBookPath, StringComparison.Ordinal))
                {
                    skip = false;
                }

                if (string.Equals(file.BookPath, current, StringComparison.Ordinal))
                {
                    skip = true;
                }
            }
        }

        return result;
    }

    private static string? GetNextResource(
        IReadOnlyList<MultiFileSearchFile> files, string fromBookPath, SearchDirection direction)
    {
        if (files.Count == 0)
        {
            return null;
        }

        int cur = IndexOfPath(files, fromBookPath);
        if (cur < 0)
        {
            cur = 0;
        }

        int max = files.Count - 1;
        int next = direction == SearchDirection.Up
            ? (cur - 1 >= 0 ? cur - 1 : max)
            : (cur + 1 <= max ? cur + 1 : 0);

        return files[next].BookPath;
    }

    private static bool ResourceContainsPattern(
        IReadOnlyList<MultiFileSearchFile> files, string bookPath, string pattern) =>
        TryGetFile(files, bookPath, out MultiFileSearchFile file) &&
        SearchOperations.CountInText(pattern, file.Text) > 0;

    private static MultiFileFindResult FindWithinFile(
        MultiFileSearchFile file,
        MultiFileSearchRequest request,
        int selectionStart,
        int selectionEnd,
        int caret,
        bool ignoreSelectionOffset,
        int splitAt)
    {
        var search = new CodeViewSearch();
        FindResult result = search.FindNext(
            file.Text, selectionStart, selectionEnd, caret,
            request.Pattern, request.Direction, wrap: false, ignoreSelectionOffset, splitAt);

        return result.Found
            ? new MultiFileFindResult(true, file.BookPath, result.Start, result.End)
            : MultiFileFindResult.NotFound;
    }

    private static bool ContainsPath(IReadOnlyList<MultiFileSearchFile> files, string bookPath) =>
        IndexOfPath(files, bookPath) >= 0;

    private static int IndexOfPath(IReadOnlyList<MultiFileSearchFile> files, string bookPath)
    {
        for (int i = 0; i < files.Count; i++)
        {
            if (string.Equals(files[i].BookPath, bookPath, StringComparison.Ordinal))
            {
                return i;
            }
        }

        return -1;
    }

    private static bool TryGetFile(
        IReadOnlyList<MultiFileSearchFile> files, string bookPath, out MultiFileSearchFile file)
    {
        int index = IndexOfPath(files, bookPath);
        if (index >= 0)
        {
            file = files[index];
            return true;
        }

        file = null!;
        return false;
    }
}
