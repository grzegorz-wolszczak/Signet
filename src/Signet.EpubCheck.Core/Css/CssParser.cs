using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;

namespace Signet.EpubCheck.Core.Css;

/// <summary>What the parser tells about the content of a style sheet (upstream's <c>CssContentHandler</c>).</summary>
public interface ICssContentHandler
{
    /// <summary>The document starts.</summary>
    void StartDocument();

    /// <summary>The document ends.</summary>
    void EndDocument();

    /// <summary>An at-rule starts (its block, if any, follows).</summary>
    void StartAtRule(CssAtRule atRule);

    /// <summary>An at-rule ends.</summary>
    void EndAtRule(string name);

    /// <summary>The selectors of a rule set.</summary>
    void Selectors(IReadOnlyList<CssSelector> selectors);

    /// <summary>The rule set of the selectors ends.</summary>
    void EndSelectors(IReadOnlyList<CssSelector> selectors);

    /// <summary>A declaration.</summary>
    void Declaration(CssDeclaration declaration);
}

/// <summary>A content handler that does nothing (upstream's <c>CssDefaultHandler</c>).</summary>
public class CssDefaultHandler : ICssContentHandler
{
    /// <inheritdoc />
    public virtual void StartDocument()
    {
    }

    /// <inheritdoc />
    public virtual void EndDocument()
    {
    }

    /// <inheritdoc />
    public virtual void StartAtRule(CssAtRule atRule)
    {
    }

    /// <inheritdoc />
    public virtual void EndAtRule(string name)
    {
    }

    /// <inheritdoc />
    public virtual void Selectors(IReadOnlyList<CssSelector> selectors)
    {
    }

    /// <inheritdoc />
    public virtual void EndSelectors(IReadOnlyList<CssSelector> selectors)
    {
    }

    /// <inheritdoc />
    public virtual void Declaration(CssDeclaration declaration)
    {
    }
}

/// <summary>The parser of CSS (upstream's <c>CssParser</c>): scans a style sheet and tells a handler its rules and declarations.</summary>
public sealed class CssParser
{
    private const string CustomPropertyPrefix = "--";

    private readonly CultureInfo? _culture;
    private readonly CssSelectorConstructFactory _selectors;

    /// <summary>A parser whose error messages are in the language of <paramref name="culture"/> (the current one when <c>null</c>).</summary>
    public CssParser(CultureInfo? culture = null)
    {
        _culture = culture;
        _selectors = new CssSelectorConstructFactory(culture);
    }

    /// <summary>Parses the style sheet <paramref name="source"/>.</summary>
    public void Parse(CssSource source, ICssErrorHandler errors, ICssContentHandler document)
    {
        ArgumentNullException.ThrowIfNull(source);
        using TextReader reader = source.NewReader();
        Parse(reader, source.SystemId, errors, document);
    }

    /// <summary>Parses the style sheet <paramref name="reader"/> reads.</summary>
    public void Parse(TextReader reader, string systemId, ICssErrorHandler errors, ICssContentHandler document)
    {
        CssTokenList.CssTokenIterator iter = Scan(reader, systemId, errors);
        document.StartDocument();
        while (iter.HasNext(CssTokenList.Filters.SpaceCommentCdoCdc))
        {
            CssToken token = iter.Next(CssTokenList.Filters.SpaceCommentCdoCdc);
            try
            {
                if (token.Type == CssTokenType.AtKeyword)
                {
                    HandleAtRule(token, iter, document, errors);
                }
                else
                {
                    HandleRuleSet(token, iter, document, errors);
                }
            }
            catch (CssPrematureEofException)
            {
                break;
            }
        }

        document.EndDocument();
    }

    /// <summary>Parses the value of a <c>style</c> attribute: declarations without a rule set.</summary>
    public void ParseStyleAttribute(TextReader reader, string systemId, ICssErrorHandler errors, ICssContentHandler document)
    {
        CssTokenList.CssTokenIterator iter = Scan(reader, systemId, errors);
        document.StartDocument();
        while (iter.HasNext())
        {
            CssToken token = iter.Next();
            if (CssToken.Matchers.Semi(token))
            {
                continue; // starting with ';' is allowed, Issue 238
            }

            try
            {
                CssDeclaration? declaration = HandleDeclaration(token, iter, errors, isStyleAttribute: true);
                if (declaration is null)
                {
                    return;
                }

                document.Declaration(declaration);
            }
            catch (CssPrematureEofException)
            {
                break;
            }
        }

        document.EndDocument();
    }

