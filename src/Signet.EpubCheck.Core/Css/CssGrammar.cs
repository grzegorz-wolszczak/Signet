using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;

namespace Signet.EpubCheck.Core.Css;

/// <summary>The kinds of CSS constructs (upstream's <c>CssConstruct.Type</c>).</summary>
public enum CssConstructType
{
    /// <summary>A string.</summary>
    StringLiteral,

    /// <summary>A keyword or identifier.</summary>
    Keyword,

    /// <summary>A selector combinator: space, plus, greater-than, tilde.</summary>
    Combinator,

    /// <summary>An attribute match: <c>~=</c>, <c>|=</c>, <c>^=</c>, <c>$=</c>, <c>*=</c>.</summary>
    AttributeMatch,

    /// <summary><c>#ident</c>.</summary>
    HashName,

    /// <summary><c>.ident</c>.</summary>
    ClassName,

    /// <summary>A number with or without a unit.</summary>
    Quantity,

    /// <summary>A unicode range.</summary>
    Urange,

    /// <summary>A <c>url()</c>.</summary>
    Uri,

    /// <summary>A single character, such as an operator.</summary>
    Symbol,

    /// <summary><c>ns|E</c>, <c>*|E</c>, <c>|E</c>, <c>E</c>, <c>*</c>.</summary>
    TypeSelector,

    /// <summary>A pseudo class or element.</summary>
    Pseudo,

    /// <summary>An at-rule.</summary>
    AtRule,

    /// <summary>A function.</summary>
    Function,

    /// <summary>A declaration.</summary>
    Declaration,

    /// <summary>A selector.</summary>
    Selector,

    /// <summary>A sequence of simple selectors.</summary>
    SimpleSelectorSeq,

    /// <summary><c>[...]</c> of an attribute selector.</summary>
    AttributeSelector,

    /// <summary><c>(...)</c> and <c>[...]</c> that are not an attribute selector.</summary>
    ScopedGroup,
}

/// <summary>A construct of CSS: a value, a selector, a declaration, an at-rule (upstream's <c>CssGrammar.CssConstruct</c>).</summary>
public abstract class CssConstruct
{
    /// <summary>A construct of kind <paramref name="type"/> at <paramref name="location"/>.</summary>
    protected CssConstruct(CssConstructType type, CssLocation location)
    {
        Location = location ?? throw new ArgumentNullException(nameof(location));
        Type = type;
    }

    /// <summary>Where the construct starts.</summary>
    public CssLocation Location { get; }

    /// <summary>The kind.</summary>
    public CssConstructType Type { get; }

    /// <summary>The construct as CSS.</summary>
    public abstract string ToCssString();
}

/// <summary>A construct that is one token (upstream's <c>CssAtomicConstruct</c>).</summary>
public abstract class CssAtomicConstruct : CssConstruct
{
    /// <summary>An atomic construct with the text <paramref name="value"/>.</summary>
    protected CssAtomicConstruct(CssConstructType type, string value, CssLocation location)
        : base(type, location)
    {
        Value = value ?? throw new ArgumentNullException(nameof(value));
    }

    /// <summary>The text.</summary>
    protected string Value { get; }

    /// <inheritdoc />
    public override string ToCssString() => Value;

    /// <inheritdoc />
    public override string ToString() => $"{GetType().Name}{{{Value}}}";
}

/// <summary>A string.</summary>
public sealed class CssString(string value, CssLocation location) : CssAtomicConstruct(CssConstructType.StringLiteral, value, location)
{
    /// <inheritdoc />
    public override string ToCssString() => "'" + Value + "'";
}

/// <summary>A keyword.</summary>
public sealed class CssKeyword(string value, CssLocation location) : CssAtomicConstruct(CssConstructType.Keyword, value, location);

/// <summary>A <c>#name</c>.</summary>
public sealed class CssHashName(string value, CssLocation location) : CssAtomicConstruct(CssConstructType.HashName, value, location);

/// <summary>A <c>.name</c>.</summary>
public sealed class CssClassName(string value, CssLocation location) : CssAtomicConstruct(CssConstructType.ClassName, value, location);

/// <summary>A unicode range.</summary>
public sealed class CssUnicodeRange(string value, CssLocation location) : CssAtomicConstruct(CssConstructType.Urange, value, location);

