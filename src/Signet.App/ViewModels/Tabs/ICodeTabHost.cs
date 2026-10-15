using System.Collections.Generic;
using System.Threading.Tasks;
using Avalonia.Input;
using Signet.App.Actions;
using Signet.Core.BookManipulation;
using Signet.Core.Footnotes;
using Signet.Core.MiscEditors;
using Signet.Core.Resources;

namespace Signet.App.ViewModels.Tabs;

/// <summary>
/// Main window services needed by the Code Editor context menu (the editor reaches the
/// main window for clips, actions, book operations and the image preview). Implemented by
/// <c>MainWindowViewModel</c>; <c>TabManager</c> passes it to every new code tab.
/// </summary>
public interface ICodeTabHost
{
    /// <summary>
    /// The actions whose shortcuts work only in a code editor (<see cref="Actions.AppActionIds.IsCodeEditorAction"/>):
    /// every code tab view binds them while it is shown.
    /// </summary>
    IReadOnlyList<AppAction> CodeEditorShortcutActions { get; }

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

    /// <summary>
    /// The numbers a footnote added at <paramref name="offset"/> of <paramref name="html"/> gets as a local and as a
    /// global one (shown in the "Add Footnote" dialog); 1 and 1 without a book.
    /// </summary>
    (int Local, int Global) GetFootnoteNumbers(HtmlResource html, int offset);

    /// <summary>Whether <paramref name="content"/> can be the text of a footnote of the current book.</summary>
    FootnoteError ValidateFootnoteContent(string content);

    /// <summary>
    /// "Add Footnote" — adds a footnote at <paramref name="offset"/> of <paramref name="html"/> (with a checkpoint),
    /// refreshes the tabs and panels and puts the caret on the new reference.
    /// </summary>
    void AddFootnote(HtmlResource html, int offset, string content, FootnotePlacement placement);

    /// <summary>
    /// "Edit Footnote" — the data of the footnote window for the footnote of <paramref name="hit"/> (saves the open tabs
    /// first); <c>null</c> (with a message) when the footnote is gone.
    /// </summary>
    FootnoteEditState? GetFootnoteEdit(FootnoteHit hit);

    /// <summary>Whether <paramref name="content"/> can replace the text of the note of <paramref name="footnote"/>.</summary>
    FootnoteError ValidateFootnoteEdit(Footnote footnote, string content);

    /// <summary>
    /// "Edit Footnote" — replaces the text of the note and moves it to <paramref name="placement"/> when it is elsewhere
    /// (with a checkpoint), refreshes the tabs and panels and puts the caret back on the part of the footnote of
    /// <paramref name="hit"/>.
    /// </summary>
    void EditFootnote(FootnoteHit hit, string content, FootnotePlacement placement);

    /// <summary>"Remove" in the footnote window — removes the reference and the note (with a checkpoint).</summary>
    void RemoveFootnote(FootnoteHit hit);

    /// <summary>
    /// The preview of the footnote window: the markup of a footnote with <paramref name="content"/> as footnote
    /// <paramref name="number"/> of <paramref name="placement"/> — a new one at <paramref name="referenceStart"/> of
    /// <paramref name="html"/>, or <paramref name="footnote"/> with its reference at
    /// <paramref name="referenceStart"/>..<paramref name="referenceEnd"/>; <c>null</c> when the text is not valid. Changes
    /// nothing (called on every change of the text).
    /// </summary>
    FootnoteMarkup? PreviewFootnote(
        HtmlResource html, int referenceStart, int referenceEnd, string content, FootnotePlacement placement, int number,
        Footnote? footnote);

    /// <summary>Whether removing a footnote asks for a confirmation (Preferences → Footnotes).</summary>
    bool ConfirmFootnoteRemoval { get; set; }

    /// <summary>"Go to note/reference" — opens the other part of the footnote of <paramref name="hit"/>.</summary>
    void GoToFootnote(FootnoteHit hit);

    /// <summary>
    /// "Renumber Footnotes" — renumbers Signet's footnotes of the book (with a checkpoint); footnotes of other origin are
    /// left as they are, with a warning.
    /// </summary>
    void RenumberFootnotes();

    /// <summary>"Convert Footnotes of Other Origin…" — opens the conversion dialog.</summary>
    void ShowFootnoteConversion();
}

/// <summary>The footnote state at a position of a file (<see cref="ICodeTabHost.GetFootnoteState"/>).</summary>
/// <param name="Hit">The footnote whose reference or note contains the position, or <c>null</c>.</param>
/// <param name="AddError">Why a footnote cannot be added at the position (<see cref="FootnoteError.None"/> when it can).</param>
/// <param name="HasFootnotes">Whether the book has any footnotes.</param>
/// <param name="ForeignCount">How many footnotes of the book are not Signet's (Signet edits them only after converting them).</param>
public sealed record FootnoteMenuState(FootnoteHit? Hit, FootnoteError AddError, bool HasFootnotes, int ForeignCount = 0);

/// <summary>The data of the footnote window for editing a footnote (<see cref="ICodeTabHost.GetFootnoteEdit"/>).</summary>
/// <param name="Footnote">The footnote (of the current texts).</param>
/// <param name="ReferenceText">The source of the file of the reference.</param>
/// <param name="ReferenceStart">Where the reference starts in it.</param>
/// <param name="ReferenceEnd">Where the reference ends in it.</param>
/// <param name="Content">The text of the note.</param>
/// <param name="LocalNumber">The number of the footnote as a local one.</param>
/// <param name="GlobalNumber">The number of the footnote as a global one.</param>
public sealed record FootnoteEditState(
    Footnote Footnote, string ReferenceText, int ReferenceStart, int ReferenceEnd, FootnoteContent Content,
    int LocalNumber, int GlobalNumber);
