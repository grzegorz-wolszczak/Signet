using System;
using System.Collections.Generic;
using System.Linq;
using Signet.EpubCheck.Core.Galimatias;
using Signet.EpubCheck.Core.Ocf;
using Signet.EpubCheck.Core.Reporting;
using Signet.EpubCheck.Core.Util;
using Signet.EpubCheck.Core.Vocab;

namespace Signet.EpubCheck.Core.Opf;

/// <summary>An item of the manifest (upstream's <c>OPFItem</c>): its id, URL, media type, fallbacks, properties and place in the spine.</summary>
public sealed class OPFItem : IEquatable<OPFItem>
{
    private const int DataPathLength = 30;
    private const string Ellipsis = "…";
    private const string DataScheme = "data";

    internal OPFItem(OPFItemBuilder builder)
    {
        ArgumentNullException.ThrowIfNull(builder);
        if (builder.Id is null)
        {
            throw new InvalidOperationException("item ID is null");
        }

        if (builder.Url is null)
        {
            throw new InvalidOperationException("item path is null");
        }

        if (builder.Location is null)
        {
            throw new InvalidOperationException("item location is null");
        }

        List<VocabProperty> properties = [.. builder.PropertyList];
        if (builder.SpinePosition < 0 || !builder.Linear)
        {
            AddOnce(properties, EpubCheckVocab.Vocab.Get(EpubCheckVocab.Property.NonLinear));
        }

        if (builder.FixedLayout)
        {
            AddOnce(properties, EpubCheckVocab.Vocab.Get(EpubCheckVocab.Property.FixedLayout));
        }

        Id = builder.Id.Trim();
        Url = builder.Url;
        MimeType = builder.MimeType;
        Location = builder.Location;
        HasFallback = builder.HasFallback;
        HasCoreMediaTypeFallback = builder.HasCoreMediaTypeFallback;
        HasContentDocumentFallback = builder.HasContentDocumentFallback;
        Properties = properties;
        IsNcx = builder.Ncx;
        IsInSpine = builder.SpinePosition > -1;
        SpinePosition = builder.SpinePosition;
        IsNav = properties.Contains(PackageVocabs.ItemVocab.Get(PackageVocabs.ItemProperty.Nav));
        IsScripted = properties.Contains(PackageVocabs.ItemVocab.Get(PackageVocabs.ItemProperty.Scripted));
        linear = builder.Linear;
        IsFixedLayout = builder.FixedLayout;
        MediaOverlay = builder.MediaOverlay;
        IsRemote = builder.Remote;
        Path = ComputePath(builder);
    }

    private readonly bool linear;

    /// <summary>The id, trimmed.</summary>
    public string Id { get; }

    /// <summary>The URL of the resource.</summary>
    public Url Url { get; }

    /// <summary>The path shown in messages: relative to the container, or the URL of a remote resource.</summary>
    public string Path { get; }

    /// <summary>The normalized media type.</summary>
    public string MimeType { get; }

    /// <summary>Where the item is declared.</summary>
    public EpubLocation Location { get; }

    /// <summary>Whether the item has a <c>fallback</c>.</summary>
    public bool HasFallback { get; }

    /// <summary>Whether a fallback chain of the item reaches a core media type.</summary>
    public bool HasCoreMediaTypeFallback { get; }

    /// <summary>Whether a fallback chain of the item reaches a content document.</summary>
    public bool HasContentDocumentFallback { get; }

    /// <summary>The properties (those of the <c>properties</c> attribute, plus EPUBCheck's own).</summary>
    public IReadOnlyList<VocabProperty> Properties { get; }

    /// <summary>The position in the spine, or -1.</summary>
    public int SpinePosition { get; }

    /// <summary>Whether the spine's <c>toc</c> names the item.</summary>
    public bool IsNcx { get; }

    /// <summary>Whether the item has the <c>scripted</c> property.</summary>
    public bool IsScripted { get; }

    /// <summary>Whether the item has the <c>nav</c> property.</summary>
    public bool IsNav { get; }