/// <summary>A <c>url()</c>.</summary>
public sealed class CssURI(string value, CssLocation location) : CssAtomicConstruct(CssConstructType.Uri, value, location)
{
    /// <summary>The URI inside the parentheses, without quotes and white space around it.</summary>
    public string ToUriString()
    {
        StringBuilder builder = new();
        bool inStart = false;
        for (int i = 0; i < Value.Length; i++)
        {
            if (i > 3 && i < Value.Length - 1)
            {
                char ch = Value[i];
                if (CssScanner.Quotes.Matches(ch) || CssScanner.Whitespace.Matches(ch))
                {
                    if (inStart)
                    {
                        builder.Append(ch);
                    }
                }
                else
                {
                    inStart = true;
                    builder.Append(ch);
                }
            }
        }

        while (builder.Length > 0)
        {
            char ch = builder[^1];
            if (CssScanner.Quotes.Matches(ch) || CssScanner.Whitespace.Matches(ch))
            {
                builder.Length--;
            }
            else
            {
                break;
            }
        }

        return builder.ToString();
    }
}

/// <summary>A single character.</summary>
public sealed class CssSymbol : CssAtomicConstruct
{
    /// <summary>A symbol; <paramref name="value"/> is one character.</summary>
    public CssSymbol(string value, CssLocation location)
        : base(CssConstructType.Symbol, value, location)
    {
        if (value.Length != 1)
        {
            throw new ArgumentException("A symbol is one character.", nameof(value));
        }
    }
}

/// <summary>The kinds of selector combinators.</summary>
public enum CssCombinatorType
{
    /// <summary>White space.</summary>
    Descendant,

    /// <summary><c>&gt;</c>.</summary>
    Child,

    /// <summary><c>+</c>.</summary>
    AdjacentSibling,

    /// <summary><c>~</c>.</summary>
    GeneralSibling,
}

/// <summary>A selector combinator.</summary>
public sealed class CssSelectorCombinator : CssAtomicConstruct
{
    /// <summary>A combinator written <paramref name="value"/>.</summary>
    public CssSelectorCombinator(char value, CssLocation location)
        : base(CssConstructType.Combinator, value.ToString(), location)
    {
        CombinatorType = value switch
        {
            ' ' => CssCombinatorType.Descendant,
            '>' => CssCombinatorType.Child,
            '+' => CssCombinatorType.AdjacentSibling,
            '~' => CssCombinatorType.GeneralSibling,
            _ => throw new InvalidOperationException("Not a combinator: " + value),
        };
    }

    /// <summary>The kind of combinator.</summary>
    public CssCombinatorType CombinatorType { get; }
}

/// <summary>The kinds of attribute matches.</summary>
public enum CssAttributeMatchType
{
    /// <summary><c>=</c>.</summary>
    Equals,

    /// <summary><c>~=</c>.</summary>
    Includes,

    /// <summary><c>|=</c>.</summary>
    DashMatch,

    /// <summary><c>^=</c>.</summary>
    PrefixMatch,

    /// <summary><c>$=</c>.</summary>
    SuffixMatch,

    /// <summary><c>*=</c>.</summary>
    SubstringMatch,
}

/// <summary>The match of an attribute selector.</summary>
public sealed class CssAttributeMatchSelector : CssAtomicConstruct
{
    /// <summary>A match written <paramref name="value"/>.</summary>
    public CssAttributeMatchSelector(string value, CssAttributeMatchType type, CssLocation location)
        : base(CssConstructType.AttributeMatch, value, location)
    {
        AttributeMatchType = type;
    }

    /// <summary>The kind of match.</summary>
    public CssAttributeMatchType AttributeMatchType { get; }

    /// <inheritdoc />
    public override string ToString() => $"CssAttributeMatchSelector{{{AttributeMatchType}, {Value}}}";
}

/// <summary>A type selector.</summary>
public sealed class CssTypeSelector(string value, CssLocation location) : CssAtomicConstruct(CssConstructType.TypeSelector, value, location);

/// <summary>The units of quantities.</summary>
public enum CssQuantityUnit
{
    /// <summary>A number with an unknown unit.</summary>
    Dimen,

    /// <summary>A percentage.</summary>
    Percentage,

    /// <summary>A length.</summary>
    Length,

    /// <summary><c>em</c>.</summary>
    Ems,

    /// <summary><c>ex</c>.</summary>
    Exs,

    /// <summary><c>rem</c>.</summary>
    Rems,

    /// <summary><c>ch</c>.</summary>
    Chs,

    /// <summary>An angle.</summary>
    Angle,

    /// <summary>A time.</summary>
    Time,

    /// <summary>A frequency.</summary>
    Freq,

    /// <summary>A resolution.</summary>
    Resolution,

