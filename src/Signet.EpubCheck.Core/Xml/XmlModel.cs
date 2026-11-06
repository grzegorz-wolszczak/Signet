using System;
using System.Collections.Generic;

namespace Signet.EpubCheck.Core.Xml;

// The model keeps upstream's names (XMLElement, XMLAttr): the .NET style names are taken by System.Xml.

/// <summary>An attribute as <see cref="XmlHandler"/> sees it (upstream's <c>XMLAttr</c>): an attribute without a
/// prefix has no namespace (<c>null</c>), as upstream's handler makes it.</summary>
public sealed record XMLAttr(string? Namespace, string? Prefix, string Name, string Value);

/// <summary>
/// The element being read, with its parent chain (upstream's <c>XMLElement</c>): name, namespace, prefix, attributes,
/// and data a handler keeps on it while its content is read.
/// </summary>
public sealed class XMLElement
{
    private readonly XMLAttr[] _attributes;
    private readonly Dictionary<string, object?> _privateData = new(StringComparer.Ordinal);

    /// <summary>Creates the element.</summary>
    public XMLElement(string? namespaceUri, string? prefix, string name, XMLAttr[]? attributes, XMLElement? parent)
    {
        Namespace = namespaceUri;
        Prefix = prefix;
        Name = name;
        _attributes = attributes ?? [];
        Parent = parent;
    }

    /// <summary>The namespace URI (empty for none).</summary>
    public string? Namespace { get; }

    /// <summary>The prefix, or <c>null</c>.</summary>
    public string? Prefix { get; }

    /// <summary>The local name.</summary>
    public string Name { get; }

    /// <summary>The parent element, or <c>null</c> for the root.</summary>
    public XMLElement? Parent { get; }

    /// <summary>The attributes.</summary>
    public IReadOnlyList<XMLAttr> Attributes => _attributes;

    /// <summary>The value of the attribute <paramref name="name"/> in the namespace <paramref name="namespaceUri"/>
    /// (<c>null</c> for no namespace), or <c>null</c>.</summary>
    public string? GetAttributeNS(string? namespaceUri, string name)
    {
        foreach (XMLAttr attribute in _attributes)
        {
            if (attribute.Name == name && string.Equals(attribute.Namespace, namespaceUri, StringComparison.Ordinal))
            {
                return attribute.Value;
            }
        }

        return null;
    }

    /// <summary>The value of the attribute <paramref name="name"/> without a namespace, or <c>null</c>.</summary>
    public string? GetAttribute(string name) => GetAttributeNS(null, name);

    /// <summary>The data kept under <paramref name="key"/>, or <c>null</c>.</summary>
    public object? GetPrivateData(string key) => _privateData.GetValueOrDefault(key);

    /// <summary>Keeps <paramref name="value"/> under <paramref name="key"/>.</summary>
    public void SetPrivateData(string key, object? value) => _privateData[key] = value;
}
