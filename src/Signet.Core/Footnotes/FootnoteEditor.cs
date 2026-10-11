using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Net;
using System.Text;
using System.Text.RegularExpressions;
using Signet.Core.BookManipulation;
using Signet.Core.Resources;

namespace Signet.Core.Footnotes;

/// <summary>Why a footnote cannot be added at a position.</summary>
public enum FootnoteError
{
    /// <summary>No problem.</summary>
    None,

    /// <summary>The position is not in <c>&lt;body&gt;</c>.</summary>
    NotInBody,

    /// <summary>The position is inside a tag.</summary>
    InsideTag,

    /// <summary>The position is inside a link (a footnote reference is a link itself).</summary>
    InsideLink,

    /// <summary>The position is inside a footnote (its reference or its note).</summary>
    InsideFootnote,

    /// <summary>The text of the footnote is empty.</summary>
    EmptyContent,

    /// <summary>The text of the footnote is not well-formed XHTML.</summary>
    InvalidContent,

    /// <summary>The new text of a note leaves out its link back to the reference.</summary>
    MissingBacklink,

    /// <summary>The footnote (its reference or its note) is no longer in the book.</summary>
    NotFound,
}

/// <summary>A footnote and the part of it a position is in (<see cref="FootnoteEditor.FindAt"/>).</summary>
/// <param name="Footnote">The footnote.</param>
/// <param name="Side">The part the position is in.</param>
public sealed record FootnoteHit(Footnote Footnote, FootnoteSide Side);

/// <summary>The text of a note as <see cref="FootnoteEditor.Edit"/> replaces it (<see cref="FootnoteEditor.GetContent"/>).</summary>
/// <param name="Text">The text (an XHTML fragment).</param>
/// <param name="IsWholeNote">
/// <c>true</c> — the whole inside of the note, its link back included (a note not in Signet's template); <c>false</c> —
/// the text after the link back of Signet's note.
/// </param>
public sealed record FootnoteContent(string Text, bool IsWholeNote);

/// <summary>
/// Adds, edits, removes, moves and renumbers footnotes (see <see cref="FootnoteScanner"/> for what a footnote is). The
/// changes are text edits of the files involved: the rest of the source keeps its formatting.
/// </summary>
/// <remarks>
/// <para>
/// Signet's footnotes follow one template, styled by the rules of its classes (<see cref="FootnoteStyles"/>, kept up
/// to date by <see cref="Renumber"/> in the file's stylesheet or a <c>&lt;style&gt;</c> element). The reference:
/// <c>&lt;a class="signet-fn-ref" epub:type="noteref" href="#fn-1" id="fn-1-ref"&gt;[1]&lt;/a&gt;</c>. The note:
/// <c>&lt;aside class="signet-fn" epub:type="footnote" id="fn-1"&gt;&lt;p&gt;&lt;a href="#fn-1-ref"&gt;1.&lt;/a&gt; text&lt;/p&gt;&lt;/aside&gt;</c>.
/// Local notes go to <c>&lt;hr class="signet-fn-separator"/&gt;</c> + <c>&lt;section class="signet-footnotes"&gt;</c>
/// at the end of the file's body; global ones (endnotes, ids <c>en-N</c>) to <c>&lt;section class="signet-endnotes"&gt;</c>
/// of <see cref="GlobalFileName"/>, created at the end of the reading order when needed. EPUB 2 has no
/// <c>aside</c>/<c>section</c>/<c>epub:type</c>: <c>div</c> elements without it are used instead.
/// </para>
/// <para>
/// Numbering (<see cref="Renumber"/>, run after every change): local footnotes from 1 in each file, global ones
/// continuously through the book, both in reading order of the references. Signet's footnotes get the template's
/// markers and ids; for other footnotes only a marker that holds exactly one number has it replaced (ids stay).
/// An emptied container goes away, and so does an emptied notes file.
/// </para>
/// </remarks>
public static class FootnoteEditor
{
    /// <summary>The name of the file with the global footnotes.</summary>
    public const string GlobalFileName = "Przypisy.xhtml";

    /// <summary>The title and heading of the global footnotes file.</summary>
    public const string GlobalHeading = "Przypisy";

    /// <summary>The class of Signet's notes.</summary>
    public const string NoteClass = "signet-fn";

    /// <summary>The class of Signet's references.</summary>
    public const string ReferenceClass = "signet-fn-ref";

    /// <summary>The class of the container of the local notes of a file.</summary>
    public const string LocalContainerClass = "signet-footnotes";

    /// <summary>The class of the container of the global notes.</summary>
    public const string GlobalContainerClass = "signet-endnotes";

    /// <summary>The class of the line above the local notes.</summary>
    public const string SeparatorClass = "signet-fn-separator";

    private static readonly Regex StyleElement = new(
        @"(<style\b[^>]*>)(.*?)</style>", RegexOptions.CultureInvariant | RegexOptions.IgnoreCase | RegexOptions.Singleline);

    private static readonly Regex SignetId = new(@"^(fn|en)-(\d+)$", RegexOptions.CultureInvariant);

    private static readonly Regex Tag = new("<[^>]*>", RegexOptions.CultureInvariant);

    // The wrappers that go with a removed reference when they hold nothing else.
    private static readonly string[] WrapperNames = { "span", "sup" };

    /// <summary>The footnote whose reference or note contains <paramref name="offset"/> of <paramref name="html"/>.</summary>
    public static FootnoteHit? FindAt(Book book, HtmlResource html, int offset)
    {
        ArgumentNullException.ThrowIfNull(book);
        ArgumentNullException.ThrowIfNull(html);
        IReadOnlyList<Footnote> footnotes = FootnoteScanner.Scan(book);
        string text = html.GetText();
        TagLister tags = new(text);

        // A reference inside a note is the more specific hit.
        foreach (Footnote footnote in footnotes.Where(f => ReferenceEquals(f.ReferenceFile, html)))
        {
            if (FindAnchor(tags, text, footnote.ReferenceHref, footnote.ReferenceOccurrence) is { } anchor
                && offset >= anchor.OuterStart && offset <= anchor.OuterEnd)
            {
                return new FootnoteHit(footnote, FootnoteSide.Reference);
            }
        }

        foreach (Footnote footnote in footnotes.Where(f => ReferenceEquals(f.NoteFile, html)))
        {
            if (FindElementById(tags, text, footnote.NoteId) is { } note && offset >= note.Start && offset <= note.End)
            {
                return new FootnoteHit(footnote, FootnoteSide.Note);
            }
        }

        return null;
    }

    /// <summary>
    /// The number a footnote added at <paramref name="offset"/> of <paramref name="html"/> gets with
    /// <paramref name="placement"/> (as <see cref="Renumber"/> counts): one more than the local footnotes of the file
    /// before it, or than the global footnotes before it in reading order.
    /// </summary>
    public static int NumberAt(Book book, HtmlResource html, int offset, FootnotePlacement placement)
    {
        ArgumentNullException.ThrowIfNull(book);
        ArgumentNullException.ThrowIfNull(html);
        int spine = book.GetHtmlResources().ToList().IndexOf(html);
        int before = 0;
        foreach (Footnote footnote in FootnoteScanner.Scan(book).Where(f => f.Placement == placement))
        {
            bool sameFile = ReferenceEquals(footnote.ReferenceFile, html);
            if (placement == FootnotePlacement.Local && !sameFile)
            {
                continue;
            }

            bool precedes = sameFile
                ? Locate(footnote, FootnoteSide.Reference) is { } place && place.Offset < offset
                : footnote.SpineIndex < spine;
            if (precedes)
            {
                before++;
            }
        }

        return before + 1;
    }

