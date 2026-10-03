using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text.RegularExpressions;
using System.Xml;
using System.Xml.Linq;

namespace Signet.Core.Tests.TestSupport;

/// <summary>Options controlling the comparison of two EPUB archives by <see cref="EpubComparer"/>.</summary>
public sealed class EpubComparisonOptions
{
    /// <summary>Archive entries (full paths) skipped entirely in the comparison.</summary>
    public ISet<string> IgnoredEntries { get; } = new HashSet<string>(StringComparer.Ordinal);

    /// <summary>
    /// A predicate selecting the XML elements that are removed from both documents before the comparison
    /// (e.g. the volatile <c>&lt;meta property="dcterms:modified"&gt;</c>).
    /// </summary>
    public Func<XElement, bool>? IgnoreXmlElement { get; set; }

    /// <summary>
    /// Strict comparison: insignificant whitespace and the attribute order in XML are always ignored,
    /// but no elements are ignored.
    /// </summary>
    public static EpubComparisonOptions Default => new();

    /// <summary>
    /// Round-trip mode: additionally ignores the <c>&lt;meta&gt;</c> with the modification date and the
    /// "generator" meta (Signet), which by nature change on every save.
    /// </summary>
    public static EpubComparisonOptions RoundTrip => new() { IgnoreXmlElement = IsVolatileMeta };

    private static bool IsVolatileMeta(XElement element)
    {
        if (!string.Equals(element.Name.LocalName, "meta", StringComparison.Ordinal))
        {
            return false;
        }

        string property = (string?)element.Attribute("property") ?? string.Empty;
        string name = (string?)element.Attribute("name") ?? string.Empty;
        string content = (string?)element.Attribute("content") ?? string.Empty;

        return string.Equals(property, "dcterms:modified", StringComparison.Ordinal)
            || name.Contains("modified", StringComparison.OrdinalIgnoreCase)
            || content.Contains("Signet version", StringComparison.OrdinalIgnoreCase)
            || content.Contains("Signet", StringComparison.OrdinalIgnoreCase);
    }
}

/// <summary>The result of comparing two EPUB archives.</summary>
public sealed class EpubComparisonResult
{
    internal EpubComparisonResult(IReadOnlyList<string> differences) => Differences = differences;

    /// <summary>The list of detected differences. Empty means equivalence.</summary>
    public IReadOnlyList<string> Differences { get; }

    /// <summary>Whether the archives are equivalent in the sense of the <see cref="EpubComparisonOptions"/> used.</summary>
    public bool AreEquivalent => Differences.Count == 0;

    /// <summary>A readable report (one difference per line) or an equivalence message.</summary>
    public string Report => AreEquivalent
        ? "The EPUBs are equivalent."
        : "EPUB differences:" + Environment.NewLine + string.Join(Environment.NewLine, Differences);
}

