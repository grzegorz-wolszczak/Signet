using AvaloniaEdit.Document;

namespace Signet.App.ViewModels;

/// <summary>
/// A bookmark: a line of a book file, marked in the gutter of Code Editor and listed in the "Bookmarks" panel. While the
/// file is open in a tab the bookmark is tied to the text (<see cref="BookmarkList.Attach"/>), so it moves with the
/// line when the text above is edited and goes away with the line when that is deleted.
/// </summary>
public sealed class Bookmark
{
    internal Bookmark(string bookPath, int line)
    {
        BookPath = bookPath;
        Line = line;
    }

    /// <summary>Book path of the file.</summary>
    public string BookPath { get; }

    /// <summary>The line the bookmark is on now (1-based).</summary>
    public int Line { get; internal set; }

    /// <summary>The place in the text of the open file the bookmark follows; <see langword="null"/> while the file is not open.</summary>
    internal TextAnchor? Anchor { get; set; }
}
