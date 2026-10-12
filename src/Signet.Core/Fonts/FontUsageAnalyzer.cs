using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using AngleSharp.Dom;
using AngleSharp.Html.Dom;
using Signet.Core.BookManipulation;
using Signet.Core.Parsers;

namespace Signet.Core.Fonts;

/// <summary>A face a family lacks: the reader has to imitate it (slant or embolden the regular face).</summary>
/// <param name="Family">The family (as declared in its <c>@font-face</c> rules).</param>
/// <param name="Variant">What is missing: <c>bold</c>, <c>italic</c> or <c>bold italic</c>.</param>
/// <param name="Elements">How many elements with text ask for the missing face.</param>
/// <param name="BookPath">The file of the first such element.</param>
/// <param name="Offset">The offset of the first such element in its file.</param>
public sealed record MissingFontVariant(string Family, string Variant, int Elements, string BookPath, int Offset);

/// <summary>How the elements of the book use the embedded families.</summary>
public sealed class FontUsageReport
{
    internal FontUsageReport(
        IReadOnlyDictionary<string, int> elementsByFamily,
        IReadOnlyDictionary<string, (string BookPath, int Offset)> firstElementByFamily,
        IReadOnlyList<MissingFontVariant> missing)
    {
        ElementsByFamily = elementsByFamily;
        FirstElementByFamily = firstElementByFamily;
        Missing = missing;
    }

    /// <summary>The number of elements with text rendered in each embedded family (by <see cref="FontFaceDescriptors.FamilyKey"/>).</summary>
    public IReadOnlyDictionary<string, int> ElementsByFamily { get; }

    /// <summary>The first element rendered in each embedded family (by family key).</summary>
    public IReadOnlyDictionary<string, (string BookPath, int Offset)> FirstElementByFamily { get; }

    /// <summary>The faces the families lack, with the elements asking for them.</summary>
    public IReadOnlyList<MissingFontVariant> Missing { get; }
}

/// <summary>
/// Finds out which embedded family, weight and style each element with text gets — a simplified cascade over the
/// stylesheets the document sees, its <c>&lt;style&gt;</c> blocks and <c>style</c> attributes, with inheritance and the
/// reading system defaults for <c>b</c>, <c>strong</c>, <c>h1</c>–<c>h6</c>, <c>th</c> (bold) and <c>i</c>,
/// <c>em</c>, <c>cite</c>, <c>var</c>, <c>dfn</c>, <c>address</c> (italic) — and reports the faces the families lack.
/// </summary>
/// <remarks>
/// Simplifications: rules inside <c>@media</c> are treated as applying; linked stylesheets come before the
/// <c>&lt;style&gt;</c> blocks; the first family of a list that is embedded is taken as the one used (a named font
/// that is not embedded is assumed missing on the reader, a generic family ends the search).
/// </remarks>
public static class FontUsageAnalyzer
{
    private static readonly HashSet<string> FontProperties = new(StringComparer.OrdinalIgnoreCase)
    {
        "font-family", "font-weight", "font-style", "font",
    };

    private static readonly HashSet<string> BoldElements = new(StringComparer.OrdinalIgnoreCase)
    {
        "h1", "h2", "h3", "h4", "h5", "h6", "th",
    };

    private static readonly HashSet<string> BolderElements = new(StringComparer.OrdinalIgnoreCase) { "b", "strong" };

    private static readonly HashSet<string> ItalicElements = new(StringComparer.OrdinalIgnoreCase)
    {
        "i", "em", "cite", "var", "dfn", "address",
    };

    /// <summary>Analyses the (X)HTML files of the book against the faces of <paramref name="index"/>.</summary>
    public static FontUsageReport Analyze(Book book, BookFontIndex index)
    {
        ArgumentNullException.ThrowIfNull(book);
        ArgumentNullException.ThrowIfNull(index);
        Dictionary<string, string> css = book.GetCssResources().ToDictionary(c => c.BookPath, c => c.GetText(), StringComparer.Ordinal);
        return Analyze(
            book.GetHtmlResources().Select(h => (h.BookPath, h.GetText(), book.GetVisibleStylesheets(h))),
            path => css.TryGetValue(path, out string? text) ? text : null,
            index);
    }

