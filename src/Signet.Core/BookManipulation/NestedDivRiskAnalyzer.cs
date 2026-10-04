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
/// The consequences of collapsing one <see cref="NestedDivChain"/> (<see cref="NestedDivCollapser"/>) — what the
/// user has to accept before the chain is collapsed:
/// <list type="bullet">
/// <item>comments and CDATA sections between the chain's tags (kept, but moved inside the remaining
/// <c>&lt;div&gt;</c>);</item>
/// <item>styles of the removed <c>&lt;div&gt;</c>s that add up with nesting — a non-inherited property such as
/// <c>margin</c> or <c>padding</c> applied to every level, or an inherited one with a relative value
/// (<c>font-size: 1.2em</c>) — and inherited styles the content gets from an inner <c>&lt;div&gt;</c> only
/// (e.g. from <c>.a .a { color: red }</c>);</item>
/// <item>changes of the winning value of any property on the remaining <c>&lt;div&gt;</c> or on its content,
/// because a selector depends on the nesting (<c>.a &gt; .a</c>, <c>.a .a p</c>, <c>:only-child</c>…) — found by
/// computing the cascade of every such element before and after the collapse.</item>
/// </list>
/// The cascade uses the stylesheets the file sees, its <c>&lt;style&gt;</c> blocks and the <c>style</c> attributes;
/// rules inside <c>@media</c> are treated as applying (the consequence names the condition).
/// </summary>
internal static class NestedDivRiskAnalyzer
{
    private const int MaxConsequences = 20;
    private const int MaxSeparatorLength = 60;

    private static readonly Regex RelativeValueRegex = new(@"\d(?:em|ex|%)(?![a-z])|\b(?:larger|smaller)\b", RegexOptions.IgnoreCase | RegexOptions.Compiled);
    private static readonly Regex ZeroLengthRegex = new(@"^[+-]?(?:0+\.?0*|\.0+)(?:[a-z]+|%)?$", RegexOptions.IgnoreCase | RegexOptions.Compiled);

    private static readonly HashSet<string> NeutralKeywords = new(StringComparer.OrdinalIgnoreCase)
    {
        "none", "transparent", "auto", "normal", "initial", "unset", "static", "visible",
    };

    /// <summary>
    /// The consequences of collapsing <paramref name="chain"/> in <paramref name="text"/> (the file
    /// <paramref name="htmlBookPath"/>); empty when the collapse is safe. <paramref name="stylesheets"/> are the
    /// parsed stylesheets the file sees, in cascade order.
    /// </summary>
    public static IReadOnlyList<CleanupConsequence> Analyse(
        string htmlBookPath, string text, NestedDivChain chain, IReadOnlyList<CssInfo> stylesheets)
    {
        List<CleanupConsequence> consequences = chain.Separators
            .Select(s => new CleanupConsequence(
                CoreStrings.Format(s.IsCData ? "Cleanup_Risk_DivCData" : "Cleanup_Risk_DivComment", Shorten(s.Text)),
                htmlBookPath,
                s.Pos))
            .ToList();

        string? collapsed = NestedDivCollapser.Collapse(text, chain.OuterPos);
        if (collapsed is null)
        {
            return consequences;
        }

        IHtmlDocument before = XhtmlDoc.Parse(text);
        IHtmlDocument after = XhtmlDoc.Parse(collapsed);
        List<IElement> divs = ChainElements(before, chain);
        IElement? kept = ElementAt(after, chain.OuterPos);
        if (divs.Count != chain.Depth || kept is null)
        {
            return consequences;
        }

        List<(CssRule Rule, int Sequence)> rules = stylesheets
            .Concat(new HtmlStyleInfo(text).Styles)
            .SelectMany(info => info.Rules)
            .Where(r => !string.IsNullOrWhiteSpace(r.SelectorText))
            .Select((rule, i) => (rule, i))
            .ToList();

        string file = htmlBookPath[(htmlBookPath.LastIndexOf('/') + 1)..];
        List<string> found = new();
        Dictionary<string, Winner> keptWinners = Winners(rules, kept);

        // Styles of the removed levels that add up, or that only the inner levels pass on to the content.
        Dictionary<string, Winner>[] levelWinners = divs.Select(d => Winners(rules, d)).ToArray();
        HashSet<string> reported = new(StringComparer.OrdinalIgnoreCase);
        for (int level = 1; level < divs.Count; level++)
        {
            foreach ((string property, Winner winner) in levelWinners[level])
            {
                if (!reported.Add($"{property}\u0001{winner.Value}"))
                {
                    continue;
                }

                bool inherited = CssInheritedProperties.IsInherited(property);
                if (!inherited ? !IsNeutral(property, winner.Value) : RelativeValueRegex.IsMatch(winner.Value))
                {
                    int count = levelWinners.Count(w => w.TryGetValue(property, out Winner? other) && other.Value == winner.Value);
                    found.Add(CoreStrings.Format(
                        "Cleanup_Risk_DivRepeated", file, CssCascadeResolver.DescribeElement(divs[level]), property,
                        winner.Value, winner.Selector, count) + ConditionText(winner));
                }
                else if (inherited && !(keptWinners.TryGetValue(property, out Winner? keptWinner) && keptWinner.Value == winner.Value))
                {
                    found.Add(CoreStrings.Format(
                        "Cleanup_Risk_DivInherited", file, CssCascadeResolver.DescribeElement(divs[level]), property,
                        winner.Value, winner.Selector, keptWinner?.Value ?? CoreStrings.Get("Cleanup_Risk_NoValue")) + ConditionText(winner));
                }
            }
        }

        // Selectors that depend on the nesting: the remaining div and its content before and after.
        List<IElement> beforeElements = new List<IElement> { divs[0] }.Concat(divs[^1].QuerySelectorAll("*")).ToList();
        List<IElement> afterElements = new List<IElement> { kept }.Concat(kept.QuerySelectorAll("*")).ToList();
        Dictionary<string, (IElement Element, string Property, Winner? Was, Winner? Will, int Count)> changes = new(StringComparer.Ordinal);
        if (beforeElements.Count == afterElements.Count)
        {
            for (int i = 0; i < beforeElements.Count; i++)
            {
                Dictionary<string, Winner> was = Winners(rules, beforeElements[i]);
                Dictionary<string, Winner> will = Winners(rules, afterElements[i]);
                foreach (string property in was.Keys.Union(will.Keys, StringComparer.OrdinalIgnoreCase))
                {
                    was.TryGetValue(property, out Winner? oldWinner);
                    will.TryGetValue(property, out Winner? newWinner);
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
                ConditionText(change.Will ?? change.Was)));
        }

        consequences.AddRange(found.Take(MaxConsequences).Select(t => new CleanupConsequence(t, htmlBookPath, chain.OuterPos)));
        if (found.Count > MaxConsequences)
        {
            consequences.Add(new CleanupConsequence(
                CoreStrings.Format("Cleanup_Risk_MoreChanges", found.Count - MaxConsequences), htmlBookPath, chain.OuterPos));
        }

        return consequences;
    }

