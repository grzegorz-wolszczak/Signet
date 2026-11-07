using System;
using System.Globalization;
using Signet.EpubCheck.Core.Jing.Sax;
using Signet.EpubCheck.Core.Jing.Validate;
using Signet.EpubCheck.Core.Messages;
using Signet.EpubCheck.Core.Opf;
using Signet.EpubCheck.Core.Util.Mime;

namespace Signet.EpubCheck.Core.Xml;

/// <summary>
/// Prepares an EPUB 3 XHTML document for the schemas and handlers behind it (upstream's
/// <c>PreprocessingDefaultHandler</c>): custom elements move to one namespace, <c>data-*</c> attributes and attributes
/// of custom namespaces are removed (reporting the invalid ones, <c>HTM-061</c> and <c>HTM-054</c>), and the HTML
/// attributes whose value is case-insensitive are lower-cased. Every other document is passed on as it is.
/// </summary>
internal sealed class PreprocessingHandler : DelegatingContentHandler
{
    private const string IdpfHost = "idpf.org";
    private const string W3cHost = "w3.org";

    private readonly ValidationContext _context;
    private ILocator? _locator;

    public PreprocessingHandler(IContentHandler handler, ValidationContext context)
    {
        Delegate = handler;
        _context = context;
    }

    public override void SetDocumentLocator(ILocator locator)
    {
        _locator = locator;
        base.SetDocumentLocator(locator);
    }

    public override void StartElement(string namespaceUri, string localName, string qName, IAttributes attributes)
    {
        base.StartElement(PreprocessNamespace(namespaceUri, localName), localName, qName, PreprocessAttributes(namespaceUri, attributes));
    }

    private bool IsXhtml => MediaTypes.Is(MediaTypes.Xhtml, _context.MimeType);

    private string PreprocessNamespace(string namespaceUri, string localName) =>
        _context.Version == EpubVersion.Version3 && IsXhtml && HTMLUtils.IsCustomElement(namespaceUri, localName)
            ? Namespaces.CustomElements
            : namespaceUri;

    private IAttributes PreprocessAttributes(string elementNamespace, IAttributes source)
    {
        if (_context.Version != EpubVersion.Version3)
        {
            return source;
        }

        // Most elements have nothing to remove or change: they go on as they are, without a copy.
        bool changes = false;
        for (int i = 0; i < source.Length && !changes; i++)
        {
            string uri = source.GetUri(i);
            string local = source.GetLocalName(i);
            changes = HTMLUtils.IsDataAttribute(uri, local)
                || (IsXhtml && HTMLUtils.IsCustomNamespace(uri))
                || (IsXhtml && Namespaces.Xhtml == elementNamespace && HTMLUtils.IsCaseInsensitiveAttribute(uri, local));
        }

        if (!changes)
        {
            return source;
        }

        AttributeList attributes = AttributeList.CopyOf(source);
        for (int i = attributes.Length - 1; i >= 0; i--)
        {
            string namespaceUri = attributes.GetUri(i);
            string name = attributes.GetLocalName(i);
            if (HTMLUtils.IsDataAttribute(namespaceUri, name))
            {
                if (!HTMLUtils.IsValidDataAttribute(name))
                {
                    _context.Report.Message(MessageId.HTM_061, Location(), name);
                }

                attributes.RemoveAt(i);
            }
            else if (IsXhtml && HTMLUtils.IsCustomNamespace(namespaceUri))
            {
                if (ReservedHost(namespaceUri) is { } reserved)
                {
                    _context.Report.Message(MessageId.HTM_054, Location(), namespaceUri, reserved);
                }

                attributes.RemoveAt(i);
            }
            else if (IsXhtml && Namespaces.Xhtml == elementNamespace && HTMLUtils.IsCaseInsensitiveAttribute(namespaceUri, name))
            {
                attributes.SetValue(i, attributes.GetValue(i).ToLower(CultureInfo.InvariantCulture));
            }
        }

        return attributes;
    }

    // The reserved host a custom namespace claims (w3.org, idpf.org), or null.
    private static string? ReservedHost(string namespaceUri)
    {
        if (!Uri.TryCreate(namespaceUri, UriKind.Absolute, out Uri? uri))
        {
            return null;
        }

        if (uri.Host.Contains(W3cHost, StringComparison.Ordinal))
        {
            return W3cHost;
        }

        return uri.Host.Contains(IdpfHost, StringComparison.Ordinal) ? IdpfHost : null;
    }

    private Reporting.EpubLocation Location() =>
        _locator is null ? _context.Location() : _context.Location().At(_locator.LineNumber, _locator.ColumnNumber);
}
