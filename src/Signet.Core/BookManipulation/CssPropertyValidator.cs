using System;
using System.Collections.Frozen;
using System.Collections.Generic;
using Signet.Core.Localization;
using Signet.Core.Parsers;
using Signet.Core.Resources;

namespace Signet.Core.BookManipulation;

/// <summary>
/// CSS declaration checks in stylesheets and <c>&lt;style&gt;</c> elements: an unknown property name (usually a typo)
/// and a property without a value. Called from <see cref="BookValidator.ValidateCurrentBook"/>.
/// </summary>
/// <remarks>
/// calibre runs stylelint (JavaScript); Signet checks the declarations its own CSS parser (<see cref="CssInfo"/>) finds
/// against a list of the properties of the CSS specifications and the <c>@font-face</c> / <c>@page</c> descriptors.
/// Vendor-prefixed properties (<c>-webkit-</c>, <c>-epub-</c>, …) and custom properties (<c>--name</c>) are not
/// checked. Values are not validated beyond being present: what a reader accepts varies too much.
/// </remarks>
public static class CssPropertyValidator
{
    private static readonly FrozenSet<string> KnownProperties = new[]
    {
        "accent-color", "align-content", "align-items", "align-self", "alignment-baseline", "all", "anchor-name",
        "animation", "animation-composition", "animation-delay", "animation-direction", "animation-duration",
        "animation-fill-mode", "animation-iteration-count", "animation-name", "animation-play-state",
        "animation-timing-function", "appearance", "aspect-ratio", "azimuth",
        "backdrop-filter", "backface-visibility", "background", "background-attachment", "background-blend-mode",
        "background-clip", "background-color", "background-image", "background-origin", "background-position",
        "background-position-x", "background-position-y", "background-repeat", "background-size", "baseline-shift",
        "block-size", "bookmark-label", "bookmark-level", "bookmark-state",
        "border", "border-block", "border-block-color", "border-block-end", "border-block-end-color",
        "border-block-end-style", "border-block-end-width", "border-block-start", "border-block-start-color",
        "border-block-start-style", "border-block-start-width", "border-block-style", "border-block-width",
        "border-bottom", "border-bottom-color", "border-bottom-left-radius", "border-bottom-right-radius",
        "border-bottom-style", "border-bottom-width", "border-collapse", "border-color", "border-end-end-radius",
        "border-end-start-radius", "border-image", "border-image-outset", "border-image-repeat", "border-image-slice",
        "border-image-source", "border-image-width", "border-inline", "border-inline-color", "border-inline-end",
        "border-inline-end-color", "border-inline-end-style", "border-inline-end-width", "border-inline-start",
        "border-inline-start-color", "border-inline-start-style", "border-inline-start-width", "border-inline-style",
        "border-inline-width", "border-left", "border-left-color", "border-left-style", "border-left-width",
        "border-radius", "border-right", "border-right-color", "border-right-style", "border-right-width",
        "border-spacing", "border-start-end-radius", "border-start-start-radius", "border-style", "border-top",
        "border-top-color", "border-top-left-radius", "border-top-right-radius", "border-top-style",
        "border-top-width", "border-width", "bottom", "box-decoration-break", "box-shadow", "box-sizing",
        "break-after", "break-before", "break-inside",
        "caption-side", "caret-color", "clear", "clip", "clip-path", "clip-rule", "color", "color-interpolation",
        "color-interpolation-filters", "color-scheme", "column-count", "column-fill", "column-gap", "column-rule",
        "column-rule-color", "column-rule-style", "column-rule-width", "column-span", "column-width", "columns",
        "contain", "contain-intrinsic-size", "container", "container-name", "container-type", "content",
        "content-visibility", "counter-increment", "counter-reset", "counter-set", "cue", "cue-after", "cue-before",
        "cursor", "cx", "cy",
        "d", "direction", "display", "dominant-baseline",
        "elevation", "empty-cells",
        "fill", "fill-opacity", "fill-rule", "filter", "flex", "flex-basis", "flex-direction", "flex-flow",
        "flex-grow", "flex-shrink", "flex-wrap", "float", "flood-color", "flood-opacity", "font", "font-family",
        "font-feature-settings", "font-kerning", "font-language-override", "font-optical-sizing", "font-palette",
        "font-size", "font-size-adjust", "font-stretch", "font-style", "font-synthesis", "font-synthesis-small-caps",
        "font-synthesis-style", "font-synthesis-weight", "font-variant", "font-variant-alternates",
        "font-variant-caps", "font-variant-east-asian", "font-variant-emoji", "font-variant-ligatures",
        "font-variant-numeric", "font-variant-position", "font-variation-settings", "font-weight",
        "footnote-display", "footnote-policy", "forced-color-adjust",
        "gap", "grid", "grid-area", "grid-auto-columns", "grid-auto-flow", "grid-auto-rows", "grid-column",
        "grid-column-end", "grid-column-gap", "grid-column-start", "grid-gap", "grid-row", "grid-row-end",
        "grid-row-gap", "grid-row-start", "grid-template", "grid-template-areas", "grid-template-columns",
        "grid-template-rows",
        "hanging-punctuation", "height", "hyphenate-character", "hyphenate-limit-chars", "hyphenate-limit-last",
        "hyphenate-limit-lines", "hyphenate-limit-zone", "hyphens",
        "image-orientation", "image-rendering", "image-resolution", "initial-letter", "initial-letter-align",
        "inline-size", "inset", "inset-block", "inset-block-end", "inset-block-start", "inset-inline",
        "inset-inline-end", "inset-inline-start", "isolation",
        "justify-content", "justify-items", "justify-self",
        "left", "letter-spacing", "lighting-color", "line-break", "line-clamp", "line-height", "line-height-step",
        "list-style", "list-style-image", "list-style-position", "list-style-type",
        "margin", "margin-block", "margin-block-end", "margin-block-start", "margin-bottom", "margin-break",
        "margin-inline", "margin-inline-end", "margin-inline-start", "margin-left", "margin-right", "margin-top",
        "marker", "marker-end", "marker-mid", "marker-side", "marker-start", "marks", "mask", "mask-border",
        "mask-clip", "mask-composite", "mask-image", "mask-mode", "mask-origin", "mask-position", "mask-repeat",
        "mask-size", "mask-type", "math-depth", "math-shift", "math-style", "max-block-size", "max-height",
        "max-inline-size", "max-lines", "max-width", "min-block-size", "min-height", "min-inline-size", "min-width",
        "mix-blend-mode",
        "object-fit", "object-position", "offset", "offset-anchor", "offset-distance", "offset-path",
        "offset-position", "offset-rotate", "opacity", "order", "orphans", "outline", "outline-color",
        "outline-offset", "outline-style", "outline-width", "overflow", "overflow-anchor", "overflow-block",
        "overflow-clip-margin", "overflow-inline", "overflow-wrap", "overflow-x", "overflow-y", "overscroll-behavior",
        "overscroll-behavior-block", "overscroll-behavior-inline", "overscroll-behavior-x", "overscroll-behavior-y",
        "padding", "padding-block", "padding-block-end", "padding-block-start", "padding-bottom", "padding-inline",
        "padding-inline-end", "padding-inline-start", "padding-left", "padding-right", "padding-top", "page",
        "page-break-after", "page-break-before", "page-break-inside", "paint-order", "pause", "pause-after",
        "pause-before", "perspective", "perspective-origin", "pitch", "pitch-range", "place-content", "place-items",
        "place-self", "play-during", "pointer-events", "position", "print-color-adjust",
        "quotes",
        "r", "resize", "rest", "rest-after", "rest-before", "richness", "right", "rotate", "row-gap", "ruby-align",
        "ruby-merge", "ruby-overhang", "ruby-position", "rx", "ry",
        "scale", "scroll-behavior", "scroll-margin", "scroll-padding", "scroll-snap-align", "scroll-snap-stop",
        "scroll-snap-type", "scrollbar-color", "scrollbar-gutter", "scrollbar-width", "shape-image-threshold",
        "shape-margin", "shape-outside", "shape-rendering", "speak", "speak-as", "speak-header", "speak-numeral",
        "speak-punctuation", "speech-rate", "stop-color", "stop-opacity", "stress", "string-set", "stroke",
        "stroke-dasharray", "stroke-dashoffset", "stroke-linecap", "stroke-linejoin", "stroke-miterlimit",
        "stroke-opacity", "stroke-width",
        "tab-size", "table-layout", "text-align", "text-align-all", "text-align-last", "text-anchor",
        "text-autospace", "text-box", "text-box-edge", "text-box-trim", "text-combine-upright", "text-decoration",
        "text-decoration-color", "text-decoration-line", "text-decoration-skip", "text-decoration-skip-ink",
        "text-decoration-style", "text-decoration-thickness", "text-emphasis", "text-emphasis-color",
        "text-emphasis-position", "text-emphasis-style", "text-indent", "text-justify", "text-orientation",
        "text-overflow", "text-rendering", "text-shadow", "text-size-adjust", "text-spacing", "text-spacing-trim",
        "text-transform", "text-underline-offset", "text-underline-position", "text-wrap", "text-wrap-mode",
        "text-wrap-style", "top", "touch-action", "transform", "transform-box", "transform-origin",
        "transform-style", "transition", "transition-behavior", "transition-delay", "transition-duration",
        "transition-property", "transition-timing-function", "translate",
        "unicode-bidi", "user-select",
        "vector-effect", "vertical-align", "visibility", "voice-balance", "voice-duration", "voice-family",
        "voice-pitch", "voice-range", "voice-rate", "voice-stress", "voice-volume", "volume",
        "white-space", "white-space-collapse", "widows", "width", "will-change", "word-break", "word-spacing",
        "word-wrap", "writing-mode",
        "x", "y", "z-index", "zoom",

        // Non-standard names without a vendor prefix that EPUB readers know.
        "adobe-hyphenate", "adobe-text-layout",

        // @font-face and @page descriptors.
        "ascent-override", "descent-override", "font-display", "font-named-instance", "line-gap-override",
        "size", "size-adjust", "src", "unicode-range", "bleed",
    }.ToFrozenSet(StringComparer.OrdinalIgnoreCase);

