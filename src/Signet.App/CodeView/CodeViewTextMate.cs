using System;
using Avalonia.Styling;
using Signet.Core.MainUI;
using Signet.Core.Misc;
using TextMateSharp.Grammars;
using TextMateSharp.Themes;

namespace Signet.App.CodeView;

/// <summary>
/// Selects syntax highlighting for Code View. (X)HTML/XML/SVG and CSS use line-by-line
/// highlighters (<see cref="CreateColorizer"/>, colors from <c>CodeViewAppearance</c>) —
/// basic or extended; the remaining syntaxes (JavaScript, JSON) use TextMateSharp with the
/// "Light+"/"Dark+" themes.
/// </summary>
internal static class CodeViewTextMate
{
    private static readonly RegistryOptions SharedOptions = new(ThemeName.LightPlus);

    /// <summary>Registry options (grammars + themes) shared by all Code View tabs.</summary>
    public static RegistryOptions Options => SharedOptions;

    /// <summary>TextMate theme matching the application's theme variant.</summary>
    public static IRawTheme ThemeFor(ThemeVariant variant) =>
        SharedOptions.LoadTheme(variant == ThemeVariant.Dark ? ThemeName.DarkPlus : ThemeName.LightPlus);

    /// <summary>
    /// Line-by-line colorizer for the given syntax, or <c>null</c> when the syntax goes through
    /// TextMate / is not highlighted. Without extensions — the basic highlighters
    /// (<see cref="XhtmlHighlighter"/> for HTML/XML/SVG, <see cref="CssHighlighter"/> for CSS);
    /// with extensions — <see cref="ExtendedXhtmlHighlighter"/> (XML mode for OPF/NCX/SVG/XML)
    /// and <see cref="ExtendedCssHighlighter"/>.
    /// </summary>
    /// <param name="syntax">Tab syntax.</param>
    /// <param name="extended">Whether to use extended highlighting.</param>
    /// <param name="linkExists">Link target check (extended only); <c>null</c> — no checking.</param>
    /// <param name="appearance">Colors.</param>
    public static SyntaxColorizer? CreateColorizer(
        CodeViewSyntax syntax, bool extended, Func<string, bool>? linkExists, CodeViewAppearance appearance) =>
        (syntax, extended) switch
        {
            (CodeViewSyntax.Html, true) => new LineStateSyntaxColorizer<ExtendedXhtmlState>(new ExtendedXhtmlHighlighter(false, linkExists), appearance),
            (CodeViewSyntax.Xml, true) => new LineStateSyntaxColorizer<ExtendedXhtmlState>(new ExtendedXhtmlHighlighter(true, linkExists), appearance),
            (CodeViewSyntax.Css, true) => new LineStateSyntaxColorizer<int>(new ExtendedCssHighlighter(linkExists), appearance),
            (CodeViewSyntax.Html or CodeViewSyntax.Xml, false) => new LineStateSyntaxColorizer<int>(new XhtmlHighlighter(), appearance),
            (CodeViewSyntax.Css, false) => new LineStateSyntaxColorizer<int>(new CssHighlighter(), appearance),
            _ => null,
        };

    /// <summary>
    /// TextMate grammar scope for the given syntax (e.g. <c>text.html.basic</c>), or <c>null</c>
    /// when the syntax is not highlighted (plain text) or the grammar is unavailable.
    /// </summary>
    public static string? ScopeFor(CodeViewSyntax syntax)
    {
        string? languageId = CodeViewSyntaxMap.ToTextMateLanguageId(syntax);
        return languageId is null ? null : SharedOptions.GetScopeByLanguageId(languageId);
    }
}
