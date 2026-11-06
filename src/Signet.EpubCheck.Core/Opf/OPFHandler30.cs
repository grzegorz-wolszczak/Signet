using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using Signet.EpubCheck.Core.Galimatias;
using Signet.EpubCheck.Core.Messages;
using Signet.EpubCheck.Core.Reporting;
using Signet.EpubCheck.Core.Util;
using Signet.EpubCheck.Core.Vocab;
using Signet.EpubCheck.Core.Xml;

namespace Signet.EpubCheck.Core.Opf;

/// <summary>
/// Reads an EPUB 3 package document (upstream's <c>OPFHandler30</c>), on top of what <see cref="OPFHandler"/> reads: the
/// vocabularies a <c>prefix</c> attribute declares, the properties of items, item references, links and metadata, the
/// metadata set with its refinements, the linked resources and the collections, and the checks of their syntax
/// (<c>OPF-012</c>, <c>OPF-065</c>, <c>OPF-070</c>, <c>OPF-086</c>, <c>OPF-089</c>, <c>OPF-092</c>–<c>OPF-095</c>, <c>OPF-098</c>,
/// <c>RSC-029</c>, <c>OBS-001</c>).
/// </summary>
public partial class OPFHandler30 : OPFHandler
{
    private const string OpfNamespace = "http://www.idpf.org/2007/opf";
    private const string DcNamespace = "http://purl.org/dc/elements/1.1/";
    private const string XmlNamespace = "http://www.w3.org/XML/1998/namespace";
    private const string DefaultPrefix = "";
    private const string Roll = "roll";
    private const string PrePaginated = "pre-paginated";

    private static readonly Dictionary<string, IVocab> s_reservedVocabs = new(StringComparer.Ordinal)
    {
        [ForeignVocabs.DctermsPrefix] = ForeignVocabs.DctermsVocab,
        [ForeignVocabs.MarcPrefix] = ForeignVocabs.MarcVocab,
        [ForeignVocabs.OnixPrefix] = ForeignVocabs.OnixVocab,
        [ForeignVocabs.SchemaPrefix] = ForeignVocabs.SchemaVocab,
        [ForeignVocabs.XsdPrefix] = ForeignVocabs.XsdVocab,
    };

    private static readonly Dictionary<string, IVocab> s_knownVocabUris = new(StringComparer.Ordinal)
    {
        [ForeignVocabs.DctermsUri] = ForeignVocabs.DctermsVocab,
        [ForeignVocabs.MarcUri] = ForeignVocabs.MarcVocab,
        [ForeignVocabs.OnixUri] = ForeignVocabs.OnixVocab,
        [ForeignVocabs.SchemaUri] = ForeignVocabs.SchemaVocab,
        [ForeignVocabs.XsdUri] = ForeignVocabs.XsdVocab,
    };

    private static readonly IReadOnlyDictionary<string, IVocab> s_reservedMetaVocabs = With(s_reservedVocabs, new()
    {
        [DefaultPrefix] = AggregateVocab.Of(PackageVocabs.MetaVocab, PackageVocabs.MetaVocabCamel),
        [AccessibilityVocab.Prefix] = AccessibilityVocab.MetaVocab,
        [MediaOverlaysVocab.Prefix] = MediaOverlaysVocab.Vocab,
        [RenditionVocabs.Prefix] = RenditionVocabs.MetaVocab,
    });

    private static readonly IReadOnlyDictionary<string, IVocab> s_reservedItemVocabs = With(s_reservedVocabs, new()
    {
        [DefaultPrefix] = PackageVocabs.ItemVocab,
        [MediaOverlaysVocab.Prefix] = VocabUtil.EmptyVocab,
        [RenditionVocabs.Prefix] = VocabUtil.EmptyVocab,
    });

    private static readonly IReadOnlyDictionary<string, IVocab> s_reservedItemrefVocabs = With(s_reservedVocabs, new()
    {
        [DefaultPrefix] = PackageVocabs.ItemrefVocab,
        [MediaOverlaysVocab.Prefix] = VocabUtil.EmptyVocab,
        [RenditionVocabs.Prefix] = RenditionVocabs.ItemrefVocab,
    });

