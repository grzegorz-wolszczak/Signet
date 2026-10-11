using System;
using System.Linq;
using AngleSharp.Dom;

namespace Signet.Core.Footnotes;

/// <summary>
/// When the link back of a note returns to a reference — the rule shared by <see cref="FootnoteScanner"/> and
/// <c>FootnoteValidator</c>, so that both see the same pairs.
/// </summary>
internal static class FootnoteLinks
{
    // The elements that end a run of text: an empty element right before a reference must be in the same one.
    private static readonly string[] BlockNames =
    {
        "p", "div", "li", "dd", "dt", "td", "th", "h1", "h2", "h3", "h4", "h5", "h6", "section", "aside",
        "blockquote", "body",
    };

    /// <summary>
    /// Whether a link back to <paramref name="target"/> returns to the reference <paramref name="anchor"/>: the target
    /// is the reference link, an element around it (<c>&lt;sup id&gt;</c>, <c>&lt;span id&gt;</c>) or inside it, or an
    /// empty element right before it in the same block with no text in between — e.g. InDesign's
    /// <c>&lt;span id="footnote-1-backlink"&gt;&lt;/span&gt;</c> placed just before the reference.
    /// </summary>
    public static bool LeadsTo(IElement target, IElement anchor) =>
        ReferenceEquals(target, anchor)
        || target.Contains(anchor)
        || anchor.Contains(target)
        || (string.IsNullOrWhiteSpace(target.TextContent) && IsRightBefore(target, anchor));

    // Whether only tags and whitespace stand between the end of `target` and `anchor`, both in the same block.
    private static bool IsRightBefore(IElement target, IElement anchor)
    {
        if (BlockOf(target) is not { } block || !ReferenceEquals(block, BlockOf(anchor)))
        {
            return false;
        }

        for (INode? node = NextSkippingChildren(target); node is not null && block.Contains(node); node = Next(node))
        {
            if (ReferenceEquals(node, anchor))
            {
                return true;
            }

            if (node is IText text && !string.IsNullOrWhiteSpace(text.Data))
            {
                return false;
            }
        }

        return false;
    }

    private static IElement? BlockOf(IElement element)
    {
        for (IElement? parent = element.ParentElement; parent is not null; parent = parent.ParentElement)
        {
            if (BlockNames.Contains(parent.LocalName, StringComparer.OrdinalIgnoreCase))
            {
                return parent;
            }
        }

        return null;
    }

    // The next node in document order, into the children first.
    private static INode? Next(INode node) => node.FirstChild ?? NextSkippingChildren(node);

    // The next node in document order after the node and its children.
    private static INode? NextSkippingChildren(INode node)
    {
        for (INode? current = node; current is not null; current = current.Parent)
        {
            if (current.NextSibling is { } sibling)
            {
                return sibling;
            }
        }

        return null;
    }
}
