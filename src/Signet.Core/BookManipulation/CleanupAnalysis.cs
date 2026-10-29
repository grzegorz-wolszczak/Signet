using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading;
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

    /// <summary>
    /// Remove class tokens from XHTML attributes when no selector in the file's visible stylesheets or its
    /// <c>&lt;style&gt;</c> blocks defines them. Script and Media Overlay class references are preserved.
    /// </summary>
    UndefinedClasses,

    /// <summary>Merge rules with an identical selector within one stylesheet.</summary>
    MergeSameSelectors,

    /// <summary>Merge rules with identical properties within one stylesheet.</summary>
    MergeSameProperties,

    /// <summary>
    /// Remove <c>id</c> attributes nothing in the book refers to (<see cref="UnusedIdCleaner"/>, references:
    /// <see cref="IdReferenceIndex"/>). Before the empty elements, bare spans and divs, which an <c>id</c> kept
    /// out of the way of: an element with an <c>id</c> is never removed or merged.
    /// </summary>
    UnusedIds,

    /// <summary>
    /// Remove the attributes that only repeat a value the element already has from an ancestor
    /// (<see cref="InheritedAttributeCleaner"/>): <c>lang</c>, <c>xml:lang</c>, <c>dir</c> and repeated namespace
    /// declarations. Before the empty elements and the bare spans, which weigh the attributes an element carries.
    /// </summary>
    InheritedAttributes,

    /// <summary>
    /// Remove empty <c>&lt;p&gt;</c>, <c>&lt;span&gt;</c> and <c>&lt;div&gt;</c> elements without an <c>id</c>
    /// (<see cref="EmptyElementCleaner"/>). Before the nested divs, which removing an empty element can reveal.
    /// </summary>
    EmptyElements,

    /// <summary>
    /// Remove the tags of <c>&lt;span&gt;</c> elements without attributes and keep their content
    /// (<see cref="BareSpanCleaner"/>).
    /// </summary>
    BareSpans,

    /// <summary>
    /// Merge runs of consecutive sibling <c>&lt;div&gt;</c>s with identical attributes into one
    /// (<see cref="AdjacentDivMerger"/>), on all nesting levels or on the levels chosen per file
    /// (<see cref="DivMergeOptions"/>). Before the nested divs: merging often leaves nestings to collapse.
    /// </summary>
    MergeDivs,

    /// <summary>
    /// Collapse chains of directly nested <c>&lt;div&gt;</c>s with identical attributes into one
    /// (<see cref="NestedDivCollapser"/>).
    /// </summary>
    NestedDivs,

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
/// <param name="Offset">The 0-based offset of the item in <paramref name="BookPath"/> (for navigation).</param>
/// <param name="RuleCount">The number of underlying changes represented by this item, or 0 when not applicable.</param>
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
/// <param name="AppliedRuleCount">The number of underlying changes in applied items, such as merged rules or removed attributes.</param>
/// <param name="RiskyCount">How many of <paramref name="Items"/> are risky (<see cref="CleanupItem.IsRisky"/>).</param>
public sealed record CleanupStepResult(CleanupStep Step, IReadOnlyList<CleanupItem> Items, int AppliedCount, int AppliedRuleCount, int RiskyCount)
{
    /// <summary>
    /// For <see cref="CleanupStep.MergeDivs"/>: every file with mergeable <c>&lt;div&gt;</c>s and the nesting levels
    /// they were found on (also the levels the user switched off); empty for the other steps.
    /// </summary>
    public IReadOnlyList<CleanupLevelFile> Levels { get; init; } = Array.Empty<CleanupLevelFile>();
}

/// <summary>A file with mergeable <c>&lt;div&gt;</c>s and the nesting levels they were found on (ascending).</summary>
/// <param name="BookPath">The XHTML file.</param>
/// <param name="Levels">The levels (1 = directly in <c>&lt;body&gt;</c>).</param>
public sealed record CleanupLevelFile(string BookPath, IReadOnlyList<int> Levels);

/// <summary>The options of <see cref="CleanupStep.MergeDivs"/>.</summary>
/// <param name="AllLevels">Merge on every nesting level (<paramref name="ExcludedLevels"/> are ignored).</param>
/// <param name="Repeat">
/// Merge again after merging, until nothing more can be merged (within the chosen levels) — merging two
/// <c>&lt;div&gt;</c>s can make their children new neighbours.
/// </param>
/// <param name="ExcludedLevels">The levels switched off per file (<see cref="LevelKey"/>), used unless <paramref name="AllLevels"/>.</param>
public sealed record DivMergeOptions(bool AllLevels, bool Repeat, IReadOnlySet<string> ExcludedLevels)
{
    /// <summary>All levels, repeated until nothing more can be merged.</summary>
    public static DivMergeOptions Default { get; } = new(true, true, new HashSet<string>(StringComparer.Ordinal));

    /// <summary>The key of a level of a file in <see cref="ExcludedLevels"/>.</summary>
    public static string LevelKey(string bookPath, int level) =>
        string.Concat(bookPath, "|", level.ToString(System.Globalization.CultureInfo.InvariantCulture));

    /// <summary>Whether the plan merges on <paramref name="level"/> of <paramref name="bookPath"/>.</summary>
    public bool Includes(string bookPath, int level) => AllLevels || !ExcludedLevels.Contains(LevelKey(bookPath, level));
}

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
/// <b>Execution order</b> (<see cref="CleanupStep"/>): unreferenced stylesheets → unused selectors → undefined
/// classes → merging identical selectors → merging identical properties → unused ids → empty elements → bare spans
/// → merging adjacent divs → nested divs → unused media.
/// Unused selectors are computed on the original texts (only whole stylesheets can disappear before them).
/// Undefined classes account for selectors removed by that step; merges and HTML steps use texts left by previous
/// steps, and media references use the CSS that remains at the end.
/// </remarks>
public sealed class CleanupAnalysis
{
    private readonly record struct SelectorKey(string BookPath, int Position, string Text);