    private static readonly IReadOnlyDictionary<string, IVocab> s_reservedLinkrelVocabs = With(s_reservedVocabs, new()
    {
        [DefaultPrefix] = PackageVocabs.LinkrelVocab,
        [AccessibilityVocab.Prefix] = AccessibilityVocab.LinkrelVocab,
        [MediaOverlaysVocab.Prefix] = VocabUtil.EmptyVocab,
        [RenditionVocabs.Prefix] = VocabUtil.EmptyVocab,
    });

    private static readonly IReadOnlyDictionary<string, IVocab> s_reservedLinkVocabs = With(s_reservedVocabs, new()
    {
        [DefaultPrefix] = PackageVocabs.LinkVocab,
        [AccessibilityVocab.Prefix] = VocabUtil.EmptyVocab,
        [MediaOverlaysVocab.Prefix] = VocabUtil.EmptyVocab,
        [RenditionVocabs.Prefix] = VocabUtil.EmptyVocab,
    });

    private static readonly IReadOnlyDictionary<string, IVocab> s_knownMetaVocabUris = With(s_knownVocabUris, new()
    {
        [AccessibilityVocab.Uri] = AccessibilityVocab.MetaVocab,
        [MediaOverlaysVocab.Uri] = MediaOverlaysVocab.Vocab,
        [RenditionVocabs.Uri] = RenditionVocabs.MetaVocab,
        [ScriptedCompVocab.Uri] = ScriptedCompVocab.Vocab,
    });

    private static readonly IReadOnlyDictionary<string, IVocab> s_knownItemVocabUris = With(s_knownVocabUris, new()
    {
        [AccessibilityVocab.Uri] = VocabUtil.EmptyVocab,
        [MediaOverlaysVocab.Uri] = VocabUtil.EmptyVocab,
        [RenditionVocabs.Uri] = VocabUtil.EmptyVocab,
        [ScriptedCompVocab.Uri] = VocabUtil.EmptyVocab,
    });

    private static readonly IReadOnlyDictionary<string, IVocab> s_knownItemrefVocabUris = With(s_knownVocabUris, new()
    {
        [AccessibilityVocab.Uri] = VocabUtil.EmptyVocab,
        [MediaOverlaysVocab.Uri] = VocabUtil.EmptyVocab,
        [RenditionVocabs.Uri] = RenditionVocabs.ItemrefVocab,
    });

    private static readonly IReadOnlyDictionary<string, IVocab> s_knownLinkVocabUris = With(s_knownVocabUris, new()
    {
        [AccessibilityVocab.Uri] = VocabUtil.EmptyVocab,
        [MediaOverlaysVocab.Uri] = VocabUtil.EmptyVocab,
        [RenditionVocabs.Uri] = VocabUtil.EmptyVocab,
        [ScriptedCompVocab.Uri] = VocabUtil.EmptyVocab,
    });

    private static readonly IReadOnlyDictionary<string, IVocab> s_knownLinkrelVocabUris = With(s_knownVocabUris, new()
    {
        [AccessibilityVocab.Uri] = AccessibilityVocab.LinkrelVocab,
        [MediaOverlaysVocab.Uri] = VocabUtil.EmptyVocab,
        [RenditionVocabs.Uri] = VocabUtil.EmptyVocab,
        [ScriptedCompVocab.Uri] = VocabUtil.EmptyVocab,
    });

    private static readonly IReadOnlySet<string> s_defaultVocabUris = new HashSet<string>(StringComparer.Ordinal)
    {
        PackageVocabs.ItemVocabUri, PackageVocabs.ItemrefVocabUri, PackageVocabs.MetaVocabUri, PackageVocabs.LinkVocabUri,
    };

    private readonly LinkedList<MetadataSet.Builder> _metadataBuilders = new();
    private readonly LinkedList<LinkedResources.Builder> _linkedResourcesBuilders = new();
    private readonly LinkedList<CollectionNode.Builder> _collectionBuilders = new();
    private readonly ResourceCollections.Builder _collectionsBuilder = ResourceCollections.CreateBuilder();
    private IReadOnlyDictionary<string, IVocab> _itemrefVocabs = new Dictionary<string, IVocab>();
    private IReadOnlyDictionary<string, IVocab> _itemVocabs = new Dictionary<string, IVocab>();
    private IReadOnlyDictionary<string, IVocab> _metaVocabs = new Dictionary<string, IVocab>();
    private IReadOnlyDictionary<string, IVocab> _linkVocabs = new Dictionary<string, IVocab>();
    private IReadOnlyDictionary<string, IVocab> _linkrelVocabs = new Dictionary<string, IVocab>();
    private MetadataSet? _metadata;
    private LinkedResources? _linkedResources;
    private ResourceCollections? _collections;

