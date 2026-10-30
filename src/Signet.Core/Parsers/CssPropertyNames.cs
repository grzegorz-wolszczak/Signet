using System;
using System.Collections.Frozen;
using System.Collections.Generic;
using System.Linq;

namespace Signet.Core.Parsers;

/// <summary>
/// The CSS property names Signet knows: the properties of the CSS specifications, and the vendor-prefixed and
/// non-standard names EPUB reading systems understand. One list for every feature that needs to know whether a name is
/// a property — the declaration checks of the validator and the CSS property reference — so they cannot disagree.
/// </summary>
public static class CssPropertyNames
{
    // The properties of the CSS specifications (W3C recommendations and drafts that browsers or reading systems ship),
    // including the CSS 2 aural / CSS 3 speech properties of the EPUB 3 CSS profile and the paged-media ones. No
    // vendor prefixes, no @font-face / @page descriptors.
    private static readonly string[] StandardNames =
    {
        "accent-color", "align-content", "align-items", "align-self", "alignment-baseline", "all", "anchor-name",
        "animation", "animation-composition", "animation-delay", "animation-direction", "animation-duration",
        "animation-fill-mode", "animation-iteration-count", "animation-name", "animation-play-state",
        "animation-timing-function", "appearance", "aspect-ratio", "azimuth", "backdrop-filter",
        "backface-visibility", "background", "background-attachment", "background-blend-mode", "background-clip",
        "background-color", "background-image", "background-origin", "background-position", "background-position-x",
        "background-position-y", "background-repeat", "background-size", "baseline-shift", "block-size",
        "bookmark-label", "bookmark-level", "bookmark-state", "border", "border-block", "border-block-color",
        "border-block-end", "border-block-end-color", "border-block-end-style", "border-block-end-width",
        "border-block-start", "border-block-start-color", "border-block-start-style", "border-block-start-width",
        "border-block-style", "border-block-width", "border-bottom", "border-bottom-color",
        "border-bottom-left-radius", "border-bottom-right-radius", "border-bottom-style", "border-bottom-width",
        "border-collapse", "border-color", "border-end-end-radius", "border-end-start-radius", "border-image",
        "border-image-outset", "border-image-repeat", "border-image-slice", "border-image-source",
        "border-image-width", "border-inline", "border-inline-color", "border-inline-end", "border-inline-end-color",
        "border-inline-end-style", "border-inline-end-width", "border-inline-start", "border-inline-start-color",
        "border-inline-start-style", "border-inline-start-width", "border-inline-style", "border-inline-width",
        "border-left", "border-left-color", "border-left-style", "border-left-width", "border-radius", "border-right",
        "border-right-color", "border-right-style", "border-right-width", "border-spacing", "border-start-end-radius",
        "border-start-start-radius", "border-style", "border-top", "border-top-color", "border-top-left-radius",
        "border-top-right-radius", "border-top-style", "border-top-width", "border-width", "bottom",
        "box-decoration-break", "box-shadow", "box-sizing", "break-after", "break-before", "break-inside",
        "caption-side", "caret-color", "clear", "clip", "clip-path", "clip-rule", "color", "color-interpolation",
        "color-interpolation-filters", "color-scheme", "column-count", "column-fill", "column-gap", "column-rule",
        "column-rule-color", "column-rule-style", "column-rule-width", "column-span", "column-width", "columns",
        "contain", "contain-intrinsic-size", "container", "container-name", "container-type", "content",
        "content-visibility", "counter-increment", "counter-reset", "counter-set", "cue", "cue-after", "cue-before",
        "cursor", "cx", "cy", "d", "direction", "display", "dominant-baseline", "elevation", "empty-cells", "fill",
        "fill-opacity", "fill-rule", "filter", "flex", "flex-basis", "flex-direction", "flex-flow", "flex-grow",
        "flex-shrink", "flex-wrap", "float", "flood-color", "flood-opacity", "font", "font-family",
        "font-feature-settings", "font-kerning", "font-language-override", "font-optical-sizing", "font-palette",
        "font-size", "font-size-adjust", "font-stretch", "font-style", "font-synthesis", "font-synthesis-small-caps",
        "font-synthesis-style", "font-synthesis-weight", "font-variant", "font-variant-alternates",
        "font-variant-caps", "font-variant-east-asian", "font-variant-emoji", "font-variant-ligatures",
        "font-variant-numeric", "font-variant-position", "font-variation-settings", "font-weight", "footnote-display",
        "footnote-policy", "forced-color-adjust", "gap", "grid", "grid-area", "grid-auto-columns", "grid-auto-flow",
        "grid-auto-rows", "grid-column", "grid-column-end", "grid-column-gap", "grid-column-start", "grid-gap",
        "grid-row", "grid-row-end", "grid-row-gap", "grid-row-start", "grid-template", "grid-template-areas",
        "grid-template-columns", "grid-template-rows", "hanging-punctuation", "height", "hyphenate-character",
        "hyphenate-limit-chars", "hyphenate-limit-last", "hyphenate-limit-lines", "hyphenate-limit-zone", "hyphens",
        "image-orientation", "image-rendering", "image-resolution", "initial-letter", "initial-letter-align",
        "inline-size", "inset", "inset-block", "inset-block-end", "inset-block-start", "inset-inline",
        "inset-inline-end", "inset-inline-start", "isolation", "justify-content", "justify-items", "justify-self",
        "left", "letter-spacing", "lighting-color", "line-break", "line-clamp", "line-height", "line-height-step",
        "list-style", "list-style-image", "list-style-position", "list-style-type", "margin", "margin-block",
        "margin-block-end", "margin-block-start", "margin-bottom", "margin-break", "margin-inline",
        "margin-inline-end", "margin-inline-start", "margin-left", "margin-right", "margin-top", "marker",
        "marker-end", "marker-mid", "marker-side", "marker-start", "marks", "mask", "mask-border", "mask-clip",
        "mask-composite", "mask-image", "mask-mode", "mask-origin", "mask-position", "mask-repeat", "mask-size",
        "mask-type", "math-depth", "math-shift", "math-style", "max-block-size", "max-height", "max-inline-size",
        "max-lines", "max-width", "min-block-size", "min-height", "min-inline-size", "min-width", "mix-blend-mode",
        "object-fit", "object-position", "offset", "offset-anchor", "offset-distance", "offset-path",
        "offset-position", "offset-rotate", "opacity", "order", "orphans", "outline", "outline-color",
        "outline-offset", "outline-style", "outline-width", "overflow", "overflow-anchor", "overflow-block",
        "overflow-clip-margin", "overflow-inline", "overflow-wrap", "overflow-x", "overflow-y", "overscroll-behavior",
        "overscroll-behavior-block", "overscroll-behavior-inline", "overscroll-behavior-x", "overscroll-behavior-y",
        "padding", "padding-block", "padding-block-end", "padding-block-start", "padding-bottom", "padding-inline",
        "padding-inline-end", "padding-inline-start", "padding-left", "padding-right", "padding-top", "page",
        "page-break-after", "page-break-before", "page-break-inside", "paint-order", "pause", "pause-after",
        "pause-before", "perspective", "perspective-origin", "pitch", "pitch-range", "place-content", "place-items",
        "place-self", "play-during", "pointer-events", "position", "print-color-adjust", "quotes", "r", "resize",
        "rest", "rest-after", "rest-before", "richness", "right", "rotate", "row-gap", "ruby-align", "ruby-merge",
        "ruby-overhang", "ruby-position", "rx", "ry", "scale", "scroll-behavior", "scroll-margin", "scroll-padding",
        "scroll-snap-align", "scroll-snap-stop", "scroll-snap-type", "scrollbar-color", "scrollbar-gutter",
        "scrollbar-width", "shape-image-threshold", "shape-margin", "shape-outside", "shape-rendering", "speak",
        "speak-as", "speak-header", "speak-numeral", "speak-punctuation", "speech-rate", "stop-color", "stop-opacity",
        "stress", "string-set", "stroke", "stroke-dasharray", "stroke-dashoffset", "stroke-linecap",
        "stroke-linejoin", "stroke-miterlimit", "stroke-opacity", "stroke-width", "tab-size", "table-layout",
        "text-align", "text-align-all", "text-align-last", "text-anchor", "text-autospace", "text-box",
        "text-box-edge", "text-box-trim", "text-combine-upright", "text-decoration", "text-decoration-color",
        "text-decoration-line", "text-decoration-skip", "text-decoration-skip-ink", "text-decoration-style",
        "text-decoration-thickness", "text-emphasis", "text-emphasis-color", "text-emphasis-position",
        "text-emphasis-style", "text-indent", "text-justify", "text-orientation", "text-overflow", "text-rendering",
        "text-shadow", "text-size-adjust", "text-spacing", "text-spacing-trim", "text-transform",
        "text-underline-offset", "text-underline-position", "text-wrap", "text-wrap-mode", "text-wrap-style", "top",
        "touch-action", "transform", "transform-box", "transform-origin", "transform-style", "transition",
        "transition-behavior", "transition-delay", "transition-duration", "transition-property",
        "transition-timing-function", "translate", "unicode-bidi", "user-select", "vector-effect", "vertical-align",
        "visibility", "voice-balance", "voice-duration", "voice-family", "voice-pitch", "voice-range", "voice-rate",
        "voice-stress", "voice-volume", "volume", "white-space", "white-space-collapse", "widows", "width",
        "will-change", "word-break", "word-spacing", "word-wrap", "writing-mode", "x", "y", "z-index", "zoom",
    };

