using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using AngleSharp.Dom;
using Signet.Core.Localization;
using Signet.Core.Misc;
using Signet.Core.Parsers;
using Signet.Core.Resources;

namespace Signet.Core.BookManipulation;

/// <summary>
/// The steps of the whole-book Cleanup, in the order they are executed. The order matters: a later step works on
/// the result of the earlier ones (e.g. a stylesheet removed in the first step no longer contributes unused
/// selectors, and media referenced only from removed CSS become unused).
/// </summary>
public enum CleanupStep
{
    /// <summary>Remove CSS stylesheets that no XHTML file links.</summary>
    UnreferencedStylesheets,

    /// <summary>Remove CSS selectors (from stylesheets and <c>&lt;style&gt;</c> blocks) that match nothing.</summary>
    UnusedSelectors,

    /// <summary>Merge rules with an identical selector within one stylesheet.</summary>
    MergeSameSelectors,

    /// <summary>Merge rules with identical properties within one stylesheet.</summary>
    MergeSameProperties,

    /// <summary>
    /// Remove media files (images, SVG, audio, video) referenced from nowhere — no XHTML attribute (src, href,
    /// data, poster, srcset, style), no CSS and no SVG file.
    /// </summary>
    UnusedMedia,
}

/// <summary>A single change a Cleanup step would make (a stylesheet, a selector, a merge group, a media file).</summary>
/// <param name="Step">The step the item belongs to.</param>
/// <param name="Key">
/// A key identifying the item across re-planning (stable when the user toggles other items or steps) — used in
/// the set of excluded items passed to <see cref="CleanupAnalysis.Plan"/>.
/// </param>
/// <param name="Text">A readable description (selector text, merge description, file path).</param>
/// <param name="BookPath">The file the item concerns (for navigation).</param>
/// <param name="Offset">The 0-based offset of the item in the current text of <paramref name="BookPath"/> (for navigation).</param>
/// <param name="RuleCount">For merge groups: the number of rules in the group; otherwise 0.</param>
public sealed record CleanupItem(CleanupStep Step, string Key, string Text, string BookPath, int Offset, int RuleCount = 0)
{
    /// <summary>
    /// What applying the item would change in the book (merges that alter the styling); empty for a safe item.
    /// </summary>
    public IReadOnlyList<CleanupConsequence> Consequences { get; init; } = Array.Empty<CleanupConsequence>();

    /// <summary>Whether the item has <see cref="Consequences"/> — it is then applied only when explicitly accepted.</summary>
    public bool IsRisky => Consequences.Count > 0;

    /// <summary>Whether the plan applies the item (not excluded; a risky item only when accepted).</summary>
    public bool IsApplied { get; init; } = true;
}

/// <summary>The items of one enabled step of a <see cref="CleanupPlan"/>.</summary>
/// <param name="Step">The step.</param>
/// <param name="Items">All items the step found (including the ones the user excluded).</param>
/// <param name="AppliedCount">How many of <paramref name="Items"/> will be applied (not excluded).</param>
/// <param name="AppliedRuleCount">For merge steps: the number of rules in the applied groups; otherwise 0.</param>
/// <param name="RiskyCount">How many of <paramref name="Items"/> are risky (<see cref="CleanupItem.IsRisky"/>).</param>
public sealed record CleanupStepResult(CleanupStep Step, IReadOnlyList<CleanupItem> Items, int AppliedCount, int AppliedRuleCount, int RiskyCount);

/// <summary>
/// The simulated result of a Cleanup: what each enabled step finds and the final file contents — the book itself
/// is not modified until <see cref="Book.ApplyCleanup"/>.
/// </summary>
public sealed class CleanupPlan
{
    internal CleanupPlan(
        IReadOnlyList<CleanupStepResult> steps,
        IReadOnlyDictionary<string, string> newTexts,
        IReadOnlyList<Resource> resourcesToDelete)
    {
        Steps = steps;
        NewTexts = newTexts;
        ResourcesToDelete = resourcesToDelete;
    }

    /// <summary>The results of the enabled steps, in execution order.</summary>
    public IReadOnlyList<CleanupStepResult> Steps { get; }

    /// <summary>The new text of every changed CSS/XHTML file (by book path); deleted files are not included.</summary>
    public IReadOnlyDictionary<string, string> NewTexts { get; }