    /// <summary>A number.</summary>
    Number,

    /// <summary>An integer.</summary>
    WholeNumber,
}

/// <summary>A number, with a unit or not.</summary>
public sealed class CssQuantity : CssAtomicConstruct
{
    /// <summary>A quantity written <paramref name="value"/>.</summary>
    public CssQuantity(string value, CssQuantityUnit unit, CssLocation location)
        : base(CssConstructType.Quantity, value, location)
    {
        Unit = unit;
    }

    /// <summary>The unit.</summary>
    public CssQuantityUnit Unit { get; }

    /// <inheritdoc />
    public override string ToString() => $"CssQuantity{{{Unit}, {Value}}}";
}

/// <summary>A construct made of other constructs (upstream's <c>CssComposedConstruct</c>).</summary>
public abstract class CssComposedConstruct : CssConstruct
{
    /// <summary>A composed construct named <paramref name="name"/> (may be <c>null</c>).</summary>
    protected CssComposedConstruct(CssConstructType type, string? name, CssLocation location)
        : base(type, location)
    {
        NameValue = name;
    }

    /// <summary>A composed construct without a name.</summary>
    protected CssComposedConstruct(CssConstructType type, CssLocation location)
        : this(type, null, location)
    {
    }

    /// <summary>The constructs it is made of.</summary>
    public List<CssConstruct> Components { get; } = [];

    /// <summary>The name, or <c>null</c>.</summary>
    public virtual string? Name => NameValue;

    /// <summary>The name given to the constructor.</summary>
    protected string? NameValue { get; }

    /// <inheritdoc />
    public override string ToString() => $"{GetType().Name}{{{Type}, {Name}, {string.Join(" ", Components)}}}";

    /// <summary>The CSS of the components, one after the other.</summary>
    protected string JoinedComponents() => string.Concat(Components.Select(component => component.ToCssString()));
}

/// <summary>The <c>[...]</c> of an attribute selector.</summary>
public sealed class CssAttributeSelector(CssLocation location) : CssComposedConstruct(CssConstructType.AttributeSelector, location)
{
    /// <inheritdoc />
    public override string ToCssString() => "[" + JoinedComponents() + "]";
}

/// <summary>A function.</summary>
public sealed class CssFunction : CssComposedConstruct
{
    /// <summary>A function named <paramref name="name"/> (lower-cased).</summary>
    public CssFunction(string name, CssLocation location)
        : base(CssConstructType.Function, AsciiLower(name ?? throw new ArgumentNullException(nameof(name))), location)
    {
    }

    /// <inheritdoc />
    public override string ToCssString() => Name + "(" + JoinedComponents() + ")";

    internal static string AsciiLower(string text) =>
        string.Create(text.Length, text, static (span, source) =>
        {
            for (int i = 0; i < source.Length; i++)
            {
                char c = source[i];
                span[i] = c is >= 'A' and <= 'Z' ? (char)(c + ('a' - 'A')) : c;
            }
        });
}

/// <summary>An at-rule.</summary>
public sealed class CssAtRule : CssComposedConstruct
{
    /// <summary>An at-rule named <paramref name="name"/> (lower-cased, with the <c>@</c>).</summary>
    public CssAtRule(string name, CssLocation location)
        : base(CssConstructType.AtRule, CssFunction.AsciiLower(name ?? throw new ArgumentNullException(nameof(name))), location)
    {
    }

    /// <summary>Whether the rule has a block.</summary>
    public bool HasBlock { get; internal set; }

    /// <inheritdoc />
    public override string ToCssString()
    {
        StringBuilder sb = new StringBuilder().Append(Name).Append(' ');
        foreach (CssConstruct component in Components)
        {
            sb.Append(component.ToCssString()).Append(' ');
        }

        return sb.Remove(sb.Length - 1, 1).ToString();
    }
}

/// <summary>A declaration, a property and its value.</summary>
public sealed class CssDeclaration : CssComposedConstruct
{
    /// <summary>A declaration of the property <paramref name="name"/> (lower-cased).</summary>
    public CssDeclaration(string name, CssLocation location)
        : base(CssConstructType.Declaration, CssFunction.AsciiLower(name), location)
    {
    }

    /// <summary>Whether the declaration is <c>!important</c>.</summary>
    public bool Important { get; internal set; }

    /// <inheritdoc />
    public override string ToCssString()
    {
        StringBuilder sb = new StringBuilder().Append(Name).Append(" : ");
        foreach (CssConstruct component in Components)
        {
            sb.Append(component.ToCssString()).Append(' ');
        }

        if (Important)
        {
            sb.Append("!important ");
        }

        return sb.Remove(sb.Length - 1, 1).Append(" ;").ToString();
    }
}

