using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using AngleSharp.Dom;
using AngleSharp.Html.Dom;
using Signet.Core.Localization;
using Signet.Core.Parsers;

namespace Signet.Core.BookManipulation;

/// <summary>
/// The consequences of merging one <see cref="AdjacentDivGroup"/> (<see cref="AdjacentDivMerger"/>) — what the user
/// has to accept before the <c>&lt;div&gt;</c>s are merged. Only real changes are reported, not possible ones:
/// <list type="bullet">
/// <item>a CDATA section with text between the <c>&lt;div&gt;</c>s (the text moves into the merged one); comments and
/// blank CDATA sections render nothing, so they are not reported;</item>
/// <item>an <c>id</c> on the <c>&lt;div&gt;</c>s that something in the book really refers to
/// (<see cref="IdReferenceIndex"/>) — after merging only one element is left;</item>
/// <item>styles under which separate boxes look different from one box: a vertical margin, padding or border at a
/// joint (the bottom of a <c>&lt;div&gt;</c> followed by another, the top of one that follows another), a full
/// <c>border</c>/<c>outline</c>, a fixed height, a background image, rounded corners of a painted box, a forced page
/// break, a different <c>display</c>, floats, counters and the like (neutral values do not count). Horizontal
/// margins, paddings and borders, widths and a background colour look the same on one box, so they are not
/// reported;</item>
/// <item>inline content (text, inline elements) at both sides of a joint — it no longer starts on a new line and runs
/// together;</item>
/// <item>changes of the winning value of any property on the merged <c>&lt;div&gt;</c>, its content or the following
/// siblings, because a selector depends on the structure (<c>:first-child</c>, <c>:nth-child</c>, <c>+</c>,
/// <c>~</c>…) — found by computing the cascade of those elements before and after the merge.</item>
/// </list>
/// The cascade is the one of <see cref="NestedDivRiskAnalyzer"/> (<c>@media</c> rules count as applying).
/// </summary>
internal static class AdjacentDivRiskAnalyzer
{
    private const int MaxConsequences = 20;
    private const int MaxSeparatorLength = 60;

    // Elements that start on a new line on their own, so content next to them does not run together.
    private static readonly HashSet<string> BlockElements = new(StringComparer.OrdinalIgnoreCase)
    {
        "address", "article", "aside", "blockquote", "details", "dialog", "div", "dl", "dd", "dt", "fieldset",
        "figcaption", "figure", "footer", "form", "h1", "h2", "h3", "h4", "h5", "h6", "header", "hgroup", "hr", "li",
        "main", "nav", "ol", "p", "pre", "section", "table", "ul", "summary",
    };

    // Properties that look the same on the separate boxes and on the merged one (for block boxes stacked without a
    // gap — a vertical gap comes from a margin at a joint, which is reported on its own).
    private static readonly HashSet<string> SameOnOneBox = new(StringComparer.OrdinalIgnoreCase)
    {
        "margin-left", "margin-right", "margin-inline", "margin-inline-start", "margin-inline-end",
        "padding-left", "padding-right", "padding-inline", "padding-inline-start", "padding-inline-end",
        "border-left", "border-left-width", "border-left-style", "border-left-color",
        "border-right", "border-right-width", "border-right-style", "border-right-color",
        "border-inline", "border-inline-start", "border-inline-end", "border-inline-width", "border-inline-style",
        "border-inline-color", "width", "min-width", "max-width", "inline-size", "min-inline-size", "max-inline-size",
        "background-color", "box-sizing",
    };

    private static readonly HashSet<string> TopSide = new(StringComparer.OrdinalIgnoreCase)
    {
        "margin-top", "padding-top", "border-top", "border-top-width", "border-top-style", "border-top-color",
        "margin-block-start", "padding-block-start", "border-block-start",
    };

    private static readonly HashSet<string> BottomSide = new(StringComparer.OrdinalIgnoreCase)
    {
        "margin-bottom", "padding-bottom", "border-bottom", "border-bottom-width", "border-bottom-style",
        "border-bottom-color", "margin-block-end", "padding-block-end", "border-block-end",
    };