    private const string SheetKeyPrefix = "sheet|";
    private const string SelectorKeyPrefix = "selector|";
    private const string UndefinedClassesKeyPrefix = "undefinedclass|";
    private const string MergeSelectorKeyPrefix = "mergesel|";
    private const string MergePropertiesKeyPrefix = "mergeprop|";
    private const string MediaKeyPrefix = "media|";
    private const string NestedDivsKeyPrefix = "divnest|";
    private const string UnusedIdsKeyPrefix = "unusedid|";
    private const string InheritedAttributesKeyPrefix = "inherited|";
    private const string EmptyElementsKeyPrefix = "empty|";
    private const string BareSpansKeyPrefix = "barespan|";
    private const string MergeDivsKeyPrefix = "divmerge|";

    // Rounds of "repeat until nothing more can be merged" — a safety limit; every round removes at least one div.
    private const int MaxMergeDivRounds = 100;
    private const int MaxSpanContentLength = 40;
    private const int MaxOpenTagLength = 80;

    // Attributes that reference a file: src (img, audio, video, source, track, embed, script…), href (link, a,
    // and SVG <image>/<use> incl. xlink:href), data (object), poster (video); srcset is a list.
    private static readonly string[] ReferenceAttributes = { "src", "href", "data", "poster" };

    private static readonly Regex SvgHrefRegex = new(
        @"(?:xlink:)?href\s*=\s*[""'](?<href>[^""']+)[""']", RegexOptions.IgnoreCase | RegexOptions.Compiled);

    // src="…" of the <audio>/<text> elements of a Media Overlays (SMIL) document.
    private static readonly Regex SmilSrcRegex = new(
        @"\bsrc\s*=\s*[""'](?<src>[^""']+)[""']", RegexOptions.IgnoreCase | RegexOptions.Compiled);

    private readonly IReadOnlyList<CssResource> _cssResources;
    private readonly Dictionary<string, string> _cssTexts = new(StringComparer.Ordinal);
    private readonly Dictionary<string, string> _htmlTexts = new(StringComparer.Ordinal);
    private readonly Dictionary<string, string> _htmlFolders = new(StringComparer.Ordinal);
    private readonly IReadOnlyList<CssResource> _unreferencedSheets;
    private readonly IReadOnlyList<CssSelectorUsage> _unusedSelectors;
    private readonly HashSet<string> _mediaOverlayActiveClassSelectors;
    private readonly Dictionary<string, IReadOnlyList<ClassAttributeToken>> _classUsesByHtml = new(StringComparer.Ordinal);
    private readonly Dictionary<string, Dictionary<string, int>> _classDefinitionsByHtml = new(StringComparer.Ordinal);
    private readonly Dictionary<SelectorKey, IReadOnlyList<string>> _classNamesBySelector = new();
    private readonly HashSet<string> _htmlWithCssParseErrors = new(StringComparer.Ordinal);
    private readonly HashSet<string> _mediaReferencedFromHtml = new(StringComparer.Ordinal);
    private readonly IReadOnlyList<Resource> _media;
    private readonly string _coverImagePath;
    private readonly Lazy<CssMergeRiskAnalyzer> _mergeRisks;

    // Where the book refers to element ids (links, ID references, scripts) — built when a merged div has an id.
    private readonly Lazy<IdReferenceIndex> _idReferences;
    private readonly Dictionary<string, IReadOnlyList<string>> _visibleSheets = new(StringComparer.Ordinal);

    // The nested-div chains of each XHTML file with their consequences, for the texts (the file and the stylesheets it
    // sees) they were found in — finding the consequences means computing the cascade, so it is not repeated after
    // every toggle.
    private readonly Dictionary<string, (string Stamp, List<(NestedDivChain Chain, IReadOnlyList<CleanupConsequence> Consequences)> Chains)> _divChains =
        new(StringComparer.Ordinal);

    // The same for the bare spans of each XHTML file.
    private readonly Dictionary<string, (string Stamp, List<(SpanTags Span, IReadOnlyList<CleanupConsequence> Consequences)> Spans)> _bareSpans =
        new(StringComparer.Ordinal);

    // The same for the mergeable div runs of each XHTML file, per merge round (key: path|round).
    private readonly Dictionary<string, (string Stamp, List<(AdjacentDivGroup Group, IReadOnlyList<CleanupConsequence> Consequences)> Groups)> _divGroups =
        new(StringComparer.Ordinal);

    // The same for the unused ids of each XHTML file.
    private readonly Dictionary<string, (string Stamp, List<(IdAttribute Id, IReadOnlyList<CleanupConsequence> Consequences)> Ids)> _unusedIds =
        new(StringComparer.Ordinal);

    // The same for the attributes of each XHTML file that only repeat an inherited value.
    private readonly Dictionary<string, (string Stamp, List<(RedundantAttribute Attribute, IReadOnlyList<CleanupConsequence> Consequences)> Attributes)> _inheritedAttributes =
        new(StringComparer.Ordinal);

    // The same for the empty elements of each XHTML file.
    private readonly Dictionary<string, (string Stamp, List<(EmptyElement Element, IReadOnlyList<CleanupConsequence> Consequences)> Elements)> _emptyElements =
        new(StringComparer.Ordinal);

