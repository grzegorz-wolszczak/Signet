using System;
using System.Buffers;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using Signet.Core.BookManipulation;
using Signet.Core.Parsers;

namespace Signet.Core.Fonts;

/// <summary>Where a piece of CSS lives.</summary>
public enum FontStyleSourceKind
{
    /// <summary>A CSS stylesheet.</summary>
    Stylesheet,

    /// <summary>A <c>&lt;style&gt;</c> block of an (X)HTML file.</summary>
    StyleBlock,

    /// <summary>The <c>style</c> attribute of an element.</summary>
    StyleAttribute,
}

/// <summary>A file whose CSS <see cref="BookFontIndex"/> reads.</summary>
/// <param name="BookPath">The book path of the file.</param>
/// <param name="Text">The current text of the file.</param>
/// <param name="IsStylesheet"><c>true</c> for a CSS stylesheet, <c>false</c> for an (X)HTML file.</param>
public sealed record FontIndexSource(string BookPath, string Text, bool IsStylesheet);

/// <summary>One source of an <c>@font-face</c> <c>src</c> list (<c>url(…) format(…)</c> or <c>local(…)</c>).</summary>
/// <param name="Text">The source as written.</param>
/// <param name="Start">The offset of the source in its file.</param>
/// <param name="End">The offset right after the source.</param>
/// <param name="Url">The URL of a <c>url()</c> source as written (without quotes), or <c>null</c>.</param>
/// <param name="BookPath">The book path the URL points to (a relative URL into the book), or <c>null</c>.</param>
public sealed record FontFaceSource(string Text, int Start, int End, string? Url, string? BookPath)
{
    /// <summary>Whether this is a <c>local()</c> source (a font installed on the reader's device).</summary>
    public bool IsLocal => Text.StartsWith("local(", StringComparison.OrdinalIgnoreCase);
}

/// <summary>An <c>@font-face</c> rule found in the book.</summary>
public sealed record FontFaceRule
{
    /// <summary>The file containing the rule.</summary>
    public required string BookPath { get; init; }

    /// <summary>A stylesheet or a <c>&lt;style&gt;</c> block.</summary>
    public required FontStyleSourceKind Kind { get; init; }

    /// <summary>The offset of <c>@font-face</c>.</summary>
    public required int Start { get; init; }

    /// <summary>The offset right after the closing <c>}</c>.</summary>
    public required int End { get; init; }

    /// <summary>The declared family (normalized, see <see cref="FontFaceDescriptors.NormalizeFamily"/>); <c>""</c> when missing.</summary>
    public required string Family { get; init; }

    /// <summary>The <c>font-family</c> declaration, or <c>null</c> when missing.</summary>
    public CssScannedDeclaration? FamilyDeclaration { get; init; }

    /// <summary>The <c>font-weight</c> value as written (<c>""</c> when missing).</summary>
    public required string WeightText { get; init; }

    /// <summary>The <c>font-style</c> value as written (<c>""</c> when missing).</summary>
    public required string StyleText { get; init; }

    /// <summary>The <c>src</c> declaration, or <c>null</c> when missing.</summary>
    public CssScannedDeclaration? SrcDeclaration { get; init; }

    /// <summary>The sources of the <c>src</c> list, in order.</summary>
    public required IReadOnlyList<FontFaceSource> Sources { get; init; }

    /// <summary>The weight range the rule covers.</summary>
    public FontWeightRange Weight => FontFaceDescriptors.ParseWeight(WeightText);

    /// <summary>The normalized style (<c>normal</c>, <c>italic</c>, <c>oblique</c>).</summary>
    public string Style => FontFaceDescriptors.NormalizeStyle(StyleText);

    /// <summary>The family as compared (<see cref="FontFaceDescriptors.FamilyKey"/>).</summary>
    public string FamilyKey => FontFaceDescriptors.FamilyKey(Family);

    /// <summary>Whether the rule describes the same face (family, weight, style) as the given descriptors.</summary>
    public bool IsSameFace(string family, string weight, string style) =>
        string.Equals(FamilyKey, FontFaceDescriptors.FamilyKey(family), StringComparison.Ordinal)
        && Weight == FontFaceDescriptors.ParseWeight(weight)
        && string.Equals(Style, FontFaceDescriptors.NormalizeStyle(style), StringComparison.Ordinal);
}

