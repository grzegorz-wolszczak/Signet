using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Xml.Linq;
using Signet.Core.Misc;

namespace Signet.Core.BookManipulation;

/// <summary>
/// Formatting options for <see cref="CleanSource.PrettyPrint(string, bool, string, PrettyPrintOptions?, IReadOnlyDictionary{char, string}?)"/> — the configurable fields
/// of <c>prettyprint.xml</c> (<c>indent_string</c>, <c>singlespace</c>). The tag sets are not
/// configurable through this type (see <see cref="PrettyPrintProps"/>).
/// </summary>
/// <param name="IndentString">The indent string of one level (two spaces by default).</param>
/// <param name="SingleSpace">
/// When <c>true</c>, blocks are not separated by a blank line (like <c>&lt;singlespace&gt;true&lt;/singlespace&gt;</c>).
/// </param>
public sealed record PrettyPrintOptions(string IndentString = "  ", bool SingleSpace = false)
{
    /// <summary>The default options: a two-space indent, without <c>singlespace</c> mode.</summary>
    public static PrettyPrintOptions Default { get; } = new();
}

/// <summary>
/// The tag classification that drives formatting. The default sets are built in
/// (the <c>DEFAULTXML</c> defaults). Overrides are loaded from the <c>prettyprint.xml</c> file in the
/// user's prefs folder (<see cref="LoadUserPrefs"/>): a missing file is created with the default
/// content, and each of the six tag groups (<c>structural_tags</c>, <c>inline_tags</c>, <c>void_tags</c>,
/// <c>preservespace_tags</c>, <c>noentitysub_tags</c>, <c>textholder_tags</c>) plus
/// <c>indent_string</c>/<c>singlespace</c> can be overridden independently.
/// </summary>
/// <remarks>
/// The file is parsed with <see cref="XDocument"/> (it must be valid XML).
/// </remarks>
internal sealed class PrettyPrintProps
{
    // ---- Default tag sets ----

    private static readonly HashSet<string> StructuralTags = new(StringComparer.Ordinal)
    {
        "annotation", "annotation-xml", "article", "aside", "blockquote",
        "body", "canvas", "colgroup", "div", "dl", "figure", "footer", "head", "header",
        "hr", "html", "maction", "math", "menclose", "mfrac", "mmultiscripts", "mover",
        "mpadded", "mphantom", "mroot", "mrow", "msqrt", "mstyle", "mtable", "mtd", "mtr",
        "munder", "munderover", "nav", "ol", "section", "semantics", "table", "tbody",
        "tfoot", "thead", "td", "th", "tr", "ul",
    };

    private static readonly HashSet<string> InlineTags = new(StringComparer.Ordinal)
    {
        "a", "abbr", "acronym", "b", "bdo", "big", "br", "button", "cite", "code", "del", "dfn", "em",
        "font", "i", "image", "img", "input", "ins", "kbd", "label", "map", "mark", "mbp:nu", "mi",
        "mn", "mo", "ms", "mspace", "mtext", "msub", "msup", "msubsup", "nobr", "object", "q",
        "ruby", "rp", "rt", "s", "samp", "select", "small", "span", "strike", "strong", "sub",
        "sup", "textarea", "tt", "u", "var", "wbr",
    };

    private static readonly HashSet<string> VoidTags = new(StringComparer.Ordinal)
    {
        "area", "base", "basefont", "bgsound", "br", "col", "command", "embed", "event-source",
        "frame", "hr", "img", "input", "keygen", "link", "maligngroup", "malignmark",
        "mbp:pagebreak", "meta", "mglyph", "mprescripts", "msline", "mspace", "none",
        "param", "source", "spacer", "track", "wbr",
    };

    private static readonly HashSet<string> PreserveSpaceTags = new(StringComparer.Ordinal)
    {
        "code", "cs", "pre", "textarea", "script", "style",
    };

    private static readonly HashSet<string> NoEntitySubTags = new(StringComparer.Ordinal)
    {
        "script", "style",
    };