/// <summary>The kinds of scoped groups.</summary>
public enum CssScopedGroupType
{
    /// <summary><c>( )</c>.</summary>
    Paren,

    /// <summary><c>[ ]</c>.</summary>
    Bracket,
}

/// <summary>A group in parentheses or brackets.</summary>
public sealed class CssScopedGroup : CssComposedConstruct
{
    /// <summary>A group of kind <paramref name="type"/>.</summary>
    public CssScopedGroup(CssScopedGroupType type, CssLocation location)
        : base(CssConstructType.ScopedGroup, location)
    {
        GroupType = type;
    }

    /// <summary>The kind.</summary>
    public CssScopedGroupType GroupType { get; }

    /// <inheritdoc />
    public override string ToCssString()
    {
        StringBuilder sb = new();
        sb.Append(GroupType == CssScopedGroupType.Paren ? '(' : '[');
        foreach (CssConstruct component in Components)
        {
            sb.Append(component.ToCssString()).Append(' ');
        }

        // upstream removes the last character even when the group is empty
        if (sb.Length > 0)
        {
            sb.Length--;
        }

        sb.Append(GroupType == CssScopedGroupType.Paren ? ')' : ']');
        return sb.ToString();
    }

    /// <inheritdoc />
    public override string ToString() => $"CssScopedGroup{{{Type}, {GroupType}, {string.Join(" ", Components)}}}";
}

/// <summary>The kinds of pseudo selectors.</summary>
public enum CssPseudoSelectorType
{
    /// <summary><c>::name</c>.</summary>
    PseudoElement,

    /// <summary><c>:name</c>.</summary>
    PseudoClass,
}

/// <summary>A pseudo class or element.</summary>
public sealed class CssPseudoSelector : CssComposedConstruct
{
    /// <summary>A pseudo selector of kind <paramref name="type"/>.</summary>
    public CssPseudoSelector(CssPseudoSelectorType type, CssLocation location)
        : base(CssConstructType.Pseudo, location)
    {
        SubType = type;
    }

    /// <summary>The kind.</summary>
    public CssPseudoSelectorType SubType { get; }

    /// <summary>The function, for a functional pseudo; otherwise <c>null</c>.</summary>
    public CssFunction? Function { get; internal set; }

    /// <summary>The name with its colons, for a pseudo without a function; may stay <c>null</c>.</summary>
    internal string? PlainName { get; set; }

    /// <summary>The name of the function, else the plain name (upstream fails when there is neither).</summary>
    public override string? Name => Function is not null ? Function.Name : PlainName;

    /// <inheritdoc />
    public override string ToCssString() => Function is not null ? Function.ToCssString() : PlainName ?? string.Empty;

    /// <inheritdoc />
    public override string ToString() =>
        $"CssPseudoSelector{{{SubType}, {Name}, {(Function is not null ? string.Join(" ", Function.Components) : string.Empty)}}}";
}

/// <summary>A selector.</summary>
public sealed class CssSelector(CssLocation location) : CssComposedConstruct(CssConstructType.Selector, location)
{
    /// <inheritdoc />
    public override string ToCssString() => JoinedComponents();
}

/// <summary>A sequence of simple selectors.</summary>
public sealed class CssSimpleSelectorSequence(CssLocation location) : CssComposedConstruct(CssConstructType.SimpleSelectorSeq, location)
{
    /// <inheritdoc />
    public override string ToCssString() => JoinedComponents();
}

/// <summary>Functions of the grammar.</summary>
public static class CssGrammar
{
    /// <summary>The constructs a construct is made of, all the way down.</summary>
    public static List<CssConstruct> Flatten(CssConstruct construct)
    {
        ArgumentNullException.ThrowIfNull(construct);
        return construct is CssComposedConstruct composed ? [.. composed.Components.SelectMany(Flatten)] : [construct];
    }
}