    /// <summary>Analyses the given documents (text and the stylesheets each one sees, in cascade order).</summary>
    public static FontUsageReport Analyze(
        IEnumerable<(string BookPath, string Text, IReadOnlyList<string> VisibleSheets)> documents,
        Func<string, string?> stylesheetText,
        BookFontIndex index)
    {
        ArgumentNullException.ThrowIfNull(documents);
        ArgumentNullException.ThrowIfNull(stylesheetText);
        ArgumentNullException.ThrowIfNull(index);

        Dictionary<string, List<FontFaceRule>> faces = index.Rules
            .Where(r => r.Family.Length > 0)
            .GroupBy(r => r.FamilyKey)
            .ToDictionary(g => g.Key, g => g.ToList(), StringComparer.Ordinal);

        Dictionary<string, int> elements = new(StringComparer.Ordinal);
        Dictionary<string, (string, int)> first = new(StringComparer.Ordinal);
        Dictionary<(string Family, string Variant), (int Count, string BookPath, int Offset)> missing = new();

        Dictionary<string, List<StyleRule>> sheetRules = new(StringComparer.Ordinal);
        foreach ((string bookPath, string text, IReadOnlyList<string> sheets) in documents)
        {
            List<StyleRule> rules = new();
            foreach (string sheet in sheets)
            {
                if (!sheetRules.TryGetValue(sheet, out List<StyleRule>? parsed))
                {
                    string? sheetText = stylesheetText(sheet);
                    parsed = sheetText is null ? new List<StyleRule>() : ParseRules(sheetText, 0, sheetText.Length).ToList();
                    sheetRules[sheet] = parsed;
                }

                rules.AddRange(parsed);
            }

            foreach ((int start, int end) in BookFontIndex.StyleBlockRanges(text))
            {
                rules.AddRange(ParseRules(text, start, end));
            }

            IHtmlDocument document = XhtmlDoc.Parse(text);
            if (document.DocumentElement is not { } root)
            {
                continue;
            }

            Dictionary<IElement, List<(StyleRule Rule, CssSpecificity Specificity)>> matches = Match(document, rules);
            Visit(root, new Computed(null, 400, "normal"), matches, element =>
            {
                Computed computed = element.Computed;
                string? family = EmbeddedFamily(computed.Families, faces);
                if (family is null)
                {
                    return;
                }

                int offset = XhtmlDoc.OffsetFromNode(element.Element);
                elements[family] = elements.TryGetValue(family, out int count) ? count + 1 : 1;
                first.TryAdd(family, (bookPath, offset));
                if (MissingVariant(faces[family], computed.Weight, computed.Style) is { } variant)
                {
                    (string, string) key = (family, variant);
                    missing[key] = missing.TryGetValue(key, out var seen)
                        ? (seen.Count + 1, seen.BookPath, seen.Offset)
                        : (1, bookPath, offset);
                }
            });
        }

        List<MissingFontVariant> missingList = missing
            .Select(m => new MissingFontVariant(faces[m.Key.Family][0].Family, m.Key.Variant, m.Value.Count, m.Value.BookPath, m.Value.Offset))
            .OrderBy(m => m.Family, StringComparer.OrdinalIgnoreCase)
            .ThenBy(m => m.Variant, StringComparer.Ordinal)
            .ToList();
        return new FontUsageReport(elements, first, missingList);
    }

    /// <summary>
    /// What a request for <paramref name="weight"/> and <paramref name="style"/> lacks among <paramref name="faces"/>:
    /// <c>bold</c>, <c>italic</c>, <c>bold italic</c>, or <c>null</c> when a real face serves it. Follows the CSS font
    /// matching: the style is chosen first (italic falls back to oblique and vice versa), then the weight; a bold
    /// request (600+) served by a face lighter than 600 is emboldened, an italic request without an italic or oblique
    /// face is slanted.
    /// </summary>
    public static string? MissingVariant(IReadOnlyList<FontFaceRule> faces, int weight, string style)
    {
        ArgumentNullException.ThrowIfNull(faces);
        ArgumentNullException.ThrowIfNull(style);
        List<FontFaceRule> styleFaces = style switch
        {
            "italic" => Prefer(faces, "italic", "oblique"),
            "oblique" => Prefer(faces, "oblique", "italic"),
            _ => Prefer(faces, "normal", "oblique", "italic"),
        };
        bool slanted = style != "normal" && styleFaces.All(f => f.Style == "normal");
        if (styleFaces.Count == 0)
        {
            styleFaces = faces.ToList();
        }

        bool emboldened = weight >= 600 && styleFaces.Count > 0 && styleFaces.Max(f => f.Weight.Max) < 600;
        if (!emboldened && !slanted)
        {
            return null;
        }

        // The face asked for (the one the family lacks), not only the part the reader imitates.
        return (weight >= 600, style != "normal") switch
        {
            (true, true) => "bold italic",
            (true, false) => "bold",
            _ => "italic",
        };
    }

