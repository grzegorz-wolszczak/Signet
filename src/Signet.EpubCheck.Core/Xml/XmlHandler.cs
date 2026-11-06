using System;
using Signet.EpubCheck.Core.Galimatias;
using Signet.EpubCheck.Core.Jing.Sax;
using Signet.EpubCheck.Core.Messages;
using Signet.EpubCheck.Core.Opf;
using Signet.EpubCheck.Core.References;
using Signet.EpubCheck.Core.Reporting;

namespace Signet.EpubCheck.Core.Xml;

/// <summary>
/// The base of EPUBCheck's content handlers (upstream's <c>XMLHandler</c>, with <c>BaseURLHandler</c> and
/// <c>LocationHandler</c>): it keeps the current element with its ancestors, the base URL (<c>xml:base</c>, HTML's
/// <c>base</c>), the location of the current event, and checks the XML version at the root (<c>HTM-001</c>).
/// Subclasses override <see cref="StartElement()"/> and <see cref="EndElement()"/>.
/// </summary>
public abstract class XmlHandler : IContentHandler
{
    private const string XmlVersion10 = "1.0";
    private const string HtmlNamespace = "http://www.w3.org/1999/xhtml";
    private const string XmlNamespace = "http://www.w3.org/XML/1998/namespace";
    private const string BaseName = "base";
    private const string HrefName = "href";

    private readonly URLChecker _urlChecker;
    private ILocator? _locator;

    /// <summary>A handler of the resource of <paramref name="context"/>, whose URLs resolve against
    /// <paramref name="baseUrl"/> (the URL of the resource when <c>null</c>).</summary>
    protected XmlHandler(ValidationContext context, Url? baseUrl = null)
    {
        Context = context ?? throw new ArgumentNullException(nameof(context));
        BaseUrl = baseUrl ?? context.Url;
        _urlChecker = new URLChecker(context, BaseUrl);
    }

    /// <summary>The context of the check.</summary>
    protected ValidationContext Context { get; }

    /// <summary>The report of the check.</summary>
    protected IReport Report => Context.Report;

    /// <summary>The path of the checked resource.</summary>
    protected string Path => Context.Path;

    /// <summary>The base URL in force.</summary>
    protected Url BaseUrl { get; private set; }

    /// <summary>The element being read.</summary>
    protected XMLElement CurrentElement { get; private set; } = null!;

    /// <inheritdoc />
    public void SetDocumentLocator(ILocator locator) => _locator = locator;

    /// <inheritdoc />
    public virtual void StartDocument()
    {
    }

    /// <inheritdoc />
    public virtual void EndDocument()
    {
    }

    /// <inheritdoc />
    public virtual void StartPrefixMapping(string prefix, string uri)
    {
    }

    /// <inheritdoc />
    public virtual void EndPrefixMapping(string prefix)
    {
    }

    /// <inheritdoc />
    public void StartElement(string namespaceUri, string localName, string qName, IAttributes attributes)
    {
        ArgumentNullException.ThrowIfNull(attributes);
        UpdateBase(namespaceUri, localName, attributes);
        if (CurrentElement is null)
        {
            CheckXmlVersion();
        }

        int index = qName.IndexOf(':', StringComparison.Ordinal);
        string? prefix = index < 0 ? null : qName[..index];
        string name = index < 0 ? qName : qName[(index + 1)..];
        XMLAttr[] list = new XMLAttr[attributes.Length];
        for (int i = 0; i < attributes.Length; i++)
        {
            string attributeQName = attributes.GetQName(i);
            int attributeIndex = attributeQName.IndexOf(':', StringComparison.Ordinal);
            list[i] = attributeIndex < 0
                ? new XMLAttr(null, null, attributes.GetLocalName(i), attributes.GetValue(i))
                : new XMLAttr(attributes.GetUri(i), attributeQName[..attributeIndex], attributes.GetLocalName(i), attributes.GetValue(i));
        }

        CurrentElement = new XMLElement(namespaceUri, prefix, name, list, CurrentElement);
        StartElement();
    }

    /// <inheritdoc />
    public void EndElement(string namespaceUri, string localName, string qName)
    {
        EndElement();
        CurrentElement = CurrentElement.Parent!;
    }

    /// <inheritdoc />
    public virtual void Characters(string text)
    {
    }

    /// <inheritdoc />
    public virtual void ProcessingInstruction(string target, string data)
    {
    }

    /// <summary>Where the current event is.</summary>
    protected EpubLocation Location() =>
        _locator is null ? Context.Location() : Context.Location().At(_locator.LineNumber, _locator.ColumnNumber);

    /// <summary>The URL <paramref name="value"/> refers to, resolved against the base; <c>null</c> when it does not parse (reported).</summary>
    protected Url? CheckUrl(string? value) => _urlChecker.CheckUrl(value, Location());

    /// <summary>Registers a reference from the current place to <paramref name="url"/> (nothing when there is no container).</summary>
    protected void RegisterReference(Url? url, ReferenceType type, bool hasIntrinsicFallback = false)
    {
        if (url is not null)
        {
            Context.ReferenceRegistry?.RegisterReference(url, type, Location(), hasIntrinsicFallback);
        }
    }

    /// <summary>An element starts: <see cref="CurrentElement"/> is it.</summary>
    protected virtual void StartElement()
    {
    }

    /// <summary>An element ends: <see cref="CurrentElement"/> is it.</summary>
    protected virtual void EndElement()
    {
    }

    private void UpdateBase(string namespaceUri, string localName, IAttributes attributes)
    {
        string? newBase = HtmlNamespace == namespaceUri && localName == BaseName
            ? attributes.GetValue(string.Empty, HrefName)
            : attributes.GetValue(XmlNamespace, BaseName);
        if (newBase is not null)
        {
            BaseUrl = _urlChecker.SetBase(newBase, Location());
        }
    }

    private void CheckXmlVersion()
    {
        string version = (_locator as ILocator2)?.XmlVersion ?? XmlVersion10;
        if (version != XmlVersion10)
        {
            Report.Message(MessageId.HTM_001, Context.Location(), version);
        }
    }
}
