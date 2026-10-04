using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using AngleSharp.Dom;
using Signet.Core.BookManipulation;
using Signet.Core.Resources;

namespace Signet.Core.Parsers;

/// <summary>The rule a declaration comes from — used to say which rule overrode another declaration.</summary>
/// <param name="BookPath">The file containing the rule (a CSS stylesheet, or the HTML file for an inline style/<c>&lt;style&gt;</c>).</param>
/// <param name="Offset">The 0-based offset of the selector (or of the element carrying the <c>style</c> attribute) in <paramref name="BookPath"/>.</param>
/// <param name="SelectorText">The selector text (or <c>"style=\"…\""</c> for an inline style).</param>
/// <param name="IsInlineStyle">Whether this is an element's <c>style</c> attribute (not a selector rule).</param>
public sealed record CssDeclarationSource(string BookPath, int Offset, string SelectorText, bool IsInlineStyle);

/// <summary>A single declaration in a rule matched to an element, with the cascade result.</summary>
/// <param name="Property">The property name.</param>
/// <param name="Value">The value (including a possible <c>!important</c>).</param>
/// <param name="IsImportant">Whether the declaration has <c>!important</c>.</param>
/// <param name="IsOverridden">
/// Whether this declaration is overridden — by another declaration of the same property with a higher cascade
/// priority on the same element, or (for an inherited property declared on an ancestor) by a declaration on an
/// element closer to the inspected one. Struck through in the UI.
/// </param>
/// <param name="AffectsElement">
/// Whether the declaration takes effect on the inspected element. <c>false</c> for overridden declarations and
/// for non-inherited properties (e.g. <c>margin</c>) declared on an ancestor — those are shown dimmed.
/// </param>
/// <param name="OverriddenBy">
/// For an overridden declaration: the rule whose declaration actually wins (on the inspected element for
/// inherited properties, on the declaring element otherwise). <c>null</c> when not overridden.
/// </param>
public readonly record struct CssMatchedDeclaration(
    string Property,
    string Value,
    bool IsImportant,
    bool IsOverridden,
    bool AffectsElement = true,
    CssDeclarationSource? OverriddenBy = null);

/// <summary>A CSS rule matched to an element (via <see cref="IElement.Matches"/>).</summary>
/// <param name="BookPath">The file containing the rule (a CSS stylesheet, or the HTML file itself for an inline style/<c>&lt;style&gt;</c>).</param>
/// <param name="Offset">The 0-based offset of the selector (or of the <c>style</c> attribute) in <paramref name="BookPath"/>.</param>
/// <param name="SelectorText">The selector text (or <c>"style=\"…\""</c> for an inline style).</param>
/// <param name="Specificity">The specificity per <see cref="CssSpecificity"/>.</param>
/// <param name="IsInlineStyle">Whether this is the element's <c>style</c> attribute (not a selector rule).</param>
/// <param name="Declarations">The rule's declarations, with the cascade result per property.</param>
public sealed record CssMatchedRule(
    string BookPath,
    int Offset,
    string SelectorText,
    CssSpecificity Specificity,
    bool IsInlineStyle,
    IReadOnlyList<CssMatchedDeclaration> Declarations)
{
    /// <summary>This rule as a <see cref="CssDeclarationSource"/>.</summary>
    public CssDeclarationSource Source => new(BookPath, Offset, SelectorText, IsInlineStyle);
}

/// <summary>One element of the inspected element's ancestor chain, with the rules matched to it.</summary>
/// <param name="ElementDescription">A short description of the element, e.g. <c>div.a</c>.</param>
/// <param name="ElementOffset">The 0-based offset of the element's opening tag in the HTML file.</param>
/// <param name="IsInspectedElement">Whether this is the element under the caret (the last level).</param>
/// <param name="MatchedRules">All rules matching this element (in source cascade order), with the inline style last.</param>
public sealed record CssCascadeLevel(
    string ElementDescription,
    int ElementOffset,
    bool IsInspectedElement,
    IReadOnlyList<CssMatchedRule> MatchedRules);

