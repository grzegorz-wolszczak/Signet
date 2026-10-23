using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Net;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using Signet.Core.BookManipulation;
using Signet.Core.Resources;

namespace Signet.Core.Footnotes;

/// <summary>Why a footnote of other origin needs a look before it is converted (<see cref="ConversionItem.Warnings"/>).</summary>
public enum ConversionWarning
{
    /// <summary>Its marker is not a single number (e.g. <c>*</c>); it gets a number.</summary>
    NonNumericMarker,

    /// <summary>Its number changes (the renumbering follows Signet's rules).</summary>
    NumberChanges,

    /// <summary>Other links lead to the note or to the place its link back returns to; they are rewritten.</summary>
    LinkedElsewhere,

    /// <summary>The link back is inside the text of the note, not at its start; it is removed from there.</summary>
    BacklinkInText,

    /// <summary>The note has no link back to the reference; Signet's link back is added.</summary>
    NoBacklink,

    /// <summary>The note does not start with a paragraph (a list, a quotation…); the link back gets a paragraph of its own.</summary>
    FirstBlockNotParagraph,

    /// <summary>
    /// The note holds the note of another footnote (<see cref="ConversionItem.NestedWith"/>): the elements are nested, usually by a
    /// comment that swallows a closing tag or an unclosed element. The notes become separate; the text of this one stays
    /// without the other.
    /// </summary>
    ContainsNote,

    /// <summary>The note is inside the note of another footnote (<see cref="ConversionItem.NestedWith"/>); it becomes a separate one.</summary>
    InsideNote,
}

/// <summary>Why a footnote of other origin cannot be converted (<see cref="ConversionItem.Blocker"/>).</summary>
public enum ConversionBlocker
{
    /// <summary>Nothing: it can be converted.</summary>
    None,

    /// <summary>Its reference (one of the references of a shared note) was not found in its file.</summary>
    ReferenceNotFound,

    /// <summary>Its note was not found in its file.</summary>
    NoteNotFound,

    /// <summary>A link that looks like a reference but is not one of the note's leads to it.</summary>
    OtherReference,
}

/// <summary>What <see cref="FootnoteEditor.Convert"/> is doing (<see cref="ConversionProgress.Stage"/>).</summary>
public enum ConversionStage
{
    /// <summary>Reading the book before the conversion (the footnotes and the links check).</summary>
    Preparing,

    /// <summary>Converting the footnotes one by one.</summary>
    Converting,

    /// <summary>Removing the leftovers, retargeting the links and renumbering.</summary>
    Finishing,

    /// <summary>Checking the result.</summary>
    Checking,
}

/// <summary>Where <see cref="FootnoteEditor.Convert"/> is.</summary>
/// <param name="Stage">What it is doing.</param>
/// <param name="Done">The footnotes converted so far (while <see cref="ConversionStage.Converting"/>).</param>
/// <param name="Total">The footnotes to convert.</param>
public sealed record ConversionProgress(ConversionStage Stage, int Done, int Total);

/// <summary>
/// What the conversion of a footnote of other origin does besides the usual (<see cref="ConversionItem.Notices"/>): for
/// information only, an item with notices is selected by default.
/// </summary>
public enum ConversionNotice
{
    /// <summary>Several references lead to the note: it becomes Signet's shared note, its references <c>[4a]</c>, <c>[4b]</c>.</summary>
    SharedNote,

    /// <summary>The note is in the file of a reference, but references from other files lead to it too: it becomes global.</summary>
    BecomesGlobal,
}

/// <summary>A footnote of other origin in the conversion plan (<see cref="FootnoteEditor.PlanConversion"/>).</summary>
/// <param name="Footnote">The footnote: the first reference of its note.</param>
/// <param name="Marker">The text of its reference.</param>
/// <param name="NewNumber">The number it gets.</param>
/// <param name="CanConvert">
/// Whether it can be converted: <c>false</c> when its reference or its note is not found, or when a link that looks
/// like a reference but is not one of the note's leads to it. Other links to it are rewritten.
/// </param>
/// <param name="Warnings">What needs a look (an item with warnings is not selected by default).</param>
/// <param name="Links">
/// The other links that lead to its note or to its reference, in the order of the book
/// (<see cref="ConversionWarning.LinkedElsewhere"/>); the other references among them make it impossible to convert.
/// </param>
/// <param name="NoteText">The text of its note without the link back (tags left out, whitespace collapsed).</param>
public sealed record ConversionItem(
    Footnote Footnote, string Marker, int NewNumber, bool CanConvert, IReadOnlyList<ConversionWarning> Warnings,
    IReadOnlyList<ConversionLink> Links, string NoteText)
{
    /// <summary>
    /// Where its reference is in <see cref="Footnote.ReferenceFile"/> (with the <c>&lt;sup&gt;</c> around the link, as
    /// <see cref="FootnoteEditor.LocateReferenceRange"/>), at the time of the plan; <c>null</c> when it was not found.
    /// </summary>
    public (int Start, int End)? ReferenceRange { get; init; }

    /// <summary>Why it cannot be converted (<see cref="CanConvert"/> is <c>false</c>), or <see cref="ConversionBlocker.None"/>.</summary>
    public ConversionBlocker Blocker { get; init; }

    /// <summary>
    /// For <see cref="ConversionWarning.ContainsNote"/> and <see cref="ConversionWarning.InsideNote"/> the id of the other note;
    /// empty otherwise.
    /// </summary>
    public string NestedWith { get; init; } = string.Empty;

    /// <summary>
    /// All the references of its note in reading order (<see cref="Footnote"/> first); more than one for a shared note,
    /// whose references are all converted.
    /// </summary>
    public IReadOnlyList<Footnote> References { get; init; } = Array.Empty<Footnote>();

    /// <summary>What the conversion does besides the usual (for information; the item stays selected).</summary>
    public IReadOnlyList<ConversionNotice> Notices { get; init; } = Array.Empty<ConversionNotice>();

    /// <summary>The files of the other links that lead to it, each once.</summary>
    public IReadOnlyList<string> OtherLinks =>
        Links.Select(l => l.File.BookPath).Distinct(StringComparer.Ordinal).ToList();
}

/// <summary>
/// A link of the book to a footnote of other origin besides its own reference (<see cref="ConversionItem.Links"/>).
/// </summary>
/// <param name="File">The file the link is in.</param>
/// <param name="Offset">Where its tag starts in the text of the file; -1 for a link with no place in a text (the guide of the OPF).</param>
/// <param name="IsReference">
/// Whether it is another reference to the note (a link that looks like a footnote marker, or <c>noteref</c>) — Signet's
/// footnote has one, so the footnote cannot be converted; otherwise it is a link that the conversion rewrites.
/// </param>
public sealed record ConversionLink(Resource File, int Offset, bool IsReference);

/// <summary>What converting the footnotes of other origin of a book would do (<see cref="FootnoteEditor.PlanConversion"/>).</summary>
/// <param name="Items">The footnotes of other origin, in reading order.</param>
/// <param name="Candidates">The files that could become the notes file of the global footnotes.</param>
/// <param name="AutomaticFile">The candidate taken without asking, or <c>null</c>.</param>
public sealed record ConversionPlan(
    IReadOnlyList<ConversionItem> Items, IReadOnlyList<GlobalFileCandidate> Candidates, GlobalFileCandidate? AutomaticFile);

/// <summary>What to convert (<see cref="FootnoteEditor.Convert"/>).</summary>
/// <param name="Items">The items of the plan to convert (items that cannot be converted are skipped).</param>
/// <param name="GlobalFile">
/// The file the global notes go to: a candidate (taken over: Signet's container goes where its notes are, the headings
/// between them become groups), or <c>null</c> for Signet's notes file (created when needed).
/// </param>
/// <param name="KeepClasses">
/// Whether the classes of the converted elements stay next to Signet's: of the reference and the wrappers around it, of
/// the note and of its paragraphs.
/// </param>
public sealed record ConversionRequest(IReadOnlyList<ConversionItem> Items, HtmlResource? GlobalFile, bool KeepClasses);

