using System;
using System.Collections.Generic;
using System.Xml;

namespace Signet.EpubCheck.Core.Xml;

/// <summary>What EPUBCheck knows of HTML's names (upstream's <c>HTMLUtils</c>).</summary>
public static class HTMLUtils
{
    private const string DataPrefix = "data-";

    private static readonly HashSet<string> s_knownXhtmlNamespaces = new(StringComparer.Ordinal)
    {
        Namespaces.Xhtml, Namespaces.Xml, Namespaces.Ops, Namespaces.Svg, Namespaces.MathMl, Namespaces.Ssml,
        Namespaces.XmlEvents, Namespaces.XLink,
    };

    private static readonly HashSet<string> s_caseInsensitiveAttributes = new(StringComparer.Ordinal)
    {
        "align", "allowfullscreen", "allowpaymentrequest", "allowusermedia", "async", "autocapitalize", "autocomplete",
        "autofocus", "autoplay", "checked", "contenteditable", "controls", "crossorigin", "default", "defer", "dir",
        "disabled", "draggable", "formnovalidate", "hidden", "http-equiv", "ismap", "itemscope", "kind", "loop", "multiple",
        "muted", "nomodule", "novalidate", "open", "playsinline", "preload", "readonly", "required", "reversed", "scope",
        "selected", "shape", "sizes", "spellcheck", "step", "translate", "type", "typemustmatch", "valign", "value", "wrap",
    };

    /// <summary>Whether the element is an HTML custom element (its name has a hyphen).</summary>
    public static bool IsCustomElement(string? namespaceUri, string name)
    {
        ArgumentNullException.ThrowIfNull(name);
        return Namespaces.Xhtml == namespaceUri && name.Contains('-', StringComparison.Ordinal);
    }

    /// <summary>Whether the namespace is not one of those XHTML content documents may use.</summary>
    public static bool IsCustomNamespace(string? namespaceUri) =>
        !(string.IsNullOrWhiteSpace(namespaceUri) || s_knownXhtmlNamespaces.Contains(namespaceUri.Trim()));

    /// <summary>Whether the attribute is one of HTML's whose value is case-insensitive.</summary>
    public static bool IsCaseInsensitiveAttribute(string namespaceUri, string name) =>
        namespaceUri.Length == 0 && s_caseInsensitiveAttributes.Contains(name);

    /// <summary>Whether the attribute is a custom data attribute (<c>data-*</c>).</summary>
    public static bool IsDataAttribute(string namespaceUri, string name) =>
        namespaceUri.Length == 0 && name.StartsWith(DataPrefix, StringComparison.Ordinal);

    /// <summary>Whether the name of a <c>data-*</c> attribute is valid: an NCName after the prefix, no upper-case ASCII letter.</summary>
    public static bool IsValidDataAttribute(string name)
    {
        if (name is null || !name.StartsWith(DataPrefix, StringComparison.Ordinal))
        {
            throw new ArgumentException("Not a data attribute: " + name, nameof(name));
        }

        string rest = name[DataPrefix.Length..];
        return rest.Length > 0 && IsNcName(rest) && !rest.AsSpan().ContainsAnyInRange('A', 'Z');
    }

    private static bool IsNcName(string name)
    {
        try
        {
            XmlConvert.VerifyNCName(name);
            return true;
        }
        catch (XmlException)
        {
            return false;
        }
    }
}

/// <summary>The namespaces EPUBCheck names (upstream's <c>com.adobe.epubcheck.xml.Namespaces</c>).</summary>
public static class Namespaces
{
    /// <summary>XHTML.</summary>
    public const string Xhtml = "http://www.w3.org/1999/xhtml";

    /// <summary>The <c>xml:</c> prefix.</summary>
    public const string Xml = "http://www.w3.org/XML/1998/namespace";

    /// <summary>EPUB's <c>epub:</c> prefix (OPS).</summary>
    public const string Ops = "http://www.idpf.org/2007/ops";

    /// <summary>SVG.</summary>
    public const string Svg = "http://www.w3.org/2000/svg";

    /// <summary>MathML.</summary>
    public const string MathMl = "http://www.w3.org/1998/Math/MathML";

    /// <summary>SSML.</summary>
    public const string Ssml = "http://www.w3.org/2001/10/synthesis";

    /// <summary>XML Events.</summary>
    public const string XmlEvents = "http://www.w3.org/2001/xml-events";

    /// <summary>XLink.</summary>
    public const string XLink = "http://www.w3.org/1999/xlink";

    /// <summary>The OCF container file.</summary>
    public const string OcfContainer = "urn:oasis:names:tc:opendocument:xmlns:container";

    /// <summary>The custom elements of the Nu HTML Checker's schemas.</summary>
    public const string CustomElements = "http://n.validator.nu/custom-elements/";
}