    // Shorthands with 1–4 values (top right bottom left).
    private static readonly HashSet<string> FourSides = new(StringComparer.OrdinalIgnoreCase)
    {
        "margin", "padding", "border-width", "border-style", "border-color",
    };

    // Shorthands with 1–2 values (block start, block end).
    private static readonly HashSet<string> BlockSides = new(StringComparer.OrdinalIgnoreCase)
    {
        "margin-block", "padding-block", "border-block-width", "border-block-style", "border-block-color",
    };

    /// <summary>
    /// The consequences of merging <paramref name="group"/> in <paramref name="text"/> (the file
    /// <paramref name="htmlBookPath"/>); empty when the merge is safe. <paramref name="stylesheets"/> are the parsed
    /// stylesheets the file sees, in cascade order; <paramref name="isIdReferenced"/> tells whether an <c>id</c> in the
    /// file is referred to.
    /// </summary>
    public static IReadOnlyList<CleanupConsequence> Analyse(
        string htmlBookPath, string text, AdjacentDivGroup group, IReadOnlyList<CssInfo> stylesheets, Func<string, bool> isIdReferenced)
    {
        ArgumentNullException.ThrowIfNull(isIdReferenced);
        List<CleanupConsequence> consequences = group.Separators
            .Where(s => s.IsCData && !string.IsNullOrWhiteSpace(CDataContent(s.Text)))
            .Select(s => new CleanupConsequence(CoreStrings.Format("Cleanup_Risk_DivCData", Shorten(s.Text)), htmlBookPath, s.Pos))
            .ToList();

        string file = htmlBookPath[(htmlBookPath.LastIndexOf('/') + 1)..];
        IHtmlDocument before = XhtmlDoc.Parse(text);
        List<IElement> divs = group.DivPositions
            .Select(p => NestedDivRiskAnalyzer.ElementAt(before, p))
            .OfType<IElement>()
            .ToList();
        if (divs.Count != group.Count)
        {
            return consequences;
        }

        if (divs[0].GetAttribute("id") is { Length: > 0 } id && isIdReferenced(id))
        {
            consequences.Add(new CleanupConsequence(CoreStrings.Format("Cleanup_Risk_MergeDivId", file, id), htmlBookPath, group.FirstPos));
        }

        List<(CssRule Rule, int Sequence)> rules = NestedDivRiskAnalyzer.CascadeRules(text, stylesheets);
        List<string> found = new();

        // Styles under which the separate boxes look different from one box.
        HashSet<string> reported = new(StringComparer.OrdinalIgnoreCase);
        for (int k = 0; k < divs.Count; k++)
        {
            IElement div = divs[k];
            Dictionary<string, NestedDivRiskAnalyzer.Winner> winners = NestedDivRiskAnalyzer.Winners(rules, div);
            bool painted = winners.Any(w => IsPaint(w.Key) && !NestedDivRiskAnalyzer.IsNeutral(w.Key, w.Value.Value));
            foreach ((string property, NestedDivRiskAnalyzer.Winner winner) in winners)
            {
                if (CssInheritedProperties.IsInherited(property)
                    || NestedDivRiskAnalyzer.IsNeutral(property, winner.Value)
                    || !ChangesWhenMerged(property, winner.Value, topJoint: k > 0, bottomJoint: k < divs.Count - 1, painted)
                    || !reported.Add($"{property}\u0001{winner.Value}"))
                {
                    continue;
                }

                found.Add(CoreStrings.Format(
                    "Cleanup_Risk_MergeDivBox", file, CssCascadeResolver.DescribeElement(div), property, winner.Value,
                    winner.Selector, group.Count) + NestedDivRiskAnalyzer.ConditionText(winner));
            }
        }

        // Inline content on both sides of a joint runs together.
        for (int k = 0; k < divs.Count - 1; k++)
        {
            if (EndsInline(divs[k]) && StartsInline(divs[k + 1]))
            {
                found.Add(CoreStrings.Format("Cleanup_Risk_MergeDivInline", file, k + 1, k + 2));
            }
        }

        if (AdjacentDivMerger.Merge(text, group.FirstPos) is { } merged)
        {
            found.AddRange(StructuralChanges(file, rules, divs, XhtmlDoc.Parse(merged), group.FirstPos));
        }

        consequences.AddRange(found.Take(MaxConsequences).Select(t => new CleanupConsequence(t, htmlBookPath, group.FirstPos)));
        if (found.Count > MaxConsequences)
        {
            consequences.Add(new CleanupConsequence(
                CoreStrings.Format("Cleanup_Risk_MoreChanges", found.Count - MaxConsequences), htmlBookPath, group.FirstPos));
        }

        return consequences;
    }

