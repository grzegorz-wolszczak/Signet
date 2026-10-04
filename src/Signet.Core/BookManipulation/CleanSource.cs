using System;
using System.Collections.Generic;
using System.Text;
using System.Text.RegularExpressions;
using AngleSharp;
using AngleSharp.Dom;
using AngleSharp.Html.Parser;
using AngleSharp.Xhtml;

namespace Signet.Core.BookManipulation;

/// <summary>
/// Repair and normalization of XHTML code (<c>Mend</c>, <c>ToValidXHTML</c> and the component steps).
/// </summary>
/// <remarks>
/// <para>
/// The structure is repaired with the AngleSharp HTML5 parser (the same one as
/// <see cref="XhtmlDoc"/> / <c>HtmlResource</c> — tolerant of invalid XHTML) and serialized with
/// <see cref="XhtmlMarkupFormatter"/> using <c>emptyTagsToSelfClosing = false</c> (empty
/// non-void elements serialize as a <c>&lt;script&gt;&lt;/script&gt;</c> pair, not <c>&lt;script/&gt;</c>).
/// The <c>&lt;?xml?&gt;</c> declaration and the DOCTYPE are rebuilt explicitly,
/// because AngleSharp in HTML mode does not emit the XML prolog.
/// </para>
/// <para>
/// A DOCTYPE is never added silently: a document without one stays without one unless the caller
/// explicitly asks for it (<c>addMissingDoctype</c> — the user's Mend / Prettify preference). An existing
/// DOCTYPE is normalized to the one of the EPUB version.
/// </para>
/// </remarks>
public static class CleanSource
{
    private const int SafeLength = 200;

    private static readonly Regex HeadEnd = new(@"</\s*head\s*>", RegexOptions.Compiled);

    private static readonly Regex MetaCharset = new(@"<meta[^>]+charset[^>]+>", RegexOptions.Compiled);

    private static readonly Regex RootSvgTagWithPrefix = new(@"<\s*svg\s*:\s*svg", RegexOptions.Compiled);

    private static readonly Regex SvgNamespacePrefix = new(
        "<\\s*[^>]*(xmlns\\s*:\\s*svg\\s*=\\s*(?:\"|')[^\"']+(?:\"|'))[^>]*>",
        RegexOptions.Compiled);

    private static readonly Regex StartingChildSvgTagWithPrefix = new(@"<\s*svg\s*:", RegexOptions.Compiled);

    private static readonly Regex EndingChildSvgTagWithPrefix = new(@"<\s*/\s*svg\s*:", RegexOptions.Compiled);

    private static readonly Regex DoctypeInvalid = new("<!DOCTYPE html PUBLIC \"W3C", RegexOptions.Compiled);

    private static readonly Regex DoctypeMissingNewline = new(@"\?><!DOCTYPE", RegexOptions.Compiled);

    private static readonly Regex HtmlMissingNewline = new("\"><html ", RegexOptions.Compiled);

    private static readonly Regex NcxMissingNewline = new("\"><ncx ", RegexOptions.Compiled);

    private static readonly Regex DoctypeHttpMissingNewline = new("//EN\" \"http://", RegexOptions.Compiled);

    internal const string XmlDeclaration = "<?xml version=\"1.0\" encoding=\"utf-8\"?>\n";

    private const string DoctypeXhtml11 =
        "<!DOCTYPE html PUBLIC \"-//W3C//DTD XHTML 1.1//EN\"\n" +
        "  \"http://www.w3.org/TR/xhtml11/DTD/xhtml11.dtd\">\n\n";

    private const string DoctypeHtml5 = "<!DOCTYPE html>\n\n";

    // =====================================================================
    //  Public API
    // =====================================================================

