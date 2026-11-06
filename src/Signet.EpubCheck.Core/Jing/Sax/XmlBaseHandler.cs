using System;
using System.Collections.Generic;

namespace Signet.EpubCheck.Core.Jing.Sax;

/// <summary>Follows the <c>xml:base</c> attributes of a document to give the base URI of the current element (Jing's <c>XmlBaseHandler</c>).</summary>
internal sealed class XmlBaseHandler
{
    private int _depth;
    private ILocator? _locator;
    private Entry? _stack;

    public void SetLocator(ILocator locator) => _locator = locator;

    public void StartElement() => ++_depth;

    public void EndElement()
    {
        if (_stack is not null && _stack.Depth == _depth)
        {
            _stack = _stack.Parent;
        }

        --_depth;
    }

    public void XmlBaseAttribute(string value) => _stack = new Entry(_stack, value, SystemId, _depth);

    public string? GetBaseUri() => GetBaseUri(SystemId, _stack);

    private string? SystemId => _locator?.SystemId;

    private static string? GetBaseUri(string? baseUri, Entry? stack)
    {
        if (stack is null || (baseUri is not null && !string.Equals(baseUri, stack.SystemId, StringComparison.Ordinal)))
        {
            return baseUri;
        }

        string attValue = stack.AttValue;
        if (JingUri.IsAbsolute(attValue))
        {
            return attValue;
        }

        return JingUri.Resolve(GetBaseUri(stack.SystemId, stack.Parent), attValue);
    }

    private sealed record Entry(Entry? Parent, string AttValue, string? SystemId, int Depth);
}

/// <summary>The notations and unparsed entities a DTD declared (Jing's <c>DtdContext</c>), for the datatypes that refer to them.</summary>
internal class DtdContext : IDtdHandler
{
    private readonly HashSet<string> _notations;
    private readonly HashSet<string> _unparsedEntities;

    public DtdContext()
    {
        _notations = new HashSet<string>(StringComparer.Ordinal);
        _unparsedEntities = new HashSet<string>(StringComparer.Ordinal);
    }

    public DtdContext(DtdContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        _notations = context._notations;
        _unparsedEntities = context._unparsedEntities;
    }

    public void NotationDecl(string name, string? publicId, string? systemId) => _notations.Add(name);

    public void UnparsedEntityDecl(string name, string? publicId, string? systemId, string notationName) => _unparsedEntities.Add(name);

    public bool IsNotation(string notationName) => _notations.Contains(notationName);

    public bool IsUnparsedEntity(string entityName) => _unparsedEntities.Contains(entityName);

    public void ClearDtdContext()
    {
        _notations.Clear();
        _unparsedEntities.Clear();
    }
}
