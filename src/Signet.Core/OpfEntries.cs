using System;
using System.Collections.Generic;
using System.Text;

namespace Signet.Core;

/// <summary>Shared helper functions for serializing OPF entries back to XML.</summary>
internal static class OpfXml
{
    /// <summary>Appends attributes in order of occurrence, escaping only <c>"</c>.</summary>
    public static void AppendAttributes(StringBuilder builder, TagAttributes attributes)
    {
        foreach (KeyValuePair<string, string> pair in attributes)
        {
            builder.Append(' ').Append(pair.Key).Append("=\"")
                .Append(pair.Value.Replace("\"", "&quot;", StringComparison.Ordinal)).Append('"');
        }
    }
}

/// <summary>
/// The <c>package</c> element: the version, the unique identifier id and the other attributes
/// (e.g. <c>xmlns</c>, <c>xml:lang</c>, <c>prefix</c>).
/// </summary>
public sealed record PackageEntry
{
    /// <summary>The OPF version, e.g. <c>2.0</c> or <c>3.0</c>.</summary>
    public string Version { get; set; } = "2.0";

    /// <summary>The value of the <c>unique-identifier</c> attribute (an id pointing at <c>dc:identifier</c>).</summary>
    public string UniqueIdentifier { get; set; } = "bookid";

    /// <summary>The other attributes of the <c>package</c> element, in order of occurrence.</summary>
    public TagAttributes Attributes { get; set; } = new();

    /// <summary>Serializes the opening <c>&lt;package ...&gt;</c> tag (with a trailing <c>\n</c>).</summary>
    public string ToXml()
    {
        StringBuilder sb = new();
        sb.Append("<package version=\"").Append(Version).Append('"');
        sb.Append(" unique-identifier=\"").Append(UniqueIdentifier).Append('"');
        OpfXml.AppendAttributes(sb, Attributes);
        sb.Append(">\n");
        return sb.ToString();
    }
}

/// <summary>The attributes of the <c>metadata</c> element (namespaces etc.).</summary>
public sealed record MetadataAttributesEntry
{
    /// <summary>The attributes of the <c>metadata</c> element, in order of occurrence.</summary>
    public TagAttributes Attributes { get; set; } = new();

    /// <summary>Serializes the opening <c>&lt;metadata ...&gt;</c> tag (a 2-space indent).</summary>
    public string ToXml()
    {
        StringBuilder sb = new();
        sb.Append("  <metadata");
        OpfXml.AppendAttributes(sb, Attributes);
        sb.Append(">\n");
        return sb.ToString();
    }
}

/// <summary>
/// A single metadata entry: a <c>dc:*</c>, <c>meta</c> or <c>link</c> element.
/// <see cref="Content"/> is the text content (empty for empty tags).
/// </summary>
public sealed record MetaEntry
{
    /// <summary>The element name, e.g. <c>dc:title</c>, <c>meta</c>.</summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>The element's text content (trimmed), empty for an empty tag.</summary>
    public string Content { get; set; } = string.Empty;

    /// <summary>The element's attributes, in order of occurrence (e.g. <c>refines</c>, <c>property</c>, <c>id</c>).</summary>
    public TagAttributes Attributes { get; set; } = new();

    /// <summary>Serializes the entry (a 4-space indent). Empty content =&gt; an empty tag.</summary>
    public string ToXml()
    {
        StringBuilder sb = new();
        sb.Append("    <").Append(Name);
        OpfXml.AppendAttributes(sb, Attributes);
        if (Content.Length == 0)
        {
            sb.Append(" />\n");
        }
        else
        {
            sb.Append('>').Append(Content).Append("</").Append(Name).Append(">\n");
        }

        return sb.ToString();
    }
}

/// <summary>
/// A <c>manifest</c> entry: <c>id</c>, <c>href</c> (always URL-encoded), <c>media-type</c>
/// and the other attributes (<c>properties</c>, <c>fallback</c>, <c>media-overlay</c>).
/// </summary>
public sealed record ManifestEntry
{
    /// <summary>The entry identifier (unique in the manifest).</summary>
    public string Id { get; set; } = string.Empty;