    /// <summary>Runs the CSS declaration checks for the whole book.</summary>
    public static IReadOnlyList<ValidationResult> Validate(Book book)
    {
        ArgumentNullException.ThrowIfNull(book);

        List<ValidationResult> results = new();
        foreach (CssResource css in book.GetCssResources())
        {
            string text = css.GetText();
            CheckCss(css.BookPath, text, 0, text, results);
        }

        foreach (HtmlResource html in book.GetHtmlResources())
        {
            string text = html.GetText();
            foreach ((int offset, string css) in MarkupEdits.StyleElements(text))
            {
                CheckCss(html.BookPath, css, offset, text, results);
            }
        }

        return results;
    }

    /// <summary>Whether <paramref name="property"/> is a known, prefixed or custom property.</summary>
    public static bool IsKnownProperty(string property)
    {
        ArgumentNullException.ThrowIfNull(property);
        return property.StartsWith('-') || KnownProperties.Contains(property);
    }

    private static void CheckCss(string bookPath, string css, int offset, string fileText, List<ValidationResult> results)
    {
        CssInfo info;
        try
        {
            info = new CssInfo(css, offset);
        }
        catch (FormatException)
        {
            return;
        }

        // CssInfo works on the text with "\r\n" turned into "\n": its positions count from there.
        string normalized = css.Replace("\r\n", "\n", StringComparison.Ordinal);
        int firstLine = LineOf(fileText, offset);
        foreach (CssRule rule in info.Rules)
        {
            string ruleName = rule.SelectorText.Length > 0 ? rule.SelectorText.Trim() : rule.AtRulePrelude ?? string.Empty;
            int line = -1;
            foreach (CssDeclaration declaration in rule.Declarations)
            {
                string property = declaration.Property;
                if (property.Length == 0)
                {
                    continue;
                }

                string? code = !IsKnownProperty(property) ? "Validation_CssUnknownProperty"
                    : StripImportant(declaration.Value).Length == 0 ? "Validation_CssEmptyValue"
                    : null;
                if (code is null)
                {
                    continue;
                }

                line = line > 0 ? line : firstLine + LineOf(normalized, rule.BlockStart - offset) - 1;
                results.Add(new ValidationResult(
                    ValidationSeverity.Warning, bookPath, line, -1, CoreStrings.Format(code, property, ruleName), code));
            }
        }
    }

    private static string StripImportant(string value)
    {
        string trimmed = value.Trim();
        int bang = trimmed.LastIndexOf('!');
        return bang >= 0 && trimmed[(bang + 1)..].Trim().Equals("important", StringComparison.OrdinalIgnoreCase)
            ? trimmed[..bang].Trim()
            : trimmed;
    }

    private static int LineOf(string text, int offset)
    {
        int line = 1;
        for (int i = 0; i < Math.Min(offset, text.Length); i++)
        {
            if (text[i] == '\n')
            {
                line++;
            }
        }

        return line;
    }
}
