using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using AngleSharp;
using AngleSharp.Dom;
using AngleSharp.Html.Dom;
using AngleSharp.Html.Parser;
using AngleSharp.Xhtml;
using CoreBookPath = Signet.Core.BookPath;

namespace Signet.Core.BookManipulation;

/// <summary>
/// Common set of read operations on XHTML documents, built on AngleSharp.
/// </summary>
/// <remarks>
/// <para>
/// Parsing: the AngleSharp HTML5 parser (<see cref="HtmlParser"/>, the same one used by
/// <see cref="Resources.HtmlResource"/> — tolerant of malformed XHTML), with source position
/// tracking enabled. Serialization: <see cref="XhtmlMarkupFormatter"/> (closes empty elements, the
/// output parses as XML). <b>AngleSharp's HTML mode is not XML-namespace aware</b> —
/// <c>epub:type</c> is a literal attribute name (not <c>{…ops}type</c>); this is sufficient for the
/// operations of this module and is preserved on round-trip. Strict well-formedness checking is
/// handled by <see cref="WellFormedChecker"/>.
/// </para>
/// <para>
/// All methods have a variant taking a raw <c>string</c> (parses from scratch) and a variant
/// taking an already parsed <see cref="IHtmlDocument"/> (e.g. the DOM cache of <c>HtmlResource</c>).
/// Offset mapping (<see cref="NodeFromOffset(IHtmlDocument, int)"/> / <see cref="OffsetFromNode"/>) works at
/// element granularity based on AngleSharp source positions; it does not resolve down to
/// text nodes.
/// </para>
/// </remarks>
public static class XhtmlDoc
{
    private static readonly char[] Whitespace = { ' ', '\t', '\r', '\n', '\f' };

    private static readonly Regex WhitespaceRun = new(@"\s+", RegexOptions.Compiled);

    private static readonly string[] DirectionValues = { "ltr", "rtl", "auto" };

    // =====================================================================
    //  Parsing / serialization
    // =====================================================================

    /// <summary>
    /// Parses XHTML source into an AngleSharp DOM document (HTML5 parser, source position tracking
    /// enabled — required by <see cref="NodeFromOffset(IHtmlDocument, int)"/> / <see cref="OffsetFromNode"/>).
    /// </summary>
    public static IHtmlDocument Parse(string source)
    {
        ArgumentNullException.ThrowIfNull(source);
        HtmlParser parser = new(new HtmlParserOptions { IsKeepingSourceReferences = true });
        return parser.ParseDocument(source);
    }

    /// <summary>
    /// Serializes a node (or the whole document) back to XHTML with minimal changes
    /// (<see cref="XhtmlMarkupFormatter"/> — empty elements closed, attributes with a colon
    /// kept verbatim).
    /// </summary>
    public static string Serialize(IMarkupFormattable node)
    {
        ArgumentNullException.ThrowIfNull(node);
        return node.ToHtml(XhtmlMarkupFormatter.Instance);
    }

    // =====================================================================
    //  Identifiers and references
    // =====================================================================

    /// <summary>
    /// All values usable as a link target: <c>id</c> attributes of any element and
    /// (legacy) <c>name</c> attributes of <c>&lt;a&gt;</c> elements. Document order, no duplicates.
    /// </summary>
    public static IReadOnlyList<string> GetAllDescendantIds(IHtmlDocument document)
    {
        ArgumentNullException.ThrowIfNull(document);
        List<string> ids = new();
        HashSet<string> seen = new(StringComparer.Ordinal);

        foreach (IElement element in document.All)
        {
            string? id = element.GetAttribute("id");
            if (!string.IsNullOrEmpty(id))
            {
                if (seen.Add(id))
                {
                    ids.Add(id);
                }

                continue;
            }

            if (element.LocalName.Equals("a", StringComparison.OrdinalIgnoreCase))
            {
                string? name = element.GetAttribute("name");
                if (!string.IsNullOrEmpty(name) && seen.Add(name))
                {
                    ids.Add(name);
                }
            }
        }

        return ids;
    }

    /// <inheritdoc cref="GetAllDescendantIds(IHtmlDocument)"/>
    public static IReadOnlyList<string> GetAllDescendantIds(string source) => GetAllDescendantIds(Parse(source));

