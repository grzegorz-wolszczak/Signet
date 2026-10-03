using System;
using System.Collections.Generic;
using Signet.Core.Semantics;
using Signet.Core.Localization;

namespace Signet.Core.Metadata;

/// <summary>
/// Static data of the "Metadata Editor": lists of OPF elements/properties/attributes
/// with descriptions, for EPUB 2 and EPUB 3. The descriptions are the standard EPUB descriptions
/// and are not localized (like <see cref="MarcRelators"/> / <see cref="Language"/>).
/// </summary>
public static class MetadataFieldCatalog
{
    /// <summary>The <c>dc:*</c> elements recognized by the editor (both EPUB versions).</summary>
    public static readonly IReadOnlyList<string> RecognizedDcElements = new[]
    {
        "dc:identifier", "dc:title", "dc:creator", "dc:contributor", "dc:source", "dc:date",
        "dc:language", "dc:coverage", "dc:description", "dc:format", "dc:publisher",
        "dc:relation", "dc:rights", "dc:subject", "dc:type",
    };

    /// <summary>
    /// Primary <c>meta property=</c> properties treated as standalone elements (EPUB 3 only).
    ///
    /// </summary>
    public static readonly IReadOnlyList<string> RecognizedMetaProperties = new[]
    {
        "belongs-to-collection", "dcterms:issued", "dcterms:created", "dcterms:modified",
        "schema:accessibilitySummary", "schema:accessMode", "schema:accessModeSufficient",
        "schema:accessibilityFeature", "schema:accessibilityHazard",
    };

    /// <summary>
    /// The prefix of a new <c>id</c> generated for an element when it needs refines and has no
    /// own <c>id</c> (the default prefix for unrecognized elements is <c>num</c>).
    /// </summary>
    public static readonly IReadOnlyDictionary<string, string> IdRootPrefixes = new Dictionary<string, string>(StringComparer.Ordinal)
    {
        ["dc:identifier"] = "uid",
        ["dc:title"] = "tle",
        ["dc:creator"] = "cre",
        ["dc:contributor"] = "con",
        ["dc:source"] = "src",
        ["dc:date"] = "dat",
        ["dc:language"] = "lng",
        ["dc:coverage"] = "cov",
        ["dc:description"] = "des",
        ["dc:format"] = "fmt",
        ["dc:publisher"] = "pub",
        ["dc:relation"] = "rln",
        ["dc:rights"] = "rgt",
        ["dc:subject"] = "sub",
        ["dc:type"] = "typ",
    };

    /// <summary>The default id prefix for elements outside <see cref="IdRootPrefixes"/> (<c>"num"</c>).</summary>
    public const string DefaultIdRootPrefix = "num";

