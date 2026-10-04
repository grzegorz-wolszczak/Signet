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
/// A consequence of a Cleanup item the user should know about before applying it — e.g. that merging two rules
/// changes the color of a paragraph.
/// </summary>
/// <param name="Text">A readable description of what will happen.</param>
/// <param name="BookPath">The file to open for it (the affected XHTML file, or the stylesheet).</param>
/// <param name="Offset">The 0-based offset in <paramref name="BookPath"/> (the element, or the rule).</param>
public sealed record CleanupConsequence(string Text, string BookPath, int Offset);

/// <summary>
/// Detects merges of CSS rules (<see cref="CssRuleMerger"/>) that would change the book's styling:
/// <list type="bullet">
/// <item>a merge moves declarations or selectors earlier in the stylesheet, so a rule in between with the same
/// priority may start to win — detected exactly by computing, for every element of the book the group's rules
/// apply to, the winning value of each affected property before and after the merge (only within the stylesheet:
/// the merge does not change its order relative to other stylesheets);</item>
/// <item>merging selectors into one list: a reading system that does not understand one selector (vendor prefix,
/// CSS Selectors 4, or a selector the parser rejects) drops the whole rule.</item>
/// </list>
/// Rules inside <c>@media</c> are treated as applying (the consequence names the condition); state
/// pseudo-classes and pseudo-elements are stripped when matching (<see cref="CssSelectorMatching"/>).
/// </summary>
internal sealed class CssMergeRiskAnalyzer
{
    private const int MaxConsequencesPerGroup = 20;

    private static readonly Regex VendorPrefixRegex = new(@"::?-[a-z]+-", RegexOptions.IgnoreCase | RegexOptions.Compiled);

