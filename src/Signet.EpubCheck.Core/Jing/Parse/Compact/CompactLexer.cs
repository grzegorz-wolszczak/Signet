using System;
using System.Collections.Frozen;
using System.Collections.Generic;

namespace Signet.EpubCheck.Core.Jing.Parse.Compact;

/// <summary>The kinds of the tokens of the compact syntax (the tokens of Jing's <c>CompactSyntax.jj</c>).</summary>
internal enum TokenKind
{
    Eof,

    // The keywords.
    Element,
    Attribute,
    Namespace,
    List,
    Mixed,
    Grammar,
    Empty,
    Text,
    Parent,
    External,
    NotAllowed,
    Start,
    Include,
    Default,
    Inherit,
    String,
    Token,
    Datatypes,
    Div,

    // The operators and delimiters.
    LeftBracket,
    RightBracket,
    Equals,
    InterleaveAssign,
    ChoiceAssign,
    LeftBrace,
    RightBrace,
    LeftParen,
    RightParen,
    Choice,
    Interleave,
    Comma,
    Plus,
    QuestionMark,
    Star,
    Minus,
    Tilde,
    FollowAnnotation,

    // The other tokens.
    Documentation,
    DocumentationContinue,
    DocumentationAfterSingleLineComment,
    Identifier,
    EscapedIdentifier,
    PrefixStar,
    PrefixedName,
    Literal,

    /// <summary>A character no other token starts with (Jing has this token so that its lexer never fails).</summary>
    IllegalChar,
}

/// <summary>A token with the place where it begins.</summary>
internal sealed record CompactToken(TokenKind Kind, string Image, int BeginLine, int BeginColumn);

/// <summary>
/// Splits a compact syntax schema into tokens as the token manager JavaCC makes from Jing's grammar: the longest match
/// wins, a keyword wins over an identifier of the same length, white space and comments are skipped. Jing keeps the
/// comments as "special tokens" for Trang; the schema builder throws them away, so they are skipped here. Tokens are
/// read when the parser first asks for them.
/// </summary>
internal sealed class CompactLexer(CompactCharStream input)
{
    private const char Hash = '#';
    private const char Quote = '"';
    private const char Apostrophe = '\'';
    private const char Backslash = '\\';
    private const char Colon = ':';
    private const int TripleQuoteLength = 3;

    private static readonly FrozenDictionary<string, TokenKind> s_keywords = new Dictionary<string, TokenKind>(StringComparer.Ordinal)
    {
        ["element"] = TokenKind.Element,
        ["attribute"] = TokenKind.Attribute,
        ["namespace"] = TokenKind.Namespace,
        ["list"] = TokenKind.List,
        ["mixed"] = TokenKind.Mixed,
        ["grammar"] = TokenKind.Grammar,
        ["empty"] = TokenKind.Empty,
        ["text"] = TokenKind.Text,
        ["parent"] = TokenKind.Parent,
        ["external"] = TokenKind.External,
        ["notAllowed"] = TokenKind.NotAllowed,
        ["start"] = TokenKind.Start,
        ["include"] = TokenKind.Include,
        ["default"] = TokenKind.Default,
        ["inherit"] = TokenKind.Inherit,
        ["string"] = TokenKind.String,
        ["token"] = TokenKind.Token,
        ["datatypes"] = TokenKind.Datatypes,
        ["div"] = TokenKind.Div,
    }.ToFrozenDictionary(StringComparer.Ordinal);

    private readonly List<CompactToken> _tokens = [];
    private int _pos;
    private LexicalState _state = LexicalState.Default;

    private enum LexicalState
    {
        Default,
        AfterDocumentation,
        AfterSingleLineComment,
    }

    /// <summary>Whether <paramref name="kind"/> is a keyword (Jing's <c>Keyword()</c>).</summary>
    public static bool IsKeyword(TokenKind kind) => kind is >= TokenKind.Element and <= TokenKind.Div;

