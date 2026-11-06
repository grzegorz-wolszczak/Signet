using System.Collections.Frozen;
using System.Collections.Generic;

namespace Signet.EpubCheck.Core.Vocab;

// The vocabularies of upstream's com.adobe.epubcheck.vocab: their URIs, prefixes and instances. The enums and the names
// of their properties are generated into Vocabularies.g.cs (scripts/epubcheck/generate_vocabs.py).

/// <summary>The accessibility vocabulary (<c>a11y</c>).</summary>
public static partial class AccessibilityVocab
{
    public const string Prefix = "a11y";
    public const string Uri = "http://www.idpf.org/epub/vocab/package/a11y/#";
    public static readonly EnumVocab<MetaProperty> MetaVocab = new(Uri, Prefix, MetaPropertyEntries);
    public static readonly EnumVocab<LinkrelProperty> LinkrelVocab = new(Uri, Prefix, LinkrelPropertyEntries);
}

/// <summary>The alternate style tags (no URI, no prefix).</summary>
public static partial class AltStylesheetVocab
{
    public const string Prefix = "";
    public const string Uri = "";
    public static readonly EnumVocab<Property> Vocab = new(Uri, Prefix, PropertyEntries);
}

/// <summary>The structural semantics of comics.</summary>
public static partial class ComicsVocab
{
    public const string Uri = StructureVocab.Uri;
    public static readonly EnumVocab<EpubType> Vocab = new(Uri, null, EpubTypeEntries);
}

/// <summary>The Dublin Core elements (<c>dc</c>).</summary>
public static partial class DCMESVocab
{
    public const string Prefix = "dc";
    public const string Uri = "http://purl.org/dc/elements/1.1/";
    public static readonly EnumVocab<Property> Vocab = new(Uri, Prefix, PropertyEntries);
}

/// <summary>The structural semantics of data navigation documents.</summary>
public static partial class DataNavVocab
{
    public const string Uri = StructureVocab.Uri;
    public static readonly EnumVocab<EpubType> Vocab = new(Uri, null, EpubTypeEntries);
}

/// <summary>The structural semantics of dictionaries.</summary>
public static partial class DictVocab
{
    public const string Uri = StructureVocab.Uri;
    public static readonly EnumVocab<EpubType> Vocab = new(Uri, null, EpubTypeEntries);
}

/// <summary>The properties EPUBCheck gives a publication itself (<c>epubcheck</c>).</summary>
public static partial class EpubCheckVocab
{
    public const string Prefix = "epubcheck";
    public const string Uri = "http://www.idpf.org/epubcheck/#";
    public static readonly EnumVocab<Property> Vocab = new(Uri, Prefix, PropertyEntries);
    public static readonly IReadOnlyDictionary<string, IVocab> VocabMap = new Dictionary<string, IVocab> { [Prefix] = Vocab }.ToFrozenDictionary();
}

/// <summary>The structural semantics of indexes.</summary>
public static partial class IndexVocab
{
    public const string Uri = StructureVocab.Uri;
    public static readonly EnumVocab<EpubType> Vocab = new(Uri, null, EpubTypeEntries);
}

/// <summary>The Media Overlays vocabulary (<c>media</c>).</summary>
public static partial class MediaOverlaysVocab
{
    public const string Prefix = "media";
    public const string Uri = "http://www.idpf.org/epub/vocab/overlays/#";
    public static readonly EnumVocab<Property> Vocab = new(Uri, Prefix, PropertyEntries);
}

/// <summary>The scriptable components vocabulary (<c>epubsc</c>).</summary>
public static partial class ScriptedCompVocab
{
    public const string Prefix = "epubsc";
    public const string Uri = "http://idpf.org/epub/vocab/sc/#";
    public static readonly EnumVocab<Property> Vocab = new(Uri, Prefix, PropertyEntries);
}

/// <summary>The EPUB structural semantics vocabulary (<c>epub:type</c>).</summary>
public static partial class StructureVocab
{
    public const string Uri = "http://www.idpf.org/epub/vocab/structure/#";
    public static readonly EnumVocab<EpubType> Vocab = new(Uri, null, EpubTypeEntries);
    public static readonly IVocab UncheckedVocab = new UncheckedVocab(Uri, string.Empty);
}

/// <summary>The rendition vocabulary (<c>rendition</c>).</summary>
public static partial class RenditionVocabs
{
    public const string Prefix = "rendition";
    public const string Uri = "http://www.idpf.org/vocab/rendition/#";
    public static readonly EnumVocab<MetaProperty> MetaVocab = new(Uri, Prefix, MetaPropertyEntries);
    public static readonly EnumVocab<ItemrefProperty> ItemrefVocab = new(Uri, Prefix, ItemrefPropertyEntries);

    /// <summary>The properties that set a page spread, of this vocabulary and of the package one.</summary>
    public static readonly IReadOnlySet<VocabProperty> SpreadProperties = new HashSet<VocabProperty>
    {
        ItemrefVocab.Get(ItemrefProperty.PageSpreadCenter),
        ItemrefVocab.Get(ItemrefProperty.PageSpreadLeft),
        ItemrefVocab.Get(ItemrefProperty.PageSpreadRight),
        PackageVocabs.ItemrefVocab.Get(PackageVocabs.ItemrefProperty.PageSpreadLeft),
        PackageVocabs.ItemrefVocab.Get(PackageVocabs.ItemrefProperty.PageSpreadRight),
    }.ToFrozenSet();
}