    private static List<FontFaceRule> Prefer(IReadOnlyList<FontFaceRule> faces, params string[] styles)
    {
        foreach (string style in styles)
        {
            List<FontFaceRule> matching = faces.Where(f => f.Style == style).ToList();
            if (matching.Count > 0)
            {
                return matching;
            }
        }

        return faces.Where(f => f.Style == "normal").ToList();
    }

    // The first family of the list that is embedded; a generic family ends the search.
    private static string? EmbeddedFamily(IReadOnlyList<FontFamilyEntry>? families, Dictionary<string, List<FontFaceRule>> faces)
    {
        if (families is null)
        {
            return null;
        }

        foreach (FontFamilyEntry entry in families)
        {
            if (entry.IsGeneric)
            {
                return null;
            }

            string key = FontFaceDescriptors.FamilyKey(entry.Name);
            if (faces.ContainsKey(key))
            {
                return key;
            }
        }

        return null;
    }

    // For every element: the rules matching it, each with the highest specificity of its matching selectors. Each
    // selector is evaluated once per document (a selector the parser does not understand matches nothing).
    private static Dictionary<IElement, List<(StyleRule Rule, CssSpecificity Specificity)>> Match(IHtmlDocument document, List<StyleRule> rules)
    {
        Dictionary<IElement, List<(StyleRule, CssSpecificity)>> matches = new();
        for (int order = 0; order < rules.Count; order++)
        {
            // The position in the cascade decides between equal specificities.
            StyleRule rule = rules[order] with { Order = order };
            Dictionary<IElement, CssSpecificity> best = new();
            foreach ((string selector, CssSpecificity specificity) in rule.Selectors)
            {
                string stripped = CssSelectorMatching.StripDynamicPseudo(selector);
                IEnumerable<IElement> matched;
                try
                {
                    matched = stripped.Length == 0 ? document.All : document.QuerySelectorAll(stripped).ToList();
                }
                catch (Exception ex) when (ex is not OutOfMemoryException)
                {
                    continue;
                }

                foreach (IElement element in matched)
                {
                    if (!best.TryGetValue(element, out CssSpecificity current) || specificity > current)
                    {
                        best[element] = specificity;
                    }
                }
            }

            foreach ((IElement element, CssSpecificity specificity) in best)
            {
                if (!matches.TryGetValue(element, out List<(StyleRule, CssSpecificity)>? list))
                {
                    list = new List<(StyleRule, CssSpecificity)>();
                    matches[element] = list;
                }

                list.Add((rule, specificity));
            }
        }

        return matches;
    }

    private static void Visit(
        IElement element,
        Computed parent,
        Dictionary<IElement, List<(StyleRule Rule, CssSpecificity Specificity)>> matches,
        Action<(IElement Element, Computed Computed)> onText)
    {
        Computed computed = Compute(element, parent, matches.TryGetValue(element, out var matched) ? matched : null);
        if (element.ChildNodes.Any(n => n.NodeType == NodeType.Text && !string.IsNullOrWhiteSpace(n.TextContent)))
        {
            onText((element, computed));
        }

        foreach (IElement child in element.Children)
        {
            Visit(child, computed, matches, onText);
        }
    }

    private static Computed Compute(IElement element, Computed parent, List<(StyleRule Rule, CssSpecificity Specificity)>? matched)
    {
        Declared? family = null;
        Declared? weight = null;
        Declared? style = null;
        foreach ((StyleRule rule, CssSpecificity specificity) in matched ?? Enumerable.Empty<(StyleRule, CssSpecificity)>())
        {
            Apply(rule.Declarations, specificity, rule.Order, ref family, ref weight, ref style);
        }

        if (element.GetAttribute("style") is { Length: > 0 } inline)
        {
            Apply(Declarations(inline, 0, inline.Length), CssSpecificity.Inline, int.MaxValue, ref family, ref weight, ref style);
        }

        string tag = element.LocalName;
        IReadOnlyList<FontFamilyEntry>? families = family is null || IsInherit(family.Value.Value)
            ? parent.Families
            : FontFamilyList.Parse(family.Value.Value, 0, family.Value.Value.Length);

        int computedWeight = weight is not null
            ? ComputeWeight(weight.Value.Value, parent.Weight)
            : BoldElements.Contains(tag) ? 700
            : BolderElements.Contains(tag) ? ComputeWeight("bolder", parent.Weight)
            : parent.Weight;

        string computedStyle = style is not null
            ? IsInherit(style.Value.Value) ? parent.Style : FontFaceDescriptors.NormalizeStyle(style.Value.Value)
            : ItalicElements.Contains(tag) ? "italic"
            : parent.Style;

        return new Computed(families, computedWeight, computedStyle);
    }