/// <summary>What the check after the conversion found wrong (<see cref="ConversionResult.Problems"/>).</summary>
public enum ConversionProblemKind
{
    /// <summary>The text of a note differs from its text before.</summary>
    TextChanged,

    /// <summary>A converted footnote is not recognized as Signet's.</summary>
    NotSignet,

    /// <summary>The number of footnotes in the book changed.</summary>
    CountChanged,

    /// <summary>The links or footnote pairs check (F7) finds something new.</summary>
    NewValidationResult,
}

/// <summary>A problem found by the check after the conversion.</summary>
/// <param name="Kind">What is wrong.</param>
/// <param name="BookPath">The file it is in.</param>
/// <param name="Detail">More about it (a note id, a validation message…).</param>
/// <param name="Expected">
/// For <see cref="ConversionProblemKind.TextChanged"/>: the text of the note before the conversion (tags left out,
/// whitespace collapsed), or <c>null</c>.
/// </param>
/// <param name="Actual">
/// For <see cref="ConversionProblemKind.TextChanged"/>: the text of the note after the conversion, or <c>null</c> when the
/// note was not found.
/// </param>
public sealed record ConversionProblem(
    ConversionProblemKind Kind, string BookPath, string Detail, string? Expected = null, string? Actual = null)
{
    /// <summary>The note as it was before the conversion: its file (for <see cref="ConversionProblemKind.TextChanged"/>).</summary>
    public string SourcePath { get; init; } = string.Empty;

    /// <summary>The note as it was before the conversion: its id.</summary>
    public string SourceId { get; init; } = string.Empty;

    /// <summary>The marker of its reference before the conversion (<c>[9]</c>, <c>*</c>…).</summary>
    public string Marker { get; init; } = string.Empty;
}

/// <summary>The outcome of <see cref="FootnoteEditor.Convert"/>.</summary>
/// <param name="Converted">How many footnotes were converted.</param>
/// <param name="RemovedFiles">The files removed because nothing but headings was left in them.</param>
/// <param name="Problems">What the check found wrong (the caller rewinds the book then); empty when all is well.</param>
public sealed record ConversionResult(int Converted, IReadOnlyList<string> RemovedFiles, IReadOnlyList<ConversionProblem> Problems)
{
    /// <summary>Whether the check found nothing wrong.</summary>
    public bool Succeeded => Problems.Count == 0;
}

/// <content>Converting footnotes of other origin to Signet's.</content>
public static partial class FootnoteEditor
{
    private static readonly Regex SingleNumber = new(@"^\D*(\d+)\D*$", RegexOptions.CultureInvariant);

    // What is left of a marker next to a removed link back: "1</a>. Text" leaves ". Text".
    private const string MarkerResidue = " \t\r\n\u00a0.)]:-–—";

    private static readonly Regex BlockTag = new(
        @"<(?:/?)(?:p|div|h[1-6]|li|ul|ol|dd|dt|dl|br|hr|section|aside|blockquote|table|tr|td|th)\b[^>]*>",
        RegexOptions.CultureInvariant | RegexOptions.IgnoreCase);

    private static readonly HashSet<string> ListNames = new(StringComparer.OrdinalIgnoreCase) { "ol", "ul", "dl" };

    // A link of the book: its owner, where its attribute value is, and what it leads to.
    private sealed record BookLink(Resource Owner, int Position, string Target, string Fragment);

    /// <summary>
    /// What converting the footnotes of other origin would do: each with the number it gets and what needs a look, and
    /// the candidates for the notes file of the global footnotes. It only reads the book, so it can run off the UI
    /// thread while nothing edits the book. <paramref name="progress"/> reports how far the scan of the book's files is
    /// (a book can have thousands).
    /// </summary>
    public static ConversionPlan PlanConversion(Book book, IProgress<ScanProgress>? progress = null)
    {
        ArgumentNullException.ThrowIfNull(book);
        IReadOnlyList<Footnote> footnotes = FootnoteScanner.Scan(book, progress);

        // Every link by what it leads to, and every file parsed once: a book can have thousands of footnotes.
        Dictionary<(string Target, string Fragment), List<BookLink>> links = new();
        foreach (BookLink link in BookLinks(book))
        {
            if (!links.TryGetValue((link.Target, link.Fragment), out List<BookLink>? same))
            {
                links[(link.Target, link.Fragment)] = same = new List<BookLink>();
            }

            same.Add(link);
        }

        Dictionary<HtmlResource, (string Text, TagLister Tags)> parsed = new(ReferenceEqualityComparer.Instance);
        (string Text, TagLister Tags) Parsed(HtmlResource file)
        {
            if (!parsed.TryGetValue(file, out (string Text, TagLister Tags) entry))
            {
                string text = file.GetText();
                parsed[file] = entry = (text, TagLister.For(text));
            }

            return entry;
        }

        Dictionary<HtmlResource, int> local = new(ReferenceEqualityComparer.Instance);
        int global = 0;
        List<ConversionItem> items = new();
        Dictionary<(string BookPath, string Id), List<Footnote>> referencesOf = footnotes
            .GroupBy(f => (f.NoteFile.BookPath, f.NoteId))
            .ToDictionary(g => g.Key, g => g.ToList());

        NestedNotes nested = FindNestedNotes(footnotes, Parsed);

        // A note counts at its first reference and is one item of the plan.
        foreach (Footnote footnote in footnotes.Where(f => f.ReferenceIndex == 0))
        {
            int number = footnote.Placement == FootnotePlacement.Local
                ? local[footnote.ReferenceFile] = local.GetValueOrDefault(footnote.ReferenceFile) + 1
                : ++global;
            if (!footnote.IsSignet)
            {
                items.Add(PlanItem(referencesOf[(footnote.NoteFile.BookPath, footnote.NoteId)], number, links, Parsed, nested));
            }
        }

        IReadOnlyList<GlobalFileCandidate> candidates = GlobalFileCandidates(book);
        return new ConversionPlan(items, candidates, AutomaticGlobalFile(candidates));
    }

    // The notes that hold the note of another footnote, and the ones inside them, each with the id of one other (by file and id);
    // and how deep a note is (how many notes it is inside).
    private sealed record NestedNotes(
        Dictionary<(string BookPath, string Id), string> Outer,
        Dictionary<(string BookPath, string Id), string> Inner,
        Dictionary<(string BookPath, string Id), int> Depth);