    /// <summary>Whether the spine refers to the item.</summary>
    public bool IsInSpine { get; }

    /// <summary>Whether the item is part of the primary reading order.</summary>
    /// <exception cref="InvalidOperationException">The item is not in the spine.</exception>
    public bool IsLinear => IsInSpine ? linear : throw new InvalidOperationException("linear");

    /// <summary>Whether the item is fixed-layout.</summary>
    public bool IsFixedLayout { get; }

    /// <summary>Whether the URL is a <c>data:</c> URL.</summary>
    public bool HasDataUrl => Url.Scheme == DataScheme;

    /// <summary>Whether the resource is outside the container.</summary>
    public bool IsRemote { get; }

    /// <summary>The <c>media-overlay</c> attribute, or <c>null</c>.</summary>
    public string? MediaOverlay { get; }

    /// <inheritdoc />
    public override string ToString() => Path + "[" + Id + "]";

    /// <inheritdoc />
    public bool Equals(OPFItem? other) => other is not null && Id == other.Id && Url.Equals(other.Url);

    /// <inheritdoc />
    public override bool Equals(object? obj) => Equals(obj as OPFItem);

    /// <inheritdoc />
    public override int GetHashCode() => HashCode.Combine(Id, Url);

    private static void AddOnce(List<VocabProperty> properties, VocabProperty property)
    {
        if (!properties.Contains(property))
        {
            properties.Add(property);
        }
    }

    private string ComputePath(OPFItemBuilder builder)
    {
        if (IsRemote)
        {
            return Url.ToHumanString();
        }

        if (HasDataUrl)
        {
            string text = Url.ToString();
            return text[..Math.Min(text.Length, DataPathLength)] + Ellipsis;
        }

        if (builder.Container is { } container)
        {
            return EpubCheckUrlUtils.Decode(container.Relativize(Url));
        }

        Url document = builder.DocumentUrl ?? throw new InvalidOperationException("item document URL is null");
        return EpubCheckUrlUtils.Decode(document.Resolve(".").Relativize(EpubCheckUrlUtils.DocUrl(Url)));
    }
}

/// <summary>Builds an <see cref="OPFItem"/> while the manifest is read and its fallback chains resolved (upstream's <c>OPFItem.Builder</c>).</summary>
public sealed class OPFItemBuilder
{
    private string _mimeType = string.Empty;
    private string _fallback = string.Empty;
    private string _fallbackStyle = string.Empty;

    /// <summary>The id.</summary>
    public string? Id { get; set; }

    /// <summary>The URL.</summary>
    public Url? Url { get; set; }

    /// <summary>Where the item is declared.</summary>
    public EpubLocation? Location { get; set; }

    /// <summary>The container, if the package document is in one.</summary>
    public OCFContainer? Container { get; set; }

    /// <summary>The URL of the package document (what paths are relative to when there is no container).</summary>
    public Url? DocumentUrl { get; set; }

    /// <summary>Whether the resource is outside the container.</summary>
    public bool Remote { get; set; }

    /// <summary>The media type, normalized when set.</summary>
    public string MimeType
    {
        get => _mimeType;
        set => _mimeType = Util.Mime.MimeType.Normalize(value);
    }

    /// <summary>The <c>fallback</c> id, trimmed (empty for none).</summary>
    public string Fallback
    {
        get => _fallback;
        set => _fallback = (value ?? string.Empty).Trim();
    }

    /// <summary>Whether there is a <c>fallback</c>.</summary>
    public bool HasFallback => _fallback.Length > 0;

    /// <summary>The <c>fallback-style</c> id, trimmed (empty for none).</summary>
    public string FallbackStyle
    {
        get => _fallbackStyle;
        set => _fallbackStyle = (value ?? string.Empty).Trim();
    }

    /// <summary>Whether there is a <c>fallback-style</c>.</summary>
    public bool HasFallbackStyle => _fallbackStyle.Length > 0;

    /// <summary>Whether a fallback chain reaches a core media type.</summary>
    public bool HasCoreMediaTypeFallback { get; set; }