/// <summary>Builds selectors from tokens (upstream's <c>CssSelectorConstructFactory</c>).</summary>
internal sealed class CssSelectorConstructFactory(CultureInfo? culture)
{
    public CssSimpleSelectorSequence? CreateSimpleSelectorSequence(CssToken start, CssTokenList.CssTokenIterator iter, ICssErrorHandler err)
    {
        CssSimpleSelectorSequence seq = new(start.Location);
        CssConstruct? item = CreateSimpleSelector(start, iter, err);
        if (item is null)
        {
            return null;
        }

        seq.Components.Add(item);
        CssToken next = iter.Peek(CssTokenList.Filters.None);
        while (next.Type != CssTokenType.S
            && !CssToken.Matchers.Comma(next)
            && !CssToken.Matchers.OpenBrace(next)
            && !CssToken.Matchers.CloseParen(next)
            && !CssToken.Matchers.CombinatorChar(next))
        {
            item = CreateSimpleSelector(iter.Next(CssTokenList.Filters.None), iter, err);
            if (item is null)
            {
                return null;
            }

            seq.Components.Add(item);
            next = iter.Peek(CssTokenList.Filters.None);
        }

        return seq;
    }

    public List<CssSelector>? CreateSelectorList(CssToken start, CssTokenList.CssTokenIterator iter, ICssErrorHandler err) =>
        CreateSelectorList(start, iter, err, false, false, CssToken.Matchers.OpenBrace);

    private List<CssSelector>? CreateSelectorList(
        CssToken start, CssTokenList.CssTokenIterator iter, ICssErrorHandler err, bool forgiving, bool relative, Func<CssToken, bool> endMatcher)
    {
        List<CssSelector> selectors = [];
        bool end = false;
        while (true)
        {
            // comma loop
            CssSelector selector = new(start.Location);
            int selectorIndex = iter.Index;
            while (true)
            {
                // combinator loop
                if (!relative || iter.Index > selectorIndex)
                {
                    CssSimpleSelectorSequence? seq = CreateSimpleSelectorSequence(start, iter, forgiving ? ForgivingErrorHandler.Instance : err);
                    if (seq is null)
                    {
                        return null;
                    }

                    selector.Components.Add(seq);
                    start = iter.Next();
                }

                int idx = iter.Index;
                if (endMatcher(start))
                {
                    end = true;
                    break;
                }

                if (CssToken.Matchers.Comma(start))
                {
                    break;
                }

                CssSelectorCombinator? combinator = CreateCombinator(start);
                if (combinator is not null)
                {
                    selector.Components.Add(combinator);
                    start = iter.Next();
                }
                else if (iter.List[idx - 1].Type is CssTokenType.S or CssTokenType.Function)
                {
                    selector.Components.Add(new CssSelectorCombinator(' ', start.Location));
                    relative = false;
                }
                else
                {
                    err.HandleError(new CssGrammarException(CssErrorCode.GrammarUnexpectedToken, iter.Last!.Location, culture, iter.Last.Chars));
                    return null;
                }
            }

            selectors.Add(selector);
            if (end)
            {
                break;
            }

            start = iter.Next();
        }

        return selectors;
    }

    public CssConstruct? CreateSimpleSelector(CssToken start, CssTokenList.CssTokenIterator iter, ICssErrorHandler err)
    {
        if (start.Type == CssTokenType.Ident || CssToken.Matchers.StarPipe(start))
        {
            return CreateTypeSelector(start, iter, err);
        }

        if (start.Type == CssTokenType.HashName)
        {
            return new CssHashName(start.Chars, start.Location);
        }

        if (start.Type == CssTokenType.ClassName)
        {
            return new CssClassName(start.Chars, start.Location);
        }

        if (CssToken.Matchers.OpenSquareBracket(start))
        {
            return CreateAttributeSelector(iter.Next(), iter, err);
        }

        if (CssToken.Matchers.Colon(start))
        {
            return CreatePseudoSelector(start, iter, err);
        }

        if (start.Type == CssTokenType.QntyPercentage)
        {
            // a keyframe selector
            CssSelector selector = new(start.Location);
            selector.Components.Add(new CssQuantity(start.Chars, CssQuantityUnit.Percentage, start.Location));
            return selector;
        }

        err.HandleError(new CssGrammarException(CssErrorCode.GrammarUnexpectedToken, start.Location, culture, start.Chars));
        return null;
    }

    public static CssSelectorCombinator? CreateCombinator(CssToken start) =>
        start.Type == CssTokenType.Char && start.Char is '>' or '+' or '~' ? new CssSelectorCombinator(start.Char, start.Location) : null;