    private sealed record Winner(string Value, string Selector, string? Condition, bool Important, CssSpecificity Specificity, int Sequence);

    private static string ConditionText(Winner? winner) =>
        winner?.Condition is { } prelude ? CoreStrings.Format("Cleanup_Risk_Condition", prelude) : string.Empty;

    // The divs of the chain: the outermost one and its only element child, level by level.
    private static List<IElement> ChainElements(IHtmlDocument document, NestedDivChain chain)
    {
        List<IElement> divs = new();
        IElement? current = ElementAt(document, chain.OuterPos);
        while (current is not null && divs.Count < chain.Depth)
        {
            divs.Add(current);
            current = current.Children.Length == 1 ? current.Children[0] : null;
        }

        return divs;
    }

    private static IElement? ElementAt(IHtmlDocument document, int offset) =>
        document.All.FirstOrDefault(e => XhtmlDoc.OffsetFromNode(e) == offset);

    // The winning declaration of each property on the element: !important first, then specificity, then source
    // order; the style attribute beats selector rules within the same importance.
    private static Dictionary<string, Winner> Winners(List<(CssRule Rule, int Sequence)> rules, IElement element)
    {
        Dictionary<string, Winner> winners = new(StringComparer.OrdinalIgnoreCase);

        void Offer(CssDeclaration declaration, string selector, string? condition, CssSpecificity specificity, int sequence)
        {
            string property = declaration.Property.Trim();
            Winner candidate = new(declaration.Value.Trim(), selector, condition, declaration.IsImportant, specificity, sequence);
            if (!winners.TryGetValue(property, out Winner? current))
            {
                winners[property] = candidate;
                return;
            }

            bool beats;
            if (candidate.Important != current.Important)
            {
                beats = candidate.Important;
            }
            else
            {
                int bySpecificity = specificity.CompareTo(current.Specificity);
                // Declarations are offered in source order, so a later one wins a tie (also within one rule).
                beats = bySpecificity != 0 ? bySpecificity > 0 : sequence >= current.Sequence;
            }

            if (beats)
            {
                winners[property] = candidate;
            }
        }

        foreach ((CssRule rule, int sequence) in rules)
        {
            CssSpecificity? specificity = null;
            string? matched = null;
            foreach (string raw in rule.Selectors)
            {
                string selector = raw.Trim();
                if (selector.Length == 0 || CssSelectorMatching.TryMatch(element, selector) != true)
                {
                    continue;
                }

                CssSpecificity candidate = CssSpecificity.ForSelector(selector);
                if (specificity is null || candidate.CompareTo(specificity.Value) > 0)
                {
                    specificity = candidate;
                    matched = selector;
                }
            }

            if (specificity is { } spec)
            {
                foreach (CssDeclaration declaration in rule.Declarations)
                {
                    Offer(declaration, matched!, rule.AtRulePrelude, spec, sequence);
                }
            }
        }

        string? style = element.GetAttribute("style");
        if (!string.IsNullOrWhiteSpace(style) && new CssInfo("x{" + style + "}").Rules is { Count: > 0 } inline)
        {
            foreach (CssDeclaration declaration in inline[0].Declarations)
            {
                Offer(declaration, "style=\"…\"", null, CssSpecificity.Inline, int.MaxValue);
            }
        }

        return winners;
    }

    // A value of a non-inherited property that does nothing on a div, so repeating it does not add up.
    private static bool IsNeutral(string property, string value)
    {
        string v = value.Replace("!important", string.Empty, StringComparison.OrdinalIgnoreCase).Trim();
        if (NeutralKeywords.Contains(v)
            || (string.Equals(property, "display", StringComparison.OrdinalIgnoreCase) && string.Equals(v, "block", StringComparison.OrdinalIgnoreCase)))
        {
            return true;
        }

        string[] tokens = v.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
        return tokens.Length > 0 && tokens.All(t => ZeroLengthRegex.IsMatch(t) || NeutralKeywords.Contains(t));
    }

    private static string Shorten(string text)
    {
        string single = Regex.Replace(text, @"\s+", " ").Trim();
        return single.Length <= MaxSeparatorLength ? single : single[..MaxSeparatorLength] + "…";
    }
}
