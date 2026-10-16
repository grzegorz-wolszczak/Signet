using System;
using System.Collections.Generic;

namespace Signet.Core.BookManipulation;

/// <summary>
/// One entry of a <see cref="LoadWarning"/> that concerns a single file: the file, why it is reported and, when
/// known, where in the file the problem is.
/// </summary>
/// <param name="BookPath">Bookpath of the file, or <c>null</c> when the entry is not a file of the book.</param>
/// <param name="Reason">A translated, user-facing reason (e.g. the XML parser's message).</param>
/// <param name="Line">Line of the problem (1-based), or <c>-1</c> when unknown.</param>
/// <param name="Column">Column of the problem (1-based), or <c>-1</c> when unknown.</param>
public sealed record LoadWarningItem(string? BookPath, string Reason, int Line = -1, int Column = -1);

/// <summary>
/// A warning collected while loading a book (see <see cref="Book.LoadWarningDetails"/>). A warning about a single
/// thing is just its <see cref="Message"/>; one that concerns many files carries them as <see cref="Items"/>, so that the
/// UI can list them one by one instead of showing the glued-together <see cref="Message"/>.
/// </summary>
/// <param name="Message">The whole warning as one translated sentence (also what <see cref="Book.LoadWarnings"/> holds).</param>
/// <param name="Title">A short translated heading of a warning with <paramref name="Items"/>; <c>null</c> without them.</param>
/// <param name="Items">The files the warning concerns; empty for a warning about a single thing.</param>
public sealed record LoadWarning(string Message, string? Title = null, IReadOnlyList<LoadWarningItem>? Items = null)
{
    /// <summary>The files the warning concerns (empty for a warning about a single thing).</summary>
    public IReadOnlyList<LoadWarningItem> Items { get; init; } = Items ?? Array.Empty<LoadWarningItem>();

    /// <summary>
    /// Bookpath of the single file a warning without <see cref="Items"/> is about (so that the UI can open it), or
    /// <c>null</c> when the warning is not about one file of the book.
    /// </summary>
    public string? BookPath { get; init; }
}
