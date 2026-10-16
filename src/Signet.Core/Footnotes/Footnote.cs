using Signet.Core.Resources;

namespace Signet.Core.Footnotes;

/// <summary>Where the text of a footnote lives.</summary>
public enum FootnotePlacement
{
    /// <summary>At the end of the file that references it (a footnote).</summary>
    Local,

    /// <summary>In the book's notes file (an endnote), <see cref="FootnoteEditor.GlobalFileName"/>.</summary>
    Global,
}

/// <summary>
/// The letters that tell apart the references of a note that several references lead to: the references read
/// <c>[4a]</c>, <c>[4b]</c> and the links back of the note <c>4a, 4b</c> (Preferences → Footnotes).
/// </summary>
public enum FootnoteLetterStyle
{
    /// <summary>a, b, c… z, aa, ab…</summary>
    LowerLatin,

    /// <summary>A, B, C… Z, AA, AB…</summary>
    UpperLatin,

    /// <summary>i, ii, iii, iv…</summary>
    LowerRoman,
}

/// <summary>Which part of a footnote a position is in.</summary>
public enum FootnoteSide
{
    /// <summary>The reference in the text (the link, with a <c>&lt;sup&gt;</c> around it).</summary>
    Reference,

    /// <summary>The note (the element with the text of the footnote).</summary>
    Note,
}

/// <summary>
/// A footnote of the book: a reference in the text and the note it leads to (see <see cref="FootnoteScanner"/>).
/// The reference is identified by its <c>href</c> and the number of earlier links with the same <c>href</c> in its
/// file (a reference need not have an id), the note by its id. A note that several references lead to (a shared
/// note) is a footnote per reference: they have the same note, <see cref="ReferenceCount"/> and the number, and each
/// its own <see cref="ReferenceIndex"/> (the letter of Signet's marker, <c>[4a]</c>, <c>[4b]</c>).
/// </summary>
public sealed class Footnote
{
    internal Footnote(
        HtmlResource referenceFile, string referenceHref, int referenceOccurrence, string referenceId,
        HtmlResource noteFile, string noteId, bool isSignet, int spineIndex, int documentIndex,
        int referenceIndex = 0, int referenceCount = 1, FootnotePlacement? placement = null)
    {
        ReferenceFile = referenceFile;
        ReferenceHref = referenceHref;
        ReferenceOccurrence = referenceOccurrence;
        ReferenceId = referenceId;
        NoteFile = noteFile;
        NoteId = noteId;
        IsSignet = isSignet;
        SpineIndex = spineIndex;
        DocumentIndex = documentIndex;
        ReferenceIndex = referenceIndex;
        ReferenceCount = referenceCount;
        Placement = placement ?? (ReferenceEquals(referenceFile, noteFile) ? FootnotePlacement.Local : FootnotePlacement.Global);
    }

    /// <summary>The file with the reference.</summary>
    public HtmlResource ReferenceFile { get; }

    /// <summary>The <c>href</c> of the reference link (decoded).</summary>
    public string ReferenceHref { get; }

    /// <summary>How many earlier links of <see cref="ReferenceFile"/> have the same <c>href</c>.</summary>
    public int ReferenceOccurrence { get; }

    /// <summary>
    /// The id the note's link back leads to: of the reference link, of an element around it or of an empty element
    /// right before it (<see cref="FootnoteLinks.LeadsTo"/>); without a link back the reference link's id; or <c>""</c>.
    /// </summary>
    public string ReferenceId { get; }

    /// <summary>The file with the note.</summary>
    public HtmlResource NoteFile { get; }

    /// <summary>The id of the note.</summary>
    public string NoteId { get; }

    /// <summary>Whether Signet made the footnote (the note has the <c>signet-fn</c> class).</summary>
    public bool IsSignet { get; }

    /// <summary>Local (the note is in the file of its references, all of them) or global.</summary>
    public FootnotePlacement Placement { get; }

    /// <summary>The place of the reference among the references of its note, in reading order (from 0).</summary>
    public int ReferenceIndex { get; }

    /// <summary>How many references lead to the note.</summary>
    public int ReferenceCount { get; }

    /// <summary>Whether several references lead to the note.</summary>
    public bool IsShared => ReferenceCount > 1;

    // The reading order of the reference: its file in the spine, then its place in the file.
    internal int SpineIndex { get; }

    internal int DocumentIndex { get; }
}