/// <summary>A <c>font-family</c> or <c>font</c> declaration outside <c>@font-face</c> — a place that uses families.</summary>
public sealed record FontFamilyUsage
{
    /// <summary>The file containing the declaration.</summary>
    public required string BookPath { get; init; }

    /// <summary>A stylesheet, a <c>&lt;style&gt;</c> block or a <c>style</c> attribute.</summary>
    public required FontStyleSourceKind Kind { get; init; }

    /// <summary>The selector of the rule (for a <c>style</c> attribute: <c>style="…"</c>).</summary>
    public required string Context { get; init; }

    /// <summary><c>font-family</c> or <c>font</c> (lower case).</summary>
    public required string Property { get; init; }

    /// <summary>The offset of the property name in the file.</summary>
    public required int DeclarationStart { get; init; }

    /// <summary>The offset right after the declaration (its <c>;</c>) in the file.</summary>
    public required int DeclarationEnd { get; init; }

    /// <summary>The whole value as written (decoded for a <c>style</c> attribute).</summary>
    public required string Value { get; init; }

    /// <summary>The families of the list, with offsets in the file.</summary>
    public required IReadOnlyList<FontFamilyEntry> Families { get; init; }

    /// <summary>For the <c>font</c> shorthand: the style it sets; otherwise <c>null</c>.</summary>
    public string? ShorthandStyle { get; init; }

    /// <summary>For the <c>font</c> shorthand: the weight it sets; otherwise <c>null</c>.</summary>
    public string? ShorthandWeight { get; init; }

    /// <summary>For a <c>style</c> attribute: the offset of its value in the file; otherwise -1.</summary>
    public int AttributeValueStart { get; init; } = -1;

    /// <summary>For a <c>style</c> attribute: the offset right after its value; otherwise -1.</summary>
    public int AttributeValueEnd { get; init; } = -1;

    /// <summary>For a <c>style</c> attribute: the offset of the whitespace before the attribute name; otherwise -1.</summary>
    public int AttributeStart { get; init; } = -1;

    /// <summary>For a <c>style</c> attribute: the offset right after the closing quote; otherwise -1.</summary>
    public int AttributeEnd { get; init; } = -1;

    /// <summary>For a <c>style</c> attribute: its quote character; otherwise <c>\0</c>.</summary>
    public char AttributeQuote { get; init; }

    /// <summary>Whether <see cref="Families"/> names the family.</summary>
    public bool Uses(string familyKey) =>
        Families.Any(f => !f.IsGeneric && string.Equals(FontFaceDescriptors.FamilyKey(f.Name), familyKey, StringComparison.Ordinal));
}

/// <summary>
/// Every <c>@font-face</c> rule and every place that names font families in the book — CSS stylesheets,
/// <c>&lt;style&gt;</c> blocks and <c>style</c> attributes — with exact offsets in the current file texts, so the
/// font operations (adding faces, removing fonts, renaming families) can edit the files in place.
/// </summary>
public sealed class BookFontIndex
{
    private static readonly Regex StyleOpen = new(@"<\s*style\b[^>]*>", RegexOptions.IgnoreCase | RegexOptions.Compiled);
    private static readonly Regex StyleClose = new(@"<\s*/\s*style\s*>", RegexOptions.IgnoreCase | RegexOptions.Compiled);
    private static readonly SearchValues<char> QueryOrFragment = SearchValues.Create("?#");
    private static readonly Regex UrlFunction = new(@"url\(\s*(?:""([^""]*)""|'([^']*)'|([^)]*?))\s*\)", RegexOptions.IgnoreCase | RegexOptions.Compiled);

    private readonly List<FontFaceRule> _rules = new();
    private readonly List<FontFamilyUsage> _usages = new();
    private readonly Dictionary<string, string> _texts = new(StringComparer.Ordinal);

    private BookFontIndex(IEnumerable<FontIndexSource> sources)
    {
        foreach (FontIndexSource source in sources)
        {
            _texts[source.BookPath] = source.Text;
            string folder = BookPath.StartingDir(source.BookPath);
            if (source.IsStylesheet)
            {
                ScanCss(source.BookPath, folder, source.Text, 0, source.Text.Length, FontStyleSourceKind.Stylesheet);
            }
            else
            {
                ScanHtml(source.BookPath, folder, source.Text);
            }
        }
    }

