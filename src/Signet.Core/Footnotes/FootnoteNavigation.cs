using System;
using System.Collections.Generic;
using System.Linq;

namespace Signet.Core.Footnotes;

/// <summary>What a link is in terms of footnotes.</summary>
public enum FootnoteLinkKind
{
    /// <summary>Not a footnote link: an ordinary link, or one to an element that is not a note or a reference.</summary>
    None,

    /// <summary>A link to a note — a footnote reference, also when it stands in another note's text.</summary>
    Reference,

    /// <summary>A note's link back to its reference.</summary>
    Backlink,
}

/// <summary>
/// Tells footnote links from other links by the footnotes <see cref="FootnoteScanner"/> found — the same notion of a
/// footnote the footnote editor and the validator use, rather than the look of the link.
/// </summary>
public static class FootnoteNavigation
{
    /// <summary>
    /// What a link in the file <paramref name="fromBookPath"/> that leads to
    /// <paramref name="targetBookPath"/>#<paramref name="fragment"/> is.
    /// </summary>
    /// <param name="footnotes">The footnotes of the book (<see cref="FootnoteScanner.Scan"/>).</param>
    /// <param name="fromBookPath">The book path of the file holding the link.</param>
    /// <param name="targetBookPath">The book path of the file the link leads to.</param>
    /// <param name="fragment">The id the link leads to (decoded, without <c>#</c>); a link without one is no footnote link.</param>
    public static FootnoteLinkKind Classify(
        IEnumerable<Footnote> footnotes, string fromBookPath, string targetBookPath, string fragment)
    {
        ArgumentNullException.ThrowIfNull(footnotes);
        ArgumentNullException.ThrowIfNull(fromBookPath);
        ArgumentNullException.ThrowIfNull(targetBookPath);
        ArgumentNullException.ThrowIfNull(fragment);
        if (fragment.Length == 0)
        {
            return FootnoteLinkKind.None;
        }

        List<Footnote> all = footnotes as List<Footnote> ?? footnotes.ToList();
        if (all.Exists(f => Is(f.ReferenceFile.BookPath, fromBookPath)
            && Is(f.NoteFile.BookPath, targetBookPath) && Is(f.NoteId, fragment)))
        {
            return FootnoteLinkKind.Reference;
        }

        return all.Exists(f => Is(f.NoteFile.BookPath, fromBookPath)
            && Is(f.ReferenceFile.BookPath, targetBookPath) && f.ReferenceId.Length > 0 && Is(f.ReferenceId, fragment))
            ? FootnoteLinkKind.Backlink
            : FootnoteLinkKind.None;
    }

    private static bool Is(string a, string b) => string.Equals(a, b, StringComparison.Ordinal);
}
