using System;
using System.Collections.Generic;
using System.Text;

namespace Signet.Core;

/// <summary>
/// An in-memory model of an OPF document (<c>content.opf</c> / <c>package.opf</c>): the <c>package</c> element,
/// metadata, manifest, spine, guide and bindings — preserving order and unknown attributes.
/// </summary>
/// <remarks>
/// Parsing is tolerant (<see cref="MarkupTokenizer"/>) — a syntactically invalid file
/// does not throw; a "best effort" model is built (well-formed validation is done separately).
/// <see cref="ToXml"/> regenerates the XML in a fixed format,
/// not byte for byte relative to the input; the <c>Parse → ToXml → Parse</c> round trip is stable.
/// Comments, CDATA and processing instructions in the OPF are not persisted.
/// </remarks>
public sealed class OpfDocument
{
    private const string OpfNamespace = "http://www.idpf.org/2007/opf";

    private static readonly string[] DeprecatedMetadataWrappers = { "dc-metadata", "x-metadata" };

    /// <summary>The <c>package</c> element (version, unique-identifier, other attributes).</summary>
    public PackageEntry Package { get; private set; } = new();

    /// <summary>The attributes of the <c>metadata</c> element.</summary>
    public MetadataAttributesEntry MetadataAttributes { get; private set; } = new();

    /// <summary>The metadata entries in order of occurrence.</summary>
    public List<MetaEntry> Metadata { get; } = new();

    /// <summary>The manifest entries in order of occurrence.</summary>
    public List<ManifestEntry> Manifest { get; } = new();

    /// <summary>The attributes of the <c>spine</c> element (including <c>toc</c> pointing at the NCX).</summary>
    public SpineAttributesEntry SpineAttributes { get; private set; } = new();

    /// <summary>The spine entries in reading order.</summary>
    public List<SpineEntry> Spine { get; } = new();

    /// <summary>The guide entries (EPUB 2) in order of occurrence.</summary>
    public List<GuideEntry> Guide { get; } = new();

    /// <summary>The bindings entries (EPUB 3, deprecated) in order of occurrence.</summary>
    public List<BindingsEntry> Bindings { get; } = new();

    /// <summary>A map of manifest entry <c>id</c> -&gt; its position (the index in <see cref="Manifest"/>).</summary>
    public IReadOnlyDictionary<string, int> IdToManifestPosition => _idToManifestPosition;

    /// <summary>A map of manifest entry <c>href</c> -&gt; its position (the index in <see cref="Manifest"/>).</summary>
    public IReadOnlyDictionary<string, int> HrefToManifestPosition => _hrefToManifestPosition;

    private readonly Dictionary<string, int> _idToManifestPosition = new(StringComparer.Ordinal);
    private readonly Dictionary<string, int> _hrefToManifestPosition = new(StringComparer.Ordinal);

    /// <summary>The OPF version from the <c>package</c> element (e.g. <c>2.0</c>, <c>3.0</c>).</summary>
    public string Version => Package.Version;

    /// <summary>The value of <c>package/@unique-identifier</c>.</summary>
    public string UniqueIdentifierId => Package.UniqueIdentifier;

    /// <summary>Parses OPF text into the model.</summary>
    /// <param name="source">The content of the OPF file.</param>
    /// <returns>The populated <see cref="OpfDocument"/>.</returns>
    public static OpfDocument Parse(string source)
    {
        ArgumentNullException.ThrowIfNull(source);
        OpfDocument document = new();
        document.ParseInternal(source);
        return document;
    }

    /// <summary>
    /// Serializes just the <c>&lt;metadata&gt;…&lt;/metadata&gt;</c> block.
    /// </summary>
    public string GetMetadataXml()
    {
        StringBuilder sb = new();
        sb.Append(MetadataAttributes.ToXml());
        foreach (MetaEntry entry in Metadata)
        {
            sb.Append(entry.ToXml());
        }

        sb.Append("  </metadata>\n");
        return sb.ToString();
    }