    /// <summary>The file and offset of the reference or the note of <paramref name="footnote"/>; <c>null</c> when gone.</summary>
    public static (HtmlResource File, int Offset)? Locate(Footnote footnote, FootnoteSide side)
    {
        ArgumentNullException.ThrowIfNull(footnote);
        if (side == FootnoteSide.Reference)
        {
            string text = footnote.ReferenceFile.GetText();
            return FindAnchor(new TagLister(text), text, footnote.ReferenceHref, footnote.ReferenceOccurrence) is { } anchor
                ? (footnote.ReferenceFile, anchor.Start)
                : null;
        }

        string noteText = footnote.NoteFile.GetText();
        return FindElementById(new TagLister(noteText), noteText, footnote.NoteId) is { } note
            ? (footnote.NoteFile, note.Start)
            : null;
    }

    /// <summary>Whether a footnote can be added at <paramref name="offset"/> of <paramref name="html"/>.</summary>
    public static FootnoteError CanAdd(Book book, HtmlResource html, int offset)
    {
        ArgumentNullException.ThrowIfNull(book);
        ArgumentNullException.ThrowIfNull(html);
        string text = html.GetText();
        TagLister tags = new(text);
        if (offset < 0 || offset > text.Length || !tags.IsPositionInBody(offset))
        {
            return FootnoteError.NotInBody;
        }

        int last = tags.FindLastTagOnOrBefore(offset);
        if (last >= 0 && tags.At(last) is { } tag && offset > tag.Pos && offset < tag.Pos + tag.Len)
        {
            return FootnoteError.InsideTag;
        }

        foreach (TagLister.TagInfo open in OpenElementsAt(tags, offset))
        {
            string tagText = text.Substring(open.Pos, open.Len);
            if (open.TagName.Equals("a", StringComparison.OrdinalIgnoreCase))
            {
                return FootnoteError.InsideLink;
            }

            if (HasClass(tagText, LocalContainerClass) || HasClass(tagText, GlobalContainerClass))
            {
                return FootnoteError.InsideFootnote;
            }
        }

        return FindAt(book, html, offset) is null ? FootnoteError.None : FootnoteError.InsideFootnote;
    }

    /// <summary>Whether <paramref name="content"/> can be the text of a footnote (a well-formed XHTML fragment).</summary>
    public static FootnoteError ValidateContent(string content, string epubVersion)
    {
        ArgumentNullException.ThrowIfNull(content);
        if (content.Trim().Length == 0)
        {
            return FootnoteError.EmptyContent;
        }

        string document = "<?xml version=\"1.0\" encoding=\"utf-8\"?>\n<!DOCTYPE html>\n"
            + "<html xmlns=\"http://www.w3.org/1999/xhtml\"><head><title>t</title></head><body><p>"
            + content.Trim() + "</p></body></html>";
        return WellFormedChecker.CheckXhtmlStructure(document, epubVersion).IsWellFormed
            ? FootnoteError.None
            : FootnoteError.InvalidContent;
    }

    /// <summary>
    /// Adds a footnote with <paramref name="content"/> (an XHTML fragment) at <paramref name="offset"/> of
    /// <paramref name="html"/> and renumbers the footnotes.
    /// </summary>
    public static FootnoteError Add(Book book, HtmlResource html, int offset, string content, FootnotePlacement placement) =>
        Add(book, html, offset, content, placement, out _);

    /// <summary>
    /// Adds a footnote like <see cref="Add(Book, HtmlResource, int, string, FootnotePlacement)"/>;
    /// <paramref name="added"/> is the new footnote after the renumbering (<c>null</c> when it was not added).
    /// </summary>
    public static FootnoteError Add(
        Book book, HtmlResource html, int offset, string content, FootnotePlacement placement, out Footnote? added,
        FootnoteStyles? styles = null)
    {
        added = null;
        ArgumentNullException.ThrowIfNull(book);
        ArgumentNullException.ThrowIfNull(html);
        ArgumentNullException.ThrowIfNull(content);
        FootnoteError error = CanAdd(book, html, offset);
        if (error == FootnoteError.None)
        {
            error = ValidateContent(content, book.EpubVersion);
        }

        if (error != FootnoteError.None)
        {
            return error;
        }

        bool epub3 = book.IsEpub3;
        HtmlResource noteFile = placement == FootnotePlacement.Local ? html : GetOrCreateGlobalFile(book, html);

        // Temporary ids: the renumbering right after gives the final ones.
        string noteId = "fn-new-" + Ulid.NewUlid().ToString().ToLowerInvariant();
        string referenceId = noteId + "-ref";
        string reference = "<a class=\"" + ReferenceClass + "\"" + (epub3 ? " epub:type=\"noteref\"" : string.Empty)
            + " href=\"" + Href(html, noteFile, noteId) + "\" id=\"" + referenceId + "\">[0]</a>";
        html.SetText(html.GetText().Insert(offset, reference));

        string tag = epub3 ? "aside" : "div";
        string type = !epub3 ? string.Empty : placement == FootnotePlacement.Local ? " epub:type=\"footnote\"" : " epub:type=\"endnote\"";
        string note = "<" + tag + " class=\"" + NoteClass + "\"" + type + " id=\"" + noteId + "\"><p><a href=\""
            + Href(noteFile, html, referenceId) + "\">0.</a> " + content.Trim() + "</p></" + tag + ">";
        InsertNote(noteFile, note, placement, epub3);

        int index = FootnoteScanner.Scan(book).ToList()
            .FindIndex(f => ReferenceEquals(f.ReferenceFile, html) && f.ReferenceId == referenceId);
        Renumber(book, styles);
        added = Rescan(book, index);
        book.Modified = true;
        return FootnoteError.None;
    }

