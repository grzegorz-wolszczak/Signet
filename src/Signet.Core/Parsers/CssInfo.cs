using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;

namespace Signet.Core.Parsers;

/// <summary>
/// CSS stylesheet analysis, built on the structure scanner
/// <see cref="CssTokenizer"/>. Provides the list of selectors
/// with source offsets, lookup by class/element, property values, reformatting
/// and removal of given selectors.
/// </summary>
/// <remarks>
/// The object is immutable and reflects the text passed to the constructor. After
/// <see cref="RemoveMatchingSelectors"/> or <see cref="GetReformattedCssText"/> a new
/// <see cref="CssInfo"/> should be created over the returned text.
/// </remarks>
public sealed class CssInfo
{
    private readonly string _source;
    private readonly List<CssToken> _tokens;
    private readonly List<CssSelector> _selectors;
    private readonly List<CssRule> _rules;

    /// <summary>Parses <paramref name="text"/>; <paramref name="offset"/> is added to all positions.</summary>
    public CssInfo(string text, int offset = 0)
    {
        ArgumentNullException.ThrowIfNull(text);
        _source = text;

        CssTokenizer.Result result = CssTokenizer.Tokenize(text);
        ParseErrors = result.Errors;
        ParseErrorPositions = result.ErrorPositions;

        _tokens = new List<CssToken>(result.Tokens.Count);
        foreach (CssToken tok in result.Tokens)
        {
            _tokens.Add(tok with { Pos = tok.Pos + offset });
        }

        _selectors = GenerateSelectorsList();
        _rules = GenerateRulesList();
    }

    /// <summary>Messages about unclosed constructs / structural errors (empty = structurally valid input).</summary>
    public IReadOnlyList<string> ParseErrors { get; }

    /// <summary>
    /// The source offset for each <see cref="ParseErrors"/> entry (<c>null</c> = no position, e.g.
    /// an unclosed block at the end of the input).
    /// </summary>
    public IReadOnlyList<int?> ParseErrorPositions { get; }

    /// <summary>All selectors (after splitting groups) in source order. An alias of <see cref="GetAllSelectors"/>.</summary>
    public IReadOnlyList<CssSelector> Selectors => _selectors;

    /// <summary>All rules with offsets (including <c>@font-face</c> / <c>@page</c> blocks).</summary>
    public IReadOnlyList<CssRule> Rules => _rules;

    // =====================================================================
    //  Queries
    // =====================================================================

    /// <summary>All selectors in source order.</summary>
    public IReadOnlyList<CssSelector> GetAllSelectors() => _selectors;

    /// <summary>Selectors containing a space / <c>&gt;</c> / <c>~</c> / <c>+</c>.</summary>
    public IReadOnlyList<CssSelector> GetAllSelectorsWithCombinators()
    {
        List<CssSelector> matches = new();
        foreach (CssSelector selector in _selectors)
        {
            string s = selector.Text;
            if (s.Contains(' ', StringComparison.Ordinal) || s.Contains('>', StringComparison.Ordinal)
                || s.Contains('~', StringComparison.Ordinal) || s.Contains('+', StringComparison.Ordinal))
            {
                matches.Add(selector);
            }
        }

        return matches;
    }

    /// <summary>Selectors with a non-empty <see cref="CssSelector.ClassName"/>, optionally filtered by class name.</summary>
    public IReadOnlyList<CssSelector> GetClassSelectors(string filterClassName = "")
    {
        ArgumentNullException.ThrowIfNull(filterClassName);
        List<CssSelector> selectors = new();
        foreach (CssSelector selector in _selectors)
        {
            if (selector.ClassName.Length == 0)
            {
                continue;
            }

            if (filterClassName.Length == 0 || string.Equals(selector.ClassName, filterClassName, StringComparison.Ordinal))
            {
                selectors.Add(selector);
            }
        }

        return selectors;
    }