    /// <summary>
    /// Values of <c>id</c> attributes defined in this file (without legacy <c>&lt;a name&gt;</c>) —
    /// for checking the integrity of <c>#frag</c> fragments. Document order, no duplicates.
    /// </summary>
    public static IReadOnlyList<string> GetIdsInFile(IHtmlDocument document)
    {
        ArgumentNullException.ThrowIfNull(document);
        List<string> ids = new();
        HashSet<string> seen = new(StringComparer.Ordinal);

        foreach (IElement element in document.All)
        {
            string? id = element.GetAttribute("id");
            if (!string.IsNullOrEmpty(id) && seen.Add(id))
            {
                ids.Add(id);
            }
        }

        return ids;
    }

    /// <inheritdoc cref="GetIdsInFile(IHtmlDocument)"/>
    public static IReadOnlyList<string> GetIdsInFile(string source) => GetIdsInFile(Parse(source));

    /// <summary>
    /// Raw (encoded) values of all <c>href</c> attributes in the document. Document order,
    /// no duplicates.
    /// </summary>
    public static IReadOnlyList<string> GetAllDescendantHrefs(IHtmlDocument document)
    {
        ArgumentNullException.ThrowIfNull(document);
        List<string> hrefs = new();
        HashSet<string> seen = new(StringComparer.Ordinal);

        foreach (IElement element in document.All)
        {
            string? href = element.GetAttribute("href");
            if (!string.IsNullOrEmpty(href) && seen.Add(href))
            {
                hrefs.Add(href);
            }
        }

        return hrefs;
    }

    /// <inheritdoc cref="GetAllDescendantHrefs(IHtmlDocument)"/>
    public static IReadOnlyList<string> GetAllDescendantHrefs(string source) => GetAllDescendantHrefs(Parse(source));

    /// <summary>
    /// Elements referring to other files: <c>&lt;a&gt;</c>/<c>&lt;link&gt;</c> (<c>href</c>
    /// attribute) and <c>&lt;img&gt;</c>/<c>&lt;script&gt;</c> (<c>src</c> attribute). Returns tuples
    /// (tag name, attribute name, raw value) in document order — used for updating
    /// references on rename and for Find &amp; Replace.
    /// </summary>
    public static IReadOnlyList<LinkElement> GetLinkElements(IHtmlDocument document)
    {
        ArgumentNullException.ThrowIfNull(document);
        List<LinkElement> result = new();

        foreach (IElement element in document.All)
        {
            string attribute = element.LocalName.ToLowerInvariant() switch
            {
                "a" or "link" => "href",
                "img" or "script" => "src",
                _ => string.Empty,
            };

            if (attribute.Length == 0)
            {
                continue;
            }

            string? value = element.GetAttribute(attribute);
            if (!string.IsNullOrEmpty(value))
            {
                result.Add(new LinkElement(element.LocalName.ToLowerInvariant(), attribute, value));
            }
        }

        return result;
    }

    /// <inheritdoc cref="GetLinkElements(IHtmlDocument)"/>
    public static IReadOnlyList<LinkElement> GetLinkElements(string source) => GetLinkElements(Parse(source));

    /// <summary>
    /// Fragment values (the part after <c>#</c>) of all <c>href</c> / <c>src</c> attributes in the
    /// document — the identifiers something refers to. Document order, no
    /// duplicates.
    /// </summary>
    public static IReadOnlyList<string> GetFragmentTargetsInHrefs(IHtmlDocument document)
    {
        ArgumentNullException.ThrowIfNull(document);
        List<string> result = new();
        HashSet<string> seen = new(StringComparer.Ordinal);

        foreach (IElement element in document.All)
        {
            foreach (string attribute in new[] { "href", "src" })
            {
                string? value = element.GetAttribute(attribute);
                int hash = value?.IndexOf('#', StringComparison.Ordinal) ?? -1;
                if (hash < 0 || value is null)
                {
                    continue;
                }

                string fragment = value[(hash + 1)..];
                if (fragment.Length > 0 && seen.Add(fragment))
                {
                    result.Add(fragment);
                }
            }
        }

        return result;
    }