    /// <summary>A handler of the package document of <paramref name="context"/>.</summary>
    public OPFHandler30(ValidationContext context)
        : base(context)
    {
    }

    /// <summary>The metadata of the package.</summary>
    public MetadataSet Metadata => _metadata ?? MetadataSet.CreateBuilder().Build();

    /// <summary>The resources the metadata link to.</summary>
    public LinkedResources LinkedResources => _linkedResources ?? LinkedResources.CreateBuilder().Build();

    /// <summary>The collections of the package.</summary>
    public ResourceCollections Collections => _collections ?? ResourceCollections.CreateBuilder().Build();

    private VocabContext VocabContext => new(Report, Context.MimeType, Context.Culture);

    /// <inheritdoc />
    protected override void StartElement()
    {
        base.StartElement();
        XMLElement element = CurrentElement;
        string? lang = element.GetAttributeNS(XmlNamespace, "lang");
        if (!string.IsNullOrEmpty(lang))
        {
            CheckLanguageTag(lang);
        }

        if (element.Namespace != OpfNamespace)
        {
            return;
        }

        switch (element.Name)
        {
            case "package":
                ParsePrefixes(element.GetAttribute("prefix"));
                break;
            case "metadata":
                _metadataBuilders.AddFirst(MetadataSet.CreateBuilder());
                _linkedResourcesBuilders.AddFirst(LinkedResources.CreateBuilder());
                break;
            case "link":
                ProcessLink(element);
                break;
            case "item":
                if (element.GetAttribute("id") is { } id && ItemBuilders.GetValueOrDefault(id) is { } itemBuilder)
                {
                    ProcessItemProperties(itemBuilder, element.GetAttribute("properties"), element.GetAttribute("media-type"));
                }

                break;
            case "itemref":
                if (element.GetAttribute("idref") is { } idref && ItemBuilders.GetValueOrDefault(idref) is { } refBuilder)
                {
                    ProcessItemrefProperties(refBuilder, element.GetAttribute("properties"));
                }

                break;
            case "collection":
                if (_collectionBuilders.Count == 0)
                {
                    Report.Message(MessageId.OBS_001, Location(), "element", "\"collection\"");
                }

                _collectionBuilders.AddFirst(CollectionNode.CreateBuilder().Roles(ProcessCollectionRole(element.GetAttribute("role"))));
                _linkedResourcesBuilders.AddFirst(LinkedResources.CreateBuilder());
                break;
            case "guide":
                Report.Message(MessageId.OBS_001, Location(), "element", "OPF 2 \"guide\"");
                break;
        }
    }

    /// <inheritdoc />
    protected override void EndElement()
    {
        XMLElement element = CurrentElement;
        if (element.Namespace == OpfNamespace)
        {
            switch (element.Name)
            {
                case "package":
                    _collections = _collectionsBuilder.Build();
                    foreach (CollectionNode collection in Collections.GetByRole(CollectionRole.Index))
                    {
                        ProcessItemsInIndexCollection(collection);
                    }

                    break;
                case "meta":
                    ProcessMeta(element);
                    break;
                case "metadata":
                    EndMetadata();
                    break;
                case "collection":
                    EndCollection();
                    break;
            }
        }
        else if (element.Namespace == DcNamespace)
        {
            ProcessDublinCoreElement(element);
        }

        base.EndElement();
    }