    /// <summary>
    /// The first selector matching an element and (optionally) a class.
    /// Matching order: <c>element.class</c>, then <c>.class</c>; for an empty class — the element alone.
    /// </summary>
    public CssSelector? GetCssSelectorForElementClass(string elementName, string className)
    {
        ArgumentNullException.ThrowIfNull(elementName);
        ArgumentNullException.ThrowIfNull(className);

        if (className.Length != 0)
        {
            IReadOnlyList<CssSelector> classSelectors = GetClassSelectors(className);
            foreach (CssSelector selector in classSelectors)
            {
                if (selector.ElementName.Length == 0)
                {
                    return selector;
                }

                if (string.Equals(selector.ElementName, elementName, StringComparison.Ordinal)
                    && selector.Text.Contains(elementName + "." + className, StringComparison.Ordinal))
                {
                    return selector;
                }
            }

            return null;
        }

        foreach (CssSelector selector in _selectors)
        {
            if (string.Equals(selector.ElementName, elementName, StringComparison.Ordinal) && selector.ClassName.Length == 0)
            {
                return selector;
            }
        }

        return null;
    }

    /// <summary><em>All</em> selectors matching an element/class.</summary>
    public IReadOnlyList<CssSelector> GetAllCssSelectorsForElementClass(string elementName, string className)
    {
        ArgumentNullException.ThrowIfNull(elementName);
        ArgumentNullException.ThrowIfNull(className);

        List<CssSelector> matches = new();
        if (className.Length != 0)
        {
            foreach (CssSelector selector in GetClassSelectors(className))
            {
                if (selector.ElementName.Length == 0)
                {
                    matches.Add(selector);
                }

                if (string.Equals(selector.ElementName, elementName, StringComparison.Ordinal))
                {
                    matches.Add(selector);
                }
            }

            return matches;
        }

        foreach (CssSelector selector in _selectors)
        {
            if (string.Equals(selector.ElementName, elementName, StringComparison.Ordinal) && selector.ClassName.Length == 0)
            {
                matches.Add(selector);
            }
        }

        return matches;
    }

    /// <summary>
    /// The values of a given property from all selector rules
    /// (an empty <paramref name="property"/> = all properties). Declarations in <c>@font-face</c>
    /// etc. are not included (only the inside of selector rules counts).
    /// </summary>
    public IReadOnlyList<string> GetAllPropertyValues(string property)
    {
        ArgumentNullException.ThrowIfNull(property);

        List<string> values = new();
        bool inSelector = false;
        bool getValue = false;

        foreach (CssToken tok in _tokens)
        {
            switch (tok.Type)
            {
                case CssTokenType.Selector:
                    inSelector = true;
                    break;
                case CssTokenType.SelBlockEnd:
                    inSelector = false;
                    break;
                case CssTokenType.Property when inSelector:
                    getValue = property.Length == 0 || string.Equals(tok.Data, property, StringComparison.Ordinal);
                    break;
                case CssTokenType.PropertyValue when inSelector && getValue:
                    values.Add(tok.Data);
                    getValue = false;
                    break;
            }
        }

        return values;
    }

    /// <summary>
    /// The stylesheet reformatted into a multi-line form
    /// (each property on its own line) or a single-line form. On a structural error it returns
    /// the source text unchanged.
    /// </summary>
    public string GetReformattedCssText(bool multipleLineFormat)
    {
        CssTokenizer.Result fresh = CssTokenizer.Tokenize(_source);
        if (fresh.Errors.Count > 0)
        {
            return _source;
        }

        return CssTokenizer.Serialize(fresh.Tokens, multipleLineFormat);
    }