    private static readonly IReadOnlyDictionary<string, DescriptiveInfo> Epub3ElementsSource = Build(new[]
    {
        ("dc:creator-aut", "Author", "Represents a primary author of the book or publication"),
        ("dc:subject", "Subject", "An arbitrary phrase or keyword describing the subject in question. Use multiple 'subject' elements if needed."),
        ("dc:description", "Description", "Description of the publication's content."),
        ("dc:publisher", "Publisher", "An entity responsible for making the publication available."),
        ("dc:date", "Date Published", "The date of publication."),
        ("dcterms:created", "Date Created", "The date of creation."),
        ("dcterms:modified", "Date Modified", "The date of modification."),
        ("dc:type", "Type", "Used to indicate that the given EPUB Publication is of a specialized type.."),
        ("dc:format", "Format", "The media type or dimensions of the publication. Best practice is to use a value from a controlled vocabulary (e.g. MIME media types)."),
        ("dc:source", "Source", "Identifies the related resource(s) from which this EPUB Publication is derived."),
        ("dc:language", "Language", "Specifies the language of the publication. Select from the dropdown menu"),
        ("dc:relation", "Related To", "A reference to a related resource. The recommended best practice is to identify the referenced resource by means of a string or number conforming to a formal identification system."),
        ("dc:coverage", "Coverage", "The extent or scope of the content of the publication's content."),
        ("dc:rights", "Rights", "Information about rights held in and over the publication. Rights information often encompasses Intellectual Property Rights (IPR), Copyright, and various Property Rights. If the Rights element is absent, no assumptions may be made about any rights held in or over the publication."),
        ("dc:creator", "Creator", "Represents the name of a person, organization, etc. responsible for the creation of the content of an EPUB Publication. The Role property can be attached to the element to indicate the function the creator played in the creation of the content."),
        ("dc:contributor", "Contributor", "Represents the name of a person, organization, etc. that played a secondary role in the creation of the content of an EPUB Publication. The Role property can be attached to the element to indicate the function the creator played in the creation of the content."),
        ("belongs-to-collection", "Belongs to a Collection", "Identifies the name of a collection to which the EPUB Publication belongs. An EPUB Publication may belong to one or more collections."),
        ("dc:title", "Title", "A title of the publication.  A publication may have only one main title but may have numerous other title types.  These include main, subtitle, short, collection, edition, and expanded title types."),
        ("dc:identifier-doi", "Identifier: DOI", "Digital Object Identifier associated with this publication."),
        ("dc:identifier-isbn", "Identifier: ISBN", "International Standard Book Number associated with this publication."),
        ("dc:identifier-issn", "Identifier: ISSN", "International Standard Serial Number associated with this publication."),
        ("dc:identifier-uuid", "Identifier: UUID", "A Universally Unique Identifier generated for this publication."),
        ("dc:identifier-amazon", "Identifier: ASIN", "An Amazon Standard Identification Number associated with this publication."),
        ("dc:identifier-custom", "Identifier: Custom", "A custom identifier based on a specified scheme"),
        ("custom-element", "Custom Element", "An empty metadata element you can modify."),
        ("meta", "Meta Element (primary)", "An empty primary metadata element you can modify."),
    });

    /// <summary>Elements available in "Add Metadata Element" for EPUB 3. Names and descriptions in the interface language.</summary>
    public static IReadOnlyDictionary<string, DescriptiveInfo> Epub3Elements => Localize("Meta3Elem", Epub3ElementsSource);

    private static readonly IReadOnlyDictionary<string, DescriptiveInfo> Epub3PropertiesSource = Build(new[]
    {
        ("id", "Id Attribute", "Optional, typically short, unique identifier string used as an attribute in the Package (opf) document."),
        ("xml:lang", "XML Language", "Optional, language specifying attribute.  Uses same codes as Language. Not for use with Language, Date, or Identifier metadata elements."),
        ("dir:rtl", "Uses Right To Left Text", "Optional text direction attribute for this metadata item. right-to-left (rtl). Not for use with dc:language, dc:date, or dc:identifier metadata elements."),
        ("dir:ltr", "Uses Left to Right Text", "Optional text direction attribute for this metadata item. left-to-right (ltr). Not for use with dc:language, dc:date, or dc:identifier metadata elements."),
        ("title-type:main", "Title: Main Title", "Indicates the associated title is the main title of the publication.  Only one main title should exist."),
        ("title-type:subtitle", "Title: Subtitle", "Indicates that the associated title is a subtitle of the publication if one exists.."),
        ("title-type:short", "Title: Short Title", "Indicates that the associated title is a shortened title of the publication if one exists."),
        ("title-type:collection", "Title: Collection Title", "Indicates that the associated title is the title of a collection that includes this publication belongs to, if one exists."),
        ("title-type:edition", "Title: Edition Title", "Indicates that the associated title is an edition title for this publications if one exists."),
        ("title-type:expanded", "Title: Expanded Title", "Indicates that the associated title is an expanded title for this publication if one exists."),
        ("alternate-script", "Alternate Script", "Provides an alternate expression of the associated property value in a language and script identified by an xml:lang attribute."),
        ("altlang", "Alternate Language", "Language code for the language used in the associated alternate-script property value."),
        ("collection-type:set", "Collection is a Set", "Property used with belongs-to-collection. Indicates the form or nature of a collection. The value 'set' should be used for a finite collection of works that together constitute a single intellectual unit; typically issued together and able to be sold as a unit.."),
        ("collection-type:series", "Collection is a Series", "Property used with belongs-to-collection. Indicates the form or nature of a collection. The value 'series'' should be used for a sequence of related works that are formally identified as a group; typically open-ended with works issued individually over time."),
        ("display-seq", "Display Sequence", "Indicates the numeric position in which to display the current property relative to identical metadata properties (e.g., to indicate the order in which to render multiple titles or multiple authors)."),
        ("file-as", "File As", "Provides the normalized form of the associated property for sorting. Typically used with author, creator, and contributor names."),
        ("group-position", "Position In Group", "Indicates the numeric position in which the EPUB Publication is ordered relative to other works belonging to the same group (whether all EPUB Publications or not)."),
        ("identifier-type", "Identifier Type", "Indicates the form or nature of an identifier. When the identifier-type value is drawn from a code list or other formal enumeration, the scheme attribute should be used to identify its source."),
        ("role", "Role", "Describes the nature of work performed by a creator or contributor (e.g., that the person is the author or editor of a work).  Typically used with the marc:relators scheme for a controlled vocabulary."),
        ("scheme", "Scheme", "This attribute is typically added to Identifier, Source, Creator, or Contributors to indicate the controlled vocabulary system employed. (e.g. marc:relators to specify valid values for the role property."),
        ("source-of", "Source of Pagination", "Indicates a unique aspect of an adapted source resource that has been retained in the given Rendition of the EPUB Publication. This specification defines the pagination value to indicate that the referenced source element is the source of the pagebreak properties defined in the content. This value should be set whenever pagination is included and the print source is known. Valid values: pagination."),
        ("custom-property", "Custom Property", "An empty metadata property or attribute you can modify."),
    });