    // Names outside the specifications that EPUB content uses and reading systems understand: the -epub- properties
    // of the EPUB 3 CSS profile, the -webkit- ones Apple Books and other WebKit-based readers read, the hyphenation
    // fallbacks of other engines, and Adobe's (RMSDK) unprefixed extensions.
    private static readonly string[] EpubVendorNames =
    {
        "-epub-hyphens", "-epub-line-break", "-epub-ruby-position", "-epub-speak-header", "-epub-speak-numeral",
        "-epub-speak-punctuation", "-epub-text-align-last", "-epub-text-combine", "-epub-text-combine-horizontal",
        "-epub-text-emphasis", "-epub-text-emphasis-color", "-epub-text-emphasis-position",
        "-epub-text-emphasis-style", "-epub-text-orientation", "-epub-text-underline-position", "-epub-word-break",
        "-epub-writing-mode",
        "-webkit-box-decoration-break", "-webkit-column-break-after", "-webkit-column-break-before",
        "-webkit-column-break-inside", "-webkit-font-feature-settings", "-webkit-hyphenate-character",
        "-webkit-hyphens", "-webkit-line-break", "-webkit-ruby-position", "-webkit-text-combine",
        "-webkit-text-emphasis", "-webkit-text-emphasis-color", "-webkit-text-emphasis-position",
        "-webkit-text-emphasis-style", "-webkit-text-fill-color", "-webkit-text-orientation",
        "-webkit-text-size-adjust", "-webkit-text-stroke", "-webkit-text-stroke-color", "-webkit-text-stroke-width",
        "-webkit-transform", "-webkit-transform-origin", "-webkit-word-break", "-webkit-writing-mode",
        "-moz-font-feature-settings", "-moz-hyphens", "-ms-hyphens",
        "adobe-hyphenate", "adobe-text-layout",
    };