    private static readonly HashSet<string> TextHolderTags = new(StringComparer.Ordinal)
    {
        "address", "caption", "dd", "div", "dt", "figcaption", "h1", "h2", "h3", "h4", "h5", "h6",
        "legend", "li", "option", "p", "td", "th", "title",
    };

    private readonly HashSet<string> _structural;
    private readonly HashSet<string> _inline;
    private readonly HashSet<string> _void;
    private readonly HashSet<string> _preserveSpace;
    private readonly HashSet<string> _noEntitySub;
    private readonly HashSet<string> _textHolder;

    public PrettyPrintProps(
        string indentString = "  ",
        bool singleSpace = false,
        IEnumerable<string>? structuralTags = null,
        IEnumerable<string>? inlineTags = null,
        IEnumerable<string>? voidTags = null,
        IEnumerable<string>? preserveSpaceTags = null,
        IEnumerable<string>? noEntitySubTags = null,
        IEnumerable<string>? textHolderTags = null)
    {
        ArgumentException.ThrowIfNullOrEmpty(indentString);
        IndentString = indentString;
        SingleSpace = singleSpace;
        _structural = new HashSet<string>(structuralTags ?? StructuralTags, StringComparer.Ordinal);
        _inline = new HashSet<string>(inlineTags ?? InlineTags, StringComparer.Ordinal);
        _void = new HashSet<string>(voidTags ?? VoidTags, StringComparer.Ordinal);
        _preserveSpace = new HashSet<string>(preserveSpaceTags ?? PreserveSpaceTags, StringComparer.Ordinal);
        _noEntitySub = new HashSet<string>(noEntitySubTags ?? NoEntitySubTags, StringComparer.Ordinal);
        _textHolder = new HashSet<string>(textHolderTags ?? TextHolderTags, StringComparer.Ordinal);
    }

    /// <summary>The default instance: a two-space indent, <c>singlespace = false</c>, the built-in tag sets.</summary>
    public static PrettyPrintProps Default { get; } = new();

    /// <summary>The configuration file name in the prefs folder (<see cref="AppDirectories.PrefsDirectory"/>).</summary>
    public const string UserFileName = "prettyprint.xml";

    /// <summary>
    /// Loads the configuration from <c>prettyprint.xml</c> in the user's prefs folder.
    /// When the file does not exist, it is created with the default content and
    /// <see cref="Default"/> is returned. A read/parse error does not interrupt processing — the
    /// exception is swallowed here and <see cref="Default"/> is returned, so that a corrupted user
    /// file does not block Reformat/Prettify.
    /// </summary>
    public static PrettyPrintProps LoadUserPrefs()
    {
        string path = Path.Combine(AppDirectories.PrefsDirectory, UserFileName);
        return LoadFromFile(path);
    }

