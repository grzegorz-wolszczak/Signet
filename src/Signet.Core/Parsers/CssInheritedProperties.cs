using System;
using System.Collections.Generic;
using System.Collections.Frozen;
using System.Linq;

namespace Signet.Core.Parsers;

/// <summary>
/// The CSS properties that are inherited by default (an element without its own value takes the parent's) —
/// used by <see cref="CssCascadeResolver"/> to decide which ancestor declarations reach the inspected element.
/// </summary>
/// <remarks>
/// The list follows the "Inherited: yes" entries of the CSS specifications (CSS 2.1, Fonts, Text, Text
/// Decoration, Writing Modes, Lists, Ruby, …) plus the prefixed variants common in EPUBs
/// (<c>-webkit-</c>/<c>-epub-</c>). Custom properties (<c>--name</c>) are always inherited.
/// </remarks>
public static class CssInheritedProperties
{
    private static readonly FrozenSet<string> Inherited = new[]
    {
        "accent-color", "caret", "caret-animation", "caret-shape", "clip-rule", "color-interpolation-filters",
        "dominant-baseline", "dynamic-range-limit",
        "fill", "fill-opacity", "fill-rule",
        "azimuth", "border-collapse", "border-spacing", "caption-side", "caret-color", "color", "color-scheme",
        "cursor", "direction", "elevation", "empty-cells",
        "font", "font-family", "font-feature-settings", "font-kerning", "font-language-override",
        "font-optical-sizing", "font-palette", "font-size", "font-size-adjust", "font-stretch", "font-style",
        "font-synthesis", "font-synthesis-small-caps", "font-synthesis-style", "font-synthesis-weight",
        "font-variant", "font-variant-alternates", "font-variant-caps", "font-variant-east-asian",
        "font-variant-emoji", "font-variant-ligatures", "font-variant-numeric", "font-variant-position",
        "font-variation-settings", "font-weight", "forced-color-adjust",
        "hanging-punctuation", "hyphenate-character", "hyphenate-limit-chars", "hyphens", "image-orientation",
        "image-rendering", "interactivity", "interest-delay", "interest-delay-end", "interest-delay-start",
        "letter-spacing", "line-break", "line-height",
        "list-style", "list-style-image", "list-style-position", "list-style-type",
        "marker", "marker-end", "marker-mid", "marker-start", "math-depth", "math-style",
        "orphans", "overflow-wrap", "paint-order", "pitch", "pitch-range", "pointer-events",
        "print-color-adjust", "quotes", "richness", "ruby-align", "ruby-overhang", "ruby-position",
        "scrollbar-color", "shape-rendering", "speak", "speak-header", "speak-numeral", "speak-punctuation",
        "speech-rate", "stress", "stroke", "stroke-dasharray", "stroke-dashoffset", "stroke-linecap",
        "stroke-linejoin", "stroke-miterlimit", "stroke-opacity", "stroke-width", "tab-size",
        "text-align", "text-align-last", "text-combine-upright", "text-decoration-skip-ink",
        "text-anchor", "text-autospace", "text-emphasis", "text-emphasis-color", "text-emphasis-position",
        "text-emphasis-style", "text-fit", "text-indent", "text-justify", "text-orientation", "text-rendering",
        "text-shadow", "text-size-adjust", "text-transform", "text-underline-offset", "text-underline-position",
        "text-wrap", "text-wrap-mode", "text-wrap-style",
        "visibility", "voice-family", "volume", "white-space", "white-space-collapse", "widows", "window-drag",
        "word-break", "word-spacing", "word-wrap", "writing-mode",

        // Prefixed variants used in EPUBs.
        "-webkit-hyphens", "-epub-hyphens", "-moz-hyphens", "-ms-hyphens", "adobe-hyphenate",
        "-webkit-writing-mode", "-epub-writing-mode", "-webkit-text-orientation", "-epub-text-orientation",
        "-webkit-text-combine", "-epub-text-combine", "-webkit-text-emphasis", "-epub-text-emphasis",
        "-webkit-text-emphasis-color", "-epub-text-emphasis-color", "-webkit-text-emphasis-position",
        "-epub-text-emphasis-position", "-webkit-text-emphasis-style", "-epub-text-emphasis-style",
        "-webkit-line-break", "-epub-line-break", "-webkit-word-break", "-epub-word-break",
        "-webkit-text-size-adjust", "-webkit-font-feature-settings", "-moz-font-feature-settings",
        "-webkit-font-smoothing", "-webkit-text-fill-color", "-webkit-text-stroke",
        "-webkit-text-stroke-color", "-webkit-text-stroke-width", "-webkit-hyphenate-character",
        "-webkit-ruby-position", "-epub-ruby-position", "-webkit-text-security",
    }.ToFrozenSet(StringComparer.OrdinalIgnoreCase);

    private static readonly IReadOnlyList<string> InheritedNames = Array.AsReadOnly(
        Inherited.OrderBy(property => property, StringComparer.OrdinalIgnoreCase).ToArray());

    /// <summary>The known inherited property names, including EPUB-common prefixed variants.</summary>
    public static IReadOnlyList<string> KnownInheritedProperties => InheritedNames;

    /// <summary>Whether <paramref name="property"/> is inherited by default (case-insensitive).</summary>
    public static bool IsInherited(string property)
    {
        ArgumentNullException.ThrowIfNull(property);
        return property.StartsWith("--", StringComparison.Ordinal) || Inherited.Contains(property);
    }
}