    /// <inheritdoc cref="GetFragmentTargetsInHrefs(IHtmlDocument)"/>
    public static IReadOnlyList<string> GetFragmentTargetsInHrefs(string source) =>
        GetFragmentTargetsInHrefs(Parse(source));

    /// <summary>
    /// Resolves all <em>relative</em> references (<c>href</c> / <c>src</c>) in the document to
    /// bookpaths relative to <paramref name="startFolder"/> (the folder of the XHTML file). Absolute
    /// references (with a scheme), pure fragments (<c>#…</c>) and <c>data:</c> are skipped.
    /// The fragment and query are stripped. Document order, no duplicates.
    /// </summary>
    /// <param name="document">Parsed document.</param>
    /// <param name="startFolder">Bookpath of the folder containing the document (e.g. <c>EPUB/text</c>).</param>
    public static IReadOnlyList<string> ResolveHrefs(IHtmlDocument document, string startFolder)
    {
        ArgumentNullException.ThrowIfNull(document);
        ArgumentNullException.ThrowIfNull(startFolder);

        List<string> result = new();
        HashSet<string> seen = new(StringComparer.Ordinal);

        foreach (IElement element in document.All)
        {
            foreach (string attribute in new[] { "href", "src" })
            {
                string? bookPath = RelativeReferenceToBookPath(element.GetAttribute(attribute), startFolder);
                if (bookPath is not null && seen.Add(bookPath))
                {
                    result.Add(bookPath);
                }
            }
        }

        return result;
    }

    /// <inheritdoc cref="ResolveHrefs(IHtmlDocument, string)"/>
    public static IReadOnlyList<string> ResolveHrefs(string source, string startFolder) =>
        ResolveHrefs(Parse(source), startFolder);

    // =====================================================================
    //  Headings
    // =====================================================================

    /// <summary>
    /// Heading elements <c>h1</c>–<c>h6</c> in document order: tag name, level (1–6),
    /// <c>id</c> (or <c>""</c>) and text (whitespace collapsed and trimmed). A primitive for
    /// TOC generation — <b>without</b> hierarchy and "include in TOC" flags.
    /// </summary>
    public static IReadOnlyList<HeadingElement> GetHeadingElements(IHtmlDocument document)
    {
        ArgumentNullException.ThrowIfNull(document);
        List<HeadingElement> headings = new();

        foreach (IElement element in document.QuerySelectorAll("h1, h2, h3, h4, h5, h6"))
        {
            int level = element.LocalName[1] - '0';
            string id = element.GetAttribute("id") ?? string.Empty;
            string text = WhitespaceRun.Replace(element.TextContent, " ").Trim();
            headings.Add(new HeadingElement(element.LocalName.ToLowerInvariant(), level, id, text));
        }

        return headings;
    }

    /// <inheritdoc cref="GetHeadingElements(IHtmlDocument)"/>
    public static IReadOnlyList<HeadingElement> GetHeadingElements(string source) => GetHeadingElements(Parse(source));

    // =====================================================================
    //  Text direction / body classes
    // =====================================================================

    /// <summary>
    /// Dominant text direction of the document: the <c>dir</c> attribute of <c>&lt;html&gt;</c>, or,
    /// if absent, of <c>&lt;body&gt;</c>. Returns <c>"ltr"</c>, <c>"rtl"</c>, <c>"auto"</c> or <c>""</c>
    /// when not declared.
    /// </summary>
    public static string GetDominantTextDirection(IHtmlDocument document)
    {
        ArgumentNullException.ThrowIfNull(document);

        foreach (string? candidate in new[]
                 {
                     document.QuerySelector("html")?.GetAttribute("dir"),
                     document.QuerySelector("body")?.GetAttribute("dir"),
                 })
        {
            string direction = candidate?.Trim().ToLowerInvariant() ?? string.Empty;
            if (DirectionValues.Contains(direction))
            {
                return direction;
            }
        }

        return string.Empty;
    }

    /// <inheritdoc cref="GetDominantTextDirection(IHtmlDocument)"/>
    public static string GetDominantTextDirection(string source) => GetDominantTextDirection(Parse(source));