    // A comment that swallows a closing tag, or an unclosed element, nests one note in another (the book is well formed, the
    // note just holds the other). The text of the outer note then includes the inner one. The conversion converts the inner
    // notes first (see Convert): moving one out of the outer note leaves the outer one with its own text.
    private static NestedNotes FindNestedNotes(
        IReadOnlyList<Footnote> footnotes, Func<HtmlResource, (string Text, TagLister Tags)> parsed)
    {
        NestedNotes nested = new(
            new Dictionary<(string, string), string>(), new Dictionary<(string, string), string>(), new Dictionary<(string, string), int>());
        foreach (IGrouping<HtmlResource, Footnote> file in footnotes.Where(f => !f.IsSignet).GroupBy(f => f.NoteFile))
        {
            HashSet<string> ids = file.Select(f => f.NoteId).ToHashSet(StringComparer.Ordinal);
            if (ids.Count < 2)
            {
                continue;
            }

            (string text, TagLister tags) = parsed(file.Key);
            List<(int Index, string Id)> opens = new();
            for (int i = 0; i < tags.Count; i++)
            {
                TagLister.TagInfo tag = tags.At(i);
                if (IsOpening(tag) && AttributeValue(text, tag, "id") is { Length: > 0 } id && ids.Contains(id))
                {
                    opens.Add((i, id));
                }
            }

            for (int outer = 0; outer < opens.Count; outer++)
            {
                if (ToElement(tags, text, opens[outer].Index) is not { } element)
                {
                    continue;
                }

                for (int inner = outer + 1; inner < opens.Count && tags.At(opens[inner].Index).Pos < element.End; inner++)
                {
                    nested.Outer.TryAdd((file.Key.BookPath, opens[outer].Id), opens[inner].Id);
                    nested.Inner.TryAdd((file.Key.BookPath, opens[inner].Id), opens[outer].Id);
                    nested.Depth[(file.Key.BookPath, opens[inner].Id)] = nested.Depth.GetValueOrDefault((file.Key.BookPath, opens[inner].Id)) + 1;
                }
            }
        }

        return nested;
    }

    // The text of a note as the dialog and the report show it: tags left out, whitespace collapsed. Block tags separate words
    // (the paragraphs of the note do not run together), inline ones do not.
    private static string ReadableText(string content)
    {
        string text = WebUtility.HtmlDecode(Tag.Replace(BlockTag.Replace(content, " "), string.Empty));
        return Regex.Replace(text, "\\s+", " ", RegexOptions.CultureInvariant).Trim();
    }

    private static ConversionItem PlanItem(
        List<Footnote> references,
        int number,
        Dictionary<(string Target, string Fragment), List<BookLink>> links,
        Func<HtmlResource, (string Text, TagLister Tags)> parsed,
        NestedNotes nested)
    {
        Footnote footnote = references[0];
        (string referenceText, TagLister referenceTags) = parsed(footnote.ReferenceFile);
        Anchor? anchor = FindAnchor(referenceTags, referenceText, footnote.ReferenceHref, footnote.ReferenceOccurrence);

        // Where the tags of all the references of the note start.
        HashSet<(HtmlResource File, int Position)> referenceTagsAt = new();
        bool allFound = true;
        foreach (Footnote reference in references)
        {
            (string fileText, TagLister fileTags) = parsed(reference.ReferenceFile);
            if (FindAnchor(fileTags, fileText, reference.ReferenceHref, reference.ReferenceOccurrence) is { } found)
            {
                referenceTagsAt.Add((reference.ReferenceFile, found.Start));
            }
            else
            {
                allFound = false;
            }
        }

        string marker = anchor is null ? string.Empty : PlainText(referenceText[anchor.InnerStart..anchor.InnerEnd]);
        (string noteText, TagLister noteTags) = parsed(footnote.NoteFile);
        Element? note = FindElementById(noteTags, noteText, footnote.NoteId);

        bool InNote(BookLink link) =>
            note is not null && ReferenceEquals(link.Owner, footnote.NoteFile) && link.Position >= note.Start && link.Position < note.End;

        List<BookLink> LinksTo(string target, string fragment) =>
            links.TryGetValue((target, fragment), out List<BookLink>? found) ? found.Where(l => !InNote(l)).ToList() : new List<BookLink>();

        List<BookLink> toNote = LinksTo(footnote.NoteFile.BookPath, footnote.NoteId);
        List<BookLink> toReference = references
            .Where(r => r.ReferenceId.Length > 0)
            .Select(r => (r.ReferenceFile.BookPath, r.ReferenceId))
            .Distinct()
            .SelectMany(r => LinksTo(r.BookPath, r.ReferenceId))
            .ToList();

        List<ConversionWarning> warnings = new();
        Match numeric = SingleNumber.Match(marker);
        if (!numeric.Success)
        {
            warnings.Add(ConversionWarning.NonNumericMarker);
        }
        else if (int.Parse(numeric.Groups[1].Value, NumberStyles.None, CultureInfo.InvariantCulture) != number)
        {
            warnings.Add(ConversionWarning.NumberChanges);
        }

        bool IsTheReference(BookLink link) =>
            link.Owner is HtmlResource html && referenceTagsAt.Contains((html, link.Position));

        bool IsReference(BookLink link) => link.Position >= 0 && LooksLikeReference(link);
        List<ConversionLink> others = toNote.Where(l => !IsTheReference(l))
            .Select(l => new ConversionLink(l.Owner, l.Position, IsReference(l)))
            .Concat(toReference.Select(l => new ConversionLink(l.Owner, l.Position, false)))
            .ToList();
        if (others.Count > 0)
        {
            warnings.Add(ConversionWarning.LinkedElsewhere);
        }

        Anchor? backlink = note is null ? null : NoteBacklink(noteTags, noteText, note, references).Backlink;
        if (backlink is null)
        {
            warnings.Add(ConversionWarning.NoBacklink);
        }
        else if (PlainText(noteText[(noteTags.At(note!.OpenIndex).Pos + noteTags.At(note.OpenIndex).Len)..backlink.Start]).Length > 0)
        {
            warnings.Add(ConversionWarning.BacklinkInText);
        }

        string content = note is null ? string.Empty : CutContent(noteTags, noteText, note, backlink).Content;
        if (note is not null && NoteBlocks(content, false) is [{ IsParagraph: false }, ..])
        {
            warnings.Add(ConversionWarning.FirstBlockNotParagraph);
        }

        string text = ReadableText(content);
        List<ConversionNotice> notices = new();
        if (references.Count > 1)
        {
            notices.Add(ConversionNotice.SharedNote);
        }

        if (footnote.Placement == FootnotePlacement.Global && references.Any(r => ReferenceEquals(r.ReferenceFile, footnote.NoteFile)))
        {
            notices.Add(ConversionNotice.BecomesGlobal);
        }

        (string BookPath, string Id) noteKey = (footnote.NoteFile.BookPath, footnote.NoteId);
        string nestedWith = string.Empty;
        if (nested.Outer.TryGetValue(noteKey, out string? innerId))
        {
            warnings.Add(ConversionWarning.ContainsNote);
            nestedWith = innerId;
        }

        if (nested.Inner.TryGetValue(noteKey, out string? outerId))
        {
            warnings.Add(ConversionWarning.InsideNote);
            nestedWith = nestedWith.Length > 0 ? nestedWith : outerId;
        }

        ConversionBlocker blocker;
        if (anchor is null || !allFound)
        {
            blocker = ConversionBlocker.ReferenceNotFound;
        }
        else if (note is null)
        {
            blocker = ConversionBlocker.NoteNotFound;
        }
        else
        {
            blocker = others.Any(l => l.IsReference) ? ConversionBlocker.OtherReference : ConversionBlocker.None;
        }

        return new ConversionItem(footnote, marker, number, blocker == ConversionBlocker.None, warnings, others, text)
        {
            ReferenceRange = anchor is null ? null : (anchor.OuterStart, anchor.OuterEnd),
            References = references,
            Notices = notices,
            Blocker = blocker,
            NestedWith = nestedWith,
        };
    }

