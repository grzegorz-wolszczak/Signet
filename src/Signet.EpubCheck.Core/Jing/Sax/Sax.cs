using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace Signet.EpubCheck.Core.Jing.Sax;

/// <summary>Where the reader is in a document (SAX's <c>Locator</c>): read at the moment of an event.</summary>
public interface ILocator
{
    /// <summary>The URI of the document, or <c>null</c>.</summary>
    string? SystemId { get; }

    /// <summary>The line (from 1), or -1 when unknown.</summary>
    int LineNumber { get; }

    /// <summary>The column (from 1), or -1 when unknown.</summary>
    int ColumnNumber { get; }
}

/// <summary>A locator that also knows the XML version of the document (SAX's <c>Locator2</c>).</summary>
public interface ILocator2 : ILocator
{
    /// <summary>The XML version of the document (<c>1.0</c> when it has no XML declaration).</summary>
    string XmlVersion { get; }
}

/// <summary>A location that does not move (a copy of a <see cref="ILocator"/> taken at one moment).</summary>
public sealed record FixedLocator(string? SystemId, int LineNumber, int ColumnNumber) : ILocator
{
    /// <summary>The value of a line or column that is not known.</summary>
    public const int Unknown = -1;

    /// <summary>A copy of <paramref name="locator"/> as it is now, or <c>null</c>.</summary>
    public static FixedLocator? CopyOf(ILocator? locator) =>
        locator is null ? null : new FixedLocator(locator.SystemId, locator.LineNumber, locator.ColumnNumber);
}

/// <summary>The attributes of a start-tag, without the namespace declarations (SAX's <c>Attributes</c>).</summary>
public interface IAttributes
{
    /// <summary>The number of attributes.</summary>
    int Length { get; }

    /// <summary>The namespace URI of attribute <paramref name="index"/> (empty for none).</summary>
    string GetUri(int index);

    /// <summary>The local name of attribute <paramref name="index"/>.</summary>
    string GetLocalName(int index);

    /// <summary>The qualified name of attribute <paramref name="index"/> as written.</summary>
    string GetQName(int index);

    /// <summary>The (normalized) value of attribute <paramref name="index"/>.</summary>
    string GetValue(int index);
}

/// <summary>Helpers of <see cref="IAttributes"/>.</summary>
public static class AttributesExtensions
{
    /// <summary>The value of the attribute <paramref name="localName"/> in the namespace <paramref name="uri"/> (empty
    /// for none), or <c>null</c> (SAX's <c>Attributes.getValue(uri, localName)</c>).</summary>
    public static string? GetValue(this IAttributes attributes, string uri, string localName)
    {
        ArgumentNullException.ThrowIfNull(attributes);
        for (int i = 0; i < attributes.Length; i++)
        {
            if (attributes.GetLocalName(i) == localName && attributes.GetUri(i) == uri)
            {
                return attributes.GetValue(i);
            }
        }

        return null;
    }
}

/// <summary>A list of attributes.</summary>
public sealed class AttributeList : IAttributes
{
    private readonly List<(string Uri, string LocalName, string QName, string Value)> _attributes = [];

    /// <inheritdoc />
    public int Length => _attributes.Count;

    /// <summary>Adds an attribute.</summary>
    public void Add(string uri, string localName, string qName, string value) => _attributes.Add((uri, localName, qName, value));

    /// <summary>Removes all the attributes.</summary>
    public void Clear() => _attributes.Clear();

    /// <summary>A copy of <paramref name="attributes"/>.</summary>
    public static AttributeList CopyOf(IAttributes attributes)
    {
        ArgumentNullException.ThrowIfNull(attributes);
        AttributeList copy = new();
        for (int i = 0; i < attributes.Length; i++)
        {
            copy.Add(attributes.GetUri(i), attributes.GetLocalName(i), attributes.GetQName(i), attributes.GetValue(i));
        }

        return copy;
    }

    /// <summary>Removes attribute <paramref name="index"/>.</summary>
    public void RemoveAt(int index) => _attributes.RemoveAt(index);

    /// <summary>Replaces the value of attribute <paramref name="index"/>.</summary>
    public void SetValue(int index, string value) => _attributes[index] = _attributes[index] with { Value = value };

    /// <inheritdoc />
    public string GetUri(int index) => _attributes[index].Uri;

    /// <inheritdoc />
    public string GetLocalName(int index) => _attributes[index].LocalName;

    /// <inheritdoc />
    public string GetQName(int index) => _attributes[index].QName;

    /// <inheritdoc />
    public string GetValue(int index) => _attributes[index].Value;
}

/// <summary>Receives the content of a document (SAX's <c>ContentHandler</c>).</summary>
public interface IContentHandler
{
    /// <summary>Gives the locator that tells where the following events are.</summary>
    void SetDocumentLocator(ILocator locator);

    /// <summary>The document starts.</summary>
    void StartDocument();

    /// <summary>The document ends.</summary>
    void EndDocument();