    /// <summary>
    /// Serializes the whole OPF document in a fixed format:
    /// an XML declaration with <c>encoding="utf-8"</c>, 2/4-space indents, <c>\n</c> line endings.
    /// <c>guide</c> is omitted when empty; <c>bindings</c> only for versions other than 2.x.
    /// </summary>
    public string ToXml()
    {
        StringBuilder sb = new();
        sb.Append("<?xml version=\"1.0\" encoding=\"utf-8\"?>\n");
        sb.Append(Package.ToXml());
        sb.Append(MetadataAttributes.ToXml());
        foreach (MetaEntry entry in Metadata)
        {
            sb.Append(entry.ToXml());
        }

        sb.Append("  </metadata>\n");
        sb.Append("  <manifest>\n");
        foreach (ManifestEntry entry in Manifest)
        {
            sb.Append(entry.ToXml());
        }

        sb.Append("  </manifest>\n");
        sb.Append(SpineAttributes.ToXml());
        foreach (SpineEntry entry in Spine)
        {
            sb.Append(entry.ToXml());
        }

        sb.Append("  </spine>\n");

        if (Guide.Count > 0)
        {
            sb.Append("  <guide>\n");
            foreach (GuideEntry entry in Guide)
            {
                sb.Append(entry.ToXml());
            }

            sb.Append("  </guide>\n");
        }

        if (Bindings.Count > 0 && !Package.Version.StartsWith('2'))
        {
            sb.Append("  <bindings>\n");
            foreach (BindingsEntry entry in Bindings)
            {
                sb.Append(entry.ToXml());
            }

            sb.Append("  </bindings>\n");
        }

        sb.Append("</package>\n");
        return sb.ToString();
    }

    /// <summary>Rebuilds the <see cref="IdToManifestPosition"/> / <see cref="HrefToManifestPosition"/> maps after the manifest changes.</summary>
    public void RebuildManifestIndex()
    {
        _idToManifestPosition.Clear();
        _hrefToManifestPosition.Clear();
        for (int i = 0; i < Manifest.Count; i++)
        {
            _idToManifestPosition[Manifest[i].Id] = i;
            _hrefToManifestPosition[Manifest[i].Href] = i;
        }
    }

