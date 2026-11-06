using System;

namespace Signet.EpubCheck.Core.Jing;

/// <summary>An expanded XML name: a namespace URI (empty for none) and a local name (Jing's <c>Name</c>).</summary>
public sealed class Name : IEquatable<Name>
{
    /// <summary>Creates the name.</summary>
    public Name(string namespaceUri, string localName)
    {
        NamespaceUri = namespaceUri ?? throw new ArgumentNullException(nameof(namespaceUri));
        LocalName = localName ?? throw new ArgumentNullException(nameof(localName));
    }

    /// <summary>The namespace URI (empty when the name has none).</summary>
    public string NamespaceUri { get; }

    /// <summary>The local name.</summary>
    public string LocalName { get; }

    /// <summary>Orders names by namespace URI, then local name (ordinal, as Java's <c>String.compareTo</c>).</summary>
    public static int Compare(Name n1, Name n2)
    {
        ArgumentNullException.ThrowIfNull(n1);
        ArgumentNullException.ThrowIfNull(n2);
        int ret = string.CompareOrdinal(n1.NamespaceUri, n2.NamespaceUri);
        return ret != 0 ? ret : string.CompareOrdinal(n1.LocalName, n2.LocalName);
    }

    /// <inheritdoc />
    public bool Equals(Name? other) =>
        other is not null
        && string.Equals(NamespaceUri, other.NamespaceUri, StringComparison.Ordinal)
        && string.Equals(LocalName, other.LocalName, StringComparison.Ordinal);

    /// <inheritdoc />
    public override bool Equals(object? obj) => Equals(obj as Name);

    /// <inheritdoc />
    public override int GetHashCode() =>
        StringComparer.Ordinal.GetHashCode(NamespaceUri) ^ StringComparer.Ordinal.GetHashCode(LocalName);

    /// <inheritdoc />
    public override string ToString() => NamespaceUri.Length == 0 ? LocalName : "{" + NamespaceUri + "}" + LocalName;
}

/// <summary>The namespace URIs Jing knows by name (Jing's <c>WellKnownNamespaces</c>).</summary>
public static class WellKnownNamespaces
{
    /// <summary>The namespace of the <c>xml</c> prefix.</summary>
    public const string Xml = "http://www.w3.org/XML/1998/namespace";

    /// <summary>The namespace of the namespace declarations (as Jing spells it, without the trailing slash).</summary>
    public const string Xmlns = "http://www.w3.org/2000/xmlns";

    /// <summary>The XML Schema datatype library.</summary>
    public const string XmlSchemaDatatypes = "http://www.w3.org/2001/XMLSchema-datatypes";

    /// <summary>The RELAX NG DTD compatibility datatype library.</summary>
    public const string RelaxNgCompatibilityDatatypes = "http://relaxng.org/ns/compatibility/datatypes/1.0";

    /// <summary>The RELAX NG namespace.</summary>
    public const string RelaxNg = "http://relaxng.org/ns/structure/1.0";

    /// <summary>The RELAX NG DTD compatibility annotations.</summary>
    public const string RelaxNgCompatibilityAnnotations = "http://relaxng.org/ns/compatibility/annotations/1.0";
}
