using System;
using System.Text;
using Signet.EpubCheck.Core.Jing.Sax;

namespace Signet.EpubCheck.Core.Jing.Patterns;

/// <summary>The namespaces in scope in the document validated (Jing's <c>relaxng.sax.Context</c>).</summary>
internal class ValidatorContext : DtdContext, IMatchContext
{
    private PrefixMapping _prefixMapping = InitialMapping();

    public virtual string? BaseUri => null;

    public virtual void StartPrefixMapping(string prefix, string uri) =>
        _prefixMapping = new PrefixMapping(prefix, uri.Length == 0 ? null : uri, _prefixMapping);

    /// <summary>Ends the innermost prefix mapping (SAX ends them innermost first).</summary>
    public void EndPrefixMapping() => _prefixMapping = _prefixMapping.Previous!;

    public string? ResolveNamespacePrefix(string prefix)
    {
        for (PrefixMapping? tem = _prefixMapping; tem is not null; tem = tem.Previous)
        {
            if (string.Equals(tem.Prefix, prefix, StringComparison.Ordinal))
            {
                return tem.NamespaceUri;
            }
        }

        return null;
    }

    public string? GetPrefix(string namespaceUri)
    {
        for (PrefixMapping? tem = _prefixMapping; tem is not null; tem = tem.Previous)
        {
            // The prefix must not be bound to something else further in. Jing compares with ==, which with Xerces (it
            // interns the namespace URIs) compares the values.
            if (string.Equals(namespaceUri, tem.NamespaceUri, StringComparison.Ordinal)
                && string.Equals(tem.NamespaceUri, ResolveNamespacePrefix(tem.Prefix), StringComparison.Ordinal))
            {
                return tem.Prefix;
            }
        }

        return null;
    }

    public virtual void Reset()
    {
        _prefixMapping = InitialMapping();
        ClearDtdContext();
    }

    private static PrefixMapping InitialMapping() => new("xml", WellKnownNamespaces.Xml, null);

    // A null namespace undeclares the prefix.
    private sealed record PrefixMapping(string Prefix, string? NamespaceUri, PrefixMapping? Previous);
}

/// <summary>
/// Validates the events of a document against a pattern (Jing's <c>PatternValidator</c> and <c>RngValidator</c>):
/// each error goes to the error handler at the place the locator gives.
/// </summary>
internal sealed class PatternValidator : ValidatorContext, IContentHandler
{
    private readonly ISaxErrorHandler _eh;
    private readonly StringBuilder _charBuf = new();
    private PatternMatcher _matcher;
    private bool _bufferingCharacters;
    private ILocator? _locator;

    public PatternValidator(Pattern pattern, ValidatorPatternBuilder builder, ISaxErrorHandler eh, JingLocalizer localizer)
    {
        _matcher = new PatternMatcher(pattern, builder, localizer);
        _eh = eh;
    }

    public void StartElement(string namespaceUri, string localName, string qName, IAttributes attributes)
    {
        FlushTextBeforeStartTag();
        Name name = new(namespaceUri, localName);
        Check(_matcher.MatchStartTagOpen(name, qName, this));
        for (int i = 0; i < attributes.Length; i++)
        {
            Name attName = new(attributes.GetUri(i), attributes.GetLocalName(i));
            string attQName = attributes.GetQName(i);
            Check(_matcher.MatchAttributeName(attName, attQName, this));
            Check(_matcher.MatchAttributeValue(attributes.GetValue(i), attName, attQName, this));
        }

        Check(_matcher.MatchStartTagClose(name, qName, this));
        if (_matcher.IsTextTyped)
        {
            _bufferingCharacters = true;
            _charBuf.Clear();
        }
    }

    public void EndElement(string namespaceUri, string localName, string qName)
    {
        if (_bufferingCharacters)
        {
            _bufferingCharacters = false;
            if (_charBuf.Length > 0)
            {
                Check(_matcher.MatchTextBeforeEndTag(_charBuf.ToString(), new Name(namespaceUri, localName), qName, this));
            }
        }

        Check(_matcher.MatchEndTag(new Name(namespaceUri, localName), qName, this));
    }

    public void Characters(string text)
    {
        if (_bufferingCharacters)
        {
            _charBuf.Append(text);
            return;
        }

        foreach (char c in text)
        {
            if (c is not (' ' or '\r' or '\t' or '\n'))
            {
                Check(_matcher.MatchUntypedText(this));
                return;
            }
        }
    }

    public void EndDocument() => Check(PatternMatcher.MatchEndDocument());

    public void SetDocumentLocator(ILocator locator) => _locator = locator;

    public void StartDocument() => Check(_matcher.MatchStartDocument());

    public void ProcessingInstruction(string target, string data)
    {
    }

    public override void StartPrefixMapping(string prefix, string uri)
    {
        FlushTextBeforeStartTag();
        base.StartPrefixMapping(prefix, uri);
    }

    void IContentHandler.EndPrefixMapping(string prefix) => EndPrefixMapping();

    public override void Reset()
    {
        base.Reset();
        _bufferingCharacters = false;
        _locator = null;
        _matcher = _matcher.Start();
    }

    private void FlushTextBeforeStartTag()
    {
        if (_bufferingCharacters)
        {
            _bufferingCharacters = false;
            Check(_matcher.MatchTextBeforeStartTag(_charBuf.ToString(), this));
        }
    }

    private void Check(bool ok)
    {
        if (!ok)
        {
            _eh.OnError(new SaxParseException(_matcher.ErrorMessage ?? string.Empty, _locator));
        }
    }
}
