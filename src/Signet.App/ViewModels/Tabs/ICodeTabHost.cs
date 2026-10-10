using System.Threading.Tasks;
using Avalonia.Input;
using Signet.Core.BookManipulation;
using Signet.Core.Footnotes;
using Signet.Core.MiscEditors;
using Signet.Core.Resources;

namespace Signet.App.ViewModels.Tabs;

/// <summary>
/// Main window services needed by the Code View context menu (the editor reaches the
/// main window for clips, actions, book operations and the image preview). Implemented by
/// <c>MainWindowViewModel</c>; <c>TabManager</c> passes it to every new code tab.
/// </summary>
public interface ICodeTabHost
{
    /// <summary>Root of the clip library (the "Clips" submenu).</summary>
    ClipEditorNode ClipLibraryRoot { get; }

    /// <summary>Pastes the clip content into the active document.</summary>
    void PasteClip(string text);

    /// <summary>"Add To Clips..." — adds the text as a new clip and opens the Clip Editor.</summary>
    void AddToClips(string text);

    /// <summary>Runs the application action with the given id (e.g. Go To Link Or Style, Mark Selection).</summary>
    void ExecuteAction(string actionId);

    /// <summary>The current keyboard shortcut of the action (shown next to its context menu item); <c>null</c> = none.</summary>
    KeyGesture? GetActionGesture(string actionId);

    /// <summary>
    /// Formats the text of an (X)HTML file editor: <paramref name="toValid"/> = Mend Code, otherwise
    /// Mend and Prettify Code. <c>null</c> when the operation was cancelled (e.g. the file is not
    /// well-formed, or the user cancelled the missing DOCTYPE warning).
    /// </summary>
    Task<string?> ReformatHtmlTextAsync(Resource resource, string text, bool toValid);

    /// <summary>
    /// "Rename Class" from an XHTML file: saves the tabs and returns a
    /// <see cref="ClassRenamer"/> over the texts of the whole book. <c>null</c> (with a message on the
    /// status bar) when there is no book or any XHTML file is not well-formed, and also when the user
    /// cancelled the missing DOCTYPE warning.
    /// </summary>
    Task<ClassRenamer?> PrepareClassRenameAsync();

    /// <summary>Writes the result of "Rename Class" into the book files and refreshes the tabs, Book Browser and Preview.</summary>
    void ApplyClassRename(ClassRenameResult result);

    /// <summary>
    /// "Remove span" — how many spans of the book are the same as the span under <paramref name="offset"/> in
    /// <paramref name="text"/> (the current text of <paramref name="html"/>); 0 without a book.
    /// </summary>
    int CountSameSpansInBook(HtmlResource html, string text, int offset);

    /// <summary>
    /// "Remove span" — plans the removal (<see cref="SpanRemoval.Plan"/>) with the consequences for the styling.
    /// For <see cref="SpanRemovalScope.Book"/> the open tabs are saved first. <c>null</c> without a book or a span.
    /// </summary>
    SpanRemovalPlan? PlanSpanRemoval(HtmlResource html, string text, int offset, SpanRemovalScope scope);

    /// <summary>Writes a book-wide "Remove span" into the files (with a checkpoint) and refreshes the tabs and panels.</summary>
    void ApplySpanRemovalInBook(SpanRemovalPlan plan);

    /// <summary>"View Image" — shows the image with the given book path in the preview window.</summary>
    void ViewImage(string bookPath);

    /// <summary>
    /// "Footnotes" submenu — the footnote under <paramref name="offset"/> of <paramref name="html"/> and whether one can
    /// be added there. Saves the open tabs first (the footnotes are found in the book's texts). <c>null</c> without a book.
    /// </summary>
    FootnoteMenuState? GetFootnoteState(HtmlResource html, int offset);

    /// <summary>The placement chosen the last time a footnote was added (the default of the "Add Footnote" dialog).</summary>
    FootnotePlacement LastFootnotePlacement { get; }

    /// <summary>Whether <paramref name="content"/> can be the text of a footnote of the current book.</summary>
    FootnoteError ValidateFootnoteContent(string content);

    /// <summary>
    /// "Add Footnote" — adds a footnote at <paramref name="offset"/> of <paramref name="html"/> (with a checkpoint),
    /// refreshes the tabs and panels and puts the caret on the new reference.
    /// </summary>
    void AddFootnote(HtmlResource html, int offset, string content, FootnotePlacement placement);

    /// <summary>"Remove Footnote" — removes the reference and the note (with a checkpoint).</summary>
    void RemoveFootnote(FootnoteHit hit);

    /// <summary>"Move to global/local footnotes" — moves the note to the other placement (with a checkpoint).</summary>
    void MoveFootnote(FootnoteHit hit);

    /// <summary>"Go to note/reference" — opens the other part of the footnote of <paramref name="hit"/>.</summary>
    void GoToFootnote(FootnoteHit hit);

    /// <summary>"Renumber Footnotes" — renumbers the footnotes of the book (with a checkpoint).</summary>
    void RenumberFootnotes();
}

/// <summary>The footnote state at a position of a file (<see cref="ICodeTabHost.GetFootnoteState"/>).</summary>
/// <param name="Hit">The footnote whose reference or note contains the position, or <c>null</c>.</param>
/// <param name="AddError">Why a footnote cannot be added at the position (<see cref="FootnoteError.None"/> when it can).</param>
/// <param name="HasFootnotes">Whether the book has any footnotes.</param>
public sealed record FootnoteMenuState(FootnoteHit? Hit, FootnoteError AddError, bool HasFootnotes);
