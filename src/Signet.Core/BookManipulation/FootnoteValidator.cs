using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using AngleSharp.Dom;
using Signet.Core.Localization;
using Signet.Core.Resources;

namespace Signet.Core.BookManipulation;

/// <summary>
/// Checks that footnotes and their references point at each other. Called from
/// <see cref="BookValidator.ValidateCurrentBook"/>.
/// </summary>
/// <remarks>
/// <para>
/// A footnote is a pair of anchors: a reference in the text (<c>&lt;a href="#fn-1" id="fn-1-ref"&gt;</c>) and the
/// note, an element with an id that holds a link back to the reference. The note is recognized by its structure, not
/// its look: an element marked as a note (<c>epub:type</c> <c>footnote</c>/<c>endnote</c>/<c>rearnote</c>/<c>note</c>
/// or <c>role="doc-footnote"</c>/<c>"doc-endnote"</c>), the target of a link marked as a note reference
/// (<c>epub:type="noteref"</c>, <c>role="doc-noteref"</c>), or the target of a link that holds a link back to it.
/// </para>
/// <para>
/// Every check is a direct lookup (link → target, target → its links back), never a walk along a chain of links, so
/// links pointing at each other in a circle (A → B → C → A) cannot make it loop. Dead links and missing anchors are
/// <see cref="LinkIntegrityValidator"/>'s job and are not reported again here.
/// </para>
/// </remarks>
public static class FootnoteValidator
{
    private static readonly string[] NoteTypes = { "footnote", "endnote", "rearnote", "note" };
    private static readonly string[] NoteRoles = { "doc-footnote", "doc-endnote" };

    /// <summary>Runs the footnote checks for the whole book.</summary>
    public static IReadOnlyList<ValidationResult> Validate(Book book)
    {
        ArgumentNullException.ThrowIfNull(book);

        List<Link> links = new();
        Dictionary<string, (HtmlResource Html, IDocument Document, Dictionary<string, IElement> ById)> documents = new(StringComparer.Ordinal);
        HtmlResource? nav = book.GetNavResource();
        foreach (HtmlResource html in book.GetHtmlResources())
        {
            IDocument document = html.GetDocument();
            documents[html.BookPath] = (html, document, Footnotes.ElementIds.Of(document));

            // The links of the nav document are a table of contents, never note references.
            if (ReferenceEquals(html, nav))
            {
                continue;
            }

            foreach (IElement anchor in document.QuerySelectorAll("a[href]"))
            {
                links.Add(new Link(html, anchor, anchor.GetAttribute("href")!));
            }
        }

        foreach (Link link in links)
        {
            (link.TargetHtml, link.Target) = Resolve(link, documents);
        }

        // Links grouped by the element they point at.
        Dictionary<IElement, List<Link>> linksTo = new();
        foreach (Link link in links.Where(l => l.Target is not null))
        {
            if (!linksTo.TryGetValue(link.Target!, out List<Link>? list))
            {
                linksTo[link.Target!] = list = new List<Link>();
            }

            list.Add(link);
        }

        // The links inside each linked-to element (its candidate links back), found through the ancestors of every link.
        Dictionary<IElement, List<Link>> linksInside = new();
        foreach (Link link in links)
        {
            for (IElement? element = link.Anchor; element is not null; element = element.ParentElement)
            {
                if (linksTo.ContainsKey(element))
                {
                    if (!linksInside.TryGetValue(element, out List<Link>? list))
                    {
                        linksInside[element] = list = new List<Link>();
                    }

                    list.Add(link);
                }
            }
        }

        // The notes: marked ones and targets of note references (Strong), then targets that only link back to their
        // link. The last kind is often no note at all (a chapter heading linking back to an HTML table of contents),
        // so only its links back are checked.
        List<(HtmlResource Html, IElement Note, bool Strong)> notes = new();
        HashSet<IElement> seen = new();
        foreach ((HtmlResource html, IDocument document, Dictionary<string, IElement> _) in documents.Values)
        {
            foreach (IElement element in document.All)
            {
                if (IsMarkedNote(element) && seen.Add(element))
                {
                    notes.Add((html, element, true));
                }
            }
        }

        foreach (Link link in links.Where(l => l.Target is not null && IsNoteRef(l.Anchor) && !Inside(l.Anchor, l.Target)))
        {
            if (seen.Add(link.Target!))
            {
                notes.Add((link.TargetHtml!, link.Target!, true));
            }
        }

        foreach (Link link in links.Where(l => l.Target is not null && !Inside(l.Anchor, l.Target)))
        {
            IElement target = link.Target!;
            if (BacklinksOf(target, linksInside).Any(b => Points(b, link.Anchor)) && seen.Add(target))
            {
                notes.Add((link.TargetHtml!, target, false));
            }
        }

        List<ValidationResult> results = new();
        Dictionary<HtmlResource, Dictionary<string, int>> linesOf = new(ReferenceEqualityComparer.Instance);
        foreach ((HtmlResource html, IElement note, bool strong) in notes)
        {
            if (!linesOf.TryGetValue(html, out Dictionary<string, int>? lines))
            {
                linesOf[html] = lines = LinesOfIds(html.GetText());
            }

            CheckNote(html, note, strong, linksTo, linksInside, lines, results);
        }

        return results;
    }