    /// <summary>A namespace declaration comes into scope (before the start-tag that declares it). The URI is empty when the declaration undeclares the default namespace.</summary>
    void StartPrefixMapping(string prefix, string uri);

    /// <summary>A namespace declaration goes out of scope (after the end-tag).</summary>
    void EndPrefixMapping(string prefix);

    /// <summary>A start-tag.</summary>
    void StartElement(string namespaceUri, string localName, string qName, IAttributes attributes);

    /// <summary>An end-tag (also sent for an empty-element tag).</summary>
    void EndElement(string namespaceUri, string localName, string qName);

    /// <summary>Character data (one piece of it: text may come in several calls).</summary>
    void Characters(string text);

    /// <summary>A processing instruction.</summary>
    void ProcessingInstruction(string target, string data);
}

/// <summary>Receives the notations and unparsed entities a DTD declares (SAX's <c>DTDHandler</c>).</summary>
public interface IDtdHandler
{
    /// <summary>A notation declaration.</summary>
    void NotationDecl(string name, string? publicId, string? systemId);

    /// <summary>An unparsed entity declaration.</summary>
    void UnparsedEntityDecl(string name, string? publicId, string? systemId, string notationName);
}

/// <summary>Receives the problems found while reading or validating (SAX's <c>ErrorHandler</c>). A handler may throw to stop.</summary>
public interface ISaxErrorHandler
{
    /// <summary>A warning.</summary>
    void OnWarning(SaxParseException exception);

    /// <summary>An error the reader recovers from.</summary>
    void OnError(SaxParseException exception);

    /// <summary>An error the reader does not recover from (the reader throws it after this call).</summary>
    void OnFatalError(SaxParseException exception);
}

/// <summary>A problem found while reading or validating (SAX's <c>SAXException</c>).</summary>
public class SaxException : Exception
{
    /// <summary>Creates the exception.</summary>
    public SaxException()
    {
    }

    /// <summary>Creates the exception.</summary>
    public SaxException(string message)
        : base(message)
    {
    }

    /// <summary>Creates the exception.</summary>
    public SaxException(string message, Exception? innerException)
        : base(message, innerException)
    {
    }
}

/// <summary>A problem at a place in a document (SAX's <c>SAXParseException</c>).</summary>
public class SaxParseException : SaxException
{
    /// <summary>Creates the exception.</summary>
    public SaxParseException()
    {
        LineNumber = FixedLocator.Unknown;
        ColumnNumber = FixedLocator.Unknown;
    }

    /// <summary>Creates the exception.</summary>
    public SaxParseException(string message)
        : this(message, (ILocator?)null)
    {
    }

    /// <summary>Creates the exception.</summary>
    public SaxParseException(string message, Exception innerException)
        : base(message, innerException)
    {
        LineNumber = FixedLocator.Unknown;
        ColumnNumber = FixedLocator.Unknown;
    }

    /// <summary>Creates the exception at the place <paramref name="locator"/> is now (anywhere when it is <c>null</c>).</summary>
    public SaxParseException(string message, ILocator? locator)
        : base(message)
    {
        SystemId = locator?.SystemId;
        LineNumber = locator?.LineNumber ?? FixedLocator.Unknown;
        ColumnNumber = locator?.ColumnNumber ?? FixedLocator.Unknown;
    }

    /// <summary>Creates the exception at a given place.</summary>
    public SaxParseException(string message, string? systemId, int lineNumber, int columnNumber, Exception? innerException)
        : base(message, innerException)
    {
        SystemId = systemId;
        LineNumber = lineNumber;
        ColumnNumber = columnNumber;
    }

    /// <summary>The URI of the document, or <c>null</c>.</summary>
    public string? SystemId { get; }

    /// <summary>The line (from 1), or -1.</summary>
    public int LineNumber { get; }

    /// <summary>The column (from 1), or -1.</summary>
    public int ColumnNumber { get; }
}

/// <summary>Where a document comes from (SAX's <c>InputSource</c>): its URI and a way to open it.</summary>
public sealed class InputSource
{
    private readonly Func<Stream> _open;

    /// <summary>A document that <paramref name="open"/> opens (a new stream each call); <paramref name="systemId"/> is its URI.</summary>
    public InputSource(string? systemId, Func<Stream> open)
    {
        SystemId = systemId;
        _open = open ?? throw new ArgumentNullException(nameof(open));
    }

    /// <summary>The URI of the document (the base of its relative references), or <c>null</c>.</summary>
    public string? SystemId { get; }

    /// <summary>A document held as text (encoded as UTF-8 for the reader).</summary>
    public static InputSource FromString(string? systemId, string text)
    {
        ArgumentNullException.ThrowIfNull(text);
        byte[] bytes = Encoding.UTF8.GetBytes(text);
        return new InputSource(systemId, () => new MemoryStream(bytes, writable: false));
    }

    /// <summary>Opens the document.</summary>
    public Stream Open() => _open();
}