    /// <summary>
    /// Converts the footnotes of <paramref name="request"/> to Signet's: the reference and the note get Signet's
    /// template (the text of the note stays), local ones go to the end of their file, global ones to the notes file of
    /// the request; every link to their old ids is rewritten, emptied containers of the old notes go (with the line
    /// right before them), and so do files where nothing but headings is left. Then the footnotes are renumbered and the
    /// result is checked: the texts of the notes, the number of footnotes and the links/footnote pairs check (F7). On a
    /// problem the book is left as it is now; the caller rewinds it (a checkpoint taken before). The same when
    /// <paramref name="cancellationToken"/> is cancelled: an <see cref="OperationCanceledException"/> is thrown and the book is
    /// left half converted, so the caller rewinds it.
    /// </summary>
    public static ConversionResult Convert(
        Book book, ConversionRequest request, FootnoteOptions? options = null, IProgress<ConversionProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(book);
        ArgumentNullException.ThrowIfNull(request);
        List<ConversionItem> items = request.Items.Where(i => i.CanConvert && !i.Footnote.IsSignet).ToList();
        List<Footnote> selected = items.Select(i => i.Footnote).ToList();
        if (selected.Count == 0)
        {
            return new ConversionResult(0, Array.Empty<string>(), Array.Empty<ConversionProblem>());
        }

        progress?.Report(new ConversionProgress(ConversionStage.Preparing, 0, items.Count));
        cancellationToken.ThrowIfCancellationRequested();
        IReadOnlyList<Footnote> scanned = FootnoteScanner.Scan(book);
        int countBefore = scanned.Count;

        // A note inside another one is converted first: moved out, it leaves the outer note with its own text; the other way
        // round the outer note would take the inner one with it and the check would find its text changed.
        Dictionary<HtmlResource, (string Text, TagLister Tags)> parsedFiles = new(ReferenceEqualityComparer.Instance);
        NestedNotes nested = FindNestedNotes(scanned, file =>
        {
            if (!parsedFiles.TryGetValue(file, out (string Text, TagLister Tags) entry))
            {
                string fileText = file.GetText();
                parsedFiles[file] = entry = (fileText, TagLister.For(fileText));
            }

            return entry;
        });
        if (nested.Depth.Count > 0)
        {
            items = items.OrderByDescending(i => nested.Depth.GetValueOrDefault((i.Footnote.NoteFile.BookPath, i.Footnote.NoteId))).ToList();
        }

        Dictionary<(string Code, string BookPath), int> checksBefore = LinkChecks(book);
        HashSet<(string BookPath, string Id)> linked = BookLinks(book).Select(l => (l.Target, l.Fragment)).ToHashSet();
        bool epub3 = book.IsEpub3;

        // Where the global notes go; a taken-over file gets Signet's container (and groups) first.
        List<Footnote> globals = selected.Where(f => f.Placement == FootnotePlacement.Global).ToList();
        HtmlResource? signetFile = FindGlobalFile(book);
        HtmlResource? target = null;
        Dictionary<string, int> groupOf = new(StringComparer.Ordinal);
        if (globals.Count > 0)
        {
            target = request.GlobalFile ?? GetOrCreateGlobalFile(book, globals[0].ReferenceFile);
            if (request.GlobalFile is not null && !ReferenceEquals(request.GlobalFile, signetFile))
            {
                groupOf = TakeOver(target, globals.Where(f => ReferenceEquals(f.NoteFile, target)).ToList(), epub3);
            }
        }

        Dictionary<(string BookPath, string Id), (string BookPath, string Id)> moves = new();
        HashSet<HtmlResource> touched = new(ReferenceEqualityComparer.Instance);
        List<ConvertedNote> converted = new();
        int step = Math.Max(1, items.Count / 200);
        for (int i = 0; i < items.Count; i++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            ConversionItem item = items[i];
            Footnote footnote = item.Footnote;
            IReadOnlyList<Footnote> references = item.References.Count > 0 ? item.References : new[] { footnote };
            (string tempId, string content) = ConvertOne(references, footnote.Placement == FootnotePlacement.Local ? footnote.ReferenceFile : target!,
                groupOf, request.KeepClasses, epub3, linked, moves, touched);
            converted.Add(new ConvertedNote(tempId, content, footnote.NoteFile.BookPath, footnote.NoteId, item.Marker));
            if (i % step == 0)
            {
                progress?.Report(new ConversionProgress(ConversionStage.Converting, i + 1, items.Count));
            }
        }

        progress?.Report(new ConversionProgress(ConversionStage.Converting, items.Count, items.Count));
        progress?.Report(new ConversionProgress(ConversionStage.Finishing, items.Count, items.Count));
        cancellationToken.ThrowIfCancellationRequested();

        // Signet's own global notes move to a taken-over file.
        if (target is not null && signetFile is not null && !ReferenceEquals(target, signetFile))
        {
            MoveSignetNotes(book, signetFile, target, moves);
            touched.Add(signetFile);
        }

        foreach (HtmlResource file in touched)
        {
            RemoveLoneLines(file);
        }

        // Files left with nothing but headings go; links to them lead to the notes file.
        List<string> removed = new();
        Dictionary<string, string> removedFiles = new(StringComparer.Ordinal);
        // The files of the book once: asking for them for each touched file of a big book took seconds.
        HashSet<HtmlResource> inBook = new(book.GetHtmlResources(), ReferenceEqualityComparer.Instance);
        foreach (HtmlResource file in touched.Where(f => !ReferenceEquals(f, target) && inBook.Contains(f)))
        {
            if (target is not null && OnlyHeadingsLeft(file.GetText()))
            {
                removedFiles[file.BookPath] = target.BookPath;
                removed.Add(file.BookPath);
                book.GetFolderKeeper().BulkRemoveResources(new Resource[] { file });
            }
        }

        RetargetLinks(book, moves, removedFiles);

        // The final numbers and ids; the links to the temporary ids follow them.
        List<Footnote> beforeRenumber = FootnoteScanner.Scan(book).ToList();
        Dictionary<string, int> beforeIndex = new(StringComparer.Ordinal);
        for (int i = 0; i < beforeRenumber.Count; i++)
        {
            beforeIndex.TryAdd(beforeRenumber[i].NoteId, i);
        }

        List<int> indexes = converted.Select(c => beforeIndex.GetValueOrDefault(c.TempId, -1)).ToList();
        cancellationToken.ThrowIfCancellationRequested();
        RenumberWith(book, options, null, cancel: cancellationToken);
        if (signetFile is not null && !ReferenceEquals(signetFile, target) && book.GetHtmlResources().Contains(signetFile))
        {
            RemoveEmptyContainer(book, signetFile);
        }

        List<Footnote> after = FootnoteScanner.Scan(book).ToList();
        Dictionary<(string BookPath, string Id), (string BookPath, string Id)> finals = new();
        for (int i = 0; i < indexes.Count; i++)
        {
            if (indexes[i] >= 0 && indexes[i] < after.Count && indexes[i] < beforeRenumber.Count)
            {
                Footnote was = beforeRenumber[indexes[i]];
                Footnote now = after[indexes[i]];
                finals[(was.NoteFile.BookPath, was.NoteId)] = (now.NoteFile.BookPath, now.NoteId);
                finals[(was.ReferenceFile.BookPath, was.ReferenceId)] = (now.ReferenceFile.BookPath, now.ReferenceId);
            }
        }

        RetargetLinks(book, finals, new Dictionary<string, string>());
        book.Modified = true;

        cancellationToken.ThrowIfCancellationRequested();
        progress?.Report(new ConversionProgress(ConversionStage.Checking, items.Count, items.Count));
        List<ConversionProblem> problems = Check(book, converted, indexes, countBefore, checksBefore);
        return new ConversionResult(selected.Count, removed, problems);
    }

    // A footnote converted by Convert: its temporary note id, the markup its note must keep inside, and where it was.
    private sealed record ConvertedNote(string TempId, string Content, string SourcePath, string SourceId, string Marker);