    /// <summary>
    /// Removes the reference and the note of <paramref name="footnote"/> and renumbers the footnotes. With the reference
    /// go the <c>&lt;span&gt;</c>/<c>&lt;sup&gt;</c> elements that held only it, and the empty element its link back led
    /// to (with the empty <c>&lt;span&gt;</c>/<c>&lt;sup&gt;</c> around it) — unless another link leads to their id.
    /// </summary>
    public static bool Remove(Book book, Footnote footnote, FootnoteStyles? styles = null)
    {
        ArgumentNullException.ThrowIfNull(book);
        ArgumentNullException.ThrowIfNull(footnote);

        string referenceText = footnote.ReferenceFile.GetText();
        TagLister referenceTags = new(referenceText);
        Anchor? anchor = FindAnchor(referenceTags, referenceText, footnote.ReferenceHref, footnote.ReferenceOccurrence);
        string noteText = footnote.NoteFile.GetText();
        Element? note = FindElementById(new TagLister(noteText), noteText, footnote.NoteId);
        if (anchor is null || note is null)
        {
            return false;
        }

        // The element the link back leads to goes with the footnote only when nothing else links to it.
        string? returnId = footnote.ReferenceId.Length > 0 && !IsLinkedOutside(book, footnote, note) ? footnote.ReferenceId : null;
        (int start, int end) = Unwrap(referenceTags, referenceText, anchor.OuterStart, anchor.OuterEnd, returnId);
        List<TextEdit> referenceEdits = new() { new TextEdit(start, end - start, string.Empty) };
        if (returnId is not null
            && FindElementById(referenceTags, referenceText, returnId) is { } target
            && (target.End <= start || target.Start >= end)
            && Tag.Replace(referenceText[target.Start..target.End], string.Empty).Trim().Length == 0)
        {
            (int targetStart, int targetEnd) = Unwrap(referenceTags, referenceText, target.Start, target.End, null);
            referenceEdits.Add(new TextEdit(targetStart, targetEnd - targetStart, string.Empty));
        }

        (int noteStart, int noteEnd) = WholeLines(noteText, note.Start, note.End);
        TextEdit noteEdit = new(noteStart, noteEnd - noteStart, string.Empty);
        if (ReferenceEquals(footnote.ReferenceFile, footnote.NoteFile))
        {
            referenceEdits.Add(noteEdit);
            footnote.ReferenceFile.SetText(Apply(referenceText, referenceEdits));
        }
        else
        {
            footnote.ReferenceFile.SetText(Apply(referenceText, referenceEdits));
            footnote.NoteFile.SetText(Apply(noteText, new[] { noteEdit }));
        }

        RemoveEmptyContainer(book, footnote.NoteFile);
        Renumber(book, styles);
        book.Modified = true;
        return true;
    }

    /// <summary>
    /// Moves the note of <paramref name="footnote"/> to the other placement (local to global or back) and renumbers
    /// the footnotes.
    /// </summary>
    public static bool Move(Book book, Footnote footnote) => Move(book, footnote, out _);

    /// <summary>
    /// Moves a footnote like <see cref="Move(Book, Footnote)"/>; <paramref name="moved"/> is the footnote after the
    /// renumbering (<c>null</c> when it was not moved).
    /// </summary>
    public static bool Move(Book book, Footnote footnote, out Footnote? moved, FootnoteStyles? styles = null)
    {
        moved = null;
        ArgumentNullException.ThrowIfNull(book);
        ArgumentNullException.ThrowIfNull(footnote);
        int index = IndexOf(book, footnote);

        HtmlResource oldFile = footnote.NoteFile;
        string oldText = oldFile.GetText();
        Element? element = FindElementById(new TagLister(oldText), oldText, footnote.NoteId);
        if (element is null)
        {
            return false;
        }

        FootnotePlacement target = footnote.Placement == FootnotePlacement.Local ? FootnotePlacement.Global : FootnotePlacement.Local;
        HtmlResource newFile = target == FootnotePlacement.Local
            ? footnote.ReferenceFile
            : GetOrCreateGlobalFile(book, footnote.ReferenceFile);
        string note = oldText[element.Start..element.End];
        (int start, int end) = WholeLines(oldText, element.Start, element.End);
        oldFile.SetText(oldText.Remove(start, end - start));

        // The note's link back now starts from its new file; its id must be free there.
        string noteId = footnote.NoteId;
        HashSet<string> ids = MarkupEdits.CollectIds(newFile.GetText());
        if (ids.Contains(noteId))
        {
            noteId = MarkupEdits.UniqueId(noteId, ids);
        }

        note = RetypeNote(note, target, noteId);
        if (footnote.ReferenceId.Length > 0)
        {
            note = MarkupEdits.ReplaceAttributeValues(note, MarkupEdits.LinkAttributes, href =>
                SplitHref(href).Fragment == footnote.ReferenceId ? Href(newFile, footnote.ReferenceFile, footnote.ReferenceId) : null);
        }

        string referenceText = footnote.ReferenceFile.GetText();
        if (FindAnchor(new TagLister(referenceText), referenceText, footnote.ReferenceHref, footnote.ReferenceOccurrence) is { } anchor
            && anchor.Href is { } href)
        {
            footnote.ReferenceFile.SetText(Apply(
                referenceText, new[] { new TextEdit(href.Start, href.Length, Encode(Href(footnote.ReferenceFile, newFile, noteId))) }));
        }

        InsertNote(newFile, note, target, book.IsEpub3);
        RemoveEmptyContainer(book, oldFile);
        Renumber(book, styles);
        moved = Rescan(book, index);
        book.Modified = true;
        return true;
    }

    /// <summary>
    /// The text of the note of <paramref name="footnote"/> as <see cref="Edit"/> replaces it: of Signet's note (one
    /// <c>&lt;p&gt;</c> that starts with the link back) the text after the link back, of any other note its whole inside
    /// (the link back included). Trimmed; <c>null</c> when the note is gone.
    /// </summary>
    public static FootnoteContent? GetContent(Footnote footnote)
    {
        ArgumentNullException.ThrowIfNull(footnote);
        string text = footnote.NoteFile.GetText();
        return ContentOf(footnote, text) is { } content
            ? new FootnoteContent(text[content.Start..content.End], !content.AfterBacklink)
            : null;
    }

    /// <summary>
    /// Whether <paramref name="content"/> can replace the text of the note of <paramref name="footnote"/>
    /// (<see cref="GetContent"/>): well-formed XHTML, and the whole inside of a note keeps its link back.
    /// </summary>
    public static FootnoteError ValidateEdit(Footnote footnote, string content, string epubVersion)
    {
        ArgumentNullException.ThrowIfNull(footnote);
        ArgumentNullException.ThrowIfNull(content);
        string text = footnote.NoteFile.GetText();
        if (ContentOf(footnote, text) is not { } current)
        {
            return FootnoteError.NotFound;
        }

        if (current.AfterBacklink)
        {
            return ValidateContent(content, epubVersion);
        }

        if (content.Trim().Length == 0)
        {
            return FootnoteError.EmptyContent;
        }

        string document = "<?xml version=\"1.0\" encoding=\"utf-8\"?>\n<!DOCTYPE html>\n"
            + "<html xmlns=\"http://www.w3.org/1999/xhtml\" xmlns:epub=\"http://www.idpf.org/2007/ops\"><head><title>t</title></head>"
            + "<body><div>" + content.Trim() + "</div></body></html>";
        if (!WellFormedChecker.CheckXhtmlStructure(document, epubVersion).IsWellFormed)
        {
            return FootnoteError.InvalidContent;
        }

        return footnote.ReferenceId.Length > 0
            && HasBacklink(text[current.Start..current.End], footnote.ReferenceId)
            && !HasBacklink(content, footnote.ReferenceId)
                ? FootnoteError.MissingBacklink
                : FootnoteError.None;
    }

