using System;
using System.Linq;
using Signet.App.Resources;
using Signet.Core.Parsers;
using Signet.Core.RichContent;

namespace Signet.App.ViewModels;

/// <summary>
/// The tooltip of a property in the CSS property reference: its own description when there is one (a prefixed name
/// uses the unprefixed one's), otherwise the description of its family; then its finite keyword values — formatted
/// (the label in bold, the values as code) through the markup of the resource <c>CssPropertyReferenceTooltip_WithKeywords</c>.
/// </summary>
internal static class CssPropertyReferenceDocumentation
{
    public static RichText TooltipFor(string property)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(property);

        string normalized = StripKnownPrefix(property);
        string key = $"CssPropertyReferenceDescription_{normalized.Replace('-', '_')}";
        string summary = Strings.TryGet(key)
            ?? Strings.Format($"CssPropertyReferenceFamily_{FamilyFor(normalized)}", normalized);
        string? keywords = CssPropertyKeywordCatalog.ForProperty(property);

        if (keywords is null)
        {
            return RichText.Plain(summary);
        }

        string values = string.Join(", ", keywords.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries)
            .Select(k => "`" + RichTextMarkup.EscapeCode(k) + "`"));
        return RichTextMarkup.Parse(Strings.Format("CssPropertyReferenceTooltip_WithKeywords", RichTextMarkup.Escape(summary), values));
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

        if (property.StartsWith("border", StringComparison.Ordinal))
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
            or "max-inline-size" or "aspect-ratio")
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
            || property is "translate" or "rotate" or "scale")
        {
            return "Transforms";
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
            || property is "opacity"
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

        if (property.StartsWith("speak", StringComparison.Ordinal)
            || property.StartsWith("voice", StringComparison.Ordinal)
            || property.StartsWith("cue", StringComparison.Ordinal)
            || property.StartsWith("pause", StringComparison.Ordinal)
            || property.StartsWith("rest", StringComparison.Ordinal)
            || property.StartsWith("pitch", StringComparison.Ordinal)
            || property is "azimuth" or "elevation" or "play-during" or "richness" or "speech-rate" or "stress"
            or "volume")
        {
            return "Speech";
        }

        if (property.StartsWith("ruby", StringComparison.Ordinal))
        {
            return "Ruby";
        }

        if (property.Contains("color", StringComparison.Ordinal))
        {
            return "Color";
        }

        if (property.StartsWith("overflow", StringComparison.Ordinal))
        {
            return "Overflow";
        }

        if (property is "display" or "float" or "clear" or "order"
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