/// <summary>The result of <see cref="CssCascadeResolver.Resolve"/> for a single element.</summary>
/// <param name="ElementBookPath">The HTML file containing the element.</param>
/// <param name="ElementOffset">The 0-based offset of the element's opening tag in <see cref="ElementBookPath"/>.</param>
/// <param name="ElementDescription">A short description of the element for the panel header, e.g. <c>p.note#foo</c>.</param>
/// <param name="Levels">
/// The ancestor chain from <c>body</c> (top) down to the inspected element (last, always present). Ancestors
/// without any matching rule are left out; <c>html</c> is never included.
/// </param>
public sealed record CssCascadeResult(
    string ElementBookPath,
    int ElementOffset,
    string ElementDescription,
    IReadOnlyList<CssCascadeLevel> Levels)
{
    /// <summary>The rules matched to the inspected element itself (the last level).</summary>
    public IReadOnlyList<CssMatchedRule> MatchedRules => Levels[^1].MatchedRules;
}

/// <summary>
/// The CSS cascade/specificity engine behind the "Live CSS Panel":
/// for the element under the caret in Code View and each of its ancestors up to <c>body</c> it finds all
/// matching CSS rules (linked stylesheets + <c>&lt;style&gt;</c> blocks + the inline <c>style</c> attribute),
/// computes the specificity and marks the properties overridden by a rule with a higher priority — on the same
/// element, or (for inherited properties, <see cref="CssInheritedProperties"/>) on an element closer to the
/// inspected one.
/// </summary>
/// <remarks>
/// <b>Deliberate simplifications:</b>
/// <list type="bullet">
/// <item>The input is the caret in Code View, NOT a live hover in Preview — the cascade engine
/// itself is independent of that and can be hooked up to Preview in the future without changes in this class.</item>
/// <item>Shorthands are not expanded (<c>font</c> does not override <c>font-size</c> and vice versa), and the
/// <c>inherit</c>/<c>initial</c> keywords get no special treatment.</item>
/// <item><c>@import</c>ed stylesheets are included (before the importing one); <c>@media</c> is not taken
/// into account — rules inside <c>@media</c> are
/// treated as ordinary top-level rules (the same simplified model that
/// <see cref="CssClassDefinitionLocator"/> uses).</item>
/// <item>Rules from <c>CssInfo.Rules</c> without a selector (e.g. <c>@font-face</c> blocks) are skipped —
/// they are irrelevant to matching against an element.</item>
/// <item>The specificity (<see cref="CssSpecificity.ForSelector"/>) is a regex heuristic, not a full
/// selector parser — see the notes on that class.</item>
/// </list>
/// </remarks>
public static class CssCascadeResolver
{
    /// <summary>
    /// Finds the element at <paramref name="caretOffset"/> in <paramref name="html"/> and computes its
    /// CSS cascade. Returns <c>null</c> when the document does not parse or the caret is not in
    /// any element. <paramref name="resolveCssInfo"/> supplies the parsed stylesheet by bookpath
    /// (as in <see cref="CssClassDefinitionLocator.Find"/>) — <c>null</c> when the stylesheet does not exist.
    /// </summary>
    public static CssCascadeResult? Resolve(HtmlResource html, int caretOffset, Func<string, CssInfo?> resolveCssInfo)
    {
        ArgumentNullException.ThrowIfNull(html);
        ArgumentNullException.ThrowIfNull(resolveCssInfo);

        AngleSharp.Html.Dom.IHtmlDocument document;
        try
        {
            document = XhtmlDoc.Parse(html.GetText());
        }
        catch (Exception)
        {
            return null;
        }

        if (XhtmlDoc.NodeFromOffset(document, caretOffset) is not IElement element)
        {
            return null;
        }

        // Linked stylesheets and the ones imported by them or by <style> blocks (@import, transitively).
        Dictionary<string, CssInfo?> infos = new(StringComparer.Ordinal);
        CssInfo? InfoOf(string bookPath)
        {
            if (!infos.TryGetValue(bookPath, out CssInfo? info))
            {
                info = resolveCssInfo(bookPath);
                infos[bookPath] = info;
            }

            return info;
        }

        IEnumerable<string> roots = html.GetLinkedStylesheets().Concat(CssImports.FromStyleBlocks(html.GetText(), html.BookPath));
        List<(string BookPath, CssInfo Info)> sheets = CssImports
            .VisibleStylesheets(
                roots,
                bookPath => InfoOf(bookPath) is { } info ? CssImports.Parse(info.SourceText, BookPath.StartingDir(bookPath)) : null)
            .Select(bookPath => (bookPath, InfoOf(bookPath)!))
            .ToList();

        HtmlStyleInfo styleInfo = new(html.GetText());
        foreach (CssInfo block in styleInfo.Styles)
        {
            sheets.Add((html.BookPath, block));
        }

        // The chain from the inspected element up to body (html excluded), then reversed: body first.
        List<IElement> chain = new() { element };
        for (IElement? parent = element.ParentElement;
             parent is not null && !string.Equals(parent.LocalName, "html", StringComparison.OrdinalIgnoreCase);
             parent = parent.ParentElement)
        {
            chain.Add(parent);
        }

        chain.Reverse();

        List<LevelCascade> levels = chain
            .Select(e => ApplyElementCascade(MatchRules(e, sheets, html.BookPath)))
            .ToList();
        List<CssCascadeLevel> resultLevels = ApplyInheritance(chain, levels);

        int caretElementOffset = Math.Max(0, XhtmlDoc.OffsetFromNode(element));
        return new CssCascadeResult(html.BookPath, caretElementOffset, DescribeElement(element), resultLevels);
    }