    private static void CheckNote(
        HtmlResource html,
        IElement note,
        bool strong,
        Dictionary<IElement, List<Link>> linksTo,
        Dictionary<IElement, List<Link>> linksInside,
        Dictionary<string, int> lines,
        List<ValidationResult> results)
    {
        string id = note.GetAttribute("id") ?? string.Empty;
        int line = id.Length > 0 ? lines.GetValueOrDefault(id, -1) : -1;
        List<Link> references = linksTo.GetValueOrDefault(note, new List<Link>())
            .Where(l => !Inside(l.Anchor, note))
            .ToList();

        if (references.Count == 0)
        {
            Add(results, html, line, "Validation_FootnoteOrphan", id.Length > 0 ? id : note.LocalName);
            return;
        }

        // A note has other links than the one back (a reference to a verse, "see also…"): only the links at the start of the note —
        // "1." or "4a, 4b." — and the links that really lead back count as links back.
        HashSet<IElement> leading = LeadingLinks(note);
        List<Link> backlinks = BacklinksOf(note, linksInside)
            .Where(b => leading.Contains(b.Anchor) || references.Any(r => Points(b, r.Anchor)))
            .ToList();
        if (backlinks.Count == 0 && strong)
        {
            Add(results, html, line, "Validation_FootnoteNoBacklink", id, Describe(references[0]));
        }

        foreach (Link backlink in backlinks.Where(b => !references.Any(r => Points(b, r.Anchor))))
        {
            Add(results, html, line, "Validation_FootnoteBacklinkMismatch", id, backlink.Href);
        }

        // Only links that look like references count here: a plain link to a note ("see note 2") is not one. Several
        // references are fine when the note has a link back to each of them (Signet's shared note: "4a, 4b.").
        List<Link> markers = references.Where(l => Footnotes.FootnoteLinks.IsReferenceLike(l.Anchor)).ToList();
        if (markers.Count > 1 && strong && markers.Any(m => !backlinks.Any(b => Points(b, m.Anchor))))
        {
            Add(results, html, line, "Validation_FootnoteManyRefs", id, markers.Count);
        }
    }

    // The links the note starts with: the first thing in it is a link, and each next one follows it after a comma or a semicolon
    // ("4a, 4b."); the first text of another kind (even a space) ends the run, so "1. <link to a verse> text" has one.
    private static HashSet<IElement> LeadingLinks(IElement note)
    {
        HashSet<IElement> result = new();
        bool Walk(INode node)
        {
            foreach (INode child in node.ChildNodes)
            {
                if (child is IElement element)
                {
                    if (element.LocalName.Equals("a", StringComparison.OrdinalIgnoreCase) && element.HasAttribute("href"))
                    {
                        result.Add(element);
                    }
                    else if (!Walk(element))
                    {
                        return false;
                    }
                }
                else if (child is IText text && text.Data.Length > 0)
                {
                    // Before the first link only white space (the indentation of the markup); between two links only a comma or a
                    // semicolon: a space alone, or any other text, ends the run.
                    string data = text.Data.Trim();
                    if (data.Length == 0 ? result.Count > 0 : result.Count == 0 || data.Any(c => c is not (',' or ';')))
                    {
                        return false;
                    }
                }
            }

            return true;
        }

        Walk(note);
        return result;
    }

    // The links inside the note that lead out of it (to the reference in the text).
    private static IEnumerable<Link> BacklinksOf(IElement note, Dictionary<IElement, List<Link>> linksInside) =>
        linksInside.GetValueOrDefault(note, new List<Link>()).Where(l => l.Target is not null && !Inside(l.Target, note));