    // The winners that change on the merged div, its content and the following siblings (the elements a structural
    // selector can reach), comparing the elements before and after the merge in document order. Inherited values are
    // not computed, so a change on a div shows up there and is not repeated for its content.
    private static List<string> StructuralChanges(
        string file, List<(CssRule Rule, int Sequence)> rules, List<IElement> divs, IHtmlDocument after, int firstPos)
    {
        List<string> found = new();
        IElement? kept = NestedDivRiskAnalyzer.ElementAt(after, firstPos);
        if (kept is null)
        {
            return found;
        }

        // Every div of the run becomes the kept one (so a style only the later divs had, e.g. from ".a + .a", and the
        // content inherits, is found), the contents follow in order, then the following siblings.
        List<IElement> beforeElements = divs
            .Concat(divs.SelectMany(d => d.QuerySelectorAll("*")))
            .Concat(FollowingSiblings(divs[^1]))
            .ToList();
        List<IElement> afterElements = Enumerable.Repeat(kept, divs.Count)
            .Concat(kept.QuerySelectorAll("*"))
            .Concat(FollowingSiblings(kept))
            .ToList();
        if (beforeElements.Count != afterElements.Count)
        {
            return found;
        }

        Dictionary<string, (IElement Element, string Property, NestedDivRiskAnalyzer.Winner? Was, NestedDivRiskAnalyzer.Winner? Will, int Count)> changes =
            new(StringComparer.Ordinal);
        for (int i = 0; i < beforeElements.Count; i++)
        {
            Dictionary<string, NestedDivRiskAnalyzer.Winner> was = NestedDivRiskAnalyzer.Winners(rules, beforeElements[i]);
            Dictionary<string, NestedDivRiskAnalyzer.Winner> will = NestedDivRiskAnalyzer.Winners(rules, afterElements[i]);
            foreach (string property in was.Keys.Union(will.Keys, StringComparer.OrdinalIgnoreCase))
            {
                // The merged box takes its top edge from the first div and its bottom edge from the last one; an edge
                // at a joint disappears and is judged by the box check, not here.
                if (i < divs.Count && ((i > 0 && TopSide.Contains(property)) || (i < divs.Count - 1 && BottomSide.Contains(property))))
                {
                    continue;
                }

                was.TryGetValue(property, out NestedDivRiskAnalyzer.Winner? oldWinner);
                will.TryGetValue(property, out NestedDivRiskAnalyzer.Winner? newWinner);
                if (string.Equals(oldWinner?.Value, newWinner?.Value, StringComparison.Ordinal))
                {
                    continue;
                }

                string key = $"{property}\u0001{oldWinner?.Value}\u0001{newWinner?.Value}\u0001{newWinner?.Selector}";
                changes[key] = changes.TryGetValue(key, out var existing)
                    ? existing with { Count = existing.Count + 1 }
                    : (beforeElements[i], property, oldWinner, newWinner, 1);
            }
        }

        foreach (var change in changes.Values)
        {
            string more = change.Count > 1 ? CoreStrings.Format("Cleanup_Risk_MoreElements", change.Count - 1) : string.Empty;
            found.Add(CoreStrings.Format(
                "Cleanup_Risk_ValueChange",
                file,
                CssCascadeResolver.DescribeElement(change.Element),
                more,
                change.Property,
                change.Was?.Value ?? CoreStrings.Get("Cleanup_Risk_NoValue"),
                change.Will?.Value ?? CoreStrings.Get("Cleanup_Risk_NoValue"),
                change.Will?.Selector ?? CoreStrings.Get("Cleanup_Risk_NoValue"),
                NestedDivRiskAnalyzer.ConditionText(change.Will ?? change.Was)));
        }

        return found;
    }