    private CleanupAnalysis(Book book, CancellationToken cancellationToken)
    {
        _cssResources = book.GetCssResources();
        foreach (CssResource css in _cssResources)
        {
            _cssTexts[css.BookPath] = css.GetText();
        }

        foreach (HtmlResource html in book.GetHtmlResources())
        {
            cancellationToken.ThrowIfCancellationRequested();
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

        // Media Overlays: the audio of a SMIL file (<audio src>) is used even when no XHTML file refers to it.
        foreach (TextResource smil in book.GetAllResources().OfType<TextResource>()
                     .Where(r => string.Equals(r.MediaType, "application/smil+xml", StringComparison.OrdinalIgnoreCase)))
        {
            foreach (Match match in SmilSrcRegex.Matches(smil.GetText()))
            {
                MarkReference(_mediaReferencedFromHtml, match.Groups["src"].Value, smil.Folder);
            }
        }

        // A manifest item named in another item's fallback attribute is used by the reading system.
        OpfResource opf = book.GetOpf();
        _mediaOverlayActiveClassSelectors = new(opf.GetMediaOverlayActiveClassSelectors(), StringComparer.Ordinal);
        List<ManifestEntry> manifest = opf.GetOpfDocument().Manifest.ToList();
        foreach (string fallbackId in manifest.Select(e => e.Attributes.Value("fallback")).Where(id => id.Length > 0))
        {
            if (manifest.FirstOrDefault(e => string.Equals(e.Id, fallbackId, StringComparison.Ordinal)) is { } target)
            {
                _mediaReferencedFromHtml.Add(BookPath.BuildBookPath(Utility.UrlDecodePath(target.Href), opf.Folder));
            }
        }

        List<(string, string, IReadOnlyList<string>)> documents = book.GetHtmlResources()
            .Select(html => (html.BookPath, html.GetText(), book.GetVisibleStylesheets(html)))
            .ToList();
        _mergeRisks = new Lazy<CssMergeRiskAnalyzer>(() => new CssMergeRiskAnalyzer(documents));
        _idReferences = new Lazy<IdReferenceIndex>(() => new IdReferenceIndex(book));
        foreach ((string htmlBookPath, _, IReadOnlyList<string> sheets) in documents)
        {
            _visibleSheets[htmlBookPath] = sheets;
        }

        PrepareClassDefinitions(cancellationToken);
        _unreferencedSheets = book.FindUnusedStylesheets();
        _unusedSelectors = CssSelectorUsageAnalyzer.GetUnusedSelectorsCancellable(book, cancellationToken);
        _media = book.GetMediaResources();
        _coverImagePath = book.GetOpf().GetCoverImagePath();
    }

    /// <summary>
    /// Prepares the analysis of <paramref name="book"/> (parses the files, finds unused selectors). Held back when
    /// any (X)HTML file is not well-formed.
    /// </summary>
    public static CleanupPreparation Prepare(Book book) => PrepareCancellable(book, CancellationToken.None);

    /// <summary>
    /// Like <see cref="Prepare"/>, but stops with an <see cref="OperationCanceledException"/> between the files when
    /// <paramref name="cancellationToken"/> is cancelled. (Named differently rather than overloaded: see
    /// <c>CheckpointHistory.AddCheckpointCancellable</c>.)
    /// </summary>
    public static CleanupPreparation PrepareCancellable(Book book, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(book);
        foreach (HtmlResource html in book.GetHtmlResources())
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (Book.FindFirstNotWellFormed(new[] { html }) is { } bad)
            {
                return new CleanupPreparation(null, bad);
            }
        }

        return new CleanupPreparation(new CleanupAnalysis(book, cancellationToken), null);
    }

    /// <summary>
    /// Simulates the <paramref name="enabledSteps"/> in execution order. Items whose
    /// <see cref="CleanupItem.Key"/> is in <paramref name="excludedKeys"/> are listed but not applied, so the
    /// following steps see the book without that change. A risky item (<see cref="CleanupItem.IsRisky"/>) is applied
    /// only when its key is in <paramref name="acceptedRiskyKeys"/> (and not excluded). <paramref name="divMerge"/> are
    /// the options of <see cref="CleanupStep.MergeDivs"/> (<see cref="DivMergeOptions.Default"/> when omitted).
    /// </summary>
    public CleanupPlan Plan(
        IReadOnlySet<CleanupStep> enabledSteps,
        IReadOnlySet<string> excludedKeys,
        IReadOnlySet<string>? acceptedRiskyKeys = null,
        DivMergeOptions? divMerge = null)
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

        HashSet<SelectorKey> removedSelectors = new();
        if (enabledSteps.Contains(CleanupStep.UnusedSelectors))
        {
            steps.Add(PlanUnusedSelectors(cssTexts, htmlTexts, ruleOrigins, excludedKeys, removedSelectors));
        }

        if (enabledSteps.Contains(CleanupStep.UndefinedClasses))
        {
            steps.Add(PlanUndefinedClasses(htmlTexts, excludedKeys, removedSelectors));
        }

        if (enabledSteps.Contains(CleanupStep.MergeSameSelectors))
        {
            steps.Add(PlanMerges(CleanupStep.MergeSameSelectors, CssMergeKind.SameSelector, cssTexts, ruleOrigins, excludedKeys, acceptedRiskyKeys));
        }

        if (enabledSteps.Contains(CleanupStep.MergeSameProperties))
        {
            steps.Add(PlanMerges(CleanupStep.MergeSameProperties, CssMergeKind.SameProperties, cssTexts, ruleOrigins, excludedKeys, acceptedRiskyKeys));
        }

        if (enabledSteps.Contains(CleanupStep.UnusedIds))
        {
            steps.Add(PlanUnusedIds(cssTexts, htmlTexts, excludedKeys, acceptedRiskyKeys));
        }

        if (enabledSteps.Contains(CleanupStep.InheritedAttributes))
        {
            steps.Add(PlanInheritedAttributes(cssTexts, htmlTexts, excludedKeys, acceptedRiskyKeys));
        }

        if (enabledSteps.Contains(CleanupStep.EmptyElements))
        {
            steps.Add(PlanEmptyElements(cssTexts, htmlTexts, excludedKeys, acceptedRiskyKeys));
        }

        if (enabledSteps.Contains(CleanupStep.BareSpans))
        {
            steps.Add(PlanBareSpans(cssTexts, htmlTexts, excludedKeys, acceptedRiskyKeys));
        }

        if (enabledSteps.Contains(CleanupStep.MergeDivs))
        {
            steps.Add(PlanMergeDivs(cssTexts, htmlTexts, excludedKeys, acceptedRiskyKeys, divMerge ?? DivMergeOptions.Default));
        }

        if (enabledSteps.Contains(CleanupStep.NestedDivs))
        {
            steps.Add(PlanNestedDivs(cssTexts, htmlTexts, excludedKeys, acceptedRiskyKeys));
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
        IReadOnlySet<string> excludedKeys,
        ISet<SelectorKey> removedSelectorKeys)
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
                    foreach (CssSelector selector in removed)
                    {
                        removedSelectorKeys.Add(new SelectorKey(file.Key, selector.Pos, selector.Text));
                    }

