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
    /// Whether the search wraps: after the end of the scope (the last file for Down, the first one for Up) it goes on
    /// from the other end, up to and including the current file. Without it the search ends at the end of the scope.
    /// </summary>
    public bool Wrap { get; init; } = true;

    /// <summary>
    /// Whether the search starts at the beginning of the scope (the start of the first file for Down, the end of the
    /// last one for Up) instead of the caret — "Restart".
    /// </summary>
    public bool FromStart { get; init; }
}

/// <summary>Result of a multi-file "Find Next".</summary>
/// <param name="Found">Whether a match was found.</param>
/// <param name="BookPath">The file containing the match.</param>
/// <param name="Start">Start of the match (code units).</param>
/// <param name="End">End of the match.</param>
public sealed record MultiFileFindResult(bool Found, string BookPath, int Start, int End)
{
    /// <summary>Whether the match was found after wrapping past the end of the scope.</summary>
    public bool Wrapped { get; init; }

    /// <summary>The "not found" result.</summary>
    public static MultiFileFindResult NotFound { get; } = new(false, string.Empty, -1, -1);
}

/// <summary>
/// View-independent multi-file "Find Next": searches the current file from the caret, then the following files of the
/// scope (in Book Browser order, backwards for Up) from their edge; with <see cref="MultiFileSearchRequest.Wrap"/> it
/// goes on from the other end of the scope up to and including the current file, as "Find Next" wraps in a single
/// file. <see cref="MultiFileSearchRequest.FromStart"/> ("Restart") searches the whole scope from its beginning instead
/// of the caret. The engine keeps no state between calls.
/// </summary>
public static class MultiFileFindEngine
{
    /// <summary>Finds the next match among the files in scope.</summary>
    public static MultiFileFindResult FindNext(MultiFileSearchRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        IReadOnlyList<MultiFileSearchFile> files = request.Files;
        if (files.Count == 0 || !PcreCache.Instance.GetObject(request.Pattern).IsValid)
        {
            return MultiFileFindResult.NotFound;
        }

        bool down = request.Direction != SearchDirection.Up;
        int step = down ? 1 : -1;

        // The current file from the caret — unless the search restarts or the file is not in scope.
        int current = request.FromStart ? -1 : IndexOfPath(files, request.CurrentBookPath);
        if (current >= 0)
        {
            MultiFileFindResult inCurrent = FindWithinFile(
                files[current], request,
                request.CurrentSelectionStart, request.CurrentSelectionEnd, request.CurrentCaret, ignoreSelectionOffset: false);
            if (inCurrent.Found)
            {
                return inCurrent;
            }
        }

        // The following files, up to the end of the scope.
        int from = current >= 0 ? current + step : (down ? 0 : files.Count - 1);
        for (int i = from; i >= 0 && i < files.Count; i += step)
        {
            MultiFileFindResult found = FindFromEdge(files[i], request);
            if (found.Found)
            {
                return found;
            }
        }

        // Wrapping: from the other end of the scope up to and including the current file (the part before the caret).
        if (!request.Wrap || current < 0)
        {
            return MultiFileFindResult.NotFound;
        }

        for (int i = down ? 0 : files.Count - 1; down ? i <= current : i >= current; i += step)
        {
            MultiFileFindResult found = FindFromEdge(files[i], request);
            if (found.Found)
            {
                return found with { Wrapped = true };
            }
        }

        return MultiFileFindResult.NotFound;
    }

    // The first match of a file from its start (Down) or its end (Up).
    private static MultiFileFindResult FindFromEdge(MultiFileSearchFile file, MultiFileSearchRequest request)
    {
        int edge = request.Direction == SearchDirection.Up ? file.Text.Length : 0;
        return FindWithinFile(file, request, edge, edge, edge, ignoreSelectionOffset: true);
    }

    private static MultiFileFindResult FindWithinFile(
        MultiFileSearchFile file,
        MultiFileSearchRequest request,
        int selectionStart,
        int selectionEnd,
        int caret,
        bool ignoreSelectionOffset)
    {
        var search = new CodeViewSearch();
        FindResult result = search.FindNext(
            file.Text, selectionStart, selectionEnd, caret,
            request.Pattern, request.Direction, wrap: false, ignoreSelectionOffset);

        return result.Found
            ? new MultiFileFindResult(true, file.BookPath, result.Start, result.End)
            : MultiFileFindResult.NotFound;
    }

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
}
