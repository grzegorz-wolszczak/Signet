namespace Signet.App.ViewModels;

/// <summary>
/// A remembered location in the book — file + caret position in Code View, listed in the
/// Window menu. Session-only: not saved to the EPUB, survives tab switching.
/// </summary>
/// <param name="BookPath">Bookpath of the resource.</param>
/// <param name="Name">Menu label (file name + line number).</param>
/// <param name="Line">Line number (1-based) — for the label and for jumping when the offset is stale.</param>
/// <param name="Offset">Caret offset (0-based) at the time the bookmark was added.</param>
public sealed record Bookmark(string BookPath, string Name, int Line, int Offset);