    /// <summary>The path to the resource relative to the OPF folder, URL-encoded.</summary>
    public string Href { get; set; } = string.Empty;

    /// <summary>The resource's MIME type.</summary>
    public string MediaType { get; set; } = string.Empty;

    /// <summary>The entry's other attributes, in order of occurrence.</summary>
    public TagAttributes Attributes { get; set; } = new();

    /// <summary>Serializes <c>&lt;item .../&gt;</c> (a 4-space indent, the order <c>id</c>, <c>href</c>, <c>media-type</c>).</summary>
    public string ToXml()
    {
        StringBuilder sb = new();
        sb.Append("    <item id=\"").Append(Id).Append('"');
        sb.Append(" href=\"").Append(Href).Append('"');
        sb.Append(" media-type=\"").Append(MediaType).Append('"');
        OpfXml.AppendAttributes(sb, Attributes);
        sb.Append("/>\n");
        return sb.ToString();
    }
}

/// <summary>The attributes of the <c>spine</c> element (<c>toc</c>, <c>page-progression-direction</c>).</summary>
public sealed record SpineAttributesEntry
{
    /// <summary>The attributes of the <c>spine</c> element, in order of occurrence.</summary>
    public TagAttributes Attributes { get; set; } = new();

    /// <summary>Serializes the opening <c>&lt;spine ...&gt;</c> tag (a 2-space indent).</summary>
    public string ToXml()
    {
        StringBuilder sb = new();
        sb.Append("  <spine");
        OpfXml.AppendAttributes(sb, Attributes);
        sb.Append(">\n");
        return sb.ToString();
    }
}

/// <summary>A <c>spine</c> entry: <c>idref</c> and the other attributes (<c>linear</c>, <c>properties</c>, <c>id</c>).</summary>
public sealed record SpineEntry
{
    /// <summary>A reference to the <c>id</c> of a manifest entry.</summary>
    public string IdRef { get; set; } = string.Empty;

    /// <summary>The entry's other attributes, in order of occurrence.</summary>
    public TagAttributes Attributes { get; set; } = new();

    /// <summary>Serializes <c>&lt;itemref .../&gt;</c> (a 4-space indent).</summary>
    public string ToXml()
    {
        StringBuilder sb = new();
        sb.Append("    <itemref idref=\"").Append(IdRef).Append('"');
        OpfXml.AppendAttributes(sb, Attributes);
        sb.Append("/>\n");
        return sb.ToString();
    }
}

/// <summary>A <c>guide</c> entry (EPUB 2): <c>type</c>, <c>title</c>, <c>href</c>.</summary>
public sealed record GuideEntry
{
    /// <summary>The reference type, e.g. <c>toc</c>, <c>text</c>, <c>cover</c>.</summary>
    public string Type { get; set; } = string.Empty;

    /// <summary>The reference title.</summary>
    public string Title { get; set; } = string.Empty;

    /// <summary>The target path.</summary>
    public string Href { get; set; } = string.Empty;

    /// <summary>Serializes <c>&lt;reference .../&gt;</c> (a 4-space indent). Values are not escaped.</summary>
    public string ToXml()
    {
        StringBuilder sb = new();
        sb.Append("    <reference type=\"").Append(Type).Append('"');
        sb.Append(" title=\"").Append(Title).Append('"');
        sb.Append(" href=\"").Append(Href).Append('"');
        sb.Append("/>\n");
        return sb.ToString();
    }
}

/// <summary>A <c>bindings</c> entry (EPUB 3, deprecated): <c>media-type</c> and <c>handler</c>.</summary>
public sealed record BindingsEntry
{
    /// <summary>The MIME type handled by the handler.</summary>
    public string MediaType { get; set; } = string.Empty;

    /// <summary>The id of the manifest entry that is the handler.</summary>
    public string Handler { get; set; } = string.Empty;

    /// <summary>Serializes <c>&lt;mediaType .../&gt;</c> (a 4-space indent; a double space after the name).</summary>
    public string ToXml()
    {
        StringBuilder sb = new();
        sb.Append("    <mediaType  media-type=\"").Append(MediaType).Append('"');
        sb.Append(" handler=\"").Append(Handler).Append('"');
        sb.Append("/>\n");
        return sb.ToString();
    }
}