    /// <summary>
    /// Replaces the text of the note of <paramref name="footnote"/> (<see cref="GetContent"/>) with
    /// <paramref name="content"/> and moves the note to <paramref name="placement"/> when it is elsewhere (renumbering
    /// the footnotes then); <paramref name="edited"/> is the footnote afterwards (<c>null</c> when it was not edited).
    /// </summary>
    public static FootnoteError Edit(
        Book book, Footnote footnote, string content, FootnotePlacement placement, out Footnote? edited,
        FootnoteStyles? styles = null)
    {
        edited = null;
        ArgumentNullException.ThrowIfNull(book);
        ArgumentNullException.ThrowIfNull(footnote);
        ArgumentNullException.ThrowIfNull(content);
        FootnoteError error = ValidateEdit(footnote, content, book.EpubVersion);
        if (error != FootnoteError.None)
        {
            return error;
        }

        string text = footnote.NoteFile.GetText();
        NoteContent current = ContentOf(footnote, text)!;
        string replacement = content.Trim();
        if (text[current.Start..current.End] != replacement)
        {
            footnote.NoteFile.SetText(string.Concat(text.AsSpan(0, current.Start), replacement, text.AsSpan(current.End)));
            book.Modified = true;
        }

        if (Refresh(book, footnote) is not { } changed)
        {
            return FootnoteError.NotFound;
        }

        if (changed.Placement == placement)
        {
            edited = changed;
            return FootnoteError.None;
        }

        return Move(book, changed, out edited, styles) ? FootnoteError.None : FootnoteError.NotFound;
    }

    /// <summary>
    /// The file of the reference of <paramref name="footnote"/> and the range of the reference in it (with the
    /// <c>&lt;sup&gt;</c> around the link); <c>null</c> when it is gone.
    /// </summary>
    public static (HtmlResource File, int Start, int End)? LocateReferenceRange(Footnote footnote)
    {
        ArgumentNullException.ThrowIfNull(footnote);
        string text = footnote.ReferenceFile.GetText();
        return FindAnchor(new TagLister(text), text, footnote.ReferenceHref, footnote.ReferenceOccurrence) is { } anchor
            ? (footnote.ReferenceFile, anchor.OuterStart, anchor.OuterEnd)
            : null;
    }

    /// <summary>
    /// Numbers the footnotes: local ones from 1 in each file, global ones through the book, in reading order of the
    /// references, and puts the notes of Signet's containers in that order. The files with Signet's footnotes get the
    /// rules of <paramref name="styles"/> (<see cref="FootnoteStyles.Default"/> when <c>null</c>). Returns whether
    /// anything changed.
    /// </summary>
    public static bool Renumber(Book book, FootnoteStyles? styles = null)
    {
        ArgumentNullException.ThrowIfNull(book);
        IReadOnlyList<Footnote> footnotes = FootnoteScanner.Scan(book);
        Dictionary<HtmlResource, (string Text, TagLister Tags)> snapshots = new(ReferenceEqualityComparer.Instance);
        Dictionary<HtmlResource, List<TextEdit>> edits = new(ReferenceEqualityComparer.Instance);
        Dictionary<HtmlResource, HashSet<string>> takenIds = new(ReferenceEqualityComparer.Instance);
        Dictionary<HtmlResource, int> localCounters = new(ReferenceEqualityComparer.Instance);
        int globalCounter = 0;

        (string Text, TagLister Tags) Snapshot(HtmlResource file)
        {
            if (!snapshots.TryGetValue(file, out (string Text, TagLister Tags) snapshot))
            {
                string text = file.GetText();
                snapshots[file] = snapshot = (text, new TagLister(text));
            }

            return snapshot;
        }

        void AddEdit(HtmlResource file, TextEdit edit)
        {
            if (!edits.TryGetValue(file, out List<TextEdit>? list))
            {
                edits[file] = list = new List<TextEdit>();
            }

            list.Add(edit);
        }

        // The ids Signet's footnotes give up get handed out again; every other id of a file stays taken.
        HashSet<string> Taken(HtmlResource file)
        {
            if (!takenIds.TryGetValue(file, out HashSet<string>? taken))
            {
                taken = MarkupEdits.CollectIds(Snapshot(file).Text);
                foreach (Footnote footnote in footnotes.Where(f => f.IsSignet))
                {
                    if (ReferenceEquals(footnote.NoteFile, file))
                    {
                        taken.Remove(footnote.NoteId);
                    }

                    if (ReferenceEquals(footnote.ReferenceFile, file))
                    {
                        taken.Remove(footnote.ReferenceId);
                    }
                }

                takenIds[file] = taken;
            }

            return taken;
        }

        foreach (Footnote footnote in footnotes)
        {
            int number;
            if (footnote.Placement == FootnotePlacement.Local)
            {
                number = localCounters.GetValueOrDefault(footnote.ReferenceFile) + 1;
                localCounters[footnote.ReferenceFile] = number;
            }
            else
            {
                number = ++globalCounter;
            }

            string n = number.ToString(CultureInfo.InvariantCulture);
            string? noteId = null;
            string? referenceId = null;
            if (footnote.IsSignet)
            {
                string prefix = footnote.Placement == FootnotePlacement.Local ? "fn-" : "en-";
                noteId = MarkupEdits.UniqueId(prefix + n, Taken(footnote.NoteFile));
                referenceId = MarkupEdits.UniqueId(noteId + "-ref", Taken(footnote.ReferenceFile));
            }

            (string referenceText, TagLister referenceTags) = Snapshot(footnote.ReferenceFile);
            Anchor? anchor = FindAnchor(referenceTags, referenceText, footnote.ReferenceHref, footnote.ReferenceOccurrence);
            (string noteText, TagLister noteTags) = Snapshot(footnote.NoteFile);
            Element? note = FindElementById(noteTags, noteText, footnote.NoteId);
            if (anchor is null || note is null)
            {
                continue;
            }

            string inner = referenceText[anchor.InnerStart..anchor.InnerEnd];
            string? marker = footnote.IsSignet ? "[" + n + "]" : ReplaceSingleNumber(inner, number);
            if (marker is not null && marker != inner)
            {
                AddEdit(footnote.ReferenceFile, new TextEdit(anchor.InnerStart, anchor.InnerEnd - anchor.InnerStart, marker));
            }

            if (noteId is not null && referenceId is not null)
            {
                AddAttributeEdit(footnote.ReferenceFile, anchor.Href, Href(footnote.ReferenceFile, footnote.NoteFile, noteId));
                AddAttributeEdit(footnote.ReferenceFile, anchor.Id, referenceId);
                AddAttributeEdit(footnote.NoteFile, note.Id, noteId);
            }

            if (footnote.ReferenceId.Length > 0 && FindBacklink(noteTags, noteText, note, footnote.ReferenceId) is { } backlink)
            {
                string backInner = noteText[backlink.InnerStart..backlink.InnerEnd];
                string? backMarker = footnote.IsSignet ? n + "." : ReplaceSingleNumber(backInner, number);
                if (backMarker is not null && backMarker != backInner)
                {
                    AddEdit(footnote.NoteFile, new TextEdit(backlink.InnerStart, backlink.InnerEnd - backlink.InnerStart, backMarker));
                }

                if (referenceId is not null)
                {
                    AddAttributeEdit(footnote.NoteFile, backlink.Href, Href(footnote.NoteFile, footnote.ReferenceFile, referenceId));
                }
            }
        }

        void AddAttributeEdit(HtmlResource file, Span? value, string newValue)
        {
            if (value is { } span && WebUtility.HtmlDecode(Snapshot(file).Text.Substring(span.Start, span.Length)) != newValue)
            {
                AddEdit(file, new TextEdit(span.Start, span.Length, Encode(newValue)));
            }
        }

        bool changed = false;
        foreach ((HtmlResource file, List<TextEdit> list) in edits)
        {
            file.SetText(Apply(snapshots[file].Text, list));
            changed = true;
        }

        foreach (HtmlResource file in book.GetHtmlResources())
        {
            changed |= SortContainer(file, LocalContainerClass) | SortContainer(file, GlobalContainerClass);
        }

        HashSet<HtmlResource> styled = new(ReferenceEqualityComparer.Instance);
        foreach (Footnote footnote in footnotes.Where(f => f.IsSignet))
        {
            styled.Add(footnote.ReferenceFile);
            styled.Add(footnote.NoteFile);
        }

        foreach (HtmlResource file in book.GetHtmlResources().Where(styled.Contains))
        {
            changed |= ApplyStyles(book, file, styles ?? FootnoteStyles.Default);
        }

        if (changed)
        {
            book.Modified = true;
        }

        return changed;
    }