    /// <summary>
    /// Minimal repair and normalization of XHTML source.
    /// </summary>
    /// <param name="source">The XHTML source.</param>
    /// <param name="version">The EPUB version (<c>"2.0"</c> / <c>"3.0"</c> — only the first character is examined).</param>
    /// <param name="entityOverrides">
    /// An optional character → entity text map for <see cref="CharToEntity"/> — "Preserve Entities" from
    /// Preferences. <see langword="null"/> = the default behavior.
    /// </param>
    /// <param name="addMissingDoctype">
    /// Whether to add a DOCTYPE when the source has none. An existing DOCTYPE is always normalized to the
    /// one of <paramref name="version"/>.
    /// </param>
    /// <remarks>
    /// Pipeline: <see cref="PreprocessSpecialCases"/> → <see cref="RemoveMetaCharset"/> → structure
    /// repair with the HTML5 parser (closing tags, fixing nesting, adding missing
    /// <c>html/head/body</c>, XML prolog + DOCTYPE per version) → <see cref="CharToEntity"/> →
    /// <see cref="PrettifyDOCTYPEHeader"/>.
    /// </remarks>
    public static string Mend(
        string source,
        string version,
        IReadOnlyDictionary<char, string>? entityOverrides = null,
        bool addMissingDoctype = false)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(version);

