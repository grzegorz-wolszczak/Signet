using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Signet.Core.BookManipulation;
using Signet.Core.Resources;

namespace Signet.Core.Metadata;

/// <summary>
/// The OPF metadata editing state between <see cref="MetadataEditModel"/>'s <c>Extract</c> and <c>Save</c>.
/// <see cref="Elements"/> is the editable tree; the remaining fields are
/// carried untouched between loading and saving.
/// </summary>
public sealed class MetadataEditState
{
    /// <summary>The editable tree of recognized metadata elements (top-level elements).</summary>
    public IList<MetadataEntry> Elements { get; } = new List<MetadataEntry>();

    /// <summary>Metadata entries not recognized by the editor — rewritten unchanged.</summary>
    internal List<MetaEntry> Other { get; } = new();

    /// <summary>
    /// Identifiers already used in the document (other than those "freed" by recognized elements) —
    /// the pool checked by <see cref="MetadataEditModel.ValidId"/>.
    /// </summary>
    internal List<string> AvailableIds { get; } = new();

    /// <summary>
    /// The target attributes of the <c>&lt;metadata&gt;</c> tag (for EPUB 2 with forced <c>xmlns:opf</c>/
    /// <c>xmlns:dc</c>).
    /// </summary>
    internal MetadataAttributesEntry MetadataAttributes { get; set; } = new();
}

/// <summary>
/// The view-independent engine of the "Metadata Editor": decomposes the OPF <c>&lt;metadata&gt;</c>
/// into an editable tree of recognized elements (with <c>meta refines=</c> folded into the
/// child's attributes for EPUB 3, or directly from <c>opf:*</c> attributes for EPUB 2) and rebuilds it
/// on save.
/// </summary>
/// <remarks>
/// <c>dc:language</c> normalization ("xx-YY") splits the value at the
/// <b>first</b> hyphen, so codes with more than one hyphen (e.g. <c>zh-Hans-CN</c>) are handled.
/// </remarks>
public static class MetadataEditModel
{
    /// <summary>Builds the editable metadata state from the current book.</summary>
    public static MetadataEditState Extract(Book book)
    {
        ArgumentNullException.ThrowIfNull(book);
        return Extract(book.GetOpf().GetOpfDocument(), book.IsEpub3);
    }

    /// <summary>Like <see cref="Extract(Book)"/>, but working directly on <see cref="OpfDocument"/>.</summary>
    public static MetadataEditState Extract(OpfDocument document, bool isEpub3)
    {
        ArgumentNullException.ThrowIfNull(document);

        MetadataEditState state = new();
        List<string> idlst = CollectAllIds(document);
        string uniqueId = document.UniqueIdentifierId;

        if (isEpub3)
        {
            ExtractEpub3(document, state, uniqueId, idlst);
            state.MetadataAttributes = new MetadataAttributesEntry
            {
                Attributes = new TagAttributes(document.MetadataAttributes.Attributes),
            };
        }
        else
        {
            ExtractEpub2(document, state, uniqueId, idlst);
        }

        state.AvailableIds.AddRange(idlst);
        return state;
    }

    /// <summary>Writes the edited state back to the book.</summary>
    public static void Save(Book book, MetadataEditState state)
    {
        ArgumentNullException.ThrowIfNull(book);
        ArgumentNullException.ThrowIfNull(state);

        OpfResource opf = book.GetOpf();
        OpfDocument document = opf.GetOpfDocument();
        Save(document, state, book.IsEpub3);
        opf.SetOpfDocument(document);
        book.Modified = true;
    }

    /// <summary>Like <see cref="Save(Book,MetadataEditState)"/>, but working directly on <see cref="OpfDocument"/>.</summary>
    public static void Save(OpfDocument document, MetadataEditState state, bool isEpub3)
    {
        ArgumentNullException.ThrowIfNull(document);
        ArgumentNullException.ThrowIfNull(state);

        List<string> idlst = new(state.AvailableIds);
        List<MetaEntry> newMetadata = new();

        foreach (MetadataEntry element in state.Elements)
        {
            if (isEpub3)
            {
                AppendEpub3Element(element, idlst, newMetadata);
            }
            else
            {
                AppendEpub2Element(element, idlst, newMetadata);
            }
        }

        newMetadata.AddRange(state.Other);

        document.Metadata.Clear();
        document.Metadata.AddRange(newMetadata);

        if (!isEpub3)
        {
            document.MetadataAttributes.Attributes = state.MetadataAttributes.Attributes;
        }
    }

    // --- EPUB 3: extraction ---

