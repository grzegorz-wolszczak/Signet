using System;
using System.Collections.Generic;
using System.Linq;
using Signet.Core.BookManipulation;
using Signet.Core.Resources;

namespace Signet.Core.Footnotes;

/// <summary>
/// A shared note whose letters are not in the chosen style (<see cref="FootnoteEditor.PlanLetters"/>).
/// </summary>
/// <param name="Footnote">Its first reference.</param>
/// <param name="Number">The number of the note.</param>
/// <param name="Current">The style its letters are written in.</param>
/// <param name="References">All its references, in reading order.</param>
/// <param name="Links">
/// The links from outside the note to the ids of its references (e.g. a table of contents): they are redirected to
/// the new ids.
/// </param>
public sealed record LetterItem(
    Footnote Footnote, int Number, FootnoteLetterStyle Current, IReadOnlyList<Footnote> References, IReadOnlyList<ConversionLink> Links);

/// <content>Writing the letters of shared notes in one style.</content>
public static partial class FootnoteEditor
{
    /// <summary>
    /// The shared Signet notes of the book whose letters are not written in <paramref name="style"/>, in the order of
    /// their first references.
    /// </summary>
    public static IReadOnlyList<LetterItem> PlanLetters(Book book, FootnoteLetterStyle style)
    {
        ArgumentNullException.ThrowIfNull(book);
        IReadOnlyList<Footnote> footnotes = FootnoteScanner.Scan(book);
        Dictionary<(string BookPath, string Id), int> numbers = NoteNumbers(footnotes);
        List<IGrouping<(string, string), Footnote>> notes = footnotes
            .Where(f => f.IsSignet && f.IsShared)
            .GroupBy(f => (f.NoteFile.BookPath, f.NoteId))
            .ToList();
        if (notes.Count == 0)
        {
            return Array.Empty<LetterItem>();
        }

        List<BookLink> links = BookLinks(book);
        List<LetterItem> items = new();
        foreach (IGrouping<(string BookPath, string Id), Footnote> note in notes)
        {
            List<Footnote> references = note.ToList();
            if (LettersOf(references) is not { } current || current == style)
            {
                continue;
            }

            Footnote first = references[0];
            string text = first.NoteFile.GetText();
            Element? element = FindElementById(TagLister.For(text), text, first.NoteId);
            HashSet<(string, string)> ids = references.Where(r => r.ReferenceId.Length > 0)
                .Select(r => (r.ReferenceFile.BookPath, r.ReferenceId))
                .ToHashSet();
            List<ConversionLink> outside = links
                .Where(l => ids.Contains((l.Target, l.Fragment))
                    && !(element is not null && ReferenceEquals(l.Owner, first.NoteFile) && l.Position >= element.Start && l.Position < element.End))
                .Select(l => new ConversionLink(l.Owner, l.Position, false))
                .ToList();
            items.Add(new LetterItem(first, numbers[note.Key], current, references, outside));
        }

        return items;
    }

