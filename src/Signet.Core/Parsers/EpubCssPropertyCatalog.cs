using System;
using System.Collections.Frozen;
using System.Collections.Generic;
using System.Linq;

namespace Signet.Core.Parsers;

/// <summary>
/// The CSS properties the property reference lists for EPUB authors: a short list of the common ones, and all the ones
/// that matter in an ebook. Built on <see cref="CssPropertyNames"/>, so every name listed is one the validator knows.
/// </summary>
public static class EpubCssPropertyCatalog
{
    private static readonly IReadOnlyList<string> Common = Array.AsReadOnly(new[]
    {
        "background", "background-color", "background-image", "border", "border-bottom", "border-left",
        "border-right", "border-top", "bottom", "break-after", "break-before", "clear", "color", "display",
        "direction", "float", "font", "font-family", "font-size", "font-style", "font-weight", "height",
        "hyphens", "left", "letter-spacing", "line-height", "list-style", "list-style-type", "margin",
        "margin-bottom", "margin-left", "margin-right", "margin-top", "opacity", "orphans", "overflow",
        "padding", "padding-bottom", "padding-left", "padding-right", "padding-top", "page-break-after",
        "page-break-before", "page-break-inside", "position", "right", "text-align", "text-decoration",
        "text-indent", "text-transform", "top", "transform", "vertical-align", "visibility", "white-space",
        "width", "widows", "word-spacing", "z-index",
    });

    // Standard properties that do nothing useful in an ebook: reading systems paginate a static page, so animation,
    // transitions, scrolling, scroll bars, pointer and text-input interaction, container queries and motion paths
    // either are not supported or work against the reader's own controls. They stay known to the validator.
    private static readonly FrozenSet<string> NotForEbooks = new[]
    {
        "accent-color", "anchor-name", "animation", "animation-composition", "animation-delay", "animation-direction",
        "animation-duration", "animation-fill-mode", "animation-iteration-count", "animation-name",
        "animation-play-state", "animation-timing-function", "appearance", "backdrop-filter", "backface-visibility",
        "caret-color", "contain", "contain-intrinsic-size", "container", "container-name", "container-type",
        "content-visibility", "cursor", "forced-color-adjust", "offset", "offset-anchor", "offset-distance",
        "offset-path", "offset-position", "offset-rotate", "overflow-anchor", "overscroll-behavior",
        "overscroll-behavior-block", "overscroll-behavior-inline", "overscroll-behavior-x", "overscroll-behavior-y",
        "perspective", "perspective-origin", "pointer-events", "print-color-adjust", "resize", "scroll-behavior",
        "scroll-margin", "scroll-padding", "scroll-snap-align", "scroll-snap-stop", "scroll-snap-type",
        "scrollbar-color", "scrollbar-gutter", "scrollbar-width", "touch-action", "transform-style", "transition",
        "transition-behavior", "transition-delay", "transition-duration", "transition-property",
        "transition-timing-function", "user-select", "will-change", "zoom",
    }.ToFrozenSet(StringComparer.OrdinalIgnoreCase);

    private static readonly IReadOnlyList<string> All = Array.AsReadOnly(
        Common
            .Concat(CssPropertyNames.StandardProperties.Where(property => !NotForEbooks.Contains(property)))
            .Concat(CssPropertyNames.EpubVendorProperties)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(property => property, StringComparer.OrdinalIgnoreCase)
            .ToArray());

    /// <summary>The curated properties most commonly encountered in EPUB stylesheets.</summary>
    public static IReadOnlyList<string> CommonProperties => Common;

    /// <summary>
    /// Every property that matters in an ebook: the standard ones without those reading systems have no use for
    /// (animation, scrolling, interaction…), plus the <c>-epub-</c>, <c>-webkit-</c> and Adobe names EPUB readers
    /// understand. Custom properties are omitted because their names are book-defined.
    /// </summary>
    public static IReadOnlyList<string> AllProperties => All;
}
