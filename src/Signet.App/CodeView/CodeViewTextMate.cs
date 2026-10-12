using System;
using Signet.App.Infrastructure;
using Signet.Core.MainUI;
using Signet.Core.Misc;
using TextMateSharp.Grammars;
using TextMateSharp.Themes;

namespace Signet.App.CodeView;

/// <summary>
/// Selects syntax highlighting for Code View. (X)HTML/XML/SVG and CSS use line-by-line
/// highlighters (<see cref="CreateColorizer"/>, colors from the theme) —
/// basic or extended; the remaining syntaxes (JavaScript, JSON) use TextMateSharp with the
/// "Light+" theme.
/// </summary>
internal static class CodeViewTextMate
{
    private static readonly RegistryOptions SharedOptions = new(ThemeName.LightPlus);

    /// <summary>Registry options (grammars + themes) shared by all Code View tabs.</summary>
    public static RegistryOptions Options => SharedOptions;

    /// <summary>The TextMate theme of Code View.</summary>
    public static IRawTheme Theme => SharedOptions.LoadTheme(ThemeName.LightPlus);

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
    /// <param name="theme">The colors.</param>
    public static SyntaxColorizer? CreateColorizer(
        CodeViewSyntax syntax, bool extended, Func<string, bool>? linkExists, ResolvedTheme theme) =>
        (syntax, extended) switch
        {
            (CodeViewSyntax.Html, true) => new LineStateSyntaxColorizer<ExtendedXhtmlState>(new ExtendedXhtmlHighlighter(false, linkExists), theme),
            (CodeViewSyntax.Xml, true) => new LineStateSyntaxColorizer<ExtendedXhtmlState>(new ExtendedXhtmlHighlighter(true, linkExists), theme),
            (CodeViewSyntax.Css, true) => new LineStateSyntaxColorizer<int>(new ExtendedCssHighlighter(linkExists), theme),
            (CodeViewSyntax.Html or CodeViewSyntax.Xml, false) => new LineStateSyntaxColorizer<int>(new XhtmlHighlighter(), theme),
            (CodeViewSyntax.Css, false) => new LineStateSyntaxColorizer<int>(new CssHighlighter(), theme),
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