    private static readonly FrozenSet<string> Standard = StandardNames.ToFrozenSet(StringComparer.OrdinalIgnoreCase);

    private static readonly FrozenSet<string> EpubVendor = EpubVendorNames.ToFrozenSet(StringComparer.OrdinalIgnoreCase);

    /// <summary>The standard property names, sorted.</summary>
    public static IReadOnlyList<string> StandardProperties { get; } =
        Array.AsReadOnly(StandardNames.Order(StringComparer.OrdinalIgnoreCase).ToArray());

    /// <summary>The vendor-prefixed and non-standard names EPUB reading systems understand, sorted.</summary>
    public static IReadOnlyList<string> EpubVendorProperties { get; } =
        Array.AsReadOnly(EpubVendorNames.Order(StringComparer.OrdinalIgnoreCase).ToArray());

    /// <summary>Whether <paramref name="property"/> is a property of the CSS specifications (case-insensitive).</summary>
    public static bool IsStandard(string property)
    {
        ArgumentNullException.ThrowIfNull(property);
        return Standard.Contains(property);
    }

    /// <summary>Whether <paramref name="property"/> is a vendor or non-standard name EPUB readers understand.</summary>
    public static bool IsEpubVendorProperty(string property)
    {
        ArgumentNullException.ThrowIfNull(property);
        return EpubVendor.Contains(property);
    }
}
