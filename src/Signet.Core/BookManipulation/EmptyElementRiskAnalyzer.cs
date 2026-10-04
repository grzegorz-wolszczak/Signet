using System;
using System.Collections.Generic;
using System.Linq;
using AngleSharp.Dom;
using AngleSharp.Html.Dom;
using Signet.Core.Localization;
using Signet.Core.Parsers;

namespace Signet.Core.BookManipulation;

/// <summary>
/// The consequences of removing an <see cref="EmptyElement"/> — the CSS declarations (from the stylesheets the file
/// sees, its <c>&lt;style&gt;</c> blocks and the <c>style</c> attribute) that make even an empty element do
/// something: take space (<c>height</c>, <c>min-height</c>, <c>padding</c>, <c>margin</c>, a visible
/// <c>border</c>/<c>outline</c>), break the page (<c>page-break-*</c>/<c>break-*</c>), end floats
/// (<c>clear</c>), float, change its display, or generate content (<c>content</c> of a <c>::before</c>/
/// <c>::after</c> rule — pseudo-elements are stripped when matching, so their declarations count for the element).
/// The cascade is the one of <see cref="NestedDivRiskAnalyzer"/> (<c>@media</c> rules count as applying).
/// The browser's default margins of <c>&lt;p&gt;</c> are not considered (they collapse with the neighbours'
/// margins).
/// </summary>
internal static class EmptyElementRiskAnalyzer
{
    private static readonly HashSet<string> ForcedBreaks = new(StringComparer.OrdinalIgnoreCase)
    {
        "always", "page", "left", "right", "recto", "verso", "column",
    };

    /// <summary>
    /// The consequences of removing each of <paramref name="elements"/> (found in <paramref name="text"/>, the file
    /// <paramref name="htmlBookPath"/>), in the same order; an empty list means the removal is safe.
    /// </summary>
    public static IReadOnlyList<IReadOnlyList<CleanupConsequence>> Analyse(
        string htmlBookPath, string text, IReadOnlyList<EmptyElement> elements, IReadOnlyList<CssInfo> stylesheets)
    {
        if (elements.Count == 0)
        {
            return Array.Empty<IReadOnlyList<CleanupConsequence>>();
        }

        IHtmlDocument document = XhtmlDoc.Parse(text);
        List<(CssRule Rule, int Sequence)> rules = NestedDivRiskAnalyzer.CascadeRules(text, stylesheets);
        string file = htmlBookPath[(htmlBookPath.LastIndexOf('/') + 1)..];

        List<IReadOnlyList<CleanupConsequence>> result = new();
        foreach (EmptyElement empty in elements)
        {
            IElement? element = NestedDivRiskAnalyzer.ElementAt(document, empty.Pos);
            if (element is null)
            {
                result.Add(Array.Empty<CleanupConsequence>());
                continue;
            }

            result.Add(NestedDivRiskAnalyzer.Winners(rules, element)
                .Where(w => HasEffect(empty.TagName, w.Key, w.Value.Value))
                .Select(w => new CleanupConsequence(
                    CoreStrings.Format(
                        "Cleanup_Risk_EmptyStyle", file, CssCascadeResolver.DescribeElement(element), w.Key, w.Value.Value,
                        w.Value.Selector, NestedDivRiskAnalyzer.ConditionText(w.Value)),
                    htmlBookPath,
                    empty.Pos))
                .ToList());
        }

        return result;
    }

    // Whether the declaration makes an empty element of this kind visible or change the layout.
    private static bool HasEffect(string tagName, string property, string rawValue)
    {
        string value = rawValue.Replace("!important", string.Empty, StringComparison.OrdinalIgnoreCase).Trim();
        string p = property.ToLowerInvariant();
        if (p is "content")
        {
            return !value.Equals("none", StringComparison.OrdinalIgnoreCase) && !value.Equals("normal", StringComparison.OrdinalIgnoreCase);
        }

        if (p is "clear" or "float")
        {
            return !value.Equals("none", StringComparison.OrdinalIgnoreCase);
        }

        if (p is "display")
        {
            string natural = tagName == "span" ? "inline" : "block";
            return !value.Equals("none", StringComparison.OrdinalIgnoreCase) && !value.Equals(natural, StringComparison.OrdinalIgnoreCase);
        }

        if (p.StartsWith("page-break-", StringComparison.Ordinal) || p is "break-before" or "break-after")
        {
            return ForcedBreaks.Contains(value);
        }

        bool takesSpace = p.StartsWith("margin", StringComparison.Ordinal)
            || p.StartsWith("padding", StringComparison.Ordinal)
            || p.StartsWith("outline", StringComparison.Ordinal)
            || (p.StartsWith("border", StringComparison.Ordinal) && p is not ("border-collapse" or "border-spacing") && !p.Contains("radius", StringComparison.Ordinal))
            || p is "height" or "min-height";
        return takesSpace && !NestedDivRiskAnalyzer.IsNeutral(property, value);
    }
}