    /// <summary>
    /// The footnote of the current texts with the same reference as <paramref name="footnote"/> (the same <c>href</c>
    /// and occurrence in the same file), e.g. one found before the open tabs were saved; <c>null</c> when there is none.
    /// After a renumbering another footnote may have that reference, so it is meant for a footnote of the same state.
    /// </summary>
    public static Footnote? Refresh(Book book, Footnote footnote)
    {
        ArgumentNullException.ThrowIfNull(book);
        ArgumentNullException.ThrowIfNull(footnote);
        return Rescan(book, IndexOf(book, footnote));
    }

    // The place of the footnote with the reference of `footnote` in the reading order; -1 when there is none. The
    // edits keep the references and their order, so the same place finds the footnote again after them.
    private static int IndexOf(Book book, Footnote footnote) =>
        FootnoteScanner.Scan(book).ToList().FindIndex(f =>
            ReferenceEquals(f.ReferenceFile, footnote.ReferenceFile)
            && f.ReferenceHref == footnote.ReferenceHref
            && f.ReferenceOccurrence == footnote.ReferenceOccurrence);

    private static Footnote? Rescan(Book book, int index)
    {
        if (index < 0)
        {
            return null;
        }

        IReadOnlyList<Footnote> footnotes = FootnoteScanner.Scan(book);
        return index < footnotes.Count ? footnotes[index] : null;
    }

    /// <summary>The file with Signet's global footnotes, or <c>null</c> when the book has none.</summary>
    public static HtmlResource? FindGlobalFile(Book book)
    {
        ArgumentNullException.ThrowIfNull(book);
        return book.GetHtmlResources().FirstOrDefault(h =>
        {
            string text = h.GetText();
            return text.Contains(GlobalContainerClass, StringComparison.Ordinal)
                && FindContainer(new TagLister(text), text, GlobalContainerClass) is not null;
        });
    }

    internal static (string Path, string Fragment) SplitHref(string href)
    {
        string trimmed = href.Trim();
        int hash = trimmed.IndexOf('#', StringComparison.Ordinal);
        return hash < 0 ? (trimmed, string.Empty) : (trimmed[..hash], Uri.UnescapeDataString(trimmed[(hash + 1)..]));
    }

    internal static bool IsExternal(string path)
    {
        int colon = path.IndexOf(':', StringComparison.Ordinal);
        int slash = path.IndexOf('/', StringComparison.Ordinal);
        return colon >= 0 && (slash < 0 || colon < slash);
    }

    /// <summary>
    /// <paramref name="marker"/> with its only number (outside tags) replaced by <paramref name="number"/>; <c>null</c>
    /// when it holds no number or several.
    /// </summary>
    internal static string? ReplaceSingleNumber(string marker, int number)
    {
        int found = -1;
        int length = 0;
        bool inTag = false;
        for (int i = 0; i < marker.Length; i++)
        {
            char c = marker[i];
            if (c == '<')
            {
                inTag = true;
            }
            else if (c == '>')
            {
                inTag = false;
            }
            else if (!inTag && char.IsAsciiDigit(c) && (i == 0 || !char.IsAsciiDigit(marker[i - 1])))
            {
                if (found >= 0)
                {
                    return null;
                }

                found = i;
                length = 1;
                while (found + length < marker.Length && char.IsAsciiDigit(marker[found + length]))
                {
                    length++;
                }
            }
        }

        return found < 0
            ? null
            : string.Concat(marker.AsSpan(0, found), number.ToString(CultureInfo.InvariantCulture), marker.AsSpan(found + length));
    }

    // The notes file links the stylesheet with the footnote rules of the file whose footnote it is created for.
    private static HtmlResource GetOrCreateGlobalFile(Book book, HtmlResource referenceFile)
    {
        if (FindGlobalFile(book) is { } existing)
        {
            return existing;
        }

        HtmlResource page = book.CreateEmptyHtmlFile();
        page.RenameTo(book.GetFolderKeeper().GetUniqueFilenameVersion(GlobalFileName));
        string language = book.GetMetadataValues("dc:language").FirstOrDefault(l => l.Trim().Length > 0)?.Trim() ?? string.Empty;
        string link = StyleTarget(book, referenceFile) is { } css
            ? "  <link href=\"" + Encode(Utility.UrlEncodePath(Core.BookPath.Relative(page.BookPath, css.BookPath)))
                + "\" rel=\"stylesheet\" type=\"text/css\"/>\n"
            : string.Empty;
        page.SetText(GlobalXhtml(book.IsEpub3, WebUtility.HtmlEncode(language), link));
        return page;
    }

    private static string GlobalXhtml(bool epub3, string language, string headLinks)
    {
        StringBuilder sb = new("<?xml version=\"1.0\" encoding=\"utf-8\"?>\n");
        if (epub3)
        {
            sb.Append("<!DOCTYPE html>\n\n<html xmlns=\"http://www.w3.org/1999/xhtml\" xmlns:epub=\"http://www.idpf.org/2007/ops\"");
            if (language.Length > 0)
            {
                sb.Append(" lang=\"").Append(language).Append("\" xml:lang=\"").Append(language).Append('"');
            }
        }
        else
        {
            sb.Append("<!DOCTYPE html PUBLIC \"-//W3C//DTD XHTML 1.1//EN\"\n  \"http://www.w3.org/TR/xhtml11/DTD/xhtml11.dtd\">\n\n");
            sb.Append("<html xmlns=\"http://www.w3.org/1999/xhtml\"");
            if (language.Length > 0)
            {
                sb.Append(" xml:lang=\"").Append(language).Append('"');
            }
        }

        string section = epub3 ? "section" : "div";
        sb.Append(">\n<head>\n  <title>").Append(GlobalHeading).Append("</title>\n").Append(headLinks).Append("</head>\n\n<body>\n");
        sb.Append("  <h1>").Append(GlobalHeading).Append("</h1>\n");
        sb.Append("  <").Append(section).Append(" class=\"").Append(GlobalContainerClass).Append('"')
            .Append(epub3 ? " epub:type=\"endnotes\"" : string.Empty).Append(">\n  </").Append(section).Append(">\n");
        sb.Append("</body>\n</html>");
        return sb.ToString();
    }