                    ruleOrigins[file.Key] = SurvivingRuleOrigins(info, removed, ruleOrigins[file.Key], newText);
                    cssTexts[file.Key] = newText;
                }
            }
            else
            {
                string htmlText = htmlTexts[file.Key];
                HtmlStyleInfo styleInfo = new(htmlText);
                List<CssSelector> removed = styleInfo.GetAllSelectors().Where(Matches).ToList();
                foreach (CssSelector selector in removed)
                {
                    removedSelectorKeys.Add(new SelectorKey(file.Key, selector.Pos, selector.Text));
                }

                htmlTexts[file.Key] = styleInfo.RemoveMatchingSelectors(removed);
            }
        }

        return Result(CleanupStep.UnusedSelectors, items);
    }

    private CleanupStepResult PlanUndefinedClasses(
        Dictionary<string, string> htmlTexts,
        IReadOnlySet<string> excludedKeys,
        IReadOnlySet<SelectorKey> removedSelectorKeys)
    {
        List<CleanupItem> items = new();
        Dictionary<string, List<SelectorKey>> removedBySource = removedSelectorKeys
            .GroupBy(selector => selector.BookPath, StringComparer.Ordinal)
            .ToDictionary(group => group.Key, group => group.ToList(), StringComparer.Ordinal);

        foreach (string path in htmlTexts.Keys.ToList())
        {
            string text = htmlTexts[path];
            IReadOnlyList<ClassAttributeToken> classUses = _classUsesByHtml[path];
            if (classUses.Count == 0)
            {
                continue;
            }

            if (_htmlWithCssParseErrors.Contains(path))
            {
                continue;
            }

            Dictionary<string, int> definedClasses = new(_classDefinitionsByHtml[path], StringComparer.Ordinal);
            HashSet<string> visibleSources = new(_visibleSheets[path], StringComparer.Ordinal) { path };
            foreach (string sourcePath in visibleSources)
            {
                if (!removedBySource.TryGetValue(sourcePath, out List<SelectorKey>? selectors))
                {
                    continue;
                }

                foreach (SelectorKey selector in selectors)
                {
                    if (!_classNamesBySelector.TryGetValue(selector, out IReadOnlyList<string>? classNames))
                    {
                        continue;
                    }

                    foreach (string className in classNames)
                    {
                        if (!definedClasses.TryGetValue(className, out int count))
                        {
                            continue;
                        }

                        if (count == 1)
                        {
                            definedClasses.Remove(className);
                        }
                        else
                        {
                            definedClasses[className] = count - 1;
                        }
                    }
                }
            }

            HashSet<string> toRemove = new(StringComparer.Ordinal);
            foreach (IGrouping<string, ClassAttributeToken> group in classUses.GroupBy(use => use.Name, StringComparer.Ordinal))
            {
                if (definedClasses.ContainsKey(group.Key)
                    || _mediaOverlayActiveClassSelectors.Contains("." + group.Key)
                    || _idReferences.Value.IsMentionedInScript(group.Key))
                {
                    continue;
                }

                string key = $"{UndefinedClassesKeyPrefix}{path}|{Uri.EscapeDataString(group.Key)}";
                bool isApplied = !excludedKeys.Contains(key);
                ClassAttributeToken first = group.MinBy(use => use.Offset);
                int occurrenceCount = group.Count();
                items.Add(new CleanupItem(CleanupStep.UndefinedClasses, key, "." + group.Key, path, first.Offset, occurrenceCount)
                {
                    IsApplied = isApplied,
                });
                if (isApplied)
                {
                    toRemove.Add(group.Key);
                }
            }

            if (toRemove.Count > 0)
            {
                htmlTexts[path] = ClassAttributeEditor.Remove(text, toRemove);
            }
        }

        return Result(CleanupStep.UndefinedClasses, items);
    }

    private void PrepareClassDefinitions(CancellationToken cancellationToken)
    {
        Dictionary<string, IReadOnlyList<string>> classNamesBySource = new(StringComparer.Ordinal);
        HashSet<string> sourcesWithParseErrors = new(StringComparer.Ordinal);

        foreach ((string path, string text) in _cssTexts)
        {
            cancellationToken.ThrowIfCancellationRequested();
            CssInfo info = new(text);
            classNamesBySource[path] = AddClassSelectors(path, info);
            if (info.ParseErrors.Count > 0)
            {
                sourcesWithParseErrors.Add(path);
            }
        }

        foreach ((string path, string text) in _htmlTexts)
        {
            cancellationToken.ThrowIfCancellationRequested();
            _classUsesByHtml[path] = ClassAttributeEditor.Find(text);
            List<string> classNames = new();
            foreach (CssInfo style in new HtmlStyleInfo(text).Styles)
            {
                classNames.AddRange(AddClassSelectors(path, style));
                if (style.ParseErrors.Count > 0)
                {
                    sourcesWithParseErrors.Add(path);
                }
            }

            classNamesBySource[path] = classNames;
        }

        foreach ((string htmlPath, IReadOnlyList<string> visibleSheets) in _visibleSheets)
        {
            Dictionary<string, int> definitions = new(StringComparer.Ordinal);
            HashSet<string> sources = new(visibleSheets, StringComparer.Ordinal) { htmlPath };
            foreach (string sourcePath in sources)
            {
                if (sourcesWithParseErrors.Contains(sourcePath))
                {
                    _htmlWithCssParseErrors.Add(htmlPath);
                }

                if (!classNamesBySource.TryGetValue(sourcePath, out IReadOnlyList<string>? classNames))
                {
                    continue;
                }

                foreach (string className in classNames)
                {
                    if (definitions.TryGetValue(className, out int count))
                    {
                        definitions[className] = count + 1;
                    }
                    else
                    {
                        definitions[className] = 1;
                    }
                }
            }

            _classDefinitionsByHtml[htmlPath] = definitions;
        }
    }

    private List<string> AddClassSelectors(string bookPath, CssInfo info)
    {
        List<string> classNames = new();
        foreach (CssSelector selector in info.GetAllSelectors())
        {
            List<string> selectorClassNames = CssToolbox.FindClassesInSelector(selector.Text)
                .Select(className => className.Name)
                .ToList();
            if (selectorClassNames.Count == 0)
            {
                continue;
            }

            _classNamesBySelector[new SelectorKey(bookPath, selector.Pos, selector.Text)] = selectorClassNames;
            classNames.AddRange(selectorClassNames);
        }

        return classNames;
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

    /// <summary>
    /// Plans the adjacent-div step: per XHTML file, one item for every run of identical sibling <c>&lt;div&gt;</c>s on a
    /// chosen level; a risky run (<see cref="AdjacentDivRiskAnalyzer"/>) is applied only when accepted. With
    /// <see cref="DivMergeOptions.Repeat"/> the file is searched again after merging (round by round) until no run on a
    /// chosen level is applied. Runs on levels switched off are not listed, but their levels are reported in
    /// <see cref="CleanupStepResult.Levels"/>. The <see cref="CleanupItem.RuleCount"/> of an item is the number of
    /// <c>&lt;div&gt;</c>s it removes.
    /// </summary>
    private CleanupStepResult PlanMergeDivs(
        Dictionary<string, string> cssTexts,
        Dictionary<string, string> htmlTexts,
        IReadOnlySet<string> excludedKeys,
        IReadOnlySet<string> acceptedRiskyKeys,
        DivMergeOptions options)
    {
        List<CleanupItem> items = new();
        List<CleanupLevelFile> levels = new();
        foreach (string path in htmlTexts.Keys.ToList())
        {
            string text = htmlTexts[path];
            SortedSet<int> found = new();
            for (int round = 0; round < MaxMergeDivRounds; round++)
            {
                var groups = MergeDivGroups(path, round, text, cssTexts);
                List<int> toMerge = new();
                for (int i = 0; i < groups.Count; i++)
                {
                    (AdjacentDivGroup group, IReadOnlyList<CleanupConsequence> consequences) = groups[i];
                    found.Add(group.Level);
                    if (!options.Includes(path, group.Level))
                    {
                        continue;
                    }

                    string key = string.Create(System.Globalization.CultureInfo.InvariantCulture, $"{MergeDivsKeyPrefix}{path}|{round}|{i}");
                    bool isApplied = !excludedKeys.Contains(key) && (consequences.Count == 0 || acceptedRiskyKeys.Contains(key));
                    items.Add(new CleanupItem(
                        CleanupStep.MergeDivs, key,
                        CoreStrings.Format("Cleanup_MergeDivs_Group", DescribeOpenTag(group.OpenTag), group.Count, group.Level),
                        path, group.FirstPos, group.Count - 1)
                    {
                        Consequences = consequences,
                        IsApplied = isApplied,
                    });
                    if (isApplied)
                    {
                        toMerge.Add(group.FirstPos);
                    }
                }

                // From the end of the file: a merge only changes the text after the run's first opening tag, so the
                // opening tags of the runs before it (and of the runs around it) stay where they were.
                string before = text;
                foreach (int firstPos in toMerge.OrderByDescending(p => p))
                {
                    text = AdjacentDivMerger.Merge(text, firstPos) ?? text;
                }

                if (!options.Repeat || string.Equals(before, text, StringComparison.Ordinal))
                {
                    break;
                }
            }

            htmlTexts[path] = text;
            if (found.Count > 0)
            {
                levels.Add(new CleanupLevelFile(path, found.ToList()));
            }
        }

        return Result(CleanupStep.MergeDivs, items) with { Levels = levels };
    }

    private List<(AdjacentDivGroup Group, IReadOnlyList<CleanupConsequence> Consequences)> MergeDivGroups(
        string path, int round, string text, Dictionary<string, string> cssTexts)
    {
        string cacheKey = string.Create(System.Globalization.CultureInfo.InvariantCulture, $"{path}|{round}");
        (List<string> sheetTexts, string stamp) = SheetsAndStamp(path, text, cssTexts);
        if (_divGroups.TryGetValue(cacheKey, out var cached) && string.Equals(cached.Stamp, stamp, StringComparison.Ordinal))
        {
            return cached.Groups;
        }

        IReadOnlyList<AdjacentDivGroup> groups = AdjacentDivMerger.FindGroups(text);
        List<CssInfo> sheets = groups.Count == 0 ? new List<CssInfo>() : sheetTexts.Select(t => new CssInfo(t)).ToList();
        var result = groups
            .Select(group => (group, AdjacentDivRiskAnalyzer.Analyse(path, text, group, sheets, id => _idReferences.Value.IsReferenced(path, id))))
            .ToList();
        _divGroups[cacheKey] = (stamp, result);
        return result;
    }

    /// <summary>
    /// Plans the nested-div step: per XHTML file, one item for all its safe chains (they are applied together) and
    /// one item for every risky chain (<see cref="NestedDivRiskAnalyzer"/>), applied only when accepted. The
    /// <see cref="CleanupItem.RuleCount"/> of an item is the number of <c>&lt;div&gt;</c>s it removes.
    /// </summary>
    private CleanupStepResult PlanNestedDivs(
        Dictionary<string, string> cssTexts,
        Dictionary<string, string> htmlTexts,
        IReadOnlySet<string> excludedKeys,
        IReadOnlySet<string> acceptedRiskyKeys)
    {
        List<CleanupItem> items = new();
        foreach (string path in htmlTexts.Keys.ToList())
        {
            string text = htmlTexts[path];
            var chains = NestedDivChains(path, text, cssTexts);
            if (chains.Count == 0)
            {
                continue;
            }

            List<int> toCollapse = new();
            List<NestedDivChain> safe = chains.Where(c => c.Consequences.Count == 0).Select(c => c.Chain).ToList();
            if (safe.Count > 0)
            {
                string key = NestedDivsKeyPrefix + path;
                int removed = safe.Sum(c => c.Depth - 1);
                bool isApplied = !excludedKeys.Contains(key);
                items.Add(new CleanupItem(
                    CleanupStep.NestedDivs, key, CoreStrings.Format("Cleanup_NestedDivs_File", removed, safe.Count), path, safe[0].OuterPos, removed)
                {
                    IsApplied = isApplied,
                });
                if (isApplied)
                {
                    toCollapse.AddRange(safe.Select(c => c.OuterPos));
                }
            }

            for (int i = 0; i < chains.Count; i++)
            {
                (NestedDivChain chain, IReadOnlyList<CleanupConsequence> consequences) = chains[i];
                if (consequences.Count == 0)
                {
                    continue;
                }

                string key = $"{NestedDivsKeyPrefix}{path}|{i}";
                bool isApplied = !excludedKeys.Contains(key) && acceptedRiskyKeys.Contains(key);
                items.Add(new CleanupItem(
                    CleanupStep.NestedDivs, key, CoreStrings.Format("Cleanup_NestedDivs_Chain", DescribeOpenTag(chain.OpenTag), chain.Depth, chain.Depth - 1),
                    path, chain.OuterPos, chain.Depth - 1)
                {
                    Consequences = consequences,
                    IsApplied = isApplied,
                });
                if (isApplied)
                {
                    toCollapse.Add(chain.OuterPos);
                }
            }

            // From the end of the file: a collapse only changes the text after the chain's opening tag, so the
            // opening tags of the chains before it stay where they were.
            foreach (int outerPos in toCollapse.OrderByDescending(p => p))
            {
                text = NestedDivCollapser.Collapse(text, outerPos) ?? text;
            }

            htmlTexts[path] = text;
        }

        return Result(CleanupStep.NestedDivs, items);
    }

    private List<(NestedDivChain Chain, IReadOnlyList<CleanupConsequence> Consequences)> NestedDivChains(
        string path, string text, Dictionary<string, string> cssTexts)
    {
        (List<string> sheetTexts, string stamp) = SheetsAndStamp(path, text, cssTexts);
        if (_divChains.TryGetValue(path, out var cached) && string.Equals(cached.Stamp, stamp, StringComparison.Ordinal))
        {
            return cached.Chains;
        }

        IReadOnlyList<NestedDivChain> chains = NestedDivCollapser.FindChains(text);
        List<CssInfo> sheets = chains.Count == 0 ? new List<CssInfo>() : sheetTexts.Select(t => new CssInfo(t)).ToList();
        var result = chains
            .Select(chain => (chain, NestedDivRiskAnalyzer.Analyse(path, text, chain, sheets)))
            .ToList();
        _divChains[path] = (stamp, result);
        return result;
    }

    /// <summary>
    /// Plans the unused-id step: per XHTML file, one item for all the ids nothing refers to and no selector names
    /// (they are applied together) and one item for every id a selector still reaches
    /// (<see cref="UnusedIdRiskAnalyzer"/>), applied only when accepted. The <see cref="CleanupItem.RuleCount"/> of an
    /// item is the number of <c>id</c> attributes it removes.
    /// </summary>
    private CleanupStepResult PlanUnusedIds(
        Dictionary<string, string> cssTexts,
        Dictionary<string, string> htmlTexts,
        IReadOnlySet<string> excludedKeys,
        IReadOnlySet<string> acceptedRiskyKeys)
    {
        List<CleanupItem> items = new();
        foreach (string path in htmlTexts.Keys.ToList())
        {
            string text = htmlTexts[path];
            var ids = UnusedIdsOf(path, text, cssTexts);
            if (ids.Count == 0)
            {
                continue;
            }

            List<int> toRemove = new();
            List<IdAttribute> safe = ids.Where(i => i.Consequences.Count == 0).Select(i => i.Id).ToList();
            if (safe.Count > 0)
            {
                string key = UnusedIdsKeyPrefix + path;
                bool isApplied = !excludedKeys.Contains(key);
                items.Add(new CleanupItem(
                    CleanupStep.UnusedIds, key, CoreStrings.Format("Cleanup_UnusedIds_File", safe.Count), path, safe[0].Pos, safe.Count)
                {
                    IsApplied = isApplied,
                });
                if (isApplied)
                {
                    toRemove.AddRange(safe.Select(i => i.TagPos));
                }
            }

            for (int i = 0; i < ids.Count; i++)
            {
                (IdAttribute id, IReadOnlyList<CleanupConsequence> consequences) = ids[i];
                if (consequences.Count == 0)
                {
                    continue;
                }

                string key = $"{UnusedIdsKeyPrefix}{path}|{i}";
                bool isApplied = !excludedKeys.Contains(key) && acceptedRiskyKeys.Contains(key);
                items.Add(new CleanupItem(
                    CleanupStep.UnusedIds, key, CoreStrings.Format("Cleanup_UnusedIds_Id", id.Value, DescribeOpenTag(id.OpenTag)), path, id.Pos, 1)
                {
                    Consequences = consequences,
                    IsApplied = isApplied,
                });
                if (isApplied)
                {
                    toRemove.Add(id.TagPos);
                }
            }

            if (toRemove.Count > 0)
            {
                htmlTexts[path] = UnusedIdCleaner.Remove(text, toRemove);
            }
        }

        return Result(CleanupStep.UnusedIds, items);
    }

    private List<(IdAttribute Id, IReadOnlyList<CleanupConsequence> Consequences)> UnusedIdsOf(
        string path, string text, Dictionary<string, string> cssTexts)
    {
        (List<string> sheetTexts, string stamp) = SheetsAndStamp(path, text, cssTexts);
        if (_unusedIds.TryGetValue(path, out var cached) && string.Equals(cached.Stamp, stamp, StringComparison.Ordinal))
        {
            return cached.Ids;
        }

        List<IdAttribute> found = UnusedIdCleaner.Find(text).Where(id => !_idReferences.Value.IsReferenced(path, id.Value)).ToList();
        List<CssInfo> sheets = found.Count == 0 ? new List<CssInfo>() : sheetTexts.Select(t => new CssInfo(t)).ToList();
        IReadOnlyList<IReadOnlyList<CleanupConsequence>> consequences = UnusedIdRiskAnalyzer.Analyse(path, text, found, sheets);
        var result = found.Select((id, i) => (id, consequences[i])).ToList();
        _unusedIds[path] = (stamp, result);
        return result;
    }

    /// <summary>
    /// Plans the inherited-attribute step: per XHTML file, one item for all the attributes no selector could name
    /// (they are applied together) and one item for every attribute a selector still reaches
    /// (<see cref="InheritedAttributeRiskAnalyzer"/>), applied only when accepted. The
    /// <see cref="CleanupItem.RuleCount"/> of an item is the number of attributes it removes.
    /// </summary>
    private CleanupStepResult PlanInheritedAttributes(
        Dictionary<string, string> cssTexts,
        Dictionary<string, string> htmlTexts,
        IReadOnlySet<string> excludedKeys,
        IReadOnlySet<string> acceptedRiskyKeys)
    {
        List<CleanupItem> items = new();
        foreach (string path in htmlTexts.Keys.ToList())
        {
            string text = htmlTexts[path];
            var attributes = InheritedAttributesOf(path, text, cssTexts);
            if (attributes.Count == 0)
            {
                continue;
            }

            List<(int TagPos, string Name)> toRemove = new();
            List<RedundantAttribute> safe = attributes.Where(a => a.Consequences.Count == 0).Select(a => a.Attribute).ToList();
            if (safe.Count > 0)
            {
                string key = InheritedAttributesKeyPrefix + path;
                bool isApplied = !excludedKeys.Contains(key);
                string counts = string.Join(", ", safe
                    .GroupBy(a => a.Name, StringComparer.OrdinalIgnoreCase)
                    .OrderBy(g => g.Key, StringComparer.OrdinalIgnoreCase)
                    .Select(g => $"{g.Key}: {g.Count()}"));
                items.Add(new CleanupItem(
                    CleanupStep.InheritedAttributes,
                    key,
                    CoreStrings.Format("Cleanup_InheritedAttributes_File", safe.Count, counts),
                    path,
                    safe[0].Pos,
                    safe.Count)
                {
                    IsApplied = isApplied,
                });
                if (isApplied)
                {
                    toRemove.AddRange(safe.Select(a => (a.TagPos, a.Name)));
                }
            }

            for (int i = 0; i < attributes.Count; i++)
            {
                (RedundantAttribute attribute, IReadOnlyList<CleanupConsequence> consequences) = attributes[i];
                if (consequences.Count == 0)
                {
                    continue;
                }

                string key = $"{InheritedAttributesKeyPrefix}{path}|{i}";
                bool isApplied = !excludedKeys.Contains(key) && acceptedRiskyKeys.Contains(key);
                items.Add(new CleanupItem(
                    CleanupStep.InheritedAttributes,
                    key,
                    CoreStrings.Format(
                        "Cleanup_InheritedAttributes_Attribute",
                        attribute.Name,
                        attribute.Value,
                        attribute.FromTagName,
                        DescribeOpenTag(attribute.OpenTag)),
                    path,
                    attribute.Pos,
                    1)
                {
                    Consequences = consequences,
                    IsApplied = isApplied,
                });
                if (isApplied)
                {
                    toRemove.Add((attribute.TagPos, attribute.Name));
                }
            }

            if (toRemove.Count > 0)
            {
                htmlTexts[path] = InheritedAttributeCleaner.Remove(text, toRemove);
            }
        }

        return Result(CleanupStep.InheritedAttributes, items);
    }

    private List<(RedundantAttribute Attribute, IReadOnlyList<CleanupConsequence> Consequences)> InheritedAttributesOf(
        string path, string text, Dictionary<string, string> cssTexts)
    {
        (List<string> sheetTexts, string stamp) = SheetsAndStamp(path, text, cssTexts);
        if (_inheritedAttributes.TryGetValue(path, out var cached) && string.Equals(cached.Stamp, stamp, StringComparison.Ordinal))
        {
            return cached.Attributes;
        }

        IReadOnlyList<RedundantAttribute> found = InheritedAttributeCleaner.Find(text);
        List<CssInfo> sheets = found.Count == 0 ? new List<CssInfo>() : sheetTexts.Select(t => new CssInfo(t)).ToList();
        IReadOnlyList<IReadOnlyList<CleanupConsequence>> consequences =
            InheritedAttributeRiskAnalyzer.Analyse(path, text, found, sheets);
        var result = found.Select((attribute, i) => (attribute, consequences[i])).ToList();
        _inheritedAttributes[path] = (stamp, result);
        return result;
    }

    /// <summary>
    /// Plans the empty-element step: per XHTML file, one item for all its safe empty elements and one item for every
    /// risky one (<see cref="EmptyElementRiskAnalyzer"/>), applied only when accepted. The
    /// <see cref="CleanupItem.RuleCount"/> of an item is the number of elements it removes.
    /// </summary>
    private CleanupStepResult PlanEmptyElements(
        Dictionary<string, string> cssTexts,
        Dictionary<string, string> htmlTexts,
        IReadOnlySet<string> excludedKeys,
        IReadOnlySet<string> acceptedRiskyKeys)
    {
        List<CleanupItem> items = new();
        foreach (string path in htmlTexts.Keys.ToList())
        {
            string text = htmlTexts[path];
            var elements = EmptyElementsOf(path, text, cssTexts);
            if (elements.Count == 0)
            {
                continue;
            }

            List<int> toRemove = new();
            List<EmptyElement> safe = elements.Where(e => e.Consequences.Count == 0).Select(e => e.Element).ToList();
            if (safe.Count > 0)
            {
                string key = EmptyElementsKeyPrefix + path;
                bool isApplied = !excludedKeys.Contains(key);
                string counts = string.Join(", ", safe
                    .GroupBy(e => e.TagName, StringComparer.Ordinal)
                    .OrderBy(g => g.Key, StringComparer.Ordinal)
                    .Select(g => $"<{g.Key}>: {g.Count()}"));
                items.Add(new CleanupItem(
                    CleanupStep.EmptyElements, key, CoreStrings.Format("Cleanup_EmptyElements_File", safe.Count, counts),
                    path, safe[0].Pos, safe.Count)
                {
                    IsApplied = isApplied,
                });
                if (isApplied)
                {
                    toRemove.AddRange(safe.Select(e => e.Pos));
                }
            }

            for (int i = 0; i < elements.Count; i++)
            {
                (EmptyElement element, IReadOnlyList<CleanupConsequence> consequences) = elements[i];
                if (consequences.Count == 0)
                {
                    continue;
                }

                string key = $"{EmptyElementsKeyPrefix}{path}|{i}";
                bool isApplied = !excludedKeys.Contains(key) && acceptedRiskyKeys.Contains(key);
                items.Add(new CleanupItem(
                    CleanupStep.EmptyElements, key, CoreStrings.Format("Cleanup_EmptyElements_Element", DescribeOpenTag(element.OpenTag)),
                    path, element.Pos, 1)
                {
                    Consequences = consequences,
                    IsApplied = isApplied,
                });
                if (isApplied)
                {
                    toRemove.Add(element.Pos);
                }
            }

            if (toRemove.Count > 0)
            {
                htmlTexts[path] = EmptyElementCleaner.Remove(text, toRemove);
            }
        }

        return Result(CleanupStep.EmptyElements, items);
    }

    private List<(EmptyElement Element, IReadOnlyList<CleanupConsequence> Consequences)> EmptyElementsOf(
        string path, string text, Dictionary<string, string> cssTexts)
    {
        (List<string> sheetTexts, string stamp) = SheetsAndStamp(path, text, cssTexts);
        if (_emptyElements.TryGetValue(path, out var cached) && string.Equals(cached.Stamp, stamp, StringComparison.Ordinal))
        {
            return cached.Elements;
        }

        IReadOnlyList<EmptyElement> found = EmptyElementCleaner.Find(text);
        List<CssInfo> sheets = found.Count == 0 ? new List<CssInfo>() : sheetTexts.Select(t => new CssInfo(t)).ToList();
        IReadOnlyList<IReadOnlyList<CleanupConsequence>> consequences = EmptyElementRiskAnalyzer.Analyse(path, text, found, sheets);
        var result = found.Select((element, i) => (element, consequences[i])).ToList();
        _emptyElements[path] = (stamp, result);
        return result;
    }

    /// <summary>
    /// Plans the bare-span step: per XHTML file, one item for all its safe bare spans and one item for every risky
    /// one (<see cref="SpanRemovalRiskAnalyzer"/>), applied only when accepted. The
    /// <see cref="CleanupItem.RuleCount"/> of an item is the number of spans it unwraps.
    /// </summary>
    private CleanupStepResult PlanBareSpans(
        Dictionary<string, string> cssTexts,
        Dictionary<string, string> htmlTexts,
        IReadOnlySet<string> excludedKeys,
        IReadOnlySet<string> acceptedRiskyKeys)
    {
        List<CleanupItem> items = new();
        foreach (string path in htmlTexts.Keys.ToList())
        {
            string text = htmlTexts[path];
            var spans = BareSpansOf(path, text, cssTexts);
            if (spans.Count == 0)
            {
                continue;
            }

            List<int> toRemove = new();
            List<SpanTags> safe = spans.Where(s => s.Consequences.Count == 0).Select(s => s.Span).ToList();
            if (safe.Count > 0)
            {
                string key = BareSpansKeyPrefix + path;
                bool isApplied = !excludedKeys.Contains(key);
                items.Add(new CleanupItem(
                    CleanupStep.BareSpans, key, CoreStrings.Format("Cleanup_BareSpans_File", safe.Count), path, safe[0].OpenPos, safe.Count)
                {
                    IsApplied = isApplied,
                });
                if (isApplied)
                {
                    toRemove.AddRange(safe.Select(s => s.OpenPos));
                }
            }

            for (int i = 0; i < spans.Count; i++)
            {
                (SpanTags span, IReadOnlyList<CleanupConsequence> consequences) = spans[i];
                if (consequences.Count == 0)
                {
                    continue;
                }

                string key = $"{BareSpansKeyPrefix}{path}|{i}";
                bool isApplied = !excludedKeys.Contains(key) && acceptedRiskyKeys.Contains(key);
                string content = Regex.Replace(Regex.Replace(span.Content, "<[^>]*>", string.Empty), @"\s+", " ").Trim();
                items.Add(new CleanupItem(
                    CleanupStep.BareSpans, key,
                    CoreStrings.Format("Cleanup_BareSpans_Span", content.Length <= MaxSpanContentLength ? content : content[..MaxSpanContentLength] + "…"),
                    path, span.OpenPos, 1)
                {
                    Consequences = consequences,
                    IsApplied = isApplied,
                });
                if (isApplied)
                {
                    toRemove.Add(span.OpenPos);
                }
            }

            if (toRemove.Count > 0)
            {
                htmlTexts[path] = BareSpanCleaner.Remove(text, toRemove);
            }
        }

        return Result(CleanupStep.BareSpans, items);
    }

    private List<(SpanTags Span, IReadOnlyList<CleanupConsequence> Consequences)> BareSpansOf(
        string path, string text, Dictionary<string, string> cssTexts)
    {
        (List<string> sheetTexts, string stamp) = SheetsAndStamp(path, text, cssTexts);
        if (_bareSpans.TryGetValue(path, out var cached) && string.Equals(cached.Stamp, stamp, StringComparison.Ordinal))
        {
            return cached.Spans;
        }

        IReadOnlyList<SpanTags> found = BareSpanCleaner.Find(text);
        List<CssInfo> sheets = found.Count == 0 ? new List<CssInfo>() : sheetTexts.Select(t => new CssInfo(t)).ToList();
        IReadOnlyList<IReadOnlyList<CleanupConsequence>> consequences = SpanRemovalRiskAnalyzer.Analyse(path, text, found, sheets);
        var result = found.Select((span, i) => (span, consequences[i])).ToList();
        _bareSpans[path] = (stamp, result);
        return result;
    }

    // The texts of the stylesheets the file sees, and a stamp of them together with the file's text (cache key).
    private (List<string> SheetTexts, string Stamp) SheetsAndStamp(string path, string text, Dictionary<string, string> cssTexts)
    {
        IReadOnlyList<string> sheetPaths = _visibleSheets.TryGetValue(path, out IReadOnlyList<string>? visible) ? visible : Array.Empty<string>();
        List<string> sheetTexts = sheetPaths.Where(cssTexts.ContainsKey).Select(p => cssTexts[p]).ToList();
        return (sheetTexts, string.Join("\u0001", sheetTexts.Prepend(text)));
    }

    private static string DescribeOpenTag(string openTag)
    {
        string single = Regex.Replace(openTag, @"\s+", " ");
        return single.Length <= MaxOpenTagLength ? single : single[..MaxOpenTagLength] + "…";
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
