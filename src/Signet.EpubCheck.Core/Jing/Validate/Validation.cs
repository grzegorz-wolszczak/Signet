using System;
using System.Collections.Generic;
using System.Globalization;
using Signet.EpubCheck.Core.Jing.Datatype;
using Signet.EpubCheck.Core.Jing.Parse;
using Signet.EpubCheck.Core.Jing.Sax;
using Signet.EpubCheck.Core.Jing.Validate.Nvdl;
using Signet.EpubCheck.Core.Jing.Validate.Schematron;

namespace Signet.EpubCheck.Core.Jing.Validate;

/// <summary>A schema of any language Jing knows (Jing's <c>validate.Schema</c>); its validators can run in parallel.</summary>
public interface ISchema
{
    /// <summary>A validator whose errors go to <paramref name="errorHandler"/>, worded in the language of <paramref name="culture"/>.</summary>
    IValidator CreateValidator(ISaxErrorHandler errorHandler, CultureInfo? culture = null);
}

/// <summary>Validates one document at a time (Jing's <c>validate.Validator</c>): feed it the events of the document.</summary>
public interface IValidator
{
    /// <summary>The handler that receives the content of the document.</summary>
    IContentHandler ContentHandler { get; }

    /// <summary>The handler that receives the DTD declarations of the document.</summary>
    IDtdHandler DtdHandler { get; }

    /// <summary>Makes the validator ready for another document.</summary>
    void Reset();
}

/// <summary>Helpers of <see cref="IValidator"/>.</summary>
public static class ValidatorExtensions
{
    /// <summary>Reads <paramref name="document"/> and validates it: the errors go to the validator's error handler, a
    /// well-formedness error to <paramref name="errorHandler"/> too.</summary>
    /// <exception cref="SaxParseException">The document is not well-formed.</exception>
    public static void Validate(this IValidator validator, InputSource document, ISaxErrorHandler? errorHandler)
    {
        ArgumentNullException.ThrowIfNull(validator);
        XmlEventReader reader = new() { ContentHandler = validator.ContentHandler, DtdHandler = validator.DtdHandler, ErrorHandler = errorHandler };
        reader.Parse(document);
    }
}

/// <summary>What every schema reader of a language is given (Jing's property map, the part EPUBCheck sets).</summary>
internal sealed record SchemaReadSettings(
    ISaxErrorHandler? ErrorHandler,
    ISchemaResolver Resolver,
    IDatatypeLibraryFactory? DatatypeLibraryFactory,
    CultureInfo? Culture);

/// <summary>A schema language that <see cref="AutoSchemaReader"/> recognizes by the namespace of the root element (Jing's <c>SchemaReceiverFactory</c>).</summary>
internal interface ISchemaLanguage
{
    string NamespaceUri { get; }

    /// <summary>Reads a schema; <paramref name="isAttributesSchema"/> makes it validate the attributes of an element of
    /// NVDL's instance namespace (Jing's <c>WrapProperty.ATTRIBUTE_OWNER</c>).</summary>
    /// <exception cref="IncorrectSchemaException">The schema has errors (reported).</exception>
    ISchema CreateSchema(InputSource source, SchemaReadSettings settings, bool isAttributesSchema);
}

/// <summary>
/// Reads a schema in the language its root element says (Jing's <c>AutoSchemaReader</c>): RELAX NG in the XML syntax,
/// NVDL, ISO Schematron or Schematron 1.5. Jing starts parsing and hands the events to the reader of the language once it sees the root element; the port
/// reads up to the root element, then lets the reader of the language read the whole document.
/// </summary>
public sealed class AutoSchemaReader
{
    private static readonly List<ISchemaLanguage> s_languages =
    [
        new RelaxNgLanguage(),
        new NvdlLanguage(),
        new SchematronLanguage(SchematronLanguage.IsoNamespace),
        new SchematronLanguage(SchematronLanguage.Schematron15Namespace),
    ];

    /// <summary>Where the errors of the schema go.</summary>
    public ISaxErrorHandler? ErrorHandler { get; init; }

    /// <summary>Finds the documents a schema refers to.</summary>
    public ISchemaResolver Resolver { get; init; } = BasicSchemaResolver.Instance;

    /// <summary>The datatype libraries of RELAX NG (the XML Schema datatypes when <c>null</c>).</summary>
    public IDatatypeLibraryFactory? DatatypeLibraryFactory { get; init; }

    /// <summary>The language of the messages about the schema.</summary>
    public CultureInfo? Culture { get; init; }