    /// <summary>
    /// Removes from the stylesheet the selectors whose <see cref="CssSelector.Pos"/>
    /// and <see cref="CssSelector.Text"/> match the given ones. From a group only the indicated
    /// members are removed (the rest of the group stays); an empty group → the whole rule is cut. Returns the new stylesheet text
    /// (reformatted), <c>"/* CSS */\n"</c> when nothing is left, or <c>null</c>
    /// when no selector matched.
    /// </summary>
    public string? RemoveMatchingSelectors(IEnumerable<CssSelector> selectors)
    {
        ArgumentNullException.ThrowIfNull(selectors);

        List<CssSelector> requested = selectors.ToList();
        List<CssSelector> removeSelectors = new();
        foreach (CssSelector req in requested)
        {
            foreach (CssSelector mine in _selectors)
            {
                if (mine.Pos == req.Pos && string.Equals(mine.Text, req.Text, StringComparison.Ordinal))
                {
                    removeSelectors.Add(mine);
                }
            }
        }

        if (removeSelectors.Count == 0)
        {
            return null;
        }

        removeSelectors.Sort(CssSelector.ByPosition);

        List<CssToken> newTokens = new();
        int i = 0;
        while (i < _tokens.Count)
        {
            CssToken tok = _tokens[i];
            bool storeIt = true;

            if (tok.Type == CssTokenType.Selector && !tok.Data.StartsWith('@'))
            {
                List<string> sels = SplitGroupSelectorInternal(tok.Data).ToList();

                foreach (CssSelector css in removeSelectors)
                {
                    if (css.Pos < tok.Pos)
                    {
                        continue;
                    }

                    if (css.Pos == tok.Pos)
                    {
                        int found = sels.FindIndex(s => string.Equals(css.Text, s, StringComparison.Ordinal));
                        if (found != -1)
                        {
                            sels.RemoveAt(found);
                        }
                    }

                    if (css.Pos > tok.Pos)
                    {
                        break;
                    }
                }

                if (sels.Count != 0)
                {
                    tok = tok with { Data = sels.Count == 1 ? sels[0] : string.Join(",", sels) };
                }
                else
                {
                    storeIt = false;
                    while (tok.Type != CssTokenType.SelBlockEnd)
                    {
                        i++;
                        if (i >= _tokens.Count)
                        {
                            break;
                        }

                        tok = _tokens[i];
                    }
                }
            }

            if (storeIt)
            {
                newTokens.Add(tok);
            }

            i++;
        }

        string newText = CssTokenizer.Serialize(newTokens, multiline: true);
        return newText.Length == 0 ? "/* CSS */\n" : newText;
    }

    // =====================================================================
    //  Splitting a selector group (aware of [], (), '', "")
    // =====================================================================

    /// <summary>
    /// Splits a selector group on commas,
    /// ignoring commas inside <c>[]</c>, <c>()</c> and strings. Each member is trimmed.
    /// </summary>
    public static IReadOnlyList<string> SplitGroupSelector(string selector)
    {
        ArgumentNullException.ThrowIfNull(selector);
        return SplitGroupSelectorInternal(selector);
    }

    private static List<string> SplitGroupSelectorInternal(string sel)
    {
        List<string> res = new();
        int pos = 0;
        bool insquote = false;
        bool indquote = false;
        bool inbracket = false;
        bool inparen = false;

        for (int i = 0; i < sel.Length; i++)
        {
            char c = sel[i];

            if (c == '[' && !insquote && !indquote)
            {
                inbracket = true;
            }

            if (c == ']' && !insquote && !indquote)
            {
                inbracket = false;
            }

            if (c == '(' && !insquote && !indquote)
            {
                inparen = true;
            }

            if (c == ')' && !insquote && !indquote)
            {
                inparen = false;
            }

            if (c == '\'' && insquote && !indquote)
            {
                insquote = false;
            }
            else if (c == '\'' && !insquote && !indquote)
            {
                insquote = true;
            }

            if (c == '"' && !insquote && !indquote)
            {
                indquote = true;
            }
            else if (c == '"' && !insquote && indquote)
            {
                indquote = false;
            }

            if (c == ',' && !inbracket && !inparen)
            {
                res.Add(sel[pos..i].Trim());
                pos = i + 1;
            }
            else if (i == sel.Length - 1)
            {
                res.Add(sel[pos..sel.Length].Trim());
                pos = sel.Length;
            }
        }

        return res;
    }

    // =====================================================================
    //  Building the selector and rule lists
    // =====================================================================

