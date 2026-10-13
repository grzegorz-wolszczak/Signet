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
}

/// <summary>A footnote of other origin in the conversion plan (<see cref="FootnoteEditor.PlanConversion"/>).</summary>
/// <param name="Footnote">The footnote.</param>
/// <param name="Marker">The text of its reference.</param>
/// <param name="NewNumber">The number it gets.</param>
/// <param name="CanConvert">
/// Whether it can be converted: <c>false</c> when several references (links that look like footnote markers, or
/// <c>noteref</c>) lead to its note — Signet's footnote has one. Other links to it are rewritten.
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
public sealed record ConversionProblem(ConversionProblemKind Kind, string BookPath, string Detail);

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

    // The text of a link that looks like a footnote marker: "3", "[3]", "(3)", "3.", "*", "†"…
    private static readonly Regex MarkerText = new(@"^[\[(]?\s*(\d+|[*†‡§¶#]+)\s*[\])]?\.?$", RegexOptions.CultureInvariant);

    // A link of the book: its owner, where its attribute value is, and what it leads to.
    private sealed record BookLink(Resource Owner, int Position, string Target, string Fragment);

    /// <summary>
    /// What converting the footnotes of other origin would do: each with the number it gets and what needs a look, and
    /// the candidates for the notes file of the global footnotes. It only reads the book, so it can run off the UI
    /// thread while nothing edits the book.
    /// </summary>
    public static ConversionPlan PlanConversion(Book book)
    {
        ArgumentNullException.ThrowIfNull(book);
        IReadOnlyList<Footnote> footnotes = FootnoteScanner.Scan(book);

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
                parsed[file] = entry = (text, new TagLister(text));
            }

            return entry;
        }

        Dictionary<HtmlResource, int> local = new(ReferenceEqualityComparer.Instance);
        int global = 0;
        List<ConversionItem> items = new();
        foreach (Footnote footnote in footnotes)
        {
            int number = footnote.Placement == FootnotePlacement.Local
                ? local[footnote.ReferenceFile] = local.GetValueOrDefault(footnote.ReferenceFile) + 1
                : ++global;
            if (!footnote.IsSignet)
            {
                items.Add(PlanItem(footnote, number, links, Parsed));
            }
        }

        IReadOnlyList<GlobalFileCandidate> candidates = GlobalFileCandidates(book);
        return new ConversionPlan(items, candidates, AutomaticGlobalFile(candidates));
    }

    private static ConversionItem PlanItem(
        Footnote footnote,
        int number,
        Dictionary<(string Target, string Fragment), List<BookLink>> links,
        Func<HtmlResource, (string Text, TagLister Tags)> parsed)
    {
        (string referenceText, TagLister referenceTags) = parsed(footnote.ReferenceFile);
        Anchor? anchor = FindAnchor(referenceTags, referenceText, footnote.ReferenceHref, footnote.ReferenceOccurrence);
        string marker = anchor is null ? string.Empty : PlainText(referenceText[anchor.InnerStart..anchor.InnerEnd]);
        (string noteText, TagLister noteTags) = parsed(footnote.NoteFile);
        Element? note = FindElementById(noteTags, noteText, footnote.NoteId);

        bool InNote(BookLink link) =>
            note is not null && ReferenceEquals(link.Owner, footnote.NoteFile) && link.Position >= note.Start && link.Position < note.End;

        List<BookLink> LinksTo(string target, string fragment) =>
            links.TryGetValue((target, fragment), out List<BookLink>? found) ? found.Where(l => !InNote(l)).ToList() : new List<BookLink>();

        List<BookLink> toNote = LinksTo(footnote.NoteFile.BookPath, footnote.NoteId);
        List<BookLink> toReference = footnote.ReferenceId.Length == 0
            ? new List<BookLink>()
            : LinksTo(footnote.ReferenceFile.BookPath, footnote.ReferenceId);

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
            anchor is not null && ReferenceEquals(link.Owner, footnote.ReferenceFile) && link.Position == anchor.Start;

        bool IsReference(BookLink link) => link.Position >= 0 && LooksLikeReference(link);
        int references = toNote.Count(l => IsTheReference(l) || IsReference(l));
        List<ConversionLink> others = toNote.Where(l => !IsTheReference(l))
            .Select(l => new ConversionLink(l.Owner, l.Position, IsReference(l)))
            .Concat(toReference.Select(l => new ConversionLink(l.Owner, l.Position, false)))
            .ToList();
        if (others.Count > 0)
        {
            warnings.Add(ConversionWarning.LinkedElsewhere);
        }

        Anchor? backlink = note is not null && footnote.ReferenceId.Length > 0
            ? FindBacklink(noteTags, noteText, note, footnote.ReferenceId)
            : null;
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

        // Block tags separate words (the paragraphs of the note do not run together), inline ones do not.
        string text = WebUtility.HtmlDecode(Tag.Replace(BlockTag.Replace(content, " "), string.Empty));
        text = Regex.Replace(text, "\\s+", " ", RegexOptions.CultureInvariant).Trim();
        return new ConversionItem(footnote, marker, number, references <= 1, warnings, others, text)
        {
            ReferenceRange = anchor is null ? null : (anchor.OuterStart, anchor.OuterEnd),
        };
    }

    /// <summary>
    /// Converts the footnotes of <paramref name="request"/> to Signet's: the reference and the note get Signet's
    /// template (the text of the note stays), local ones go to the end of their file, global ones to the notes file of
    /// the request; every link to their old ids is rewritten, emptied containers of the old notes go (with the line
    /// right before them), and so do files where nothing but headings is left. Then the footnotes are renumbered and the
    /// result is checked: the texts of the notes, the number of footnotes and the links/footnote pairs check (F7). On a
    /// problem the book is left as it is now; the caller rewinds it (a checkpoint taken before).
    /// </summary>
    public static ConversionResult Convert(Book book, ConversionRequest request, FootnoteOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(book);
        ArgumentNullException.ThrowIfNull(request);
        List<Footnote> selected = request.Items.Where(i => i.CanConvert && !i.Footnote.IsSignet).Select(i => i.Footnote).ToList();
        if (selected.Count == 0)
        {
            return new ConversionResult(0, Array.Empty<string>(), Array.Empty<ConversionProblem>());
        }

        int countBefore = FootnoteScanner.Scan(book).Count;
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
        List<(string TempId, string Expected)> converted = new();
        foreach (Footnote footnote in selected)
        {
            converted.Add(ConvertOne(footnote, footnote.Placement == FootnotePlacement.Local ? footnote.ReferenceFile : target!,
                groupOf, request.KeepClasses, epub3, linked, moves, touched));
        }

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
        foreach (HtmlResource file in touched.Where(f => !ReferenceEquals(f, target) && book.GetHtmlResources().Contains(f)))
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
        List<int> indexes = converted.Select(c => beforeRenumber.FindIndex(f => f.NoteId == c.TempId)).ToList();
        Renumber(book, options);
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

        List<ConversionProblem> problems = Check(book, converted, indexes, countBefore, checksBefore);
        return new ConversionResult(selected.Count, removed, problems);
    }

    // Converts one footnote; returns its temporary note id and the text its note must keep.
    private static (string TempId, string Expected) ConvertOne(
        Footnote footnote, HtmlResource target, Dictionary<string, int> groupOf, bool keepClasses, bool epub3,
        HashSet<(string BookPath, string Id)> linked, Dictionary<(string BookPath, string Id), (string BookPath, string Id)> moves,
        HashSet<HtmlResource> touched)
    {
        bool local = footnote.Placement == FootnotePlacement.Local;
        string noteId = "fn-new-" + Ulid.NewUlid().ToString().ToLowerInvariant();
        string referenceId = noteId + "-ref";
        HtmlResource referenceFile = footnote.ReferenceFile;

        // The reference, with the wrappers that held only it and the empty element its link back led to.
        string text = referenceFile.GetText();
        TagLister tags = new(text);
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

        // The note: its text without the link back, in Signet's template.
        HtmlResource noteFile = footnote.NoteFile;
        text = noteFile.GetText();
        tags = new TagLister(text);
        Element note = FindElementById(tags, text, footnote.NoteId)!;
        Anchor? backlink = returnId is null ? null : FindBacklink(tags, text, note, returnId);
        (string content, _) = CutContent(tags, text, note, backlink);
        List<string> noteClasses = keepClasses ? Classes(text, tags.At(note.OpenIndex)).ToList() : new List<string>();
        string tag = epub3 ? "aside" : "div";
        string open = "<" + tag + " class=\"" + JoinClasses(NoteClass, noteClasses) + "\""
            + (!epub3 ? string.Empty : local ? " epub:type=\"footnote\"" : " epub:type=\"endnote\"")
            + " id=\"" + noteId + "\">";
        string newBacklink = "<a href=\"" + Encode(Href(target.BookPath, referenceFile.BookPath, referenceId)) + "\">0.</a>";
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
        if (returnId is not null)
        {
            moves[(referenceFile.BookPath, returnId)] = (referenceFile.BookPath, referenceId);
        }

        return (noteId, Compact(PlainText(content)));
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
        TagLister tags = new(content);
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
        TagLister tags = new(text);
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
        TagLister tags = new(text);
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
            Element? note = FindElementById(new TagLister(text), text, footnote.NoteId);
            if (note is null)
            {
                continue;
            }

            string markup = Unindent(text, note);
            (int start, int end) = WholeLines(text, note.Start, note.End);
            from.SetText(text.Remove(start, end - start));
            InsertNote(to, markup, FootnotePlacement.Global, book.IsEpub3);
            string referenceText = footnote.ReferenceFile.GetText();
            if (FindAnchor(new TagLister(referenceText), referenceText, footnote.ReferenceHref, footnote.ReferenceOccurrence) is { Href: { } href })
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
            TagLister tags = new(text);
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
            TagLister tags = new(text);
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
        TagLister tags = new(text);
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
        TagLister tags = new(text);
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
        return close >= 0 && MarkerText.IsMatch(PlainText(text[(tag.Pos + tag.Len)..tags.At(close).Pos]));
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
        Book book, List<(string TempId, string Expected)> converted, List<int> indexes, int countBefore,
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
            if (indexes[i] < 0 || indexes[i] >= after.Count)
            {
                problems.Add(new ConversionProblem(ConversionProblemKind.NotSignet, string.Empty, converted[i].TempId));
                continue;
            }

            Footnote footnote = after[indexes[i]];
            if (!footnote.IsSignet)
            {
                problems.Add(new ConversionProblem(ConversionProblemKind.NotSignet, footnote.NoteFile.BookPath, footnote.NoteId));
                continue;
            }

            string text = footnote.NoteFile.GetText();
            TagLister tags = new(text);
            if (FindElementById(tags, text, footnote.NoteId) is not { } note
                || Compact(PlainText(CutContent(tags, text, note, FindBacklink(tags, text, note, footnote.ReferenceId)).Content)) != converted[i].Expected)
            {
                problems.Add(new ConversionProblem(ConversionProblemKind.TextChanged, footnote.NoteFile.BookPath, footnote.NoteId));
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