    /// <summary>Reads the schema of <paramref name="source"/>.</summary>
    /// <exception cref="IncorrectSchemaException">The schema has errors (reported to <see cref="ErrorHandler"/>).</exception>
    /// <exception cref="SaxParseException">A schema document is not well-formed, or its language is not known.</exception>
    public ISchema CreateSchema(InputSource source)
    {
        ArgumentNullException.ThrowIfNull(source);
        return CreateSchema(source, new SchemaReadSettings(ErrorHandler, Resolver, DatatypeLibraryFactory, Culture), isAttributesSchema: false);
    }

    internal static ISchema CreateSchema(InputSource source, SchemaReadSettings settings, bool isAttributesSchema)
    {
        (string ns, FixedLocator? locator) = RootNamespace(source, settings.ErrorHandler);
        ISchemaLanguage? language = s_languages.Find(l => string.Equals(l.NamespaceUri, ns, StringComparison.Ordinal));

        if (language is null)
        {
            JingLocalizer localizer = JingLocalizer.ForAutoSchema(settings.Culture);
            string detail = ns.Length == 0 ? localizer.Message("no_namespace") : localizer.Message("unknown_namespace", ns);
            throw new SaxParseException(detail, locator);
        }

        return language.CreateSchema(source, settings, isAttributesSchema);
    }

    private static (string Namespace, FixedLocator? Locator) RootNamespace(InputSource source, ISaxErrorHandler? errorHandler)
    {
        RootProbe probe = new();
        try
        {
            new XmlEventReader { ContentHandler = probe, ErrorHandler = errorHandler }.Parse(source);
        }
        catch (RootFoundException e)
        {
            return (e.NamespaceUri, e.Locator);
        }

        return (string.Empty, null);
    }

    private sealed class RootFoundException(string namespaceUri, FixedLocator? locator) : Exception
    {
        public string NamespaceUri { get; } = namespaceUri;

        public FixedLocator? Locator { get; } = locator;
    }

    private sealed class RootProbe : IContentHandler
    {
        private ILocator? _locator;

        public void SetDocumentLocator(ILocator locator) => _locator = locator;

        public void StartElement(string namespaceUri, string localName, string qName, IAttributes attributes) =>
            throw new RootFoundException(namespaceUri, FixedLocator.CopyOf(_locator));

        public void StartDocument()
        {
        }

        public void EndDocument()
        {
        }

        public void StartPrefixMapping(string prefix, string uri)
        {
        }

        public void EndPrefixMapping(string prefix)
        {
        }

        public void EndElement(string namespaceUri, string localName, string qName)
        {
        }

        public void Characters(string text)
        {
        }

        public void ProcessingInstruction(string target, string data)
        {
        }
    }

    private sealed class RelaxNgLanguage : ISchemaLanguage
    {
        public string NamespaceUri => WellKnownNamespaces.RelaxNg;

        public ISchema CreateSchema(InputSource source, SchemaReadSettings settings, bool isAttributesSchema) =>
            RelaxNgSchemaReader.From(settings, RelaxNgSyntax.Xml, isAttributesSchema).CreateSchema(source);
    }
}

/// <summary>Counts the errors it passes on (Jing's <c>CountingErrorHandler</c>).</summary>
internal sealed class CountingErrorHandler(ISaxErrorHandler? errorHandler) : ISaxErrorHandler
{
    public bool HadErrorOrFatalError { get; private set; }

    public void OnWarning(SaxParseException exception) => errorHandler?.OnWarning(exception);

    public void OnError(SaxParseException exception)
    {
        HadErrorOrFatalError = true;
        errorHandler?.OnError(exception);
    }

    public void OnFatalError(SaxParseException exception)
    {
        HadErrorOrFatalError = true;
        errorHandler?.OnFatalError(exception);
    }
}

/// <summary>Passes every event to a delegate, which can be set later (Jing's <c>DelegatingContentHandler</c>).</summary>
internal class DelegatingContentHandler : IContentHandler
{
    protected IContentHandler? Delegate { get; set; }

    public virtual void SetDocumentLocator(ILocator locator) => Delegate?.SetDocumentLocator(locator);

    public virtual void StartDocument() => Delegate?.StartDocument();

    public virtual void EndDocument() => Delegate?.EndDocument();

    public virtual void StartPrefixMapping(string prefix, string uri) => Delegate?.StartPrefixMapping(prefix, uri);

    public virtual void EndPrefixMapping(string prefix) => Delegate?.EndPrefixMapping(prefix);

    public virtual void StartElement(string namespaceUri, string localName, string qName, IAttributes attributes) =>
        Delegate?.StartElement(namespaceUri, localName, qName, attributes);

    public virtual void EndElement(string namespaceUri, string localName, string qName) => Delegate?.EndElement(namespaceUri, localName, qName);

    public virtual void Characters(string text) => Delegate?.Characters(text);

    public virtual void ProcessingInstruction(string target, string data) => Delegate?.ProcessingInstruction(target, data);
}
