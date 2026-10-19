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
/// The consequences of removing the <c>id</c> attribute of an element nothing links to — for the unused-id Cleanup
/// step (<see cref="UnusedIdCleaner"/>). A stylesheet can still reach the element by its id (<c>#note { }</c>,
/// <c>[id^="fn"]</c>), so for the ids named by a selector the winning values of the element's parent subtree (the
/// element, its content and its following siblings) are computed before and after the removal, and every change is a
/// consequence. The cascade is the one of <see cref="NestedDivRiskAnalyzer"/>. An id that no selector names is safe
/// without any computation.
/// </summary>
internal static class UnusedIdRiskAnalyzer
{
    private const int MaxConsequences = 20;

    private static readonly Regex IdSelector = new(@"#(?<id>(?:[\w\-\u0080-￿]|\\.)+)", RegexOptions.Compiled);

    // An attribute selector on the id: [id], [id=x], [id^=x], [ns|id…].
    private static readonly Regex IdAttributeSelector = new(@"\[\s*(?:[\w\-*]*\|)?id\s*[\]~|^$*=]", RegexOptions.IgnoreCase | RegexOptions.Compiled);

    /// <summary>
    /// The consequences of removing each of <paramref name="ids"/> (found in <paramref name="text"/>, the file
    /// <paramref name="htmlBookPath"/>) on its own, in the same order; an empty list means the removal is safe.
    /// </summary>
    public static IReadOnlyList<IReadOnlyList<CleanupConsequence>> Analyse(
        string htmlBookPath, string text, IReadOnlyList<IdAttribute> ids, IReadOnlyList<CssInfo> stylesheets)
    {
        IReadOnlyList<CleanupConsequence> none = Array.Empty<CleanupConsequence>();
        List<(CssRule Rule, int Sequence)> rules = NestedDivRiskAnalyzer.CascadeRules(text, stylesheets);
        HashSet<string> named = new(StringComparer.Ordinal);
        bool anyId = false;
        foreach ((CssRule rule, _) in rules)
        {
            anyId |= IdAttributeSelector.IsMatch(rule.SelectorText);
            foreach (Match match in IdSelector.Matches(rule.SelectorText))
            {
                named.Add(Regex.Replace(match.Groups["id"].Value, @"\\(.)", "$1"));
            }
        }

        if (ids.Count == 0 || (!anyId && named.Count == 0))
        {
            return ids.Select(_ => none).ToList();
        }

        IHtmlDocument before = XhtmlDoc.Parse(text);
        return ids
            .Select(id => anyId || named.Contains(id.Value)
                ? (IReadOnlyList<CleanupConsequence>)Limit(Consequences(htmlBookPath, text, before, id, rules), htmlBookPath, id.TagPos)
                : none)
            .ToList();
    }

    private static List<(string Text, int Offset)> Consequences(
        string htmlBookPath, string text, IHtmlDocument before, IdAttribute id, List<(CssRule Rule, int Sequence)> rules)
    {
        List<(string Text, int Offset)> found = new();
        if (NestedDivRiskAnalyzer.ElementAt(before, id.TagPos) is not { } element)
        {
            return found;
        }

        // The id reaches the element, its content and what follows it among its siblings; they share the parent.
        IElement rootBefore = element.ParentElement ?? element;
        int rootPos = XhtmlDoc.OffsetFromNode(rootBefore);
        if (rootPos < 0)
        {
            return found;
        }

        // The root starts before the opening tag that loses its id, so it keeps its offset.
        IHtmlDocument after = XhtmlDoc.Parse(UnusedIdCleaner.Remove(text, new[] { id.TagPos }));
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
