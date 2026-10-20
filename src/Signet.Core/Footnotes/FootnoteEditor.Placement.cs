using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading;
using Signet.Core.BookManipulation;
using Signet.Core.Resources;

namespace Signet.Core.Footnotes;

/// <summary>
/// A footnote of Signet whose placement can change (<see cref="FootnoteEditor.PlanPlacement"/>): its note and the references
/// that lead to it.
/// </summary>
/// <param name="Footnote">The footnote of the first reference of the note (reading order).</param>
/// <param name="References">The footnotes of all the references of the note (more than one for a shared note), in reading order.</param>
/// <param name="Marker">The marker of the first reference as it reads in the text (<c>[4]</c>); the markers of a shared note joined with a comma.</param>
/// <param name="Blocker">
/// <see cref="FootnoteError.None"/>, or <see cref="FootnoteError.SeveralFiles"/> for a global note that references from several
/// files lead to (it can only be global).
/// </param>
public sealed record PlacementItem(Footnote Footnote, IReadOnlyList<Footnote> References, string Marker, FootnoteError Blocker)
{
    /// <summary>Whether the note can move to the other placement.</summary>
    public bool CanMove => Blocker == FootnoteError.None;

    /// <summary>The placement the note is in now.</summary>
    public FootnotePlacement Placement => Footnote.Placement;
}

/// <summary>The steps of <see cref="FootnoteEditor.ChangePlacement"/>.</summary>
public enum PlacementStage
{
    /// <summary>Looking at the footnotes.</summary>
    Preparing,

    /// <summary>Moving the notes (<see cref="PlacementProgress.Done"/> of <see cref="PlacementProgress.Total"/>).</summary>
    Moving,

    /// <summary>Writing the files and renumbering.</summary>
    Finishing,
}

/// <summary>How far <see cref="FootnoteEditor.ChangePlacement"/> is.</summary>
/// <param name="Stage">The step.</param>
/// <param name="Done">How many notes were prepared (in <see cref="PlacementStage.Moving"/>).</param>
/// <param name="Total">How many notes are to be moved.</param>
public readonly record struct PlacementProgress(PlacementStage Stage, int Done, int Total);

/// <summary>
/// What a caller may ask of <see cref="FootnoteEditor.ChangePlacement"/>: to be told how far it is, and to be able to stop it
/// (an <see cref="OperationCanceledException"/>; the book may then be partly changed, so the caller that cancels rewinds it from a
/// checkpoint it took before).
/// </summary>
/// <param name="Progress">Told how far the work is; <c>null</c> for nobody.</param>
/// <param name="Cancellation">Checked between the steps.</param>
public readonly record struct PlacementRun(IProgress<PlacementProgress>? Progress = null, CancellationToken Cancellation = default);

/// <summary>What <see cref="FootnoteEditor.ChangePlacement"/> did.</summary>
/// <param name="Moved">How many notes moved to the other placement (the global notes that follow a new notes file are not counted).</param>
/// <param name="Skipped">How many chosen notes could not (blocked, gone, or their markup overlaps another note's).</param>
public readonly record struct PlacementOutcome(int Moved, int Skipped);

/// <content>Moving many footnotes between local and global placement at once.</content>
public static partial class FootnoteEditor
{
    /// <summary>
    /// The footnotes of Signet (one per note, a shared note once) with what blocks moving them, in reading order of their
    /// first references. Footnotes of other origin are left out (they are converted first).
    /// </summary>
    public static IReadOnlyList<PlacementItem> PlanPlacement(Book book)
    {
        ArgumentNullException.ThrowIfNull(book);
        Dictionary<HtmlResource, (string Text, TagLister Tags)> snapshots = new(ReferenceEqualityComparer.Instance);
        string MarkerOf(Footnote reference)
        {
            if (!snapshots.TryGetValue(reference.ReferenceFile, out (string Text, TagLister Tags) snapshot))
            {
                string text = reference.ReferenceFile.GetText();
                snapshots[reference.ReferenceFile] = snapshot = (text, TagLister.For(text));
            }

            return FindAnchor(snapshot.Tags, snapshot.Text, reference.ReferenceHref, reference.ReferenceOccurrence) is { } anchor
                ? snapshot.Text[anchor.InnerStart..anchor.InnerEnd]
                : string.Empty;
        }

        List<PlacementItem> items = new();
        foreach (IGrouping<(string BookPath, string Id), Footnote> note in FootnoteScanner.Scan(book)
                     .Where(f => f.IsSignet)
                     .GroupBy(f => (f.NoteFile.BookPath, f.NoteId)))
        {
            List<Footnote> references = note.OrderBy(f => f.ReferenceIndex).ToList();
            Footnote first = references[0];
            FootnoteError blocker = first.Placement == FootnotePlacement.Global
                && references.Select(r => r.ReferenceFile).Distinct().Skip(1).Any()
                    ? FootnoteError.SeveralFiles
                    : FootnoteError.None;
            items.Add(new PlacementItem(first, references, string.Join(", ", references.Select(MarkerOf)), blocker));
        }

        return items;
    }