    /// <inheritdoc />
    protected override void ReportItem(OPFItem item)
    {
        base.ReportItem(item);
        foreach (RenditionVocabs.ItemrefProperty property in VocabProperty.Filter<RenditionVocabs.ItemrefProperty>(item.Properties))
        {
            switch (property)
            {
                case RenditionVocabs.ItemrefProperty.LayoutPrePaginated:
                    Report.Info(item.Path, FeatureKind.RenditionLayout, PrePaginated);
                    Report.Info(item.Path, FeatureKind.HasFixedLayout, "true");
                    break;
                case RenditionVocabs.ItemrefProperty.LayoutReflowable:
                    Report.Info(item.Path, FeatureKind.RenditionLayout, "reflowable");
                    Report.Info(item.Path, FeatureKind.HasFixedLayout, "false");
                    break;
                case RenditionVocabs.ItemrefProperty.OrientationAuto:
                    Report.Info(item.Path, FeatureKind.RenditionOrientation, "auto");
                    break;
                case RenditionVocabs.ItemrefProperty.OrientationLandscape:
                    Report.Info(item.Path, FeatureKind.RenditionOrientation, "landscape");
                    break;
                case RenditionVocabs.ItemrefProperty.OrientationPortrait:
                    Report.Info(item.Path, FeatureKind.RenditionOrientation, "portrait");
                    break;
                case RenditionVocabs.ItemrefProperty.SpreadAuto:
                    Report.Info(item.Path, FeatureKind.RenditionSpread, "auto");
                    break;
                case RenditionVocabs.ItemrefProperty.SpreadBoth:
                    Report.Info(item.Path, FeatureKind.RenditionSpread, "both");
                    break;
                case RenditionVocabs.ItemrefProperty.SpreadLandscape:
                    Report.Info(item.Path, FeatureKind.RenditionSpread, "landscape");
                    break;
                case RenditionVocabs.ItemrefProperty.SpreadNone:
                    Report.Info(item.Path, FeatureKind.RenditionSpread, "none");
                    break;
                case RenditionVocabs.ItemrefProperty.SpreadPortrait:
                    Report.Info(item.Path, FeatureKind.RenditionSpread, "portrait");
                    break;
            }
        }
    }

    /// <summary>Reports the rendition facts of the package metadata.</summary>
    protected virtual void ReportMetadata()
    {
        VocabProperty layout = RenditionVocabs.MetaVocab.Get(RenditionVocabs.MetaProperty.Layout);
        if (Metadata.ContainsPrimary(layout, PrePaginated))
        {
            Report.Info(null, FeatureKind.RenditionLayout, PrePaginated);
            Report.Info(null, FeatureKind.HasFixedLayout, "true");
        }
        else if (Metadata.ContainsPrimary(layout, Roll))
        {
            Report.Info(null, FeatureKind.RenditionLayout, Roll);
            Report.Info(null, FeatureKind.HasFixedLayout, "true");
        }

        if (MetadataSet.TryFind(Metadata.All, RenditionVocabs.MetaVocab.Get(RenditionVocabs.MetaProperty.Orientation), null) is { } orientation)
        {
            Report.Info(null, FeatureKind.RenditionOrientation, orientation.Value);
        }

        if (MetadataSet.TryFind(Metadata.All, RenditionVocabs.MetaVocab.Get(RenditionVocabs.MetaProperty.Spread), null) is { } spread)
        {
            Report.Info(null, FeatureKind.RenditionSpread, spread.Value);
        }
    }

    private static Dictionary<string, IVocab> With(Dictionary<string, IVocab> baseVocabs, Dictionary<string, IVocab> more)
    {
        Dictionary<string, IVocab> merged = new(more, StringComparer.Ordinal);
        foreach ((string key, IVocab vocab) in baseVocabs)
        {
            merged[key] = vocab;
        }

        return merged;
    }

    private void ParsePrefixes(string? declaration)
    {
        EpubLocation location = Location();
        IReadOnlyDictionary<string, IVocab> Parse(IReadOnlyDictionary<string, IVocab> reserved, IReadOnlyDictionary<string, IVocab> known, IReport report) =>
            VocabUtil.ParsePrefixDeclaration(declaration, reserved, known, s_defaultVocabUris, report, location);
        _metaVocabs = Parse(s_reservedMetaVocabs, s_knownMetaVocabUris, Report);
        _itemVocabs = Parse(s_reservedItemVocabs, s_knownItemVocabUris, QuietReport.Instance);
        _itemrefVocabs = Parse(s_reservedItemrefVocabs, s_knownItemrefVocabUris, QuietReport.Instance);
        _linkrelVocabs = Parse(s_reservedLinkrelVocabs, s_knownLinkrelVocabUris, QuietReport.Instance);
        _linkVocabs = Parse(s_reservedLinkVocabs, s_knownLinkVocabUris, QuietReport.Instance);
    }

    private void EndMetadata()
    {
        MetadataSet? metadata = null;
        try
        {
            if (_metadataBuilders.Count > 0)
            {
                metadata = _metadataBuilders.First!.Value.Build();
                _metadataBuilders.RemoveFirst();
            }
        }
        catch (InvalidOperationException)
        {
            _metadataBuilders.RemoveFirst();
            Report.Message(MessageId.OPF_065, Location());
        }

        LinkedResources? linkedResources = null;
        if (_linkedResourcesBuilders.Count > 0)
        {
            linkedResources = _linkedResourcesBuilders.First!.Value.Build();
            _linkedResourcesBuilders.RemoveFirst();
        }

        if (_collectionBuilders.Count == 0)
        {
            _metadata = metadata;
            _linkedResources = linkedResources;
            ReportMetadata();
        }
        else
        {
            _collectionBuilders.First!.Value.Metadata(metadata).MetadataLinks(linkedResources);
        }
    }

