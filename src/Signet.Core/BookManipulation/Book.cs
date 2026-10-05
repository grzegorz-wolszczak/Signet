using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using AngleSharp.Dom;
using Signet.Core.Parsers;
using Signet.Core.Resources;
using Signet.Core.SourceUpdates;
using Signet.Core.Toc;
using Signet.Core.Localization;
using SysPath = System.IO.Path;

namespace Signet.Core.BookManipulation;

/// <summary>
/// The aggregate of a single open EPUB publication: <see cref="FolderKeeper"/> (the resource registry),
/// <see cref="OpfResource"/>, an optional <see cref="NcxResource"/>, the EPUB version and the list
/// of load warnings.
/// </summary>
/// <remarks>
/// There is no "universal updates" logic; modification changes are signalled by the
/// <see cref="ModifiedStateChanged"/> event. The API surface is limited to what the import
/// and export pipelines require.
/// </remarks>
public sealed class Book : IDisposable
{
    private const string PlaceholderXhtml = "<?xml version=\"1.0\" encoding=\"utf-8\"?>\n";

    /// <summary>Default content of an empty XHTML file for EPUB 2.</summary>
    private const string EmptyHtmlFile =
        "<?xml version=\"1.0\" encoding=\"utf-8\"?>\n"
        + "<!DOCTYPE html PUBLIC \"-//W3C//DTD XHTML 1.1//EN\"\n"
        + "  \"http://www.w3.org/TR/xhtml11/DTD/xhtml11.dtd\">\n\n"
        + "<html xmlns=\"http://www.w3.org/1999/xhtml\">\n"
        + "<head>\n"
        + "  <title></title>\n"
        + "</head>\n\n"
        + "<body>\n"
        + "  <p>&#160;</p>\n"
        + "</body>\n"
        + "</html>";

    /// <summary>Default content of an empty XHTML file for EPUB 3.</summary>
    private const string EmptyHtml5File =
        "<?xml version=\"1.0\" encoding=\"utf-8\"?>\n"
        + "<!DOCTYPE html>\n\n"
        + "<html xmlns=\"http://www.w3.org/1999/xhtml\" xmlns:epub=\"http://www.idpf.org/2007/ops\">\n"
        + "<head>\n"
        + "  <title></title>\n"
        + "</head>\n\n"
        + "<body>\n"
        + "  <p>&#160;</p>\n"
        + "</body>\n"
        + "</html>";

    /// <summary>File name of the cover page created by <see cref="CreateHtmlCoverFile"/>.</summary>
    private const string HtmlCoverFilename = "cover.xhtml";

    /// <summary>Default cover page template (EPUB 2 — SVG in XHTML 1.1).</summary>
    private const string HtmlCoverSource =
        "<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"no\" ?>\n"
        + "<!DOCTYPE html PUBLIC \"-//W3C//DTD XHTML 1.1//EN\"\n"
        + "\"http://www.w3.org/TR/xhtml11/DTD/xhtml11.dtd\">\n"
        + "<html xmlns=\"http://www.w3.org/1999/xhtml\">\n"
        + "<head>\n"
        + "  <title>Cover</title>\n"
        + "</head>\n"
        + "<body>\n"
        + "  <div style=\"text-align: center; padding: 0pt; margin: 0pt;\">\n"
        + "    <svg xmlns=\"http://www.w3.org/2000/svg\" height=\"100%\" preserveAspectRatio=\"xMidYMid meet\" version=\"1.1\" viewBox=\"0 0 SGC_IMAGE_WIDTH SGC_IMAGE_HEIGHT\" width=\"100%\" xmlns:xlink=\"http://www.w3.org/1999/xlink\">\n"
        + "      <image width=\"SGC_IMAGE_WIDTH\" height=\"SGC_IMAGE_HEIGHT\" xlink:href=\"SGC_IMAGE_FILENAME\"/>\n"
        + "    </svg>\n"
        + "  </div>\n"
        + "</body>\n"
        + "</html>\n";

    /// <summary>Default cover page template (EPUB 3 — SVG in HTML5).</summary>
    private const string Html5CoverSource =
        "<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"no\" ?>\n"
        + "<!DOCTYPE html>\n\n"
        + "<html xmlns=\"http://www.w3.org/1999/xhtml\" xmlns:epub=\"http://www.idpf.org/2007/ops\">\n"
        + "<head>\n"
        + "  <title>Cover</title>\n"
        + "</head>\n"
        + "<body>\n"
        + "  <div style=\"height: 100vh; text-align: center; padding: 0pt; margin: 0pt;\">\n"
        + "    <svg xmlns=\"http://www.w3.org/2000/svg\" height=\"100%\" preserveAspectRatio=\"xMidYMid meet\" version=\"1.1\" viewBox=\"0 0 SGC_IMAGE_WIDTH SGC_IMAGE_HEIGHT\" width=\"100%\" xmlns:xlink=\"http://www.w3.org/1999/xlink\">\n"
        + "      <image width=\"SGC_IMAGE_WIDTH\" height=\"SGC_IMAGE_HEIGHT\" xlink:href=\"SGC_IMAGE_FILENAME\"/>\n"
        + "    </svg>\n"
        + "  </div>\n"
        + "</body>\n"
        + "</html>\n";

    private readonly FolderKeeper _folderKeeper;
    private readonly List<string> _loadWarnings = new();
    private bool _isModified;
    private bool _disposed;

    /// <summary>Creates a book around the given resource registry (which must already contain the OPF).</summary>
    /// <param name="folderKeeper">The resource registry with <see cref="FolderKeeper.Opf"/> already added.</param>
    public Book(FolderKeeper folderKeeper)
    {
        ArgumentNullException.ThrowIfNull(folderKeeper);
        _folderKeeper = folderKeeper;
    }

    /// <summary>Raised when the "modified" state (<see cref="Modified"/>) changes.</summary>
    public event EventHandler<bool>? ModifiedStateChanged;

    /// <summary>The registry of all the book's resources.</summary>
    public FolderKeeper GetFolderKeeper() => _folderKeeper;

    /// <summary>The EPUB version (<c>2.0</c> / <c>3.0</c>). Taken from <see cref="OpfResource"/> by default.</summary>
    public string EpubVersion { get; set; } = "2.0";

    /// <summary>Whether the publication is EPUB 3 (or higher).</summary>
    public bool IsEpub3 => EpubVersion.StartsWith('3');

    /// <summary>Whether the book has unsaved changes (import sets <c>true</c> when there were warnings/repairs).</summary>
    public bool Modified
    {
        get => _isModified;
        set
        {
            if (_isModified == value)
            {
                return;
            }

            _isModified = value;
            ModifiedStateChanged?.Invoke(this, value);
        }
    }

    /// <summary>Warnings collected while loading the book (to be shown to the user).</summary>
    public IReadOnlyList<string> LoadWarnings => _loadWarnings;

    /// <summary>The book's OPF document.</summary>
    /// <exception cref="InvalidOperationException">The registry has no OPF yet.</exception>
    public OpfResource GetOpf() =>
        _folderKeeper.Opf ?? throw new InvalidOperationException(CoreStrings.Get("Error_BookHasNoOpf"));

    /// <summary>The book's NCX resource or <c>null</c> (EPUB 3 may not have one).</summary>
    public NcxResource? GetNcx() => _folderKeeper.Ncx;

    /// <summary>All the book's resources.</summary>
    public IReadOnlyList<Resource> GetAllResources() => _folderKeeper.GetResourceList();

    /// <summary>HTML resources in spine order (files outside the spine last).</summary>
    public IReadOnlyList<HtmlResource> GetHtmlResources() =>
        _folderKeeper.GetResourceTypeList<HtmlResource>(sorted: true);

    /// <summary>The Dublin Core elements from the OPF metadata.</summary>
    public IReadOnlyList<MetaEntry> GetMetadata() => GetOpf().GetDcMetadata();

    /// <summary>The values of a specific DC element (e.g. <c>dc:creator</c>).</summary>
    public IReadOnlyList<string> GetMetadataValues(string dcName) => GetOpf().GetDcMetadataValues(dcName);

    /// <summary>Overwrites the Dublin Core elements and marks the book as modified.</summary>
    public void SetMetadata(IEnumerable<MetaEntry> metadata)
    {
        ArgumentNullException.ThrowIfNull(metadata);
        GetOpf().SetDcMetadata(metadata);
        Modified = true;
    }

