using System;
using Signet.App.Resources;
using Signet.Core.Parsers;

namespace Signet.App.ViewModels;

internal static class CssPropertyReferenceDocumentation
{
    public static string TooltipFor(string property)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(property);

        string normalized = StripKnownPrefix(property);
        string key = $"CssPropertyReferenceDescription_{normalized.Replace('-', '_')}";
        string summary = Strings.TryGet(key)
            ?? Strings.Format($"CssPropertyReferenceFamily_{FamilyFor(normalized)}", normalized);
        string? keywords = CssPropertyKeywordCatalog.ForProperty(property);

        return keywords is null
            ? summary
            : Strings.Format("CssPropertyReferenceTooltip_WithKeywords", summary, keywords);
    }

    private static string FamilyFor(string property)
    {
        if (property is "border-collapse" or "caption-side" or "empty-cells" || property.StartsWith("table-", StringComparison.Ordinal))
        {
            return "Tables";
        }

        if (property.StartsWith("background", StringComparison.Ordinal))
        {
            return "Background";
        }

        if (property.StartsWith("border", StringComparison.Ordinal) || property.StartsWith("corner", StringComparison.Ordinal))
        {
            return "Border";
        }

        if (property.StartsWith("font", StringComparison.Ordinal))
        {
            return "Font";
        }

        if (property.StartsWith("text", StringComparison.Ordinal)
            || property.StartsWith("line", StringComparison.Ordinal)
            || property.StartsWith("word", StringComparison.Ordinal)
            || property.StartsWith("hyphen", StringComparison.Ordinal)
            || property.StartsWith("white-space", StringComparison.Ordinal)
            || property.StartsWith("writing-mode", StringComparison.Ordinal)
            || property.StartsWith("unicode-bidi", StringComparison.Ordinal)
            || property.StartsWith("math-", StringComparison.Ordinal)
            || property is "direction" or "hanging-punctuation")
        {
            return "Text";
        }

        if (property.StartsWith("margin", StringComparison.Ordinal)
            || property.StartsWith("padding", StringComparison.Ordinal)
            || property.StartsWith("inset", StringComparison.Ordinal)
            || property is "width" or "height" or "min-width" or "max-width" or "min-height" or "max-height"
            or "block-size" or "inline-size" or "min-block-size" or "max-block-size" or "min-inline-size"
            or "max-inline-size" or "aspect-ratio" or "zoom" or "frame-sizing")
        {
            return "Box";
        }

        if (property.StartsWith("grid", StringComparison.Ordinal))
        {
            return "Grid";
        }

        if (property.StartsWith("flex", StringComparison.Ordinal))
        {
            return "Flex";
        }

        if (property is "alignment-baseline" or "dominant-baseline")
        {
            return "Svg";
        }

        if (property.StartsWith("align", StringComparison.Ordinal)
            || property.StartsWith("justify", StringComparison.Ordinal)
            || property.StartsWith("place", StringComparison.Ordinal)
            || property is "gap" or "row-gap" or "column-gap"
            || (property.StartsWith("column", StringComparison.Ordinal)
                && !property.StartsWith("column-break", StringComparison.Ordinal)))
        {
            return "Layout";
        }

        if (property.StartsWith("scroll", StringComparison.Ordinal) || property.StartsWith("overscroll", StringComparison.Ordinal))
        {
            return "Scrolling";
        }

        if (property.StartsWith("list", StringComparison.Ordinal)
            || property.StartsWith("counter", StringComparison.Ordinal))
        {
            return "Lists";
        }

        if (property.StartsWith("break", StringComparison.Ordinal)
            || property.StartsWith("page", StringComparison.Ordinal)
            || property.StartsWith("column-break", StringComparison.Ordinal)
            || property is "orphans" or "widows")
        {
            return "Pagination";
        }

        if (property is "position" or "top" or "right" or "bottom" or "left" or "z-index")
        {
            return "Position";
        }

        if (property.StartsWith("transform", StringComparison.Ordinal)
            || property is "translate" or "rotate" or "scale" or "perspective" or "perspective-origin"
            or "offset" or "offset-anchor" or "offset-distance" or "offset-path" or "offset-position" or "offset-rotate")
        {
            return "Transforms";
        }

        if (property.StartsWith("animation", StringComparison.Ordinal)
            || property.StartsWith("transition", StringComparison.Ordinal)
            || property.StartsWith("timeline", StringComparison.Ordinal)
            || property.StartsWith("trigger", StringComparison.Ordinal)
            || property.StartsWith("view-transition", StringComparison.Ordinal))
        {
            return "Animation";
        }

        if (property.StartsWith("image", StringComparison.Ordinal) || property.StartsWith("object", StringComparison.Ordinal))
        {
            return "Images";
        }

        if (property.StartsWith("mask", StringComparison.Ordinal)
            || property.StartsWith("filter", StringComparison.Ordinal)
            || property.StartsWith("clip", StringComparison.Ordinal)
            || property.StartsWith("mix-blend", StringComparison.Ordinal)
            || property == "isolation"
            || property is "opacity" or "will-change"
            || property == "box-shadow")
        {
            return "Effects";
        }

        if (property.StartsWith("fill", StringComparison.Ordinal)
            || property.StartsWith("stroke", StringComparison.Ordinal)
            || property.StartsWith("flood", StringComparison.Ordinal)
            || property.StartsWith("lighting", StringComparison.Ordinal)
            || property.StartsWith("stop-", StringComparison.Ordinal)
            || property.StartsWith("marker", StringComparison.Ordinal)
            || property is "paint-order" or "shape-rendering"
            || property is "cx" or "cy" or "d" or "r" or "rx" or "ry")
        {
            return "Svg";
        }

        if (property is "content" or "quotes")
        {
            return "Content";
        }

        if (property.StartsWith("contain", StringComparison.Ordinal) || property.StartsWith("container", StringComparison.Ordinal))
        {
            return "Containment";
        }

        if (property.StartsWith("appearance", StringComparison.Ordinal)
            || property.StartsWith("caret", StringComparison.Ordinal)
            || property.StartsWith("cursor", StringComparison.Ordinal)
            || property.StartsWith("pointer", StringComparison.Ordinal)
            || property.StartsWith("touch", StringComparison.Ordinal)
            || property.StartsWith("user", StringComparison.Ordinal)
            || property is "resize" or "window-drag" or "interactivity" or "interest-delay"
            || property.StartsWith("interest-delay", StringComparison.Ordinal))
        {
            return "Interaction";
        }

        if (property.StartsWith("ruby", StringComparison.Ordinal))
        {
            return "Ruby";
        }

        if (property.Contains("color", StringComparison.Ordinal) || property is "accent-color" or "dynamic-range-limit")
        {
            return "Color";
        }

        if (property.StartsWith("overflow", StringComparison.Ordinal))
        {
            return "Overflow";
        }

        if (property is "display" or "float" or "clear" or "order" or "reading-flow" or "reading-order"
            or "shape-outside" or "shape-margin" or "shape-image-threshold")
        {
            return "Layout";
        }

        return "Generic";
    }

    private static string StripKnownPrefix(string property)
    {
        foreach (string prefix in new[] { "-webkit-", "-moz-", "-ms-", "-epub-" })
        {
            if (property.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
            {
                return property[prefix.Length..];
            }
        }

        return property;
    }
}
