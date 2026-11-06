using System;
using System.Collections.Generic;
using System.IO;
using System.Xml;

namespace Signet.EpubCheck.Core.Jing.Sax;

/// <summary>
/// Reads a document with <see cref="XmlReader"/> and sends it as SAX events (the part of SAX's <c>XMLReader</c> Jing
/// relies on): the namespace declarations come as prefix mappings, not as attributes; text, CDATA sections and white
/// space come as characters; entities are expanded (an internal DTD subset is read, nothing is fetched). The content
/// handler can be replaced during the parse, which Jing's schema parser does at every element.
/// </summary>
/// <remarks>
/// The locator gives the position <see cref="XmlReader"/> knows: the start of the node (the name of a start-tag), where
/// a SAX parser gives the end of the tag. Notations and unparsed entities are not reported: <see cref="XmlReader"/>
/// does not expose the declarations of the DTD.
/// </remarks>
public sealed class XmlEventReader
{
    private const string XmlnsNamespace = "http://www.w3.org/2000/xmlns/";
    private const string XmlnsPrefix = "xmlns";

    /// <summary>The handler the next events go to.</summary>
    public IContentHandler? ContentHandler { get; set; }

    /// <summary>The handler of DTD declarations (not called: see the remarks of the class).</summary>
    public IDtdHandler? DtdHandler { get; set; }

    /// <summary>The handler of a document that is not well-formed (told before the reader throws).</summary>
    public ISaxErrorHandler? ErrorHandler { get; set; }

    /// <summary>Reads the document and sends its events.</summary>
    /// <exception cref="SaxParseException">The document is not well-formed.</exception>
    public void Parse(InputSource input)
    {
        ArgumentNullException.ThrowIfNull(input);
        Locator locator = new(input.SystemId);
        XmlReaderSettings settings = new()
        {
            DtdProcessing = DtdProcessing.Parse,
            XmlResolver = null,
            IgnoreComments = true,
            CloseInput = true,
        };
        try
        {
            using Stream stream = input.Open();
            using XmlReader reader = XmlReader.Create(stream, settings, input.SystemId ?? string.Empty);
            locator.Reader = reader as IXmlLineInfo;
            Handler.SetDocumentLocator(locator);
            Handler.StartDocument();
            Read(reader);
            Handler.EndDocument();
        }
        catch (XmlException e)
        {
            SaxParseException exception = new(e.Message, input.SystemId, e.LineNumber, e.LinePosition, e);
            ErrorHandler?.OnFatalError(exception);
            throw exception;
        }
    }

    private IContentHandler Handler => ContentHandler ?? throw new InvalidOperationException("No content handler");

    private void Read(XmlReader reader)
    {
        Stack<List<string>> openPrefixes = new();
        AttributeList attributes = new();
        while (reader.Read())
        {
            switch (reader.NodeType)
            {
                case XmlNodeType.Element:
                    List<string> prefixes = StartElement(reader, attributes);
                    if (reader.IsEmptyElement)
                    {
                        EndElement(reader.NamespaceURI, reader.LocalName, reader.Name, prefixes);
                    }
                    else
                    {
                        openPrefixes.Push(prefixes);
                    }

                    break;
                case XmlNodeType.EndElement:
                    EndElement(reader.NamespaceURI, reader.LocalName, reader.Name, openPrefixes.Pop());
                    break;
                case XmlNodeType.Text or XmlNodeType.CDATA or XmlNodeType.Whitespace or XmlNodeType.SignificantWhitespace:
                    Handler.Characters(reader.Value);
                    break;
                case XmlNodeType.ProcessingInstruction:
                    Handler.ProcessingInstruction(reader.Name, reader.Value);
                    break;
            }
        }
    }

    private List<string> StartElement(XmlReader reader, AttributeList attributes)
    {
        attributes.Clear();
        List<(string Prefix, string Uri)> declarations = [];
        if (reader.MoveToFirstAttribute())
        {
            do
            {
                if (string.Equals(reader.NamespaceURI, XmlnsNamespace, StringComparison.Ordinal))
                {
                    string prefix = string.Equals(reader.Prefix, XmlnsPrefix, StringComparison.Ordinal) ? reader.LocalName : string.Empty;
                    declarations.Add((prefix, reader.Value));
                }
                else
                {
                    attributes.Add(reader.NamespaceURI, reader.LocalName, reader.Name, reader.Value);
                }
            }
            while (reader.MoveToNextAttribute());
            reader.MoveToElement();
        }

        List<string> prefixes = new(declarations.Count);
        foreach ((string prefix, string uri) in declarations)
        {
            Handler.StartPrefixMapping(prefix, uri);
            prefixes.Add(prefix);
        }

        Handler.StartElement(reader.NamespaceURI, reader.LocalName, reader.Name, attributes);
        return prefixes;
    }

    private void EndElement(string namespaceUri, string localName, string qName, List<string> prefixes)
    {
        Handler.EndElement(namespaceUri, localName, qName);
        for (int i = prefixes.Count - 1; i >= 0; i--)
        {
            Handler.EndPrefixMapping(prefixes[i]);
        }
    }

    private sealed class Locator(string? systemId) : ILocator
    {
        public IXmlLineInfo? Reader { get; set; }

        public string? SystemId { get; } = systemId;

        public int LineNumber => Reader is { } info && info.HasLineInfo() ? info.LineNumber : FixedLocator.Unknown;

        public int ColumnNumber => Reader is { } info && info.HasLineInfo() ? info.LinePosition : FixedLocator.Unknown;
    }
}