    /// <summary>The <c>@font-face</c> rules, in file order and source order.</summary>
    public IReadOnlyList<FontFaceRule> Rules => _rules;

    /// <summary>The <c>font-family</c> / <c>font</c> declarations outside <c>@font-face</c>.</summary>
    public IReadOnlyList<FontFamilyUsage> Usages => _usages;

    /// <summary>The text of an indexed file as it was indexed (the offsets refer to it).</summary>
    public string TextOf(string bookPath) => _texts.TryGetValue(bookPath, out string? text) ? text : string.Empty;

    /// <summary>Whether a file was indexed.</summary>
    public bool Contains(string bookPath) => _texts.ContainsKey(bookPath);

    /// <summary>The indexed files.</summary>
    public IReadOnlyCollection<string> BookPaths => _texts.Keys;

    /// <summary>Indexes the current texts of the book's stylesheets and (X)HTML files.</summary>
    public static BookFontIndex Build(Book book)
    {
        ArgumentNullException.ThrowIfNull(book);
        return Build(
            book.GetCssResources().Select(css => new FontIndexSource(css.BookPath, css.GetText(), true))
                .Concat(book.GetHtmlResources().Select(html => new FontIndexSource(html.BookPath, html.GetText(), false))));
    }

    /// <summary>Indexes the given files.</summary>
    public static BookFontIndex Build(IEnumerable<FontIndexSource> sources)
    {
        ArgumentNullException.ThrowIfNull(sources);
        return new BookFontIndex(sources);
    }

    /// <summary>The rules whose <c>src</c> points to the given font file.</summary>
    public IReadOnlyList<FontFaceRule> RulesForFont(string fontBookPath) =>
        _rules.Where(r => r.Sources.Any(s => string.Equals(s.BookPath, fontBookPath, StringComparison.Ordinal))).ToList();

    /// <summary>The rules of a family (compared as CSS does, see <see cref="FontFaceDescriptors.FamilyKey"/>).</summary>
    public IReadOnlyList<FontFaceRule> RulesForFamily(string family)
    {
        string key = FontFaceDescriptors.FamilyKey(family);
        return _rules.Where(r => string.Equals(r.FamilyKey, key, StringComparison.Ordinal)).ToList();
    }

    /// <summary>The declarations that name a family.</summary>
    public IReadOnlyList<FontFamilyUsage> UsagesOfFamily(string family)
    {
        string key = FontFaceDescriptors.FamilyKey(family);
        return _usages.Where(u => u.Uses(key)).ToList();
    }

    /// <summary>The content ranges of the <c>&lt;style&gt;</c> blocks of an (X)HTML text, in source order.</summary>
    internal static IEnumerable<(int Start, int End)> StyleBlockRanges(string html)
    {
        int searchFrom = 0;
        while (searchFrom < html.Length)
        {
            Match open = StyleOpen.Match(html, searchFrom);
            if (!open.Success)
            {
                yield break;
            }

            int contentStart = open.Index + open.Length;
            Match close = StyleClose.Match(html, contentStart);
            yield return (contentStart, close.Success ? close.Index : html.Length);
            searchFrom = close.Success ? close.Index + close.Length : html.Length;
        }
    }

