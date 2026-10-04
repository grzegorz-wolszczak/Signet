using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Net;
using System.Text;
using System.Text.RegularExpressions;
using System.Xml;

namespace Signet.Core.BookManipulation;

/// <summary>
/// Named character entities in XHTML. XML itself predefines only five (<c>amp lt gt quot apos</c>); the
/// HTML ones (<c>&amp;mdash;</c>, <c>&amp;nbsp;</c>, …) exist only in documents whose DOCTYPE refers to
/// an XHTML 1.x DTD (1.0 Strict/Transitional/Frameset, 1.1), which declares them. EPUB 3
/// (<c>&lt;!DOCTYPE html&gt;</c>) and documents without a DOCTYPE do not define any of them.
/// </summary>
public static class XhtmlEntities
{
    private static readonly Regex Xhtml1Doctype = new(
        @"<!DOCTYPE\s+html\s+PUBLIC\s+[""']-//W3C//DTD XHTML 1\.[01]",
        RegexOptions.Compiled | RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

    private static readonly HashSet<string> Predefined = new(StringComparer.Ordinal) { "amp", "lt", "gt", "quot", "apos" };

    /// <summary>
    /// The entities declared by the XHTML 1.x DTDs (the HTML 4 lat1, symbol and special sets, plus
    /// <c>apos</c>), without the five XML predefined ones.
    /// </summary>
    public static IReadOnlySet<string> DtdNames { get; } = new HashSet<string>(StringComparer.Ordinal)
    {
        // xhtml-lat1.ent (U+00A0–U+00FF)
        "nbsp", "iexcl", "cent", "pound", "curren", "yen", "brvbar", "sect", "uml", "copy", "ordf", "laquo",
        "not", "shy", "reg", "macr", "deg", "plusmn", "sup2", "sup3", "acute", "micro", "para", "middot",
        "cedil", "sup1", "ordm", "raquo", "frac14", "frac12", "frac34", "iquest", "Agrave", "Aacute", "Acirc",
        "Atilde", "Auml", "Aring", "AElig", "Ccedil", "Egrave", "Eacute", "Ecirc", "Euml", "Igrave", "Iacute",
        "Icirc", "Iuml", "ETH", "Ntilde", "Ograve", "Oacute", "Ocirc", "Otilde", "Ouml", "times", "Oslash",
        "Ugrave", "Uacute", "Ucirc", "Uuml", "Yacute", "THORN", "szlig", "agrave", "aacute", "acirc", "atilde",
        "auml", "aring", "aelig", "ccedil", "egrave", "eacute", "ecirc", "euml", "igrave", "iacute", "icirc",
        "iuml", "eth", "ntilde", "ograve", "oacute", "ocirc", "otilde", "ouml", "divide", "oslash", "ugrave",
        "uacute", "ucirc", "uuml", "yacute", "thorn", "yuml",

        // xhtml-symbol.ent
        "fnof", "Alpha", "Beta", "Gamma", "Delta", "Epsilon", "Zeta", "Eta", "Theta", "Iota", "Kappa", "Lambda",
        "Mu", "Nu", "Xi", "Omicron", "Pi", "Rho", "Sigma", "Tau", "Upsilon", "Phi", "Chi", "Psi", "Omega",
        "alpha", "beta", "gamma", "delta", "epsilon", "zeta", "eta", "theta", "iota", "kappa", "lambda", "mu",
        "nu", "xi", "omicron", "pi", "rho", "sigmaf", "sigma", "tau", "upsilon", "phi", "chi", "psi", "omega",
        "thetasym", "upsih", "piv", "bull", "hellip", "prime", "Prime", "oline", "frasl", "weierp", "image",
        "real", "trade", "alefsym", "larr", "uarr", "rarr", "darr", "harr", "crarr", "lArr", "uArr", "rArr",
        "dArr", "hArr", "forall", "part", "exist", "empty", "nabla", "isin", "notin", "ni", "prod", "sum",
        "minus", "lowast", "radic", "prop", "infin", "ang", "and", "or", "cap", "cup", "int", "there4", "sim",
        "cong", "asymp", "ne", "equiv", "le", "ge", "sub", "sup", "nsub", "sube", "supe", "oplus", "otimes",
        "perp", "sdot", "lceil", "rceil", "lfloor", "rfloor", "lang", "rang", "loz", "spades", "clubs",
        "hearts", "diams",

        // xhtml-special.ent (without the XML predefined ones)
        "OElig", "oelig", "Scaron", "scaron", "Yuml", "circ", "tilde", "ensp", "emsp", "thinsp", "zwnj", "zwj",
        "lrm", "rlm", "ndash", "mdash", "lsquo", "rsquo", "sbquo", "ldquo", "rdquo", "bdquo", "dagger",
        "Dagger", "permil", "lsaquo", "rsaquo", "euro",
    };

    // The declarations served instead of the real DTD: only the entities, so nothing is fetched from the network.
    private static readonly Lazy<byte[]> EntityDtd = new(() => Encoding.UTF8.GetBytes(string.Concat(
        DtdNames.Select(name => string.Create(
            CultureInfo.InvariantCulture,
            $"<!ENTITY {name} \"&#{char.ConvertToUtf32(WebUtility.HtmlDecode("&" + name + ";"), 0)};\">\n")))));

    /// <summary>Whether the document's DOCTYPE refers to an XHTML 1.x DTD, which declares the HTML entities.</summary>
    public static bool HasXhtml1Doctype(string documentText)
    {
        ArgumentNullException.ThrowIfNull(documentText);
        return Xhtml1Doctype.IsMatch(documentText);
    }

    /// <summary>
    /// Whether an entity reference such as <c>&amp;mdash;</c> (or a numeric one) is valid in the given document:
    /// numeric references and the five XML entities always are, the HTML ones only with an XHTML 1.x DOCTYPE.
    /// </summary>
    /// <param name="entity">The reference including <c>&amp;</c> and <c>;</c>.</param>
    /// <param name="documentDefinesHtmlEntities">Result of <see cref="HasXhtml1Doctype"/> for the target document.</param>
    public static bool IsDefined(string entity, bool documentDefinesHtmlEntities)
    {
        ArgumentNullException.ThrowIfNull(entity);
        if (entity.Length < 3 || entity[0] != '&' || entity[^1] != ';')
        {
            return false;
        }

        string name = entity[1..^1];
        if (name.StartsWith('#') || Predefined.Contains(name))
        {
            return true;
        }

        return documentDefinesHtmlEntities && DtdNames.Contains(name);
    }

    /// <summary>
    /// An <see cref="XmlResolver"/> that answers every DTD request with the XHTML 1.x entity declarations
    /// (and nothing else) — used to parse XHTML 1.x documents without network access.
    /// </summary>
    internal sealed class EntityDtdResolver : XmlResolver
    {
        public override object GetEntity(Uri absoluteUri, string? role, Type? ofObjectToReturn) =>
            new MemoryStream(EntityDtd.Value, writable: false);
    }
}
