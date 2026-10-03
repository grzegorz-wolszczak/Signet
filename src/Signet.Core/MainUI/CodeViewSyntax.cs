using System;
using Signet.Core.Resources;
using SysPath = System.IO.Path;

namespace Signet.Core.MainUI;

/// <summary>
/// The kind of syntax highlighted in the code editor (Code View) — selects the highlighter
/// (XHTML / CSS / none). The UI layer maps this to a TextMate grammar.
/// </summary>
public enum CodeViewSyntax
{
    /// <summary>Plain text without highlighting.</summary>
    PlainText,

    /// <summary>An (X)HTML document.</summary>
    Html,

    /// <summary>Any XML (OPF, NCX, SVG, other).</summary>
    Xml,

    /// <summary>A CSS stylesheet.</summary>
    Css,

    /// <summary>JavaScript code.</summary>
    JavaScript,

    /// <summary>JSON data.</summary>
    Json,
}

/// <summary>
/// Selecting the <see cref="CodeViewSyntax"/> for a resource or file extension and translating it
/// into a TextMate grammar language identifier.
/// </summary>
public static class CodeViewSyntaxMap
{
    /// <summary>The syntax kind appropriate for the given text resource.</summary>
    public static CodeViewSyntax ForResource(Resource resource)
    {
        ArgumentNullException.ThrowIfNull(resource);
        return resource.Type switch
        {
            ResourceType.Html => CodeViewSyntax.Html,
            ResourceType.Css => CodeViewSyntax.Css,
            ResourceType.Svg => CodeViewSyntax.Xml,
            ResourceType.Opf => CodeViewSyntax.Xml,
            ResourceType.Ncx => CodeViewSyntax.Xml,
            ResourceType.Xml => CodeViewSyntax.Xml,
            ResourceType.Text => CodeViewSyntax.PlainText,
            _ => ForExtension(resource.BookPath),
        };
    }

    /// <summary>
    /// The syntax kind inferred from the file extension (for <c>Misc</c> resources: JS, JSON,
    /// and also as a fallback).
    /// </summary>
    public static CodeViewSyntax ForExtension(string pathOrExtension)
    {
        ArgumentNullException.ThrowIfNull(pathOrExtension);
        string ext = pathOrExtension.StartsWith('.')
            ? pathOrExtension.ToLowerInvariant()
            : SysPath.GetExtension(pathOrExtension).ToLowerInvariant();

        return ext switch
        {
            ".xhtml" or ".html" or ".htm" => CodeViewSyntax.Html,
            ".css" => CodeViewSyntax.Css,
            ".js" or ".mjs" => CodeViewSyntax.JavaScript,
            ".json" => CodeViewSyntax.Json,
            ".xml" or ".opf" or ".ncx" or ".svg" or ".smil" => CodeViewSyntax.Xml,
            _ => CodeViewSyntax.PlainText,
        };
    }

    /// <summary>
    /// The TextMate grammar language identifier (<c>html</c>, <c>xml</c>, <c>css</c>,
    /// <c>javascript</c>, <c>json</c>) or <c>null</c> for plain text.
    /// </summary>
    public static string? ToTextMateLanguageId(CodeViewSyntax syntax) => syntax switch
    {
        CodeViewSyntax.Html => "html",
        CodeViewSyntax.Xml => "xml",
        CodeViewSyntax.Css => "css",
        CodeViewSyntax.JavaScript => "javascript",
        CodeViewSyntax.Json => "json",
        _ => null,
    };

    /// <summary>
    /// Whether structural validity checking makes sense for this syntax. For <see cref="CodeViewSyntax.Html"/>
    /// and <see cref="CodeViewSyntax.Xml"/> that is well-formed XML; for <see cref="CodeViewSyntax.Css"/> —
    /// a check of the structure of <c>{ }</c> blocks by the CSS structure scanner.
    /// </summary>
    public static bool SupportsWellFormedCheck(CodeViewSyntax syntax) =>
        syntax is CodeViewSyntax.Html or CodeViewSyntax.Xml or CodeViewSyntax.Css;
}