    private CssTokenList.CssTokenIterator Scan(TextReader reader, string systemId, ICssErrorHandler errors)
    {
        CssTokenList tokens = [];
        new CssScanner(reader, systemId, errors, tokens.Add, _culture).Scan();
        return tokens.Iterator(CssTokenList.Filters.SpaceComment);
    }

    private void HandleRuleSet(CssToken start, CssTokenList.CssTokenIterator iter, ICssContentHandler document, ICssErrorHandler errors)
    {
        char errChar = '{';
        try
        {
            List<CssSelector>? selectors = _selectors.CreateSelectorList(start, iter, errors);
            errChar = '}';
            if (selectors is null)
            {
                iter.Next(CssToken.Matchers.CloseBrace);
                return;
            }

            if (CssToken.Matchers.CloseParen(iter.Last!))
            {
                errors.HandleError(new CssGrammarException(CssErrorCode.GrammarUnexpectedToken, iter.Last!.Location, _culture, iter.Last.Chars));
                iter.Next(CssToken.Matchers.CloseBrace);
                return;
            }

            document.Selectors(selectors);
            HandleDeclarationBlock(iter.Next(), iter, document, errors);
            document.EndSelectors(selectors);
        }
        catch (CssNoSuchElementException)
        {
            errors.HandleError(new CssGrammarException(CssErrorCode.GrammarPrematureEof, iter.Last!.Location, _culture, "'" + errChar + "'"));
            throw new CssPrematureEofException();
        }
    }

    private void HandleDeclarationBlock(CssToken start, CssTokenList.CssTokenIterator iter, ICssContentHandler document, ICssErrorHandler errors)
    {
        while (true)
        {
            if (CssToken.Matchers.CloseBrace(start))
            {
                return;
            }

            if (CssToken.Matchers.Semi(start))
            {
                start = iter.Next();
                continue;
            }

            CssDeclaration? declaration = HandleDeclaration(start, iter, errors, isStyleAttribute: false);
            try
            {
                if (declaration is not null)
                {
                    document.Declaration(declaration);
                    if (CssToken.Matchers.CloseBrace(iter.Last!))
                    {
                        return;
                    }

                    if (CssToken.Matchers.Semi(iter.Last!) && CssToken.Matchers.CloseBrace(iter.Peek()))
                    {
                        iter.Next();
                        return;
                    }

                    start = iter.Next(); // first token after ';'
                }
                else
                {
                    start = iter.Next(CssToken.Matchers.SemiCloseBrace);
                    if (CssToken.Matchers.Semi(start))
                    {
                        start = iter.Next();
                    }
                }
            }
            catch (CssNoSuchElementException)
            {
                errors.HandleError(new CssGrammarException(
                    CssErrorCode.GrammarPrematureEof, iter.Last!.Location, _culture, "';' " + CssException.Text("or", _culture) + " '}'"));
                throw new CssPrematureEofException();
            }
        }
    }

    private CssDeclaration? HandleDeclaration(CssToken name, CssTokenList.CssTokenIterator iter, ICssErrorHandler errors, bool isStyleAttribute)
    {
        if (name.Type != CssTokenType.Ident)
        {
            errors.HandleError(new CssGrammarException(
                CssErrorCode.GrammarExpectingToken, name.Location, _culture, name.Chars, CssException.Text("a_property_name", _culture)));
            return null;
        }

        CssDeclaration declaration = new(name.Chars, name.Location);
        try
        {
            if (!CssToken.Matchers.Colon(iter.Next()))
            {
                errors.HandleError(new CssGrammarException(CssErrorCode.GrammarExpectingToken, name.Location, _culture, iter.Last!.Chars, ":"));
                return null;
            }
        }
        catch (CssNoSuchElementException)
        {
            errors.HandleError(new CssGrammarException(CssErrorCode.GrammarPrematureEof, iter.Last!.Location, _culture, ":"));
            throw new CssPrematureEofException();
        }

        try
        {
            while (true)
            {
                CssToken value = iter.Next();
                if (CssToken.Matchers.SemiCloseBrace(value))
                {
                    if (declaration.Components.Count < 1 && !(declaration.Name ?? string.Empty).StartsWith(CustomPropertyPrefix, StringComparison.Ordinal))
                    {
                        errors.HandleError(new CssGrammarException(
                            CssErrorCode.GrammarExpectingToken, iter.Last!.Location, _culture, value.Char, CssException.Text("a_property_value", _culture)));
                        return null;
                    }

                    return declaration;
                }

                if (!HandlePropertyValue(declaration, value, iter, isStyleAttribute))
                {
                    errors.HandleError(new CssGrammarException(CssErrorCode.GrammarUnexpectedToken, iter.Last!.Location, _culture, iter.Last.Chars));
                    return null;
                }

                if (isStyleAttribute && !iter.HasNext())
                {
                    return declaration;
                }
            }
        }
        catch (CssNoSuchElementException)
        {
            errors.HandleError(new CssGrammarException(
                CssErrorCode.GrammarPrematureEof, iter.Last!.Location, _culture, "';' " + CssException.Text("or", _culture) + " '}'"));
            throw new CssPrematureEofException();
        }
    }

