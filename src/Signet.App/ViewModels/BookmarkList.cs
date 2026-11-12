using System;
using System.Collections.Generic;
using System.Linq;
using AvaloniaEdit.Document;
using Signet.Core.MainUI;

namespace Signet.App.ViewModels;

/// <summary>
/// The bookmarks of the open book, like the bookmarks of the JetBrains IDEs: a line of a file carries at most one,
/// <see cref="Toggle"/> adds or removes it, <see cref="Neighbour"/> finds the next or the previous one in a file. The
/// list is kept per book file (<see cref="Places"/> / <see cref="Load"/>) and forgotten with the book.
/// </summary>
/// <remarks>
/// The open tabs hand over their documents (<see cref="Attach"/>); a bookmark in an attached document follows its line
/// through edits. A bookmark of a file that is not open keeps the line it had when the file was closed.
/// </remarks>
public sealed class BookmarkList
{
    private readonly List<Bookmark> _items = new();
    private readonly Dictionary<string, AttachedDocument> _documents = new(StringComparer.Ordinal);

    /// <summary>
    /// Reads the text of a line (1-based) of a book file that is not open in a tab, for <see cref="LineText"/>;
    /// <see langword="null"/> = unknown. Set by the main window.
    /// </summary>
    public Func<string, int, string?>? ClosedFileLineText { get; set; }

    /// <summary>Raised when a bookmark was added or removed, or a line of one changed.</summary>
    public event EventHandler? Changed;

    /// <summary>
    /// The bookmarks ordered by file (book path) and line — the order of the "Bookmarks" panel.
    /// </summary>
    public IReadOnlyList<Bookmark> Items => _items;

    /// <summary>The bookmarks as the places that are kept between sessions.</summary>
    public IReadOnlyList<BookmarkPlace> Places => _items.Select(b => new BookmarkPlace(b.BookPath, b.Line)).ToList();

    /// <summary>The bookmark on the line of the file, or <see langword="null"/>.</summary>
    public Bookmark? Find(string bookPath, int line) =>
        _items.FirstOrDefault(b => b.Line == line && string.Equals(b.BookPath, bookPath, StringComparison.Ordinal));

    /// <summary>The lines of the file that carry a bookmark, in no particular order.</summary>
    public IReadOnlyList<int> LinesIn(string bookPath) =>
        _items.Where(b => string.Equals(b.BookPath, bookPath, StringComparison.Ordinal)).Select(b => b.Line).ToList();

    /// <summary>
    /// Adds a bookmark on the line of the file, or removes the one that is there. Returns the new bookmark, or
    /// <see langword="null"/> when one was removed.
    /// </summary>
    public Bookmark? Toggle(string bookPath, int line)
    {
        ArgumentException.ThrowIfNullOrEmpty(bookPath);
        if (Find(bookPath, line) is { } existing)
        {
            Remove(existing);
            return null;
        }

        Bookmark added = new(bookPath, Math.Max(1, line));
        _items.Add(added);
        Sort();
        if (_documents.TryGetValue(bookPath, out AttachedDocument? attached))
        {
            added.Anchor = CreateAnchor(attached.Document, added.Line);
        }

        Changed?.Invoke(this, EventArgs.Empty);
        return added;
    }

    /// <summary>Removes a bookmark.</summary>
    public void Remove(Bookmark bookmark)
    {
        ArgumentNullException.ThrowIfNull(bookmark);
        if (_items.Remove(bookmark))
        {
            bookmark.Anchor = null;
            Changed?.Invoke(this, EventArgs.Empty);
        }
    }

