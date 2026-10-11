using System;
using System.Collections.Generic;
using System.Linq;
using AngleSharp.Dom;
using Signet.Core.BookManipulation;
using Signet.Core.Resources;

namespace Signet.Core.Footnotes;

/// <summary>
/// Finds the footnotes of a book: pairs of a reference in the text and the note it leads to, recognized by structure
/// rather than look (the same rules as <see cref="FootnoteValidator"/>).
/// </summary>
/// <remarks>
/// A note is an element marked as one (<c>epub:type</c> <c>footnote</c>/<c>endnote</c>/<c>rearnote</c>/<c>note</c>,
/// <c>role="doc-footnote"</c>/<c>"doc-endnote"</c>), the target of a link marked as a note reference
/// (<c>epub:type="noteref"</c>, <c>role="doc-noteref"</c>), or a block element (<c>aside</c>, <c>div</c>, <c>p</c>,
/// <c>li</c>, <c>section</c>, <c>dd</c>) that holds a link back to the link pointing at it. The link back returns to the
/// link when it leads to the link, an element around it or inside it, or an empty element right before it
/// (<see cref="FootnoteLinks.LeadsTo"/>). The reference of a note is the link its link back returns to, otherwise the
/// first link to it; a note no link leads to is not a footnote here. Links of the nav document are never references.
/// </remarks>
public static class FootnoteScanner
{
    private static readonly string[] NoteTypes = { "footnote", "endnote", "rearnote", "note" };
    private static readonly string[] NoteRoles = { "doc-footnote", "doc-endnote" };
    private static readonly string[] BlockNames = { "aside", "div", "p", "li", "section", "dd" };
    private static readonly string[] InlineNames = { "a", "sup", "span" };

    /// <summary>The footnotes of the book in reading order of their references.</summary>
    public static IReadOnlyList<Footnote> Scan(Book book)
    {
        ArgumentNullException.ThrowIfNull(book);

        HtmlResource? nav = book.GetNavResource();
        List<HtmlResource> files = book.GetHtmlResources().ToList();
        Dictionary<string, (HtmlResource Html, IDocument Document, int Spine)> documents = new(StringComparer.Ordinal);
        Dictionary<IElement, int> order = new();
        List<Link> links = new();
        for (int spine = 0; spine < files.Count; spine++)
        {
            HtmlResource html = files[spine];
            IDocument document = html.GetDocument();
            documents[html.BookPath] = (html, document, spine);
            int index = 0;
            foreach (IElement element in document.All)
            {
                order[element] = index++;
            }

            if (ReferenceEquals(html, nav))
            {
                continue;
            }

            Dictionary<string, int> occurrences = new(StringComparer.Ordinal);
            foreach (IElement anchor in document.QuerySelectorAll("a[href]"))
            {
                string href = anchor.GetAttribute("href")!;
                int occurrence = occurrences.GetValueOrDefault(href);
                occurrences[href] = occurrence + 1;
                links.Add(new Link(html, spine, anchor, href, occurrence));
            }
        }

        foreach (Link link in links)
        {
            (link.TargetHtml, link.Target) = Resolve(link, documents);
        }

        Dictionary<IElement, List<Link>> linksTo = new();
        foreach (Link link in links.Where(l => l.Target is not null && !Inside(l.Anchor, l.Target)))
        {
            if (!linksTo.TryGetValue(link.Target!, out List<Link>? list))
            {
                linksTo[link.Target!] = list = new List<Link>();
            }

            list.Add(link);
        }

        // The links inside each linked-to element (its candidate links back), found through the ancestors of every link.
        Dictionary<IElement, List<Link>> linksInside = new();
        foreach (Link link in links.Where(l => l.Target is not null))
        {
            for (IElement? element = link.Anchor; element is not null; element = element.ParentElement)
            {
                if (linksTo.ContainsKey(element) && !Inside(link.Target!, element))
                {
                    if (!linksInside.TryGetValue(element, out List<Link>? list))
                    {
                        linksInside[element] = list = new List<Link>();
                    }

                    list.Add(link);
                }
            }
        }

        List<Footnote> footnotes = new();
        foreach ((IElement note, List<Link> references) in linksTo)
        {
            // A link or an inline element is a reference, not a note, unless it is marked as a note.
            if (InlineNames.Contains(note.LocalName, StringComparer.OrdinalIgnoreCase) && !IsMarkedNote(note))
            {
                continue;
            }

            HtmlResource noteFile = references[0].TargetHtml!;
            List<Link> backlinks = linksInside.GetValueOrDefault(note, new List<Link>());
            Link? reference = null;
            Link? backlink = null;
            foreach (Link candidate in references)
            {
                backlink = backlinks.FirstOrDefault(b => FootnoteLinks.LeadsTo(b.Target!, candidate.Anchor));
                if (backlink is not null)
                {
                    reference = candidate;
                    break;
                }
            }

            bool isNote = IsMarkedNote(note)
                || references.Any(r => IsNoteRef(r.Anchor))
                || (reference is not null && BlockNames.Contains(note.LocalName, StringComparer.OrdinalIgnoreCase));
            string noteId = note.GetAttribute("id") ?? string.Empty;
            if (!isNote || noteId.Length == 0)
            {
                continue;
            }

            reference ??= references[0];

            // The id the link back returns to (the link's own, or of an element around it or right before it).
            string referenceId = backlink?.Target!.GetAttribute("id") ?? reference.Anchor.GetAttribute("id") ?? string.Empty;
            footnotes.Add(new Footnote(
                reference.Owner, reference.Href, reference.Occurrence, referenceId,
                noteFile, noteId, HasClass(note, FootnoteEditor.NoteClass), reference.Spine, order[reference.Anchor]));
        }

        return footnotes.OrderBy(f => f.SpineIndex).ThenBy(f => f.DocumentIndex).ToList();
    }