    /// <summary>The resources (stylesheets, media files) to remove from the book.</summary>
    public IReadOnlyList<Resource> ResourcesToDelete { get; }

    /// <summary>Whether applying the plan changes anything.</summary>
    public bool HasChanges => NewTexts.Count > 0 || ResourcesToDelete.Count > 0;

    /// <summary>The result of <paramref name="step"/>, or <c>null</c> when the step is not enabled.</summary>
    public CleanupStepResult? GetStep(CleanupStep step) => Steps.FirstOrDefault(s => s.Step == step);
}

/// <summary>
/// The result of <see cref="CleanupAnalysis.Prepare"/> — the analysis is held back when an (X)HTML file is not
/// well-formed (selector matching on a broken document is unreliable).
/// </summary>
/// <param name="Analysis">The prepared analysis, or <c>null</c> when held back.</param>
/// <param name="NotWellFormed">The first not-well-formed file, or <c>null</c>.</param>
public sealed record CleanupPreparation(CleanupAnalysis? Analysis, HtmlResource? NotWellFormed);

/// <summary>
/// The whole-book Cleanup: plans the enabled <see cref="CleanupStep"/>s as a simulation on copies of the file
/// texts, so the dialog can show what will change before anything is changed. The expensive part (parsing the
/// XHTML files and matching every selector) is done once in <see cref="Prepare"/>; <see cref="Plan"/> is cheap
/// enough to run after every toggle.
/// </summary>
/// <remarks>
/// <b>Execution order</b> (<see cref="CleanupStep"/>): unreferenced stylesheets → unused selectors → merging
/// identical selectors → merging identical properties → unused media. Unused selectors are always computed on the
/// original texts (only whole stylesheets can disappear before them), merges on the texts left by the previous
/// steps, and media references on the CSS that remains at the end.
/// </remarks>
public sealed class CleanupAnalysis
{
    private const string SheetKeyPrefix = "sheet|";
    private const string SelectorKeyPrefix = "selector|";
    private const string MergeSelectorKeyPrefix = "mergesel|";
    private const string MergePropertiesKeyPrefix = "mergeprop|";
    private const string MediaKeyPrefix = "media|";

    // Attributes that reference a file: src (img, audio, video, source, track, embed, script…), href (link, a,
    // and SVG <image>/<use> incl. xlink:href), data (object), poster (video); srcset is a list.
    private static readonly string[] ReferenceAttributes = { "src", "href", "data", "poster" };

    private static readonly Regex SvgHrefRegex = new(
        @"(?:xlink:)?href\s*=\s*[""'](?<href>[^""']+)[""']", RegexOptions.IgnoreCase | RegexOptions.Compiled);

    private readonly IReadOnlyList<CssResource> _cssResources;
    private readonly Dictionary<string, string> _cssTexts = new(StringComparer.Ordinal);
    private readonly Dictionary<string, string> _htmlTexts = new(StringComparer.Ordinal);
    private readonly Dictionary<string, string> _htmlFolders = new(StringComparer.Ordinal);
    private readonly IReadOnlyList<CssResource> _unreferencedSheets;
    private readonly IReadOnlyList<CssSelectorUsage> _unusedSelectors;
    private readonly HashSet<string> _mediaReferencedFromHtml = new(StringComparer.Ordinal);
    private readonly IReadOnlyList<Resource> _media;
    private readonly string _coverImagePath;
    private readonly Lazy<CssMergeRiskAnalyzer> _mergeRisks;