    // Appends the note to the container of its placement (created at the end of the body when missing).
    private static void InsertNote(HtmlResource file, string note, FootnotePlacement placement, bool epub3)
    {
        string containerClass = placement == FootnotePlacement.Local ? LocalContainerClass : GlobalContainerClass;
        string text = file.GetText();
        TagLister tags = new(text);
        if (FindContainer(tags, text, containerClass) is null)
        {
            string section = epub3 ? "section" : "div";
            string type = !epub3 ? string.Empty
                : placement == FootnotePlacement.Local ? " epub:type=\"footnotes\"" : " epub:type=\"endnotes\"";
            string separator = placement == FootnotePlacement.Local ? "<hr class=\"" + SeparatorClass + "\"/>\n" : string.Empty;
            string container = separator + "<" + section + " class=\"" + containerClass + "\"" + type + ">\n</" + section + ">\n";
            int bodyClose = tags.FindBodyCloseTag();
            int at = bodyClose >= 0 ? LineStartIfBlank(text, tags.At(bodyClose).Pos) : text.Length;
            text = text.Insert(at, container);
            tags = new TagLister(text);
        }

        Element containerElement = FindContainer(tags, text, containerClass)!;
        int close = containerElement.CloseStart;
        int lineStart = LineStartIfBlank(text, close);
        string insertion = lineStart < close || (lineStart == close && (close == 0 || text[close - 1] == '\n'))
            ? text[lineStart..close] + "  " + note + "\n"
            : "\n  " + note + "\n";
        file.SetText(text.Insert(lineStart, insertion));
    }

    // Removes Signet's container of the file when no note is left in it (the separator line too); a notes file
    // without notes is removed from the book.
    private static void RemoveEmptyContainer(Book book, HtmlResource file)
    {
        foreach (string containerClass in new[] { LocalContainerClass, GlobalContainerClass })
        {
            string text = file.GetText();
            TagLister tags = new(text);
            if (FindContainer(tags, text, containerClass) is not { } container || HasChildElements(tags, container))
            {
                continue;
            }

            if (containerClass == GlobalContainerClass)
            {
                book.GetFolderKeeper().BulkRemoveResources(new Resource[] { file });
                return;
            }

            (int start, int end) = WholeLines(text, container.Start, container.End);
            int previous = container.OpenIndex - 1;
            while (previous >= 0 && tags.At(previous).Kind == TagKind.Comment)
            {
                previous--;
            }

            if (previous >= 0 && tags.At(previous) is { } hr
                && hr.TagName.Equals("hr", StringComparison.OrdinalIgnoreCase)
                && HasClass(text.Substring(hr.Pos, hr.Len), SeparatorClass)
                && string.IsNullOrWhiteSpace(text[(hr.Pos + hr.Len)..container.Start]))
            {
                start = WholeLines(text, hr.Pos, hr.Pos + hr.Len).Start;
            }

            file.SetText(text.Remove(start, end - start));
        }
    }

    // The range widened to the <span>/<sup> elements that hold only it (whitespace aside), as long as they have no id
    // or the id `allowedId`.
    private static (int Start, int End) Unwrap(TagLister tags, string text, int start, int end, string? allowedId)
    {
        while (true)
        {
            int before = start > 0 ? tags.FindLastTagOnOrBefore(start - 1) : -1;
            if (before < 0 || tags.At(before) is not { Kind: TagKind.Begin } open
                || open.Pos + open.Len > start
                || !WrapperNames.Contains(open.TagName, StringComparer.OrdinalIgnoreCase)
                || !string.IsNullOrWhiteSpace(text[(open.Pos + open.Len)..start]))
            {
                return (start, end);
            }

            string id = AttributeValue(text, open, "id");
            int close = tags.FindCloseTagForOpen(before);
            if ((id.Length > 0 && id != allowedId) || close < 0 || tags.At(close).Pos < end
                || !string.IsNullOrWhiteSpace(text[end..tags.At(close).Pos]))
            {
                return (start, end);
            }

            start = open.Pos;
            end = tags.At(close).Pos + tags.At(close).Len;
        }
    }

    // Whether a link outside the note leads to the id the note's link back leads to.
    private static bool IsLinkedOutside(Book book, Footnote footnote, Element note)
    {
        foreach (HtmlResource html in book.GetHtmlResources())
        {
            string text = html.GetText();
            if (!text.Contains(footnote.ReferenceId, StringComparison.Ordinal))
            {
                continue;
            }

            TagLister tags = new(text);
            for (int i = 0; i < tags.Count; i++)
            {
                TagLister.TagInfo tag = tags.At(i);
                if (tag.Kind != TagKind.Begin || !tag.TagName.Equals("a", StringComparison.OrdinalIgnoreCase)
                    || (ReferenceEquals(html, footnote.NoteFile) && tag.Pos >= note.Start && tag.Pos < note.End))
                {
                    continue;
                }

                (string path, string fragment) = SplitHref(AttributeValue(text, tag, "href"));
                if (fragment == footnote.ReferenceId && !IsExternal(path)
                    && (path.Length == 0 ? html.BookPath : Core.BookPath.BuildBookPath(Utility.UrlDecodePath(path), html.Folder))
                        == footnote.ReferenceFile.BookPath)
                {
                    return true;
                }
            }
        }

        return false;
    }

    // footnote <-> endnote in the note's epub:type, and its id.
    private static string RetypeNote(string note, FootnotePlacement target, string noteId)
    {
        int end = note.IndexOf('>', StringComparison.Ordinal) + 1;
        string open = note[..end];
        string from = target == FootnotePlacement.Local ? "endnote" : "footnote";
        string to = target == FootnotePlacement.Local ? "footnote" : "endnote";
        TagLister.AttInfo type = TagLister.ParseAttribute(open, "epub:type");
        if (type.Pos >= 0 && type.VPos >= 0)
        {
            string value = string.Join(' ', type.AValue.Split(' ', StringSplitOptions.RemoveEmptyEntries).Select(t => t == from ? to : t));
            open = string.Concat(open.AsSpan(0, type.VPos), value, open.AsSpan(type.VPos + type.VLen));
        }

        TagLister.AttInfo id = TagLister.ParseAttribute(open, "id");
        if (id.Pos >= 0 && id.VPos >= 0)
        {
            open = string.Concat(open.AsSpan(0, id.VPos), Encode(noteId), open.AsSpan(id.VPos + id.VLen));
        }

        return open + note[end..];
    }

    // Puts the notes of Signet's container in the order of their numbers (fn-N / en-N), keeping the whitespace between them.
    private static bool SortContainer(HtmlResource file, string containerClass)
    {
        string text = file.GetText();
        if (!text.Contains(containerClass, StringComparison.Ordinal))
        {
            return false;
        }

        TagLister tags = new(text);
        if (FindContainer(tags, text, containerClass) is not { } container)
        {
            return false;
        }

        List<Element> children = ChildElements(tags, text, container).ToList();
        List<Element> sorted = children
            .Select((child, index) => (child, index))
            .OrderBy(c => SignetNumber(c.child.IdValue) ?? int.MaxValue)
            .ThenBy(c => c.index)
            .Select(c => c.child)
            .ToList();
        if (children.Count < 2 || sorted.SequenceEqual(children))
        {
            return false;
        }

        StringBuilder rebuilt = new();
        for (int i = 0; i < sorted.Count; i++)
        {
            rebuilt.Append(text, sorted[i].Start, sorted[i].End - sorted[i].Start);
            if (i < children.Count - 1)
            {
                rebuilt.Append(text, children[i].End, children[i + 1].Start - children[i].End);
            }
        }

        int from = children[0].Start;
        int to = children[^1].End;
        file.SetText(string.Concat(text.AsSpan(0, from), rebuilt.ToString(), text.AsSpan(to)));
        return true;
    }

