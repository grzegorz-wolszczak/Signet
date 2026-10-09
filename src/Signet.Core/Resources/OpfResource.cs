using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.RegularExpressions;
using Signet.Core.Semantics;
using SysPath = System.IO.Path;

namespace Signet.Core.Resources;

/// <summary>
/// The OPF document resource (<c>content.opf</c> / <c>package.opf</c>) together with all of the OPF mutation logic
/// that most editor features rely on.
/// </summary>
/// <remarks>
/// <para>
/// Every operation follows the same pattern: <c>GetText</c> &#8594; <see cref="OpfDocument.Parse"/>
/// &#8594; mutate the model &#8594; <see cref="SetOpfDocument"/> (serialization through the pretty-printer
/// <see cref="OpfDocument.ToXml"/>). No extra XML cleanup pass is run — the model is already canonical.
/// </para>
/// <para>
/// Detecting the Nav resource and cleaning up nav landmarks when a resource is removed are handled elsewhere.
/// Guide/landmarks data is not localized (<see cref="GuideItems"/>/<see cref="Landmarks"/>).
/// </para>
/// </remarks>
public sealed class OpfResource : XmlResource
{
    /// <summary>The MIME type of an OPF document.</summary>
    public const string OpfMediaType = "application/oebps-package+xml";

    /// <summary>The name of the <c>&lt;meta name="..."&gt;</c> metadata carrying the editor version marker.</summary>
    public const string SignetVersionMetaName = "Signet version";

    /// <summary>The default publication language in a freshly created OPF.</summary>
    public const string DefaultLanguage = "en";

    private const string MediaActiveClass = "media:active-class";
    private const string MediaPlaybackActiveClass = "media:playback-active-class";
    private static readonly string[] TextExtensions = { "htm", "html", "xhtml" };

