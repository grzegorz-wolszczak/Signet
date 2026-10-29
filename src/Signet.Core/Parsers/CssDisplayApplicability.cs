using System;
using System.Collections.Frozen;

namespace Signet.Core.Parsers;

[Flags]
public enum CssDisplayContext
{
    None = 0,
    Inline = 1,
    Block = 2,
    InlineBlock = 4,
    Flex = 8,
    Grid = 16,
    Table = 32,
}

/// <summary>Practical display-context applicability for properties in <see cref="EpubCssPropertyCatalog"/>.</summary>
/// <remarks>
/// A context is marked when a property can have a direct effect on that display type or its layout items.
/// Inline, block, and inline-block contexts include replaced elements. Flex, grid, and table include containers
/// and items. <see cref="CssDisplayContext.None"/> marks properties needing another display type.
/// </remarks>
public static class CssDisplayApplicability
{
    private const CssDisplayContext AllContexts =
        CssDisplayContext.Inline
        | CssDisplayContext.Block
        | CssDisplayContext.InlineBlock
        | CssDisplayContext.Flex
        | CssDisplayContext.Grid
        | CssDisplayContext.Table;

    private const CssDisplayContext BlockFlexGridContexts =
        CssDisplayContext.Block | CssDisplayContext.InlineBlock | CssDisplayContext.Flex | CssDisplayContext.Grid;

    private const CssDisplayContext TextContainerContexts =
        BlockFlexGridContexts | CssDisplayContext.Table;

    private const CssDisplayContext FragmentedContexts =
        TextContainerContexts;

    private const CssDisplayContext MultiColumnContexts =
        BlockFlexGridContexts;

    private static readonly FrozenSet<string> KnownProperties =
        EpubCssPropertyCatalog.AllProperties.ToFrozenSet(StringComparer.OrdinalIgnoreCase);

    private static readonly FrozenSet<string> TableProperties = Set(
        "border-collapse", "border-spacing", "caption-side", "empty-cells", "table-layout");

    private static readonly FrozenSet<string> FlexGridProperties = Set(
        "align-items", "align-self", "justify-content", "order", "place-items", "place-self", "row-gap");

    private static readonly FrozenSet<string> BlockFlexGridProperties = Set(
        "align-content", "gap", "place-content");

    private static readonly FrozenSet<string> TextContainerProperties = Set(
        "-webkit-line-clamp", "initial-letter", "line-clamp", "orphans", "text-align", "text-align-last",
        "text-box", "text-box-edge", "text-box-trim", "text-indent", "text-justify", "text-overflow", "widows");

    private static readonly FrozenSet<string> FragmentedProperties = Set(
        "-webkit-column-break-after", "-webkit-column-break-before", "-webkit-column-break-inside",
        "break-after", "break-before", "break-inside",
        "page-break-after", "page-break-before", "page-break-inside");

    private static readonly FrozenSet<string> RubyProperties = Set(
        "-epub-ruby-position", "-webkit-ruby-position", "ruby-align", "ruby-overhang", "ruby-position");

    private static readonly FrozenSet<string> ListStyleProperties = Set(
        "list-style", "list-style-image", "list-style-position", "list-style-type");

    private static FrozenSet<string> Set(params string[] properties) =>
        properties.ToFrozenSet(StringComparer.OrdinalIgnoreCase);

    /// <summary>Returns the display contexts where <paramref name="property"/> can have an effect.</summary>
    public static CssDisplayContext ForProperty(string property)
    {
        ArgumentNullException.ThrowIfNull(property);
        if (!KnownProperties.Contains(property))
        {
            return CssDisplayContext.None;
        }

        if (TableProperties.Contains(property))
        {
            return CssDisplayContext.Table;
        }

        if (property.Equals("flex", StringComparison.OrdinalIgnoreCase)
            || property.StartsWith("flex-", StringComparison.OrdinalIgnoreCase))
        {
            return CssDisplayContext.Flex;
        }

        if (property.Equals("grid", StringComparison.OrdinalIgnoreCase)
            || property.StartsWith("grid-", StringComparison.OrdinalIgnoreCase))
        {
            return CssDisplayContext.Grid;
        }

        if (FlexGridProperties.Contains(property))
        {
            return CssDisplayContext.Flex | CssDisplayContext.Grid;
        }

        if (BlockFlexGridProperties.Contains(property))
        {
            return BlockFlexGridContexts;
        }

        if (TextContainerProperties.Contains(property))
        {
            return TextContainerContexts;
        }

        if (FragmentedProperties.Contains(property))
        {
            return FragmentedContexts;
        }

        if (property.Equals("vertical-align", StringComparison.OrdinalIgnoreCase))
        {
            return CssDisplayContext.Inline | CssDisplayContext.InlineBlock | CssDisplayContext.Table;
        }

        if (property.Equals("clear", StringComparison.OrdinalIgnoreCase))
        {
            return CssDisplayContext.Block;
        }

        if (ListStyleProperties.Contains(property))
        {
            return CssDisplayContext.Flex | CssDisplayContext.Grid;
        }

        if (RubyProperties.Contains(property))
        {
            return CssDisplayContext.None;
        }

        if (property.Equals("columns", StringComparison.OrdinalIgnoreCase)
            || property.StartsWith("column-", StringComparison.OrdinalIgnoreCase))
        {
            return MultiColumnContexts;
        }

        return AllContexts;
    }
}
