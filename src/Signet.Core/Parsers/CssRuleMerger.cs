using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace Signet.Core.Parsers;

/// <summary>The kind of CSS rule group to merge — part of the "Remove unused CSS rules + merge"
/// feature.</summary>
public enum CssMergeKind
{
    /// <summary>Several rules have literally the same selector group text — merge their declarations into one rule.</summary>
    SameSelector,

    /// <summary>Several rules (with different selectors) have an identical set of declarations — merge their selectors into one rule.</summary>
    SameProperties,
}

/// <summary>A group of <see cref="CssRule"/>s from one stylesheet eligible for merging.</summary>
/// <param name="Kind">The reason for the merge.</param>
/// <param name="Rules">The group's rules, in source order (the first becomes the "primary" one after merging).</param>
public sealed record CssMergeGroup(CssMergeKind Kind, IReadOnlyList<CssRule> Rules);

/// <summary>
/// Detecting and performing merges of CSS rules with an identical selector or identical
/// properties ("Remove unused CSS rules + merge identical selectors/properties"). Operates on a
/// single stylesheet (<see cref="CssInfo"/>) — rules from <c>@media</c>/<c>@font-face</c>
/// blocks etc. (<see cref="CssRule.AtRulePrelude"/> non-empty) are skipped, so that rules from different
/// cascade contexts are not merged.
/// </summary>
public static class CssRuleMerger
{
    /// <summary>
    /// Finds the groups of rules to merge in <paramref name="info"/>: first rules with an identical
    /// selector text (<see cref="CssMergeKind.SameSelector"/>), then — among the remaining ones —
    /// rules with an identical set of declarations (<see cref="CssMergeKind.SameProperties"/>).
    /// A rule belongs to at most one group.
    /// </summary>
    public static IReadOnlyList<CssMergeGroup> FindMergeGroups(CssInfo info)
    {
        ArgumentNullException.ThrowIfNull(info);

        List<CssRule> candidates = info.Rules
            .Where(r => r.AtRulePrelude is null && !string.IsNullOrWhiteSpace(r.SelectorText))
            .ToList();

        List<CssMergeGroup> groups = new();
        HashSet<int> consumed = new();

        foreach (IGrouping<string, CssRule> bySelector in candidates.GroupBy(r => r.SelectorText, StringComparer.Ordinal))
        {
            List<CssRule> rules = bySelector.OrderBy(r => r.SelectorStart).ToList();
            if (rules.Count < 2)
            {
                continue;
            }

            groups.Add(new CssMergeGroup(CssMergeKind.SameSelector, rules));
            foreach (CssRule rule in rules)
            {
                consumed.Add(rule.SelectorStart);
            }
        }

        foreach (IGrouping<string, CssRule> byDeclarations in candidates
                     .Where(r => !consumed.Contains(r.SelectorStart))
                     .GroupBy(r => DeclarationsKey(r.Declarations)))
        {
            List<CssRule> rules = byDeclarations.OrderBy(r => r.SelectorStart).ToList();
            if (rules.Count < 2)
            {
                continue;
            }

            groups.Add(new CssMergeGroup(CssMergeKind.SameProperties, rules));
        }

        return groups.OrderBy(g => g.Rules[0].SelectorStart).ToList();
    }

    /// <summary>
    /// Applies the given merge groups to the stylesheet text <paramref name="cssText"/> (it must be the
    /// same text the rules in the groups were built from). Each group is replaced by a single
    /// rule at the place of the group's first (earliest) rule; the other rules of the group are
    /// removed. Returns the new stylesheet text, or <c>null</c> when no group qualified
    /// for merging (fewer than 2 rules).
    /// </summary>
    public static string? ApplyMerges(string cssText, IEnumerable<CssMergeGroup> groups)
    {
        ArgumentNullException.ThrowIfNull(cssText);
        ArgumentNullException.ThrowIfNull(groups);

        List<(int Start, int End, string? Replacement)> edits = new();

        foreach (CssMergeGroup group in groups)
        {
            if (group.Rules.Count < 2)
            {
                continue;
            }

            List<CssRule> rules = group.Rules.OrderBy(r => r.SelectorStart).ToList();
            CssRule primary = rules[0];
            string replacement = BuildReplacement(cssText, group.Kind, rules, primary);

            edits.Add((primary.SelectorStart, primary.BlockEnd, replacement));
            foreach (CssRule rule in rules.Skip(1))
            {
                edits.Add((rule.SelectorStart, rule.BlockEnd, null));
            }
        }

        if (edits.Count == 0)
        {
            return null;
        }

        // From the end of the text to the start, so that earlier offsets stay valid.
        edits.Sort((a, b) => b.Start.CompareTo(a.Start));

        StringBuilder result = new(cssText);
        foreach ((int start, int end, string? replacement) in edits)
        {
            result.Remove(start, end - start);
            if (replacement is not null)
            {
                result.Insert(start, replacement);
            }
        }

        return result.ToString();
    }

    private static string BuildReplacement(string cssText, CssMergeKind kind, IReadOnlyList<CssRule> rules, CssRule primary)
    {
        if (kind == CssMergeKind.SameSelector)
        {
            string header = cssText.Substring(primary.SelectorStart, primary.BlockStart - primary.SelectorStart);
            StringBuilder inner = new();
            foreach (CssRule rule in rules)
            {
                string blockInner = cssText.Substring(rule.BlockStart + 1, rule.BlockEnd - rule.BlockStart - 2).Trim();
                if (blockInner.Length == 0)
                {
                    continue;
                }

                if (inner.Length > 0)
                {
                    inner.Append('\n');
                }

                inner.Append(blockInner);
            }

            return header + "{\n" + inner + "\n}";
        }

        string combinedSelectors = string.Join(", ", rules.Select(r => r.SelectorText));
        string block = cssText.Substring(primary.BlockStart, primary.BlockEnd - primary.BlockStart);
        return combinedSelectors + " " + block;
    }

    private static string DeclarationsKey(IReadOnlyList<CssDeclaration> declarations) =>
        string.Join("|", declarations.Select(d => $"{d.Property.Trim().ToLowerInvariant()}:{d.Value.Trim()}"));
}

/// <summary>
/// A merge candidate at whole-book scale — the result of <c>Book.FindCssCleanupCandidates</c>,
/// resilient to re-parsing the stylesheet when applied (<c>Book.ApplyCssMerges</c> matches the
/// rules again by <see cref="RuleSelectorStarts"/>).
/// </summary>
/// <param name="CssBookPath">The bookpath of the CSS stylesheet the candidate concerns.</param>
/// <param name="Kind">The reason for the merge.</param>
/// <param name="Description">A readable description for the UI (which rules and why).</param>
/// <param name="RuleSelectorStarts">
/// The <see cref="CssRule.SelectorStart"/> offsets of the group's rules in source order — the matching
/// key when the stylesheet is re-parsed.
/// </param>
public sealed record CssMergeCandidate(
    string CssBookPath,
    CssMergeKind Kind,
    string Description,
    IReadOnlyList<int> RuleSelectorStarts);