    private static void ExtractEpub3(OpfDocument document, MetadataEditState state, string uniqueId, List<string> idlst)
    {
        List<(string Name, string Content, TagAttributes Attrs)> rec = new();
        Dictionary<string, int> id2rec = new(StringComparer.Ordinal);
        List<MetaEntry> refines = new();

        foreach (MetaEntry mentry in document.Metadata)
        {
            string mname = mentry.Name;
            string id = mentry.Attributes.Value("id");

            if (mname == "dc:identifier" && id.Length > 0 && string.Equals(id, uniqueId, StringComparison.Ordinal))
            {
                state.Other.Add(mentry);
                continue;
            }

            if (MetadataFieldCatalog.RecognizedDcElements.Contains(mname))
            {
                TagAttributes attrs = new(mentry.Attributes);
                attrs.Remove("xmlns:dc");
                string content = mname == "dc:language" ? NormalizeLanguage(mentry.Content) : mentry.Content;
                rec.Add((mname, content, attrs));
                if (id.Length > 0)
                {
                    id2rec[id] = rec.Count - 1;
                    idlst.Remove(id);
                }
            }
            else if (mname == "meta" && mentry.Attributes.Value("refines").Length > 0)
            {
                refines.Add(mentry);
            }
            else if (mname == "meta" && mentry.Attributes.Value("property").Length > 0)
            {
                string property = mentry.Attributes.Value("property");
                TagAttributes attrs = new(mentry.Attributes);
                string useName = mname;
                if (MetadataFieldCatalog.RecognizedMetaProperties.Contains(property))
                {
                    attrs.Remove("property");
                    useName = property;
                }

                rec.Add((useName, mentry.Content, attrs));
                if (id.Length > 0)
                {
                    id2rec[id] = rec.Count - 1;
                    idlst.Remove(id);
                }
            }
            else
            {
                state.Other.Add(mentry);
            }
        }

        foreach (MetaEntry refineEntry in refines)
        {
            string rid = refineEntry.Attributes.Value("id");
            string tid = refineEntry.Attributes.Value("refines");
            string prop = refineEntry.Attributes.Value("property");
            string scheme = refineEntry.Attributes.Value("scheme");

            if (tid.StartsWith('#') && id2rec.TryGetValue(tid[1..], out int pos))
            {
                (string dname, string dcontent, TagAttributes dattr) = rec[pos];
                dattr.Set(prop, refineEntry.Content);
                if (scheme.Length > 0)
                {
                    dattr.Set("scheme", scheme);
                }

                if (prop == "alternate-script")
                {
                    dattr.Set("altlang", refineEntry.Attributes.Value("xml:lang"));
                }

                rec[pos] = (dname, dcontent, dattr);
                if (rid.Length > 0)
                {
                    idlst.Remove(rid);
                }
            }
            else
            {
                // refines points at an unrecognized element or has no "#id" — left untouched.
                state.Other.Add(refineEntry);
            }
        }

        BuildTree(state, rec);
    }

    // --- EPUB 2: extraction ---

    private static void ExtractEpub2(OpfDocument document, MetadataEditState state, string uniqueId, List<string> idlst)
    {
        List<(string Name, string Content, TagAttributes Attrs)> rec = new();

        foreach (MetaEntry mentry in document.Metadata)
        {
            string mname = mentry.Name;
            string id = mentry.Attributes.Value("id");

            if (mname == "dc:identifier" && id.Length > 0 && string.Equals(id, uniqueId, StringComparison.Ordinal))
            {
                state.Other.Add(mentry);
                continue;
            }

            if (MetadataFieldCatalog.RecognizedDcElements.Contains(mname))
            {
                TagAttributes attrs = new(mentry.Attributes);
                attrs.Remove("xmlns:dc");
                string content = mname == "dc:language" ? NormalizeLanguage(mentry.Content) : mentry.Content;
                rec.Add((mname, content, attrs));
                if (id.Length > 0)
                {
                    idlst.Remove(id);
                }
            }
            else if (mname == "meta" && mentry.Attributes.Value("name") is { Length: > 0 } name
                     && !string.Equals(name, "cover", StringComparison.Ordinal))
            {
                TagAttributes attrs = new(mentry.Attributes);
                attrs.Remove("name");
                string content = attrs.Value("content");
                attrs.Remove("content");
                rec.Add((name, content, attrs));
                if (id.Length > 0)
                {
                    idlst.Remove(id);
                }
            }
            else
            {
                state.Other.Add(mentry);
            }
        }

        BuildTree(state, rec);

        // Forcing xmlns:opf / xmlns:dc on the <metadata> tag — they are always overwritten unconditionally.
        TagAttributes metadataAttrs = new(document.MetadataAttributes.Attributes);
        metadataAttrs.Set("xmlns:opf", "http://www.idpf.org/2007/opf");
        metadataAttrs.Set("xmlns:dc", "http://purl.org/dc/elements/1.1/");
        state.MetadataAttributes = new MetadataAttributesEntry { Attributes = metadataAttrs };
    }

    private static void BuildTree(MetadataEditState state, List<(string Name, string Content, TagAttributes Attrs)> rec)
    {
        foreach ((string name, string content, TagAttributes attrs) in rec)
        {
            MetadataEntry entry = new() { Code = name, Content = Utility.DecodeXml(content) };
            foreach (string key in attrs.Keys.OrderBy(k => k, StringComparer.Ordinal))
            {
                entry.Children.Add(new MetadataEntry { Code = key, Content = Utility.DecodeXml(attrs.Value(key)) });
            }

            state.Elements.Add(entry);
        }
    }

    // --- EPUB 3: saving ---