    private static readonly Regex PackageVersionRegex = new(
        "<\\s*package[^>]*version\\s*=\\s*[\"']([^'\"]*)['\"][^>]*>",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    private const string Template2 =
        "<?xml version=\"1.0\" encoding=\"UTF-8\"?>\n" +
        "<package version=\"2.0\" xmlns=\"http://www.idpf.org/2007/opf\" unique-identifier=\"BookId\">\n\n" +
        "  <metadata xmlns:dc=\"http://purl.org/dc/elements/1.1/\" xmlns:opf=\"http://www.idpf.org/2007/opf\">\n" +
        "    <dc:identifier opf:scheme=\"UUID\" id=\"BookId\">urn:uuid:{0}</dc:identifier>\n" +
        "    <dc:language>{1}</dc:language>\n" +
        "    <dc:title>{2}</dc:title>\n" +
        "  </metadata>\n\n" +
        "  <manifest>\n" +
        "  </manifest>\n\n" +
        "  <spine>\n" +
        "  </spine>\n\n" +
        "</package>";

    private const string Template3 =
        "<?xml version=\"1.0\" encoding=\"UTF-8\"?>\n" +
        "<package version=\"3.0\" unique-identifier=\"BookId\" xmlns=\"http://www.idpf.org/2007/opf\">\n\n" +
        "  <metadata xmlns:dc=\"http://purl.org/dc/elements/1.1/\">\n" +
        "    <dc:identifier id=\"BookId\">urn:uuid:{0}</dc:identifier>\n" +
        "    <dc:language>{1}</dc:language>\n" +
        "    <dc:title>{2}</dc:title>\n" +
        "    <meta property=\"dcterms:modified\">{3}</meta>\n" +
        "  </metadata>\n\n" +
        "  <manifest>\n" +
        "  </manifest>\n\n" +
        "  <spine>\n" +
        "  </spine>\n\n" +
        "</package>";

    /// <inheritdoc cref="Resource(string, string)"/>
    public OpfResource(string mainFolder, string fullFilePath)
        : base(mainFolder, fullFilePath)
    {
    }

    /// <inheritdoc/>
    public override ResourceType Type => ResourceType.Opf;

    // =====================================================================
    //  Model
    // =====================================================================

    /// <summary>Parses the current content into an <see cref="OpfDocument"/> model (best effort).</summary>
    public OpfDocument GetOpfDocument() => OpfDocument.Parse(GetText());

    /// <summary>Stores the serialization of the <see cref="OpfDocument"/> model as the resource content.</summary>
    public void SetOpfDocument(OpfDocument document)
    {
        ArgumentNullException.ThrowIfNull(document);
        SetText(document.ToXml());
    }

    private void UpdateText(OpfDocument document)
    {
        document.RebuildManifestIndex();
        SetOpfDocument(document);
    }

    /// <summary>
    /// Fills the resource with the default OPF content for the given EPUB version.
    /// An empty version =&gt; <c>2.0</c>.
    /// </summary>
    public void FillWithDefaultText(string version)
    {
        string epubVersion = string.IsNullOrEmpty(version) ? "2.0" : version;
        EpubVersion = epubVersion;

        string uuid = Utility.CreateUuid();
        string text;
        if (epubVersion.StartsWith('3'))
        {
            string template = Template3;
            text = string.Format(
                CultureInfo.InvariantCulture,
                template,
                uuid,
                DefaultLanguage,
                "[Main title here]",
                DateTime.UtcNow.ToString("yyyy-MM-ddTHH:mm:ssZ", CultureInfo.InvariantCulture));
        }
        else
        {
            string template = Template2;
            text = string.Format(CultureInfo.InvariantCulture, template, uuid, DefaultLanguage, "[Title here]");
        }

        SetText(text);
    }

    /// <summary>The package version read directly from the OPF text (a quick regex, falling back to <c>2.0</c>).</summary>
    public string GetPackageVersion()
    {
        Match match = PackageVersionRegex.Match(GetText());
        return match.Success ? match.Groups[1].Value : "2.0";
    }

    // =====================================================================
    //  Metadata
    // =====================================================================

    /// <summary>
    /// The value of the main publication identifier (the <c>dc:identifier</c> pointed at by
    /// <c>package/@unique-identifier</c>); <c>""</c> if absent.
    /// </summary>
    public string GetMainIdentifierValue()
    {
        OpfDocument document = GetOpfDocument();
        int index = GetMainIdentifierIndex(document);
        return index > -1 ? document.Metadata[index].Content : string.Empty;
    }

    /// <summary>
    /// The value of the publication's UUID identifier (without the <c>urn:uuid:</c> prefix). If there is
    /// none, it is created.
    /// </summary>
    public string GetUuidIdentifierValue()
    {
        EnsureUuidIdentifierPresent();
        OpfDocument document = GetOpfDocument();
        foreach (MetaEntry meta in document.Metadata)
        {
            if (meta.Name.StartsWith("dc:identifier", StringComparison.Ordinal))
            {
                string value = meta.Content.Replace("urn:uuid:", string.Empty, StringComparison.Ordinal);
                if (Guid.TryParse(value, out _))
                {
                    return value;
                }
            }
        }

        return string.Empty;
    }

    /// <summary>Adds a UUID identifier if the metadata has no valid one.</summary>
    public void EnsureUuidIdentifierPresent()
    {
        OpfDocument document = GetOpfDocument();
        foreach (MetaEntry meta in document.Metadata)
        {
            if (meta.Name.StartsWith("dc:identifier", StringComparison.Ordinal))
            {
                string value = meta.Content.Replace("urn:uuid:", string.Empty, StringComparison.Ordinal);
                if (Guid.TryParse(value, out _))
                {
                    return;
                }
            }
        }

        WriteIdentifier("UUID", "urn:uuid:" + Utility.CreateUuid(), document);
        UpdateText(document);
    }

    /// <summary>The first (main) <c>dc:title</c> value, or <c>""</c>.</summary>
    public string GetPrimaryBookTitle()
    {
        IReadOnlyList<string> titles = GetDcMetadataValues("dc:title");
        return titles.Count > 0 ? titles[0] : string.Empty;
    }

    /// <summary>
    /// Sets the main <c>dc:title</c>: overwrites the content of the first <c>dc:title</c> element
    /// or adds a new one if none exists. Used when importing an HTML file.
    /// </summary>
    public void SetPrimaryBookTitle(string title)
    {
        ArgumentNullException.ThrowIfNull(title);
        OpfDocument document = GetOpfDocument();
        MetaEntry? existing = document.Metadata.FirstOrDefault(
            m => string.Equals(m.Name, "dc:title", StringComparison.Ordinal));
        if (existing is not null)
        {
            existing.Content = Utility.EncodeXml(title);
        }
        else
        {
            int insertAt = document.Metadata.FindLastIndex(
                m => m.Name.StartsWith("dc:", StringComparison.Ordinal)) + 1;
            document.Metadata.Insert(insertAt, new MetaEntry { Name = "dc:title", Content = Utility.EncodeXml(title) });
        }

        UpdateText(document);
    }

    /// <summary>The first (main) <c>dc:language</c> value, or <see cref="DefaultLanguage"/>.</summary>
    public string GetPrimaryBookLanguage()
    {
        IReadOnlyList<string> languages = GetDcMetadataValues("dc:language");
        return languages.Count > 0 ? languages[0] : DefaultLanguage;
    }

    /// <summary>All Dublin Core (<c>dc:*</c>) elements from the metadata.</summary>
    public IReadOnlyList<MetaEntry> GetDcMetadata() =>
        GetOpfDocument().Metadata.Where(m => m.Name.StartsWith("dc:", StringComparison.Ordinal)).ToList();

    /// <summary>The values of a specific DC element (e.g. <c>dc:creator</c>).</summary>
    public IReadOnlyList<string> GetDcMetadataValues(string name)
    {
        ArgumentNullException.ThrowIfNull(name);
        return GetDcMetadata().Where(m => string.Equals(m.Name, name, StringComparison.Ordinal))
            .Select(m => m.Content).ToList();
    }

    /// <summary>The <c>&lt;metadata&gt;</c> element with its content as an XML fragment.</summary>
    public string GetMetadataXml() => GetOpfDocument().GetMetadataXml();

    /// <summary>
    /// Overwrites the Dublin Core elements with the given list (the main identifier is preserved).
    /// Note: <c>refines</c> is not handled.
    /// </summary>
    public void SetDcMetadata(IEnumerable<MetaEntry> metadata)
    {
        ArgumentNullException.ThrowIfNull(metadata);
        OpfDocument document = GetOpfDocument();
        RemoveDcElements(document);
        foreach (MetaEntry entry in metadata)
        {
            document.Metadata.Add(new MetaEntry
            {
                Name = entry.Name,
                Content = Utility.EncodeXml(entry.Content),
                Attributes = new TagAttributes(entry.Attributes),
            });
        }

        UpdateText(document);
    }

    /// <summary>The Media Overlays "active class" selectors, if defined in the metadata.</summary>
    public IReadOnlyList<string> GetMediaOverlayActiveClassSelectors()
    {
        List<string> selectors = new();
        foreach (MetaEntry meta in GetOpfDocument().Metadata)
        {
            if (!string.Equals(meta.Name, "meta", StringComparison.Ordinal))
            {
                continue;
            }

            string property = meta.Attributes.Value("property");
            if (property == MediaActiveClass || property == MediaPlaybackActiveClass)
            {
                selectors.Add("." + meta.Content);
            }
        }

        return selectors;
    }

    /// <summary>
    /// Sets/updates the publication modification date (<c>dcterms:modified</c> in EPUB 3,
    /// <c>dc:date opf:event="modification"</c> in EPUB 2) and returns the ISO 8601 timestamp used.
    /// </summary>
    public string AddModificationDateMeta()
    {
        string dateTime = DateTime.UtcNow.ToString("yyyy-MM-ddTHH:mm:ssZ", CultureInfo.InvariantCulture);
        OpfDocument document = GetOpfDocument();

        if (EpubVersion.StartsWith('3'))
        {
            for (int i = 0; i < document.Metadata.Count; i++)
            {
                MetaEntry meta = document.Metadata[i];
                if (string.Equals(meta.Name, "meta", StringComparison.Ordinal)
                    && meta.Attributes.Value("property") == "dcterms:modified")
                {
                    meta.Content = dateTime;
                    UpdateText(document);
                    return dateTime;
                }
            }

            MetaEntry created = new() { Name = "meta", Content = dateTime };
            created.Attributes.Set("property", "dcterms:modified");
            document.Metadata.Add(created);
            UpdateText(document);
            return dateTime;
        }

        string date = DateTime.UtcNow.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
        for (int i = 0; i < document.Metadata.Count; i++)
        {
            MetaEntry meta = document.Metadata[i];
            if (string.Equals(meta.Name, "dc:date", StringComparison.Ordinal)
                && meta.Attributes.Value("opf:event") == "modification")
            {
                meta.Content = date;
                UpdateText(document);
                return dateTime;
            }
        }

        MetaEntry entry = new() { Name = "dc:date", Content = date };
        entry.Attributes.Set("xmlns:opf", "http://www.idpf.org/2007/opf");
        entry.Attributes.Set("opf:event", "modification");
        document.Metadata.Add(entry);
        UpdateText(document);
        return dateTime;
    }

    /// <summary>
    /// Adds (or updates) the editor marker metadata — <c>&lt;meta name="Signet version"&gt;</c>
    /// with the value "Signet &lt;version&gt;".
    /// </summary>
    public void AddSignetVersionMeta()
    {
        OpfDocument document = GetOpfDocument();
        string content = ApplicationInfo.NameWithVersion;
        for (int i = 0; i < document.Metadata.Count; i++)
        {
            MetaEntry meta = document.Metadata[i];
            if (string.Equals(meta.Name, "meta", StringComparison.Ordinal)
                && meta.Attributes.Value("name") == SignetVersionMetaName)
            {
                meta.Attributes.Set("content", content);
                UpdateText(document);
                return;
            }
        }

        MetaEntry created = new() { Name = "meta" };
        created.Attributes.Set("name", SignetVersionMetaName);
        created.Attributes.Set("content", content);
        document.Metadata.Add(created);
        UpdateText(document);
    }

    /// <summary>
    /// The <c>content</c> of the first <c>&lt;meta name="…" content="…"/&gt;</c> with the given name; <c>null</c>
    /// when there is none.
    /// </summary>
    public string? GetNamedMeta(string name)
    {
        ArgumentException.ThrowIfNullOrEmpty(name);
        MetaEntry? meta = GetOpfDocument().Metadata.FirstOrDefault(m => IsNamedMeta(m, name));
        return meta?.Attributes.Value("content");
    }

    /// <summary>
    /// Sets the <c>content</c> of the <c>&lt;meta name="…"/&gt;</c> with the given name (adding it when missing);
    /// <c>null</c> removes every such meta. The OPF text is updated only when something changes.
    /// </summary>
    public void SetNamedMeta(string name, string? content)
    {
        ArgumentException.ThrowIfNullOrEmpty(name);
        OpfDocument document = GetOpfDocument();
        if (content is null)
        {
            if (document.Metadata.RemoveAll(m => IsNamedMeta(m, name)) > 0)
            {
                UpdateText(document);
            }

            return;
        }

        MetaEntry? meta = document.Metadata.FirstOrDefault(m => IsNamedMeta(m, name));
        if (meta is not null)
        {
            if (meta.Attributes.Value("content") != content)
            {
                meta.Attributes.Set("content", content);
                UpdateText(document);
            }

            return;
        }

        MetaEntry created = new() { Name = "meta" };
        created.Attributes.Set("name", name);
        created.Attributes.Set("content", content);
        document.Metadata.Add(created);
        UpdateText(document);
    }

    private static bool IsNamedMeta(MetaEntry meta, string name) =>
        string.Equals(meta.Name, "meta", StringComparison.Ordinal) && meta.Attributes.Value("name") == name;

    // =====================================================================
    //  Manifest
    // =====================================================================

    /// <summary>
    /// Adds a manifest entry for the resource (and for HTML — also a spine entry at the end).
    /// </summary>
    public void AddResource(Resource resource)
    {
        ArgumentNullException.ThrowIfNull(resource);
        if (ReferenceEquals(resource, this))
        {
            return;
        }

        OpfDocument document = GetOpfDocument();
        AppendManifestEntry(document, resource);
        UpdateText(document);
    }

    /// <summary>Adds manifest entries (and spine entries for HTML) for many resources at once.</summary>
    public void BulkAddResources(IEnumerable<Resource> resources)
    {
        ArgumentNullException.ThrowIfNull(resources);
        OpfDocument document = GetOpfDocument();
        foreach (Resource resource in resources)
        {
            if (!ReferenceEquals(resource, this))
            {
                AppendManifestEntry(document, resource);
                document.RebuildManifestIndex();
            }
        }

        UpdateText(document);
    }

    /// <summary>Removes the manifest entry (and the related spine / guide / cover meta) for a resource.</summary>
    public void RemoveResource(Resource resource)
    {
        ArgumentNullException.ThrowIfNull(resource);
        if (ReferenceEquals(resource, this))
        {
            return;
        }

        OpfDocument document = GetOpfDocument();
        if (document.Manifest.Count == 0)
        {
            return;
        }

        RemoveManifestEntry(document, resource);
        UpdateText(document);
    }

    /// <summary>Removes the manifest entries for many resources at once.</summary>
    public void BulkRemoveResources(IEnumerable<Resource> resources)
    {
        ArgumentNullException.ThrowIfNull(resources);
        OpfDocument document = GetOpfDocument();
        if (document.Manifest.Count == 0)
        {
            return;
        }

        foreach (Resource resource in resources)
        {
            if (!ReferenceEquals(resource, this))
            {
                RemoveManifestEntry(document, resource);
            }
        }

        UpdateText(document);
    }

    /// <summary>
    /// Reacts to a resource rename: updates the <c>href</c> and <c>id</c> of the manifest entry
    /// (the new id derived from the file name), the related <c>idref</c> in the spine, <c>toc</c> for NCX
    /// and the cover meta for an image.
    /// </summary>
    public void ResourceRenamed(Resource resource, string oldFullPath)
    {
        ArgumentNullException.ThrowIfNull(resource);
        ArgumentException.ThrowIfNullOrEmpty(oldFullPath);
        if (ReferenceEquals(resource, this))
        {
            return;
        }

        OpfDocument document = GetOpfDocument();
        RenameInDocument(document, BookPath, FullPathToBookPath(oldFullPath), resource.BookPath, resource.Type);
        UpdateText(document);
    }

    /// <summary>
    /// Reacts to a resource move: updates only the <c>href</c> of the manifest entry
    /// (the id stays unchanged).
    /// </summary>
    public void ResourceMoved(Resource resource, string oldFullPath)
    {
        ArgumentNullException.ThrowIfNull(resource);
        ArgumentException.ThrowIfNullOrEmpty(oldFullPath);
        if (ReferenceEquals(resource, this))
        {
            return;
        }

        OpfDocument document = GetOpfDocument();
        MoveInDocument(document, BookPath, FullPathToBookPath(oldFullPath), resource.BookPath);
        UpdateText(document);
    }

    /// <summary>
    /// Reacts to a move of the OPF itself: the manifest <c>href</c>s are relative to the OPF, so they are rebased onto
    /// its new folder (the files they point to stay where they are). The <c>&lt;guide&gt;</c> is rebased by
    /// <see cref="SourceUpdates.UniversalUpdates"/>.
    /// </summary>
    /// <param name="oldBookPath">The bookpath of the OPF before the move.</param>
    public void OpfMoved(string oldBookPath)
    {
        ArgumentException.ThrowIfNullOrEmpty(oldBookPath);
        OpfDocument document = GetOpfDocument();
        RebaseManifestHrefs(document, oldBookPath, BookPath);
        UpdateText(document);
    }

    /// <summary>
    /// The manifest part of <see cref="ResourceRenamed"/> on a document: the entry of <paramref name="oldBookPath"/> gets
    /// the <c>href</c> of <paramref name="newBookPath"/> and an identifier derived from the new file name, and the
    /// references to the old identifier (spine, <c>toc</c>, cover meta) follow. Shared with the planning of the
    /// "Standardize EPUB" dialog, which runs it on a simulated document.
    /// </summary>
    /// <returns>The old and the new identifier, or <c>null</c> when the manifest has no entry for the file.</returns>
    internal static (string OldId, string NewId)? RenameInDocument(
        OpfDocument document,
        string opfBookPath,
        string oldBookPath,
        string newBookPath,
        ResourceType type)
    {
        string oldHref = HrefFor(opfBookPath, oldBookPath);
        ManifestEntry? entry = document.Manifest.FirstOrDefault(e => string.Equals(e.Href, oldHref, StringComparison.Ordinal));
        if (entry is null)
        {
            return null;
        }

        string oldId = entry.Id;
        entry.Href = HrefFor(opfBookPath, newBookPath);
        document.RebuildManifestIndex();
        string newId = GetUniqueId(document, GetValidId(SysPath.GetFileName(newBookPath)), oldId);
        entry.Id = newId;
        document.RebuildManifestIndex();
        if (oldId.Length == 0)
        {
            return (oldId, newId);
        }

        foreach (SpineEntry itemref in document.Spine.Where(s => string.Equals(s.IdRef, oldId, StringComparison.Ordinal)))
        {
            itemref.IdRef = newId;
        }

        if (type == ResourceType.Ncx && document.SpineAttributes.Attributes.Value("toc") == oldId)
        {
            document.SpineAttributes.Attributes.Set("toc", newId);
        }

        if (type == ResourceType.Image && IsCoverImageId(document, oldId))
        {
            document.Metadata[GetCoverMetaIndex(document)].Attributes.Set("content", newId);
        }

        return (oldId, newId);
    }

    /// <summary>The manifest part of <see cref="ResourceMoved"/> on a document (see <see cref="RenameInDocument"/>).</summary>
    internal static void MoveInDocument(OpfDocument document, string opfBookPath, string oldBookPath, string newBookPath)
    {
        string oldHref = HrefFor(opfBookPath, oldBookPath);
        ManifestEntry? entry = document.Manifest.FirstOrDefault(e => string.Equals(e.Href, oldHref, StringComparison.Ordinal));
        if (entry is not null)
        {
            entry.Href = HrefFor(opfBookPath, newBookPath);
            document.RebuildManifestIndex();
        }
    }

    /// <summary>The manifest part of <see cref="OpfMoved"/> on a document (see <see cref="RenameInDocument"/>).</summary>
    internal static void RebaseManifestHrefs(OpfDocument document, string oldOpfBookPath, string newOpfBookPath)
    {
        string oldFolder = Core.BookPath.StartingDir(oldOpfBookPath);
        if (string.Equals(oldFolder, Core.BookPath.StartingDir(newOpfBookPath), StringComparison.Ordinal))
        {
            return;
        }

        foreach (ManifestEntry entry in document.Manifest)
        {
            if (entry.Href.Length == 0 || entry.Href.Contains(':', StringComparison.Ordinal))
            {
                // Remote resources (EPUB 3) keep their absolute URL.
                continue;
            }

            string bookPath = Core.BookPath.BuildBookPath(Utility.UrlDecodePath(entry.Href), oldFolder);
            entry.Href = HrefFor(newOpfBookPath, bookPath);
        }

        document.RebuildManifestIndex();
    }

    /// <summary>Updates the <c>href</c> of manifest entries after a bulk rename.</summary>
    public void BulkResourcesRenamed(IReadOnlyDictionary<string, Resource> oldBookPathToResource) =>
        BulkUpdateHrefs(oldBookPathToResource);

    /// <summary>Updates the <c>href</c> of manifest entries after a bulk move.</summary>
    public void BulkResourcesMoved(IReadOnlyDictionary<string, Resource> oldBookPathToResource) =>
        BulkUpdateHrefs(oldBookPathToResource);

    /// <summary>Updates the MIME types of manifest entries to the current resource types.</summary>
    public void UpdateManifestMediaTypes(IEnumerable<Resource> resources)
    {
        ArgumentNullException.ThrowIfNull(resources);
        OpfDocument document = GetOpfDocument();
        foreach ((int pos, ManifestMediaTypeChange change) in ComputeManifestMediaTypeChanges(document, BookPath, resources, r => r.BookPath))
        {
            document.Manifest[pos].MediaType = change.NewMediaType;
        }

        UpdateText(document);
    }

    /// <summary>Plans <see cref="UpdateManifestMediaTypes"/> without changing anything: the entries whose type differs.</summary>
    public IReadOnlyList<ManifestMediaTypeChange> PlanManifestMediaTypes(IEnumerable<Resource> resources)
    {
        ArgumentNullException.ThrowIfNull(resources);
        return ComputeManifestMediaTypeChanges(GetOpfDocument(), BookPath, resources, r => r.BookPath).Select(c => c.Change).ToList();
    }

    /// <summary>
    /// The shared core of <see cref="UpdateManifestMediaTypes"/> and <see cref="PlanManifestMediaTypes"/>: the manifest
    /// positions whose <c>media-type</c> differs from the resource's type. The OPF and resource bookpaths are passed in,
    /// so the "Standardize EPUB" planning can run it on a simulated state.
    /// </summary>
    internal static List<(int Position, ManifestMediaTypeChange Change)> ComputeManifestMediaTypeChanges(
        OpfDocument document,
        string opfBookPath,
        IEnumerable<Resource> resources,
        Func<Resource, string> bookPathOf)
    {
        List<(int, ManifestMediaTypeChange)> changes = new();
        foreach (Resource resource in resources)
        {
            string href = HrefFor(opfBookPath, bookPathOf(resource));
            if (document.HrefToManifestPosition.TryGetValue(href, out int pos))
            {
                string mimeType = GetResourceMimetype(resource);
                string current = document.Manifest[pos].MediaType;
                if (!string.Equals(current, mimeType, StringComparison.Ordinal))
                {
                    changes.Add((pos, new ManifestMediaTypeChange(resource, current, mimeType)));
                }
            }
        }

        return changes;
    }

    /// <summary>
    /// Updates the <c>properties</c> attribute of manifest entries for HTML resources (EPUB 3):
    /// <c>scripted</c>, <c>mathml</c>, <c>svg</c>, <c>remote-resources</c> — based on the content —
    /// and <c>cover-image</c> for the cover image.
    /// </summary>
    public void UpdateManifestProperties(IEnumerable<Resource> resources)
    {
        ArgumentNullException.ThrowIfNull(resources);
        OpfDocument document = GetOpfDocument();
        if (document.Version != "3.0")
        {
            return;
        }

        foreach (Resource resource in resources)
        {
            if (resource is not HtmlResource htmlResource)
            {
                continue;
            }

            string href = HrefForResource(htmlResource);
            if (!document.HrefToManifestPosition.TryGetValue(href, out int pos))
            {
                continue;
            }

            IReadOnlyList<string> properties = htmlResource.GetManifestProperties();
            document.Manifest[pos].Attributes.Remove("properties");
            if (properties.Count > 0)
            {
                document.Manifest[pos].Attributes.Set("properties", string.Join(' ', properties));
            }
        }

        int coverMeta = GetCoverMetaIndex(document);
        if (coverMeta > -1)
        {
            string coverId = document.Metadata[coverMeta].Attributes.Value("content");
            if (coverId.Length > 0 && document.IdToManifestPosition.TryGetValue(coverId, out int pos))
            {
                AddPropertyToken(document.Manifest[pos], "cover-image");
            }
        }

        UpdateText(document);
    }

    /// <summary>The <c>properties</c> attribute of a resource's manifest entry (EPUB 3 only).</summary>
    public string GetManifestPropertiesForResource(Resource resource)
    {
        ArgumentNullException.ThrowIfNull(resource);
        OpfDocument document = GetOpfDocument();
        if (!document.Version.StartsWith('3'))
        {
            return string.Empty;
        }

        string href = HrefForResource(resource);
        return document.HrefToManifestPosition.TryGetValue(href, out int pos)
            ? document.Manifest[pos].Attributes.Value("properties")
            : string.Empty;
    }

    /// <summary>A bookpath -&gt; <c>properties</c> attribute map for all entries that have one (EPUB 3).</summary>
    public IReadOnlyDictionary<string, string> GetManifestPropertiesForPaths()
    {
        Dictionary<string, string> result = new(StringComparer.Ordinal);
        if (!EpubVersion.StartsWith('3'))
        {
            return result;
        }

        OpfDocument document = GetOpfDocument();
        foreach (ManifestEntry entry in document.Manifest)
        {
            if (entry.Attributes.Contains("properties"))
            {
                string bookPath = Core.BookPath.BuildBookPath(Utility.UrlDecodePath(entry.Href), Folder);
                result[bookPath] = entry.Attributes.Value("properties");
            }
        }

        return result;
    }

    /// <summary>
    /// The bookpath of the EPUB 3 navigation document (the manifest entry whose <c>properties</c> attribute
    /// contains the <c>nav</c> token), or an empty string if the publication has none.
    /// Returns the path rather than the resource object (mapping it to a
    /// resource is the job of a higher layer that knows the <c>FolderKeeper</c>).
    /// </summary>
    public string GetNavResourceBookPath()
    {
        if (!EpubVersion.StartsWith('3'))
        {
            return string.Empty;
        }

        OpfDocument document = GetOpfDocument();
        foreach (ManifestEntry entry in document.Manifest)
        {
            if (!entry.Attributes.Contains("properties"))
            {
                continue;
            }

            string[] tokens = entry.Attributes.Value("properties")
                .Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
            if (Array.Exists(tokens, t => string.Equals(t, "nav", StringComparison.Ordinal)))
            {
                return Core.BookPath.BuildBookPath(Utility.UrlDecodePath(entry.Href), Folder);
            }
        }

        return string.Empty;
    }

    /// <summary>
    /// Plans <see cref="RebaseManifestIds"/> without changing anything: the manifest identifiers that would change,
    /// in manifest order.
    /// </summary>
    public IReadOnlyList<ManifestIdChange> PlanManifestIdRebase() => PlanManifestIdRebase(GetOpfDocument(), BookPath);

    /// <summary>
    /// <see cref="PlanManifestIdRebase()"/> on a given document — the "Standardize EPUB" planning runs it on a simulated
    /// document (files renamed or moved by an earlier step) with the simulated bookpath of the OPF.
    /// </summary>
    internal static IReadOnlyList<ManifestIdChange> PlanManifestIdRebase(OpfDocument document, string opfBookPath)
    {
        string opfFolder = Core.BookPath.StartingDir(opfBookPath);
        return ComputeManifestIdRebase(document, opfFolder, new Dictionary<string, string>(StringComparer.Ordinal))
            .Select(change => new ManifestIdChange(change.Entry.Id, change.NewId, EntryBookPath(change.Entry, opfFolder)))
            .ToList();
    }

    /// <summary>
    /// The shared core of <see cref="RebaseManifestIds"/> and <see cref="PlanManifestIdRebase()"/>: the new identifier of
    /// every manifest entry whose identifier changes (the entries themselves are not modified). Fills
    /// <paramref name="changed"/> with the old -&gt; new identifier map used to update the references.
    /// </summary>
    private static List<(ManifestEntry Entry, string NewId)> ComputeManifestIdRebase(
        OpfDocument document,
        string opfFolder,
        Dictionary<string, string> changed)
    {
        HashSet<string> usedIds = CollectUsedIds(document);
        List<(ManifestEntry, string)> result = new();
        foreach (ManifestEntry entry in document.Manifest)
        {
            string filename = SysPath.GetFileName(EntryBookPath(entry, opfFolder));
            string newId = GetValidId(GenerateBaseIdFromFilename(filename));
            if (string.Equals(newId, entry.Id, StringComparison.Ordinal))
            {
                continue;
            }

            if (usedIds.Contains(newId))
            {
                newId = GenerateUniqueId(newId, usedIds);
            }

            changed[entry.Id] = newId;
            if (!changed.ContainsValue(entry.Id))
            {
                usedIds.Remove(entry.Id);
            }

            usedIds.Add(newId);
            result.Add((entry, newId));
        }

        return result;
    }

    private static string EntryBookPath(ManifestEntry entry, string opfFolder) =>
        Core.BookPath.BuildBookPath(Utility.UrlDecodePath(entry.Href), opfFolder);

    /// <summary>
    /// Renumbers the manifest identifiers based on file names (asciify + validation +
    /// uniqueness), updating all references (spine, <c>toc</c>, bindings, <c>refines</c>,
    /// <c>media-overlay</c>, <c>fallback</c>, cover meta).
    /// </summary>
    public void RebaseManifestIds()
    {
        OpfDocument document = GetOpfDocument();
        Dictionary<string, string> changed = new(StringComparer.Ordinal);
        foreach ((ManifestEntry entry, string newId) in ComputeManifestIdRebase(document, Folder, changed))
        {
            entry.Id = newId;
        }

        if (changed.Count == 0)
        {
            return;
        }

        string toc = document.SpineAttributes.Attributes.Value("toc");
        if (toc.Length > 0 && changed.TryGetValue(toc, out string? newToc))
        {
            document.SpineAttributes.Attributes.Set("toc", newToc);
        }

        foreach (SpineEntry itemref in document.Spine)
        {
            if (changed.TryGetValue(itemref.IdRef, out string? mapped))
            {
                itemref.IdRef = mapped;
            }
        }

        foreach (BindingsEntry binding in document.Bindings)
        {
            if (changed.TryGetValue(binding.Handler, out string? mapped))
            {
                binding.Handler = mapped;
            }
        }

        foreach (MetaEntry meta in document.Metadata)
        {
            if (!string.Equals(meta.Name, "meta", StringComparison.Ordinal))
            {
                continue;
            }

            bool coverMeta = meta.Attributes.Value("name") == "cover";
            bool refines = meta.Attributes.Contains("property") && meta.Attributes.Contains("refines");
            if (coverMeta)
            {
                string content = meta.Attributes.Value("content");
                if (changed.TryGetValue(content, out string? mapped))
                {
                    meta.Attributes.Set("content", mapped);
                }
            }

            if (refines)
            {
                string value = meta.Attributes.Value("refines");
                if (value.StartsWith('#') && changed.TryGetValue(value[1..], out string? mapped))
                {
                    meta.Attributes.Set("refines", "#" + mapped);
                }
            }
        }

        foreach (ManifestEntry entry in document.Manifest)
        {
            foreach (string key in new[] { "media-overlay", "fallback" })
            {
                if (entry.Attributes.Contains(key) && changed.TryGetValue(entry.Attributes.Value(key), out string? mapped))
                {
                    entry.Attributes.Set(key, mapped);
                }
            }
        }

        UpdateText(document);
    }

    // =====================================================================
    //  Spine
    // =====================================================================

    /// <summary>Appends an HTML resource at the end of the spine (removing an earlier occurrence).</summary>
    public void AppendResourceToSpine(Resource resource, bool nonlinear)
    {
        ArgumentNullException.ThrowIfNull(resource);
        OpfDocument document = GetOpfDocument();
        string itemId = GetResourceManifestId(document, resource);

        document.Spine.RemoveAll(s => string.Equals(s.IdRef, itemId, StringComparison.Ordinal));
        if (resource.Type == ResourceType.Html && itemId.Length > 0)
        {
            SpineEntry entry = new() { IdRef = itemId };
            if (nonlinear)
            {
                entry.Attributes.Set("linear", "no");
            }

            document.Spine.Add(entry);
        }

        UpdateText(document);
    }

    /// <summary>Removes a resource from the spine.</summary>
    public void RemoveResourceFromSpine(Resource resource)
    {
        ArgumentNullException.ThrowIfNull(resource);
        OpfDocument document = GetOpfDocument();
        string itemId = GetResourceManifestId(document, resource);
        if (itemId.Length > 0)
        {
            int index = document.Spine.FindIndex(s => string.Equals(s.IdRef, itemId, StringComparison.Ordinal));
            if (index >= 0)
            {
                document.Spine.RemoveAt(index);
            }
        }

        UpdateText(document);
    }

    /// <summary>
    /// Rearranges the spine to contain the given HTML resources in the given order (keeping
    /// the existing entry attributes).
    /// </summary>
    public void UpdateSpineOrder(IReadOnlyList<HtmlResource> htmlFiles)
    {
        ArgumentNullException.ThrowIfNull(htmlFiles);
        OpfDocument document = GetOpfDocument();
        List<SpineEntry> newSpine = new(htmlFiles.Count);
        foreach (HtmlResource html in htmlFiles)
        {
            string id = GetResourceManifestId(document, html);
            SpineEntry? existing = document.Spine.FirstOrDefault(s => string.Equals(s.IdRef, id, StringComparison.Ordinal));
            newSpine.Add(existing ?? new SpineEntry { IdRef = id });
        }

        document.Spine.Clear();
        document.Spine.AddRange(newSpine);
        UpdateText(document);
    }

    /// <summary>
    /// Moves <paramref name="fromResource"/> so that it comes directly after
    /// <paramref name="afterResource"/> in the reading order.
    /// </summary>
    public void MoveReadingOrder(HtmlResource fromResource, HtmlResource afterResource)
    {
        if (fromResource is null || afterResource is null)
        {
            return;
        }

        OpfDocument document = GetOpfDocument();
        string fromId = GetResourceManifestId(document, fromResource);
        string afterId = GetResourceManifestId(document, afterResource);

        int fromPos = document.Spine.FindIndex(s => string.Equals(s.IdRef, fromId, StringComparison.Ordinal));
        int afterPos = document.Spine.FindIndex(s => string.Equals(s.IdRef, afterId, StringComparison.Ordinal));
        if (fromPos < 0 || afterPos < 0)
        {
            return;
        }

        if (fromPos != afterPos + 1)
        {
            if (fromPos > afterPos)
            {
                afterPos += 1;
            }

            SpineEntry moved = document.Spine[fromPos];
            document.Spine.RemoveAt(fromPos);
            document.Spine.Insert(afterPos, moved);
        }

        UpdateText(document);
    }

    /// <summary>The position of a resource in the spine (0-based) or <c>-1</c>.</summary>
    public int GetReadingOrder(Resource resource)
    {
        ArgumentNullException.ThrowIfNull(resource);
        OpfDocument document = GetOpfDocument();
        string id = GetResourceManifestId(document, resource);
        return id.Length == 0 ? -1 : document.Spine.FindIndex(s => string.Equals(s.IdRef, id, StringComparison.Ordinal));
    }

    /// <summary>A resource -&gt; reading order position map (<c>-1</c> for resources outside the spine).</summary>
    public IReadOnlyDictionary<Resource, int> GetReadingOrderAll(IEnumerable<Resource> resources)
    {
        ArgumentNullException.ThrowIfNull(resources);
        OpfDocument document = GetOpfDocument();
        Dictionary<string, int> idOrder = new(StringComparer.Ordinal);
        for (int i = 0; i < document.Spine.Count; i++)
        {
            idOrder[document.Spine[i].IdRef] = i;
        }

        Dictionary<Resource, int> readingOrder = new();
        foreach (Resource resource in resources)
        {
            string id = GetResourceManifestId(document, resource);
            readingOrder[resource] = id.Length > 0 && idOrder.TryGetValue(id, out int order) ? order : -1;
        }

        return readingOrder;
    }

    /// <summary>The resources from the given list in spine order.</summary>
    public IReadOnlyList<Resource> GetSpineOrderResources(IReadOnlyList<Resource> resources)
    {
        ArgumentNullException.ThrowIfNull(resources);
        OpfDocument document = GetOpfDocument();
        Dictionary<string, Resource> idToResource = new(StringComparer.Ordinal);
        foreach (Resource resource in resources)
        {
            string href = HrefForResource(resource);
            if (document.HrefToManifestPosition.TryGetValue(href, out int pos))
            {
                idToResource[document.Manifest[pos].Id] = resource;
            }
        }

        List<Resource> ordered = new();
        foreach (SpineEntry itemref in document.Spine)
        {
            if (idToResource.TryGetValue(itemref.IdRef, out Resource? resource))
            {
                ordered.Add(resource);
            }
        }

        return ordered;
    }

    /// <summary>The bookpaths of resources in spine order (entries with no manifest counterpart are skipped).</summary>
    public IReadOnlyList<string> GetSpineOrderBookPaths()
    {
        OpfDocument document = GetOpfDocument();
        string opfFolder = Folder;
        List<string> result = new();
        foreach (SpineEntry itemref in document.Spine)
        {
            if (document.IdToManifestPosition.TryGetValue(itemref.IdRef, out int position))
            {
                string href = Utility.UrlDecodePath(document.Manifest[position].Href);
                result.Add(Core.BookPath.BuildBookPath(href, opfFolder));
            }
        }

        return result;
    }

    /// <summary>Sets (or clears) the <c>linear="no"</c> attribute of a spine entry for an HTML resource.</summary>
    public void SetItemRefLinear(Resource resource, bool linear)
    {
        ArgumentNullException.ThrowIfNull(resource);
        OpfDocument document = GetOpfDocument();
        string href = HrefForResource(resource);
        if (!document.HrefToManifestPosition.TryGetValue(href, out int pos))
        {
            return;
        }

        string itemId = document.Manifest[pos].Id;
        if (resource.Type == ResourceType.Html)
        {
            foreach (SpineEntry itemref in document.Spine.Where(s => string.Equals(s.IdRef, itemId, StringComparison.Ordinal)))
            {
                itemref.Attributes.Remove("linear");
                if (!linear)
                {
                    itemref.Attributes.Set("linear", "no");
                }

                break;
            }
        }

        UpdateText(document);
    }

    // =====================================================================
    //  NCX
    // =====================================================================

    /// <summary>Adds a manifest entry for the NCX and returns its id.</summary>
    /// <param name="ncxBookPath">The bookpath of the NCX file.</param>
    /// <param name="id">The preferred id (<c>ncx</c> by default).</param>
    public string AddNcxItem(string ncxBookPath, string id = "ncx")
    {
        ArgumentException.ThrowIfNullOrEmpty(ncxBookPath);
        OpfDocument document = GetOpfDocument();
        ManifestEntry entry = new()
        {
            Id = GetUniqueId(document, id),
            Href = HrefForBookPath(ncxBookPath),
            MediaType = "application/x-dtbncx+xml",
        };
        document.Manifest.Add(entry);
        UpdateText(document);
        return entry.Id;
    }

    /// <summary>Sets the <c>spine/@toc</c> attribute to the new NCX id (if different).</summary>
    public void UpdateNcxOnSpine(string newNcxId)
    {
        ArgumentNullException.ThrowIfNull(newNcxId);
        OpfDocument document = GetOpfDocument();
        if (document.SpineAttributes.Attributes.Value("toc") != newNcxId)
        {
            document.SpineAttributes.Attributes.Set("toc", newNcxId);
            UpdateText(document);
        }
    }

    /// <summary>Removes the <c>spine/@toc</c> attribute.</summary>
    public void RemoveNcxOnSpine()
    {
        OpfDocument document = GetOpfDocument();
        document.SpineAttributes.Attributes.Remove("toc");
        UpdateText(document);
    }

    /// <summary>Updates the <c>href</c> of the NCX manifest entry (the one pointed at by <c>spine/@toc</c>).</summary>
    public void UpdateNcxLocationInManifest(Resource ncx)
    {
        ArgumentNullException.ThrowIfNull(ncx);
        OpfDocument document = GetOpfDocument();
        string ncxId = document.SpineAttributes.Attributes.Value("toc");
        if (ncxId.Length > 0 && document.IdToManifestPosition.TryGetValue(ncxId, out int pos))
        {
            document.Manifest[pos].Href = HrefForResource(ncx);
            UpdateText(document);
        }
    }

    // =====================================================================
    //  Guide / Landmarks
    // =====================================================================

    /// <summary>The guide semantic code for a resource (empty if none).</summary>
    public string GetGuideSemanticCodeForResource(Resource resource, string tgtId = "")
    {
        ArgumentNullException.ThrowIfNull(resource);
        OpfDocument document = GetOpfDocument();
        int pos = GetGuideReferencePos(document, resource, tgtId);
        return pos > -1 ? document.Guide[pos].Type : string.Empty;
    }

    /// <summary>The display name of the guide semantics for a resource.</summary>
    public string GetGuideSemanticNameForResource(Resource resource, string tgtId = "") =>
        GuideItems.GetName(GetGuideSemanticCodeForResource(resource, tgtId));

    /// <summary>A bookpath -&gt; list of semantic codes present in that file map.</summary>
    public IReadOnlyDictionary<string, IReadOnlyList<string>> GetSemanticCodeForPaths()
    {
        OpfDocument document = GetOpfDocument();
        Dictionary<string, List<string>> map = new(StringComparer.Ordinal);
        foreach (GuideEntry entry in document.Guide)
        {
            string bookPath = GuideHrefToBookPath(entry.Href, out _);
            if (!map.TryGetValue(bookPath, out List<string>? codes))
            {
                codes = map[bookPath] = new List<string>();
            }

            codes.Add(entry.Type);
        }

        return map.ToDictionary(kv => kv.Key, kv => (IReadOnlyList<string>)kv.Value, StringComparer.Ordinal);
    }

    /// <summary>A bookpath -&gt; list of semantic names present in that file map (plus the cover).</summary>
    public IReadOnlyDictionary<string, IReadOnlyList<string>> GetGuideSemanticNameForPaths()
    {
        OpfDocument document = GetOpfDocument();
        Dictionary<string, List<string>> map = new(StringComparer.Ordinal);
        foreach (GuideEntry entry in document.Guide)
        {
            string bookPath = GuideHrefToBookPath(entry.Href, out _);
            if (!map.TryGetValue(bookPath, out List<string>? names))
            {
                names = map[bookPath] = new List<string>();
            }

            names.Add(GuideItems.GetName(entry.Type));
        }

        int coverMeta = GetCoverMetaIndex(document);
        if (coverMeta > -1)
        {
            string coverId = document.Metadata[coverMeta].Attributes.Value("content");
            if (document.IdToManifestPosition.TryGetValue(coverId, out int pos))
            {
                string bookPath = Core.BookPath.BuildBookPath(Utility.UrlDecodePath(document.Manifest[pos].Href), Folder);
                map[bookPath] = new List<string> { GuideItems.GetName("cover") };
            }
        }

        return map.ToDictionary(kv => kv.Key, kv => (IReadOnlyList<string>)kv.Value, StringComparer.Ordinal);
    }

    /// <summary>All guide entries as records (bookpath, fragment, type, title).</summary>
    public IReadOnlyList<GuideInfo> GetAllGuideInfoByBookPath()
    {
        OpfDocument document = GetOpfDocument();
        List<GuideInfo> info = new();
        foreach (GuideEntry entry in document.Guide)
        {
            string bookPath = GuideHrefToBookPath(entry.Href, out string fragment);
            info.Add(new GuideInfo(bookPath, fragment, entry.Type, entry.Title));
        }

        return info;
    }

    /// <summary>Removes all guide entries.</summary>
    public void ClearSemanticCodesInGuide()
    {
        OpfDocument document = GetOpfDocument();
        document.Guide.Clear();
        UpdateText(document);
    }

    /// <summary>
    /// After a merge (Merge, epub2): redirects
    /// guide entries pointing at any of the merged (non-sink) files to
    /// <paramref name="sinkBookPath"/>, using <paramref name="sectionIdMap"/> when the entry had no
    /// fragment of its own (it pointed at the start of the merged file).
    /// </summary>
    public void UpdateGuideAfterMerge(
        IReadOnlyList<string> mergedBookPaths, string sinkBookPath, IReadOnlyDictionary<string, string> sectionIdMap)
    {
        ArgumentNullException.ThrowIfNull(mergedBookPaths);
        ArgumentNullException.ThrowIfNull(sinkBookPath);
        ArgumentNullException.ThrowIfNull(sectionIdMap);
        if (mergedBookPaths.Count == 0)
        {
            return;
        }

        HashSet<string> merged = new(mergedBookPaths, StringComparer.Ordinal);
        OpfDocument document = GetOpfDocument();
        bool changed = false;

        foreach (GuideEntry entry in document.Guide)
        {
            string bookPath = GuideHrefToBookPath(entry.Href, out string rawFragment);
            if (!merged.Contains(bookPath))
            {
                continue;
            }

            string fragment = Utility.UrlDecodePath(rawFragment);
            if (fragment.Length == 0 && sectionIdMap.TryGetValue(bookPath, out string? sectionId))
            {
                fragment = sectionId;
            }

            string newHref = Utility.UrlEncodePath(Core.BookPath.Relative(BookPath, sinkBookPath));
            if (fragment.Length > 0)
            {
                newHref += "#" + Utility.UrlEncodePath(fragment);
            }

            entry.Href = newHref;
            changed = true;
        }

        if (changed)
        {
            UpdateText(document);
        }
    }

    /// <summary>
    /// Assigns (or toggles) the guide semantic code for an HTML resource. When <paramref name="toggle"/>
    /// is on and the current code == the new one, the semantics is removed.
    /// </summary>
    public void AddGuideSemanticCode(Resource htmlResource, string code, bool toggle = true, string tgtId = "")
    {
        ArgumentNullException.ThrowIfNull(htmlResource);
        ArgumentNullException.ThrowIfNull(code);
        string lang = GetPrimaryBookLanguage();
        OpfDocument document = GetOpfDocument();
        string currentCode = GetGuideSemanticCodeForResource(document, htmlResource, tgtId);

        if (currentCode != code || !toggle)
        {
            RemoveDuplicateGuideCodes(document, code);
            SetGuideSemanticCodeForResource(document, code, htmlResource, lang, tgtId);
        }
        else
        {
            RemoveGuideReferenceForResource(document, htmlResource, tgtId);
        }

        UpdateText(document);
    }

    // =====================================================================
    //  Cover
    // =====================================================================

    /// <summary>Whether the image is marked as the cover (meta <c>name="cover"</c>).</summary>
    public bool IsCoverImage(ImageResource imageResource)
    {
        ArgumentNullException.ThrowIfNull(imageResource);
        OpfDocument document = GetOpfDocument();
        return IsCoverImageId(document, GetResourceManifestId(document, imageResource));
    }

    /// <summary>The bookpath of the cover image (from the <c>cover</c> meta or from <c>properties="cover-image"</c>).</summary>
    public string GetCoverImagePath()
    {
        OpfDocument document = GetOpfDocument();
        int pos = GetCoverMetaIndex(document);
        if (pos > -1)
        {
            string coverId = document.Metadata[pos].Attributes.Value("content");
            if (document.IdToManifestPosition.TryGetValue(coverId, out int manPos))
            {
                return Core.BookPath.BuildBookPath(Utility.UrlDecodePath(document.Manifest[manPos].Href), Folder);
            }
        }

        foreach (ManifestEntry entry in document.Manifest)
        {
            if (entry.Attributes.Value("properties").Contains("cover-image", StringComparison.Ordinal))
            {
                return Core.BookPath.BuildBookPath(Utility.UrlDecodePath(entry.Href), Folder);
            }
        }

        return string.Empty;
    }

    /// <summary>
    /// Marks an image as the cover: removes previous markings (meta + <c>properties</c> in EPUB 3),
    /// adds the <c>name="cover"</c> meta and — in EPUB 3 — <c>properties="cover-image"</c>.
    /// </summary>
    public void SetResourceAsCoverImage(ImageResource imageResource)
    {
        ArgumentNullException.ThrowIfNull(imageResource);
        OpfDocument document = GetOpfDocument();
        string resourceId = GetResourceManifestId(document, imageResource);

        string oldCoverId = string.Empty;
        int pos = GetCoverMetaIndex(document);
        if (pos > -1)
        {
            oldCoverId = document.Metadata[pos].Attributes.Value("content");
            document.Metadata.RemoveAt(pos);
        }

        if (oldCoverId.Length > 0 && document.Version.StartsWith('3'))
        {
            RemoveCoverImageProperty(document, oldCoverId);
        }

        AddCoverMetaForImage(document, imageResource);
        if (document.Version.StartsWith('3'))
        {
            AddCoverImageProperty(document, resourceId);
        }

        UpdateText(document);
    }

    // =====================================================================
    //  Well-formed fix
    // =====================================================================

    /// <summary>
    /// A minimal OPF repair: when the spine is completely empty, fills it with entries for the
    /// XHTML files from the manifest (sorted by <c>href</c>).
    /// </summary>
    public void AutoFixWellFormedErrors()
    {
        OpfDocument document = GetOpfDocument();
        if (document.Spine.Count == 0)
        {
            IEnumerable<(string Href, string Id)> texts = document.Manifest
                .Select(e => (Href: Utility.UrlDecodePath(e.Href), e.Id))
                .Where(e => IsTextManifestEntry(e.Href, document))
                .OrderBy(e => e.Href, StringComparer.Ordinal);
            foreach ((_, string id) in texts)
            {
                document.Spine.Add(new SpineEntry { IdRef = id });
            }
        }

        UpdateText(document);
    }

    // =====================================================================
    //  Implementation details
    // =====================================================================

    private static bool IsTextManifestEntry(string decodedHref, OpfDocument document)
    {
        int pos = document.Manifest.FindIndex(e => string.Equals(Utility.UrlDecodePath(e.Href), decodedHref, StringComparison.Ordinal));
        string mtype = pos >= 0 ? document.Manifest[pos].MediaType : string.Empty;
        string ext = SysPath.GetExtension(decodedHref).TrimStart('.').ToLowerInvariant();
        return string.Equals(mtype, "application/xhtml+xml", StringComparison.Ordinal) || TextExtensions.Contains(ext);
    }

    private string HrefForResource(Resource resource) => HrefForBookPath(resource.BookPath);

    private string HrefForBookPath(string resourceBookPath) => HrefFor(BookPath, resourceBookPath);

    private static string HrefFor(string opfBookPath, string resourceBookPath) =>
        Utility.UrlEncodePath(Core.BookPath.Relative(opfBookPath, resourceBookPath));

    private string FullPathToBookPath(string fullPath)
    {
        string mainFolder = MainFolder;
        return fullPath.Length > mainFolder.Length
            ? fullPath[(mainFolder.Length + 1)..].Replace('\\', '/')
            : fullPath.Replace('\\', '/');
    }

    private void AppendManifestEntry(OpfDocument document, Resource resource)
    {
        ManifestEntry entry = new()
        {
            Id = GetUniqueId(document, GetValidId(resource.Filename)),
            Href = HrefForResource(resource),
            MediaType = GetResourceMimetype(resource),
        };
        document.Manifest.Add(entry);
        document.RebuildManifestIndex();

        if (resource.Type == ResourceType.Html)
        {
            document.Spine.Add(new SpineEntry { IdRef = entry.Id });
        }
    }

    private void RemoveManifestEntry(OpfDocument document, Resource resource)
    {
        string href = HrefForResource(resource);
        int pos = document.HrefToManifestPosition.TryGetValue(href, out int found) ? found : -1;
        string itemId = pos > -1 ? document.Manifest[pos].Id : string.Empty;

        if (resource.Type == ResourceType.Image)
        {
            RemoveCoverMetaForImage(document, resource);
        }

        if (resource.Type == ResourceType.Html)
        {
            if (itemId.Length > 0)
            {
                int spineIndex = document.Spine.FindIndex(s => string.Equals(s.IdRef, itemId, StringComparison.Ordinal));
                if (spineIndex >= 0)
                {
                    document.Spine.RemoveAt(spineIndex);
                }
            }

            RemoveAllGuideReferencesForResource(document, resource);
        }

        if (pos > -1)
        {
            document.Manifest.RemoveAt(pos);
            document.RebuildManifestIndex();
        }

        document.Guide.RemoveAll(g => string.Equals(GuideHrefToBookPath(g.Href, out _), resource.BookPath, StringComparison.Ordinal));
    }

    private void BulkUpdateHrefs(IReadOnlyDictionary<string, Resource> oldBookPathToResource)
    {
        ArgumentNullException.ThrowIfNull(oldBookPathToResource);
        OpfDocument document = GetOpfDocument();
        string opfStartDir = Core.BookPath.StartingDir(BookPath);
        foreach (ManifestEntry entry in document.Manifest)
        {
            string bookPath = Core.BookPath.BuildBookPath(Utility.UrlDecodePath(entry.Href), opfStartDir);
            if (oldBookPathToResource.TryGetValue(bookPath, out Resource? resource))
            {
                entry.Href = HrefForResource(resource);
            }
        }

        UpdateText(document);
    }

    private static int GetMainIdentifierIndex(OpfDocument document)
    {
        string uniqueId = document.Package.UniqueIdentifier;
        for (int i = 0; i < document.Metadata.Count; i++)
        {
            MetaEntry meta = document.Metadata[i];
            if (string.Equals(meta.Name, "dc:identifier", StringComparison.Ordinal)
                && meta.Attributes.Value("id") == uniqueId)
            {
                return i;
            }
        }

        return -1;
    }

    private static int GetCoverMetaIndex(OpfDocument document)
    {
        for (int i = 0; i < document.Metadata.Count; i++)
        {
            MetaEntry meta = document.Metadata[i];
            if (string.Equals(meta.Name, "meta", StringComparison.Ordinal) && meta.Attributes.Value("name") == "cover")
            {
                return i;
            }
        }

        return -1;
    }

    private static bool IsCoverImageId(OpfDocument document, string resourceId)
    {
        int pos = GetCoverMetaIndex(document);
        return pos > -1 && document.Metadata[pos].Attributes.Value("content") == resourceId;
    }

    private string GetResourceManifestId(OpfDocument document, Resource resource)
    {
        string href = HrefForResource(resource);
        return document.HrefToManifestPosition.TryGetValue(href, out int pos) ? document.Manifest[pos].Id : string.Empty;
    }

    private static string GetUniqueId(OpfDocument document, string preferred, string allowedExisting = "")
    {
        if (!document.IdToManifestPosition.ContainsKey(preferred)
            || string.Equals(preferred, allowedExisting, StringComparison.Ordinal))
        {
            return preferred;
        }

        return "x" + Ulid.NewUlid().ToString();
    }

    private static string GetResourceMimetype(Resource resource)
    {
        string mimeType = resource.MediaType;
        if (string.IsNullOrEmpty(mimeType))
        {
            string extension = SysPath.GetExtension(resource.FullPath).TrimStart('.').ToLowerInvariant();
            mimeType = MediaTypes.GetMediaTypeFromExtension(extension);
        }

        return mimeType;
    }

    private static void RemoveDcElements(OpfDocument document)
    {
        int mainIndex = GetMainIdentifierIndex(document);
        for (int i = document.Metadata.Count - 1; i >= 0; i--)
        {
            if (i != mainIndex && document.Metadata[i].Name.StartsWith("dc:", StringComparison.Ordinal))
            {
                document.Metadata.RemoveAt(i);
            }
        }
    }

    private void WriteIdentifier(string metaName, string metaValue, OpfDocument document)
    {
        int pos = GetMainIdentifierIndex(document);
        if (pos > -1)
        {
            MetaEntry existing = document.Metadata[pos];
            string scheme = existing.Attributes.Value("scheme");
            if (scheme.Length == 0 && existing.Content.StartsWith("urn:uuid:", StringComparison.Ordinal))
            {
                scheme = "UUID";
            }

            if (metaValue == existing.Content && metaName == scheme)
            {
                return;
            }
        }

        MetaEntry entry = new() { Name = "dc:identifier" };
        if (EpubVersion.StartsWith('2'))
        {
            entry.Attributes.Set("opf:scheme", metaName);
        }

        entry.Content = string.Equals(metaName, "uuid", StringComparison.OrdinalIgnoreCase)
                        && !metaValue.Contains("urn:uuid:", StringComparison.Ordinal)
            ? "urn:uuid:" + metaValue
            : metaValue;
        document.Metadata.Add(entry);
    }

    private static void AddPropertyToken(ManifestEntry entry, string token)
    {
        List<string> tokens = entry.Attributes.Value("properties")
            .Split(' ', StringSplitOptions.RemoveEmptyEntries).ToList();
        if (!tokens.Contains(token))
        {
            tokens.Add(token);
        }

        entry.Attributes.Remove("properties");
        if (tokens.Count > 0)
        {
            entry.Attributes.Set("properties", string.Join(' ', tokens));
        }
    }

    private static void RemoveCoverImageProperty(OpfDocument document, string resourceId)
    {
        if (resourceId.Length == 0 || !document.IdToManifestPosition.TryGetValue(resourceId, out int pos))
        {
            return;
        }

        List<string> tokens = document.Manifest[pos].Attributes.Value("properties")
            .Split(' ', StringSplitOptions.RemoveEmptyEntries)
            .Where(t => !string.Equals(t, "cover-image", StringComparison.Ordinal)).ToList();
        document.Manifest[pos].Attributes.Remove("properties");
        if (tokens.Count > 0)
        {
            document.Manifest[pos].Attributes.Set("properties", string.Join(' ', tokens));
        }
    }

    private static void AddCoverImageProperty(OpfDocument document, string resourceId)
    {
        if (resourceId.Length > 0 && document.IdToManifestPosition.TryGetValue(resourceId, out int pos))
        {
            AddPropertyToken(document.Manifest[pos], "cover-image");
        }
    }

    private void AddCoverMetaForImage(OpfDocument document, Resource resource)
    {
        string resourceId = GetResourceManifestId(document, resource);
        int pos = GetCoverMetaIndex(document);
        if (pos > -1)
        {
            document.Metadata[pos].Attributes.Set("content", resourceId);
        }
        else
        {
            MetaEntry meta = new() { Name = "meta" };
            meta.Attributes.Set("name", "cover");
            meta.Attributes.Set("content", resourceId);
            document.Metadata.Add(meta);
        }
    }

    private void RemoveCoverMetaForImage(OpfDocument document, Resource resource)
    {
        int pos = GetCoverMetaIndex(document);
        if (pos > -1 && document.Metadata[pos].Attributes.Value("content") == GetResourceManifestId(document, resource))
        {
            document.Metadata.RemoveAt(pos);
        }
    }

    private int GetGuideReferencePos(OpfDocument document, Resource resource, string tgtId)
    {
        string target = HrefForResource(resource);
        if (!string.IsNullOrEmpty(tgtId))
        {
            target = target + "#" + tgtId;
        }

        for (int i = 0; i < document.Guide.Count; i++)
        {
            if (string.Equals(document.Guide[i].Href, target, StringComparison.Ordinal))
            {
                return i;
            }
        }

        return -1;
    }

    private string GetGuideSemanticCodeForResource(OpfDocument document, Resource resource, string tgtId)
    {
        int pos = GetGuideReferencePos(document, resource, tgtId);
        return pos > -1 ? document.Guide[pos].Type : string.Empty;
    }

    private static void RemoveDuplicateGuideCodes(OpfDocument document, string code)
    {
        if (code.Length == 0)
        {
            return;
        }

        for (int i = document.Guide.Count - 1; i >= 0; i--)
        {
            if (string.Equals(document.Guide[i].Type, code, StringComparison.Ordinal))
            {
                document.Guide.RemoveAt(i);
            }
        }
    }

    private void RemoveGuideReferenceForResource(OpfDocument document, Resource resource, string tgtId)
    {
        int pos = GetGuideReferencePos(document, resource, tgtId);
        while (pos > -1 && document.Guide.Count > 0)
        {
            document.Guide.RemoveAt(pos);
            pos = GetGuideReferencePos(document, resource, tgtId);
        }
    }

    private void RemoveAllGuideReferencesForResource(OpfDocument document, Resource resource)
    {
        string href = HrefForResource(resource);
        for (int i = document.Guide.Count - 1; i >= 0; i--)
        {
            string entryPath = document.Guide[i].Href.Split('#')[0];
            if (string.Equals(entryPath, href, StringComparison.Ordinal))
            {
                document.Guide.RemoveAt(i);
            }
        }
    }

    private void SetGuideSemanticCodeForResource(OpfDocument document, string code, Resource resource, string lang, string tgtId)
    {
        if (code.Length == 0)
        {
            return;
        }

        string title = GuideItems.GetTitle(code, lang);
        int pos = GetGuideReferencePos(document, resource, tgtId);
        if (pos > -1)
        {
            document.Guide[pos].Type = code;
            document.Guide[pos].Title = title;
            return;
        }

        string href = HrefForResource(resource);
        if (!string.IsNullOrEmpty(tgtId))
        {
            href = href + "#" + tgtId;
        }

        document.Guide.Add(new GuideEntry { Type = code, Title = title, Href = href });
    }

    private string GuideHrefToBookPath(string href, out string fragment)
    {
        string[] parts = href.Split('#', 2);
        fragment = parts.Length > 1 ? parts[1] : string.Empty;
        return Core.BookPath.BuildBookPath(Utility.UrlDecodePath(parts[0]), Folder);
    }

    private static HashSet<string> CollectUsedIds(OpfDocument document)
    {
        HashSet<string> ids = new(StringComparer.Ordinal);
        foreach (ManifestEntry entry in document.Manifest)
        {
            if (entry.Id.Length > 0)
            {
                ids.Add(entry.Id);
            }
        }

        foreach (SpineEntry entry in document.Spine)
        {
            string id = entry.Attributes.Value("id");
            if (id.Length > 0)
            {
                ids.Add(id);
            }
        }

        foreach (MetaEntry meta in document.Metadata)
        {
            string id = meta.Attributes.Value("id");
            if (id.Length > 0)
            {
                ids.Add(id);
            }
        }

        return ids;
    }

    private static string GenerateBaseIdFromFilename(string filename)
    {
        string name = SysPath.GetFileNameWithoutExtension(filename);
        string extension = SysPath.GetExtension(filename);
        if (extension.Length > 0)
        {
            name += extension.Replace('.', '_');
        }

        return AsciiFy.ConvertToPlainAscii(name.Trim());
    }

    private static string GenerateUniqueId(string value, HashSet<string> usedIds)
    {
        string baseValue = value;
        int count = 0;
        while (usedIds.Contains(value))
        {
            count++;
            value = baseValue + count.ToString("D4", CultureInfo.InvariantCulture);
            if (count >= 10000)
            {
                return "x" + Ulid.NewUlid().ToString();
            }
        }

        return value;
    }
}

/// <summary>A single <c>&lt;guide&gt;</c> entry split into parts.</summary>
/// <param name="BookPath">The bookpath of the target resource.</param>
/// <param name="Fragment">The fragment (without <c>#</c>) or <c>""</c>.</param>
/// <param name="Type">The semantic code (<c>reference/@type</c>).</param>
/// <param name="Title">The title (<c>reference/@title</c>).</param>
public readonly record struct GuideInfo(string BookPath, string Fragment, string Type, string Title);

/// <summary>A manifest identifier that <see cref="OpfResource.RebaseManifestIds"/> would change.</summary>
/// <param name="OldId">The current identifier.</param>
/// <param name="NewId">The identifier based on the file name.</param>
/// <param name="BookPath">The bookpath of the entry's file (the simulated one when planned on a simulated state).</param>
public sealed record ManifestIdChange(string OldId, string NewId, string BookPath);

/// <summary>A manifest <c>media-type</c> that <see cref="OpfResource.UpdateManifestMediaTypes"/> would change.</summary>
/// <param name="Resource">The resource of the manifest entry.</param>
/// <param name="OldMediaType">The <c>media-type</c> in the manifest.</param>
/// <param name="NewMediaType">The media type of the resource.</param>
public sealed record ManifestMediaTypeChange(Resource Resource, string OldMediaType, string NewMediaType);