    // Whether the link leads back to the anchor (the same rule as FootnoteScanner, Footnotes.FootnoteLinks.LeadsTo).
    private static bool Points(Link link, IElement anchor) =>
        link.Target is { } target && Footnotes.FootnoteLinks.LeadsTo(target, anchor);

    private static bool Inside(IElement element, IElement container) =>
        ReferenceEquals(element, container) || container.Contains(element);

    private static bool IsMarkedNote(IElement element) =>
        Tokens(element.GetAttribute("epub:type")).Any(t => NoteTypes.Contains(t, StringComparer.OrdinalIgnoreCase))
        || Tokens(element.GetAttribute("role")).Any(t => NoteRoles.Contains(t, StringComparer.OrdinalIgnoreCase));

    private static bool IsNoteRef(IElement anchor) =>
        Tokens(anchor.GetAttribute("epub:type")).Contains("noteref", StringComparer.OrdinalIgnoreCase)
        || Tokens(anchor.GetAttribute("role")).Contains("doc-noteref", StringComparer.OrdinalIgnoreCase);

    private static string[] Tokens(string? value) =>
        value?.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries) ?? Array.Empty<string>();

    private static (HtmlResource? Html, IElement? Element) Resolve(
        Link link, Dictionary<string, (HtmlResource Html, IDocument Document, Dictionary<string, IElement> ById)> documents)
    {
        string href = link.Href.Trim();
        int hash = href.IndexOf('#', StringComparison.Ordinal);
        if (hash < 0 || hash == href.Length - 1)
        {
            return (null, null);
        }

        string path = href[..hash];
        int colon = path.IndexOf(':', StringComparison.Ordinal);
        int slash = path.IndexOf('/', StringComparison.Ordinal);
        if (colon >= 0 && (slash < 0 || colon < slash))
        {
            return (null, null); // external
        }

        string bookPath = path.Length == 0
            ? link.Owner.BookPath
            : Core.BookPath.BuildBookPath(Utility.UrlDecodePath(path), link.Owner.Folder);
        // The id index of the file, not GetElementById, which walks the document: see Footnotes.ElementIds.
        return documents.TryGetValue(bookPath, out (HtmlResource Html, IDocument Document, Dictionary<string, IElement> ById) target)
            ? (target.Html, target.ById.GetValueOrDefault(Uri.UnescapeDataString(href[(hash + 1)..])))
            : (null, null);
    }

    private static string Describe(Link reference)
    {
        string id = reference.Anchor.GetAttribute("id") ?? reference.Anchor.ParentElement?.GetAttribute("id") ?? string.Empty;
        return id.Length > 0 ? reference.Owner.Filename + "#" + id : reference.Owner.Filename;
    }

    // Every id written in the file, with the line its first mention stands on. This used to be one search of the whole
    // text per note, plus counting the newlines in front of the hit character by character, so a file holding thousands
    // of notes paid for it in the square of their number -- it was the most expensive step of validating such a book.
    // One pass answers it for every id at once. The pattern is the one that used to be built per id, with the id read
    // out of the match instead of written into it, so it accepts the same (a value between mismatched quotes included)
    // and finds the same first mention.
    private static Dictionary<string, int> LinesOfIds(string text)
    {
        Dictionary<string, int> lines = new(StringComparer.Ordinal);
        int line = 1;
        int at = 0;
        foreach (Match match in Regex.Matches(text, @"\bid\s*=\s*[""']([^""']*)[""']", RegexOptions.CultureInvariant))
        {
            while (at < match.Index)
            {
                if (text[at] == '\n')
                {
                    line++;
                }

                at++;
            }

            lines.TryAdd(match.Groups[1].Value, line);
        }

        return lines;
    }

    private static void Add(List<ValidationResult> results, HtmlResource html, int line, string code, params object[] args) =>
        results.Add(new ValidationResult(ValidationSeverity.Warning, html.BookPath, line, -1, CoreStrings.Format(code, args), code));

    private sealed class Link
    {
        public Link(HtmlResource owner, IElement anchor, string href)
        {
            Owner = owner;
            Anchor = anchor;
            Href = href;
        }

        public HtmlResource Owner { get; }

        public IElement Anchor { get; }

        public string Href { get; }

        public IElement? Target { get; set; }

        public HtmlResource? TargetHtml { get; set; }
    }
}