    private CleanupAnalysis(Book book)
    {
        _cssResources = book.GetCssResources();
        foreach (CssResource css in _cssResources)
        {
            _cssTexts[css.BookPath] = css.GetText();
        }

        foreach (HtmlResource html in book.GetHtmlResources())
        {
            _htmlTexts[html.BookPath] = html.GetText();
            _htmlFolders[html.BookPath] = html.Folder;

            foreach (string path in html.GetPathsToLinkedResources())
            {
                _mediaReferencedFromHtml.Add(path);
            }

            foreach (IElement element in html.GetDocument().All)
            {
                MarkElementReferences(_mediaReferencedFromHtml, element, html.Folder);
            }
        }

        // SVG files can reference images (<image href>, url(...) in their styles).
        foreach (SvgResource svg in book.GetMediaResources().OfType<SvgResource>())
        {
            string text = svg.GetText();
            foreach (Match match in SvgHrefRegex.Matches(text))
            {
                MarkReference(_mediaReferencedFromHtml, match.Groups["href"].Value, svg.Folder);
            }

            MarkUrlReferences(_mediaReferencedFromHtml, text, svg.Folder);
        }

        List<(string, string, IReadOnlyList<string>)> documents = book.GetHtmlResources()
            .Select(html => (html.BookPath, html.GetText(), book.GetVisibleStylesheets(html)))
            .ToList();
        _mergeRisks = new Lazy<CssMergeRiskAnalyzer>(() => new CssMergeRiskAnalyzer(documents));

        _unreferencedSheets = book.FindUnusedStylesheets();
        _unusedSelectors = CssSelectorUsageAnalyzer.GetUnusedSelectors(book);
        _media = book.GetMediaResources();
        _coverImagePath = book.GetOpf().GetCoverImagePath();
    }

    /// <summary>
    /// Prepares the analysis of <paramref name="book"/> (parses the files, finds unused selectors). Held back when
    /// any (X)HTML file is not well-formed.
    /// </summary>
    public static CleanupPreparation Prepare(Book book)
    {
        ArgumentNullException.ThrowIfNull(book);
        if (Book.FindFirstNotWellFormed(book.GetHtmlResources()) is { } bad)
        {
            return new CleanupPreparation(null, bad);
        }

        return new CleanupPreparation(new CleanupAnalysis(book), null);
    }

    /// <summary>
    /// Simulates the <paramref name="enabledSteps"/> in execution order. Items whose
    /// <see cref="CleanupItem.Key"/> is in <paramref name="excludedKeys"/> are listed but not applied, so the
    /// following steps see the book without that change. A risky item (<see cref="CleanupItem.IsRisky"/>) is applied
    /// only when its key is in <paramref name="acceptedRiskyKeys"/> (and not excluded).
    /// </summary>
    public CleanupPlan Plan(
        IReadOnlySet<CleanupStep> enabledSteps,
        IReadOnlySet<string> excludedKeys,
        IReadOnlySet<string>? acceptedRiskyKeys = null)
    {
        ArgumentNullException.ThrowIfNull(enabledSteps);
        ArgumentNullException.ThrowIfNull(excludedKeys);
        acceptedRiskyKeys ??= new HashSet<string>(StringComparer.Ordinal);

        Dictionary<string, string> cssTexts = new(_cssTexts, StringComparer.Ordinal);
        Dictionary<string, string> htmlTexts = new(_htmlTexts, StringComparer.Ordinal);
        List<Resource> toDelete = new();
        List<CleanupStepResult> steps = new();

        // For every stylesheet: the original offset of each selector rule of its current (simulated) text — so
        // that items found in a changed text still navigate to the right place in the unchanged file.
        // null = the mapping could not be kept (navigation then falls back to the start of the file).
        Dictionary<string, List<int>?> ruleOrigins = new(StringComparer.Ordinal);
        foreach ((string path, string text) in _cssTexts)
        {
            ruleOrigins[path] = SelectorRules(new CssInfo(text)).Select(r => r.SelectorStart).ToList();
        }

        if (enabledSteps.Contains(CleanupStep.UnreferencedStylesheets))
        {
            List<CleanupItem> items = _unreferencedSheets
                .Select(css => new CleanupItem(CleanupStep.UnreferencedStylesheets, SheetKeyPrefix + css.BookPath, css.BookPath, css.BookPath, 0)
                {
                    IsApplied = !excludedKeys.Contains(SheetKeyPrefix + css.BookPath),
                })
                .ToList();
            foreach (CssResource css in _unreferencedSheets.Where(css => !excludedKeys.Contains(SheetKeyPrefix + css.BookPath)))
            {
                toDelete.Add(css);
                cssTexts.Remove(css.BookPath);
            }

            steps.Add(Result(CleanupStep.UnreferencedStylesheets, items));
        }

        if (enabledSteps.Contains(CleanupStep.UnusedSelectors))
        {
            steps.Add(PlanUnusedSelectors(cssTexts, htmlTexts, ruleOrigins, excludedKeys));
        }

        if (enabledSteps.Contains(CleanupStep.MergeSameSelectors))
        {
            steps.Add(PlanMerges(CleanupStep.MergeSameSelectors, CssMergeKind.SameSelector, cssTexts, ruleOrigins, excludedKeys, acceptedRiskyKeys));
        }

        if (enabledSteps.Contains(CleanupStep.MergeSameProperties))
        {
            steps.Add(PlanMerges(CleanupStep.MergeSameProperties, CssMergeKind.SameProperties, cssTexts, ruleOrigins, excludedKeys, acceptedRiskyKeys));
        }

        if (enabledSteps.Contains(CleanupStep.UnusedMedia))
        {
            List<CleanupItem> items = FindUnusedMedia(cssTexts, htmlTexts)
                .Select(media => new CleanupItem(CleanupStep.UnusedMedia, MediaKeyPrefix + media.BookPath, media.BookPath, media.BookPath, 0)
                {
                    IsApplied = !excludedKeys.Contains(MediaKeyPrefix + media.BookPath),
                })
                .ToList();
            toDelete.AddRange(_media.Where(m => items.Any(i => i.IsApplied && i.BookPath == m.BookPath)));
            steps.Add(Result(CleanupStep.UnusedMedia, items));
        }

        Dictionary<string, string> newTexts = new(StringComparer.Ordinal);
        foreach ((string path, string text) in cssTexts)
        {
            if (!string.Equals(text, _cssTexts[path], StringComparison.Ordinal))
            {
                newTexts[path] = text;
            }
        }

        foreach ((string path, string text) in htmlTexts)
        {
            if (!string.Equals(text, _htmlTexts[path], StringComparison.Ordinal))
            {
                newTexts[path] = text;
            }
        }

        return new CleanupPlan(steps, newTexts, toDelete);
    }