/// <summary>
/// Compares two <c>.epub</c> files entry by entry. The XML entries (<c>.opf</c>, <c>.xhtml</c>,
/// <c>.ncx</c>, <c>.xml</c>, <c>.svg</c>, <c>.html</c>) are compared structurally after
/// normalization (whitespace, attribute order); the others — byte for byte.
/// </summary>
/// <remarks>
/// This is not canonical XML C14N. The whitespace normalization trims and collapses runs of
/// whitespace in text nodes (except <c>pre</c>/<c>style</c>/<c>script</c>/<c>textarea</c>),
/// so differences in indentation are invisible at the cost of sensitivity to single spaces between
/// inline elements. This is enough for comparing hand-formatted EPUB content.
/// <para>
/// Namespace declarations (<c>xmlns</c> / <c>xmlns:*</c>) are removed before the comparison —
/// element and attribute names are already expanded by the parser, and a redundant declaration does not change
/// the meaning of the document. Thanks to this e.g. <c>&lt;metadata xmlns:opf="…"&gt;</c> (a prefix that is always added
/// to <c>&lt;metadata&gt;</c>) is equivalent to the <c>&lt;metadata&gt;</c> from the corpus.
/// </para>
/// </remarks>
public static partial class EpubComparer
{
    private static readonly HashSet<string> XmlExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".opf", ".xhtml", ".html", ".htm", ".ncx", ".xml", ".svg",
    };

    private static readonly HashSet<string> WhitespacePreservingElements = new(StringComparer.OrdinalIgnoreCase)
    {
        "pre", "style", "script", "textarea",
    };

    /// <summary>Compares two <c>.epub</c> files on disk.</summary>
    public static EpubComparisonResult Compare(string epubPathA, string epubPathB, EpubComparisonOptions? options = null) =>
        CompareBytes(File.ReadAllBytes(epubPathA), File.ReadAllBytes(epubPathB), options);

    /// <summary>Compares two EPUB archives passed as bytes.</summary>
    public static EpubComparisonResult CompareBytes(byte[] epubA, byte[] epubB, EpubComparisonOptions? options = null)
    {
        options ??= EpubComparisonOptions.Default;
        List<string> differences = [];

        using ZipArchive archiveA = new(new MemoryStream(epubA, writable: false), ZipArchiveMode.Read);
        using ZipArchive archiveB = new(new MemoryStream(epubB, writable: false), ZipArchiveMode.Read);

        Dictionary<string, ZipArchiveEntry> entriesA = FileEntries(archiveA, options);
        Dictionary<string, ZipArchiveEntry> entriesB = FileEntries(archiveB, options);

        foreach (string name in entriesA.Keys.Except(entriesB.Keys).OrderBy(k => k, StringComparer.Ordinal))
        {
            differences.Add($"Wpis tylko w A: {name}");
        }

        foreach (string name in entriesB.Keys.Except(entriesA.Keys).OrderBy(k => k, StringComparer.Ordinal))
        {
            differences.Add($"Wpis tylko w B: {name}");
        }

        foreach (string name in entriesA.Keys.Where(entriesB.ContainsKey).OrderBy(k => k, StringComparer.Ordinal))
        {
            byte[] bytesA = ReadEntry(entriesA[name]);
            byte[] bytesB = ReadEntry(entriesB[name]);

            if (XmlExtensions.Contains(Path.GetExtension(name)))
            {
                string? reason = CompareXml(bytesA, bytesB, options);
                if (reason is not null)
                {
                    differences.Add($"XML differs in '{name}': {reason}");
                }
            }
            else if (!bytesA.AsSpan().SequenceEqual(bytesB))
            {
                differences.Add($"Bajty różnią się w '{name}' ({bytesA.Length} B vs {bytesB.Length} B)");
            }
        }

        return new EpubComparisonResult(differences);
    }

    private static Dictionary<string, ZipArchiveEntry> FileEntries(ZipArchive zip, EpubComparisonOptions options) =>
        zip.Entries
            .Where(e => e.FullName.Length > 0 && !e.FullName.EndsWith('/'))
            .Where(e => !options.IgnoredEntries.Contains(e.FullName))
            .ToDictionary(e => e.FullName, e => e, StringComparer.Ordinal);

    private static byte[] ReadEntry(ZipArchiveEntry entry)
    {
        using Stream stream = entry.Open();
        using MemoryStream ms = new();
        stream.CopyTo(ms);
        return ms.ToArray();
    }

    private static string? CompareXml(byte[] a, byte[] b, EpubComparisonOptions options)
    {
        XDocument docA;
        XDocument docB;

        try
        {
            docA = Parse(a);
        }
        catch (XmlException ex)
        {
            return $"strona A nie jest poprawnym XML ({ex.Message})";
        }

        try
        {
            docB = Parse(b);
        }
        catch (XmlException ex)
        {
            return $"strona B nie jest poprawnym XML ({ex.Message})";
        }

        Normalize(docA, options);
        Normalize(docB, options);

        if (XNode.DeepEquals(docA, docB))
        {
            return null;
        }

        return "the structure or content differs" + Environment.NewLine
            + "  A: " + docA + Environment.NewLine
            + "  B: " + docB;
    }

    private static XDocument Parse(byte[] bytes)
    {
        XmlReaderSettings settings = new()
        {
            DtdProcessing = DtdProcessing.Ignore,
            XmlResolver = null,
            IgnoreComments = true,
            IgnoreProcessingInstructions = true,
            IgnoreWhitespace = true,
        };

        using MemoryStream ms = new(bytes, writable: false);
        using XmlReader reader = XmlReader.Create(ms, settings);
        XDocument doc = XDocument.Load(reader);
        doc.Declaration = null;
        return doc;
    }

    private static void Normalize(XDocument doc, EpubComparisonOptions options)
    {
        if (options.IgnoreXmlElement is { } predicate)
        {
            foreach (XElement element in doc.Descendants().Where(predicate).ToList())
            {
                element.Remove();
            }
        }

        if (doc.Root is { } root)
        {
            NormalizeElement(root, insidePreservingElement: false);
        }
    }

    private static void NormalizeElement(XElement element, bool insidePreservingElement)
    {
        foreach (XAttribute declaration in element.Attributes().Where(a => a.IsNamespaceDeclaration).ToList())
        {
            declaration.Remove();
        }

        SortAttributes(element);

        bool preserve = insidePreservingElement
            || WhitespacePreservingElements.Contains(element.Name.LocalName);

        foreach (XElement child in element.Elements().ToList())
        {
            NormalizeElement(child, preserve);
        }

        if (preserve)
        {
            return;
        }

        foreach (XText textNode in element.Nodes().OfType<XText>().ToList())
        {
            string collapsed = WhitespaceRun().Replace(textNode.Value, " ").Trim();
            if (collapsed.Length == 0)
            {
                textNode.Remove();
            }
            else
            {
                textNode.Value = collapsed;
            }
        }
    }

    private static void SortAttributes(XElement element)
    {
        List<XAttribute> attributes = element.Attributes().ToList();
        if (attributes.Count <= 1)
        {
            return;
        }

        List<XAttribute> namespaceDeclarations = attributes.Where(a => a.IsNamespaceDeclaration).ToList();
        List<XAttribute> ordered = attributes
            .Where(a => !a.IsNamespaceDeclaration)
            .OrderBy(a => a.Name.NamespaceName, StringComparer.Ordinal)
            .ThenBy(a => a.Name.LocalName, StringComparer.Ordinal)
            .ToList();

        element.RemoveAttributes();
        foreach (XAttribute declaration in namespaceDeclarations)
        {
            element.Add(declaration);
        }

        foreach (XAttribute attribute in ordered)
        {
            element.Add(attribute);
        }
    }

    [GeneratedRegex(@"\s+")]
    private static partial Regex WhitespaceRun();
}