    /// <summary>The token at <paramref name="index"/> (the last one is <see cref="TokenKind.Eof"/>, repeated).</summary>
    /// <exception cref="EscapeSyntaxException">The input has a character or an escape that is not allowed.</exception>
    public CompactToken Get(int index)
    {
        while (_tokens.Count <= index)
        {
            if (_tokens.Count > 0 && _tokens[^1].Kind == TokenKind.Eof)
            {
                return _tokens[^1];
            }

            _tokens.Add(Next());
        }

        return _tokens[index];
    }

    private CompactToken Next()
    {
        while (true)
        {
            if (!input.TryGet(_pos, out char c))
            {
                return new CompactToken(TokenKind.Eof, string.Empty, input.EndLine, input.EndColumn);
            }

            CompactToken? token = _state switch
            {
                LexicalState.AfterDocumentation => AfterDocumentation(c),
                LexicalState.AfterSingleLineComment => AfterSingleLineComment(c),
                _ => InDefault(c),
            };
            if (token is not null)
            {
                return token;
            }
        }
    }

    // <*> SKIP: WS, then DEFAULT. Returns null when white space or a comment was skipped.
    private CompactToken? InDefault(char c)
    {
        if (IsWhiteSpace(c))
        {
            SkipWhiteSpace();
            return null;
        }

        int start = _pos;
        switch (c)
        {
            case Hash:
                if (CharAt(_pos + 1) == Hash)
                {
                    SkipToLineEnd();
                    _state = LexicalState.AfterDocumentation;
                    return Make(TokenKind.Documentation, start);
                }

                SkipToLineEnd();
                _state = LexicalState.AfterSingleLineComment;
                return null;
            case Quote or Apostrophe:
                int end = LiteralEnd(c);
                if (end < 0)
                {
                    return Single(TokenKind.IllegalChar);
                }

                _pos = end;
                return Make(TokenKind.Literal, start);
            case Backslash:
                if (IsNameStart(CharAt(_pos + 1)))
                {
                    _pos++;
                    SkipName();
                    return Make(TokenKind.EscapedIdentifier, start);
                }

                return Single(TokenKind.IllegalChar);
            case '[': return Single(TokenKind.LeftBracket);
            case ']': return Single(TokenKind.RightBracket);
            case '=': return Single(TokenKind.Equals);
            case '{': return Single(TokenKind.LeftBrace);
            case '}': return Single(TokenKind.RightBrace);
            case '(': return Single(TokenKind.LeftParen);
            case ')': return Single(TokenKind.RightParen);
            case ',': return Single(TokenKind.Comma);
            case '+': return Single(TokenKind.Plus);
            case '?': return Single(TokenKind.QuestionMark);
            case '*': return Single(TokenKind.Star);
            case '-': return Single(TokenKind.Minus);
            case '~': return Single(TokenKind.Tilde);
            case '&': return CharAt(_pos + 1) == '=' ? Double(TokenKind.InterleaveAssign) : Single(TokenKind.Interleave);
            case '|': return CharAt(_pos + 1) == '=' ? Double(TokenKind.ChoiceAssign) : Single(TokenKind.Choice);
            case '>': return CharAt(_pos + 1) == '>' ? Double(TokenKind.FollowAnnotation) : Single(TokenKind.IllegalChar);
            default:
                return IsNameStart(c) ? Name(start) : Single(TokenKind.IllegalChar);
        }
    }

    // <AFTER_DOCUMENTATION>: another "##" line continues the documentation; anything else is white space, then DEFAULT.
    private CompactToken? AfterDocumentation(char c)
    {
        if (c is CompactCharStream.NewlineMarker or '\n')
        {
            int start = _pos;
            int afterIndent = SkipIndent(_pos + 1);
            if (CharAt(afterIndent) == Hash && CharAt(afterIndent + 1) == Hash)
            {
                _pos = afterIndent;
                SkipToLineEnd();
                return Make(TokenKind.DocumentationContinue, start);
            }
        }

        return WhiteSpaceOrIllegal(c);
    }