    private CleanupStepResult PlanUnusedSelectors(
        Dictionary<string, string> cssTexts,
        Dictionary<string, string> htmlTexts,
        Dictionary<string, List<int>?> ruleOrigins,
        IReadOnlySet<string> excludedKeys)
    {
        // Selectors of a stylesheet removed in the previous step are gone with it.
        List<CleanupItem> items = _unusedSelectors
            .Where(s => cssTexts.ContainsKey(s.CssBookPath) || htmlTexts.ContainsKey(s.CssBookPath))
            .Select(s =>
            {
                string key = $"{SelectorKeyPrefix}{s.CssBookPath}|{s.Position}|{s.SelectorText}";
                return new CleanupItem(CleanupStep.UnusedSelectors, key, s.SelectorText, s.CssBookPath, s.Position)
                {
                    IsApplied = !excludedKeys.Contains(key),
                };
            })
            .ToList();

        foreach (IGrouping<string, CleanupItem> file in items
                     .Where(i => i.IsApplied)
                     .GroupBy(i => i.BookPath, StringComparer.Ordinal))
        {
            bool Matches(CssSelector selector) =>
                file.Any(i => i.Offset == selector.Pos && string.Equals(i.Text, selector.Text, StringComparison.Ordinal));

            // A stylesheet takes priority over the <style> blocks of an XHTML file with the same book path
            // (the same precedence as the selector usage analysis).
            if (cssTexts.TryGetValue(file.Key, out string? cssText))
            {
                CssInfo info = new(cssText);
                List<CssSelector> removed = info.GetAllSelectors().Where(Matches).ToList();
                string? newText = info.RemoveMatchingSelectors(removed);
                if (newText is not null)
                {
                    ruleOrigins[file.Key] = SurvivingRuleOrigins(info, removed, ruleOrigins[file.Key], newText);
                    cssTexts[file.Key] = newText;
                }
            }
            else
            {
                string htmlText = htmlTexts[file.Key];
                HtmlStyleInfo styleInfo = new(htmlText);
                htmlTexts[file.Key] = styleInfo.RemoveMatchingSelectors(styleInfo.GetAllSelectors().Where(Matches));
            }
        }

        return Result(CleanupStep.UnusedSelectors, items);
    }