    private void EndCollection()
    {
        if (_collectionBuilders.Count == 0)
        {
            return;
        }

        if (_linkedResourcesBuilders.Count > 0)
        {
            _collectionBuilders.First!.Value.Resources(_linkedResourcesBuilders.First!.Value.Build());
            _linkedResourcesBuilders.RemoveFirst();
        }

        CollectionNode collection = _collectionBuilders.First!.Value.Build();
        _collectionBuilders.RemoveFirst();
        if (_collectionBuilders.Count == 0)
        {
            _collectionsBuilder.Add(collection);
        }
        else
        {
            _collectionBuilders.First!.Value.Collection(collection);
        }
    }

    private List<string> ProcessCollectionRole(string? roleAttribute)
    {
        List<string> roles = [];
        foreach (string role in Whitespace().Split(roleAttribute ?? string.Empty))
        {
            if (EpubCheckUrlUtils.IsAbsoluteUrlString(role))
            {
                try
                {
                    Url.Parse(role);
                }
                catch (GalimatiasParseException)
                {
                    Report.Message(MessageId.OPF_070, Location(), role);
                    break;
                }
            }

            roles.Add(role);
        }

        return roles;
    }

    private void ProcessLink(XMLElement element)
    {
        string? href = element.GetAttribute("href");
        if (CheckUrl(href) is { } url)
        {
            if (url.Scheme == "data")
            {
                Report.Message(MessageId.RSC_029, Location());
                return;
            }

            if (!string.IsNullOrEmpty(url.Fragment) && EpubCheckUrlUtils.DocUrl(url).Equals(Context.Url))
            {
                Report.Message(MessageId.OPF_098, Location(), href);
                return;
            }

            bool remote = Context.IsRemote(url);
            if (remote)
            {
                Report.Info(Path, FeatureKind.Reference, href);
            }

            string? rel = element.GetAttribute("rel");
            IReadOnlyList<VocabProperty> relSet = ProcessLinkRel(rel);
            IReadOnlySet<PackageVocabs.LinkrelProperty> relEnum = VocabProperty.Filter<PackageVocabs.LinkrelProperty>(relSet);
            string? mediaType = element.GetAttribute("media-type");
            if (mediaType is null)
            {
                if (!remote && _metadataBuilders.Count > 0)
                {
                    if (_linkedResourcesBuilders.Count == 1)
                    {
                        Report.Message(MessageId.OPF_093, Location());
                    }
                }
                else if (relEnum.Contains(PackageVocabs.LinkrelProperty.Record) || relEnum.Contains(PackageVocabs.LinkrelProperty.Voicing))
                {
                    Report.Message(MessageId.OPF_094, Location(), rel);
                }
            }
            else if (relEnum.Contains(PackageVocabs.LinkrelProperty.Voicing) && !OpfMediaTypes.IsAudioType(mediaType))
            {
                Report.Message(MessageId.OPF_095, Location(), mediaType);
            }

            VocabUtil.ParsePropertyList(element.GetAttribute("properties"), _linkVocabs, VocabContext, Location());
            if (_linkedResourcesBuilders.Count > 0)
            {
                _linkedResourcesBuilders.First!.Value.Add(new LinkedResource(url, element.GetAttribute("id"), relSet, element.GetAttribute("refines"), mediaType));
            }
        }

        string? hreflang = element.GetAttribute("hreflang");
        if (!string.IsNullOrEmpty(hreflang))
        {
            CheckLanguageTag(hreflang);
        }
    }

    private IReadOnlyList<VocabProperty> ProcessLinkRel(string? rel)
    {
        IReadOnlyList<VocabProperty> properties = VocabUtil.ParsePropertyList(rel, _linkrelVocabs, VocabContext, Location());
        if (VocabProperty.Filter<PackageVocabs.LinkrelProperty>(properties).Contains(PackageVocabs.LinkrelProperty.Alternate) && properties.Count > 1)
        {
            Report.Message(MessageId.OPF_089, Location());
        }

        return properties;
    }