    /// <summary>
    /// Moves the notes of <paramref name="items"/> to the other placement (local to global, global to local) with all their
    /// references and renumbers the footnotes once — what <see cref="Move(Book, Footnote)"/> does for one footnote, without
    /// scanning the book for each of them (a book can have thousands). Blocked items are skipped.
    /// </summary>
    /// <param name="book">The book.</param>
    /// <param name="items">The footnotes to move (from <see cref="PlanPlacement"/>).</param>
    /// <param name="globalFile">
    /// The file the notes that become global go to: the notes file of the book (the file with Signet's global notes) when
    /// <c>null</c>, which is created as <see cref="GlobalFileName"/> when the book has none. When it is not the file that holds
    /// Signet's global notes now, all of them move there. Not used when no note becomes global.
    /// </param>
    /// <param name="options">The footnote options (<see cref="FootnoteOptions.Default"/> when <c>null</c>).</param>
    /// <param name="run">Progress and cancellation (see <see cref="PlacementRun"/>).</param>
    public static PlacementOutcome ChangePlacement(
        Book book, IReadOnlyList<PlacementItem> items, HtmlResource? globalFile = null, FootnoteOptions? options = null,
        PlacementRun run = default)
    {
        IProgress<PlacementProgress>? progress = run.Progress;
        CancellationToken cancellationToken = run.Cancellation;
        ArgumentNullException.ThrowIfNull(book);
        ArgumentNullException.ThrowIfNull(items);
        progress?.Report(new PlacementProgress(PlacementStage.Preparing, 0, items.Count));
        cancellationToken.ThrowIfCancellationRequested();

        Dictionary<(string BookPath, string Id), List<Footnote>> notes = FootnoteScanner.Scan(book)
            .Where(f => f.IsSignet)
            .GroupBy(f => (f.NoteFile.BookPath, f.NoteId))
            .ToDictionary(g => g.Key, g => g.OrderBy(f => f.ReferenceIndex).ToList());
        int skipped = 0;
        List<List<Footnote>> chosen = new();
        HashSet<(string BookPath, string Id)> chosenKeys = new();
        foreach (PlacementItem item in items)
        {
            (string, string) key = (item.Footnote.NoteFile.BookPath, item.Footnote.NoteId);
            if (!notes.TryGetValue(key, out List<Footnote>? references) || !chosenKeys.Add(key)
                || (references[0].Placement == FootnotePlacement.Global && references.Select(r => r.ReferenceFile).Distinct().Skip(1).Any()))
            {
                skipped++;
                continue;
            }

            chosen.Add(references);
        }

        HtmlResource? signetFile = FindGlobalFile(book);
        HtmlResource? target = null;
        List<Footnote>? firstLocal = chosen.FirstOrDefault(c => c[0].Placement == FootnotePlacement.Local);
        if (firstLocal is not null)
        {
            target = globalFile ?? signetFile ?? GetOrCreateGlobalFile(book, firstLocal[0].ReferenceFile);
        }

        // What moves where: the chosen notes to the other placement; with another notes file, Signet's other global notes follow.
        List<(List<Footnote> References, FootnotePlacement Placement, HtmlResource File)> moves = new();
        foreach (List<Footnote> references in chosen)
        {
            moves.Add(references[0].Placement == FootnotePlacement.Local
                ? (references, FootnotePlacement.Global, target!)
                : (references, FootnotePlacement.Local, references[0].ReferenceFile));
        }

        if (target is not null && signetFile is not null && !ReferenceEquals(target, signetFile))
        {
            foreach (List<Footnote> references in notes.Values.Where(r =>
                         r[0].Placement == FootnotePlacement.Global && ReferenceEquals(r[0].NoteFile, signetFile) && !chosenKeys.Contains((r[0].NoteFile.BookPath, r[0].NoteId))))
            {
                moves.Add((references, FootnotePlacement.Global, target));
            }
        }

        // The edits are worked out on the texts as they are now, and written at the end, so every position stays valid.
        Dictionary<HtmlResource, (string Text, TagLister Tags)> snapshots = new(ReferenceEqualityComparer.Instance);
        Dictionary<HtmlResource, List<TextEdit>> edits = new(ReferenceEqualityComparer.Instance);
        Dictionary<(HtmlResource File, FootnotePlacement Placement), List<string>> insertions = new();
        HashSet<HtmlResource> oldFiles = new(ReferenceEqualityComparer.Instance);

        (string Text, TagLister Tags) Snapshot(HtmlResource file)
        {
            if (!snapshots.TryGetValue(file, out (string Text, TagLister Tags) snapshot))
            {
                string text = file.GetText();
                snapshots[file] = snapshot = (text, TagLister.For(text));
            }

            return snapshot;
        }

        int moved = 0;
        bool changed = false;
        int step = Math.Max(1, moves.Count / 200);
        for (int i = 0; i < moves.Count; i++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            (List<Footnote> references, FootnotePlacement placement, HtmlResource file) = moves[i];
            if (i % step == 0)
            {
                progress?.Report(new PlacementProgress(PlacementStage.Moving, i, moves.Count));
            }

            Footnote first = references[0];
            (string noteText, TagLister noteTags) = Snapshot(first.NoteFile);
            if (FindElementById(noteTags, noteText, first.NoteId) is not { } note)
            {
                skipped++;
                continue;
            }

            (int start, int end) = WholeLines(noteText, note.Start, note.End);
            List<(HtmlResource File, TextEdit Edit)> mine = new() { (first.NoteFile, new TextEdit(start, end - start, string.Empty)) };
            string noteId = "signet-moving-" + i.ToString(CultureInfo.InvariantCulture);
            bool complete = true;
            foreach (Footnote reference in references)
            {
                (string referenceText, TagLister referenceTags) = Snapshot(reference.ReferenceFile);
                if (FindAnchor(referenceTags, referenceText, reference.ReferenceHref, reference.ReferenceOccurrence) is not { Href: { } href })
                {
                    complete = false;
                    break;
                }

                mine.Add((reference.ReferenceFile, new TextEdit(href.Start, href.Length, Encode(Href(reference.ReferenceFile, file, noteId)))));
            }

            if (!complete || mine.Any(m => edits.TryGetValue(m.File, out List<TextEdit>? taken) && taken.Any(t => Overlap(t, m.Edit))))
            {
                skipped++;
                continue;
            }

            foreach ((HtmlResource editedFile, TextEdit edit) in mine)
            {
                if (!edits.TryGetValue(editedFile, out List<TextEdit>? list))
                {
                    edits[editedFile] = list = new List<TextEdit>();
                }

                list.Add(edit);
            }

            // The note's links back start from its new file; its id is a temporary one that the renumbering replaces.
            Dictionary<string, HtmlResource> returnTo = new(StringComparer.Ordinal);
            foreach (Footnote reference in references.Where(r => r.ReferenceId.Length > 0))
            {
                returnTo.TryAdd(reference.ReferenceId, reference.ReferenceFile);
            }

            string markup = RetypeNote(Unindent(noteText, note), placement, noteId);
            markup = MarkupEdits.ReplaceAttributeValues(markup, MarkupEdits.LinkAttributes, href =>
                returnTo.TryGetValue(SplitHref(href).Fragment, out HtmlResource? from) ? Href(file, from, SplitHref(href).Fragment) : null);
            if (!insertions.TryGetValue((file, placement), out List<string>? inserted))
            {
                insertions[(file, placement)] = inserted = new List<string>();
            }

            inserted.Add(markup);
            oldFiles.Add(first.NoteFile);
            changed = true;
            if (placement != first.Placement)
            {
                moved++;
            }
        }

        progress?.Report(new PlacementProgress(PlacementStage.Finishing, moves.Count, moves.Count));
        cancellationToken.ThrowIfCancellationRequested();
        foreach ((HtmlResource file, List<TextEdit> list) in edits)
        {
            file.SetText(Apply(snapshots[file].Text, list));
        }

        foreach (((HtmlResource file, FootnotePlacement placement), List<string> inserted) in insertions)
        {
            InsertNote(file, string.Join('\n', inserted), placement, book.IsEpub3);
        }

        // The containers the notes left are removed before the renumbering: it arranges the notes in the first file with a
        // container of the global notes, which must be the one they went to.
        HashSet<HtmlResource> inBook = new(book.GetHtmlResources(), ReferenceEqualityComparer.Instance);
        foreach (HtmlResource file in oldFiles.Where(inBook.Contains))
        {
            RemoveEmptyContainer(book, file);
        }

        cancellationToken.ThrowIfCancellationRequested();
        if (changed)
        {
            RenumberWith(book, options, null, cancel: cancellationToken);
            book.Modified = true;
        }

        return new PlacementOutcome(moved, skipped);
    }
}