    // Converts one footnote (all the references of its note, the last first: the occurrences of the earlier ones in a
    // file stay as they are); returns its temporary note id and the markup its note must keep inside.
    private static (string TempId, string Content) ConvertOne(
        IReadOnlyList<Footnote> references, HtmlResource target, Dictionary<string, int> groupOf, bool keepClasses, bool epub3,
        HashSet<(string BookPath, string Id)> linked, Dictionary<(string BookPath, string Id), (string BookPath, string Id)> moves,
        HashSet<HtmlResource> touched)
    {
        Footnote footnote = references[0];
        bool local = footnote.Placement == FootnotePlacement.Local;
        string noteId = "fn-new-" + Ulid.NewUlid().ToString().ToLowerInvariant();
        List<string> referenceIds = references
            .Select((_, i) => noteId + "-ref" + (i == 0 ? string.Empty : "-" + i.ToString(CultureInfo.InvariantCulture)))
            .ToList();
        for (int i = references.Count - 1; i >= 0; i--)
        {
            ConvertReference(references[i], referenceIds[i], target, noteId, keepClasses, epub3, moves, touched);
        }

        // The note: its text without the link back, in Signet's template.
        HtmlResource noteFile = footnote.NoteFile;
        string text = noteFile.GetText();
        TagLister tags = TagLister.For(text);
        Element note = FindElementById(tags, text, footnote.NoteId)!;
        (Anchor? backlink, int backTo) = NoteBacklink(tags, text, note, references);
        (string content, _) = CutContent(tags, text, note, backlink);
        List<string> noteClasses = keepClasses ? Classes(text, tags.At(note.OpenIndex)).ToList() : new List<string>();
        string tag = epub3 ? "aside" : "div";
        string open = "<" + tag + " class=\"" + JoinClasses(NoteClass, noteClasses) + "\""
            + (!epub3 ? string.Empty : local ? " epub:type=\"footnote\"" : " epub:type=\"endnote\"")
            + " id=\"" + noteId + "\">";
        string newBacklink = "<a href=\"" + Encode(Href(target.BookPath, references[backTo].ReferenceFile.BookPath, referenceIds[backTo])) + "\">0.</a>";
        string markup = open + NoteInside(NoteBlocks(content, keepClasses), newBacklink) + "</" + tag + ">";

        // Ids inside the note follow it to another file.
        if (!ReferenceEquals(noteFile, target))
        {
            foreach (string id in MarkupEdits.CollectIds(content))
            {
                moves[(noteFile.BookPath, id)] = (target.BookPath, id);
            }
        }

        (int noteStart, int noteEnd) = WholeLines(text, note.Start, note.End);
        text = text.Remove(noteStart, noteEnd - noteStart);
        noteFile.SetText(RemoveEmptyAncestors(text, noteStart, noteFile.BookPath, linked));
        touched.Add(noteFile);

        if (!local && groupOf.TryGetValue(footnote.NoteId, out int group))
        {
            InsertIntoGroup(target, group, markup);
        }
        else
        {
            InsertNote(target, markup, local ? FootnotePlacement.Local : FootnotePlacement.Global, epub3);
        }

        moves[(noteFile.BookPath, footnote.NoteId)] = (target.BookPath, noteId);
        return (noteId, content);
    }

    // The link back of a note of other origin and the reference it returns to (the first reference without one).
    private static (Anchor? Backlink, int Reference) NoteBacklink(TagLister tags, string text, Element note, IReadOnlyList<Footnote> references)
    {
        for (int i = 0; i < references.Count; i++)
        {
            if (references[i].ReferenceId.Length > 0 && FindBacklink(tags, text, note, references[i].ReferenceId) is { } backlink)
            {
                return (backlink, i);
            }
        }

        return (null, 0);
    }

    // Puts Signet's reference in place of a reference of other origin.
    private static void ConvertReference(
        Footnote footnote, string referenceId, HtmlResource target, string noteId, bool keepClasses, bool epub3,
        Dictionary<(string BookPath, string Id), (string BookPath, string Id)> moves, HashSet<HtmlResource> touched)
    {
        HtmlResource referenceFile = footnote.ReferenceFile;

        // The reference, with the wrappers that held only it and the empty element its link back led to.
        string text = referenceFile.GetText();
        TagLister tags = TagLister.For(text);
        Anchor anchor = FindAnchor(tags, text, footnote.ReferenceHref, footnote.ReferenceOccurrence)!;
        string? returnId = footnote.ReferenceId.Length > 0 ? footnote.ReferenceId : null;
        (int start, int end) = Unwrap(tags, text, anchor.OuterStart, anchor.OuterEnd, returnId);
        List<string> referenceClasses = new();
        if (keepClasses)
        {
            for (int i = 0; i < tags.Count && tags.At(i).Pos >= 0 && tags.At(i).Pos <= anchor.Start; i++)
            {
                if (tags.At(i).Pos >= start && IsOpening(tags.At(i)))
                {
                    referenceClasses.AddRange(Classes(text, tags.At(i)));
                }
            }
        }

        string reference = "<a class=\"" + JoinClasses(ReferenceClass, referenceClasses) + "\""
            + (epub3 ? " epub:type=\"noteref\"" : string.Empty)
            + " href=\"" + Encode(Href(referenceFile.BookPath, target.BookPath, noteId)) + "\" id=\"" + referenceId + "\">[0]</a>";
        List<TextEdit> edits = new() { new TextEdit(start, end - start, reference) };
        if (returnId is not null
            && FindElementById(tags, text, returnId) is { } empty
            && (empty.End <= start || empty.Start >= end)
            && Tag.Replace(text[empty.Start..empty.End], string.Empty).Trim().Length == 0)
        {
            (int emptyStart, int emptyEnd) = Unwrap(tags, text, empty.Start, empty.End, null);
            edits.Add(new TextEdit(emptyStart, emptyEnd - emptyStart, string.Empty));
        }

        referenceFile.SetText(Apply(text, edits));
        touched.Add(referenceFile);
        if (returnId is not null)
        {
            moves[(referenceFile.BookPath, returnId)] = (referenceFile.BookPath, referenceId);
        }
    }

    // The inside of a note without its link back (and what is left of its marker next to it), and whether the link back
    // was at its start.
    private static (string Content, bool BacklinkAtStart) CutContent(TagLister tags, string text, Element note, Anchor? backlink)
    {
        int innerStart = tags.At(note.OpenIndex).Pos + tags.At(note.OpenIndex).Len;
        int innerEnd = note.CloseStart;
        if (backlink is null)
        {
            return (text[innerStart..innerEnd], false);
        }

        (int start, int end) = Unwrap(tags, text, backlink.Start, backlink.End, null);
        start = Math.Max(start, innerStart);
        end = Math.Min(end, innerEnd);
        bool atStart = PlainText(text[innerStart..start]).Length == 0;
        if (atStart)
        {
            while (end < innerEnd && MarkerResidue.Contains(text[end], StringComparison.Ordinal))
            {
                end++;
            }
        }

        return (text[innerStart..start] + text[end..innerEnd], atStart);
    }

    // A block of a note's text: a paragraph (its attributes without the class, its classes and its inside) or another
    // block kept as it is.
    private sealed record NoteBlock(bool IsParagraph, string Attributes, IReadOnlyList<string> Classes, string Markup);

