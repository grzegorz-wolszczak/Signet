using System;
using Signet.Core.Resources;
using SysPath = System.IO.Path;

namespace Signet.Core.MainUI;

/// <summary>
/// The kind of syntax highlighted in the code editor (Code Editor) — selects the highlighter
/// (XHTML / CSS / none). The UI layer maps this to a TextMate grammar.
/// </summary>
public enum CodeEditorSyntax
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
/// Selecting the <see cref="CodeEditorSyntax"/> for a resource or file extension and translating it
/// into a TextMate grammar language identifier.
/// </summary>
public static class CodeEditorSyntaxMap
{
    /// <summary>The syntax kind appropriate for the given text resource.</summary>
    public static CodeEditorSyntax ForResource(Resource resource)
    {
        ArgumentNullException.ThrowIfNull(resource);
        return resource.Type switch
        {
            ResourceType.Html => CodeEditorSyntax.Html,
            ResourceType.Css => CodeEditorSyntax.Css,
            ResourceType.Svg => CodeEditorSyntax.Xml,
            ResourceType.Opf => CodeEditorSyntax.Xml,
            ResourceType.Ncx => CodeEditorSyntax.Xml,
            ResourceType.Xml => CodeEditorSyntax.Xml,
            ResourceType.Text => CodeEditorSyntax.PlainText,
            _ => ForExtension(resource.BookPath),
        };
    }

    /// <summary>
    /// The syntax kind inferred from the file extension (for <c>Misc</c> resources: JS, JSON,
    /// and also as a fallback).
    /// </summary>
    public static CodeEditorSyntax ForExtension(string pathOrExtension)
    {
        ArgumentNullException.ThrowIfNull(pathOrExtension);
        string ext = pathOrExtension.StartsWith('.')
            ? pathOrExtension.ToLowerInvariant()
            : SysPath.GetExtension(pathOrExtension).ToLowerInvariant();

        return ext switch
        {
            ".xhtml" or ".html" or ".htm" => CodeEditorSyntax.Html,
            ".css" => CodeEditorSyntax.Css,
            ".js" or ".mjs" => CodeEditorSyntax.JavaScript,
            ".json" => CodeEditorSyntax.Json,
            ".xml" or ".opf" or ".ncx" or ".svg" or ".smil" => CodeEditorSyntax.Xml,
            _ => CodeEditorSyntax.PlainText,
        };
    }

    /// <summary>
    /// The TextMate grammar language identifier (<c>html</c>, <c>xml</c>, <c>css</c>,
    /// <c>javascript</c>, <c>json</c>) or <c>null</c> for plain text.
    /// </summary>
    public static string? ToTextMateLanguageId(CodeEditorSyntax syntax) => syntax switch
    {
        CodeEditorSyntax.Html => "html",
        CodeEditorSyntax.Xml => "xml",
        CodeEditorSyntax.Css => "css",
        CodeEditorSyntax.JavaScript => "javascript",
        CodeEditorSyntax.Json => "json",
        _ => null,
    };

    /// <summary>
    /// Whether structural validity checking makes sense for this syntax. For <see cref="CodeEditorSyntax.Html"/>
    /// and <see cref="CodeEditorSyntax.Xml"/> that is well-formed XML; for <see cref="CodeEditorSyntax.Css"/> —
    /// a check of the structure of <c>{ }</c> blocks by the CSS structure scanner.
    /// </summary>
    public static bool SupportsWellFormedCheck(CodeEditorSyntax syntax) =>
        syntax is CodeEditorSyntax.Html or CodeEditorSyntax.Xml or CodeEditorSyntax.Css;
}