    /// <summary>
    /// Writes the letters of the notes of <paramref name="items"/> (<see cref="PlanLetters"/>) in
    /// <paramref name="style"/>: the markers of their references (<c>[4ii]</c> → <c>[4b]</c>), the ids of the references
    /// and the links back of the notes; the links from outside the notes to the old ids lead to the new ones. Returns how
    /// many notes changed.
    /// </summary>
    public static int UnifyLetters(Book book, IReadOnlyList<LetterItem> items, FootnoteLetterStyle style, FootnoteOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(book);
        ArgumentNullException.ThrowIfNull(items);
        if (items.Count == 0)
        {
            return 0;
        }

        // The references of every note before the change: their place in the reading order finds them afterwards.
        List<Footnote> before = FootnoteScanner.Scan(book).ToList();
        Dictionary<(string BookPath, string Id), FootnoteLetterStyle> styles = new();
        List<(int Index, string BookPath, string OldId)> references = new();
        foreach (LetterItem item in items)
        {
            styles[(item.Footnote.NoteFile.BookPath, item.Footnote.NoteId)] = style;
            foreach (Footnote reference in item.References.Where(r => r.ReferenceId.Length > 0))
            {
                int index = before.FindIndex(f => ReferenceEquals(f.ReferenceFile, reference.ReferenceFile)
                    && f.ReferenceHref == reference.ReferenceHref && f.ReferenceOccurrence == reference.ReferenceOccurrence);
                if (index >= 0)
                {
                    references.Add((index, reference.ReferenceFile.BookPath, reference.ReferenceId));
                }
            }
        }

        RenumberWith(book, options, styles);

        // The links from outside the notes to the old ids of the references lead to the new ones (inside a note they
        // are its links back, written anew by the renumbering: a new id may be the old id of another reference).
        List<Footnote> after = FootnoteScanner.Scan(book).ToList();
        Dictionary<(string BookPath, string Id), string> moves = new();
        foreach ((int index, string bookPath, string oldId) in references)
        {
            if (index < after.Count && after[index].ReferenceId != oldId)
            {
                moves[(bookPath, oldId)] = after[index].ReferenceId;
            }
        }

        if (moves.Count > 0)
        {
            RetargetOutsideNotes(book, moves, after.Where(f => styles.ContainsKey((f.NoteFile.BookPath, f.NoteId))).ToList());
        }

        book.Modified = true;
        return items.Count;
    }

    // The style the letters of a shared note are written in (null when its references have no letters yet).
    private static FootnoteLetterStyle? LettersOf(IReadOnlyList<Footnote> references) =>
        FootnoteLetters.Detect(references.Select(r =>
        {
            string text = r.ReferenceFile.GetText();
            return FindAnchor(TagLister.For(text), text, r.ReferenceHref, r.ReferenceOccurrence) is { } anchor
                ? MarkerLetters(text[anchor.InnerStart..anchor.InnerEnd])
                : string.Empty;
        }));

    // Redirects the links to moved reference ids (the file stays) everywhere but inside the notes of `notes`.
    private static void RetargetOutsideNotes(
        Book book, Dictionary<(string BookPath, string Id), string> moves, IReadOnlyList<Footnote> notes)
    {
        Dictionary<HtmlResource, List<(int Start, int End)>> inside = new(ReferenceEqualityComparer.Instance);
        foreach (Footnote note in notes.Where(f => f.ReferenceIndex == 0))
        {
            string text = note.NoteFile.GetText();
            if (FindElementById(TagLister.For(text), text, note.NoteId) is { } element)
            {
                if (!inside.TryGetValue(note.NoteFile, out List<(int Start, int End)>? ranges))
                {
                    inside[note.NoteFile] = ranges = new List<(int Start, int End)>();
                }

                ranges.Add((element.Start, element.End));
            }
        }

        string? Retarget(Resource owner, string href)
        {
            return Resolve(owner, href) is { } target && target.Fragment.Length > 0
                && moves.TryGetValue((target.BookPath, target.Fragment), out string? id)
                    ? Href(owner.BookPath, target.BookPath, id)
                    : null;
        }

        foreach (HtmlResource html in book.GetHtmlResources())
        {
            List<(int Start, int End)> skip = inside.GetValueOrDefault(html, new List<(int Start, int End)>());
            string text = html.GetText();
            string updated = MarkupEdits.ReplaceAttributeValues(text, MarkupEdits.LinkAttributes, (position, value) =>
                skip.Any(r => position >= r.Start && position < r.End) ? null : Retarget(html, value));
            if (updated != text)
            {
                html.SetText(updated);
            }
        }

        if (book.GetNcx() is { } ncx)
        {
            string text = ncx.GetText();
            string updated = MarkupEdits.ReplaceAttributeValues(text, MarkupEdits.SrcAttribute, value => Retarget(ncx, value));
            if (updated != text)
            {
                ncx.SetText(updated);
            }
        }

        OpfResource opf = book.GetOpf();
        OpfDocument document = opf.GetOpfDocument();
        bool changed = false;
        foreach (GuideEntry entry in document.Guide)
        {
            if (Retarget(opf, entry.Href) is { } href)
            {
                entry.Href = href;
                changed = true;
            }
        }

        if (changed)
        {
            opf.SetOpfDocument(document);
        }
    }
}
