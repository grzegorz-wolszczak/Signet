using System;
using System.Collections.Generic;
using System.Text;
using System.Text.RegularExpressions;
using System.Xml;
using System.Xml.Linq;

namespace Signet.Core.BookManipulation;

/// <summary>
/// Pretty-prints plain XML (OPF / NCX / SMIL / page-map) for well-formed input: two-space
/// indentation, each element on its own line, empty elements self-closing only for the "void"
/// list of the given MIME type, other empty elements written as a <c>&lt;x&gt;&lt;/x&gt;</c> pair.
/// </summary>
internal static class XmlPrettyPrinter
{
    private const string XmlnsNamespace = "http://www.w3.org/2000/xmlns/";

    private static readonly Regex XmlHeader = new(@"^<\s*\?xml\s*[^\?>]*\?*>\s*", RegexOptions.IgnoreCase | RegexOptions.Compiled);

    private const string Declaration = "<?xml version=\"1.0\" encoding=\"utf-8\"?>\n";

    // Default void tags for ebook XML types not listed in GetVoidTags.
    private static readonly string[] EbookXmlEmptyTags = { "meta", "item", "itemref", "reference", "content" };

    /// <summary>
    /// Formats <paramref name="source"/>. Returns <c>false</c> (and an empty <paramref name="formatted"/>)
    /// when the input is not well-formed — the caller then returns the source unchanged.
    /// </summary>
    public static bool TryPrettyPrint(string source, string mediaType, out string formatted)
    {
        string body = XmlHeader.Replace(source, string.Empty, 1);

        XDocument document;
        try
        {
            document = XDocument.Parse(body, LoadOptions.None);
        }
        catch (XmlException)
        {
            formatted = string.Empty;
            return false;
        }

        if (document.Root is null)
        {
            formatted = string.Empty;
            return false;
        }

        HashSet<string> voidTags = new(GetVoidTags(mediaType), StringComparer.Ordinal);

        StringBuilder builder = new();
        XmlWriterSettings settings = new()
        {
            Indent = true,
            IndentChars = "  ",
            OmitXmlDeclaration = true,
            NewLineChars = "\n",
        };

        using (XmlWriter writer = XmlWriter.Create(builder, settings))
        {
            WriteElement(writer, document.Root, voidTags);
        }

        formatted = Declaration + builder;
        return true;
    }

    private static string[] GetVoidTags(string mediaType) => mediaType switch
    {
        "application/oebps-package+xml" => new[] { "item", "itemref", "mediatype", "mediaType", "reference" },
        "application/x-dtbncx+xml" => new[] { "meta", "reference", "content" },
        "application/smil+xml" => new[] { "text", "audio" },
        "application/oebps-page-map+xml" => new[] { "page" },
        _ => EbookXmlEmptyTags,
    };

    private static void WriteElement(XmlWriter writer, XElement element, HashSet<string> voidTags)
    {
        string prefix = element.GetPrefixOfNamespace(element.Name.Namespace) ?? string.Empty;
        writer.WriteStartElement(prefix, element.Name.LocalName, element.Name.NamespaceName);

        foreach (XAttribute attribute in element.Attributes())
        {
            if (attribute.IsNamespaceDeclaration)
            {
                if (attribute.Name.LocalName == "xmlns")
                {
                    writer.WriteAttributeString("xmlns", attribute.Value);
                }
                else
                {
                    writer.WriteAttributeString(attribute.Name.LocalName, XmlnsNamespace, attribute.Value);
                }

                continue;
            }

            string attributePrefix = attribute.Name.Namespace == XNamespace.None
                ? string.Empty
                : element.GetPrefixOfNamespace(attribute.Name.Namespace) ?? string.Empty;
            writer.WriteAttributeString(attributePrefix, attribute.Name.LocalName, attribute.Name.NamespaceName, attribute.Value);
        }

        bool hasChildNodes = false;
        foreach (XNode node in element.Nodes())
        {
            hasChildNodes = true;
            switch (node)
            {
                case XElement childElement:
                    WriteElement(writer, childElement, voidTags);
                    break;
                case XText text:
                    writer.WriteString(text.Value);
                    break;
                case XComment comment:
                    writer.WriteComment(comment.Value);
                    break;
                case XProcessingInstruction pi:
                    writer.WriteProcessingInstruction(pi.Target, pi.Data);
                    break;
            }
        }

        if (!hasChildNodes && voidTags.Contains(element.Name.LocalName))
        {
            writer.WriteEndElement();
        }
        else
        {
            writer.WriteFullEndElement();
        }
    }
}