    private static bool HandlePropertyValue(CssDeclaration declaration, CssToken start, CssTokenList.CssTokenIterator iter, bool isStyleAttribute)
    {
        while (true)
        {
            if (start.Type == CssTokenType.Important)
            {
                declaration.Important = true;
            }
            else
            {
                CssConstruct? construct = CssConstructFactory.Create(start, iter, CssToken.Matchers.SemiCloseBrace, ContextRestrictions.PropertyValue);
                if (construct is null)
                {
                    return false;
                }

                declaration.Components.Add(construct);
            }

            if ((isStyleAttribute && !iter.HasNext())
                || CssToken.Matchers.Semi(iter.Peek())
                || (!isStyleAttribute && CssToken.Matchers.CloseBrace(iter.Peek())))
            {
                return declaration.Components.Count > 0;
            }

            start = iter.Next();
        }
    }

    private void HandleAtRule(CssToken start, CssTokenList.CssTokenIterator iter, ICssContentHandler document, ICssErrorHandler errors)
    {
        CssAtRule atRule = new(start.Chars, start.Location);
        try
        {
            while (true)
            {
                CssToken token = iter.Next();
                if (CssToken.Matchers.SemiOpenBrace(token))
                {
                    atRule.HasBlock = token.Char == '{';
                    break;
                }

                CssConstruct? param = CssConstructFactory.Create(token, iter, CssToken.Matchers.SemiOpenBrace, ContextRestrictions.AtRuleParam);
                if (param is null)
                {
                    errors.HandleError(new CssGrammarException(CssErrorCode.GrammarUnexpectedToken, iter.Last!.Location, _culture, iter.Last.Chars));

                    // skip the rule: up to the semicolon of a rule without a block, else to the end of its block
                    int stack = 0;
                    while (true)
                    {
                        CssToken skipped = iter.Next();
                        if (CssToken.Matchers.Semi(skipped) && stack == 0)
                        {
                            return; // a rule without a block
                        }

                        if (CssToken.Matchers.OpenBrace(skipped))
                        {
                            stack++;
                        }
                        else if (CssToken.Matchers.CloseBrace(skipped))
                        {
                            if (stack == 1)
                            {
                                break;
                            }

                            stack--;
                        }
                    }

                    return;
                }

                atRule.Components.Add(param);
            }
        }
        catch (CssNoSuchElementException)
        {
            document.StartAtRule(atRule);
            errors.HandleError(new CssGrammarException(
                CssErrorCode.GrammarPrematureEof, iter.Last!.Location, _culture, "';' " + CssException.Text("or", _culture) + " '{'"));
            document.EndAtRule(atRule.Name!);
            throw new CssPrematureEofException();
        }

        document.StartAtRule(atRule);
        if (atRule.HasBlock)
        {
            try
            {
                if (HasRuleSet(iter))
                {
                    while (!CssToken.Matchers.CloseBrace(iter.Next()))
                    {
                        if (iter.Last!.Type == CssTokenType.AtKeyword)
                        {
                            HandleAtRule(iter.Last, iter, document, errors);
                        }
                        else
                        {
                            HandleRuleSet(iter.Last, iter, document, errors);
                        }
                    }
                }
                else
                {
                    HandleDeclarationBlock(iter.Next(), iter, document, errors);
                }
            }
            catch (CssNoSuchElementException)
            {
                errors.HandleError(new CssGrammarException(CssErrorCode.GrammarPrematureEof, iter.Last!.Location, _culture, "'}'"));
                document.EndAtRule(atRule.Name!);
                throw new CssPrematureEofException();
            }
        }

        document.EndAtRule(atRule.Name!);
    }

    // Whether the block of an at-rule holds rule sets (an opening brace comes before any semicolon or closing brace).
    private static bool HasRuleSet(CssTokenList.CssTokenIterator iter)
    {
        List<CssToken> list = iter.List;
        for (int i = iter.Index + 1; i < list.Count; i++)
        {
            CssToken token = list[i];
            if (CssToken.Matchers.OpenBrace(token))
            {
                return true;
            }

            if (CssToken.Matchers.SemiCloseBrace(token))
            {
                return false;
            }
        }

        return false;
    }
}