    /// <summary>
    /// Creates a single empty XHTML file in the given folder of the Text group with the default template
    /// (<see cref="EmptyHtmlFile"/> / <see cref="EmptyHtml5File"/> per <see cref="EpubVersion"/>)
    /// and adds it to the manifest and spine (user templates from the preferences folder are not supported).
    /// </summary>
    /// <param name="folderPath">The target folder (bookpath); <c>"\\"</c> = the default folder of the <c>Text</c> group.</param>
    /// <returns>The newly created HTML resource.</returns>
    public HtmlResource CreateEmptyHtmlFile(string folderPath = "\\")
    {
        string data = EpubVersion.StartsWith('3') ? EmptyHtml5File : EmptyHtmlFile;

        string tempDir = SysPath.Combine(SysPath.GetTempPath(), "signet-html-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempDir);
        string tempPath = SysPath.Combine(tempDir, "Section0001.xhtml");
        Utility.WriteUnicodeTextFile(PlaceholderXhtml, tempPath);

        Resource resource;
        try
        {
            resource = _folderKeeper.AddContentFileToFolder(
                tempPath,
                updateOpf: true,
                "application/xhtml+xml",
                bookPath: null,
                folderPath: folderPath);
        }
        finally
        {
            try
            {
                Directory.Delete(tempDir, recursive: true);
            }
            catch (IOException)
            {
                // best-effort cleanup
            }
        }

        if (resource is not HtmlResource html)
        {
            throw new InvalidOperationException(CoreStrings.Format("Error_NewResourceWrongType", "XHTML"));
        }

        html.EpubVersion = EpubVersion;
        html.SetText(data);
        html.SaveToDisk();
        Modified = true;
        return html;
    }

    /// <summary>
    /// Creates an empty CSS file (<c>Style0001.css</c>) in the given folder of the Styles group and adds it
    /// to the manifest (user templates from the preferences folder are not supported).
    /// </summary>
    /// <param name="folderPath">The target folder (bookpath); <c>"\\"</c> = the default folder of the <c>Styles</c> group.</param>
    /// <returns>The newly created CSS resource.</returns>
    public CssResource CreateEmptyCssFile(string folderPath = "\\")
    {
        Resource resource = AddEmptyTempFile("Style0001.css", "text/css", folderPath);
        if (resource is not CssResource css)
        {
            throw new InvalidOperationException(CoreStrings.Format("Error_NewResourceWrongType", "CSS"));
        }

        Modified = true;
        return css;
    }

    /// <summary>
    /// Creates an empty JavaScript file (<c>Script0001.js</c>) in the given folder of the Misc group and adds
    /// it to the manifest.
    /// </summary>
    /// <param name="folderPath">The target folder (bookpath); <c>"\\"</c> = the default folder of the Misc group.</param>
    /// <returns>The newly created text resource.</returns>
    public MiscTextResource CreateEmptyJsFile(string folderPath = "\\")
    {
        Resource resource = AddEmptyTempFile("Script0001.js", "application/javascript", folderPath);
        if (resource is not MiscTextResource js)
        {
            throw new InvalidOperationException(CoreStrings.Format("Error_NewResourceWrongType", "JS"));
        }

        Modified = true;
        return js;
    }

    /// <summary>
    /// Creates an empty SVG file (<c>Image0001.svg</c>) in the given folder of the Images group and adds it
    /// to the manifest.
    /// </summary>
    /// <param name="folderPath">The target folder (bookpath); <c>"\\"</c> = the default folder of the Images group.</param>
    /// <returns>The newly created SVG resource.</returns>
    public SvgResource CreateEmptySvgFile(string folderPath = "\\")
    {
        Resource resource = AddEmptyTempFile("Image0001.svg", "image/svg+xml", folderPath);
        if (resource is not SvgResource svg)
        {
            throw new InvalidOperationException(CoreStrings.Format("Error_NewResourceWrongType", "SVG"));
        }

        Modified = true;
        return svg;
    }

    /// <summary>
    /// Copies the given files from disk into the book (type auto-detected from the extension, name
    /// collisions avoided) and adds them to the manifest. File selection, the duplicate-replacement dialog
    /// and progress reporting belong to the App layer — this only copies a ready list of paths.
    /// </summary>
    /// <param name="fullFilePaths">Full paths of the source files on disk.</param>
    /// <returns>The newly created resources, in the order of the given paths.</returns>
    public IReadOnlyList<Resource> AddExistingFiles(IEnumerable<string> fullFilePaths)
    {
        ArgumentNullException.ThrowIfNull(fullFilePaths);
        List<Resource> added = new();
        foreach (string path in fullFilePaths)
        {
            added.Add(_folderKeeper.AddContentFileToFolder(path));
        }

        if (added.Count > 0)
        {
            Modified = true;
        }

        return added;
    }

    /// <summary>
    /// Creates a temporary empty file named <paramref name="firstName"/> (made unique among the
    /// existing names) and adds it to the book. The shared tail of <c>CreateEmpty{Css,Js,Svg}File</c>.
    /// </summary>
    private Resource AddEmptyTempFile(string firstName, string mimeType, string folderPath)
    {
        string tempDir = SysPath.Combine(SysPath.GetTempPath(), "signet-new-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempDir);
        string tempPath = SysPath.Combine(tempDir, _folderKeeper.GetUniqueFilenameVersion(firstName));
        Utility.WriteUnicodeTextFile(string.Empty, tempPath);

        try
        {
            return _folderKeeper.AddContentFileToFolder(
                tempPath, updateOpf: true, mimeType, bookPath: null, folderPath: folderPath);
        }
        finally
        {
            try
            {
                Directory.Delete(tempDir, recursive: true);
            }
            catch (IOException)
            {
                // best-effort cleanup
            }
        }
    }

    /// <summary>
    /// Creates an empty EPUB 3 navigation document (<c>nav.xhtml</c>) with the default template
    /// (<c>toc</c> + <c>landmarks</c> sections) and, when <paramref name="updateOpf"/> is set, adds it
    /// to the manifest with the <c>nav</c> property (the nav CSS stylesheet is not linked).
    /// </summary>
    /// <param name="updateOpf">Whether to add an entry to the OPF manifest.</param>
    /// <param name="folderPath">The target folder (bookpath); <c>"\\"</c> = the default folder of the <c>Text</c> group.</param>
    public HtmlResource CreateEmptyNavFile(bool updateOpf = false, string folderPath = "\\")
    {
        string navName = _folderKeeper.GetUniqueFilenameVersion("nav.xhtml");
        string tempPath = SysPath.Combine(
            SysPath.GetTempPath(), "signet-nav-" + Guid.NewGuid().ToString("N") + ".xhtml");
        Utility.WriteUnicodeTextFile(PlaceholderXhtml, tempPath);

        Resource resource;
        try
        {
            resource = _folderKeeper.AddContentFileToFolder(
                tempPath,
                updateOpf,
                "application/xhtml+xml",
                bookPath: null,
                folderPath: folderPath);
        }
        finally
        {
            if (File.Exists(tempPath))
            {
                File.Delete(tempPath);
            }
        }

        if (resource is not HtmlResource nav)
        {
            throw new InvalidOperationException(CoreStrings.Format("Error_NewResourceWrongType", "nav"));
        }

        if (navName != nav.Filename && !_folderKeeper.GetAllFilenames().Contains(navName))
        {
            nav.RenameTo(navName);
        }

        // Empty text -> the NavProcessor constructor fills in the default nav template.
        _ = new NavProcessor(nav);
        nav.EpubVersion = EpubVersion;
        nav.SaveToDisk();

        if (updateOpf)
        {
            MarkManifestItemAsNav(nav);
        }

        Modified = true;
        return nav;
    }

    /// <summary>
    /// The HTML resource of the EPUB 3 navigation document or <c>null</c> (EPUB 2 or no nav in the manifest).
    /// Resolves the bookpath from
    /// <see cref="Resources.OpfResource.GetNavResourceBookPath"/> to the actual resource.
    /// </summary>
    public HtmlResource? GetNavResource()
    {
        string navBookPath = GetOpf().GetNavResourceBookPath();
        return navBookPath.Length == 0
            ? null
            : _folderKeeper.GetResourceByBookPathNoThrow(navBookPath) as HtmlResource;
    }

    /// <summary>
    /// The hierarchical table of contents to <b>display</b> in the "Table Of Contents" panel:
    /// taken from the nav document (EPUB 3) or, if absent, from the NCX (EPUB 2). Navigation targets are resolved to
    /// absolute bookpaths. An empty list when the publication has neither a nav nor an NCX with entries.
    /// </summary>
    public IReadOnlyList<TocDisplayEntry> GetTocForDisplay()
    {
        if (GetNavResource() is { } nav)
        {
            string navFolder = Core.BookPath.StartingDir(nav.BookPath);
            IReadOnlyList<Toc.NavTocEntry> tree = new Toc.NavProcessor(nav).GetTocTree();
            if (tree.Count > 0)
            {
                return tree.Select(e => FromNavEntry(e, navFolder)).ToList();
            }
        }

        if (GetNcx() is { } ncx)
        {
            string ncxFolder = Core.BookPath.StartingDir(ncx.BookPath);
            Toc.NcxDocument doc = ncx.GetNcxDocument();
            if (doc.NavMap.Count > 0)
            {
                return doc.NavMap.Select(p => FromNcxPoint(p, ncxFolder)).ToList();
            }
        }

        return Array.Empty<TocDisplayEntry>();
    }

    private static TocDisplayEntry FromNavEntry(Toc.NavTocEntry entry, string navFolder)
    {
        (string bookPath, string fragment) = ResolveTocHref(entry.Href, navFolder);
        return new TocDisplayEntry
        {
            Title = entry.Title,
            TargetBookPath = bookPath,
            Fragment = fragment,
            Children = entry.Children.Select(c => FromNavEntry(c, navFolder)).ToList(),
        };
    }

    private static TocDisplayEntry FromNcxPoint(Toc.NcxNavPoint point, string ncxFolder)
    {
        (string bookPath, string fragment) = ResolveTocHref(point.ContentSrc, ncxFolder);
        return new TocDisplayEntry
        {
            Title = point.Label,
            TargetBookPath = bookPath,
            Fragment = fragment,
            Children = point.Children.Select(c => FromNcxPoint(c, ncxFolder)).ToList(),
        };
    }

    // Resolves an href from the nav/NCX (relative to that document's folder) to (absolute bookpath, fragment without '#').
    private static (string BookPath, string Fragment) ResolveTocHref(string href, string sourceFolder)
    {
        if (string.IsNullOrWhiteSpace(href))
        {
            return (string.Empty, string.Empty);
        }

        string path = href;
        string fragment = string.Empty;
        int hash = href.IndexOf('#', StringComparison.Ordinal);
        if (hash >= 0)
        {
            path = href[..hash];
            fragment = href[(hash + 1)..];
        }

        string bookPath = path.Length == 0
            ? string.Empty
            : Core.BookPath.BuildBookPath(Uri.UnescapeDataString(Utility.DecodeXml(path)), sourceFolder);
        return (bookPath, fragment);
    }

    /// <summary>Whether the nav document is listed in the spine (always <c>false</c> for EPUB 2).</summary>
    public bool IsNavInSpine =>
        IsEpub3 && GetNavResource() is { } nav && GetOpf().GetReadingOrder(nav) != -1;

    /// <summary>
    /// HTML resources in spine order, excluding the nav document (EPUB 3). Used by TOC generation
    /// from headings — the nav is not a source of headings.
    /// </summary>
    public IReadOnlyList<HtmlResource> GetHtmlResourcesExcludingNav()
    {
        List<HtmlResource> list = GetHtmlResources().ToList();
        if (GetNavResource() is { } nav)
        {
            list.Remove(nav);
        }

        return list;
    }

    /// <summary>
    /// All identifiers (<c>#fragment</c>) referenced by any <c>href</c>/<c>src</c>
    /// in the book's HTML files. They act as a "do not touch this id" list when heading ids are auto-generated.
    /// </summary>
    public IReadOnlyList<string> GetIdsInHrefs()
    {
        List<string> ids = new();
        HashSet<string> seen = new(StringComparer.Ordinal);
        foreach (HtmlResource html in _folderKeeper.GetResourceTypeList<HtmlResource>(sorted: false))
        {
            foreach (string fragment in XhtmlDoc.GetFragmentTargetsInHrefs(html.GetText()))
            {
                if (seen.Add(fragment))
                {
                    ids.Add(fragment);
                }
            }
        }

        return ids;
    }

    /// <summary>
    /// Creates the table-of-contents CSS file (<c>sgc-toc.css</c>) with the default content (<see cref="Toc.TocHtmlWriter.SgcTocCss"/>)
    /// and adds it to the manifest.
    /// </summary>
    public CssResource CreateHtmlTocCssFile()
    {
        CssResource css = CreateEmptyCssFile();
        css.RenameTo(Toc.TocHtmlWriter.SgcTocCssFilename);
        css.SetText(Toc.TocHtmlWriter.SgcTocCss);
        css.SaveToDisk();
        Modified = true;
        return css;
    }

    /// <summary>
    /// The existing cover page: an HTML resource with the guide/landmark <c>cover</c> semantics or, failing that,
    /// the first resource named <see cref="HtmlCoverFilename"/>. <c>null</c> when there is none.
    /// </summary>
    public HtmlResource? FindExistingCoverHtmlResource()
    {
        HtmlResource? navResource = IsEpub3 ? GetNavResource() : null;
        NavProcessor? navProcessor = navResource is not null ? new NavProcessor(navResource) : null;
        HtmlResource? byFilename = null;

        foreach (HtmlResource html in GetHtmlResources())
        {
            string code = navProcessor is not null
                ? navProcessor.GetLandmarkCodeForResource(html)
                : GetOpf().GetGuideSemanticCodeForResource(html);
            if (string.Equals(code, "cover", StringComparison.Ordinal))
            {
                return html;
            }

            if (byFilename is null && string.Equals(html.Filename, HtmlCoverFilename, StringComparison.OrdinalIgnoreCase))
            {
                byFilename = html;
            }
        }

        return byFilename;
    }

    /// <summary>
    /// Creates a new <c>cover.xhtml</c> page (the SVG template per <see cref="EpubVersion"/> when
    /// <paramref name="text"/> is empty) and moves it to the start of the spine (skipping the nav
    /// when it is not in the spine). Assumes no resource with that name exists yet —
    /// the caller (e.g. <see cref="SetCoverImage"/>) should check
    /// <see cref="FindExistingCoverHtmlResource"/> first.
    /// </summary>
    /// <param name="text">The page content; when empty — the default template.</param>
    public HtmlResource CreateHtmlCoverFile(string? text = null)
    {
        HtmlResource html = CreateEmptyHtmlFile();
        html.RenameTo(HtmlCoverFilename);

        string data = string.IsNullOrEmpty(text) ? (IsEpub3 ? Html5CoverSource : HtmlCoverSource) : text;
        html.SetText(data);
        html.SaveToDisk();

        List<HtmlResource> htmlResources = GetHtmlResources().ToList();
        if (IsEpub3 && GetNavResource() is { } nav && !IsNavInSpine)
        {
            htmlResources.Remove(nav);
        }

        htmlResources.Remove(html);
        htmlResources.Insert(0, html);
        GetOpf().UpdateSpineOrder(htmlResources);

        Modified = true;
        return html;
    }

    /// <summary>
    /// Marks an image as the cover: creates (or overwrites <paramref name="existingCoverHtml"/>) a
    /// <c>cover.xhtml</c> page with an embedded SVG, sets the <c>cover</c> semantics (guide — EPUB 2, landmark —
    /// EPUB 3), marks the image in the manifest (<see cref="Resources.OpfResource.SetResourceAsCoverImage"/>)
    /// and substitutes the image name/dimensions in the template. Picking a file
    /// from disk, the overwrite dialog and tab refreshing are left to the UI layer.
    /// </summary>
    /// <param name="image">The image to mark as the cover.</param>
    /// <param name="existingCoverHtml">
    /// The existing cover page to overwrite (from <see cref="FindExistingCoverHtmlResource"/>) or
    /// <c>null</c> to create a new <c>cover.xhtml</c>.
    /// </param>
    public HtmlResource SetCoverImage(ImageResource image, HtmlResource? existingCoverHtml = null)
    {
        ArgumentNullException.ThrowIfNull(image);

        string template = IsEpub3 ? Html5CoverSource : HtmlCoverSource;
        HtmlResource html;
        if (existingCoverHtml is not null)
        {
            html = existingCoverHtml;
            html.SetText(template);
            html.SaveToDisk();
        }
        else
        {
            html = CreateHtmlCoverFile(template);
        }

        if (IsEpub3)
        {
            HtmlResource nav = GetNavResource()
                ?? throw new InvalidOperationException(CoreStrings.Get("Error_Epub3WithoutNav"));
            new NavProcessor(nav).AddLandmarkCode(html, "cover", toggle: false);
        }
        else
        {
            GetOpf().AddGuideSemanticCode(html, "cover", toggle: false);
        }

        if (!GetOpf().IsCoverImage(image))
        {
            GetOpf().SetResourceAsCoverImage(image);
        }

        string imageRelativePath = Utility.UrlEncodePath(Core.BookPath.Relative(html.BookPath, image.BookPath));
        (int width, int height) = image.GetDimensions();
        string text = html.GetText()
            .Replace("SGC_IMAGE_FILENAME", imageRelativePath, StringComparison.Ordinal)
            .Replace("SGC_IMAGE_WIDTH", width.ToString(CultureInfo.InvariantCulture), StringComparison.Ordinal)
            .Replace("SGC_IMAGE_HEIGHT", height.ToString(CultureInfo.InvariantCulture), StringComparison.Ordinal);
        html.SetText(text);
        html.SaveToDisk();

        if (IsEpub3)
        {
            GetOpf().UpdateManifestProperties(new Resource[] { html });
        }

        Modified = true;
        return html;
    }

    /// <summary>All CSS resources in file name order.</summary>
    public IReadOnlyList<CssResource> GetCssResources() =>
        _folderKeeper.GetResourceTypeList<CssResource>(sorted: true);

    /// <summary>
    /// Resources treated as JavaScript scripts (media type <c>application/javascript</c> /
    /// <c>application/ecmascript</c> / <c>text/javascript</c>).
    /// </summary>
    public IReadOnlyList<Resource> GetJavascriptResources() =>
        _folderKeeper.GetResourceListByMediaTypes(JavascriptMediaTypes);

    /// <summary>
    /// All media resources (images, SVG, video, audio) in the order: images, SVG, video,
    /// audio — each group sorted by file name (used by "Insert File" and "Insert Hyperlink").
    /// </summary>
    public IReadOnlyList<Resource> GetMediaResources()
    {
        var resources = new List<Resource>();
        resources.AddRange(_folderKeeper.GetResourceTypeList<ImageResource>(sorted: true));
        resources.AddRange(_folderKeeper.GetResourceTypeList<SvgResource>(sorted: true));
        resources.AddRange(_folderKeeper.GetResourceTypeList<VideoResource>(sorted: true));
        resources.AddRange(_folderKeeper.GetResourceTypeList<AudioResource>(sorted: true));
        return resources;
    }

    /// <summary>
    /// All values of the <c>id</c> attribute (and of old <c>&lt;a name&gt;</c> anchors) in the given
    /// (X)HTML file.
    /// </summary>
    public IReadOnlyList<string> GetIdsInHtmlFile(HtmlResource htmlResource)
    {
        ArgumentNullException.ThrowIfNull(htmlResource);
        return XhtmlDoc.GetAllDescendantIds(htmlResource.GetText());
    }

    /// <summary>
    /// A map of <c>bookpath &#8594; list of identifiers</c> for all the book's (X)HTML files —
    /// for the "Insert Hyperlink" dialog.
    /// </summary>
    public IReadOnlyDictionary<string, IReadOnlyList<string>> GetIdsInHtmlFiles()
    {
        var map = new Dictionary<string, IReadOnlyList<string>>(StringComparer.Ordinal);
        foreach (HtmlResource html in GetHtmlResources())
        {
            map[html.BookPath] = XhtmlDoc.GetAllDescendantIds(html.GetText());
        }

        return map;
    }

    /// <summary>
    /// A map of "which CSS stylesheets are linked to the selected (X)HTML files". A stylesheet is marked as
    /// included only when it appears (in the same order) in <b>every</b> one of <paramref name="resources"/>; the remaining
    /// stylesheets of the book go to the end of the list as not included.
    /// </summary>
    public IReadOnlyList<LinkableResourceEntry> GetStylesheetsMap(IReadOnlyList<HtmlResource> resources)
    {
        ArgumentNullException.ThrowIfNull(resources);
        return BuildLinkableMap(
            resources,
            GetCssResources().Select(css => css.BookPath).ToList(),
            html => html.GetLinkedStylesheets());
    }

    /// <summary>
    /// A map of "which scripts are linked to the selected (X)HTML files"
    /// (the same semantics as <see cref="GetStylesheetsMap"/>).
    /// </summary>
    public IReadOnlyList<LinkableResourceEntry> GetJavascriptsMap(IReadOnlyList<HtmlResource> resources)
    {
        ArgumentNullException.ThrowIfNull(resources);
        return BuildLinkableMap(
            resources,
            GetJavascriptResources().Select(js => js.BookPath).ToList(),
            html => html.GetLinkedJavascripts());
    }

    /// <summary>
    /// Re-links the stylesheets in <paramref name="resources"/> to exactly
    /// <paramref name="stylesheetBookPaths"/> (order preserved). When any of the files is not well-formed,
    /// nothing is changed and the result points at that file.
    /// </summary>
    public LinkResourcesResult LinkStylesheetsToResources(
        IReadOnlyList<HtmlResource> resources,
        IReadOnlyList<string> stylesheetBookPaths)
    {
        ArgumentNullException.ThrowIfNull(resources);
        ArgumentNullException.ThrowIfNull(stylesheetBookPaths);

        if (FindFirstNotWellFormed(resources) is { } bad)
        {
            return new LinkResourcesResult(false, bad);
        }

        foreach (HtmlResource html in resources)
        {
            string updated = LinkStylesheetsUpdate.Apply(
                html.GetText(), html.BookPath, stylesheetBookPaths, html.EpubVersion);
            html.SetText(updated);
            html.SaveToDisk();
        }

        if (resources.Count > 0)
        {
            Modified = true;
        }

        return new LinkResourcesResult(true, null);
    }

    /// <summary>
    /// Re-links the scripts in <paramref name="resources"/> to exactly
    /// <paramref name="javascriptBookPaths"/>.
    /// After the change it refreshes the manifest properties (adding <c>scripted</c>).
    /// </summary>
    public LinkResourcesResult LinkJavascriptsToResources(
        IReadOnlyList<HtmlResource> resources,
        IReadOnlyList<string> javascriptBookPaths)
    {
        ArgumentNullException.ThrowIfNull(resources);
        ArgumentNullException.ThrowIfNull(javascriptBookPaths);

        if (FindFirstNotWellFormed(resources) is { } bad)
        {
            return new LinkResourcesResult(false, bad);
        }

        foreach (HtmlResource html in resources)
        {
            string updated = LinkJavascriptsUpdate.Apply(
                html.GetText(), html.BookPath, javascriptBookPaths, html.EpubVersion);
            html.SetText(updated);
            html.SaveToDisk();
        }

        if (resources.Count > 0)
        {
            GetOpf().UpdateManifestProperties(resources);
            Modified = true;
        }

        return new LinkResourcesResult(true, null);
    }

    /// <summary>
    /// CSS stylesheets from the manifest that no XHTML file uses — neither links nor imports (directly, through an
    /// <c>@import</c> chain or from a <c>&lt;style&gt;</c> block, <see cref="GetVisibleStylesheets(HtmlResource)"/>) —
    /// the "unreferenced stylesheets" step of the Cleanup (<see cref="CleanupAnalysis"/>). Inline <c>&lt;style&gt;</c> blocks are not
    /// covered (they are not separate resources).
    /// </summary>
    public IReadOnlyList<CssResource> FindUnusedStylesheets()
    {
        IReadOnlyList<CssResource> cssResources = GetCssResources();
        HashSet<string> referenced = new(StringComparer.Ordinal);
        foreach (HtmlResource html in GetHtmlResources())
        {
            foreach (string cssBookPath in VisibleStylesheetsOf(html, cssResources))
            {
                referenced.Add(cssBookPath);
            }
        }

        return cssResources.Where(css => !referenced.Contains(css.BookPath)).ToList();
    }

    /// <summary>
    /// The stylesheets <paramref name="html"/> actually uses, in cascade order: the linked ones and the ones imported
    /// (<c>@import</c>, transitively) by them or by its <c>&lt;style&gt;</c> blocks (<see cref="CssImports"/>).
    /// </summary>
    public IReadOnlyList<string> GetVisibleStylesheets(HtmlResource html) => VisibleStylesheetsOf(html, GetCssResources());

    private static IReadOnlyList<string> VisibleStylesheetsOf(HtmlResource html, IReadOnlyList<CssResource> cssResources)
    {
        Dictionary<string, CssResource> byPath = cssResources.ToDictionary(c => c.BookPath, StringComparer.Ordinal);
        IEnumerable<string> roots = html.GetLinkedStylesheets()
            .Concat(CssImports.FromStyleBlocks(html.GetText(), html.BookPath));
        return CssImports.VisibleStylesheets(
            roots,
            bookPath => byPath.TryGetValue(bookPath, out CssResource? css)
                ? CssImports.Parse(css.GetText(), Core.BookPath.StartingDir(css.BookPath))
                : null);
    }

    /// <summary>
    /// Applies a <see cref="CleanupPlan"/> computed by <see cref="CleanupAnalysis.Plan"/> for this book: writes the
    /// new texts of the changed CSS/XHTML files and removes the stylesheets and media files to delete. Returns
    /// <c>true</c> when anything was changed.
    /// </summary>
    public bool ApplyCleanup(CleanupPlan plan)
    {
        ArgumentNullException.ThrowIfNull(plan);
        if (!plan.HasChanges)
        {
            return false;
        }

        foreach ((string bookPath, string text) in plan.NewTexts)
        {
            if (_folderKeeper.GetResourceByBookPathNoThrow(bookPath) is TextResource resource)
            {
                resource.SetText(text);
            }
        }

        if (plan.ResourcesToDelete.Count > 0)
        {
            _folderKeeper.BulkRemoveResources(plan.ResourcesToDelete);
        }

        Modified = true;
        return true;
    }

    /// <summary>
    /// "Mend All HTML Files" — repairs the structure of every (X)HTML file via <see cref="CleanSource.Mend"/>,
    /// without formatting. No well-formed guard (repairing files that are not well-formed is exactly what Mend is for).
    /// </summary>
    /// <param name="entityOverrides">
    /// An optional character → entity text map — "Preserve Entities" from Preferences.
    /// </param>
    /// <param name="addMissingDoctype">Whether to add a DOCTYPE to files that have none.</param>
    /// <returns><c>true</c> when at least one file was changed.</returns>
    public bool MendAllHtml(IReadOnlyDictionary<char, string>? entityOverrides = null, bool addMissingDoctype = false)
    {
        bool modified = false;
        foreach (HtmlResource html in GetHtmlResources())
        {
            string source = html.GetText();
            string version = html.EpubVersion.Length > 0 ? html.EpubVersion : EpubVersion;
            string newSource = CleanSource.Mend(source, version, entityOverrides, addMissingDoctype);
            if (!string.Equals(newSource, source, StringComparison.Ordinal))
            {
                html.SetText(newSource);
                modified = true;
            }
        }

        if (modified)
        {
            Modified = true;
        }

        return modified;
    }

    /// <summary>
    /// "Mend Code" for a single file from the Code View context menu — repairs the editor text
    /// <paramref name="text"/> of the resource <paramref name="html"/>.
    /// </summary>
    public string MendHtmlText(
        HtmlResource html,
        string text,
        IReadOnlyDictionary<char, string>? entityOverrides = null,
        bool addMissingDoctype = false)
    {
        ArgumentNullException.ThrowIfNull(html);
        ArgumentNullException.ThrowIfNull(text);
        return CleanSource.Mend(text, VersionOf(html), entityOverrides, addMissingDoctype);
    }

    /// <summary>
    /// "Mend and Prettify Code" for a single file — pretty-prints the editor text with a
    /// well-formed guard. Returns <c>null</c> when the text is not well-formed (operation cancelled).
    /// </summary>
    public string? SafePrettyPrintHtmlText(
        HtmlResource html,
        string text,
        IReadOnlyDictionary<char, string>? entityOverrides = null,
        bool addMissingDoctype = false)
    {
        ArgumentNullException.ThrowIfNull(html);
        ArgumentNullException.ThrowIfNull(text);
        string version = VersionOf(html);
        if (!WellFormedChecker.CheckXhtmlStructure(text, version).IsWellFormed)
        {
            return null;
        }

        bool keepWhitespace = XhtmlUsesStyleProperty(html, "white-space")
            || XhtmlUsesStyleProperty(html, "white-space-collapse");
        return CleanSource.PrettyPrint(
            text,
            keepWhitespace,
            version,
            options: null,
            entityOverrides: entityOverrides,
            props: PrettyPrintProps.LoadUserPrefs(),
            addMissingDoctype: addMissingDoctype);
    }

    /// <summary>
    /// Prepares "Rename Class" from Code View: a <see cref="ClassRenamer"/>
    /// over the current texts of all XHTML and CSS files. Like other whole-book operations,
    /// it refuses when any XHTML file is not well-formed.
    /// </summary>
    public ClassRenamePreparation PrepareClassRename()
    {
        if (FindFirstNotWellFormed(GetHtmlResources()) is { } bad)
        {
            return new ClassRenamePreparation(null, bad);
        }

        ClassRenamer renamer = new(
            GetHtmlResources().Select(h => new ClassRenameSource(h.BookPath, h.GetText())),
            GetCssResources().Select(c => new ClassRenameSource(c.BookPath, c.GetText())));
        return new ClassRenamePreparation(renamer, null);
    }

    /// <summary>
    /// "Find Usages" of a CSS class (<see cref="ClassUsageFinder"/>) in the stylesheets and the markup files ((X)HTML,
    /// SVG, XML), in the order of the Book Browser. The lenient tag scanner needs no well-formed guard.
    /// </summary>
    public IReadOnlyList<ClassUsage> FindClassUsages(string className)
    {
        ArgumentException.ThrowIfNullOrEmpty(className);
        using MainUI.OpfModel browserOrder = new(this);
        List<ClassUsageSource> sources = new();
        foreach (MainUI.OpfModelEntry entry in browserOrder.AllEntries())
        {
            if (entry.Resource is not TextResource text)
            {
                continue;
            }

            if (text.Type == ResourceType.Css)
            {
                sources.Add(new ClassUsageSource(text.BookPath, text.GetText(), IsStyleSheet: true));
            }
            else if (text.Type is ResourceType.Html or ResourceType.Svg or ResourceType.Xml)
            {
                sources.Add(new ClassUsageSource(text.BookPath, text.GetText(), IsStyleSheet: false));
            }
        }

        return ClassUsageFinder.Find(className, sources);
    }

    /// <summary>Stores the file texts computed by <see cref="ClassRenamer.Rename"/>. Returns whether anything changed.</summary>
    public bool ApplyClassRename(ClassRenameResult result)
    {
        ArgumentNullException.ThrowIfNull(result);
        bool modified = false;
        foreach ((string bookPath, string text) in result.ChangedTexts)
        {
            if (_folderKeeper.GetResourceByBookPathNoThrow(bookPath) is TextResource resource
                && !string.Equals(resource.GetText(), text, StringComparison.Ordinal))
            {
                resource.SetText(text);
                modified = true;
            }
        }

        if (modified)
        {
            Modified = true;
        }

        return modified;
    }

    private string VersionOf(HtmlResource html) => html.EpubVersion.Length > 0 ? html.EpubVersion : EpubVersion;

    /// <summary>
    /// "Mend &amp; Prettify All HTML Files" — reformats every (X)HTML file via
    /// <see cref="CleanSource.PrettyPrint(string, bool, string, PrettyPrintOptions?, IReadOnlyDictionary{char, string}?)"/> (without a prior Mend — this option
    /// deliberately does <em>not</em> repair the structure, it only pretty-prints).
    /// A well-formed guard runs on all files before any change. Preserves whitespace
    /// (<c>keep_whitespace</c>) when the file or any linked CSS stylesheet uses the
    /// <c>white-space</c>/<c>white-space-collapse</c> property.
    /// </summary>
    /// <param name="entityOverrides">
    /// An optional character → entity text map — "Preserve Entities" from Preferences.
    /// </param>
    /// <param name="addMissingDoctype">Whether to add a DOCTYPE to files that have none.</param>
    public MaintenanceOperationResult PrettyPrintAllHtml(
        IReadOnlyDictionary<char, string>? entityOverrides = null,
        bool addMissingDoctype = false)
    {
        if (FindFirstNotWellFormed(GetHtmlResources()) is { } bad)
        {
            return new MaintenanceOperationResult(false, bad);
        }

        // Tag classification and indent_string/singlespace are loaded once per call
        // from prettyprint.xml in the user's prefs folder (if missing — created with the default
        // content and the built-in values are used).
        PrettyPrintProps props = PrettyPrintProps.LoadUserPrefs();

        bool modified = false;
        foreach (HtmlResource html in GetHtmlResources())
        {
            string original = html.GetText();
            bool keepWhitespace = XhtmlUsesStyleProperty(html, "white-space")
                || XhtmlUsesStyleProperty(html, "white-space-collapse");
            string version = html.EpubVersion.Length > 0 ? html.EpubVersion : EpubVersion;
            string newSource = CleanSource.PrettyPrint(
                original,
                keepWhitespace,
                version,
                options: null,
                entityOverrides: entityOverrides,
                props: props,
                addMissingDoctype: addMissingDoctype);
            if (!string.Equals(newSource, original, StringComparison.Ordinal))
            {
                html.SetText(newSource);
                modified = true;
            }
        }

        if (modified)
        {
            Modified = true;
        }

        return MaintenanceOperationResult.Ok;
    }

    /// <summary>
    /// "Add soft hyphens" — inserts soft hyphens via <see cref="SoftHyphenInserter"/>
    /// in the content of all the book's HTML resources. A well-formed guard runs on all files before
    /// any change, like other operations that tidy up the whole book.
    /// </summary>
    public MaintenanceOperationResult AddSoftHyphens()
    {
        if (FindFirstNotWellFormed(GetHtmlResources()) is { } bad)
        {
            return new MaintenanceOperationResult(false, bad);
        }

        bool modified = false;
        foreach (HtmlResource html in GetHtmlResources())
        {
            string original = html.GetText();
            string newSource = SoftHyphenInserter.InsertSoftHyphens(original);
            if (!string.Equals(newSource, original, StringComparison.Ordinal))
            {
                html.SetText(newSource);
                modified = true;
            }
        }

        if (modified)
        {
            Modified = true;
        }

        return MaintenanceOperationResult.Ok;
    }

    /// <summary>
    /// "Remove soft hyphens" — the inverse of <see cref="AddSoftHyphens"/>,
    /// removes soft hyphens via <see cref="SoftHyphenInserter.RemoveSoftHyphens"/> from all the book's HTML resources.
    /// A well-formed guard runs on all files before any change, like other operations
    /// that tidy up the whole book.
    /// </summary>
    public MaintenanceOperationResult RemoveSoftHyphens()
    {
        if (FindFirstNotWellFormed(GetHtmlResources()) is { } bad)
        {
            return new MaintenanceOperationResult(false, bad);
        }

        bool modified = false;
        foreach (HtmlResource html in GetHtmlResources())
        {
            string original = html.GetText();
            string newSource = SoftHyphenInserter.RemoveSoftHyphens(original);
            if (!string.Equals(newSource, original, StringComparison.Ordinal))
            {
                html.SetText(newSource);
                modified = true;
            }
        }

        if (modified)
        {
            Modified = true;
        }

        return MaintenanceOperationResult.Ok;
    }

    /// <summary>
    /// "Restructure Epub to Signet Norm" — irreversibly normalizes the book to the Signet layout:
    /// <c>content.opf</c>/<c>toc.ncx</c> naming, resolving file name collisions (case-insensitive)
    /// across the whole book, and then moving every resource to its standard folder
    /// (<see cref="BookManipulation.FolderKeeper.GetStdFolderForGroup"/>, e.g.
    /// <c>OEBPS/Text</c>). All references (href/src/url) are updated via
    /// <see cref="UniversalUpdates"/>. A well-formed guard runs on all HTML files, the OPF and (if
    /// present) the NCX before any change. There is deliberately no workaround for case-sensitive
    /// file systems (correcting directories that already exist on disk, such as
    /// <c>oebps</c>→<c>OEBPS</c>, is irrelevant for <see cref="BookManipulation.FolderKeeper"/>,
    /// which always creates the target directories directly), and no "[std]" window title suffix (a UI-layer concern).
    /// </summary>
    public MaintenanceOperationResult RestructureToSignetNorm()
    {
        if (FindFirstNotWellFormedForBook() is { } bad)
        {
            return new MaintenanceOperationResult(false, bad);
        }

        EpubStandardization.ApplyStandardFolders(this);
        Modified = true;
        return MaintenanceOperationResult.Ok;
    }

    /// <summary>
    /// "Use Standard File Extensions" — renames every resource (including the OPF and NCX) whose
    /// extension does not match its MIME type (<see cref="MediaTypes.GetExtensionFromMediaType"/>),
    /// keeping the folder; a name already taken in that folder gets a unique version instead of overwriting the file
    /// (<see cref="EpubStandardization"/>). References are updated via <see cref="UniversalUpdates"/>. A
    /// well-formed guard runs on the HTML files and the OPF.
    /// </summary>
    public MaintenanceOperationResult UseStandardFileExtensions()
    {
        if (FindFirstNotWellFormed(GetHtmlResources()) is { } badHtml)
        {
            return new MaintenanceOperationResult(false, badHtml);
        }

        OpfResource opf = GetOpf();
        if (!WellFormedChecker.IsWellFormed(opf.GetText(), opf.MediaType))
        {
            return new MaintenanceOperationResult(false, opf);
        }

        if (EpubStandardization.ApplyStandardFileExtensions(this))
        {
            Modified = true;
        }

        return MaintenanceOperationResult.Ok;
    }

    /// <summary>
    /// "Rebase OPF Manifest IDs on Current Filenames" — a well-formed guard plus delegation to
    /// <see cref="OpfResource.RebaseManifestIds"/>.
    /// </summary>
    public MaintenanceOperationResult RebaseManifestIds()
    {
        OpfResource opf = GetOpf();
        if (!WellFormedChecker.IsWellFormed(opf.GetText(), opf.MediaType))
        {
            return new MaintenanceOperationResult(false, opf);
        }

        opf.RebaseManifestIds();
        Modified = true;
        return MaintenanceOperationResult.Ok;
    }

    /// <summary>
    /// Whether the file uses the given CSS property — in <c>style</c> attributes (substring match),
    /// in the file's <c>&lt;style&gt;</c> block, or in any of the linked CSS stylesheets.
    /// Used to decide <c>keep_whitespace</c> when pretty-printing.
    /// </summary>
    private bool XhtmlUsesStyleProperty(HtmlResource html, string property)
    {
        foreach (IElement element in html.GetDocument().All)
        {
            string? style = element.GetAttribute("style");
            if (!string.IsNullOrEmpty(style) && style.Contains(property, StringComparison.Ordinal))
            {
                return true;
            }
        }

        if (new HtmlStyleInfo(html.GetText()).GetAllPropertyValues(property).Count > 0)
        {
            return true;
        }

        foreach (string cssPath in html.GetPathsToLinkedResources())
        {
            CssResource? css = GetCssResources().FirstOrDefault(c => string.Equals(c.BookPath, cssPath, StringComparison.Ordinal));
            if (css is not null && new CssInfo(css.GetText()).GetAllPropertyValues(property).Count > 0)
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// Gives resources new bookpaths — a new file name (<see cref="BookManipulation.FolderKeeper.BulkRenameResources"/>)
    /// and/or a new folder (<see cref="BookManipulation.FolderKeeper.BulkMoveResources"/>), in the order of
    /// <see cref="EpubStandardization.SplitRelocation"/> — and updates the references to them across the whole book in
    /// one <see cref="UniversalUpdates"/> pass. The shared tail of the standardization
    /// steps (<see cref="EpubStandardization"/>); without a well-formed guard, which the caller has already run.
    /// </summary>
    internal void RelocateResources(IReadOnlyList<(Resource Resource, string NewBookPath)> changes)
    {
        ArgumentNullException.ThrowIfNull(changes);
        if (changes.Count == 0)
        {
            return;
        }

        List<Resource> resources = changes.Select(c => c.Resource).ToList();
        List<string> oldBookPaths = resources.Select(r => r.BookPath).ToList();

        (List<(Resource Resource, string NewFilename)> renames, List<(Resource Resource, string NewBookPath)> moves) =
            EpubStandardization.SplitRelocation(changes, r => r.BookPath);
        if (renames.Count > 0)
        {
            _folderKeeper.BulkRenameResources(renames.Select(c => c.Resource).ToList(), renames.Select(c => c.NewFilename).ToList());
        }

        if (moves.Count > 0)
        {
            _folderKeeper.BulkMoveResources(moves.Select(c => c.Resource).ToList(), moves.Select(c => c.NewBookPath).ToList());
        }

        ApplyUniversalUpdates(resources, oldBookPaths);
    }

    private void ApplyUniversalUpdates(List<Resource> resources, List<string> oldBookPaths)
    {
        Dictionary<string, string> updates = new(StringComparer.Ordinal);
        for (int i = 0; i < resources.Count; i++)
        {
            resources[i].CurrentBookRelPath = oldBookPaths[i];
            if (!string.Equals(oldBookPaths[i], resources[i].BookPath, StringComparison.Ordinal))
            {
                updates[oldBookPaths[i]] = resources[i].BookPath;
            }
        }

        UniversalUpdates.Perform(this, updates);
    }

    /// <summary>The well-formed guard for whole-book operations: all HTML files, the OPF and (if present) the NCX.</summary>
    internal Resource? FindFirstNotWellFormedForBook()
    {
        if (FindFirstNotWellFormed(GetHtmlResources()) is { } badHtml)
        {
            return badHtml;
        }

        OpfResource opf = GetOpf();
        if (!WellFormedChecker.IsWellFormed(opf.GetText(), opf.MediaType))
        {
            return opf;
        }

        NcxResource? ncx = GetNcx();
        if (ncx is not null && !WellFormedChecker.IsWellFormed(ncx.GetText(), ncx.MediaType))
        {
            return ncx;
        }

        return null;
    }

    internal static readonly Regex UrlFunctionHref =
        new(@"url\s*\(\s*['""]?([^()'""]*)[""']?\)", RegexOptions.Compiled);

    private static readonly string[] JavascriptMediaTypes =
    {
        "application/javascript", "application/ecmascript", "text/javascript",
    };

    /// <summary>
    /// The (X)HTML files without a DOCTYPE — the only non-blocking structural warning of
    /// <see cref="WellFormedChecker.CheckXhtmlStructure"/>. Operations guarded against files that are not
    /// well-formed still run on such files; the UI uses this list to warn the user first.
    /// </summary>
    /// <param name="resources">The files the operation works on; <c>null</c> = all (X)HTML files of the book.</param>
    /// <returns>
    /// The files without a DOCTYPE, in the order given. Empty when any of the files is not well-formed —
    /// the operation then refuses on its own, so a DOCTYPE warning would be pointless.
    /// </returns>
    public IReadOnlyList<HtmlResource> FindHtmlMissingDoctype(IEnumerable<HtmlResource>? resources = null)
    {
        List<HtmlResource> missing = new();
        foreach (HtmlResource html in resources ?? GetHtmlResources())
        {
            WellFormedResult check = WellFormedChecker.CheckXhtmlStructure(html.GetText(), html.EpubVersion);
            if (!check.IsWellFormed)
            {
                return Array.Empty<HtmlResource>();
            }

            if (check.Warning?.Kind == WellFormedWarningKind.MissingDoctype)
            {
                missing.Add(html);
            }
        }

        return missing;
    }

    internal static HtmlResource? FindFirstNotWellFormed(IEnumerable<HtmlResource> resources)
    {
        foreach (HtmlResource html in resources)
        {
            if (!WellFormedChecker.CheckXhtmlStructure(html.GetText(), html.EpubVersion).IsWellFormed)
            {
                return html;
            }
        }

        return null;
    }

    private static List<LinkableResourceEntry> BuildLinkableMap(
        IReadOnlyList<HtmlResource> resources,
        IReadOnlyList<string> existingBookPaths,
        Func<HtmlResource, IReadOnlyList<string>> linkedOf)
    {
        List<LinkableResourceEntry> map = new();
        if (resources.Count == 0)
        {
            foreach (string bookPath in existingBookPaths)
            {
                map.Add(new LinkableResourceEntry(bookPath, false));
            }

            return map;
        }

        HashSet<string> existing = new(existingBookPaths, StringComparer.Ordinal);
        List<string> shared = linkedOf(resources[0])
            .Where(existing.Contains)
            .ToList();

        foreach (HtmlResource html in resources)
        {
            HashSet<string> linked = new(linkedOf(html), StringComparer.Ordinal);
            shared.RemoveAll(bookPath => !linked.Contains(bookPath));
        }

        foreach (string bookPath in shared)
        {
            map.Add(new LinkableResourceEntry(bookPath, true));
        }

        foreach (string bookPath in existingBookPaths)
        {
            if (!shared.Contains(bookPath))
            {
                map.Add(new LinkableResourceEntry(bookPath, false));
            }
        }

        return map;
    }

    /// <summary>
    /// Whether the book has at least one font marked for obfuscation
    /// (<see cref="FontResource.ObfuscationAlgorithm"/> non-empty).
    /// </summary>
    public bool HasObfuscatedFonts()
    {
        foreach (FontResource font in _folderKeeper.GetResourceTypeList<FontResource>())
        {
            if (!string.IsNullOrEmpty(font.ObfuscationAlgorithm))
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// Sets (or clears — an empty string) the font obfuscation algorithm. In the working
    /// folder the font file is always plain; obfuscation happens only on export (<see cref="ExportEpub"/>),
    /// so this operation only changes a flag and marks the book as modified.
    /// </summary>
    /// <param name="font">The font resource.</param>
    /// <param name="algorithm">
    /// <c>""</c> = none, <see cref="OcfReader.IdpfFontAlgorithmId"/> or
    /// <see cref="OcfReader.AdobeFontAlgorithmId"/>.
    /// </param>
    public void SetFontObfuscation(FontResource font, string algorithm)
    {
        ArgumentNullException.ThrowIfNull(font);
        ArgumentNullException.ThrowIfNull(algorithm);

        if (algorithm.Length != 0
            && algorithm != OcfReader.IdpfFontAlgorithmId
            && algorithm != OcfReader.AdobeFontAlgorithmId)
        {
            throw new ArgumentException(CoreStrings.Format("Error_UnknownObfuscationAlgorithm", algorithm), nameof(algorithm));
        }

        if (font.ObfuscationAlgorithm == algorithm)
        {
            return;
        }

        font.ObfuscationAlgorithm = algorithm;
        Modified = true;
    }

    /// <summary>
    /// Writes the buffered content of all (text) resources to disk. File watching is
    /// suspended for the duration of the save. Runs sequentially.
    /// </summary>
    public void SaveAllResourcesToDisk()
    {
        _folderKeeper.SuspendWatchingResources();
        try
        {
            foreach (Resource resource in _folderKeeper.GetResourceList())
            {
                resource.SaveToDisk(bookWideSave: true);
            }
        }
        finally
        {
            _folderKeeper.ResumeWatchingResources();
        }
    }

    /// <inheritdoc/>
    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _folderKeeper.Dispose();
    }

    // Matches the opening <body> tag (also used by MergeResources).
    private static readonly Regex BodyStart = new("<\\s*body[^>]*>", RegexOptions.IgnoreCase | RegexOptions.Compiled);

    /// <summary>
    /// Merges <paramref name="resources"/> into the first one in the list (the sink).
    /// Moves the <c>&lt;body&gt;</c> content of every file into the sink (for files other than the first it
    /// injects section anchors: <c>&lt;a id="..."&gt;</c> on EPUB 3, <c>&lt;p id="..." style="display:none"&gt;</c>
    /// on EPUB 2), localizes references between the merged files, updates references from files outside
    /// the merge (and the guide on EPUB 2, and the NCX), and then removes the merged files other than the sink.
    /// </summary>
    /// <param name="resources">The resources to merge (at least 2); the first becomes the target file.</param>
    /// <returns>
    /// <c>null</c> on success; the navigation document resource when it was among <paramref name="resources"/>
    /// — the merge is rejected without changes (the nav document is protected).
    /// </returns>
    public HtmlResource? MergeResources(IReadOnlyList<HtmlResource> resources)
    {
        ArgumentNullException.ThrowIfNull(resources);
        if (IsEpub3)
        {
            string navBookPath = GetOpf().GetNavResourceBookPath();
            HtmlResource? nav = resources.FirstOrDefault(
                r => navBookPath.Length > 0 && string.Equals(r.BookPath, navBookPath, StringComparison.Ordinal));
            if (nav is not null)
            {
                return nav;
            }
        }

        if (resources.Count < 2)
        {
            return null;
        }

        HashSet<string> usedIds = new(StringComparer.Ordinal);
        List<string> mergedBookPaths = new();
        foreach (HtmlResource resource in resources)
        {
            mergedBookPaths.Add(resource.BookPath);
            foreach (string id in XhtmlDoc.GetAllDescendantIds(resource.GetText()))
            {
                usedIds.Add(id);
            }
        }

        HtmlResource sink = resources[0];
        string version = sink.EpubVersion.Length > 0 ? sink.EpubVersion : EpubVersion;

        Dictionary<string, string> updatedBodies = new(StringComparer.Ordinal);
        foreach (HtmlResource resource in resources)
        {
            updatedBodies[resource.BookPath] = AnchorUpdates.LocalizeAnchorsAndExtractBody(resource, mergedBookPaths);
        }

        string sinkSource = sink.GetText();
        Match bodyOpenMatch = BodyStart.Match(sinkSource);
        string header = bodyOpenMatch.Success ? sinkSource[..(bodyOpenMatch.Index + bodyOpenMatch.Length)] : sinkSource;

        StringBuilder newSource = new();
        newSource.Append(header);
        Dictionary<string, string> sectionIdMap = new(StringComparer.Ordinal);
        for (int i = 0; i < mergedBookPaths.Count; i++)
        {
            string bookPath = mergedBookPaths[i];
            if (i == 0)
            {
                newSource.Append(updatedBodies[bookPath]);
            }
            else
            {
                string sectionId = Utility.GenerateUniqueId("section", usedIds);
                usedIds.Add(sectionId);
                newSource.Append(version.StartsWith('3')
                    ? $"  <a id=\"{sectionId}\"></a>\n"
                    : $"  <p id=\"{sectionId}\" style=\"display:none\"></p>\n");
                newSource.Append(updatedBodies[bookPath]);
                sectionIdMap[bookPath] = sectionId;
            }
        }

        newSource.Append("</body>\n</html>");
        sink.SetText(newSource.ToString());

        if (!IsEpub3)
        {
            GetOpf().UpdateGuideAfterMerge(mergedBookPaths.Skip(1).ToList(), sink.BookPath, sectionIdMap);
        }

        List<HtmlResource> otherHtml = _folderKeeper.GetResourceTypeList<HtmlResource>(sorted: true)
            .Where(r => !mergedBookPaths.Contains(r.BookPath))
            .ToList();
        AnchorUpdates.UpdateAllAnchors(otherHtml, mergedBookPaths, sink, sectionIdMap);

        NcxResource? ncx = GetNcx();
        if (ncx is not null)
        {
            AnchorUpdates.UpdateTocEntriesAfterMerge(ncx, sink.BookPath, mergedBookPaths);
        }

        List<Resource> toRemove = resources.Skip(1).Cast<Resource>().ToList();
        _folderKeeper.BulkRemoveResources(toRemove);

        Modified = true;
        return null;
    }

    /// <summary>
    /// Splits <paramref name="originalResource"/> at the split markers
    /// (used by "Split At Markers" and by "Split File At Cursor", <see cref="SplitAtPosition"/>).
    /// Each new section is named <c>{base}_{number:D4}{extension}</c> and is inserted into the spine right after the
    /// source file. References (<c>&lt;a href&gt;</c>, NCX entries) are updated so that they point at
    /// the right new section according to the position of the <c>id</c> fragment.
    /// </summary>
    /// <param name="newSectionsBodies">
    /// The contents of the successive new sections (the result of <see cref="XhtmlDoc.GetSgfSectionSplits"/> — the first
    /// element of that list stays in <paramref name="originalResource"/>, the rest goes here).
    /// </param>
    /// <param name="originalResource">The resource being split (its content has already been reduced to the first section).</param>
    /// <returns>The newly created HTML resources, in spine order.</returns>
    public IReadOnlyList<HtmlResource> CreateNewSections(IReadOnlyList<string> newSectionsBodies, HtmlResource originalResource)
    {
        ArgumentNullException.ThrowIfNull(newSectionsBodies);
        ArgumentNullException.ThrowIfNull(originalResource);
        if (newSectionsBodies.Count == 0)
        {
            return Array.Empty<HtmlResource>();
        }

        string originatingBookPath = originalResource.BookPath;
        OpfResource opf = GetOpf();
        int originalPosition = opf.GetReadingOrder(originalResource);
        string originalFilename = originalResource.Filename;
        int dot = originalFilename.LastIndexOf('.');
        string newFilePrefix = dot >= 0 ? originalFilename[..dot] : originalFilename;
        string fileExtension = dot >= 0 ? originalFilename[dot..] : string.Empty;
        string folderPath = Core.BookPath.StartingDir(originatingBookPath);

        List<HtmlResource> htmlResources = _folderKeeper.GetResourceTypeList<HtmlResource>(sorted: true).ToList();
        List<HtmlResource> otherFiles = htmlResources.Where(r => !ReferenceEquals(r, originalResource)).ToList();

        if (IsEpub3)
        {
            string navBookPath = opf.GetNavResourceBookPath();
            if (navBookPath.Length > 0 && !opf.GetSpineOrderBookPaths().Contains(navBookPath))
            {
                htmlResources.RemoveAll(r => string.Equals(r.BookPath, navBookPath, StringComparison.Ordinal));
            }
        }

        int nextReadingOrder = originalPosition < 0 ? _folderKeeper.GetHighestReadingOrder() + 1 : originalPosition + 1;

        List<HtmlResource> newFiles = new() { originalResource };
        List<HtmlResource> created = new();
        for (int i = 0; i < newSectionsBodies.Count; i++)
        {
            string filename = $"{newFilePrefix}_{i + 1:D4}{fileExtension}";
            HtmlResource section = CreateOneNewSection(filename, newSectionsBodies[i], folderPath);
            newFiles.Add(section);
            created.Add(section);
            htmlResources.Insert(Math.Min(nextReadingOrder + i, htmlResources.Count), section);
        }

        // References between the new files — they come from a single file, so the ids are unique by assumption.
        AnchorUpdates.UpdateAllAnchorsWithIds(newFiles);
        // References from files outside the split to the old file — the right new section has to be found by id.
        AnchorUpdates.UpdateExternalAnchors(otherFiles, originatingBookPath, newFiles);
        NcxResource? ncx = GetNcx();
        if (ncx is not null)
        {
            AnchorUpdates.UpdateTocEntries(ncx, originatingBookPath, newFiles);
        }

        opf.UpdateSpineOrder(htmlResources);
        Modified = true;
        return created;
    }

    /// <summary>
    /// Splits <paramref name="html"/> at the <c>signet_split_marker</c> markers — the full flow of
    /// "Split At Markers": the first section stays in <paramref name="html"/>
    /// (after <see cref="CleanSource.Mend"/>), the rest goes to <see cref="CreateNewSections"/>. Works on a
    /// single file (file selection belongs to the App layer).
    /// </summary>
    /// <returns>The newly created HTML resources (empty when the content had no split marker).</returns>
    public IReadOnlyList<HtmlResource> SplitOnSectionMarkers(HtmlResource html)
    {
        ArgumentNullException.ThrowIfNull(html);
        IReadOnlyList<string> sections = XhtmlDoc.GetSgfSectionSplits(html.GetText());
        if (sections.Count <= 1)
        {
            return Array.Empty<HtmlResource>();
        }

        html.SetText(CleanSource.Mend(sections[0], html.EpubVersion));
        return CreateNewSections(sections.Skip(1).ToList(), html);
    }

    /// <summary>
    /// "Split File At Cursor": splits <paramref name="html"/> at <paramref name="position"/>
    /// (<see cref="XhtmlDoc.GetSplitAtPosition"/>) — the part before the position stays in <paramref name="html"/>
    /// (after <see cref="CleanSource.Mend"/>), the part after it goes to a new file right after it in the reading
    /// order (<see cref="CreateNewSections"/>, which also updates the links and the NCX). Other split markers in the
    /// file are left alone.
    /// </summary>
    /// <returns>The new HTML resource, or <c>null</c> when the file cannot be split there (no <c>&lt;body&gt;</c>, or
    /// the position is inside a tag) — the book is then unchanged.</returns>
    public HtmlResource? SplitAtPosition(HtmlResource html, int position)
    {
        ArgumentNullException.ThrowIfNull(html);
        if (XhtmlDoc.GetSplitAtPosition(html.GetText(), position) is not { } split)
        {
            return null;
        }

        html.SetText(CleanSource.Mend(split.Top, html.EpubVersion));
        return CreateNewSections(new[] { split.Bottom }, html)[0];
    }

    /// <summary>Creates one new HTML section (placeholder → content after <see cref="CleanSource.Mend"/>). The shared tail of <see cref="CreateNewSections"/>.</summary>
    private HtmlResource CreateOneNewSection(string filename, string content, string folderPath)
    {
        string tempDir = SysPath.Combine(SysPath.GetTempPath(), "signet-section-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempDir);
        string tempPath = SysPath.Combine(tempDir, filename);
        Utility.WriteUnicodeTextFile("PLACEHOLDER", tempPath);

        Resource resource;
        try
        {
            resource = _folderKeeper.AddContentFileToFolder(
                tempPath, updateOpf: true, "application/xhtml+xml", bookPath: null, folderPath: folderPath);
        }
        finally
        {
            try
            {
                Directory.Delete(tempDir, recursive: true);
            }
            catch (IOException)
            {
                // best-effort cleanup
            }
        }

        if (resource is not HtmlResource html)
        {
            throw new InvalidOperationException(CoreStrings.Format("Error_NewResourceWrongType", "XHTML"));
        }

        html.EpubVersion = EpubVersion;
        html.SetText(CleanSource.Mend(content, EpubVersion));
        html.SaveToDisk();
        return html;
    }

    /// <summary>Appends a load warning (used by the import pipeline).</summary>
    internal void AddLoadWarning(string warning)
    {
        if (!string.IsNullOrEmpty(warning))
        {
            _loadWarnings.Add(warning);
        }
    }

    /// <summary>Adds <c>properties="nav"</c> to the manifest entry that points at the given resource.</summary>
    private void MarkManifestItemAsNav(HtmlResource nav)
    {
        OpfResource opf = GetOpf();
        OpfDocument document = opf.GetOpfDocument();
        string href = Utility.UrlEncodePath(Core.BookPath.Relative(opf.BookPath, nav.BookPath));

        ManifestEntry? entry = document.Manifest.FirstOrDefault(
            m => string.Equals(Utility.UrlDecodePath(m.Href), Utility.UrlDecodePath(href), StringComparison.Ordinal));
        if (entry is null)
        {
            return;
        }

        List<string> props = entry.Attributes.Value("properties")
            .Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .ToList();
        if (!props.Contains("nav"))
        {
            props.Add("nav");
            entry.Attributes.Set("properties", string.Join(' ', props));
            opf.SetOpfDocument(document);
        }
    }
}
