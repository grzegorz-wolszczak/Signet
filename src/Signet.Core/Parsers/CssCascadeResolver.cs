using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using AngleSharp.Dom;
using Signet.Core.BookManipulation;
using Signet.Core.Resources;

namespace Signet.Core.Parsers;

/// <summary>A single declaration in a rule matched to an element, with the cascade result.</summary>
/// <param name="Property">The property name.</param>
/// <param name="Value">The value (including a possible <c>!important</c>).</param>
/// <param name="IsImportant">Whether the declaration has <c>!important</c>.</param>
/// <param name="IsOverridden">
/// Whether this declaration is overridden by another one with a higher priority in the cascade, for the same
/// property on the same element (to be struck through in the UI).
/// </param>
public readonly record struct CssMatchedDeclaration(string Property, string Value, bool IsImportant, bool IsOverridden);

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
    IReadOnlyList<CssMatchedDeclaration> Declarations);

/// <summary>The result of <see cref="CssCascadeResolver.Resolve"/> for a single element.</summary>
/// <param name="ElementBookPath">The HTML file containing the element.</param>
/// <param name="ElementOffset">The 0-based offset of the element's opening tag in <see cref="ElementBookPath"/>.</param>
/// <param name="ElementDescription">A short description of the element for the panel header, e.g. <c>p.note#foo</c>.</param>
/// <param name="MatchedRules">All matching rules (in source cascade order), with the inline style last.</param>
public sealed record CssCascadeResult(
    string ElementBookPath,
    int ElementOffset,
    string ElementDescription,
    IReadOnlyList<CssMatchedRule> MatchedRules);

/// <summary>
/// The CSS cascade/specificity engine behind the "Live CSS Panel":
/// for the element under the caret in Code View it finds all matching CSS rules (linked stylesheets +
/// <c>&lt;style&gt;</c> blocks + the inline <c>style</c> attribute), computes the specificity and marks the
/// properties overridden by a rule with a higher priority.
/// </summary>
/// <remarks>
/// <b>Deliberate simplifications:</b>
/// <list type="bullet">
/// <item>The trigger is Ctrl+click in Code View, NOT a live hover in Preview — the cascade engine
/// itself is independent of that and can be hooked up to Preview in the future without changes in this class.</item>
/// <item><c>@media</c>/<c>@import</c> are not taken into account — rules inside <c>@media</c> are
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

        List<(string BookPath, CssInfo Info)> sheets = new();
        foreach (string cssBookPath in html.GetLinkedStylesheets())
        {
            CssInfo? info = resolveCssInfo(cssBookPath);
            if (info is not null)
            {
                sheets.Add((cssBookPath, info));
            }
        }

        HtmlStyleInfo styleInfo = new(html.GetText());
        foreach (CssInfo block in styleInfo.Styles)
        {
            sheets.Add((html.BookPath, block));
        }

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
                    new CssMatchedRule(html.BookPath, elementOffset, "style=\"…\"", CssSpecificity.Inline, true, declarations),
                    sequence++));
            }
        }

        List<CssMatchedRule> finalRules = ApplyCascade(matched);
        int caretElementOffset = Math.Max(0, XhtmlDoc.OffsetFromNode(element));
        return new CssCascadeResult(html.BookPath, caretElementOffset, DescribeElement(element), finalRules);
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

    private static List<CssMatchedRule> ApplyCascade(List<(CssMatchedRule Rule, int Sequence)> matched)
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

        List<CssMatchedRule> result = new(matched.Count);
        for (int ruleIndex = 0; ruleIndex < matched.Count; ruleIndex++)
        {
            CssMatchedRule rule = matched[ruleIndex].Rule;
            List<CssMatchedDeclaration> finalDeclarations = new(rule.Declarations.Count);
            for (int declIndex = 0; declIndex < rule.Declarations.Count; declIndex++)
            {
                CssMatchedDeclaration decl = rule.Declarations[declIndex];
                bool isWinner = winners.TryGetValue(decl.Property, out var winner)
                    && winner.RuleIndex == ruleIndex
                    && winner.DeclIndex == declIndex;
                finalDeclarations.Add(decl with { IsOverridden = !isWinner });
            }

            result.Add(rule with { Declarations = finalDeclarations });
        }

        return result;
    }

    private static string DescribeElement(IElement element)
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
