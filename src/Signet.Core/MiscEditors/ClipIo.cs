using System;
using System.Collections.Generic;
using System.Xml.Linq;
using Signet.Core.Misc;

namespace Signet.Core.MiscEditors;

/// <summary>
/// Serialization / deserialization of the clip library. The format is XML — the store file and the only
/// import/export format:
/// <code>
/// &lt;clips&gt;
///   &lt;group name="Tags"&gt;
///     &lt;clip name="&amp;lt;test/&amp;gt;"&gt;&amp;lt;test/&amp;gt;&lt;/clip&gt;
///   &lt;/group&gt;
/// &lt;/clips&gt;
/// </code>
/// Groups nest (<see cref="GroupedXml"/>), a clip's text is the element's content.
/// </summary>
public static class ClipIo
{
    private const string RootElement = "clips";
    private const string ClipElement = "clip";

    /// <summary>Reads the entries of a clips XML text (leniently — an error or another document yields an empty list).</summary>
    public static IReadOnlyList<ClipEntry> ReadXml(string? xml) => FromDocument(XmlConfig.TryParse(xml));

    /// <summary>Writes entries as a clips XML text.</summary>
    public static string WriteXml(IReadOnlyList<ClipEntry> entries) => XmlConfig.ToText(ToDocument(entries));

    /// <summary>The entries of a clips XML document (an empty list for <see langword="null"/> or another document).</summary>
    public static IReadOnlyList<ClipEntry> FromDocument(XDocument? document)
    {
        if (document?.Root is not { } root || root.Name != RootElement)
        {
            return Array.Empty<ClipEntry>();
        }

        return GroupedXml.Read(
            root,
            ClipElement,
            (path, element) => new ClipEntry(false, path, XmlConfig.Attribute(element, GroupedXml.NameAttribute), element.Value),
            (path, name) => new ClipEntry(true, path, name, string.Empty));
    }

    /// <summary>The clips XML document of the entries.</summary>
    public static XDocument ToDocument(IReadOnlyList<ClipEntry> entries)
    {
        ArgumentNullException.ThrowIfNull(entries);
        XElement root = new(RootElement);
        GroupedXml.Write(
            root,
            entries,
            e => e.GroupPath,
            e => e.IsGroup,
            e => e.Name,
            e => new XElement(ClipElement, new XAttribute(GroupedXml.NameAttribute, e.Name), e.Text));
        return new XDocument(root);
    }
}