    private CleanupStepResult PlanMerges(
        CleanupStep step,
        CssMergeKind kind,
        Dictionary<string, string> cssTexts,
        Dictionary<string, List<int>?> ruleOrigins,
        IReadOnlySet<string> excludedKeys,
        IReadOnlySet<string> acceptedRiskyKeys)
    {
        List<CleanupItem> items = new();
        foreach (CssResource css in _cssResources)
        {
            if (!cssTexts.TryGetValue(css.BookPath, out string? text))
            {
                continue;
            }

            CssInfo info = new(text);
            List<CssRule> selectorRules = SelectorRules(info).ToList();
            List<int>? origins = ruleOrigins[css.BookPath];

            List<CssMergeGroup> applied = new();
            foreach (CssMergeGroup group in CssRuleMerger.FindMergeGroups(info).Where(g => g.Kind == kind))
            {
                string key = kind == CssMergeKind.SameSelector
                    ? $"{MergeSelectorKeyPrefix}{css.BookPath}|{group.Rules[0].SelectorText}"
                    : $"{MergePropertiesKeyPrefix}{css.BookPath}|{CssRuleMerger.DeclarationsKey(group.Rules[0].Declarations)}";
                int index = selectorRules.IndexOf(group.Rules[0]);
                int offset = origins is not null && index >= 0 && index < origins.Count ? origins[index] : 0;
                (CssMergeGroup anchored, IReadOnlyList<CleanupConsequence> consequences) = ChooseAnchor(css.BookPath, text, group, offset);
                bool isApplied = !excludedKeys.Contains(key) && (consequences.Count == 0 || acceptedRiskyKeys.Contains(key));
                items.Add(new CleanupItem(step, key, DescribeMergeGroup(anchored), css.BookPath, offset, group.Rules.Count)
                {
                    Consequences = consequences,
                    IsApplied = isApplied,
                });

                if (isApplied)
                {
                    applied.Add(anchored);
                }
            }

            string? newText = CssRuleMerger.ApplyMerges(text, applied);
            if (newText is null)
            {
                continue;
            }

            // Each merged group stays at the place of its anchor rule; the others disappear.
            HashSet<CssRule> removedRules = applied
                .SelectMany(g => g.Rules.Where((_, i) => i != g.AnchorIndex))
                .ToHashSet();
            List<int>? newOrigins = origins is null
                ? null
                : selectorRules
                    .Select((rule, i) => (rule, i))
                    .Where(x => !removedRules.Contains(x.rule))
                    .Select(x => origins[x.i])
                    .ToList();
            ruleOrigins[css.BookPath] = newOrigins is not null && SelectorRules(new CssInfo(newText)).Count() == newOrigins.Count
                ? newOrigins
                : null;
            cssTexts[css.BookPath] = newText;
        }

        return Result(step, items);
    }

    private List<Resource> FindUnusedMedia(Dictionary<string, string> cssTexts, Dictionary<string, string> htmlTexts)
    {
        HashSet<string> referenced = new(_mediaReferencedFromHtml, StringComparer.Ordinal);

        foreach (CssResource css in _cssResources)
        {
            if (cssTexts.TryGetValue(css.BookPath, out string? text))
            {
                MarkCssUrlReferences(referenced, new CssInfo(text), css.Folder);
            }
        }

        // url(...) in <style> blocks — the block's paths are relative to its XHTML file.
        foreach ((string path, string text) in htmlTexts)
        {
            HtmlStyleInfo styleInfo = new(text);
            foreach (CssInfo block in styleInfo.Styles)
            {
                MarkCssUrlReferences(referenced, block, _htmlFolders[path]);
            }
        }

        return _media
            .Where(m => !referenced.Contains(m.BookPath))
            .Where(m => !(m is ImageResource && string.Equals(m.BookPath, _coverImagePath, StringComparison.Ordinal)))
            .ToList();
    }

    private static void MarkCssUrlReferences(HashSet<string> referenced, CssInfo info, string folder)
    {
        foreach (string value in info.GetAllPropertyValues(""))
        {
            MarkUrlReferences(referenced, value, folder);
        }
    }

    private static void MarkElementReferences(HashSet<string> referenced, IElement element, string folder)
    {
        foreach (IAttr attribute in element.Attributes)
        {
            string name = attribute.LocalName;
            if (ReferenceAttributes.Contains(name, StringComparer.OrdinalIgnoreCase))
            {
                MarkReference(referenced, attribute.Value, folder);
            }
            else if (name.Equals("srcset", StringComparison.OrdinalIgnoreCase))
            {
                // "a.png 1x, b.png 2x" — the URL is the first token of each candidate.
                foreach (string candidate in attribute.Value.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
                {
                    MarkReference(referenced, candidate.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries)[0], folder);
                }
            }
            else if (name.Equals("style", StringComparison.OrdinalIgnoreCase))
            {
                MarkUrlReferences(referenced, attribute.Value, folder);
            }
        }
    }