    /// <summary>
    /// Classes of the <c>&lt;body&gt;</c> element (the <c>class</c> attribute split on whitespace).
    /// Empty list when there is no <c>&lt;body&gt;</c> or no attribute.
    /// </summary>
    public static IReadOnlyList<string> GetBodyClasses(IHtmlDocument document)
    {
        ArgumentNullException.ThrowIfNull(document);
        string classes = document.QuerySelector("body")?.GetAttribute("class") ?? string.Empty;
        return classes.Split(Whitespace, StringSplitOptions.RemoveEmptyEntries);
    }

    /// <inheritdoc cref="GetBodyClasses(IHtmlDocument)"/>
    public static IReadOnlyList<string> GetBodyClasses(string source) => GetBodyClasses(Parse(source));

    // =====================================================================
    //  Source offset ↔ DOM node mapping
    // =====================================================================

    /// <summary>
    /// 0-based character offset of the start of the element's opening tag in the source the
    /// document was parsed from (<see cref="Parse(string)"/>), or <c>-1</c> when the position is
    /// unavailable (a non-element node, or an element inserted/moved by the parser).
    /// </summary>
    public static int OffsetFromNode(INode node)
    {
        ArgumentNullException.ThrowIfNull(node);
        ISourceReference? reference = (node as IElement)?.SourceReference;
        return reference is null ? -1 : Math.Max(0, reference.Position.Position - 1);
    }

    /// <summary>
    /// The deepest element whose opening tag starts at or before <paramref name="offset"/> and which
    /// (by source order of its siblings) covers that position. Best-effort at element
    /// granularity — see the remarks on the class. Returns <c>null</c> when the offset is
    /// outside the root element.
    /// </summary>
    public static INode? NodeFromOffset(IHtmlDocument document, int offset)
    {
        ArgumentNullException.ThrowIfNull(document);
        IElement? root = document.DocumentElement;
        if (root is null || offset < 0)
        {
            return null;
        }

        return DescendToOffset(root, offset);
    }

    /// <inheritdoc cref="NodeFromOffset(IHtmlDocument, int)"/>
    public static INode? NodeFromOffset(string source, int offset) => NodeFromOffset(Parse(source), offset);

    // Matches the split marker inserted by "Insert Split Marker".
    private static readonly Regex BreakTagSearch = new(
        "(<div>\\s*)?<hr\\s*class\\s*=\\s*\"[^\"]*(signet_split_marker)[^\"]*\"\\s*/>(\\s*</div>)?",
        RegexOptions.Compiled);

    /// <summary>
    /// Splits (X)HTML source at split markers (<c>&lt;hr class="signet_split_marker"/&gt;</c>)
    /// placed in <c>&lt;body&gt;</c>. Each section gets a copy of the header (prolog + <c>&lt;head&gt;</c>
    /// + the <c>&lt;body&gt;</c> opening tag) and re-opens tags that were open at the split point (e.g.
    /// a nested <c>&lt;div&gt;</c> spanning several sections), using <see cref="TagLister"/>.
    /// No <c>&lt;body&gt;</c> or no split markers →
    /// a single-element list with <paramref name="source"/> unchanged.
    /// </summary>
    public static IReadOnlyList<string> GetSgfSectionSplits(string source)
    {
        ArgumentNullException.ThrowIfNull(source);

        TagLister tagList = new(source);
        int bo = tagList.FindBodyOpenTag();
        int bc = tagList.FindBodyCloseTag();
        if (bo < 0 || bc < 0)
        {
            return new[] { source };
        }

        TagLister.TagInfo bodyOpen = tagList.At(bo);
        int bodyTagEnd = bodyOpen.Pos + bodyOpen.Len;
        int bodyContentsEnd = tagList.At(bc).Pos;
        string header = source[..bodyTagEnd];

        List<int> sectionStarts = new();
        List<int> sectionEnds = new();

        int startPos = bodyTagEnd;
        while (startPos < bodyContentsEnd)
        {
            Match match = BreakTagSearch.Match(source, startPos);
            if (match.Success)
            {
                int splitPos = match.Index;
                if (splitPos < bodyContentsEnd)
                {
                    sectionStarts.Add(startPos);
                    sectionEnds.Add(splitPos);
                }

                startPos = splitPos + match.Length;
            }
            else
            {
                sectionStarts.Add(startPos);
                sectionEnds.Add(bodyContentsEnd);
                startPos = bodyContentsEnd;
            }
        }

        List<string> sections = new();
        for (int i = 0; i < sectionStarts.Count; i++)
        {
            string text = source[sectionStarts[i]..sectionEnds[i]];
            string openTagSource = string.Join(" ", GetUnmatchedTagsForPosition(sectionStarts[i], tagList));
            sections.Add(header + openTagSource + text + "</body>\n</html>\n");
        }

        if (sections.Count == 0)
        {
            sections.Add(source);
        }

        return sections;
    }

