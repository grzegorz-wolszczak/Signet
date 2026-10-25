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
/// The consequences of removing an attribute that only repeats an inherited value (the Cleanup step behind
/// <see cref="InheritedAttributeCleaner"/>). The value the element has does not change — that is what "inherited"
/// means — but a stylesheet can reach the element <b>through the attribute itself</b> (<c>[lang]</c>,
/// <c>p[lang="pl-PL"]</c>, <c>[dir] + p</c>), and such a rule stops matching once the attribute goes. For the
/// attributes a selector could name, the winning values of the parent subtree (the element, its content and its
/// following siblings, which a sibling combinator reaches) are computed before and after the removal, and every
/// change is a consequence. The cascade is the one of <see cref="NestedDivRiskAnalyzer"/>. An attribute no selector
/// could name is safe without any computation.
/// </summary>
/// <remarks>
/// <c>:lang()</c> and <c>:dir()</c> are not a risk: they test the language and the direction in effect, which the
/// removal leaves as they were. They are matched by the before/after comparison all the same, so a rule that does
/// behave differently is still reported.
/// </remarks>
internal static class InheritedAttributeRiskAnalyzer
{
    private const int MaxConsequences = 20;

    // An attribute selector naming one of the attributes that inherit: [lang], [lang=x], [xml|lang^=x], [dir], [xmlns].
    private static readonly Regex InheritedAttributeSelector = new(
        @"\[\s*(?:[\w\-*]*\|)?(?:lang|dir|xmlns)\s*[\]~|^$*=]",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    /// <summary>
    /// The consequences of removing each of <paramref name="attributes"/> (found in <paramref name="text"/>, the file
    /// <paramref name="htmlBookPath"/>) on its own, in the same order; an empty list means the removal is safe.
    /// </summary>
    public static IReadOnlyList<IReadOnlyList<CleanupConsequence>> Analyse(
        string htmlBookPath, string text, IReadOnlyList<RedundantAttribute> attributes, IReadOnlyList<CssInfo> stylesheets)
    {
        IReadOnlyList<CleanupConsequence> none = Array.Empty<CleanupConsequence>();
        if (attributes.Count == 0)
        {
            return Array.Empty<IReadOnlyList<CleanupConsequence>>();
        }

        List<(CssRule Rule, int Sequence)> rules = NestedDivRiskAnalyzer.CascadeRules(text, stylesheets);
        bool anyAttributeSelector = rules.Any(r => InheritedAttributeSelector.IsMatch(r.Rule.SelectorText));
        if (!anyAttributeSelector)
        {
            return attributes.Select(_ => none).ToList();
        }

        IHtmlDocument before = XhtmlDoc.Parse(text);
        return attributes
            .Select(attribute => (IReadOnlyList<CleanupConsequence>)Limit(
                Consequences(htmlBookPath, text, before, attribute, rules), htmlBookPath, attribute.TagPos))
            .ToList();
    }

    private static List<(string Text, int Offset)> Consequences(
        string htmlBookPath,
        string text,
        IHtmlDocument before,
        RedundantAttribute attribute,
        List<(CssRule Rule, int Sequence)> rules)
    {
        List<(string Text, int Offset)> found = new();
        if (NestedDivRiskAnalyzer.ElementAt(before, attribute.TagPos) is not { } element)
        {
            return found;
        }

        // The attribute reaches the element, its content and what follows it among its siblings; they share the parent.
        IElement rootBefore = element.ParentElement ?? element;
        int rootPos = XhtmlDoc.OffsetFromNode(rootBefore);
        if (rootPos < 0)
        {
            return found;
        }

        // The root starts before the opening tag that loses the attribute, so it keeps its offset.
        string removed = InheritedAttributeCleaner.Remove(text, new[] { (attribute.TagPos, attribute.Name) });
        IHtmlDocument after = XhtmlDoc.Parse(removed);
        if (NestedDivRiskAnalyzer.ElementAt(after, rootPos) is not { } rootAfter)
        {
            return found;
        }

        List<IElement> was = new List<IElement> { rootBefore }.Concat(rootBefore.QuerySelectorAll("*")).ToList();
        List<IElement> will = new List<IElement> { rootAfter }.Concat(rootAfter.QuerySelectorAll("*")).ToList();
        if (was.Count != will.Count)
        {
            return found;
        }

        string file = htmlBookPath[(htmlBookPath.LastIndexOf('/') + 1)..];
        Dictionary<string, (IElement Element, string Property, NestedDivRiskAnalyzer.Winner? Was, NestedDivRiskAnalyzer.Winner? Will, int Count)> changes =
            new(StringComparer.Ordinal);
        for (int i = 0; i < was.Count; i++)
        {
            Dictionary<string, NestedDivRiskAnalyzer.Winner> oldWinners = NestedDivRiskAnalyzer.Winners(rules, was[i]);
            Dictionary<string, NestedDivRiskAnalyzer.Winner> newWinners = NestedDivRiskAnalyzer.Winners(rules, will[i]);
            foreach (string property in oldWinners.Keys.Union(newWinners.Keys, StringComparer.OrdinalIgnoreCase))
            {
                oldWinners.TryGetValue(property, out NestedDivRiskAnalyzer.Winner? oldWinner);
                newWinners.TryGetValue(property, out NestedDivRiskAnalyzer.Winner? newWinner);
                if (string.Equals(oldWinner?.Value, newWinner?.Value, StringComparison.Ordinal))
                {
                    continue;
                }

                string key = $"{property}\u0001{oldWinner?.Value}\u0001{newWinner?.Value}\u0001{newWinner?.Selector}";
                changes[key] = changes.TryGetValue(key, out var existing)
                    ? existing with { Count = existing.Count + 1 }
                    : (was[i], property, oldWinner, newWinner, 1);
            }
        }

        foreach (var change in changes.Values)
        {
            found.Add((
                CoreStrings.Format(
                    "Cleanup_Risk_ValueChange",
                    file,
                    CssCascadeResolver.DescribeElement(change.Element),
                    change.Count > 1 ? CoreStrings.Format("Cleanup_Risk_MoreElements", change.Count - 1) : string.Empty,
                    change.Property,
                    change.Was?.Value ?? CoreStrings.Get("Cleanup_Risk_NoValue"),
                    change.Will?.Value ?? CoreStrings.Get("Cleanup_Risk_NoValue"),
                    change.Will?.Selector ?? CoreStrings.Get("Cleanup_Risk_NoValue"),
                    NestedDivRiskAnalyzer.ConditionText(change.Will ?? change.Was)),
                Math.Max(0, XhtmlDoc.OffsetFromNode(change.Element))));
        }

        return found;
    }

    private static List<CleanupConsequence> Limit(List<(string Text, int Offset)> found, string htmlBookPath, int fallbackOffset)
    {
        List<CleanupConsequence> consequences = found
            .Take(MaxConsequences)
            .Select(f => new CleanupConsequence(f.Text, htmlBookPath, f.Offset))
            .ToList();
        if (found.Count > MaxConsequences)
        {
            consequences.Add(new CleanupConsequence(
                CoreStrings.Format("Cleanup_Risk_MoreChanges", found.Count - MaxConsequences), htmlBookPath, fallbackOffset));
        }

        return consequences;
    }
}