    private void ScanHtml(string bookPath, string folder, string text)
    {
        foreach ((int start, int end) in StyleBlockRanges(text))
        {
            ScanCss(bookPath, folder, text, start, end, FontStyleSourceKind.StyleBlock);
        }

        foreach ((int pos, int len) in TagLister.EnumerateOpeningTags(text))
        {
            string tag = text.Substring(pos, len);
            if (!tag.Contains("style", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            TagLister.AttInfo style = TagLister.ParseAttribute(tag, "style");
            if (style.Pos < 0 || style.VPos < 0)
            {
                continue;
            }

            ScanStyleAttribute(bookPath, text, pos, style);
        }
    }

    private void ScanStyleAttribute(string bookPath, string text, int tagPos, TagLister.AttInfo style)
    {
        int valueStart = tagPos + style.VPos;
        int valueEnd = valueStart + style.VLen;
        char quote = valueStart > 0 && text[valueStart - 1] is '"' or '\'' ? text[valueStart - 1] : '\0';

        // The attribute value with the quote entities decoded, and where each decoded character came from.
        (string decoded, int[] map) = DecodeAttribute(text, valueStart, valueEnd);
        int attributeStart = tagPos + style.Pos;
        while (attributeStart > 0 && char.IsWhiteSpace(text[attributeStart - 1]))
        {
            attributeStart--;
        }

        int attributeEnd = quote != '\0' ? valueEnd + 1 : valueEnd;
        foreach (CssScannedDeclaration declaration in CssDeclarationScanner.ScanDeclarations(decoded, 0, decoded.Length))
        {
            AddUsage(
                bookPath,
                FontStyleSourceKind.StyleAttribute,
                "style=\"" + decoded.Trim() + "\"",
                decoded,
                declaration,
                map,
                u => u with
                {
                    AttributeValueStart = valueStart,
                    AttributeValueEnd = valueEnd,
                    AttributeStart = attributeStart,
                    AttributeEnd = attributeEnd,
                    AttributeQuote = quote,
                });
        }
    }

    private void ScanCss(string bookPath, string folder, string text, int start, int end, FontStyleSourceKind kind)
    {
        foreach (CssScannedRule rule in CssDeclarationScanner.ScanRules(text, start, end))
        {
            if (rule.IsFontFace)
            {
                _rules.Add(BuildRule(bookPath, folder, text, kind, rule));
                continue;
            }

            if (rule.IsAtRule)
            {
                continue;
            }

            foreach (CssScannedDeclaration declaration in rule.Declarations)
            {
                AddUsage(bookPath, kind, rule.Prelude, text, declaration, null, null);
            }
        }
    }

    private void AddUsage(
        string bookPath,
        FontStyleSourceKind kind,
        string context,
        string text,
        CssScannedDeclaration declaration,
        int[]? map,
        Func<FontFamilyUsage, FontFamilyUsage>? decorate)
    {
        string property = declaration.Property.ToLowerInvariant();
        IReadOnlyList<FontFamilyEntry> families;
        string? shorthandStyle = null;
        string? shorthandWeight = null;
        if (property == "font-family")
        {
            families = FontFamilyList.Parse(text, declaration.ValueStart, declaration.ValueEnd);
        }
        else if (property == "font")
        {
            if (FontFamilyList.ParseShorthand(text, declaration.ValueStart, declaration.ValueEnd) is not { } shorthand)
            {
                return;
            }

            families = FontFamilyList.Parse(text, shorthand.FamilyStart, shorthand.FamilyEnd);
            shorthandStyle = shorthand.Style;
            shorthandWeight = shorthand.Weight;
        }
        else
        {
            return;
        }

        if (families.Count == 0)
        {
            return;
        }

        FontFamilyUsage usage = new()
        {
            BookPath = bookPath,
            Kind = kind,
            Context = context,
            Property = property,
            DeclarationStart = Map(map, declaration.Start),
            DeclarationEnd = Map(map, declaration.End),
            Value = text[declaration.ValueStart..declaration.ValueEnd],
            Families = map is null
                ? families
                : families.Select(f => f with { Start = Map(map, f.Start), End = Map(map, f.End) }).ToList(),
            ShorthandStyle = shorthandStyle,
            ShorthandWeight = shorthandWeight,
        };
        _usages.Add(decorate is null ? usage : decorate(usage));
    }

    private static FontFaceRule BuildRule(string bookPath, string folder, string text, FontStyleSourceKind kind, CssScannedRule rule)
    {
        CssScannedDeclaration? family = Last(rule, "font-family");
        CssScannedDeclaration? weight = Last(rule, "font-weight");
        CssScannedDeclaration? style = Last(rule, "font-style");
        CssScannedDeclaration? src = Last(rule, "src");

        List<FontFaceSource> sources = new();
        if (src is not null)
        {
            foreach (FontFamilyEntry item in SplitSources(text, src.ValueStart, src.ValueEnd))
            {
                string itemText = text[item.Start..item.End];
                Match url = UrlFunction.Match(itemText);
                string? rawUrl = url.Success
                    ? (url.Groups[1].Success ? url.Groups[1].Value : url.Groups[2].Success ? url.Groups[2].Value : url.Groups[3].Value).Trim()
                    : null;
                sources.Add(new FontFaceSource(itemText, item.Start, item.End, rawUrl, rawUrl is null ? null : ResolveUrl(rawUrl, folder)));
            }
        }

        return new FontFaceRule
        {
            BookPath = bookPath,
            Kind = kind,
            Start = rule.Start,
            End = rule.End,
            Family = family is null ? string.Empty : FirstFamily(text, family),
            FamilyDeclaration = family,
            WeightText = weight is null ? string.Empty : text[weight.ValueStart..weight.ValueEnd],
            StyleText = style is null ? string.Empty : text[style.ValueStart..style.ValueEnd],
            SrcDeclaration = src,
            Sources = sources,
        };
    }

    private static CssScannedDeclaration? Last(CssScannedRule rule, string property) =>
        rule.Declarations.LastOrDefault(d => string.Equals(d.Property, property, StringComparison.OrdinalIgnoreCase));

    private static string FirstFamily(string text, CssScannedDeclaration declaration) =>
        FontFamilyList.Parse(text, declaration.ValueStart, declaration.ValueEnd).FirstOrDefault()?.Name ?? string.Empty;

    // The comma-separated sources of a src value — the same splitting as a family list (strings kept whole).
    private static IEnumerable<FontFamilyEntry> SplitSources(string text, int start, int end)
    {
        int depth = 0;
        int itemStart = start;
        for (int pos = start; pos <= end; pos++)
        {
            if (pos < end)
            {
                char c = text[pos];
                if (c is '"' or '\'')
                {
                    int close = text.IndexOf(c, pos + 1);
                    pos = close < 0 || close >= end ? end - 1 : close;
                    continue;
                }

                if (c == '(')
                {
                    depth++;
                    continue;
                }

                if (c == ')')
                {
                    depth = Math.Max(0, depth - 1);
                    continue;
                }

                if (c != ',' || depth > 0)
                {
                    continue;
                }
            }

            int s = itemStart;
            int e = pos;
            while (s < e && char.IsWhiteSpace(text[s]))
            {
                s++;
            }

            while (e > s && char.IsWhiteSpace(text[e - 1]))
            {
                e--;
            }

            if (e > s)
            {
                yield return new FontFamilyEntry(text[s..e], s, e, false, false);
            }

            itemStart = pos + 1;
        }
    }

    private static string? ResolveUrl(string url, string folder)
    {
        if (url.Length == 0 || url.StartsWith("data:", StringComparison.OrdinalIgnoreCase) || url.Contains("://", StringComparison.Ordinal)
            || url.StartsWith('/') || url.StartsWith('#'))
        {
            return null;
        }

        int cut = url.AsSpan().IndexOfAny(QueryOrFragment);
        string path = cut >= 0 ? url[..cut] : url;
        return path.Length == 0 ? null : BookPath.BuildBookPath(Utility.UrlDecodePath(path), folder);
    }

    private static int Map(int[]? map, int position) => map is null ? position : map[position];

    // Decodes the quote and ampersand entities of an attribute value; map[i] is where decoded character i starts in
    // the file (map[decoded.Length] = the end of the value).
    private static (string Decoded, int[] Map) DecodeAttribute(string text, int start, int end)
    {
        StringBuilder decoded = new(end - start);
        List<int> map = new(end - start + 1);
        int pos = start;
        while (pos < end)
        {
            if (text[pos] == '&' && TryDecodeEntity(text, pos, end, out char c, out int length))
            {
                map.Add(pos);
                decoded.Append(c);
                pos += length;
                continue;
            }

            map.Add(pos);
            decoded.Append(text[pos]);
            pos++;
        }

        map.Add(end);
        return (decoded.ToString(), map.ToArray());
    }

    private static bool TryDecodeEntity(string text, int pos, int end, out char c, out int length)
    {
        foreach ((string entity, char value) in Entities)
        {
            if (pos + entity.Length <= end && string.CompareOrdinal(text, pos, entity, 0, entity.Length) == 0)
            {
                c = value;
                length = entity.Length;
                return true;
            }
        }

        c = '\0';
        length = 0;
        return false;
    }

    private static readonly (string Entity, char Value)[] Entities =
    {
        ("&quot;", '"'), ("&#34;", '"'), ("&#x22;", '"'), ("&apos;", '\''), ("&#39;", '\''), ("&#x27;", '\''), ("&amp;", '&'),
    };
}