    private static void MarkReference(HashSet<string> referenced, string reference, string folder)
    {
        if (LinkReference.ResolveBookPath(reference.Trim(), folder) is { } bookPath)
        {
            referenced.Add(bookPath);
        }
    }

    private static void MarkUrlReferences(HashSet<string> referenced, string text, string folder)
    {
        foreach (Match match in Book.UrlFunctionHref.Matches(text))
        {
            string href = match.Groups[1].Value.Trim();
            if (href.Length > 0 && !href.Contains(':', StringComparison.Ordinal))
            {
                referenced.Add(BookPath.BuildBookPath(href, folder));
            }
        }
    }

    /// <summary>
    /// The original offsets of the selector rules that remain after <paramref name="removed"/> selectors were
    /// removed from <paramref name="info"/> (a rule disappears when all of its selectors are removed).
    /// </summary>
    private static List<int>? SurvivingRuleOrigins(CssInfo info, List<CssSelector> removed, List<int>? origins, string newText)
    {
        if (origins is null)
        {
            return null;
        }

        HashSet<int> removedPositions = removed.Select(s => s.Pos).ToHashSet();
        List<CssRule> rules = SelectorRules(info).ToList();
        List<int> surviving = new();
        for (int i = 0; i < rules.Count && i < origins.Count; i++)
        {
            CssRule rule = rules[i];
            bool allRemoved = info.GetAllSelectors()
                .Where(s => s.Pos >= rule.SelectorStart && s.Pos < rule.BlockStart)
                .All(s => removedPositions.Contains(s.Pos));
            if (!allRemoved)
            {
                surviving.Add(origins[i]);
            }
        }

        return SelectorRules(new CssInfo(newText)).Count() == surviving.Count ? surviving : null;
    }

    private static IEnumerable<CssRule> SelectorRules(CssInfo info) =>
        info.Rules.Where(r => !string.IsNullOrWhiteSpace(r.SelectorText));

    private static CleanupStepResult Result(CleanupStep step, List<CleanupItem> items)
    {
        List<CleanupItem> applied = items.Where(i => i.IsApplied).ToList();
        return new CleanupStepResult(step, items, applied.Count, applied.Sum(i => i.RuleCount), items.Count(i => i.IsRisky));
    }

    /// <summary>
    /// Picks the place for the merged rule: the place of each rule of the group is tried, and the one with the
    /// fewest consequences wins (the earliest on a tie) — so a merge is marked risky only when no place keeps the
    /// styling unchanged.
    /// </summary>
    private (CssMergeGroup Group, IReadOnlyList<CleanupConsequence> Consequences) ChooseAnchor(
        string cssBookPath, string cssText, CssMergeGroup group, int ruleOffset)
    {
        (CssMergeGroup Group, IReadOnlyList<CleanupConsequence> Consequences)? best = null;
        for (int anchor = 0; anchor < group.Rules.Count; anchor++)
        {
            CssMergeGroup candidate = group with { AnchorIndex = anchor };
            IReadOnlyList<CleanupConsequence> consequences = _mergeRisks.Value.Analyse(cssBookPath, cssText, candidate, ruleOffset);
            if (best is null || consequences.Count < best.Value.Consequences.Count)
            {
                best = (candidate, consequences);
            }

            if (consequences.Count == 0)
            {
                break;
            }
        }

        return best!.Value;
    }

    private static string DescribeMergeGroup(CssMergeGroup group)
    {
        string description = group.Kind == CssMergeKind.SameSelector
            ? CoreStrings.Format("Css_MergeSameSelector", group.Rules.Count, group.Rules[0].SelectorText)
            : CoreStrings.Format("Css_MergeSameProperties", group.Rules.Count, string.Join(", ", group.Rules.Select(r => r.SelectorText)));
        return group.AnchorIndex == 0
            ? description
            : description + CoreStrings.Format("Css_MergeAtRule", group.AnchorIndex + 1, group.Rules.Count);
    }
}