    public CssPseudoSelector? CreatePseudoSelector(CssToken start, CssTokenList.CssTokenIterator iter, ICssErrorHandler err)
    {
        CssPseudoSelectorType type;
        StringBuilder sb = new();
        sb.Append(start.Chars);
        CssToken next = iter.Next(CssTokenList.Filters.None);
        if (CssToken.Matchers.Colon(next))
        {
            type = CssPseudoSelectorType.PseudoElement;
            sb.Append(next.Chars);
            next = iter.Next(CssTokenList.Filters.None);
        }
        else
        {
            type = CssPseudoSelectorType.PseudoClass;
        }

        CssPseudoSelector pseudo = new(type, start.Location);
        if (next.Type == CssTokenType.Ident)
        {
            sb.Append(next.Chars);
            pseudo.PlainName = sb.ToString();
        }
        else if (next.Type == CssTokenType.Function)
        {
            CssToken token = new(next.Type, next.Location, sb + next.Chars, next.Errors);
            CssConstruct? function = CssFunction.AsciiLower(next.Chars) switch
            {
                "not(" => CreateFunctionalSelectorListPseudo(token, iter, err, false, false),
                "is(" or "where(" => CreateFunctionalSelectorListPseudo(token, iter, err, true, false),
                "has(" => CreateFunctionalSelectorListPseudo(token, iter, err, true, true),
                _ => CreateFunctionalPseudo(token, iter, CssToken.Matchers.OpenBrace),
            };
            if (function is null)
            {
                err.HandleError(new CssGrammarException(CssErrorCode.GrammarUnexpectedToken, iter.Last!.Location, culture, iter.Last.Chars, next.Chars));
                return null;
            }

            pseudo.Function = (CssFunction)function;
        }

        return pseudo;
    }

    public static CssConstruct? CreateFunctionalPseudo(CssToken start, CssTokenList.CssTokenIterator iter, Func<CssToken, bool> limit)
    {
        string name = start.Chars[..^1];
        CssFunction function = new(name, start.Location);
        CssToken token = iter.Next();
        while (!CssToken.Matchers.CloseParen(token))
        {
            if (limit(token))
            {
                return null;
            }

            CssConstruct? construct = CssConstructFactory.Create(token, iter, limit, ContextRestrictions.PseudoFunctional);
            if (construct is null)
            {
                return null;
            }

            function.Components.Add(construct);
            token = iter.Next();
        }

        return function;
    }

    public CssConstruct? CreateFunctionalSelectorListPseudo(CssToken start, CssTokenList.CssTokenIterator iter, ICssErrorHandler err, bool forgiving, bool relative)
    {
        string name = start.Chars[..^1];
        CssFunction function = new(name, start.Location);
        CssToken token = iter.Next();
        List<CssSelector>? selectors = CreateSelectorList(token, iter, err, forgiving, relative, CssToken.Matchers.CloseParen);
        if (selectors is null)
        {
            return null;
        }

        foreach (CssSelector selector in selectors)
        {
            function.Components.Add(selector);
        }

        return function;
    }

    public CssAttributeSelector? CreateAttributeSelector(CssToken start, CssTokenList.CssTokenIterator iter, ICssErrorHandler err)
    {
        CssAttributeSelector selector = new(start.Location);
        CssTypeSelector? typeSelector = CreateTypeSelector(start, iter, err);
        if (typeSelector is null)
        {
            return null;
        }

        selector.Components.Add(typeSelector);
        CssToken next = iter.Next(); // ']' or string matcher
        if (!CssToken.Matchers.CloseSquareBracket(next))
        {
            if (CssToken.Matchers.AttributeSelectorMatchers(next))
            {
                selector.Components.Add(CreateAttributeMatchSelector(next));
                next = iter.Next();
                CssConstruct? value = CssConstructFactory.Create(next, iter, CssToken.Matchers.CloseSquareBracket, ContextRestrictions.AttributeSelectorValue);
                if (value is not null)
                {
                    selector.Components.Add(value);
                }
                else
                {
                    err.HandleError(new CssGrammarException(
                        CssErrorCode.GrammarExpectingToken, next.Location, culture, next.Chars, CssException.Text("a_string_or_dentifier", culture)));
                    return null;
                }

                iter.Next(); // ']'
            }
            else
            {
                err.HandleError(new CssGrammarException(
                    CssErrorCode.GrammarExpectingToken, next.Location, culture, next.Chars, CssException.Text("an_attribute_value_matcher", culture)));
                return null;
            }
        }

        return selector;
    }

    public static CssAttributeMatchSelector CreateAttributeMatchSelector(CssToken token) => new(
        token.Chars,
        token.Type switch
        {
            CssTokenType.Includes => CssAttributeMatchType.Includes,
            CssTokenType.DashMatch => CssAttributeMatchType.DashMatch,
            CssTokenType.PrefixMatch => CssAttributeMatchType.PrefixMatch,
            CssTokenType.SuffixMatch => CssAttributeMatchType.SuffixMatch,
            CssTokenType.SubstringMatch => CssAttributeMatchType.SubstringMatch,
            _ => CssAttributeMatchType.Equals,
        },
        token.Location);