    // The blocks of a note's text: its top-level elements (inline content between them becomes paragraphs), or one
    // paragraph when it has no block element.
    private static List<NoteBlock> NoteBlocks(string content, bool keepClasses)
    {
        TagLister tags = TagLister.For(content);
        List<NoteBlock> blocks = new();
        StringBuilder inline = new();

        void FlushInline()
        {
            string run = inline.ToString().Trim();
            if (PlainText(run).Length > 0 || Regex.IsMatch(run, "<(img|svg|math)\\b", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant))
            {
                blocks.Add(new NoteBlock(true, string.Empty, Array.Empty<string>(), run));
            }

            inline.Clear();
        }

        bool hasBlock = false;
        int position = 0;
        int i = 0;
        while (i < tags.Count && tags.At(i).Pos >= 0)
        {
            TagLister.TagInfo tag = tags.At(i);
            if (!IsOpening(tag) || !BlockNames.Contains(tag.TagName))
            {
                i++;
                continue;
            }

            hasBlock = true;
            inline.Append(content, position, tag.Pos - position);
            FlushInline();
            Element? element = ToElement(tags, content, i);
            if (element is null)
            {
                break;
            }

            if (tag.TagName.Equals("p", StringComparison.OrdinalIgnoreCase) && tag.Kind == TagKind.Begin)
            {
                string inside = content[(tag.Pos + tag.Len)..element.CloseStart].Trim();
                if (PlainText(inside).Length > 0 || inside.Contains('<', StringComparison.Ordinal))
                {
                    string openTag = content.Substring(tag.Pos, tag.Len);
                    blocks.Add(new NoteBlock(
                        true, AttributesWithoutClass(openTag), keepClasses ? Classes(content, tag).ToList() : Array.Empty<string>(), inside));
                }
            }
            else
            {
                blocks.Add(new NoteBlock(false, string.Empty, Array.Empty<string>(), content[element.Start..element.End]));
            }

            position = element.End;
            i = tag.Kind == TagKind.SelfClosing ? i + 1 : tags.FindCloseTagForOpen(i) + 1;
        }

        if (!hasBlock)
        {
            return new List<NoteBlock> { new(true, string.Empty, Array.Empty<string>(), content.Trim()) };
        }

        inline.Append(content, position, content.Length - position);
        FlushInline();
        return blocks;
    }

    // The inside of a converted note: the link back starts the first paragraph (or a paragraph of its own before a first
    // block that is not one); a single plain paragraph stays on the note's line, more blocks go on lines of their own.
    private static string NoteInside(List<NoteBlock> blocks, string backlink)
    {
        List<string> lines = new();
        if (blocks.Count == 0 || !blocks[0].IsParagraph)
        {
            lines.Add(ParagraphTag + backlink + "</p>");
        }

        for (int i = 0; i < blocks.Count; i++)
        {
            NoteBlock block = blocks[i];
            if (!block.IsParagraph)
            {
                lines.Add(block.Markup);
                continue;
            }

            string start = "<p class=\"" + JoinClasses(ParagraphClass, block.Classes) + "\"" + block.Attributes + ">";
            lines.Add(start + (lines.Count == 0 ? backlink + " " : string.Empty) + block.Markup + "</p>");
        }

        return lines.Count == 1 ? lines[0] : "\n  " + string.Join("\n  ", lines.Select(l => l.Replace("\n", "\n  ", StringComparison.Ordinal))) + "\n";
    }

    // Signet's container in place of the notes of a taken-over file (before the first of them, or before the list they
    // are in); the headings between the notes become the titles of groups. Returns the group of each note.
    private static Dictionary<string, int> TakeOver(HtmlResource file, List<Footnote> notes, bool epub3)
    {
        Dictionary<string, int> groupOf = new(StringComparer.Ordinal);
        string text = file.GetText();
        TagLister tags = TagLister.For(text);
        List<Element> elements = notes
            .Select(n => FindElementById(tags, text, n.NoteId))
            .OfType<Element>()
            .OrderBy(e => e.Start)
            .ToList();
        string section = epub3 ? "section" : "div";
        string open = "<" + section + " class=\"" + GlobalContainerClass + "\"" + (epub3 ? " epub:type=\"endnotes\"" : string.Empty) + ">";
        if (elements.Count == 0)
        {
            int bodyClose = tags.FindBodyCloseTag();
            int at = bodyClose >= 0 ? LineStartIfBlank(text, tags.At(bodyClose).Pos) : text.Length;
            file.SetText(text.Insert(at, open + "\n</" + section + ">\n"));
            return groupOf;
        }

        // Before the first note, or before the lists it is in.
        int insertAt = elements[0].Start;
        foreach (TagLister.TagInfo parent in Enumerable.Reverse(OpenElementsAt(tags, elements[0].Start)))
        {
            if (!ListNames.Contains(parent.TagName))
            {
                break;
            }

            insertAt = parent.Pos;
        }

        // The headings between the notes (outside them) start groups.
        int first = elements[0].Start;
        int last = elements[^1].Start;
        List<Element> headings = new();
        for (int i = 0; i < tags.Count && tags.At(i).Pos >= 0; i++)
        {
            TagLister.TagInfo tag = tags.At(i);
            if (tag.Kind == TagKind.Begin && IsHeading(tag.TagName) && tag.Pos > first && tag.Pos < last
                && !elements.Any(e => tag.Pos >= e.Start && tag.Pos < e.End)
                && ToElement(tags, text, i) is { } heading)
            {
                headings.Add(heading);
            }
        }

        // The last heading before the first note opens the first group when it is of the level of those between them.
        if (headings.Count > 0)
        {
            string level = tags.At(headings[0].OpenIndex).TagName;
            Element? before = null;
            for (int i = 0; i < tags.Count && tags.At(i).Pos >= 0 && tags.At(i).Pos < first; i++)
            {
                if (tags.At(i).Kind == TagKind.Begin && IsHeading(tags.At(i).TagName) && ToElement(tags, text, i) is { } heading
                    && heading.End <= first)
                {
                    before = heading;
                }
            }

            if (before is not null && tags.At(before.OpenIndex).TagName.Equals(level, StringComparison.OrdinalIgnoreCase))
            {
                headings.Insert(0, before);
            }
        }

        string indent = LineIndent(text, insertAt);
        StringBuilder container = new(open);
        if (headings.Count > 0)
        {
            bool leading = elements.Any(e => e.Start < headings[0].Start);
            int offset = leading ? 1 : 0;
            foreach ((Element element, Footnote footnote) in elements.Zip(notes.OrderBy(n => FindElementById(tags, text, n.NoteId)?.Start ?? 0)))
            {
                groupOf[footnote.NoteId] = headings.Count(h => h.Start < element.Start) - 1 + offset;
            }

            string groupOpen = "<" + section + " class=\"" + GroupClass + "\">";
            if (leading)
            {
                container.Append('\n').Append(indent).Append("  ").Append(groupOpen).Append('\n').Append(indent).Append("  </").Append(section).Append('>');
            }

            foreach (Element heading in headings)
            {
                string title = AddClass(text.Substring(heading.Start, tags.At(heading.OpenIndex).Len), GroupTitleClass)
                    + text[(heading.Start + tags.At(heading.OpenIndex).Len)..heading.End];
                container.Append('\n').Append(indent).Append("  ").Append(groupOpen)
                    .Append('\n').Append(indent).Append("    ").Append(title)
                    .Append('\n').Append(indent).Append("  </").Append(section).Append('>');
            }
        }

        container.Append('\n').Append(indent).Append("</").Append(section).Append(">\n");
        List<TextEdit> edits = headings.Select(h => WholeLines(text, h.Start, h.End)).Select(r => new TextEdit(r.Start, r.End - r.Start, string.Empty)).ToList();
        int lineStart = LineStartIfBlank(text, insertAt);
        edits.Add(new TextEdit(lineStart, 0, (lineStart == insertAt ? string.Empty : "\n") + indent + container.ToString().TrimStart()));
        file.SetText(Apply(text, edits));
        return groupOf;
    }

    // Puts a note at the end of the group-th group of the global container.
    private static void InsertIntoGroup(HtmlResource file, int group, string note)
    {
        string text = file.GetText();
        TagLister tags = TagLister.For(text);
        Element container = FindContainer(tags, text, GlobalContainerClass)!;
        Element target = ChildElements(tags, text, container)
            .Where(c => HasClass(text.Substring(c.Start, tags.At(c.OpenIndex).Len), GroupClass))
            .ElementAt(group);
        file.SetText(InsertBeforeClose(text, target.CloseStart, note));
    }