    private List<CssSelector> GenerateSelectorsList()
    {
        List<CssSelector> result = new();

        foreach (CssToken tok in _tokens)
        {
            if (tok.Type != CssTokenType.Selector || tok.Data.StartsWith('@'))
            {
                continue;
            }

            foreach (string asel in SplitGroupSelectorInternal(tok.Data))
            {
                bool usesPseudo = asel.Contains(':', StringComparison.Ordinal);
                bool usesCombinator = asel.Contains(' ', StringComparison.Ordinal) || asel.Contains('>', StringComparison.Ordinal)
                    || asel.Contains('~', StringComparison.Ordinal) || asel.Contains('+', StringComparison.Ordinal);

                string elementName = string.Empty;
                string className = string.Empty;

                if (!usesCombinator && !usesPseudo)
                {
                    if (asel.Contains('.', StringComparison.Ordinal))
                    {
                        string[] parts = asel.Split('.');
                        if (parts[0].Length != 0)
                        {
                            elementName = parts[0];
                        }

                        if (parts.Length > 1 && parts[1].Length != 0)
                        {
                            className = parts[1];
                        }
                    }
                    else
                    {
                        elementName = asel;
                    }
                }

                SelectorParts decomposed = DecomposeSelector(asel);
                result.Add(new CssSelector(
                    tok.Pos,
                    asel,
                    elementName,
                    className,
                    decomposed.ElementNames,
                    decomposed.ClassNames,
                    decomposed.Ids,
                    decomposed.PseudoClasses,
                    decomposed.HasCombinator,
                    usesPseudo));
            }
        }

        return result;
    }

    private List<CssRule> GenerateRulesList()
    {
        List<CssRule> result = new();
        List<string> atStack = new();

        int i = 0;
        while (i < _tokens.Count)
        {
            CssToken tok = _tokens[i];

            if (tok.Type == CssTokenType.AtRuleBegin)
            {
                atStack.Add(tok.Data);
                // An @… block without a selector (declarations directly, e.g. @font-face) → a separate rule.
                if (i + 1 < _tokens.Count && _tokens[i + 1].Type == CssTokenType.AtBlockBegin
                    && i + 2 < _tokens.Count && _tokens[i + 2].Type is CssTokenType.Property or CssTokenType.AtBlockEnd)
                {
                    int blockStart = _tokens[i + 1].Pos;
                    List<CssDeclaration> decls = new();
                    int j = i + 2;
                    while (j < _tokens.Count && _tokens[j].Type != CssTokenType.AtBlockEnd)
                    {
                        if (_tokens[j].Type == CssTokenType.Property && j + 1 < _tokens.Count
                            && _tokens[j + 1].Type == CssTokenType.PropertyValue)
                        {
                            decls.Add(MakeDeclaration(_tokens[j].Data, _tokens[j + 1].Data));
                            j += 2;
                            continue;
                        }

                        j++;
                    }

                    int blockEnd = j < _tokens.Count ? _tokens[j].Pos + 1 : _source.Length;
                    result.Add(new CssRule(string.Empty, Array.Empty<string>(), tok.Pos, blockStart, blockEnd, decls, tok.Data));
                }

                i++;
                continue;
            }

            if (tok.Type == CssTokenType.AtBlockEnd)
            {
                if (atStack.Count > 0)
                {
                    atStack.RemoveAt(atStack.Count - 1);
                }

                i++;
                continue;
            }

            if (tok.Type == CssTokenType.Selector && i + 1 < _tokens.Count && _tokens[i + 1].Type == CssTokenType.SelBlockBegin)
            {
                int selStart = tok.Pos;
                int blockStart = _tokens[i + 1].Pos;
                List<CssDeclaration> decls = new();
                int j = i + 2;
                while (j < _tokens.Count && _tokens[j].Type != CssTokenType.SelBlockEnd)
                {
                    if (_tokens[j].Type == CssTokenType.Property && j + 1 < _tokens.Count
                        && _tokens[j + 1].Type == CssTokenType.PropertyValue)
                    {
                        decls.Add(MakeDeclaration(_tokens[j].Data, _tokens[j + 1].Data));
                        j += 2;
                        continue;
                    }

                    j++;
                }

                int blockEnd = j < _tokens.Count ? _tokens[j].Pos + 1 : _source.Length;
                result.Add(new CssRule(
                    tok.Data,
                    SplitGroupSelectorInternal(tok.Data),
                    selStart,
                    blockStart,
                    blockEnd,
                    decls,
                    atStack.Count > 0 ? atStack[^1] : null));

                i = j;
                continue;
            }

            i++;
        }

        return result;
    }

    private static readonly Regex ImportantSuffix =
        new(@"!\s*important\s*$", RegexOptions.IgnoreCase | RegexOptions.Compiled);

    private static CssDeclaration MakeDeclaration(string property, string value) =>
        new(property, value, ImportantSuffix.IsMatch(value));