    public CssTypeSelector? CreateTypeSelector(CssToken start, CssTokenList.CssTokenIterator iter, ICssErrorHandler err)
    {
        if (start.Type != CssTokenType.Ident && !CssToken.Matchers.StarPipe(start))
        {
            err.HandleError(new CssGrammarException(
                CssErrorCode.GrammarExpectingToken, start.Location, culture, start.Chars, CssException.Text("a_type_or_universal_selector", culture)));
            return null;
        }

        StringBuilder sb = new();
        sb.Append(start.Chars);
        if (CssToken.Matchers.Pipe(start))
        {
            CssToken next = iter.Peek(CssTokenList.Filters.None);
            if (next.Type != CssTokenType.Ident)
            {
                err.HandleError(new CssGrammarException(
                    CssErrorCode.GrammarExpectingToken, next.Location, culture, next.Chars, CssException.Text("a_type_or_universal_selector", culture)));
                return null;
            }

            sb.Append(iter.Next().Chars);
        }
        else if (CssToken.Matchers.Pipe(iter.Peek(CssTokenList.Filters.None)))
        {
            sb.Append(iter.Next().Chars);
            CssToken next = iter.Next(CssTokenList.Filters.None);
            if (next.Type != CssTokenType.Ident && !CssToken.Matchers.Star(next))
            {
                err.HandleError(new CssGrammarException(
                    CssErrorCode.GrammarExpectingToken, start.Location, culture, next.Chars, CssException.Text("a_type_or_universal_selector", culture)));
                return null;
            }

            sb.Append(next.Chars);
        }
        else if (iter.Peek(CssTokenList.Filters.None).Type == CssTokenType.Ident)
        {
            sb.Append(iter.Next().Chars);
        }

        return new CssTypeSelector(sb.ToString(), start.Location);
    }
}

/// <summary>What a construct may be in each place of the grammar (upstream's <c>CssParser.ContextRestrictions</c>).</summary>
internal static class ContextRestrictions
{
    public static readonly Func<CssConstruct, bool> Function = construct => construct.Type != CssConstructType.AtRule;
    public static readonly Func<CssConstruct, bool> AtRuleParam = _ => true;
    public static readonly Func<CssConstruct, bool> PropertyValue = construct => construct.Type != CssConstructType.AtRule;
    public static readonly Func<CssConstruct, bool> AttributeSelectorValue = construct => construct.Type is CssConstructType.Keyword or CssConstructType.StringLiteral;

    public static readonly Func<CssConstruct, bool> PseudoFunctional = construct =>
        construct.Type is CssConstructType.Keyword or CssConstructType.StringLiteral or CssConstructType.Quantity
        || (construct.Type == CssConstructType.Symbol && (construct.ToCssString() == "+" || construct.ToCssString() == "-"));
}

/// <summary>Builds the constructs of values from tokens (upstream's <c>CssConstructFactory</c>).</summary>
internal static class CssConstructFactory
{
    private delegate CssConstruct? Transform(CssToken start, CssTokenList.CssTokenIterator iter, Func<CssToken, bool> limit, Func<CssConstruct, bool> permitted);

    public static CssConstruct? Create(CssToken start, CssTokenList.CssTokenIterator iter, Func<CssToken, bool> limit, Func<CssConstruct, bool> permitted) =>
        TransformOf(start.Type)?.Invoke(start, iter, limit, permitted);

    private static Transform? TransformOf(CssTokenType type) => type switch
    {
        CssTokenType.Function => BuildFunction,
        CssTokenType.Char => BuildChar,
        CssTokenType.Ident or CssTokenType.Uri or CssTokenType.String or CssTokenType.And or CssTokenType.Not or CssTokenType.Only
            or CssTokenType.Urange or CssTokenType.HashName or CssTokenType.ClassName => BuildAtomic,
        CssTokenType.QntyAngle or CssTokenType.QntyDimen or CssTokenType.QntyRems or CssTokenType.QntyEms or CssTokenType.QntyExs
            or CssTokenType.QntyChs or CssTokenType.QntyFreq or CssTokenType.QntyLength or CssTokenType.QntyPercentage
            or CssTokenType.QntyResolution or CssTokenType.QntyTime or CssTokenType.Number or CssTokenType.Integer => BuildQuantity,
        _ => null,
    };