    // Whether a non-neutral, non-inherited declaration on a div of the run makes the merged box look different.
    private static bool ChangesWhenMerged(string property, string value, bool topJoint, bool bottomJoint, bool painted)
    {
        if (SameOnOneBox.Contains(property))
        {
            return false;
        }

        if (TopSide.Contains(property))
        {
            return topJoint;
        }

        if (BottomSide.Contains(property))
        {
            return bottomJoint;
        }

        string[] tokens = value.Replace("!important", string.Empty, StringComparison.OrdinalIgnoreCase)
            .Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
        if (FourSides.Contains(property) && tokens.Length is >= 1 and <= 4)
        {
            string top = tokens[0];
            string bottom = tokens.Length >= 3 ? tokens[2] : tokens[0];
            return (topJoint && !NestedDivRiskAnalyzer.IsNeutral(property, top))
                || (bottomJoint && !NestedDivRiskAnalyzer.IsNeutral(property, bottom));
        }

        if (BlockSides.Contains(property) && tokens.Length is 1 or 2)
        {
            string start = tokens[0];
            string end = tokens[^1];
            return (topJoint && !NestedDivRiskAnalyzer.IsNeutral(property, start))
                || (bottomJoint && !NestedDivRiskAnalyzer.IsNeutral(property, end));
        }

        if (property.Equals("background", StringComparison.OrdinalIgnoreCase))
        {
            return HasImage(value);
        }

        if (property.StartsWith("border-", StringComparison.OrdinalIgnoreCase) && property.EndsWith("radius", StringComparison.OrdinalIgnoreCase))
        {
            return painted;
        }

        // Anything else (border, outline, height, display, float, page breaks, counters, box-shadow…), and every
        // property not known to be safe, counts.
        return true;
    }

    // Declarations that paint the box, so its rounded corners show.
    private static bool IsPaint(string property) =>
        property.StartsWith("background", StringComparison.OrdinalIgnoreCase)
        || (property.StartsWith("border", StringComparison.OrdinalIgnoreCase) && !property.EndsWith("radius", StringComparison.OrdinalIgnoreCase))
        || property.StartsWith("outline", StringComparison.OrdinalIgnoreCase)
        || property.Equals("box-shadow", StringComparison.OrdinalIgnoreCase);

    // A background image or gradient is laid out on each box, so it looks different on one box.
    private static bool HasImage(string value) =>
        value.Contains("url(", StringComparison.OrdinalIgnoreCase) || value.Contains("gradient(", StringComparison.OrdinalIgnoreCase);

    private static string CDataContent(string cdata) =>
        cdata.StartsWith("<![CDATA[", StringComparison.Ordinal) && cdata.EndsWith("]]>", StringComparison.Ordinal)
            ? cdata["<![CDATA[".Length..^"]]>".Length]
            : cdata;

    private static IEnumerable<IElement> FollowingSiblings(IElement element)
    {
        for (IElement? next = element.NextElementSibling; next is not null; next = next.NextElementSibling)
        {
            yield return next;
        }
    }

    private static bool EndsInline(IElement div) => IsInline(Significant(div.ChildNodes).LastOrDefault());

    private static bool StartsInline(IElement div) => IsInline(Significant(div.ChildNodes).FirstOrDefault());

    // The child nodes that render: non-blank text and elements (comments and whitespace-only text are skipped).
    private static IEnumerable<INode> Significant(INodeList nodes) =>
        nodes.Where(n => n is IElement || (n.NodeType == NodeType.Text && !string.IsNullOrWhiteSpace(n.TextContent)));

    private static bool IsInline(INode? node) => node switch
    {
        null => false,
        IElement element => !BlockElements.Contains(element.LocalName),
        _ => true,
    };

    private static string Shorten(string text)
    {
        string single = Regex.Replace(text, @"\s+", " ").Trim();
        return single.Length <= MaxSeparatorLength ? single : single[..MaxSeparatorLength] + "…";
    }
}
