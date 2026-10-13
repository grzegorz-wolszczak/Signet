using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Text.Json;
using Signet.App.Infrastructure;
using Signet.Core.MainUI;
using Signet.Core.Misc;
using Signet.Core.Themes;
using TextMateSharp.Grammars;
using TextMateSharp.Internal.Themes.Reader;
using TextMateSharp.Themes;

namespace Signet.App.CodeView;

/// <summary>
/// Selects syntax highlighting for Code View. (X)HTML/XML/SVG and CSS use line-by-line
/// highlighters (<see cref="CreateColorizer"/>, colors from the theme) —
/// basic or extended; the remaining syntaxes (JavaScript, JSON) use TextMateSharp with a TextMate
/// theme built from the <c>js.*</c> and <c>json.*</c> keys of the theme (<see cref="ThemeFor"/>).
/// </summary>
internal static class CodeViewTextMate
{
    private static readonly RegistryOptions SharedOptions = new(ThemeName.LightPlus);

    // The TextMate scopes each key colors; a more specific scope of another key wins over a shorter one.
    private static readonly (ThemeKey Key, string[] Scopes)[] ScopeMap =
    {
        (ThemeKeys.JsKeyword, new[] { "keyword", "storage", "storage.type", "storage.modifier", "keyword.operator.new", "keyword.operator.expression" }),
        (ThemeKeys.JsControlKeyword, new[] { "keyword.control" }),
        (ThemeKeys.JsOperator, new[] { "keyword.operator" }),
        (ThemeKeys.JsString, new[] { "string" }),
        (ThemeKeys.JsNumber, new[] { "constant.numeric" }),
        (ThemeKeys.JsConstant, new[] { "constant.language", "variable.language" }),
        (ThemeKeys.JsRegexp, new[] { "string.regexp", "constant.regexp" }),
        (ThemeKeys.JsComment, new[] { "comment" }),
        (ThemeKeys.JsFunction, new[] { "entity.name.function", "support.function" }),
        (ThemeKeys.JsVariable, new[] { "variable", "meta.definition.variable.name", "support.variable" }),
        (ThemeKeys.JsType, new[] { "support.class", "support.type", "entity.name.type", "entity.name.class" }),
        (ThemeKeys.JsonProperty, new[] { "support.type.property-name.json" }),
        (ThemeKeys.JsonString, new[] { "string.json", "string.quoted.double.json" }),
        (ThemeKeys.JsonNumber, new[] { "constant.numeric.json" }),
        (ThemeKeys.JsonConstant, new[] { "constant.language.json" }),
    };

    /// <summary>
    /// The key that colors a token with these scopes (outermost first, as TextMate gives them), matched as a TextMate
    /// theme matches: the innermost scope with a rule wins, and among the rules of a scope the longest selector (a
    /// selector matches the scope itself and the scopes below it, e.g. <c>keyword</c> matches
    /// <c>keyword.control.js</c>); <c>null</c> = plain text.
    /// </summary>
    public static ThemeKey? KeyFor(IReadOnlyList<string> scopes)
    {
        ArgumentNullException.ThrowIfNull(scopes);
        for (int i = scopes.Count - 1; i >= 0; i--)
        {
            string scope = scopes[i];
            ThemeKey? best = null;
            int bestLength = -1;
            foreach ((ThemeKey key, string[] selectors) in ScopeMap)
            {
                foreach (string selector in selectors)
                {
                    bool matches = scope == selector
                        || (scope.StartsWith(selector, StringComparison.Ordinal) && scope[selector.Length] == '.');
                    if (matches && selector.Length > bestLength)
                    {
                        best = key;
                        bestLength = selector.Length;
                    }
                }
            }

            if (best is not null)
            {
                return best;
            }
        }

        return null;
    }

    /// <summary>Registry options (grammars + themes) shared by all Code View tabs.</summary>
    public static RegistryOptions Options => SharedOptions;

    /// <summary>
    /// The TextMate theme of <paramref name="theme"/>: the text color of <c>editor.text</c> as the default and a rule
    /// per <c>js.*</c> / <c>json.*</c> key (colors, bold, italic; the underline effects as an underline and
    /// <see cref="EffectType.Strikeout"/> as a strikethrough).
    /// </summary>
    public static IRawTheme ThemeFor(ResolvedTheme theme)
    {
        ArgumentNullException.ThrowIfNull(theme);
        using MemoryStream json = new();
        using (Utf8JsonWriter writer = new(json))
        {
            writer.WriteStartObject();
            writer.WriteString("name", theme.Theme.Name);
            writer.WriteStartArray("tokenColors");

            TextAttributes text = theme.Attributes(ThemeKeys.EditorText);
            writer.WriteStartObject();
            writer.WriteStartObject("settings");
            WriteColor(writer, "foreground", text.Foreground);
            WriteColor(writer, "background", text.Background);
            writer.WriteEndObject();
            writer.WriteEndObject();

            foreach ((ThemeKey key, string[] scopes) in ScopeMap)
            {
                TextAttributes attributes = theme.Attributes(key);
                writer.WriteStartObject();
                writer.WriteStartArray("scope");
                foreach (string scope in scopes)
                {
                    writer.WriteStringValue(scope);
                }

                writer.WriteEndArray();
                writer.WriteStartObject("settings");
                WriteColor(writer, "foreground", attributes.Foreground ?? text.Foreground);
                WriteColor(writer, "background", attributes.Background);

                // Always written: an empty style keeps a parent scope's bold or italic away from this key.
                writer.WriteString("fontStyle", FontStyle(attributes));
                writer.WriteEndObject();
                writer.WriteEndObject();
            }

            writer.WriteEndArray();
            writer.WriteEndObject();
        }

        json.Position = 0;
        using StreamReader reader = new(json, Encoding.UTF8);
        return ThemeReader.ReadThemeSync(reader);
    }

    // Avalonia reads the colors of the TextMate theme, so a translucent color keeps its #AARRGGBB form.
    private static void WriteColor(Utf8JsonWriter writer, string name, ThemeColor? color)
    {
        if (color is { } c)
        {
            writer.WriteString(name, c.ToString());
        }
    }

    private static string FontStyle(TextAttributes attributes)
    {
        List<string> parts = new();
        if (attributes.Italic)
        {
            parts.Add("italic");
        }

        if (attributes.Bold)
        {
            parts.Add("bold");
        }

        switch (attributes.Effect)
        {
            case EffectType.Underline or EffectType.BoldUnderline or EffectType.Wave or EffectType.DottedUnderline
                or EffectType.DashedUnderline:
                parts.Add("underline");
                break;
            case EffectType.Strikeout:
                parts.Add("strikethrough");
                break;
        }

        return string.Join(' ', parts);
    }

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