    // The rules matched to one element plus, per property, the declaration that wins on that element.
    private sealed record LevelCascade(
        List<CssMatchedRule> Rules,
        Dictionary<string, (int RuleIndex, int DeclIndex)> Winners);

    private static List<(CssMatchedRule Rule, int Sequence)> MatchRules(
        IElement element,
        List<(string BookPath, CssInfo Info)> sheets,
        string htmlBookPath)
    {
        List<(CssMatchedRule Rule, int Sequence)> matched = new();
        int sequence = 0;
        foreach ((string bookPath, CssInfo info) in sheets)
        {
            foreach (CssRule rule in info.Rules)
            {
                if (rule.SelectorText.Length == 0)
                {
                    continue;
                }

                foreach (string rawSelector in rule.Selectors)
                {
                    string selector = rawSelector.Trim();
                    if (selector.Length == 0 || !TryMatches(element, selector))
                    {
                        continue;
                    }

                    List<CssMatchedDeclaration> declarations = rule.Declarations
                        .Select(d => new CssMatchedDeclaration(d.Property, d.Value, d.IsImportant, false))
                        .ToList();

                    matched.Add((
                        new CssMatchedRule(bookPath, rule.SelectorStart, selector, CssSpecificity.ForSelector(selector), false, declarations),
                        sequence++));
                }
            }
        }

        string? inlineStyle = element.GetAttribute("style");
        if (!string.IsNullOrWhiteSpace(inlineStyle))
        {
            CssInfo inlineInfo = new("x{" + inlineStyle + "}");
            if (inlineInfo.Rules.Count > 0)
            {
                List<CssMatchedDeclaration> declarations = inlineInfo.Rules[0].Declarations
                    .Select(d => new CssMatchedDeclaration(d.Property, d.Value, d.IsImportant, false))
                    .ToList();
                int elementOffset = Math.Max(0, XhtmlDoc.OffsetFromNode(element));
                matched.Add((
                    new CssMatchedRule(htmlBookPath, elementOffset, "style=\"…\"", CssSpecificity.Inline, true, declarations),
                    sequence++));
            }
        }

        return matched;
    }

    private static bool TryMatches(IElement element, string selector)
    {
        try
        {
            return element.Matches(selector);
        }
        catch (Exception)
        {
            // A selector AngleSharp does not understand (e.g. syntax specific to another
            // engine) — treated as not matching.
            return false;
        }
    }

    private static LevelCascade ApplyElementCascade(List<(CssMatchedRule Rule, int Sequence)> matched)
    {
        // The winner key per property (case-insensitive): first the !important layer (if
        // anyone has it for that property, only it counts), then higher specificity,
        // then later source order. An inline style has Origin=1 in CssSpecificity, so it
        // always beats selector rules in the same importance layer.
        Dictionary<string, (int RuleIndex, int DeclIndex, bool Important, CssSpecificity Specificity, int Sequence)> winners =
            new(StringComparer.OrdinalIgnoreCase);

        for (int ruleIndex = 0; ruleIndex < matched.Count; ruleIndex++)
        {
            (CssMatchedRule rule, int sequence) = matched[ruleIndex];
            for (int declIndex = 0; declIndex < rule.Declarations.Count; declIndex++)
            {
                CssMatchedDeclaration decl = rule.Declarations[declIndex];
                if (!winners.TryGetValue(decl.Property, out var current))
                {
                    winners[decl.Property] = (ruleIndex, declIndex, decl.IsImportant, rule.Specificity, sequence);
                    continue;
                }

                bool beats;
                if (decl.IsImportant != current.Important)
                {
                    beats = decl.IsImportant;
                }
                else
                {
                    int bySpecificity = rule.Specificity.CompareTo(current.Specificity);
                    beats = bySpecificity != 0 ? bySpecificity > 0 : sequence > current.Sequence;
                }

                if (beats)
                {
                    winners[decl.Property] = (ruleIndex, declIndex, decl.IsImportant, rule.Specificity, sequence);
                }
            }
        }

        Dictionary<string, (int RuleIndex, int DeclIndex)> finalWinners = new(StringComparer.OrdinalIgnoreCase);
        foreach ((string property, var winner) in winners)
        {
            finalWinners[property] = (winner.RuleIndex, winner.DeclIndex);
        }

        return new LevelCascade(matched.Select(m => m.Rule).ToList(), finalWinners);
    }