    private static readonly Regex Selectors4Regex = new(
        @"::?(?:is|where|has|matches|dir|nth-col|nth-last-col|part|slotted)\(|"
        + @"::?(?:focus-visible|focus-within|any-link|placeholder-shown|read-only|read-write|blank|user-invalid|user-valid|defined|target-within|local-link|scope|marker|placeholder|backdrop)\b|"
        + @":not\([^)]*[,\s>+~][^)]*\)|:nth-(?:last-)?child\([^)]*\bof\b",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    private readonly Dictionary<string, List<(string HtmlBookPath, IHtmlDocument Document)>> _documentsBySheet =
        new(StringComparer.Ordinal);

    private readonly IHtmlDocument _probe = XhtmlDoc.Parse("<html xmlns=\"http://www.w3.org/1999/xhtml\"><head><title>x</title></head><body></body></html>");

    /// <param name="documents">Every XHTML file with its (original) text and the stylesheets it sees.</param>
    public CssMergeRiskAnalyzer(IEnumerable<(string HtmlBookPath, string Text, IReadOnlyList<string> VisibleSheets)> documents)
    {
        foreach ((string htmlBookPath, string text, IReadOnlyList<string> sheets) in documents)
        {
            IHtmlDocument document = XhtmlDoc.Parse(text);
            foreach (string sheet in sheets)
            {
                if (!_documentsBySheet.TryGetValue(sheet, out var list))
                {
                    list = new List<(string, IHtmlDocument)>();
                    _documentsBySheet[sheet] = list;
                }

                list.Add((htmlBookPath, document));
            }
        }
    }

    /// <summary>
    /// The consequences of applying <paramref name="group"/> alone to <paramref name="cssText"/> (the current text of
    /// the stylesheet <paramref name="cssBookPath"/>). <paramref name="ruleOffset"/> is where the group's first rule
    /// is in the unchanged file (for navigation). Empty when the merge is safe.
    /// </summary>
    public IReadOnlyList<CleanupConsequence> Analyse(string cssBookPath, string cssText, CssMergeGroup group, int ruleOffset)
    {
        List<CleanupConsequence> consequences = new();
        if (group.Kind == CssMergeKind.SameProperties)
        {
            consequences.AddRange(SelectorListRisks(cssBookPath, group, ruleOffset));
        }

        foreach (string selector in group.Rules.SelectMany(r => r.Selectors).Select(s => s.Trim()).Distinct(StringComparer.Ordinal))
        {
            if (CssSelectorMatching.TryMatchAny(_probe, selector) is null)
            {
                consequences.Add(new CleanupConsequence(
                    CoreStrings.Format("Cleanup_Risk_Unverifiable", selector), cssBookPath, ruleOffset));
            }
        }

        string? merged = CssRuleMerger.ApplyMerges(cssText, new[] { group });
        if (merged is null || !_documentsBySheet.TryGetValue(cssBookPath, out var documents))
        {
            return consequences;
        }

        List<CssRule> before = SelectorRules(new CssInfo(cssText));
        List<CssRule> after = SelectorRules(new CssInfo(merged));
        HashSet<string> properties = group.Rules
            .SelectMany(r => r.Declarations)
            .Select(d => d.Property.Trim())
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        List<string> groupSelectors = group.Rules.SelectMany(r => r.Selectors).Select(s => s.Trim()).ToList();

        // One consequence per distinct change; further elements with the same change are counted.
        Dictionary<string, (string HtmlBookPath, IElement Element, string Property, Winner? Before, Winner? After, int Count)> changes =
            new(StringComparer.Ordinal);

        foreach ((string htmlBookPath, IHtmlDocument document) in documents)
        {
            foreach (IElement element in document.All)
            {
                if (!groupSelectors.Any(s => CssSelectorMatching.TryMatch(element, s) == true))
                {
                    continue;
                }

                Dictionary<string, Winner> winnersBefore = Winners(before, element, properties);
                Dictionary<string, Winner> winnersAfter = Winners(after, element, properties);
                foreach (string property in properties)
                {
                    winnersBefore.TryGetValue(property, out Winner? was);
                    winnersAfter.TryGetValue(property, out Winner? will);
                    if (string.Equals(was?.Value, will?.Value, StringComparison.Ordinal))
                    {
                        continue;
                    }

                    string key = $"{property}\u0001{was?.Value}\u0001{will?.Value}\u0001{will?.Selector}";
                    changes[key] = changes.TryGetValue(key, out var existing)
                        ? existing with { Count = existing.Count + 1 }
                        : (htmlBookPath, element, property, was, will, 1);
                }
            }
        }

        foreach (var change in changes.Values.Take(MaxConsequencesPerGroup))
        {
            string more = change.Count > 1 ? CoreStrings.Format("Cleanup_Risk_MoreElements", change.Count - 1) : string.Empty;
            string condition = (change.After?.Condition ?? change.Before?.Condition) is { } prelude
                ? CoreStrings.Format("Cleanup_Risk_Condition", prelude)
                : string.Empty;
            consequences.Add(new CleanupConsequence(
                CoreStrings.Format(
                    "Cleanup_Risk_ValueChange",
                    change.HtmlBookPath[(change.HtmlBookPath.LastIndexOf('/') + 1)..],
                    CssCascadeResolver.DescribeElement(change.Element),
                    more,
                    change.Property,
                    change.Before?.Value ?? CoreStrings.Get("Cleanup_Risk_NoValue"),
                    change.After?.Value ?? CoreStrings.Get("Cleanup_Risk_NoValue"),
                    change.After?.Selector ?? CoreStrings.Get("Cleanup_Risk_NoValue"),
                    condition),
                change.HtmlBookPath,
                Math.Max(0, XhtmlDoc.OffsetFromNode(change.Element))));
        }

        if (changes.Count > MaxConsequencesPerGroup)
        {
            consequences.Add(new CleanupConsequence(
                CoreStrings.Format("Cleanup_Risk_MoreChanges", changes.Count - MaxConsequencesPerGroup), cssBookPath, ruleOffset));
        }

        return consequences;
    }

    private IEnumerable<CleanupConsequence> SelectorListRisks(string cssBookPath, CssMergeGroup group, int ruleOffset)
    {
        List<string> selectors = group.Rules.SelectMany(r => r.Selectors).Select(s => s.Trim()).Distinct(StringComparer.Ordinal).ToList();
        if (selectors.Count < 2)
        {
            yield break;
        }

        string declarations = string.Join("; ", group.Rules[0].Declarations.Select(d => $"{d.Property}: {d.Value}"));
        foreach (string selector in selectors)
        {
            string? reasonKey = VendorPrefixRegex.IsMatch(selector) ? "Cleanup_Risk_Reason_Vendor"
                : Selectors4Regex.IsMatch(selector) ? "Cleanup_Risk_Reason_Selectors4"
                : CssSelectorMatching.TryMatchAny(_probe, selector) is null ? "Cleanup_Risk_Reason_Unparsed"
                : null;
            if (reasonKey is null)
            {
                continue;
            }

            string others = string.Join(", ", selectors.Where(s => !string.Equals(s, selector, StringComparison.Ordinal)));
            yield return new CleanupConsequence(
                CoreStrings.Format("Cleanup_Risk_SelectorList", selector, CoreStrings.Get(reasonKey), others, declarations),
                cssBookPath,
                ruleOffset);
        }
    }

    // The winning declaration of each property for the element within one stylesheet: !important first, then
    // specificity (the highest of the rule's selectors that match), then source order.
    private static Dictionary<string, Winner> Winners(List<CssRule> rules, IElement element, HashSet<string> properties)
    {
        Dictionary<string, (Winner Winner, bool Important, CssSpecificity Specificity)> winners = new(StringComparer.OrdinalIgnoreCase);
        foreach (CssRule rule in rules)
        {
            CssSpecificity? specificity = null;
            string? matchedSelector = null;
            foreach (string raw in rule.Selectors)
            {
                string selector = raw.Trim();
                if (CssSelectorMatching.TryMatch(element, selector) != true)
                {
                    continue;
                }

                CssSpecificity candidate = CssSpecificity.ForSelector(selector);
                if (specificity is null || candidate.CompareTo(specificity.Value) > 0)
                {
                    specificity = candidate;
                    matchedSelector = selector;
                }
            }

            if (specificity is not { } spec)
            {
                continue;
            }

            foreach (CssDeclaration declaration in rule.Declarations)
            {
                string property = declaration.Property.Trim();
                if (!properties.Contains(property))
                {
                    continue;
                }

                bool beats;
                if (!winners.TryGetValue(property, out var current))
                {
                    beats = true;
                }
                else if (declaration.IsImportant != current.Important)
                {
                    beats = declaration.IsImportant;
                }
                else
                {
                    // Later in source order wins a tie, hence ">=".
                    beats = spec.CompareTo(current.Specificity) >= 0;
                }

                if (beats)
                {
                    winners[property] = (new Winner(declaration.Value.Trim(), matchedSelector!, rule.AtRulePrelude), declaration.IsImportant, spec);
                }
            }
        }

        return winners.ToDictionary(w => w.Key, w => w.Value.Winner, StringComparer.OrdinalIgnoreCase);
    }

    private static List<CssRule> SelectorRules(CssInfo info) =>
        info.Rules.Where(r => !string.IsNullOrWhiteSpace(r.SelectorText)).ToList();

    private sealed record Winner(string Value, string Selector, string? Condition);
}