/// <summary>The vocabularies of the package document: <c>meta</c>, <c>item</c>, <c>itemref</c> and <c>link</c> properties.</summary>
public static partial class PackageVocabs
{
    public const string ItemVocabUri = "http://idpf.org/epub/vocab/package/item/#";
    public const string ItemrefVocabUri = "http://idpf.org/epub/vocab/package/itemref/#";
    public const string LinkVocabUri = "http://idpf.org/epub/vocab/package/link/#";
    public const string MetaVocabUri = "http://idpf.org/epub/vocab/package/meta/#";

    public static readonly EnumVocab<MetaProperty> MetaVocab = new(MetaVocabUri, null, MetaPropertyEntries);
    public static readonly EnumVocab<MetaPropertyCamel> MetaVocabCamel = new(MetaVocabUri, null, MetaPropertyCamelEntries);
    public static readonly EnumVocab<ItemProperty> ItemVocab = new(ItemVocabUri, null, ItemPropertyEntries);
    public static readonly EnumVocab<ItemrefProperty> ItemrefVocab = new(ItemrefVocabUri, null, ItemrefPropertyEntries);
    public static readonly EnumVocab<LinkrelProperty> LinkrelVocab = new(LinkVocabUri, null, LinkrelPropertyEntries);
    public static readonly EnumVocab<LinkProperty> LinkVocab = new(LinkVocabUri, null, LinkPropertyEntries);

    // The media types an item property is allowed on; "image/" stands for "image/*".
    private static readonly FrozenDictionary<ItemProperty, string[]> s_itemTypes = new Dictionary<ItemProperty, string[]>
    {
        [ItemProperty.CoverImage] = ["image/"],
        [ItemProperty.DataNav] = ["application/xhtml+xml"],
        [ItemProperty.Dictionary] = ["application/vnd.epub.search-key-map+xml"],
        [ItemProperty.Glossary] = ["application/vnd.epub.search-key-map+xml", "application/xhtml+xml"],
        [ItemProperty.Index] = ["application/xhtml+xml"],
        [ItemProperty.Mathml] = ["application/xhtml+xml", "image/svg+xml"],
        [ItemProperty.Nav] = ["application/xhtml+xml"],
        [ItemProperty.RemoteResources] = ["application/xhtml+xml", "application/smil+xml", "image/svg+xml", "text/css"],
        [ItemProperty.Scripted] = ["application/xhtml+xml", "image/svg+xml"],
        [ItemProperty.SearchKeyMap] = ["application/vnd.epub.search-key-map+xml"],
        [ItemProperty.Svg] = ["application/xhtml+xml"],
        [ItemProperty.Switch] = ["application/xhtml+xml", "image/svg+xml"],
    }.ToFrozenDictionary();

    /// <summary>Whether <paramref name="property"/> may be set on an item of <paramref name="mimeType"/> (upstream's <c>ITEM_PROPERTIES.isAllowedForType</c>).</summary>
    public static bool IsAllowedForType(ItemProperty property, string? mimeType)
    {
        if (mimeType is null)
        {
            return false;
        }

        foreach (string allowed in s_itemTypes[property])
        {
            if (allowed == mimeType || (allowed.EndsWith('/') && mimeType.StartsWith(allowed, System.StringComparison.Ordinal)))
            {
                return true;
            }
        }

        return false;
    }
}

/// <summary>The vocabularies of other standards, reserved prefixes of EPUB 3 (upstream's <c>ForeignVocabs</c>).</summary>
public static class ForeignVocabs
{
    public const string DctermsPrefix = "dcterms";
    public const string DctermsUri = "http://purl.org/dc/terms/";
    public static readonly IVocab DctermsVocab = new UncheckedVocab(DctermsUri, DctermsPrefix);
    public const string MarcPrefix = "marc";
    public const string MarcUri = "http://id.loc.gov/vocabulary/";
    public static readonly IVocab MarcVocab = new UncheckedVocab(MarcUri, MarcPrefix);
    public const string OnixPrefix = "onix";
    public const string OnixUri = "http://www.editeur.org/ONIX/book/codelists/current.html#";
    public static readonly IVocab OnixVocab = new UncheckedVocab(OnixUri, OnixPrefix);
    public const string PrismPrefix = "prism";
    public const string PrismUri = "http://www.prismstandard.org/specifications/3.0/PRISM_CV_Spec_3.0.htm#";
    public static readonly IVocab PrismVocab = new DeprecatedVocab(PrismUri, PrismPrefix);
    public const string SchemaPrefix = "schema";
    public const string SchemaUri = "http://schema.org/";
    public static readonly IVocab SchemaVocab = new UncheckedVocab(SchemaUri, SchemaPrefix);
    public const string XsdPrefix = "xsd";
    public const string XsdUri = "http://www.w3.org/2001/XMLSchema#";
    public static readonly IVocab XsdVocab = new DeprecatedVocab(XsdUri, XsdPrefix);
}

/// <summary>The magazine navigation vocabulary (<c>msv</c>), deprecated.</summary>
public static class MagazineNavigationVocab
{
    public const string Prefix = "msv";
    public const string Uri = "http://www.idpf.org/epub/vocab/structure/magazine/#";
    public static readonly IVocab Vocab = new DeprecatedVocab(Uri, Prefix);
}
