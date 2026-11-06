using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using Signet.EpubCheck.Core.Galimatias;
using Signet.EpubCheck.Core.Ocf;
using Signet.EpubCheck.Core.Opf;
using Signet.EpubCheck.Core.Reporting;
using Signet.EpubCheck.Core.Util;

namespace Signet.EpubCheck.Core.References;

/// <summary>What a reference to a resource is (upstream's <c>Reference.Type</c>).</summary>
public enum ReferenceType
{
    /// <summary>A <c>link</c> of the package document.</summary>
    Link,

    /// <summary>Any other reference to a resource of the publication.</summary>
    Generic,

    /// <summary>A style sheet.</summary>
    Stylesheet,

    /// <summary>A media overlay.</summary>
    MediaOverlay,

    /// <summary>A hyperlink.</summary>
    Hyperlink,

    /// <summary>A font.</summary>
    Font,

    /// <summary>An image.</summary>
    Image,

    /// <summary>An audio resource.</summary>
    Audio,

    /// <summary>A video resource.</summary>
    Video,

    /// <summary>A text track.</summary>
    Track,

    /// <summary>A citation (<c>cite</c>).</summary>
    Cite,

    /// <summary>A script.</summary>
    Script,

    /// <summary>An SVG paint server.</summary>
    SvgPaint,

    /// <summary>An SVG clip path.</summary>
    SvgClipPath,

    /// <summary>An SVG symbol.</summary>
    SvgSymbol,

    /// <summary>A region-based navigation reference.</summary>
    RegionBasedNav,

    /// <summary>A search key reference.</summary>
    SearchKey,

    /// <summary>A link of the table of contents.</summary>
    NavTocLink,

    /// <summary>A link of the page list.</summary>
    NavPagelistLink,

    /// <summary>A link from a media overlay to its text.</summary>
    OverlayTextLink,
}

/// <summary>Helpers of <see cref="ReferenceType"/>.</summary>
public static class ReferenceTypes
{
    /// <summary>Whether a reference of this type makes the resource it refers to a used one of the publication (upstream's <c>isPublicationResourceReference</c>).</summary>
    public static bool IsPublicationResourceReference(this ReferenceType type) =>
        type is ReferenceType.Generic or ReferenceType.Stylesheet or ReferenceType.Font or ReferenceType.Image or ReferenceType.Audio
            or ReferenceType.Video or ReferenceType.Track or ReferenceType.MediaOverlay or ReferenceType.Script;
}

/// <summary>A reference from a document to a resource (upstream's <c>Reference</c>).</summary>
public sealed class Reference
{
    /// <summary>Creates the reference.</summary>
    public Reference(Url url, ReferenceType type, EpubLocation location, bool hasIntrinsicFallback)
    {
        Url = url ?? throw new ArgumentNullException(nameof(url));
        Type = type;
        Location = location ?? throw new ArgumentNullException(nameof(location));
        TargetResource = url.WithFragment(null);
        HasIntrinsicFallback = hasIntrinsicFallback;
    }

    /// <summary>The URL referred to.</summary>
    public Url Url { get; }

    /// <summary>The URL without its fragment: the resource.</summary>
    public Url TargetResource { get; }

    /// <summary>Where the reference is.</summary>
    public EpubLocation Location { get; }

    /// <summary>What kind of reference.</summary>
    public ReferenceType Type { get; }

    /// <summary>Whether the element has a fallback of its own (an <c>object</c> with content, <c>picture</c>, …).</summary>
    public bool HasIntrinsicFallback { get; }

    /// <inheritdoc />
    public override string ToString() => Url.ToString();
}

/// <summary>The references of the publication, in the order they were registered (upstream's <c>ReferenceRegistry</c>).</summary>
public sealed class ReferenceRegistry(OCFContainer container, ResourceRegistry resourceRegistry)
{
    private readonly OCFContainer _container = container ?? throw new ArgumentNullException(nameof(container));
    private readonly ResourceRegistry _resourceRegistry = resourceRegistry ?? throw new ArgumentNullException(nameof(resourceRegistry));
    private readonly List<Reference> _references = [];
    private readonly Lock _lock = new();

    /// <summary>The references registered so far.</summary>
    public IReadOnlyList<Reference> AsList()
    {
        lock (_lock)
        {
            return [.. _references];
        }
    }

    /// <summary>Whether a reference refers exactly to <paramref name="resource"/>.</summary>
    public bool IsReferenced(Url resource) => AsList().Any(reference => reference.Url.Equals(resource));

    /// <summary>Whether a reference refers to the resource <paramref name="resource"/> (the fragment aside).</summary>
    public bool HasReferencesTo(Url? resource) => resource is not null && AsList().Any(reference => resource.Equals(reference.TargetResource));

