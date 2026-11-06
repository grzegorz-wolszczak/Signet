using System;
using System.Collections.Generic;
using System.IO;
using Signet.EpubCheck.Core.Jing.Sax;
using Signet.EpubCheck.Core.Jing.Validate;
using Signet.EpubCheck.Core.Messages;
using Signet.EpubCheck.Core.Opf;
using Signet.EpubCheck.Core.Reporting;
using Signet.EpubCheck.Core.Util.Mime;

namespace Signet.EpubCheck.Core.Xml;

/// <summary>
/// Reads one XML resource of a publication and gives its events to EPUBCheck's content handlers and schema validators
/// (upstream's <c>XMLParser</c>): it sniffs the encoding (<c>HTM-058</c>, <c>RSC-027</c>, <c>RSC-028</c>), reports the
/// errors of the document and of the schemas through <see cref="ReportingErrorHandler"/>, and reports a resource that
/// cannot be read (<c>PKG-008</c>). The handlers see the document after <see cref="PreprocessingHandler"/>.
/// </summary>
/// <remarks>
/// Not ported yet, because the checks that need them come later: the entities used but not declared (<c>HTM-011</c>),
/// and the resolver of the bundled DTDs of EPUB 2 (the reader reads no external DTD).
/// </remarks>
public sealed class XmlParser
{
    private const string Utf8 = "UTF-8";
    private const string Utf16 = "UTF-16";

    private readonly ValidationContext _context;
    private readonly List<IContentHandler> _contentHandlers = [];
    private readonly List<IDtdHandler> _dtdHandlers = [];
    private bool _reporting = true;

    /// <summary>A parser for the resource of <paramref name="context"/>.</summary>
    public XmlParser(ValidationContext context)
    {
        _context = context ?? throw new ArgumentNullException(nameof(context));
    }

    /// <summary>Whether the errors of the document are reported (upstream's peekers switch it off).</summary>
    public bool Reporting
    {
        get => _reporting;
        set => _reporting = value;
    }

    /// <summary>Adds a handler of the content of the document.</summary>
    public void AddContentHandler(IContentHandler handler) => _contentHandlers.Add(handler ?? throw new ArgumentNullException(nameof(handler)));

    /// <summary>Adds a validator: its errors are reported as EPUBCheck's messages.</summary>
    public void AddValidator(XmlValidator validator)
    {
        ArgumentNullException.ThrowIfNull(validator);
        IValidator created = validator.CreateValidator(_context.Report, _context.Location(), _context.Culture);
        _contentHandlers.Add(created.ContentHandler);
        _dtdHandlers.Add(created.DtdHandler);
    }

    /// <summary>Parses the resource. Problems are reported, never thrown.</summary>
    public void Process()
    {
        IReport report = _context.Report;
        try
        {
            byte[] bytes = ReadAll();
            InputSource source = Source(bytes);
            ReportingErrorHandler? errors = _reporting ? new ReportingErrorHandler(report, _context.Location()) : null;
            PreprocessingHandler handler = new(new Tee(_contentHandlers), _context);
            new XmlEventReader
            {
                ContentHandler = handler,
                DtdHandler = new DtdTee(_dtdHandlers),
                DeclarationHandler = new DeclarationHandler(_context),
                ErrorHandler = errors,
            }.Parse(source);
        }
        catch (SaxAbortException)
        {
            // Intentional parsing abort.
        }
        catch (IOException)
        {
            report.Message(MessageId.PKG_008, _context.Location(), _context.Path);
        }
        catch (SaxException e)
        {
            // All the errors are reported by the error handler, but not the ones of a parser that is not reporting.
            if (report.FatalErrorCount == 0)
            {
                report.Message(MessageId.RSC_016, _context.Location(), e.Message);
            }
        }
    }

    private byte[] ReadAll()
    {
        using Stream stream = _context.Resources.OpenStream(_context.Url);
        using MemoryStream buffer = new();
        stream.CopyTo(buffer);
        return buffer.ToArray();
    }

    private InputSource Source(byte[] bytes)
    {
        string? encoding = XMLEncodingSniffer.SniffEncoding(bytes);
        string systemId = _context.Url.ToString();
        if (encoding is not null and not Utf8)
        {
            if (encoding == Utf16)
            {
                // XHTML requires UTF-8; UTF-16 is an error there, and a warning for other XML.
                _context.Report.Message(
                    MediaTypes.Is(MediaTypes.Xhtml, _context.MimeType) ? MessageId.HTM_058 : MessageId.RSC_027,
                    _context.Location());
            }
            else
            {
                _context.Report.Message(MessageId.RSC_028, _context.Location(), encoding);
            }

            // The reader applies its own sniffing, as it reports useful errors (a BOM that contradicts the declaration).
            return new InputSource(systemId, () => new MemoryStream(bytes, writable: false));
        }

        // The UTF-8 BOM is skipped, as upstream skips it before decoding the stream itself.
        int skip = XMLEncodingSniffer.HasUtf8Bom(bytes) ? 3 : 0;
        return new InputSource(systemId, () => new MemoryStream(bytes, skip, bytes.Length - skip, writable: false));
    }

    private sealed class DtdTee(List<IDtdHandler> handlers) : IDtdHandler
    {
        public void NotationDecl(string name, string? publicId, string? systemId) => handlers.ForEach(h => h.NotationDecl(name, publicId, systemId));

        public void UnparsedEntityDecl(string name, string? publicId, string? systemId, string notationName) =>
            handlers.ForEach(h => h.UnparsedEntityDecl(name, publicId, systemId, notationName));
    }
}

/// <summary>Stops a parse on purpose (upstream's <c>SAXAbortException</c>).</summary>
public sealed class SaxAbortException : SaxException
{
    /// <summary>Creates the exception.</summary>
    public SaxAbortException()
        : base("Parsing aborted")
    {
    }
}

/// <summary>Gives each event to several content handlers in turn (upstream's <c>DelegateDefaultHandler</c>).</summary>
internal sealed class Tee(IReadOnlyList<IContentHandler> handlers) : IContentHandler
{
    public void SetDocumentLocator(ILocator locator) => Each(h => h.SetDocumentLocator(locator));

    public void StartDocument() => Each(h => h.StartDocument());

    public void EndDocument() => Each(h => h.EndDocument());

    public void StartPrefixMapping(string prefix, string uri) => Each(h => h.StartPrefixMapping(prefix, uri));

    public void EndPrefixMapping(string prefix) => Each(h => h.EndPrefixMapping(prefix));

    public void StartElement(string namespaceUri, string localName, string qName, IAttributes attributes) =>
        Each(h => h.StartElement(namespaceUri, localName, qName, attributes));

    public void EndElement(string namespaceUri, string localName, string qName) => Each(h => h.EndElement(namespaceUri, localName, qName));

    public void Characters(string text) => Each(h => h.Characters(text));

    public void ProcessingInstruction(string target, string data) => Each(h => h.ProcessingInstruction(target, data));

    private void Each(Action<IContentHandler> action)
    {
        foreach (IContentHandler handler in handlers)
        {
            action(handler);
        }
    }
}