    /// <summary>Properties/attributes available in "Add Property to Element" for EPUB 3. Names and descriptions in the interface language.</summary>
    public static IReadOnlyDictionary<string, DescriptiveInfo> Epub3Properties => Localize("Meta3Prop", Epub3PropertiesSource);

    private static readonly IReadOnlyDictionary<string, DescriptiveInfo> Epub3XPropertiesSource = Build(new[]
    {
        ("dir", "Text Direction", "Optional text direction attribute for this metadata item."),
        ("title-type", "Title Type", "Indicates the kind or type of the title"),
        ("collection-type", "Collection Type", "Property used with belongs-to-collection. Indicates the form or nature of a collection."),
        ("source-of", "Source of", "Indicates a unique aspect of an adapted source resource that has been retained in the given Rendition of the EPUB Publication."),
    });

    /// <summary>
    /// A code &#8596; name translation table for "categories" used as choice values
    /// (e.g. proper names of identifier schemes or event variants), NOT shown
    /// directly as a list in "Add Property" (EPUB 3).
    /// Names and descriptions in the interface language.</summary>
    public static IReadOnlyDictionary<string, DescriptiveInfo> Epub3XProperties => Localize("Meta3XProp", Epub3XPropertiesSource);

    private static readonly IReadOnlyDictionary<string, DescriptiveInfo> Epub2ElementsSource = Build(new[]
    {
        ("dc:creator-aut", "Author", "Represents a primary author of the book or publication"),
        ("dc:title", "Title", "The main title of the epub publication.  Only one title may exist."),
        ("dc:creator", "Creator", "Represents the name of a person, organization, etc. responsible for the creation of the content of an EPUB Publication. The attributes opf:role, opf:scheme and opf:file-as can be attached to the element to indicate the function the creator played in the creation of the content."),
        ("dc:contributor", "Contributor", "Represents the name of a person, organization, etc. that played a secondary role in the creation of the content of an EPUB Publication'"),
        ("dc:subject", "Subject", "An arbitrary phrase or keyword describing the subject in question. Use multiple 'subject' elements if needed."),
        ("dc:description", "Description", "Description of the publication's content."),
        ("dc:publisher", "Publisher", "An entity responsible for making the publication available."),
        ("dc:date", "Date", "A date associated with this epub, typically refined by event type information"),
        ("dc:date-publication", "Date: Publication", "The date of publication."),
        ("dc:date-creation", "Date: Creation", "The date of creation."),
        ("dc:date-modification", "Date: Modification", "The date of modification."),
        ("dc:type", "Type", "The nature or genre of the content of the resource."),
        ("dc:format", "Format", "The media type or dimensions of the publication. Best practice is to use a value from a controlled vocabulary (e.g. MIME media types)."),
        ("dc:source", "Source", "A reference to a resource from which the present publication is derived."),
        ("dc:language", "Language", "A language used in the publication. Choose a RFC5646 value."),
        ("dc:relation", "Relation", "A reference to a related resource. The recommended best practice is to identify the referenced resource by means of a string or number conforming to a formal identification system."),
        ("dc:coverage", "Coverage", "The extent or scope of the content of the publication's content."),
        ("dc:rights", "Rights", "Information about rights held in and over the publication. Rights information often encompasses Intellectual Property Rights (IPR), Copyright, and various Property Rights. If the Rights element is absent, no assumptions may be made about any rights held in or over the publication."),
        ("dc:identifier-doi", "Identifier: DOI", "Digital Object Identifier"),
        ("dc:identifier-isbn", "Identifier: ISBN", "International Standard Book Number"),
        ("dc:identifier-issn", "Identifier: ISSN", "International Standard Serial Number"),
        ("dc:identifier-uuid", "Identifier: UUID", "Universally Unique Identifier"),
        ("dc:identifier-amazon", "Identifier: ASIN", "Amazon Standard Identification Number"),
        ("dc:identifier-custom", "Identifier: Custom", "A custom identifier"),
        ("calibre:series", "Series", "Series title or name (from calibre)"),
        ("calibre:series_index", "Series Index", "Index of this book in the series (from calibre)"),
        ("calibre:title_sort", "Title for Sorting", "Version of ebook title to use for sorting (from calibre)"),
        ("custom-element", "Custom Element", "An empty element for you to modify"),
    });