    private static CssFunction? BuildFunction(CssToken start, CssTokenList.CssTokenIterator iter, Func<CssToken, bool> limit, Func<CssConstruct, bool> permitted)
    {
        string name = start.Chars[..^1];
        CssFunction function = new(name, start.Location);
        CssToken token = iter.Next();
        while (!CssToken.Matchers.CloseParen(token))
        {
            if (limit(token))
            {
                return null;
            }

            CssConstruct? construct = Create(token, iter, limit, ContextRestrictions.Function);
            if (construct is null || !permitted(construct))
            {
                return null;
            }

            function.Components.Add(construct);
            token = iter.Next();
        }

        return function;
    }

    private static CssConstruct? BuildAtomic(CssToken start, CssTokenList.CssTokenIterator iter, Func<CssToken, bool> limit, Func<CssConstruct, bool> permitted)
    {
        CssConstruct construct = start.Type switch
        {
            CssTokenType.Ident or CssTokenType.And or CssTokenType.Not or CssTokenType.Only => new CssKeyword(start.Chars, start.Location),
            CssTokenType.Uri => new CssURI(start.Chars, start.Location),
            CssTokenType.String => new CssString(start.Chars, start.Location),
            CssTokenType.Urange => new CssUnicodeRange(start.Chars, start.Location),
            CssTokenType.HashName => new CssHashName(start.Chars, start.Location),
            CssTokenType.ClassName => new CssClassName(start.Chars, start.Location),
            _ => throw new InvalidOperationException("CssTokenTransform BUILDER_ATOMIC"),
        };
        return permitted(construct) ? construct : null;
    }

    private static CssConstruct? BuildChar(CssToken start, CssTokenList.CssTokenIterator iter, Func<CssToken, bool> limit, Func<CssConstruct, bool> permitted)
    {
        char ch = start.Char;
        if (ch is '{' or '}' or ';')
        {
            return null;
        }

        CssConstruct? result = ch is '(' or '[' ? BuildScopedGroup(start, iter, limit, permitted) : new CssSymbol(start.Chars, start.Location);
        return result is not null && permitted(result) ? result : null;
    }

    private static CssScopedGroup? BuildScopedGroup(CssToken start, CssTokenList.CssTokenIterator iter, Func<CssToken, bool> limit, Func<CssConstruct, bool> permitted)
    {
        CssScopedGroupType type;
        Func<CssToken, bool> end;
        if (CssToken.Matchers.OpenParen(start))
        {
            type = CssScopedGroupType.Paren;
            end = CssToken.Matchers.CloseParen;
        }
        else if (CssToken.Matchers.OpenSquareBracket(start))
        {
            type = CssScopedGroupType.Bracket;
            end = CssToken.Matchers.CloseSquareBracket;
        }
        else
        {
            throw new InvalidOperationException();
        }

        CssScopedGroup group = new(type, start.Location);
        CssToken token = iter.Next();
        while (!end(token))
        {
            if (limit(token))
            {
                return null;
            }

            CssConstruct? construct = Create(token, iter, limit, ContextRestrictions.Function);
            if (construct is null || !permitted(construct))
            {
                return null;
            }

            group.Components.Add(construct);
            token = iter.Next();
        }

        return group;
    }

    private static CssQuantity? BuildQuantity(CssToken start, CssTokenList.CssTokenIterator iter, Func<CssToken, bool> limit, Func<CssConstruct, bool> permitted)
    {
        CssQuantity quantity = new(
            start.Chars,
            start.Type switch
            {
                CssTokenType.QntyAngle => CssQuantityUnit.Angle,
                CssTokenType.QntyDimen => CssQuantityUnit.Dimen,
                CssTokenType.QntyRems => CssQuantityUnit.Rems,
                CssTokenType.QntyEms => CssQuantityUnit.Ems,
                CssTokenType.QntyExs => CssQuantityUnit.Exs,
                CssTokenType.QntyChs => CssQuantityUnit.Chs,
                CssTokenType.QntyFreq => CssQuantityUnit.Freq,
                CssTokenType.QntyLength => CssQuantityUnit.Length,
                CssTokenType.QntyPercentage => CssQuantityUnit.Percentage,
                CssTokenType.QntyResolution => CssQuantityUnit.Resolution,
                CssTokenType.QntyTime => CssQuantityUnit.Time,
                CssTokenType.Number => CssQuantityUnit.Number,
                _ => CssQuantityUnit.WholeNumber,
            },
            start.Location);
        return permitted(quantity) ? quantity : null;
    }
}