    private void ProcessItemrefProperties(OPFItemBuilder builder, string? property)
    {
        IReadOnlyList<VocabProperty> properties = VocabUtil.ParsePropertyList(property, _itemrefVocabs, VocabContext, Location());
        builder.AddProperties(properties);
        VocabProperty layout = RenditionVocabs.MetaVocab.Get(RenditionVocabs.MetaProperty.Layout);
        bool reflowable = properties.Contains(RenditionVocabs.ItemrefVocab.Get(RenditionVocabs.ItemrefProperty.LayoutReflowable));
        if (Metadata.ContainsPrimary(layout, Roll)
            || (Metadata.ContainsPrimary(layout, PrePaginated) && !reflowable)
            || properties.Contains(RenditionVocabs.ItemrefVocab.Get(RenditionVocabs.ItemrefProperty.LayoutPrePaginated)))
        {
            builder.FixedLayout = true;
        }
    }

    private void ProcessItemProperties(OPFItemBuilder builder, string? property, string? mimeType)
    {
        if (property is null)
        {
            return;
        }

        IReadOnlyList<VocabProperty> properties = VocabUtil.ParsePropertyList(property, _itemVocabs, VocabContext, Location());
        string type = (mimeType ?? string.Empty).Trim();
        foreach (PackageVocabs.ItemProperty itemProperty in VocabProperty.Filter<PackageVocabs.ItemProperty>(properties))
        {
            if (!PackageVocabs.IsAllowedForType(itemProperty, type))
            {
                Report.Message(MessageId.OPF_012, Location(), PackageVocabs.ItemVocab.GetName(itemProperty), type);
            }
        }

        builder.AddProperties(properties);
    }

    private void ProcessMeta(XMLElement element)
    {
        if (element.GetAttribute("name") is not null)
        {
            Report.Message(MessageId.OBS_001, Location(), "element", "OPF 2 \"meta\"");
            return;
        }

        VocabProperty? property = VocabUtil.ParseProperty(element.GetAttribute("property"), _metaVocabs, VocabContext, Location());
        if (property is not null && _metadataBuilders.Count > 0)
        {
            string value = ((string?)element.GetPrivateData(TextKey) ?? string.Empty).Trim();
            _metadataBuilders.First!.Value.Meta(element.GetAttribute("id"), property, value, element.GetAttribute("refines"));
            if (_metadataBuilders.Count == 1 && property.PrefixedName == "rendition:spread" && value == "portrait")
            {
                Report.Message(
                    MessageId.OPF_086,
                    Location(),
                    "rendition:spread portrait",
                    LocalizedMessages.GetInstance(Context.Culture).GetSuggestion(MessageId.OPF_086, null));
            }
        }

        VocabUtil.ParseProperty(element.GetAttribute("scheme"), _metaVocabs, VocabContext, Location());
    }

    private void ProcessDublinCoreElement(XMLElement element)
    {
        VocabProperty? property = DCMESVocab.Vocab.Lookup(element.Name);
        if (property is not null && _metadataBuilders.Count > 0)
        {
            _metadataBuilders.First!.Value.Meta(element.GetAttribute("id"), property, (string?)element.GetPrivateData(TextKey), null);
        }

        if (element.Name == "language" && ((string?)element.GetPrivateData(TextKey))?.Trim() is { Length: > 0 } language)
        {
            CheckLanguageTag(language);
        }
    }

    private void ProcessItemsInIndexCollection(CollectionNode collection)
    {
        if (!collection.HasRole(CollectionRole.Index) && !collection.HasRole(CollectionRole.IndexGroup))
        {
            return;
        }

        foreach (LinkedResource resource in collection.Resources.AsList)
        {
            ItemBuildersByUrl.GetValueOrDefault(resource.DocumentUrl)
                ?.AddProperties([EpubCheckVocab.Vocab.Get(EpubCheckVocab.Property.InIndexCollection)]);
        }

        foreach (CollectionNode child in collection.Collections.AsList)
        {
            ProcessItemsInIndexCollection(child);
        }
    }

    private void CheckLanguageTag(string language)
    {
        if (LanguageTag.Validate(language) is { } problem)
        {
            Report.Message(MessageId.OPF_092, Location(), language, problem);
        }
    }

    [GeneratedRegex("\\s+", RegexOptions.CultureInvariant)]
    private static partial Regex Whitespace();
}