    /// <summary>
    /// Combines the per-element cascades of the chain (<paramref name="levels"/>, body first, the inspected
    /// element last) into the final result: an inherited property set on a level is overridden by the same
    /// property set on any level below it, and its effective source is the closest such level; a
    /// non-inherited property only ever affects the element it is declared on.
    /// </summary>
    private static List<CssCascadeLevel> ApplyInheritance(List<IElement> chain, List<LevelCascade> levels)
    {
        int last = levels.Count - 1;

        // For every inherited property: the winning declaration on the level closest to the inspected element.
        Dictionary<string, (int Level, CssDeclarationSource Source)> effectiveInherited = new(StringComparer.OrdinalIgnoreCase);
        for (int level = 0; level <= last; level++)
        {
            foreach ((string property, var winner) in levels[level].Winners)
            {
                if (CssInheritedProperties.IsInherited(property))
                {
                    effectiveInherited[property] = (level, levels[level].Rules[winner.RuleIndex].Source);
                }
            }
        }

        List<CssCascadeLevel> result = new(levels.Count);
        for (int level = 0; level <= last; level++)
        {
            LevelCascade cascade = levels[level];
            bool isInspected = level == last;
            if (!isInspected && cascade.Rules.Count == 0)
            {
                continue;
            }

            List<CssMatchedRule> rules = new(cascade.Rules.Count);
            for (int ruleIndex = 0; ruleIndex < cascade.Rules.Count; ruleIndex++)
            {
                CssMatchedRule rule = cascade.Rules[ruleIndex];
                List<CssMatchedDeclaration> declarations = new(rule.Declarations.Count);
                for (int declIndex = 0; declIndex < rule.Declarations.Count; declIndex++)
                {
                    CssMatchedDeclaration decl = rule.Declarations[declIndex];
                    (int RuleIndex, int DeclIndex) localWinner = cascade.Winners[decl.Property];
                    bool isLocalWinner = localWinner == (ruleIndex, declIndex);

                    if (CssInheritedProperties.IsInherited(decl.Property))
                    {
                        (int effectiveLevel, CssDeclarationSource effectiveSource) = effectiveInherited[decl.Property];
                        bool overridden = !isLocalWinner || effectiveLevel != level;
                        declarations.Add(decl with
                        {
                            IsOverridden = overridden,
                            AffectsElement = !overridden,
                            OverriddenBy = overridden ? effectiveSource : null,
                        });
                    }
                    else
                    {
                        declarations.Add(decl with
                        {
                            IsOverridden = !isLocalWinner,
                            AffectsElement = isLocalWinner && isInspected,
                            OverriddenBy = isLocalWinner ? null : cascade.Rules[localWinner.RuleIndex].Source,
                        });
                    }
                }

                rules.Add(rule with { Declarations = declarations });
            }

            IElement element = chain[level];
            result.Add(new CssCascadeLevel(
                DescribeElement(element),
                Math.Max(0, XhtmlDoc.OffsetFromNode(element)),
                isInspected,
                rules));
        }

        return result;
    }

    internal static string DescribeElement(IElement element)
    {
        StringBuilder sb = new(element.TagName.ToLowerInvariant());
        string? id = element.Id;
        if (!string.IsNullOrEmpty(id))
        {
            sb.Append('#').Append(id);
        }

        string? classAttr = element.GetAttribute("class");
        if (!string.IsNullOrWhiteSpace(classAttr))
        {
            foreach (string cls in classAttr.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries))
            {
                sb.Append('.').Append(cls);
            }
        }

        return sb.ToString();
    }
}