    /// <summary>Whether a fallback chain reaches a content document.</summary>
    public bool HasContentDocumentFallback { get; set; }

    /// <summary>Whether the fallback chain was resolved.</summary>
    public bool IsResolved { get; private set; }

    /// <summary>Whether the item is fixed-layout.</summary>
    public bool FixedLayout { get; set; }

    /// <summary>The <c>media-overlay</c> attribute.</summary>
    public string? MediaOverlay { get; set; }

    /// <summary>Whether the spine's <c>toc</c> names the item.</summary>
    public bool Ncx { get; set; }

    /// <summary>Whether the item is linear.</summary>
    public bool Linear { get; set; } = true;

    /// <summary>The position in the spine, or -1.</summary>
    public int SpinePosition { get; set; } = -1;

    internal List<VocabProperty> PropertyList { get; } = [];

    /// <summary>Marks the fallback chain of the item resolved.</summary>
    public void MarkResolved() => IsResolved = true;

    /// <summary>Adds properties.</summary>
    public void AddProperties(IEnumerable<VocabProperty>? properties)
    {
        foreach (VocabProperty property in properties ?? [])
        {
            if (!PropertyList.Contains(property))
            {
                PropertyList.Add(property);
            }
        }
    }

    /// <summary>The item.</summary>
    public OPFItem Build() => new(this);

    /// <inheritdoc />
    public override string ToString() => Id ?? string.Empty;
}

/// <summary>The items of a manifest, by id and URL, and the spine (upstream's <c>OPFItems</c>).</summary>
public sealed class OPFItems
{
    private readonly Dictionary<string, OPFItem> _byId = new(StringComparer.Ordinal);
    private readonly Dictionary<Url, OPFItem> _byUrl = [];

    private OPFItems(IReadOnlyList<OPFItem> items, IEnumerable<string> spineIds)
    {
        Items = items;
        foreach (OPFItem item in items)
        {
            _byId[item.Id] = item;
            _byUrl[item.Url] = item;
        }

        Spine = [.. spineIds.Select(id => _byId.GetValueOrDefault(id.Trim())).OfType<OPFItem>()];
    }

    /// <summary>The items in manifest order.</summary>
    public IReadOnlyList<OPFItem> Items { get; }

    /// <summary>The items of the spine, in spine order.</summary>
    public IReadOnlyList<OPFItem> Spine { get; }

    /// <summary>The item with the id <paramref name="id"/>, or <c>null</c>.</summary>
    public OPFItem? GetItemById(string id) => _byId.GetValueOrDefault(id);

    /// <summary>The item at <paramref name="url"/> (the last one declared), or <c>null</c>.</summary>
    public OPFItem? GetItemByUrl(Url url) => _byUrl.GetValueOrDefault(url);

    /// <summary>Resolves the fallback chains of the builders and makes the items.</summary>
    public static OPFItems Build(IReadOnlyDictionary<string, OPFItemBuilder> builders, IEnumerable<string> spineIds, ValidationContext context) =>
        new(new FallbackChainResolver(builders, context).Resolve(), spineIds);
}

/// <summary>An entry of the guide (upstream's <c>OPFReference</c>).</summary>
public sealed class OPFReference
{
    internal OPFReference(string? type, string? title, Url? url, int lineNumber, int columnNumber)
    {
        Type = type;
        Title = title;
        Url = url;
        LineNumber = lineNumber;
        ColumnNumber = columnNumber;
    }

    /// <summary>The <c>type</c> attribute.</summary>
    public string? Type { get; }

    /// <summary>The <c>title</c> attribute.</summary>
    public string? Title { get; }

    /// <summary>The URL, or <c>null</c> when it did not parse.</summary>
    public Url? Url { get; }

    /// <summary>The URL without its fragment.</summary>
    public Url? DocumentUrl => Url?.WithFragment(null);

    /// <summary>The line of the entry.</summary>
    public int LineNumber { get; }

    /// <summary>The column of the entry.</summary>
    public int ColumnNumber { get; }
}