    // <AFTER_SINGLE_LINE_COMMENT>: a "##" line is documentation, a "#" line continues the comment.
    private CompactToken? AfterSingleLineComment(char c)
    {
        if (c is CompactCharStream.NewlineMarker or '\n')
        {
            int start = _pos;
            int afterIndent = SkipIndent(_pos + 1);
            if (CharAt(afterIndent) == Hash)
            {
                _pos = afterIndent;
                bool documentation = CharAt(afterIndent + 1) == Hash;
                SkipToLineEnd();
                if (documentation)
                {
                    _state = LexicalState.AfterDocumentation;
                    return Make(TokenKind.DocumentationAfterSingleLineComment, start);
                }

                return null;
            }
        }

        return WhiteSpaceOrIllegal(c);
    }

    private CompactToken? WhiteSpaceOrIllegal(char c)
    {
        if (IsWhiteSpace(c))
        {
            SkipWhiteSpace();
            _state = LexicalState.Default;
            return null;
        }

        return Single(TokenKind.IllegalChar);
    }

    // IDENTIFIER, a keyword, PREFIX_STAR ("p:*") or PREFIXED_NAME ("p:local").
    private CompactToken Name(int start)
    {
        SkipName();
        if (CharAt(_pos) == Colon)
        {
            char next = CharAt(_pos + 1);
            if (next == '*')
            {
                _pos += 2;
                return Make(TokenKind.PrefixStar, start);
            }

            if (IsNameStart(next))
            {
                _pos++;
                SkipName();
                return Make(TokenKind.PrefixedName, start);
            }
        }

        CompactToken token = Make(TokenKind.Identifier, start);
        return s_keywords.TryGetValue(token.Image, out TokenKind keyword) ? token with { Kind = keyword } : token;
    }

    // The end of the literal that starts here, or -1. A triple-quoted literal is the longer match when it closes: its
    // content has no run of three quotes, and it ends at the first such run.
    private int LiteralEnd(char quote)
    {
        if (CharAt(_pos + 1) == quote && CharAt(_pos + 2) == quote)
        {
            for (int i = _pos + TripleQuoteLength; input.TryGet(i, out char c); i++)
            {
                if (c == quote && CharAt(i + 1) == quote && CharAt(i + 2) == quote)
                {
                    return i + TripleQuoteLength;
                }
            }
        }

        for (int i = _pos + 1; input.TryGet(i, out char c); i++)
        {
            if (c == quote)
            {
                return i + 1;
            }

            if (c == CompactCharStream.NewlineMarker)
            {
                return -1;
            }
        }

        return -1;
    }

    private void SkipName()
    {
        _pos++;
        while (Naming.IsNameCharNs(CharAt(_pos)))
        {
            _pos++;
        }
    }

    private void SkipWhiteSpace()
    {
        while (input.TryGet(_pos, out char c) && IsWhiteSpace(c))
        {
            _pos++;
        }
    }

    private int SkipIndent(int index)
    {
        while (input.TryGet(index, out char c) && c is ' ' or '\t')
        {
            index++;
        }

        return index;
    }

    private void SkipToLineEnd()
    {
        while (input.TryGet(_pos, out char c) && c is not (CompactCharStream.NewlineMarker or '\n'))
        {
            _pos++;
        }
    }

    // The character at index, or the newline marker at the end of the input (which no token continues with).
    private char CharAt(int index) => input.TryGet(index, out char c) ? c : CompactCharStream.NewlineMarker;

    private static bool IsWhiteSpace(char c) => c is CompactCharStream.NewlineMarker or ' ' or '\n' or '\t';

    private static bool IsNameStart(char c) => Naming.IsNameStartCharNs(c);

    private CompactToken Single(TokenKind kind)
    {
        int start = _pos++;
        return Make(kind, start);
    }

    private CompactToken Double(TokenKind kind)
    {
        int start = _pos;
        _pos += 2;
        return Make(kind, start);
    }

    private CompactToken Make(TokenKind kind, int start) =>
        new(kind, input.Substring(start, _pos), input.LineAt(start), input.ColumnAt(start));
}