    /// <summary>
    /// Finds opening tags without a matching closing tag "to the left" of <paramref name="pos"/>, up
    /// to <c>&lt;body&gt;</c> — needed so that each split section inherits the containers that are open
    /// (e.g. <c>&lt;div&gt;</c>) before the cut point.
    /// </summary>
    private static List<string> GetUnmatchedTagsForPosition(int pos, TagLister tagList)
    {
        List<string> openingTags = new();
        List<int> pairedTags = new();
        string text = tagList.Source;
        int i = tagList.FindFirstTagOnOrAfter(pos);
        i--;
        if (i < 0)
        {
            return openingTags;
        }

        while (i >= 0 && tagList.At(i).TagName != "body")
        {
            TagLister.TagInfo tagInfo = tagList.At(i);
            if (tagInfo.Kind == TagKind.End)
            {
                pairedTags.Add(tagInfo.OpenPos);
            }
            else if (tagInfo.Kind == TagKind.Begin)
            {
                if (pairedTags.Contains(tagInfo.Pos))
                {
                    pairedTags.Remove(tagInfo.Pos);
                }
                else
                {
                    openingTags.Insert(0, text.Substring(tagInfo.Pos, tagInfo.Len));
                }
            }

            i--;
        }

        return openingTags;
    }

    // =====================================================================
    //  Implementation details
    // =====================================================================

    private static IElement? DescendToOffset(IElement element, int offset)
    {
        int start = OffsetFromNode(element);
        if (start < 0 || start > offset)
        {
            return null;
        }

        IElement? candidate = null;
        foreach (IElement child in element.Children)
        {
            int childStart = OffsetFromNode(child);
            if (childStart < 0)
            {
                continue;
            }

            if (childStart <= offset)
            {
                candidate = child;
            }
            else
            {
                break;
            }
        }

        return candidate is null ? element : DescendToOffset(candidate, offset) ?? candidate;
    }

    private static string? RelativeReferenceToBookPath(string? reference, string startFolder)
    {
        if (string.IsNullOrEmpty(reference)
            || reference[0] == '#'
            || reference.Contains(':', StringComparison.Ordinal))
        {
            return null;
        }

        string path = reference;
        int hash = path.IndexOf('#', StringComparison.Ordinal);
        if (hash >= 0)
        {
            path = path[..hash];
        }

        int query = path.IndexOf('?', StringComparison.Ordinal);
        if (query >= 0)
        {
            path = path[..query];
        }

        return path.Length == 0
            ? null
            : CoreBookPath.BuildBookPath(Utility.UrlDecodePath(path), startFolder);
    }
}

/// <summary>An element referring to another file (result of <see cref="XhtmlDoc.GetLinkElements(IHtmlDocument)"/>).</summary>
/// <param name="TagName">Tag name (lowercase): <c>a</c>, <c>link</c>, <c>img</c> or <c>script</c>.</param>
/// <param name="Attribute">Name of the attribute holding the reference: <c>href</c> or <c>src</c>.</param>
/// <param name="Value">Raw (encoded) attribute value.</param>
public readonly record struct LinkElement(string TagName, string Attribute, string Value);

/// <summary>An <c>h1</c>–<c>h6</c> heading (result of <see cref="XhtmlDoc.GetHeadingElements(IHtmlDocument)"/>).</summary>
/// <param name="TagName">Tag name (lowercase): <c>h1</c>…<c>h6</c>.</param>
/// <param name="Level">Heading level: 1–6.</param>
/// <param name="Id">The heading's <c>id</c> attribute, or <c>""</c>.</param>
/// <param name="Text">Heading text, whitespace collapsed and trimmed.</param>
public readonly record struct HeadingElement(string TagName, int Level, string Id, string Text);