    private static int? SignetNumber(string id)
    {
        Match match = SignetId.Match(id);
        return match.Success && int.TryParse(match.Groups[2].Value, NumberStyles.None, CultureInfo.InvariantCulture, out int n) ? n : null;
    }

    // The footnote rules for `html` (FootnoteStyles.ApplyTo) go to: the linked stylesheet that already has some, or a
    // <style> element of the file that has some, or the first linked stylesheet, or a new <style> element in <head>.
    private static bool ApplyStyles(Book book, HtmlResource html, FootnoteStyles styles)
    {
        List<CssResource> stylesheets = LinkedStylesheets(book, html);
        CssResource? withRules = stylesheets.FirstOrDefault(css => FootnoteStyles.HasSignetRule(css.GetText()));
        string text = html.GetText();
        Match? styleElement = withRules is null
            ? StyleElement.Matches(text).FirstOrDefault(m => FootnoteStyles.HasSignetRule(m.Groups[2].Value))
            : null;
        if (styleElement is null && (withRules ?? stylesheets.FirstOrDefault()) is { } sheet)
        {
            string css = sheet.GetText();
            string updated = styles.ApplyTo(css);
            if (updated == css)
            {
                return false;
            }

            sheet.SetText(updated);
            return true;
        }

        if (styleElement is not null)
        {
            Group content = styleElement.Groups[2];
            string updated = styles.ApplyTo(content.Value);
            if (updated == content.Value)
            {
                return false;
            }

            html.SetText(text[..content.Index] + updated + text[(content.Index + content.Length)..]);
            return true;
        }

        int head = text.IndexOf("</head>", StringComparison.OrdinalIgnoreCase);
        if (head < 0)
        {
            return false;
        }

        int at = LineStartIfBlank(text, head);
        html.SetText(text.Insert(at, "<style type=\"text/css\">\n" + styles.ApplyTo(string.Empty) + "</style>\n"));
        return true;
    }

    // The stylesheet the footnote rules of `html` are in or go to (null when they go to a <style> element).
    private static CssResource? StyleTarget(Book book, HtmlResource html)
    {
        List<CssResource> stylesheets = LinkedStylesheets(book, html);
        return stylesheets.FirstOrDefault(css => FootnoteStyles.HasSignetRule(css.GetText())) ?? stylesheets.FirstOrDefault();
    }

    private static List<CssResource> LinkedStylesheets(Book book, HtmlResource html) =>
        html.GetLinkedStylesheets()
            .Select(path => book.GetFolderKeeper().GetResourceByBookPathNoThrow(path))
            .OfType<CssResource>()
            .ToList();

    // ---------------------------------------------------------------- text helpers

    private sealed record Span(int Start, int Length);

    private sealed record Anchor(int Start, int End, int InnerStart, int InnerEnd, int OuterStart, int OuterEnd, Span? Href, Span? Id);

    private sealed record Element(int OpenIndex, int Start, int End, int CloseStart, string IdValue, Span? Id);

    private readonly record struct TextEdit(int Start, int Length, string Text);

    // The editable text of a note (trimmed): after the link back of Signet's note, or the whole inside of any other.
    private sealed record NoteContent(int Start, int End, bool AfterBacklink);

    private static NoteContent? ContentOf(Footnote footnote, string text)
    {
        TagLister tags = new(text);
        if (FindElementById(tags, text, footnote.NoteId) is not { } note || tags.At(note.OpenIndex).Kind != TagKind.Begin)
        {
            return null;
        }

        TagLister.TagInfo open = tags.At(note.OpenIndex);
        int innerStart = open.Pos + open.Len;
        if (footnote.IsSignet && footnote.ReferenceId.Length > 0
            && ChildElements(tags, text, note).ToList() is [{ } paragraph]
            && tags.At(paragraph.OpenIndex) is { Kind: TagKind.Begin } p && p.TagName.Equals("p", StringComparison.OrdinalIgnoreCase)
            && string.IsNullOrWhiteSpace(text[innerStart..paragraph.Start])
            && string.IsNullOrWhiteSpace(text[paragraph.End..note.CloseStart])
            && paragraph.OpenIndex + 1 < tags.Count
            && tags.At(paragraph.OpenIndex + 1) is { Kind: TagKind.Begin } link && link.TagName.Equals("a", StringComparison.OrdinalIgnoreCase)
            && string.IsNullOrWhiteSpace(text[(p.Pos + p.Len)..link.Pos])
            && SplitHref(AttributeValue(text, link, "href")).Fragment == footnote.ReferenceId
            && tags.FindCloseTagForOpen(paragraph.OpenIndex + 1) is var linkClose and >= 0
            && tags.At(linkClose).Pos < paragraph.CloseStart)
        {
            TagLister.TagInfo linkEnd = tags.At(linkClose);
            return Trimmed(text, linkEnd.Pos + linkEnd.Len, paragraph.CloseStart, afterBacklink: true);
        }

        return Trimmed(text, innerStart, note.CloseStart, afterBacklink: false);
    }

    private static NoteContent Trimmed(string text, int start, int end, bool afterBacklink)
    {
        while (start < end && char.IsWhiteSpace(text[start]))
        {
            start++;
        }

        while (end > start && char.IsWhiteSpace(text[end - 1]))
        {
            end--;
        }

        return new NoteContent(start, end, afterBacklink);
    }

    // Whether the markup has a link to the given id (the link back of a note).
    private static bool HasBacklink(string markup, string referenceId)
    {
        TagLister tags = new(markup);
        for (int i = 0; i < tags.Count; i++)
        {
            TagLister.TagInfo tag = tags.At(i);
            if (tag.Kind == TagKind.Begin && tag.TagName.Equals("a", StringComparison.OrdinalIgnoreCase)
                && SplitHref(AttributeValue(markup, tag, "href")).Fragment == referenceId)
            {
                return true;
            }
        }

        return false;
    }

    private static string Apply(string text, IEnumerable<TextEdit> edits)
    {
        StringBuilder result = new(text);
        foreach (TextEdit edit in edits.OrderByDescending(e => e.Start))
        {
            result.Remove(edit.Start, edit.Length).Insert(edit.Start, edit.Text);
        }

        return result.ToString();
    }

    private static string Href(HtmlResource from, HtmlResource to, string id) =>
        (ReferenceEquals(from, to) ? string.Empty : Utility.UrlEncodePath(Core.BookPath.Relative(from.BookPath, to.BookPath))) + "#" + id;

    private static string Encode(string value) =>
        value.Replace("&", "&amp;", StringComparison.Ordinal).Replace("<", "&lt;", StringComparison.Ordinal)
            .Replace("\"", "&quot;", StringComparison.Ordinal);