    /// <summary>Whether <paramref name="element"/> has the class <paramref name="name"/>.</summary>
    internal static bool HasClass(IElement element, string name) =>
        Tokens(element.GetAttribute("class")).Contains(name, StringComparer.Ordinal);

    private static bool IsMarkedNote(IElement element) =>
        Tokens(element.GetAttribute("epub:type")).Any(t => NoteTypes.Contains(t, StringComparer.OrdinalIgnoreCase))
        || Tokens(element.GetAttribute("role")).Any(t => NoteRoles.Contains(t, StringComparer.OrdinalIgnoreCase));

    private static bool IsNoteRef(IElement anchor) =>
        Tokens(anchor.GetAttribute("epub:type")).Contains("noteref", StringComparer.OrdinalIgnoreCase)
        || Tokens(anchor.GetAttribute("role")).Contains("doc-noteref", StringComparer.OrdinalIgnoreCase);

    private static string[] Tokens(string? value) =>
        value?.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries) ?? Array.Empty<string>();

    private static bool Inside(IElement element, IElement? container) =>
        container is not null && (ReferenceEquals(element, container) || container.Contains(element));

    private static (HtmlResource? Html, IElement? Element) Resolve(
        Link link, Dictionary<string, (HtmlResource Html, IDocument Document, int Spine)> documents)
    {
        (string path, string fragment) = FootnoteEditor.SplitHref(link.Href);
        if (fragment.Length == 0 || FootnoteEditor.IsExternal(path))
        {
            return (null, null);
        }

        string bookPath = path.Length == 0
            ? link.Owner.BookPath
            : Core.BookPath.BuildBookPath(Utility.UrlDecodePath(path), link.Owner.Folder);
        return documents.TryGetValue(bookPath, out (HtmlResource Html, IDocument Document, int Spine) target)
            ? (target.Html, target.Document.GetElementById(fragment))
            : (null, null);
    }

    private sealed class Link
    {
        public Link(HtmlResource owner, int spine, IElement anchor, string href, int occurrence)
        {
            Owner = owner;
            Spine = spine;
            Anchor = anchor;
            Href = href;
            Occurrence = occurrence;
        }

        public HtmlResource Owner { get; }

        public int Spine { get; }

        public IElement Anchor { get; }

        public string Href { get; }

        public int Occurrence { get; }

        public IElement? Target { get; set; }

        public HtmlResource? TargetHtml { get; set; }
    }
}