    /// <summary>Like <see cref="LoadUserPrefs"/>, but for the given path (for tests).</summary>
    public static PrettyPrintProps LoadFromFile(string path)
    {
        try
        {
            if (!File.Exists(path))
            {
                string? dir = Path.GetDirectoryName(path);
                if (!string.IsNullOrEmpty(dir))
                {
                    Directory.CreateDirectory(dir);
                }

                File.WriteAllText(path, BuildDefaultXml(), new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
                return Default;
            }

            string xml = File.ReadAllText(path);
            return string.IsNullOrWhiteSpace(xml) ? Default : ParseXml(xml);
        }
        catch (Exception)
        {
            return Default;
        }
    }

    private static PrettyPrintProps ParseXml(string xml)
    {
        XDocument doc = XDocument.Parse(xml, LoadOptions.None);
        XElement? root = doc.Root;
        if (root is null || root.Name.LocalName != "prettyprint")
        {
            return Default;
        }

        string indentRaw = GetElementText(root, "indent_string") ?? "\"  \"";
        string indent = TrimQuotes(indentRaw);
        if (indent.Length == 0)
        {
            indent = "  ";
        }

        bool singleSpace = ParseBool(GetElementText(root, "singlespace"));

        return new PrettyPrintProps(
            indent,
            singleSpace,
            ParseTagSet(root, "structural_tags", StructuralTags),
            ParseTagSet(root, "inline_tags", InlineTags),
            ParseTagSet(root, "void_tags", VoidTags),
            ParseTagSet(root, "preservespace_tags", PreserveSpaceTags),
            ParseTagSet(root, "noentitysub_tags", NoEntitySubTags),
            ParseTagSet(root, "textholder_tags", TextHolderTags));
    }

    private static string? GetElementText(XElement root, string name) => root.Element(name)?.Value;

    private static IEnumerable<string> ParseTagSet(XElement root, string elementName, HashSet<string> fallback)
    {
        string? text = GetElementText(root, elementName);
        if (text is null)
        {
            return fallback;
        }

        string[] parts = text.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        return parts.Length == 0 ? fallback : parts;
    }

    private static bool ParseBool(string? text) =>
        text is not null && (string.Equals(text.Trim(), "true", StringComparison.OrdinalIgnoreCase)
            || string.Equals(text.Trim(), "on", StringComparison.OrdinalIgnoreCase)
            || text.Trim() == "1"
            || string.Equals(text.Trim(), "yes", StringComparison.OrdinalIgnoreCase));

    private static string TrimQuotes(string value)
    {
        string trimmed = value.Trim();
        if (trimmed.Length >= 2
            && ((trimmed[0] == '"' && trimmed[^1] == '"') || (trimmed[0] == '\'' && trimmed[^1] == '\'')))
        {
            return trimmed[1..^1];
        }

        return trimmed;
    }

    private static string BuildDefaultXml()
    {
        static string Join(IEnumerable<string> tags) => string.Join(", ", tags);

        return "<?xml version=\"1.0\" encoding=\"utf-8\"?>\n"
            + "<prettyprint>\n"
            + "  <!-- actual string added in front to indent one level, typically 2 or 4 blanks, quotes needed -->\n"
            + "  <indent_string>\"  \"</indent_string>\n"
            + "  <singlespace>false</singlespace>\n"
            + "  <!-- set membership of tags determine when and where whitespace is compressed, and newlines added -->\n"
            + "  <!-- Structural tags open and close on their own lines with contents properly indented -->\n"
            + $"  <structural_tags>\n      {Join(StructuralTags)}\n  </structural_tags>\n"
            + "  <!-- Inline tags have no added space nor newlines -->\n"
            + $"  <inline_tags>\n      {Join(InlineTags)}\n  </inline_tags>\n"
            + "  <!-- Void tags are tags that can not have contents and therefore must self-close -->\n"
            + $"  <void_tags>\n      {Join(VoidTags)}\n  </void_tags>\n"
            + "  <!-- Preserve Space tags are tags whose content's whitespace can not be changed or compressed -->\n"
            + $"  <preservespace_tags>\n      {Join(PreserveSpaceTags)}\n  </preservespace_tags>\n"
            + "  <!-- No Entity Substitution tags are those whose contents should not have any entities expanded -->\n"
            + $"  <noentitysub_tags>\n      {Join(NoEntitySubTags)}\n  </noentitysub_tags>\n"
            + "  <!-- Text holder tags are tags that typically have text content -->\n"
            + $"  <textholder_tags>\n      {Join(TextHolderTags)}\n  </textholder_tags>\n"
            + "</prettyprint>\n";
    }

    public string IndentString { get; }

    public bool SingleSpace { get; }

    public bool IsStructural(string tag) => _structural.Contains(tag);

    public bool IsInline(string tag) => _inline.Contains(tag);

    public bool IsVoid(string tag) => _void.Contains(tag);

    public bool IsPreserveSpace(string tag) => _preserveSpace.Contains(tag);

    public bool IsNoEntitySub(string tag) => _noEntitySub.Contains(tag);

    public bool IsTextHolder(string tag) => _textHolder.Contains(tag);
}