    private static bool HasClass(string tagText, string name)
    {
        TagLister.AttInfo info = TagLister.ParseAttribute(tagText, "class");
        return info.Pos >= 0 && info.AValue.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries).Contains(name, StringComparer.Ordinal);
    }

    private static Span? AttributeSpan(string text, TagLister.TagInfo tag, string name)
    {
        TagLister.AttInfo info = TagLister.ParseAttribute(text.Substring(tag.Pos, tag.Len), name);
        return info.Pos >= 0 && info.VPos >= 0 ? new Span(tag.Pos + info.VPos, info.VLen) : null;
    }

    private static string AttributeValue(string text, TagLister.TagInfo tag, string name)
    {
        TagLister.AttInfo info = TagLister.ParseAttribute(text.Substring(tag.Pos, tag.Len), name);
        return info.Pos >= 0 ? WebUtility.HtmlDecode(info.AValue) : string.Empty;
    }

    private static bool IsOpening(TagLister.TagInfo tag) => tag.Kind is TagKind.Begin or TagKind.SelfClosing;

    private static Element? ToElement(TagLister tags, string text, int index)
    {
        TagLister.TagInfo tag = tags.At(index);
        string id = AttributeValue(text, tag, "id");
        if (tag.Kind == TagKind.SelfClosing)
        {
            return new Element(index, tag.Pos, tag.Pos + tag.Len, tag.Pos, id, AttributeSpan(text, tag, "id"));
        }

        int close = tags.FindCloseTagForOpen(index);
        if (close < 0)
        {
            return null;
        }

        TagLister.TagInfo end = tags.At(close);
        return new Element(index, tag.Pos, end.Pos + end.Len, end.Pos, id, AttributeSpan(text, tag, "id"));
    }

    private static Element? FindElementById(TagLister tags, string text, string id)
    {
        for (int i = 0; i < tags.Count; i++)
        {
            TagLister.TagInfo tag = tags.At(i);
            if (IsOpening(tag) && AttributeValue(text, tag, "id") == id)
            {
                return ToElement(tags, text, i);
            }
        }

        return null;
    }

    private static Element? FindContainer(TagLister tags, string text, string containerClass)
    {
        for (int i = 0; i < tags.Count; i++)
        {
            TagLister.TagInfo tag = tags.At(i);
            if (tag.Kind == TagKind.Begin && HasClass(text.Substring(tag.Pos, tag.Len), containerClass))
            {
                return ToElement(tags, text, i);
            }
        }

        return null;
    }

    // The k-th link of the file with the given href (decoded), with a <sup> that holds only it as its outer range.
    private static Anchor? FindAnchor(TagLister tags, string text, string href, int occurrence)
    {
        int count = 0;
        for (int i = 0; i < tags.Count; i++)
        {
            TagLister.TagInfo tag = tags.At(i);
            if (tag.Kind != TagKind.Begin || !tag.TagName.Equals("a", StringComparison.OrdinalIgnoreCase)
                || AttributeValue(text, tag, "href") != href || count++ != occurrence)
            {
                continue;
            }

            int close = tags.FindCloseTagForOpen(i);
            if (close < 0)
            {
                return null;
            }

            TagLister.TagInfo end = tags.At(close);
            int outerStart = tag.Pos;
            int outerEnd = end.Pos + end.Len;
            if (i > 0 && close + 1 < tags.Count
                && tags.At(i - 1) is { Kind: TagKind.Begin } sup && sup.TagName.Equals("sup", StringComparison.OrdinalIgnoreCase)
                && tags.At(close + 1) is { Kind: TagKind.End } supEnd && supEnd.TagName.Equals("sup", StringComparison.OrdinalIgnoreCase)
                && string.IsNullOrWhiteSpace(text[(sup.Pos + sup.Len)..tag.Pos])
                && string.IsNullOrWhiteSpace(text[outerEnd..supEnd.Pos]))
            {
                outerStart = sup.Pos;
                outerEnd = supEnd.Pos + supEnd.Len;
            }

            return new Anchor(tag.Pos, end.Pos + end.Len, tag.Pos + tag.Len, end.Pos, outerStart, outerEnd,
                AttributeSpan(text, tag, "href"), AttributeSpan(text, tag, "id"));
        }

        return null;
    }

    // The link inside the note that leads back to the reference with the given id.
    private static Anchor? FindBacklink(TagLister tags, string text, Element note, string referenceId)
    {
        for (int i = note.OpenIndex + 1; i < tags.Count && tags.At(i).Pos < note.End; i++)
        {
            TagLister.TagInfo tag = tags.At(i);
            if (tag.Kind == TagKind.Begin && tag.TagName.Equals("a", StringComparison.OrdinalIgnoreCase)
                && SplitHref(AttributeValue(text, tag, "href")).Fragment == referenceId)
            {
                int close = tags.FindCloseTagForOpen(i);
                if (close < 0)
                {
                    return null;
                }

                TagLister.TagInfo end = tags.At(close);
                return new Anchor(tag.Pos, end.Pos + end.Len, tag.Pos + tag.Len, end.Pos, tag.Pos, end.Pos + end.Len,
                    AttributeSpan(text, tag, "href"), AttributeSpan(text, tag, "id"));
            }
        }

        return null;
    }

    private static IEnumerable<Element> ChildElements(TagLister tags, string text, Element container)
    {
        int i = container.OpenIndex + 1;
        while (i < tags.Count && tags.At(i).Pos < container.CloseStart)
        {
            TagLister.TagInfo tag = tags.At(i);
            if (IsOpening(tag) && ToElement(tags, text, i) is { } child)
            {
                yield return child;
                i = tag.Kind == TagKind.SelfClosing ? i + 1 : tags.FindCloseTagForOpen(i) + 1;
                continue;
            }

            i++;
        }
    }

    private static bool HasChildElements(TagLister tags, Element container)
    {
        for (int i = container.OpenIndex + 1; i < tags.Count && tags.At(i).Pos < container.CloseStart; i++)
        {
            if (IsOpening(tags.At(i)))
            {
                return true;
            }
        }

        return false;
    }

    // The tags (Begin) open at the offset, innermost last.
    private static List<TagLister.TagInfo> OpenElementsAt(TagLister tags, int offset)
    {
        List<TagLister.TagInfo> open = new();
        for (int i = 0; i < tags.Count && tags.At(i).Pos < offset; i++)
        {
            TagLister.TagInfo tag = tags.At(i);
            if (tag.Kind == TagKind.Begin)
            {
                open.Add(tag);
            }
            else if (tag.Kind == TagKind.End)
            {
                int match = open.FindLastIndex(t => t.TagName.Equals(tag.TagName, StringComparison.OrdinalIgnoreCase));
                if (match >= 0)
                {
                    open.RemoveRange(match, open.Count - match);
                }
            }
        }

        return open;
    }

    // The range extended to whole lines when the text around it on its lines is whitespace only.
    private static (int Start, int End) WholeLines(string text, int start, int end)
    {
        int lineStart = start;
        while (lineStart > 0 && text[lineStart - 1] is ' ' or '\t')
        {
            lineStart--;
        }

        int lineEnd = end;
        while (lineEnd < text.Length && text[lineEnd] is ' ' or '\t' or '\r')
        {
            lineEnd++;
        }

        bool startsLine = lineStart == 0 || text[lineStart - 1] == '\n';
        bool endsLine = lineEnd == text.Length || text[lineEnd] == '\n';
        return startsLine && endsLine ? (lineStart, Math.Min(text.Length, lineEnd + 1)) : (start, end);
    }

    // The start of the line of the offset when only whitespace precedes it on the line, otherwise the offset.
    private static int LineStartIfBlank(string text, int offset)
    {
        int lineStart = offset;
        while (lineStart > 0 && text[lineStart - 1] is ' ' or '\t')
        {
            lineStart--;
        }

        return lineStart == 0 || text[lineStart - 1] == '\n' ? lineStart : offset;
    }
}