    // Signet's notes of its own notes file move to the taken-over file (their references follow them).
    private static void MoveSignetNotes(
        Book book, HtmlResource from, HtmlResource to, Dictionary<(string BookPath, string Id), (string BookPath, string Id)> moves)
    {
        foreach (Footnote footnote in FootnoteScanner.Scan(book).Where(f => f.IsSignet && ReferenceEquals(f.NoteFile, from)))
        {
            string text = from.GetText();
            Element? note = FindElementById(TagLister.For(text), text, footnote.NoteId);
            if (note is null)
            {
                continue;
            }

            string markup = Unindent(text, note);
            (int start, int end) = WholeLines(text, note.Start, note.End);
            from.SetText(text.Remove(start, end - start));
            InsertNote(to, markup, FootnotePlacement.Global, book.IsEpub3);
            string referenceText = footnote.ReferenceFile.GetText();
            if (FindAnchor(TagLister.For(referenceText), referenceText, footnote.ReferenceHref, footnote.ReferenceOccurrence) is { Href: { } href })
            {
                footnote.ReferenceFile.SetText(Apply(
                    referenceText, new[] { new TextEdit(href.Start, href.Length, Encode(Href(footnote.ReferenceFile.BookPath, to.BookPath, footnote.NoteId))) }));
            }

            moves[(from.BookPath, footnote.NoteId)] = (to.BookPath, footnote.NoteId);
        }
    }

    // Removes the elements around `offset` that a removed note left empty (no element, no text), up to the body; one
    // that something links to stays, and so does Signet's container.
    private static string RemoveEmptyAncestors(string text, int offset, string bookPath, HashSet<(string BookPath, string Id)> linked)
    {
        while (true)
        {
            TagLister tags = TagLister.For(text);
            List<TagLister.TagInfo> open = OpenElementsAt(tags, offset);
            if (open.Count == 0)
            {
                return text;
            }

            TagLister.TagInfo innermost = open[^1];
            int index = tags.FindLastTagOnOrBefore(innermost.Pos);
            if (innermost.TagName.Equals("body", StringComparison.OrdinalIgnoreCase)
                || ToElement(tags, text, index) is not { } element
                || HasClass(text.Substring(innermost.Pos, innermost.Len), LocalContainerClass)
                || HasClass(text.Substring(innermost.Pos, innermost.Len), GlobalContainerClass)
                || (element.IdValue.Length > 0 && linked.Contains((bookPath, element.IdValue))))
            {
                return text;
            }

            string inside = text[(innermost.Pos + innermost.Len)..element.CloseStart];
            if (Regex.Replace(inside, "<!--.*?-->", string.Empty, RegexOptions.Singleline | RegexOptions.CultureInvariant).Trim().Length > 0)
            {
                return text;
            }

            (int start, int end) = WholeLines(text, element.Start, element.End);
            text = text.Remove(start, end - start);
            offset = start;
        }
    }

    // A line (<hr/>) or a notes heading ("Notes"…) left at the end of its block after the notes behind it went: what
    // follows it is the end of the block, or Signet's line and notes.
    private static void RemoveLoneLines(HtmlResource file)
    {
        bool changed = true;
        while (changed)
        {
            changed = false;
            string text = file.GetText();
            TagLister tags = TagLister.For(text);
            for (int i = 0; i < tags.Count && tags.At(i).Pos >= 0; i++)
            {
                TagLister.TagInfo tag = tags.At(i);
                bool isLine = tag.TagName.Equals("hr", StringComparison.OrdinalIgnoreCase) && IsOpening(tag)
                    && AttributeValue(text, tag, "id").Length == 0 && !HasClass(text.Substring(tag.Pos, tag.Len), SeparatorClass);
                bool isHeading = tag.Kind == TagKind.Begin && IsHeading(tag.TagName) && AttributeValue(text, tag, "id").Length == 0;
                if ((!isLine && !isHeading) || ToElement(tags, text, i) is not { } element)
                {
                    continue;
                }

                if (isHeading && !NotesHeadingText.IsMatch(PlainText(text[(tag.Pos + tag.Len)..element.CloseStart])))
                {
                    continue;
                }

                int next = i + (tag.Kind == TagKind.SelfClosing ? 1 : tags.FindCloseTagForOpen(i) - i + 1);
                while (next < tags.Count && tags.At(next).Pos >= 0 && tags.At(next).Kind == TagKind.Comment)
                {
                    next++;
                }

                if (next >= tags.Count || tags.At(next).Pos < 0 || !string.IsNullOrWhiteSpace(text[element.End..tags.At(next).Pos]))
                {
                    continue;
                }

                TagLister.TagInfo following = tags.At(next);
                string followingTag = text.Substring(following.Pos, following.Len);
                if (following.Kind == TagKind.End || HasClass(followingTag, SeparatorClass) || HasClass(followingTag, LocalContainerClass)
                    || (isHeading is false && IsHeading(following.TagName) && following.Kind == TagKind.Begin && LoneHeadingFollows(tags, text, next)))
                {
                    (int start, int end) = WholeLines(text, element.Start, element.End);
                    file.SetText(text.Remove(start, end - start));
                    changed = true;
                    break;
                }
            }
        }
    }

    // Whether the heading at `index` is a notes heading followed by the end of its block or Signet's notes (so the line
    // before it goes with it).
    private static bool LoneHeadingFollows(TagLister tags, string text, int index)
    {
        TagLister.TagInfo tag = tags.At(index);
        int close = tags.FindCloseTagForOpen(index);
        if (close < 0 || !NotesHeadingText.IsMatch(PlainText(text[(tag.Pos + tag.Len)..tags.At(close).Pos])))
        {
            return false;
        }

        int next = close + 1;
        if (next >= tags.Count || tags.At(next).Pos < 0)
        {
            return false;
        }

        string nextTag = text.Substring(tags.At(next).Pos, tags.At(next).Len);
        return tags.At(next).Kind == TagKind.End || HasClass(nextTag, SeparatorClass) || HasClass(nextTag, LocalContainerClass);
    }