    private static void AppendEpub3Element(MetadataEntry element, List<string> idlst, List<MetaEntry> newMetadata)
    {
        string mname = element.Code;
        TagAttributes mattr = new();
        if (MetadataFieldCatalog.RecognizedMetaProperties.Contains(mname))
        {
            mattr.Set("property", mname);
            mname = "meta";
        }

        TagAttributes refines = new();
        string? explicitId = null;

        foreach (MetadataEntry child in element.Children)
        {
            string name = child.Code;
            string value = child.Content;
            bool isAttribute = name is "id" or "xml:lang" or "dir" or "xmlns"
                || (mname == "meta" && name == "property")
                || name.StartsWith("xmlns:", StringComparison.Ordinal);

            if (isAttribute)
            {
                if (name == "id")
                {
                    explicitId = ValidId(value, idlst);
                    mattr.Set("id", explicitId);
                    idlst.Add(explicitId);
                }
                else
                {
                    mattr.Set(name, value);
                }
            }
            else
            {
                refines.Set(name, value);
            }
        }

        if (!refines.IsEmpty && explicitId is null)
        {
            string root = MetadataFieldCatalog.IdRootPrefixes.GetValueOrDefault(mname, MetadataFieldCatalog.DefaultIdRootPrefix);
            explicitId = ValidId(root, idlst);
            mattr.Set("id", explicitId);
            idlst.Add(explicitId);
        }

        newMetadata.Add(new MetaEntry { Name = mname, Content = Utility.EncodeXml(element.Content), Attributes = mattr });

        foreach (string prop in refines.Keys)
        {
            if (prop is "scheme" or "altlang")
            {
                continue;
            }

            TagAttributes rattr = new();
            rattr.Set("refines", "#" + explicitId);
            rattr.Set("property", prop);

            if (prop == "alternate-script" && refines.Contains("altlang"))
            {
                rattr.Set("xml:lang", refines.Value("altlang"));
            }

            if (prop is "role" or "identifier-type" or "title-type" or "collection-type" && refines.Contains("scheme"))
            {
                rattr.Set("scheme", refines.Value("scheme"));
            }

            newMetadata.Add(new MetaEntry { Name = "meta", Content = Utility.EncodeXml(refines.Value(prop)), Attributes = rattr });
        }
    }

    // --- EPUB 2: saving ---

    private static void AppendEpub2Element(MetadataEntry element, List<string> idlst, List<MetaEntry> newMetadata)
    {
        string mname = element.Code;
        string? elementTextContent = element.Content;
        TagAttributes mattr = new();

        if (!MetadataFieldCatalog.RecognizedDcElements.Contains(mname))
        {
            mattr.Set("name", mname);
            mattr.Set("content", element.Content);
            mname = "meta";
            elementTextContent = null;
        }

        foreach (MetadataEntry child in element.Children)
        {
            if (child.Code == "id")
            {
                // The allocated id is NOT added back to the
                // pool — collisions between two manually entered identical ids are not detected.
                mattr.Set("id", ValidId(child.Content, idlst));
            }
            else
            {
                mattr.Set(child.Code, child.Content);
            }
        }

        newMetadata.Add(new MetaEntry
        {
            Name = mname,
            Content = elementTextContent is null ? string.Empty : Utility.EncodeXml(elementTextContent),
            Attributes = mattr,
        });
    }

    /// <summary>
    /// Finds a free identifier: <paramref name="id"/> if it is not used yet, otherwise
    /// <c>id001</c>, <c>id002</c>, ...
    /// </summary>
    internal static string ValidId(string id, ICollection<string> idlst)
    {
        ArgumentNullException.ThrowIfNull(id);
        ArgumentNullException.ThrowIfNull(idlst);

        string candidate = id;
        int pos = 1;
        while (idlst.Contains(candidate))
        {
            candidate = id + pos.ToString("D3", CultureInfo.InvariantCulture);
            pos++;
        }

        return candidate;
    }

    private static string NormalizeLanguage(string content)
    {
        if (string.IsNullOrEmpty(content))
        {
            return string.Empty;
        }

        int dash = content.IndexOf('-', StringComparison.Ordinal);
        if (dash < 0)
        {
            return content.ToLowerInvariant();
        }

        string lang = content[..dash].ToLowerInvariant();
        string region = content[(dash + 1)..].ToUpperInvariant();
        return lang + "-" + region;
    }

    private static List<string> CollectAllIds(OpfDocument document)
    {
        List<string> ids = new();

        void AddIfPresent(string value)
        {
            if (value.Length > 0)
            {
                ids.Add(value);
            }
        }

        AddIfPresent(document.Package.Attributes.Value("id"));
        AddIfPresent(document.MetadataAttributes.Attributes.Value("id"));
        foreach (MetaEntry meta in document.Metadata)
        {
            AddIfPresent(meta.Attributes.Value("id"));
        }

        foreach (ManifestEntry item in document.Manifest)
        {
            AddIfPresent(item.Id);
        }

        AddIfPresent(document.SpineAttributes.Attributes.Value("id"));
        foreach (SpineEntry item in document.Spine)
        {
            AddIfPresent(item.Attributes.Value("id"));
        }

        return ids;
    }
}