        string newsource = PreprocessSpecialCases(source);
        newsource = RemoveMetaCharset(newsource);
        newsource = Repair(newsource, version, addMissingDoctype);
        newsource = CharToEntity(newsource, version, entityOverrides);
        newsource = PrettifyDOCTYPEHeader(newsource);
        return newsource;
    }

    /// <summary>
    /// Returns valid (well-formed) XHTML.
    /// </summary>
    /// <remarks>
    /// The well-formed gate: if <paramref name="source"/> is already syntactically valid
    /// (<see cref="WellFormedChecker.IsWellFormed(string, string)"/>), it is returned unchanged;
    /// otherwise the result of <see cref="Mend"/> is returned.
    /// </remarks>
    public static string ToValidXHTML(string source, string version)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(version);

        return WellFormedChecker.IsWellFormed(source) ? source : Mend(source, version);
    }

    /// <summary>
    /// Formats XHTML source into a readable form.
    /// </summary>
    /// <param name="source">The XHTML source.</param>
    /// <param name="keepWhitespace">
    /// When <c>true</c>, whitespace in the content is never collapsed (like "Reformat HTML" preserving
    /// spaces); when <c>false</c>, it is collapsed everywhere except inside <c>pre</c>/<c>code</c>/<c>textarea</c>/
    /// <c>script</c>/<c>style</c> tags.
    /// </param>
    /// <param name="version">The EPUB version (<c>"2.0"</c> / <c>"3.0"</c>).</param>
    /// <param name="options">
    /// Optional <see cref="PrettyPrintOptions"/> (the indent string, <c>singlespace</c>); by default
    /// a two-space indent without <c>singlespace</c> mode.
    /// </param>
    /// <param name="entityOverrides">
    /// An optional character → entity text map for <see cref="CharToEntity"/> — "Preserve Entities" from
    /// Preferences. <see langword="null"/> = the default behavior.
    /// </param>
    /// <param name="props">
    /// An optional full tag classification (<see cref="PrettyPrintProps.LoadUserPrefs"/>) —
    /// when given, it takes precedence over <paramref name="options"/> (which then only provides
    /// signature compatibility; its values are ignored).
    /// </param>
    /// <param name="addMissingDoctype">
    /// Whether to add a DOCTYPE when the source has none. An existing DOCTYPE is always normalized to the
    /// one of <paramref name="version"/>.
    /// </param>
    /// <remarks>
    /// The same pipeline as <see cref="Mend"/>, but instead of repairing the structure a formatting
    /// recursion is run (<see cref="XhtmlPrettyPrinter"/>):
    /// <see cref="PreprocessSpecialCases"/> → <see cref="RemoveMetaCharset"/> → pretty-print
    /// (XML prolog + DOCTYPE per version) → <see cref="CharToEntity"/> → <see cref="PrettifyDOCTYPEHeader"/>.
    /// </remarks>
    internal static string PrettyPrint(
        string source,
        bool keepWhitespace,
        string version,
        PrettyPrintOptions? options,
        IReadOnlyDictionary<char, string>? entityOverrides,
        PrettyPrintProps? props,
        bool addMissingDoctype = false)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(version);

        if (source.Length == 0)
        {
            return source;
        }

        string newsource = PreprocessSpecialCases(source);
        newsource = RemoveMetaCharset(newsource);
        newsource = Prettyprint(newsource, keepWhitespace, version, props ?? new PrettyPrintProps(
            (options ?? PrettyPrintOptions.Default).IndentString,
            (options ?? PrettyPrintOptions.Default).SingleSpace), addMissingDoctype);
        newsource = CharToEntity(newsource, version, entityOverrides);
        newsource = PrettifyDOCTYPEHeader(newsource);
        return newsource;
    }

    /// <summary>Like the overload with the additional <c>props</c> (internal); here the built-in tag classification is always used.</summary>
    public static string PrettyPrint(
        string source,
        bool keepWhitespace,
        string version,
        PrettyPrintOptions? options = null,
        IReadOnlyDictionary<char, string>? entityOverrides = null) =>
        PrettyPrint(source, keepWhitespace, version, options, entityOverrides, props: null);

    /// <summary>
    /// Formats plain XML (OPF / NCX / SMIL / page-map) into a readable form.
    /// </summary>
    /// <param name="source">The XML source.</param>
    /// <param name="mediaType">
    /// The MIME type (<c>application/oebps-package+xml</c>, <c>application/x-dtbncx+xml</c>,
    /// <c>application/smil+xml</c>, <c>application/oebps-page-map+xml</c>) — selects the list of tags
    /// that self-close when empty.
    /// </param>
    /// <remarks>
    /// <para>For an NCX without an <c>&lt;ncx</c> tag the source is returned unchanged.</para>
    /// <para>The OPF is not rebuilt through a model and no attempt is made to recover syntactically
    /// invalid XML — a source that is not well-formed is returned unchanged.
    /// Valid XML is always re-indented.</para>
    /// </remarks>
    public static string PrettyPrintXml(string source, string mediaType = "")
    {
        ArgumentNullException.ThrowIfNull(source);

        if (string.Equals(mediaType, "application/x-dtbncx+xml", StringComparison.Ordinal)
            && !source.Contains("<ncx", StringComparison.Ordinal))
        {
            return source;
        }

        return XmlPrettyPrinter.TryPrettyPrint(source, mediaType, out string formatted) ? formatted : source;
    }

    // =====================================================================
    //  Component steps (internal — tested directly)
    // =====================================================================

    /// <summary>
    /// Removes the <c>svg:</c> prefix from SVG tags and inserts an unprefixed SVG namespace into
    /// the root tag. Neither <c>svg</c> nor
    /// <c>math</c> needs a prefix (EPUB 3 includes them in HTML5), and HTML5 parsers such as AngleSharp cope
    /// with them only without a prefix.
    /// </summary>
    internal static string PreprocessSpecialCases(string source)
    {
        string newsource = RootSvgTagWithPrefix.Replace(source, "<svg xmlns=\"http://www.w3.org/2000/svg\"");

        Match nsMatch = SvgNamespacePrefix.Match(newsource);
        if (nsMatch.Success)
        {
            Group prefixGroup = nsMatch.Groups[1];
            newsource = newsource.Remove(prefixGroup.Index, prefixGroup.Length);
        }

        newsource = StartingChildSvgTagWithPrefix.Replace(newsource, "<");
        newsource = EndingChildSvgTagWithPrefix.Replace(newsource, "</");
        return newsource;
    }

    /// <summary>
    /// Removes the <c>&lt;meta&gt;</c> tags declaring the encoding from the <c>&lt;head&gt;</c> section.
    /// The encoding is carried by the XML declaration anyway, and an XHTML file
    /// cannot have two encoding declarations. The meta is not added back.
    /// </summary>
    internal static string RemoveMetaCharset(string source)
    {
        Match headEnd = HeadEnd.Match(source);
        if (!headEnd.Success)
        {
            return source;
        }

        string head = source[..headEnd.Index];
        Match meta = MetaCharset.Match(head);
        if (!meta.Success)
        {
            return source;
        }

        head = head.Remove(meta.Index, meta.Length);
        return head + source[headEnd.Index..];
    }

    /// <summary>
    /// Replaces preserved characters with entities. By default
    /// (<c>preserve_entity_names = &amp;#160;</c>) the literal character
    /// U+00A0 becomes <c>&amp;#160;</c>. The named entity
    /// <c>&amp;nbsp;</c> is also converted to <c>&amp;#160;</c> (a named entity is invalid in well-formed
    /// EPUB 3 XML). The behavior is currently independent of <paramref name="version"/>.
    /// </summary>
    /// <param name="source">The source to process.</param>
    /// <param name="version">The EPUB version — currently unused.</param>
    /// <param name="overrides">
    /// An optional character → entity text map overriding the default behavior (a hook for future
    /// integration with user settings).
    /// </param>
    internal static string CharToEntity(string source, string version, IReadOnlyDictionary<char, string>? overrides = null)
    {
        _ = version;
        string result = source;

        if (overrides is { Count: > 0 })
        {
            foreach (KeyValuePair<char, string> pair in overrides)
            {
                result = result.Replace(pair.Key.ToString(), pair.Value, StringComparison.Ordinal);
            }

            return result;
        }

        result = result.Replace("\u00A0", "&#160;", StringComparison.Ordinal);
        result = result.Replace("&nbsp;", "&#160;", StringComparison.Ordinal);
        return result;
    }

    /// <summary>
    /// Repairs typical distortions of the DOCTYPE header.
    /// Takes care not to break a valid EPUB 3 <c>&lt;!DOCTYPE html&gt;</c>. Works only within the first
    /// <see cref="SafeLength"/> characters.
    /// </summary>
    internal static string PrettifyDOCTYPEHeader(string source)
    {
        string newsource = source;

        int index = IndexOfRegex(DoctypeInvalid, newsource);
        if (index > 0 && index < SafeLength)
        {
            newsource = newsource.Insert(index + 23, "-//");
        }

        index = IndexOfRegex(DoctypeMissingNewline, source);
        if (index > 0 && index < SafeLength)
        {
            newsource = newsource.Insert(index + 2, "\n");

            index = IndexOfRegex(HtmlMissingNewline, newsource);
            if (index > 0 && index < SafeLength)
            {
                newsource = newsource.Insert(index + 2, "\n\n");
            }

            bool isNcx = false;
            index = IndexOfRegex(NcxMissingNewline, newsource);
            if (index > 0 && index < SafeLength)
            {
                isNcx = true;
                newsource = newsource.Insert(index + 2, "\n");
            }

            index = IndexOfRegex(DoctypeHttpMissingNewline, newsource);
            if (index > 0 && index < SafeLength)
            {
                newsource = newsource.Insert(index + 5, isNcx ? "\n" : "\n ");
            }
        }

        return newsource;
    }

    // =====================================================================
    //  Structure repair
    // =====================================================================

    private static string Repair(string source, string version, bool addMissingDoctype)
    {
        if (source.Length == 0)
        {
            return source;
        }

        string stripped = StripXmlDeclaration(source);

        HtmlParser parser = new(new HtmlParserOptions { IsKeepingSourceReferences = true });
        IDocument document = parser.ParseDocument(stripped);

        XhtmlMarkupFormatter formatter = new(emptyTagsToSelfClosing: false);
        StringBuilder body = new();
        foreach (INode node in document.ChildNodes)
        {
            if (node.NodeType == NodeType.DocumentType)
            {
                continue;
            }

            body.Append(node.ToHtml(formatter));
        }

        string serialized = body.ToString().TrimEnd();
        return XmlDeclaration + BuildDoctype(document, version, addMissingDoctype) + serialized;
    }

    /// <summary>
    /// Structure formatting: parsing with the
    /// tolerant HTML5 parser, the <see cref="XhtmlPrettyPrinter"/> recursion, XML prolog + DOCTYPE.
    /// </summary>
    private static string Prettyprint(
        string source, bool keepWhitespace, string version, PrettyPrintProps props, bool addMissingDoctype)
    {
        string stripped = StripXmlDeclaration(source);

        HtmlParser parser = new(new HtmlParserOptions { IsKeepingSourceReferences = true });
        IDocument document = parser.ParseDocument(stripped);

        XhtmlPrettyPrinter printer = new(props, keepWhitespace);

        string contents = (BuildDoctype(document, version, addMissingDoctype) + printer.PrintDocumentContents(document)).TrimEnd();
        return XmlDeclaration + contents;
    }

    /// <summary>
    /// Cuts off the leading <c>&lt;?xml ... ?&gt;</c> declaration together with the whitespace that follows
    /// it (the HTML5 parser treats it as a "bogus comment").
    /// </summary>
    internal static string StripXmlDeclaration(string source)
    {
        if (!source.StartsWith("<?xml", StringComparison.Ordinal))
        {
            return source;
        }

        int close = source.IndexOf('>', 5);
        if (close < 0)
        {
            return source;
        }

        int next = close + 1;
        while (next < source.Length && "\n\r\t\v\f ".Contains(source[next], StringComparison.Ordinal))
        {
            next++;
        }

        return source[next..];
    }

    /// <summary>
    /// Builds the DOCTYPE for the given EPUB version. When the document has no DOCTYPE and
    /// <paramref name="addMissingDoctype"/> is <c>false</c>, returns <c>""</c> (the document stays without one).
    /// </summary>
    internal static string BuildDoctype(IDocument document, string version, bool addMissingDoctype = false)
    {
        if (!addMissingDoctype && document.Doctype is null)
        {
            return string.Empty;
        }

        if (version.StartsWith('2'))
        {
            return DoctypeXhtml11;
        }

        if (version.StartsWith('3'))
        {
            return DoctypeHtml5;
        }

        IDocumentType? doctype = document.Doctype;
        if (doctype is null)
        {
            return string.Empty;
        }

        bool minimalHtml = doctype.Name == "html"
            && string.IsNullOrEmpty(doctype.PublicIdentifier)
            && string.IsNullOrEmpty(doctype.SystemIdentifier);

        return minimalHtml ? DoctypeHtml5 : DoctypeXhtml11;
    }

    /// <summary>
    /// Serializes the document (after in-place DOM modification) back to source: XML prolog +
    /// DOCTYPE per version (only when the document has one) + content via <see cref="XhtmlMarkupFormatter"/> +
    /// <see cref="CharToEntity"/>.
    /// The shared tail used by <see cref="SourceUpdates.PerformHtmlUpdates"/> and
    /// <see cref="SourceUpdates.AnchorUpdates"/> after changes are applied to the parsed document.
    /// </summary>
    internal static string ReserializeXhtmlDocument(IDocument document, string version)
    {
        XhtmlMarkupFormatter formatter = new(emptyTagsToSelfClosing: false);
        StringBuilder body = new();
        foreach (INode node in document.ChildNodes)
        {
            if (node.NodeType == NodeType.DocumentType)
            {
                continue;
            }

            body.Append(node.ToHtml(formatter));
        }

        string serialized = body.ToString().TrimEnd();
        string result = XmlDeclaration + BuildDoctype(document, version) + serialized;
        return CharToEntity(result, version);
    }

    private static int IndexOfRegex(Regex regex, string text)
    {
        Match match = regex.Match(text);
        return match.Success ? match.Index : -1;
    }
}