    /// <summary>Elements available in "Add Metadata Element" for EPUB 2 (including the <c>calibre:*</c> extension metadata). Names and descriptions in the interface language.</summary>
    public static IReadOnlyDictionary<string, DescriptiveInfo> Epub2Elements => Localize("Meta2Elem", Epub2ElementsSource);

    private static readonly IReadOnlyDictionary<string, DescriptiveInfo> Epub2PropertiesSource = Build(new[]
    {
        ("id", "Id Attribute", "Optional, typically short, unique identifier string used as an attribute in the Package (opf) document."),
        ("xml:lang", "XML Language", "Optional, language specifying attribute.  Uses same codes as dc:language. Not for use with dc:language, dc:date, or dc:identifier metadata elements."),
        ("opf:file-as", "File As", "Provides the normalized form of the associated property for sorting. Typically used with author, creator, and contributor names."),
        ("opf:role", "Role", "Describes the nature of work performed by a creator or contributor (e.g., that the person is the author or editor of a work).  Typically used with the marc:relators scheme for a controlled vocabulary."),
        ("opf:scheme", "Scheme", "This attribute is typically added to dc:identifier to indicate the type of identifier being used: DOI, ISBN, ISSN, UUID, or AMAZON."),
        ("opf:scheme-custom", "Custom Scheme", "This attribute is typically added to dc:identifier to indicate that a custom identifier scheme is being used."),
        ("opf:event", "Event", "This attribute is typically added to dc:date elements to specify the date type: publication, creation, or modification."),
        ("custom-property", "Custom Attribute", "An empty metadata attribute you can modify."),
    });

    /// <summary>Properties/attributes available in "Add Property to Element" for EPUB 2. Names and descriptions in the interface language.</summary>
    public static IReadOnlyDictionary<string, DescriptiveInfo> Epub2Properties => Localize("Meta2Prop", Epub2PropertiesSource);