    // Whether nothing but headings, lines and whitespace is left in the body.
    private static bool OnlyHeadingsLeft(string text)
    {
        TagLister tags = TagLister.For(text);
        int body = tags.FindBodyOpenTag();
        int bodyClose = tags.FindBodyCloseTag();
        if (body < 0 || bodyClose < 0)
        {
            return false;
        }

        string inside = text[(tags.At(body).Pos + tags.At(body).Len)..tags.At(bodyClose).Pos];
        string rest = Regex.Replace(inside, "<h[1-6]\\b.*?</h[1-6]\\s*>", string.Empty, RegexOptions.Singleline | RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
        rest = Regex.Replace(rest, "<!--.*?-->", string.Empty, RegexOptions.Singleline | RegexOptions.CultureInvariant);
        rest = Regex.Replace(rest, "<(/?)(div|section|hr|br)\\b[^>]*>", string.Empty, RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
        return rest.Trim().Length == 0;
    }

    // Every link of the book (the (X)HTML files, the NCX and the guide), with the file and the id it leads to.
    private static List<BookLink> BookLinks(Book book)
    {
        List<BookLink> links = new();
        void Collect(Resource owner, string text, IReadOnlyList<string> attributes)
        {
            foreach ((int pos, int len) in TagLister.EnumerateOpeningTags(text))
            {
                string tag = text.Substring(pos, len);
                foreach (string attribute in attributes)
                {
                    TagLister.AttInfo info = TagLister.ParseAttribute(tag, attribute);
                    if (info.Pos >= 0 && info.VPos >= 0 && Resolve(owner, System.Net.WebUtility.HtmlDecode(info.AValue)) is { } target)
                    {
                        links.Add(new BookLink(owner, pos, target.BookPath, target.Fragment));
                    }
                }
            }
        }

        foreach (HtmlResource html in book.GetHtmlResources())
        {
            Collect(html, html.GetText(), MarkupEdits.LinkAttributes);
        }

        if (book.GetNcx() is { } ncx)
        {
            Collect(ncx, ncx.GetText(), MarkupEdits.SrcAttribute);
        }

        OpfResource opf = book.GetOpf();
        foreach (GuideEntry entry in opf.GetOpfDocument().Guide)
        {
            if (Resolve(opf, entry.Href) is { } target)
            {
                links.Add(new BookLink(opf, -1, target.BookPath, target.Fragment));
            }
        }

        return links;
    }

    // Whether a link looks like a footnote reference: epub:type="noteref", or a marker for its text.
    private static bool LooksLikeReference(BookLink link)
    {
        if (link.Owner is not HtmlResource html)
        {
            return false;
        }

        string text = html.GetText();
        TagLister tags = TagLister.For(text);
        int index = tags.FindLastTagOnOrBefore(link.Position);
        if (index < 0 || tags.At(index).Pos != link.Position || !tags.At(index).TagName.Equals("a", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        TagLister.TagInfo tag = tags.At(index);
        if (AttributeValue(text, tag, "epub:type").Split(' ').Contains("noteref", StringComparer.Ordinal))
        {
            return true;
        }

        int close = tags.FindCloseTagForOpen(index);
        return close >= 0 && FootnoteLinks.IsMarker(PlainText(text[(tag.Pos + tag.Len)..tags.At(close).Pos]));
    }

    private static (string BookPath, string Fragment)? Resolve(Resource owner, string href)
    {
        (string path, string fragment) = SplitHref(href);
        if (IsExternal(path) || (path.Length == 0 && fragment.Length == 0))
        {
            return null;
        }

        return (path.Length == 0 ? owner.BookPath : Core.BookPath.BuildBookPath(Utility.UrlDecodePath(path), owner.Folder), fragment);
    }

    // Rewrites the links to moved ids and to removed files (which lead to their replacement, without the fragment).
    private static void RetargetLinks(
        Book book, Dictionary<(string BookPath, string Id), (string BookPath, string Id)> moves, Dictionary<string, string> removedFiles)
    {
        if (moves.Count == 0 && removedFiles.Count == 0)
        {
            return;
        }

        string? Retarget(Resource owner, string href)
        {
            if (Resolve(owner, href) is not { } target)
            {
                return null;
            }

            if (target.Fragment.Length > 0 && moves.TryGetValue((target.BookPath, target.Fragment), out (string BookPath, string Id) moved))
            {
                return Href(owner.BookPath, moved.BookPath, moved.Id);
            }

            if (removedFiles.TryGetValue(target.BookPath, out string? replacement))
            {
                return Utility.UrlEncodePath(Core.BookPath.Relative(owner.BookPath, replacement));
            }

            return null;
        }

        foreach (HtmlResource html in book.GetHtmlResources())
        {
            string text = html.GetText();
            string updated = MarkupEdits.ReplaceAttributeValues(text, MarkupEdits.LinkAttributes, value => Retarget(html, value));
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

    // The links and footnote pairs check (F7) as counts of each kind of result in each file.
    private static Dictionary<(string Code, string BookPath), int> LinkChecks(Book book) =>
        LinkIntegrityValidator.Validate(book).Concat(FootnoteValidator.Validate(book))
            .GroupBy(r => (r.Code.Length > 0 ? r.Code : r.Message, r.BookPath))
            .ToDictionary(g => g.Key, g => g.Count());

    private static List<ConversionProblem> Check(
        Book book, List<ConvertedNote> converted, List<int> indexes, int countBefore,
        Dictionary<(string Code, string BookPath), int> checksBefore)
    {
        List<ConversionProblem> problems = new();
        IReadOnlyList<Footnote> after = FootnoteScanner.Scan(book);
        if (after.Count != countBefore)
        {
            problems.Add(new ConversionProblem(
                ConversionProblemKind.CountChanged, string.Empty, countBefore.ToString(CultureInfo.InvariantCulture) + " → "
                + after.Count.ToString(CultureInfo.InvariantCulture)));
        }

        for (int i = 0; i < converted.Count; i++)
        {
            ConvertedNote was = converted[i];
            if (indexes[i] < 0 || indexes[i] >= after.Count)
            {
                problems.Add(new ConversionProblem(ConversionProblemKind.NotSignet, string.Empty, was.TempId) { SourcePath = was.SourcePath, SourceId = was.SourceId, Marker = was.Marker });
                continue;
            }

            Footnote footnote = after[indexes[i]];
            if (!footnote.IsSignet)
            {
                problems.Add(new ConversionProblem(ConversionProblemKind.NotSignet, footnote.NoteFile.BookPath, footnote.NoteId) { SourcePath = was.SourcePath, SourceId = was.SourceId, Marker = was.Marker });
                continue;
            }

            string text = footnote.NoteFile.GetText();
            TagLister tags = TagLister.For(text);
            string? actual = FindElementById(tags, text, footnote.NoteId) is { } note
                ? ConvertedContent(tags, text, note, footnote)
                : null;
            if (actual is null || Compact(PlainText(actual)) != Compact(PlainText(was.Content)))
            {
                problems.Add(new ConversionProblem(
                    ConversionProblemKind.TextChanged, footnote.NoteFile.BookPath, footnote.NoteId,
                    ReadableText(was.Content), actual is null ? null : ReadableText(actual))
                {
                    SourcePath = was.SourcePath,
                    SourceId = was.SourceId,
                    Marker = was.Marker,
                });
            }
        }

        foreach (((string code, string bookPath), int count) in LinkChecks(book))
        {
            if (count > checksBefore.GetValueOrDefault((code, bookPath)))
            {
                problems.Add(new ConversionProblem(ConversionProblemKind.NewValidationResult, bookPath, code));
            }
        }

        return problems;
    }

    // The inside of a converted note without its links back ("1." or "1a, 1b.").
    private static string ConvertedContent(TagLister tags, string text, Element note, Footnote footnote)
    {
        if (BacklinkRun(tags, text, note) is not { } run)
        {
            return CutContent(tags, text, note, FindBacklink(tags, text, note, footnote.ReferenceId)).Content;
        }

        int innerStart = tags.At(note.OpenIndex).Pos + tags.At(note.OpenIndex).Len;
        return text[innerStart..run.Start] + text[run.End..note.CloseStart];
    }

    // Text without any whitespace: the layout of the blocks may change, the words may not.
    private static string Compact(string text) => Regex.Replace(text, "\\s+", string.Empty, RegexOptions.CultureInvariant);

    private static string[] Classes(string text, TagLister.TagInfo tag) =>
        AttributeValue(text, tag, "class").Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);

    private static string JoinClasses(string signetClass, IEnumerable<string> others) =>
        string.Join(' ', new[] { signetClass }.Concat(others.Where(c => c != signetClass)).Distinct(StringComparer.Ordinal));

    // The attributes of an opening tag without its class (with their leading spaces, as written).
    private static string AttributesWithoutClass(string openTag)
    {
        int nameEnd = 1;
        while (nameEnd < openTag.Length && !char.IsWhiteSpace(openTag[nameEnd]) && openTag[nameEnd] is not '>' and not '/')
        {
            nameEnd++;
        }

        string attributes = openTag[nameEnd..].TrimEnd('>').TrimEnd('/').TrimEnd();
        return Regex.Replace(attributes, "\\s+class\\s*=\\s*(\"[^\"]*\"|'[^']*')", string.Empty, RegexOptions.CultureInvariant);
    }
}