    private void ParseInternal(string source)
    {
        MarkupTokenizer tokenizer = new(source);
        string textContent = string.Empty;
        MarkupToken? matchingBeginTag = null;
        int autoIdCounter = 0;
        int manifestPosition = 0;
        bool captureContent = false;

        _idToManifestPosition.Clear();
        _hrefToManifestPosition.Clear();

        while (true)
        {
            MarkupToken? token = tokenizer.ParseNext();
            if (token is null)
            {
                break;
            }

            if (token.Text.Length > 0)
            {
                if (captureContent)
                {
                    textContent = token.Text.Trim();
                }

                continue;
            }

            string tagPath = token.TagPath;
            bool beginOrSingle = token.TagType is MarkupTokenType.Begin or MarkupTokenType.Single;

            // manifest
            if (tagPath.Contains("manifest", StringComparison.Ordinal) && beginOrSingle)
            {
                if (token.TagName == "item")
                {
                    string fallbackId = "xid" + autoIdCounter.ToString("D3", System.Globalization.CultureInfo.InvariantCulture);
                    autoIdCounter++;
                    string id = token.Attributes.Value("id", fallbackId);
                    token.Attributes.Remove("id");

                    string href = token.Attributes.Value("href");
                    if (!href.Contains(':', StringComparison.Ordinal))
                    {
                        href = Utility.UrlEncodePath(Utility.UrlDecodePath(href));
                    }

                    token.Attributes.Remove("href");
                    string mediaType = token.Attributes.Value("media-type");
                    token.Attributes.Remove("media-type");

                    Manifest.Add(new ManifestEntry
                    {
                        Id = id,
                        Href = href,
                        MediaType = mediaType,
                        Attributes = token.Attributes,
                    });
                    _idToManifestPosition[id] = manifestPosition;
                    _hrefToManifestPosition[href] = manifestPosition;
                    manifestPosition++;
                }

                continue;
            }

            // spine opening tag (attributes: toc, page-progression-direction, ...)
            if (token.TagName == "spine" && token.TagType == MarkupTokenType.Begin)
            {
                SpineAttributes = new SpineAttributesEntry { Attributes = token.Attributes };
                continue;
            }

            if (tagPath.Contains("spine", StringComparison.Ordinal) && beginOrSingle)
            {
                if (token.TagName == "itemref")
                {
                    string idref = token.Attributes.Value("idref");
                    token.Attributes.Remove("idref");
                    Spine.Add(new SpineEntry { IdRef = idref, Attributes = token.Attributes });
                }

                continue;
            }

            // metadata opening tag
            if (token.TagName == "metadata" && token.TagType == MarkupTokenType.Begin)
            {
                if (!token.Attributes.Contains("xmlns:opf"))
                {
                    token.Attributes.Set("xmlns:opf", OpfNamespace);
                }

                MetadataAttributes = new MetadataAttributesEntry { Attributes = token.Attributes };
                continue;
            }

            // drop deprecated <dc-metadata> / <x-metadata> wrappers
            if (tagPath.Contains("metadata", StringComparison.Ordinal) &&
                Array.IndexOf(DeprecatedMetadataWrappers, token.TagName) >= 0)
            {
                continue;
            }

            if (tagPath.Contains("metadata", StringComparison.Ordinal) && token.TagType == MarkupTokenType.Begin)
            {
                matchingBeginTag = token;
                captureContent = true;
                continue;
            }

            if (tagPath.Contains("metadata", StringComparison.Ordinal) &&
                token.TagType is MarkupTokenType.Single or MarkupTokenType.End)
            {
                if (token.TagType == MarkupTokenType.Single)
                {
                    Metadata.Add(new MetaEntry { Name = token.TagName, Content = string.Empty, Attributes = token.Attributes });
                }
                else
                {
                    Metadata.Add(new MetaEntry
                    {
                        Name = matchingBeginTag?.TagName ?? string.Empty,
                        Content = textContent,
                        Attributes = matchingBeginTag?.Attributes ?? new TagAttributes(),
                    });
                    matchingBeginTag = null;
                    textContent = string.Empty;
                    captureContent = false;
                }

                continue;
            }

            // package
            if (token.TagName == "package" && token.TagType == MarkupTokenType.Begin)
            {
                string version = token.Attributes.Value("version", "2.0");
                token.Attributes.Remove("version");
                string uniqueId = token.Attributes.Value("unique-identifier", "bookid");
                token.Attributes.Remove("unique-identifier");
                if (tokenizer.NsRemapNeeded)
                {
                    token.Attributes.Remove("xmlns:" + tokenizer.OldPrefix);
                    token.Attributes.Set("xmlns", OpfNamespace);
                }

                Package = new PackageEntry { Version = version, UniqueIdentifier = uniqueId, Attributes = token.Attributes };
                continue;
            }

            // guide
            if (tagPath.Contains("guide", StringComparison.Ordinal) && beginOrSingle)
            {
                if (token.TagName == "reference")
                {
                    Guide.Add(new GuideEntry
                    {
                        Type = token.Attributes.Value("type"),
                        Title = token.Attributes.Value("title"),
                        Href = token.Attributes.Value("href"),
                    });
                }

                continue;
            }

            // bindings
            if (tagPath.Contains("bindings", StringComparison.Ordinal) && beginOrSingle)
            {
                if (token.TagName is "mediaType" or "mediatype")
                {
                    Bindings.Add(new BindingsEntry
                    {
                        MediaType = token.Attributes.Value("media-type"),
                        Handler = token.Attributes.Value("handler"),
                    });
                }

                continue;
            }
        }
    }
}