    private static readonly IReadOnlyDictionary<string, DescriptiveInfo> Epub2XPropertiesSource = Build(new[]
    {
        ("opf:event-published", "Published", "Event Type is Published."),
        ("opf:event-publication", "Publication", "Event Type is Publication."),
        ("opf:event-creation", "Creation", "Event Type is Creation."),
        ("opf:event-modification", "Modification", "Event Type is Modification."),
        ("DOI", "Digital Object Identifier", "Identifier Scheme: Digital Object Identifier"),
        ("ISBN", "International Standard Book Number", "Identifier Scheme: International Standard Book Number"),
        ("ISSN", "International Standard Serial Number", "Identifier Scheme: International Standard Serial Number"),
        ("UUID", "Universally Unique Identifier", "Identifier Scheme: Universally Unique Identifier"),
        ("AMAZON", "Amazon Unique Identifier", "Identifier Scheme: Amazon Unique Identifier"),
    });

    /// <summary>Like <see cref="Epub3XProperties"/>, but for EPUB 2 (identifier schemes + event variants). Names and descriptions in the interface language.</summary>
    public static IReadOnlyDictionary<string, DescriptiveInfo> Epub2XProperties => Localize("Meta2XProp", Epub2XPropertiesSource);

    /// <summary>Choice variants for the <c>title-type</c> property (EPUB 3).</summary>
    public static readonly IReadOnlyList<string> TitleTypeChoiceCodes = new[]
    {
        "title-type:main", "title-type:subtitle", "title-type:short",
        "title-type:collection", "title-type:edition", "title-type:expanded",
    };

    /// <summary>Choice variants for the <c>dir</c> property (EPUB 3).</summary>
    public static readonly IReadOnlyList<string> TextDirectionChoiceCodes = new[] { "dir:rtl", "dir:ltr" };

    /// <summary>Choice variants for the <c>collection-type</c> property (EPUB 3).</summary>
    public static readonly IReadOnlyList<string> CollectionTypeChoiceCodes = new[]
    {
        "collection-type:set", "collection-type:series",
    };

    /// <summary>Choice variants for the <c>opf:event</c> attribute (EPUB 2).</summary>
    public static readonly IReadOnlyList<string> Epub2EventChoiceCodes = new[]
    {
        "opf:event-publication", "opf:event-creation", "opf:event-modification",
    };

    /// <summary>Choice variants for the <c>opf:scheme</c> attribute (EPUB 2).</summary>
    public static readonly IReadOnlyList<string> Epub2SchemeChoiceCodes = new[]
    {
        "marc:relators", "DOI", "ISBN", "ISSN", "UUID", "AMAZON",
    };

    /// <summary>The known <c>opf:scheme</c> schemes (EPUB 2) — any other value means a custom scheme.</summary>
    public static readonly IReadOnlyList<string> KnownEpub2OpfSchemes = new[]
    {
        "MARC:RELATORS", "DOI", "ISBN", "ISSN", "UUID", "AMAZON",
    };

    // A copy of the map with names and descriptions from CoreStrings (key: <map prefix>_<code>_Name/Desc).
    private static Dictionary<string, DescriptiveInfo> Localize(string catalog, IReadOnlyDictionary<string, DescriptiveInfo> source)
    {
        Dictionary<string, DescriptiveInfo> result = new(source.Count, StringComparer.Ordinal);
        foreach (KeyValuePair<string, DescriptiveInfo> pair in source)
        {
            result[pair.Key] = new DescriptiveInfo(
                CatalogText.Name(catalog, pair.Key, pair.Value.Name),
                CatalogText.Description(catalog, pair.Key, pair.Value.Description));
        }

        return result;
    }

    private static Dictionary<string, DescriptiveInfo> Build(IEnumerable<(string Code, string Name, string Description)> rows)
    {
        Dictionary<string, DescriptiveInfo> map = new(StringComparer.Ordinal);
        foreach ((string code, string name, string description) in rows)
        {
            map[code] = new DescriptiveInfo(name, description);
        }

        return map;
    }
}
