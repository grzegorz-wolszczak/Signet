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
        Dictionary<string, (HtmlResource Html, IDocument Document)> documents = new(StringComparer.Ordinal);
        HtmlResource? nav = book.GetNavResource();
        foreach (HtmlResource html in book.GetHtmlResources())
        {
            IDocument document = html.GetDocument();
            documents[html.BookPath] = (html, document);

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
        foreach ((HtmlResource html, IDocument document) in documents.Values)
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
        foreach ((HtmlResource html, IElement note, bool strong) in notes)
        {
            CheckNote(html, note, strong, linksTo, linksInside, results);
        }

        return results;
    }

    private static void CheckNote(
        HtmlResource html,
        IElement note,
        bool strong,
        Dictionary<IElement, List<Link>> linksTo,
        Dictionary<IElement, List<Link>> linksInside,
        List<ValidationResult> results)
    {
        string id = note.GetAttribute("id") ?? string.Empty;
        string text = html.GetText();
        int line = id.Length > 0 ? LineOfId(text, id) : -1;
        List<Link> references = linksTo.GetValueOrDefault(note, new List<Link>())
            .Where(l => !Inside(l.Anchor, note))
            .ToList();

        if (references.Count == 0)
        {
            Add(results, html, line, "Validation_FootnoteOrphan", id.Length > 0 ? id : note.LocalName);
            return;
        }

        List<Link> backlinks = BacklinksOf(note, linksInside).ToList();
        if (backlinks.Count == 0 && strong)
        {
            Add(results, html, line, "Validation_FootnoteNoBacklink", id, Describe(references[0]));
        }

        foreach (Link backlink in backlinks.Where(b => !references.Any(r => Points(b, r.Anchor))))
        {
            Add(results, html, line, "Validation_FootnoteBacklinkMismatch", id, backlink.Href);
        }

        if (references.Count > 1 && strong)
        {
            Add(results, html, line, "Validation_FootnoteManyRefs", id, references.Count);
        }
    }

    // The links inside the note that lead out of it (to the reference in the text).
    private static IEnumerable<Link> BacklinksOf(IElement note, Dictionary<IElement, List<Link>> linksInside) =>
        linksInside.GetValueOrDefault(note, new List<Link>()).Where(l => l.Target is not null && !Inside(l.Target, note));

    // Whether the link leads to the anchor: to the anchor itself, to an element around it (e.g. <sup id>) or inside it.
    private static bool Points(Link link, IElement anchor) =>
        link.Target is { } target && (ReferenceEquals(target, anchor) || target.Contains(anchor) || anchor.Contains(target));

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
        Link link, Dictionary<string, (HtmlResource Html, IDocument Document)> documents)
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
        return documents.TryGetValue(bookPath, out (HtmlResource Html, IDocument Document) target)
            ? (target.Html, target.Document.GetElementById(Uri.UnescapeDataString(href[(hash + 1)..])))
            : (null, null);
    }

    private static string Describe(Link reference)
    {
        string id = reference.Anchor.GetAttribute("id") ?? reference.Anchor.ParentElement?.GetAttribute("id") ?? string.Empty;
        return id.Length > 0 ? reference.Owner.Filename + "#" + id : reference.Owner.Filename;
    }

    private static int LineOfId(string text, string id)
    {
        Match match = Regex.Match(text, @"\bid\s*=\s*[""']" + Regex.Escape(id) + @"[""']", RegexOptions.CultureInvariant);
        if (!match.Success)
        {
            return -1;
        }

        int line = 1;
        for (int i = 0; i < match.Index; i++)
        {
            if (text[i] == '\n')
            {
                line++;
            }
        }

        return line;
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