    // =====================================================================
    //  Parsing a single selector (best effort)
    // =====================================================================

    private readonly record struct SelectorParts(
        IReadOnlyList<string> ElementNames,
        IReadOnlyList<string> ClassNames,
        IReadOnlyList<string> Ids,
        IReadOnlyList<string> PseudoClasses,
        bool HasCombinator);

    private static SelectorParts DecomposeSelector(string sel)
    {
        List<string> elements = new();
        List<string> classes = new();
        List<string> ids = new();
        List<string> pseudos = new();
        bool hasCombinator = false;

        bool insquote = false;
        bool indquote = false;
        bool inbracket = false;
        int inparen = 0;

        for (int i = 0; i < sel.Length; i++)
        {
            char c = sel[i];

            if (insquote)
            {
                if (c == '\'')
                {
                    insquote = false;
                }

                continue;
            }

            if (indquote)
            {
                if (c == '"')
                {
                    indquote = false;
                }

                continue;
            }

            switch (c)
            {
                case '\'':
                    insquote = true;
                    continue;
                case '"':
                    indquote = true;
                    continue;
                case '[':
                    inbracket = true;
                    continue;
                case ']':
                    inbracket = false;
                    continue;
                case '(':
                    inparen++;
                    continue;
                case ')':
                    if (inparen > 0)
                    {
                        inparen--;
                    }

                    continue;
            }

            if (inbracket || inparen > 0)
            {
                continue;
            }

            if (c is ' ' or '\t' or '\n' or '\r' or '>' or '~' or '+')
            {
                hasCombinator = true;
                continue;
            }

            if (c == '.')
            {
                string name = ReadIdentifier(sel, i + 1, out int next);
                if (name.Length != 0)
                {
                    classes.Add(name);
                }

                i = next - 1;
                continue;
            }

            if (c == '#')
            {
                string name = ReadIdentifier(sel, i + 1, out int next);
                if (name.Length != 0)
                {
                    ids.Add(name);
                }

                i = next - 1;
                continue;
            }

            if (c == ':')
            {
                int start = i + 1;
                if (start < sel.Length && sel[start] == ':')
                {
                    start++;
                }

                string name = ReadIdentifier(sel, start, out int next);
                if (name.Length != 0)
                {
                    pseudos.Add(name);
                }

                i = next - 1;
                continue;
            }

            if (IsIdentStart(c) || c == '*' || c == '|')
            {
                string name = ReadTypeName(sel, i, out int next);
                if (name.Length != 0 && name != "*")
                {
                    elements.Add(name);
                }

                i = next - 1;
                continue;
            }
        }

        return new SelectorParts(elements, classes, ids, pseudos, hasCombinator);
    }

    private static string ReadIdentifier(string sel, int start, out int next)
    {
        System.Text.StringBuilder sb = new();
        int j = start;
        bool inEscape = false;
        while (j < sel.Length)
        {
            char d = sel[j];
            if (d == '\\' && !inEscape)
            {
                inEscape = true;
                sb.Append(d);
                j++;
                continue;
            }

            if (inEscape)
            {
                inEscape = false;
                sb.Append(d);
                j++;
                continue;
            }

            if (char.IsLetterOrDigit(d) || d >= (char)160 || d == '-' || d == '_')
            {
                sb.Append(d);
                j++;
                continue;
            }

            break;
        }

        next = j;
        return sb.ToString();
    }

    private static string ReadTypeName(string sel, int start, out int next)
    {
        int j = start;
        while (j < sel.Length && (IsIdentStart(sel[j]) || char.IsDigit(sel[j]) || sel[j] is '-' or '_' or '*'))
        {
            j++;
        }

        string name = sel[start..j];

        // Namespace prefix: keep only the local name (ns|rect -> rect, svg|* -> *).
        if (j < sel.Length && sel[j] == '|')
        {
            int k = j + 1;
            while (k < sel.Length && (IsIdentStart(sel[k]) || char.IsDigit(sel[k]) || sel[k] is '-' or '_' or '*'))
            {
                k++;
            }

            name = sel[(j + 1)..k];
            j = k;
        }

        next = j;
        return name;
    }

    private static bool IsIdentStart(char c) => char.IsLetter(c) || c == '_' || c >= (char)160;
}
