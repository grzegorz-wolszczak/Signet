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
/// The consequences of removing the tags of <c>&lt;span&gt;</c> elements (their content stays) — for the bare-span
/// Cleanup step (<see cref="BareSpanCleaner"/>) and for "Remove span" in Code View (<see cref="SpanRemoval"/>):
/// <list type="bullet">
/// <item>every declaration that wins on a removed span — from its classes, its <c>style</c>, or rules that reach it by
/// its name (<c>span { }</c>, <c>p span</c>); a value that does nothing on an inline element (a zero margin…) is
/// left out;</item>
/// <item>every change of a winning value on the other elements, because the structure changes — the children of a
/// span become children of its parent (<c>p &gt; em</c> starts to match, <c>span em</c> stops) and the positions of
/// the siblings change (<c>:first-child</c>, <c>+</c>, <c>~</c>); computed before and after the removal.</item>
/// </list>
/// The cascade is the one of <see cref="NestedDivRiskAnalyzer"/>. The structural comparison is skipped when no rule
/// of the file has a <c>span</c> type selector, a <c>&gt;</c>/<c>+</c>/<c>~</c> combinator or a structural
/// pseudo-class.
/// </summary>
internal static class SpanRemovalRiskAnalyzer
{
    private const int MaxConsequences = 20;

    private static readonly Regex StructuralSelector = new(
        @"(?<![\w-])span(?![\w-])|[>+~]|:(?:first|last|only|nth)-|:empty\b",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    /// <summary>
    /// The consequences of removing each of <paramref name="spans"/> (found in <paramref name="text"/>, the file
    /// <paramref name="htmlBookPath"/>) on its own, in the same order; an empty list means the removal is safe.
    /// </summary>
    public static IReadOnlyList<IReadOnlyList<CleanupConsequence>> Analyse(
        string htmlBookPath, string text, IReadOnlyList<SpanTags> spans, IReadOnlyList<CssInfo> stylesheets)
    {
        List<(CssRule Rule, int Sequence)> rules = NestedDivRiskAnalyzer.CascadeRules(text, stylesheets);
        if (spans.Count == 0 || rules.Count == 0)
        {
            return spans.Select(_ => (IReadOnlyList<CleanupConsequence>)Array.Empty<CleanupConsequence>()).ToList();
        }

        IHtmlDocument before = XhtmlDoc.Parse(text);
        bool structural = rules.Any(r => StructuralSelector.IsMatch(r.Rule.SelectorText));
        return spans
            .Select(span => (IReadOnlyList<CleanupConsequence>)Limit(
                Consequences(htmlBookPath, text, before, new[] { span }, rules, structural), htmlBookPath, span.OpenPos))
            .ToList();
    }

    /// <summary>
    /// The consequences of removing all of <paramref name="spans"/> at once (found in <paramref name="text"/>, the
    /// file <paramref name="htmlBookPath"/>) — the same messages counted once; empty when the removal is safe.
    /// </summary>
    public static IReadOnlyList<CleanupConsequence> AnalyseTogether(
        string htmlBookPath, string text, IReadOnlyList<SpanTags> spans, IReadOnlyList<CssInfo> stylesheets)
    {
        List<(CssRule Rule, int Sequence)> rules = NestedDivRiskAnalyzer.CascadeRules(text, stylesheets);
        if (spans.Count == 0 || rules.Count == 0)
        {
            return Array.Empty<CleanupConsequence>();
        }

        IHtmlDocument before = XhtmlDoc.Parse(text);
        bool structural = rules.Any(r => StructuralSelector.IsMatch(r.Rule.SelectorText));
        return Limit(Consequences(htmlBookPath, text, before, spans, rules, structural), htmlBookPath, spans[0].OpenPos);
    }

    // The messages (with the offset of what they concern), each distinct message once.
    private static List<(string Text, int Offset)> Consequences(
        string htmlBookPath,
        string text,
        IHtmlDocument before,
        IReadOnlyList<SpanTags> spans,
        List<(CssRule Rule, int Sequence)> rules,
        bool structural)
    {
        string file = htmlBookPath[(htmlBookPath.LastIndexOf('/') + 1)..];
        List<(string Text, int Offset)> found = new();
        HashSet<string> seen = new(StringComparer.Ordinal);
        void Add(string message, int offset)
        {
            if (seen.Add(message))
            {
                found.Add((message, offset));
            }
        }

        // Declarations that win on the removed spans themselves.
        HashSet<IElement> removed = new();
        foreach (SpanTags span in spans)
        {
            if (NestedDivRiskAnalyzer.ElementAt(before, span.OpenPos) is not { } element)
            {
                continue;
            }

            removed.Add(element);
            foreach ((string property, NestedDivRiskAnalyzer.Winner winner) in NestedDivRiskAnalyzer.Winners(rules, element))
            {
                if (!NestedDivRiskAnalyzer.IsNeutral(property, winner.Value))
                {
                    Add(CoreStrings.Format(
                        "Cleanup_Risk_SpanStyle", file, property, winner.Value, winner.Selector, NestedDivRiskAnalyzer.ConditionText(winner)),
                        span.OpenPos);
                }
            }
        }

        // The structure that can change: for one span its parent's subtree (its siblings and children), for several
        // the whole body.
        IElement? rootBefore = removed.Count == 1 ? removed.First().ParentElement : before.Body;
        int rootPos = rootBefore is null ? -1 : XhtmlDoc.OffsetFromNode(rootBefore);
        if (!structural || rootBefore is null || rootPos < 0)
        {
            return found;
        }

        // The root lies before every removed tag, so it keeps its offset.
        IHtmlDocument after = XhtmlDoc.Parse(BareSpanCleaner.RemoveTags(text, spans));
        if (NestedDivRiskAnalyzer.ElementAt(after, rootPos) is not { } rootAfter)
        {
            return found;
        }

        List<IElement> was = new List<IElement> { rootBefore }.Concat(rootBefore.QuerySelectorAll("*").Where(e => !removed.Contains(e))).ToList();
        List<IElement> will = new List<IElement> { rootAfter }.Concat(rootAfter.QuerySelectorAll("*")).ToList();
        if (was.Count != will.Count)
        {
            return found;
        }

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
            Add(
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
                Math.Max(0, XhtmlDoc.OffsetFromNode(change.Element)));
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