    /// <summary>Registers a reference; one with a query to a resource of the container loses its query. A <c>data:</c> URL registers its resource.</summary>
    public void RegisterReference(Url? url, ReferenceType type, EpubLocation location, bool hasIntrinsicFallback = false)
    {
        if (url is null)
        {
            return;
        }

        if (url.IsHierarchical && url.Query is not null && !_container.IsRemote(url))
        {
            url = url.WithQuery(null);
        }

        lock (_lock)
        {
            _references.Add(new Reference(url, type, location, hasIntrinsicFallback));
        }

        if (url.Scheme == "data")
        {
            _resourceRegistry.RegisterResource(url, EpubCheckUrlUtils.GetDataUrlType(url));
        }
    }
}

/// <summary>A resource of the publication: the package document, an item of the manifest, or a <c>data:</c> URL (upstream's <c>Resource</c>).</summary>
public sealed class Resource
{
    private Resource(Url url, string mimeType, OPFItem? item)
    {
        Url = url;
        MimeType = mimeType;
        Item = item;
    }

    /// <summary>The URL.</summary>
    public Url Url { get; }

    /// <summary>The media type.</summary>
    public string MimeType { get; }

    /// <summary>The manifest item, or <c>null</c>.</summary>
    public OPFItem? Item { get; }

    /// <summary>Whether the resource has a manifest item.</summary>
    public bool HasItem => Item is not null;

    /// <summary>Whether a fallback chain of the item reaches a core media type.</summary>
    public bool HasCoreMediaTypeFallback => Item is { HasCoreMediaTypeFallback: true };

    /// <summary>Whether a fallback chain of the item reaches a content document.</summary>
    public bool HasContentDocumentFallback => Item is { HasContentDocumentFallback: true };

    /// <summary>Whether the item has a manifest fallback.</summary>
    public bool HasManifestFallback => Item is { HasFallback: true };

    /// <summary>Whether the item is in the spine.</summary>
    public bool IsInSpine => Item is { IsInSpine: true };

    internal static Resource FromItem(OPFItem item) => new(item.Url, item.MimeType, item);

    internal static Resource FromUrl(Url url, string? mimeType) => new(url, Util.Mime.MimeType.Normalize(mimeType), null);

    /// <inheritdoc />
    public override string ToString() => Url.ToString();
}

/// <summary>The resources of the publication and the ids defined in them (upstream's <c>ResourceRegistry</c>).</summary>
public sealed class ResourceRegistry
{
    private readonly Dictionary<Url, Resource> _resources = [];
    private readonly Dictionary<Url, List<string>> _ids = [];
    private readonly Dictionary<(Url, string), ReferenceType> _idTypes = [];
    private readonly Lock _lock = new();

    /// <summary>Registers the id <paramref name="id"/> defined in the resource at <paramref name="resourceUrl"/>.</summary>
    public void RegisterId(string? id, ReferenceType type, Url resourceUrl)
    {
        if (id is null)
        {
            return;
        }

        ArgumentNullException.ThrowIfNull(resourceUrl);
        if (!_resources.ContainsKey(resourceUrl))
        {
            throw new ArgumentException("resource not registered", nameof(resourceUrl));
        }

        lock (_lock)
        {
            if (!_ids.TryGetValue(resourceUrl, out List<string>? ids))
            {
                _ids[resourceUrl] = ids = [];
            }

            if (!ids.Contains(id))
            {
                ids.Add(id);
                _idTypes[(resourceUrl, id)] = type;
            }
        }
    }

    /// <summary>The place of the id in the resource, counted from 1 (0 for no id, -1 when the resource does not define it).</summary>
    public int GetIdPosition(string? id, Resource resource)
    {
        ArgumentNullException.ThrowIfNull(resource);
        if (string.IsNullOrWhiteSpace(id))
        {
            return 0;
        }

        int position = _ids.TryGetValue(resource.Url, out List<string>? ids) ? ids.IndexOf(id) : -1;
        return position == -1 ? -1 : position + 1;
    }

    /// <summary>The kind of the element that defines the id, or <c>null</c>.</summary>
    public ReferenceType? GetIdType(string id, Resource resource)
    {
        ArgumentNullException.ThrowIfNull(resource);
        return _idTypes.TryGetValue((resource.Url, id), out ReferenceType type) ? type : null;
    }

    /// <summary>Registers the resource of a manifest item.</summary>
    public void RegisterResource(OPFItem item)
    {
        ArgumentNullException.ThrowIfNull(item);
        _resources.TryAdd(item.Url, Resource.FromItem(item));
    }

    /// <summary>Registers a resource that has no manifest item.</summary>
    public void RegisterResource(Url url, string? mimeType)
    {
        ArgumentNullException.ThrowIfNull(url);
        _resources.TryAdd(url, Resource.FromUrl(url, mimeType));
    }

    /// <summary>The media type of the resource, or <c>null</c> when it is not registered.</summary>
    public string? GetMimeType(Url resource) => _resources.TryGetValue(resource, out Resource? found) ? found.MimeType : null;

    /// <summary>The manifest item of the resource, or <c>null</c>.</summary>
    public OPFItem? GetOpfItem(Url url) => _resources.TryGetValue(url, out Resource? found) ? found.Item : null;

    /// <summary>The resource, or <c>null</c>.</summary>
    public Resource? GetResource(Url url) => _resources.GetValueOrDefault(url);
}