    private static void Apply(
        IReadOnlyList<(string Property, string Value, bool Important)> declarations,
        CssSpecificity specificity,
        int order,
        ref Declared? family,
        ref Declared? weight,
        ref Declared? style)
    {
        foreach ((string property, string value, bool important) in declarations)
        {
            Declared declared = new(StripImportant(value), important, specificity, order);
            switch (property)
            {
                case "font-family":
                    family = Win(family, declared);
                    break;
                case "font-weight":
                    weight = Win(weight, declared);
                    break;
                case "font-style":
                    style = Win(style, declared);
                    break;
                case "font":
                    string text = declared.Value;
                    if (FontFamilyList.ParseShorthand(text, 0, text.Length) is { } shorthand)
                    {
                        family = Win(family, declared with { Value = text[shorthand.FamilyStart..shorthand.FamilyEnd] });
                        weight = Win(weight, declared with { Value = shorthand.Weight });
                        style = Win(style, declared with { Value = shorthand.Style });
                    }

                    break;
            }
        }
    }

    // The cascade: !important first, then specificity, then the later declaration.
    private static Declared Win(Declared? current, Declared candidate)
    {
        if (current is not { } c)
        {
            return candidate;
        }

        int byImportance = candidate.Important.CompareTo(c.Important);
        if (byImportance != 0)
        {
            return byImportance > 0 ? candidate : c;
        }

        int bySpecificity = candidate.Specificity.CompareTo(c.Specificity);
        if (bySpecificity != 0)
        {
            return bySpecificity > 0 ? candidate : c;
        }

        return candidate.Order >= c.Order ? candidate : c;
    }

    private static int ComputeWeight(string value, int parent)
    {
        string v = value.Trim().ToLowerInvariant();
        return v switch
        {
            "inherit" or "unset" => parent,
            "initial" or "normal" => 400,
            "bold" => 700,
            "bolder" => parent < 350 ? 400 : parent < 550 ? 700 : 900,
            "lighter" => parent < 550 ? 100 : parent < 750 ? 400 : 700,
            _ => double.TryParse(v, NumberStyles.Float, CultureInfo.InvariantCulture, out double n) && n is >= 1 and <= 1000
                ? (int)Math.Round(n)
                : parent,
        };
    }

    private static bool IsInherit(string value) =>
        value.Trim().Equals("inherit", StringComparison.OrdinalIgnoreCase) || value.Trim().Equals("unset", StringComparison.OrdinalIgnoreCase);

    private static string StripImportant(string value)
    {
        int bang = value.LastIndexOf('!');
        return bang >= 0 && value[(bang + 1)..].Trim().Equals("important", StringComparison.OrdinalIgnoreCase)
            ? value[..bang].TrimEnd()
            : value;
    }

    private static IEnumerable<StyleRule> ParseRules(string text, int start, int end)
    {
        foreach (CssScannedRule rule in CssDeclarationScanner.ScanRules(text, start, end))
        {
            if (rule.IsAtRule)
            {
                continue;
            }

            List<(string, string, bool)> declarations = Declarations(text, rule.Declarations);
            if (declarations.Count == 0)
            {
                continue;
            }

            List<(string, CssSpecificity)> selectors = CssInfo.SplitGroupSelector(rule.Prelude)
                .Where(s => s.Length > 0)
                .Select(s => (s, CssSpecificity.ForSelector(s)))
                .ToList();
            yield return new StyleRule(selectors, declarations, 0);
        }
    }

    private static List<(string Property, string Value, bool Important)> Declarations(string text, int start, int end) =>
        Declarations(text, CssDeclarationScanner.ScanDeclarations(text, start, end));

    private static List<(string Property, string Value, bool Important)> Declarations(string text, IReadOnlyList<CssScannedDeclaration> scanned)
    {
        List<(string, string, bool)> result = new();
        foreach (CssScannedDeclaration declaration in scanned)
        {
            string property = declaration.Property.ToLowerInvariant();
            if (!FontProperties.Contains(property))
            {
                continue;
            }

            string value = text[declaration.ValueStart..declaration.ValueEnd];
            bool important = !string.Equals(StripImportant(value), value, StringComparison.Ordinal);
            result.Add((property, value, important));
        }

        return result;
    }

    private sealed record StyleRule(
        IReadOnlyList<(string Selector, CssSpecificity Specificity)> Selectors,
        IReadOnlyList<(string Property, string Value, bool Important)> Declarations,
        int Order);

    private readonly record struct Declared(string Value, bool Important, CssSpecificity Specificity, int Order);

    private sealed record Computed(IReadOnlyList<FontFamilyEntry>? Families, int Weight, string Style);
}
