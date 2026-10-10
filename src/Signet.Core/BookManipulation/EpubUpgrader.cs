using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using Signet.Core.Resources;
using Signet.Core.Semantics;
using Signet.Core.Toc;

namespace Signet.Core.BookManipulation;

/// <summary>The outcome of <see cref="EpubUpgrader.UpgradeToEpub3"/>.</summary>
/// <param name="Nav">The navigation document created for the book.</param>
/// <param name="TocEntries">The number of table of contents entries copied from the NCX into the nav.</param>
/// <param name="Landmarks">The number of landmarks created from the OPF guide.</param>
/// <param name="Doctypes">The number of (X)HTML files whose DOCTYPE was changed to the HTML5 one.</param>
public sealed record EpubUpgradeResult(HtmlResource Nav, int TocEntries, int Landmarks, int Doctypes);

/// <summary>
/// Converts an EPUB 2 book to EPUB 3 in place ("Upgrade to EPUB 3"), modelled on calibre's <c>epub_2_to_3</c>
/// (<c>ebooks/oeb/polish/upgrade.py</c> and <c>ebooks/metadata/opf_2_to_3.py</c>).
/// </summary>
/// <remarks>
/// Unlike calibre, the NCX and the OPF <c>&lt;guide&gt;</c> are kept (EPUB 3 still allows both, and EPUB 2 readers
/// keep their table of contents), and the DOCTYPE of every (X)HTML file is replaced with the HTML5 one. The steps:
/// <list type="number">
/// <item>metadata: <c>opf:role</c> / <c>opf:file-as</c> become <c>refines</c> metadata, <c>opf:scheme</c> is folded
/// into the identifier value, only the first <c>dc:date</c> that is not a modification date is kept (that one is
/// <c>dcterms:modified</c> now), EPUB 2 rendition metadata becomes <c>rendition:*</c> properties, and the other
/// <c>opf:*</c> attributes are removed;</item>
/// <item><c>version="3.0"</c> in the OPF and on every resource;</item>
/// <item>manifest <c>properties</c> (<c>svg</c>, <c>scripted</c>, <c>mathml</c>, <c>cover-image</c>, ...);</item>
/// <item>a new nav document: the table of contents and page list from the NCX (or from the headings when the
/// NCX has no entries), the landmarks from the guide;</item>
/// <item>font media types, Adobe font obfuscation switched to the IDPF algorithm (Adobe's is not part of EPUB 3).</item>
/// </list>
/// </remarks>
public static class EpubUpgrader
{
    private const string Epub3Version = "3.0";

