namespace Signet.Core.Search;

/// <summary>Result of a Find Next / Find Previous operation in the current file.</summary>
/// <param name="Found">Whether a match was found.</param>
/// <param name="Start">Start of the match (code units) — to be selected.</param>
/// <param name="End">End of the match.</param>
/// <param name="Wrapped">Whether the search wrapped around the end/start of the document.</param>
public sealed record FindResult(bool Found, int Start, int End, bool Wrapped)
{
    /// <summary>The "not found" result.</summary>
    public static FindResult NotFound { get; } = new(false, -1, -1, false);
}

/// <summary>Result of a Replace / Replace&amp;Find operation (a single replacement of the selection).</summary>
/// <param name="Replaced">Whether the replacement was performed.</param>
/// <param name="NewText">The new full document content (unchanged when <paramref name="Replaced"/> is <c>false</c>).</param>
/// <param name="SelectionStart">New selection start / caret position.</param>
/// <param name="SelectionEnd">New selection end (equal to <paramref name="SelectionStart"/> for a caret without a selection).</param>
/// <param name="MarkedEndDelta">How far the end of the "marked text" region moved (0 when the replacement was outside it).</param>
public sealed record ReplaceResult(
    bool Replaced,
    string NewText,
    int SelectionStart,
    int SelectionEnd,
    int MarkedEndDelta)
{
    /// <summary>The "not replaced" result (content unchanged).</summary>
    public static ReplaceResult NotReplaced { get; } = new(false, string.Empty, -1, -1, 0);
}

/// <summary>Result of a Replace All / Count operation in the current file.</summary>
/// <param name="Count">Number of replacements performed (or matches for Count).</param>
/// <param name="NewText">The new full document content (for Count, the input content).</param>
/// <param name="CaretPosition">Caret position after the operation.</param>
public sealed record ReplaceAllResult(int Count, string NewText, int CaretPosition);