    /// <summary>Removes all the bookmarks.</summary>
    public void Clear()
    {
        if (_items.Count == 0)
        {
            return;
        }

        _items.Clear();
        Changed?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>Replaces the bookmarks with the kept <paramref name="places"/> (a book was opened).</summary>
    public void Load(IEnumerable<BookmarkPlace> places)
    {
        ArgumentNullException.ThrowIfNull(places);
        _items.Clear();
        foreach (BookmarkPlace place in places)
        {
            if (Find(place.BookPath, place.Line) is null)
            {
                Bookmark bookmark = new(place.BookPath, Math.Max(1, place.Line));
                if (_documents.TryGetValue(place.BookPath, out AttachedDocument? attached))
                {
                    bookmark.Anchor = CreateAnchor(attached.Document, bookmark.Line);
                }

                _items.Add(bookmark);
            }
        }

        Sort();
        Changed?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>
    /// The line of the next (<paramref name="forward"/>) or the previous bookmark in the file after
    /// <paramref name="currentLine"/>, wrapping around at the end of the file; <see langword="null"/> when there is none
    /// to go to.
    /// </summary>
    public int? Neighbour(string bookPath, int currentLine, bool forward) =>
        BookmarkPlace.Neighbour(LinesIn(bookPath), currentLine, forward);

    /// <summary>
    /// The text of the line of a bookmark, trimmed, for the panel; empty when it is not known (the file is not open and
    /// <see cref="ClosedFileLineText"/> does not know it either).
    /// </summary>
    public string LineText(Bookmark bookmark)
    {
        ArgumentNullException.ThrowIfNull(bookmark);
        if (_documents.TryGetValue(bookmark.BookPath, out AttachedDocument? attached))
        {
            return bookmark.Line <= attached.Document.LineCount
                ? attached.Document.GetText(attached.Document.GetLineByNumber(bookmark.Line)).Trim()
                : string.Empty;
        }

        return ClosedFileLineText?.Invoke(bookmark.BookPath, bookmark.Line)?.Trim() ?? string.Empty;
    }

    /// <summary>
    /// A tab shows <paramref name="document"/> of the file: its bookmarks follow their lines through the edits from now on
    /// (until <see cref="Detach"/>).
    /// </summary>
    public void Attach(string bookPath, TextDocument document)
    {
        ArgumentException.ThrowIfNullOrEmpty(bookPath);
        ArgumentNullException.ThrowIfNull(document);
        Detach(bookPath);

        AttachedDocument attached = new(document, (_, e) => OnDocumentChanged(bookPath, document, e));
        _documents[bookPath] = attached;
        document.Changed += attached.Handler;
        foreach (Bookmark bookmark in _items.Where(b => string.Equals(b.BookPath, bookPath, StringComparison.Ordinal)))
        {
            bookmark.Anchor = CreateAnchor(document, bookmark.Line);
        }
    }

    /// <summary>The tab of the file was closed: its bookmarks keep the line they are on.</summary>
    public void Detach(string bookPath)
    {
        if (!_documents.Remove(bookPath, out AttachedDocument? attached))
        {
            return;
        }

        attached.Document.Changed -= attached.Handler;
        foreach (Bookmark bookmark in _items.Where(b => string.Equals(b.BookPath, bookPath, StringComparison.Ordinal)))
        {
            SyncLine(bookmark);
            bookmark.Anchor = null;
        }
    }

    private void OnDocumentChanged(string bookPath, TextDocument document, DocumentChangeEventArgs e)
    {
        int lengthBefore = document.TextLength - e.InsertionLength + e.RemovalLength;
        bool replacedAll = e.Offset == 0 && e.RemovalLength == lengthBefore && lengthBefore > 0;
        (int First, int Last)? deletedLines = replacedAll ? null : WholeLinesRemoved(document, e);
        bool changed = false;
        foreach (Bookmark bookmark in _items.Where(b => string.Equals(b.BookPath, bookPath, StringComparison.Ordinal)).ToList())
        {
            // The anchor of a line that was deleted as a whole stays at the start of the text that took its place, so the
            // lines that went are told from the numbers the bookmarks had.
            bool lineDeleted = deletedLines is { } lines && bookmark.Line >= lines.First && bookmark.Line <= lines.Last;
            if (lineDeleted || bookmark.Anchor is not { IsDeleted: false })
            {
                if (replacedAll && !lineDeleted)
                {
                    // The text was replaced as a whole (a reformat, a revert): the bookmarks stay on their lines.
                    bookmark.Line = Math.Clamp(bookmark.Line, 1, Math.Max(1, document.LineCount));
                    bookmark.Anchor = CreateAnchor(document, bookmark.Line);
                }
                else
                {
                    // The line was deleted, and the bookmark with it.
                    _items.Remove(bookmark);
                }

                changed = true;
            }
            else if (bookmark.Anchor.Line != bookmark.Line)
            {
                bookmark.Line = bookmark.Anchor.Line;
                changed = true;
            }
        }

        if (changed)
        {
            Sort();
            Changed?.Invoke(this, EventArgs.Empty);
        }
    }

    // The lines (by their numbers before the change) that a removal that started at the start of a line took away as a
    // whole, with their line breaks; null when the change removed no whole line.
    private static (int First, int Last)? WholeLinesRemoved(TextDocument document, DocumentChangeEventArgs e)
    {
        if (e.InsertionLength > 0 || e.RemovalLength == 0)
        {
            return null;
        }

        string removed = e.RemovedText.Text;
        int breaks = 0;
        for (int i = 0; i < removed.Length; i++)
        {
            if (removed[i] == '\n' || (removed[i] == '\r' && (i + 1 >= removed.Length || removed[i + 1] != '\n')))
            {
                breaks++;
            }
        }

        DocumentLine line = document.GetLineByOffset(Math.Min(e.Offset, document.TextLength));
        return breaks > 0 && line.Offset == e.Offset ? (line.LineNumber, line.LineNumber + breaks - 1) : null;
    }

    private static void SyncLine(Bookmark bookmark)
    {
        if (bookmark.Anchor is { IsDeleted: false } anchor)
        {
            bookmark.Line = anchor.Line;
        }
    }

    // After an insertion at the start of the line the anchor moves with the text, so the bookmark stays on its line.
    private static TextAnchor CreateAnchor(TextDocument document, int line)
    {
        DocumentLine documentLine = document.GetLineByNumber(Math.Clamp(line, 1, Math.Max(1, document.LineCount)));
        TextAnchor anchor = document.CreateAnchor(documentLine.Offset);
        anchor.MovementType = AnchorMovementType.AfterInsertion;
        return anchor;
    }

    private void Sort() => _items.Sort((a, b) =>
    {
        int byPath = string.CompareOrdinal(a.BookPath, b.BookPath);
        return byPath != 0 ? byPath : a.Line.CompareTo(b.Line);
    });

    private sealed record AttachedDocument(TextDocument Document, EventHandler<DocumentChangeEventArgs> Handler);
}