    // The DOCTYPE of an (X)HTML file, without an internal subset (a file that declares its own entities is left alone).
    private static readonly Regex HtmlDoctype = new(
        @"<!DOCTYPE\s+html\b[^>\[]*>", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

    private const string Html5Doctype = "<!DOCTYPE html>";

    // EPUB 2 rendition metadata (<meta name="..." content="...">) and the EPUB 3 property it becomes.
    private static readonly Dictionary<string, string> RenditionMeta = new(StringComparer.Ordinal)
    {
        ["orientation"] = "rendition:orientation",
        ["layout"] = "rendition:layout",
        ["spread"] = "rendition:spread",
        ["fixed-layout"] = "rendition:layout",
        ["orientation-lock"] = "rendition:orientation",
    };

    /// <summary>
    /// Converts <paramref name="book"/> to EPUB 3. Returns <c>null</c> (and changes nothing) when the book already is
    /// EPUB 3. Open editors must be saved into the resources first.
    /// </summary>
    public static EpubUpgradeResult? UpgradeToEpub3(Book book)
    {
        ArgumentNullException.ThrowIfNull(book);
        if (book.IsEpub3)
        {
            return null;
        }

        FolderKeeper folderKeeper = book.GetFolderKeeper();
        OpfResource opf = book.GetOpf();

        // Read the EPUB 2 navigation before anything changes.
        NcxResource? ncx = book.GetNcx();
        NcxDocument? ncxDocument = ncx?.GetNcxDocument();
        IReadOnlyList<GuideInfo> guide = opf.GetAllGuideInfoByBookPath();
        string language = opf.GetPrimaryBookLanguage();

        OpfDocument document = opf.GetOpfDocument();
        document.Package.Version = Epub3Version;
        UpgradeMetadata(document);
        opf.SetOpfDocument(document);

        book.EpubVersion = Epub3Version;
        foreach (Resource resource in folderKeeper.GetResourceList())
        {
            resource.EpubVersion = Epub3Version;
        }

        int doctypes = 0;
        foreach (HtmlResource html in book.GetHtmlResources())
        {
            if (ReplaceDoctype(html))
            {
                doctypes++;
            }
        }

        // Before the nav exists: the content-based properties replace the attribute of every HTML entry.
        opf.UpdateManifestProperties(folderKeeper.GetResourceList());
        opf.UpdateManifestMediaTypes(folderKeeper.GetResourceTypeList<FontResource>());
        foreach (FontResource font in folderKeeper.GetResourceTypeList<FontResource>())
        {
            if (font.ObfuscationAlgorithm == OcfReader.AdobeFontAlgorithmId)
            {
                book.SetFontObfuscation(font, OcfReader.IdpfFontAlgorithmId);
            }
        }

        HtmlResource nav = book.CreateEmptyNavFile(updateOpf: true);
        nav.SetText(string.Empty);
        NavProcessor navProcessor = new(nav, language);
        if (ncx is not null && ncxDocument is not null && ncxDocument.NavMap.Count > 0)
        {
            navProcessor.ApplyNcx(ncxDocument, ncx.BookPath);
        }
        else
        {
            navProcessor.GenerateTocFromBookContents(book);
        }

        int landmarks = AddLandmarks(navProcessor, folderKeeper, guide);
        nav.SaveToDisk();

        opf.AddModificationDateMeta();
        book.Modified = true;
        return new EpubUpgradeResult(nav, navProcessor.GetToc().Count, landmarks, doctypes);
    }

    /// <summary>Replaces the DOCTYPE of <paramref name="html"/> with the HTML5 one; <c>true</c> when the text changed.</summary>
    internal static bool ReplaceDoctype(HtmlResource html)
    {
        string text = html.GetText();
        Match match = HtmlDoctype.Match(text);
        if (!match.Success || match.Value == Html5Doctype)
        {
            return false;
        }

        html.SetText(string.Concat(text.AsSpan(0, match.Index), Html5Doctype, text.AsSpan(match.Index + match.Length)));
        return true;
    }

    private static int AddLandmarks(NavProcessor navProcessor, FolderKeeper folderKeeper, IReadOnlyList<GuideInfo> guide)
    {
        int added = 0;
        foreach (GuideInfo entry in guide)
        {
            string code = LandmarkCodeOf(entry.Type);
            if (code.Length == 0
                || folderKeeper.GetResourceByBookPathNoThrow(entry.BookPath) is not HtmlResource target)
            {
                continue;
            }

            navProcessor.AddLandmarkCode(target, code, toggle: false, targetId: entry.Fragment);
            added++;
        }

        return added;
    }

    // A guide type (e.g. "text", "title-page", "other.afterword") as a landmark code; "" when there is none.
    private static string LandmarkCodeOf(string guideType)
    {
        string type = guideType.Trim().ToLowerInvariant();
        if (type.StartsWith("other.", StringComparison.Ordinal))
        {
            type = type["other.".Length..];
        }

        string mapped = Landmarks.GuideLandMapping(type);
        if (mapped.Length > 0 && Landmarks.IsLandmarksCode(mapped))
        {
            return mapped;
        }

        return Landmarks.IsLandmarksCode(type) ? type : string.Empty;
    }

    private static void UpgradeMetadata(OpfDocument document)
    {
        List<MetaEntry> metadata = document.Metadata;
        HashSet<string> ids = new(
            metadata.Select(m => m.Attributes.Value("id")).Where(id => id.Length > 0), StringComparer.Ordinal);
        List<MetaEntry> refines = new();
        bool hasDate = false;

        for (int i = 0; i < metadata.Count; i++)
        {
            MetaEntry meta = metadata[i];
            switch (meta.Name)
            {
                case "dc:identifier":
                    UpgradeIdentifier(meta);
                    break;
                case "dc:creator" or "dc:contributor":
                    refines.AddRange(RefinesForPerson(meta, ids));
                    break;
                case "dc:date":
                    // EPUB 3 allows a single dc:date (the publication date); the modification date is dcterms:modified.
                    if (meta.Content.Length == 0 || hasDate || meta.Attributes.Value("opf:event") == "modification")
                    {
                        metadata.RemoveAt(i--);
                        continue;
                    }

                    hasDate = true;
                    break;
                case "meta":
                    UpgradeRenditionMeta(meta);
                    break;
            }

            if (meta.Name.StartsWith("dc:", StringComparison.Ordinal))
            {
                RemoveOpfAttributes(meta);
            }
        }

        metadata.AddRange(refines);
    }

    // opf:scheme="ISBN" + "9780000000000" -> "urn:isbn:9780000000000"; a value that already is a URN stays as it is.
    private static void UpgradeIdentifier(MetaEntry identifier)
    {
        string scheme = identifier.Attributes.Value("opf:scheme").Trim();
        string value = identifier.Content;
        if (scheme.Length == 0 || value.Length == 0 || value.StartsWith("urn:", StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        string lowered = scheme.ToLowerInvariant();
        identifier.Content = lowered is "isbn" or "uuid" ? $"urn:{lowered}:{value}" : $"{lowered}:{value}";
    }

    private static List<MetaEntry> RefinesForPerson(MetaEntry person, HashSet<string> ids)
    {
        string role = person.Attributes.Value("opf:role").Trim();
        string fileAs = person.Attributes.Value("opf:file-as").Trim();
        List<MetaEntry> refines = new();
        if (role.Length == 0 && fileAs.Length == 0)
        {
            return refines;
        }

        string id = person.Attributes.Value("id");
        if (id.Length == 0)
        {
            string prefix = person.Name["dc:".Length..];
            int n = 1;
            while (!ids.Add(id = prefix + n.ToString(System.Globalization.CultureInfo.InvariantCulture)))
            {
                n++;
            }

            person.Attributes.Set("id", id);
        }

        if (role.Length > 0)
        {
            MetaEntry meta = new() { Name = "meta", Content = role };
            meta.Attributes.Set("refines", "#" + id);
            meta.Attributes.Set("property", "role");
            meta.Attributes.Set("scheme", "marc:relators");
            refines.Add(meta);
        }

        if (fileAs.Length > 0)
        {
            MetaEntry meta = new() { Name = "meta", Content = fileAs };
            meta.Attributes.Set("refines", "#" + id);
            meta.Attributes.Set("property", "file-as");
            refines.Add(meta);
        }

        return refines;
    }

    // <meta name="fixed-layout" content="true"/> -> <meta property="rendition:layout">pre-paginated</meta>
    private static void UpgradeRenditionMeta(MetaEntry meta)
    {
        string name = meta.Attributes.Value("name");
        if (name.StartsWith("rendition:", StringComparison.Ordinal))
        {
            name = name["rendition:".Length..];
        }

        if (!RenditionMeta.TryGetValue(name, out string? property))
        {
            return;
        }

        string content = meta.Attributes.Value("content");
        content = name switch
        {
            "fixed-layout" => content.Equals("true", StringComparison.OrdinalIgnoreCase) ? "pre-paginated" : "reflowable",
            "orientation-lock" => content.ToLowerInvariant() is "portrait" or "landscape" ? content.ToLowerInvariant() : "auto",
            _ => content,
        };

        meta.Attributes.Remove("name");
        meta.Attributes.Remove("content");
        meta.Attributes.Set("property", property);
        meta.Content = content;
    }

    private static void RemoveOpfAttributes(MetaEntry meta)
    {
        foreach (string key in meta.Attributes.Keys.Where(k => k.StartsWith("opf:", StringComparison.Ordinal) || k == "xmlns:opf").ToList())
        {
            meta.Attributes.Remove(key);
        }
    }
}
