using System;
using System.Collections.Generic;

namespace Signet.EpubCheck.Core.Css;

/// <summary>There is no further token (Java's <c>NoSuchElementException</c> in upstream's iterator).</summary>
internal sealed class CssNoSuchElementException : Exception
{
}

/// <summary>The tokens ended inside a rule (upstream's <c>PrematureEOFException</c>).</summary>
internal sealed class CssPrematureEofException : Exception
{
}

/// <summary>The tokens of a style sheet, read through filters (upstream's <c>CssTokenList</c>).</summary>
internal sealed class CssTokenList : List<CssToken>
{
    private const int InitialSize = 2048;

    public CssTokenList()
        : base(InitialSize)
    {
    }

    /// <summary>An iterator that skips the tokens <paramref name="filter"/> does not accept.</summary>
    public CssTokenIterator Iterator(Func<CssToken, bool> filter) => new(filter, this);

    /// <summary>The filters of tokens (upstream's <c>CssTokenList.Filters</c>).</summary>
    internal static class Filters
    {
        /// <summary>Skips white space and comments.</summary>
        public static readonly Func<CssToken, bool> SpaceComment = token => !(token.Type is CssTokenType.S or CssTokenType.Comment);

        /// <summary>Skips white space, comments and the HTML comment delimiters.</summary>
        public static readonly Func<CssToken, bool> SpaceCommentCdoCdc =
            token => !(token.Type is CssTokenType.S or CssTokenType.Comment or CssTokenType.Cdo or CssTokenType.Cdc);

        /// <summary>Skips nothing.</summary>
        public static readonly Func<CssToken, bool> None = _ => true;
    }

    /// <summary>An iterator over the tokens with a filter and a look-ahead.</summary>
    internal sealed class CssTokenIterator(Func<CssToken, bool> filter, List<CssToken> list)
    {
        private int _next;
        private int _lastRet = -1;

        /// <summary>The list.</summary>
        public List<CssToken> List { get; } = list;

        /// <summary>The last token returned.</summary>
        public CssToken? Last { get; private set; }

        /// <summary>The index of the last token returned.</summary>
        public int Index => _lastRet;

        /// <summary>The default filter.</summary>
        public Func<CssToken, bool> Filter => filter;

        public CssToken Next() => Next(filter);

        public CssToken Next(Func<CssToken, bool> tokenFilter)
        {
            while (true)
            {
                int i = _next;
                if (i >= List.Count)
                {
                    throw new CssNoSuchElementException();
                }

                _next = i + 1;
                CssToken token = List[_lastRet = i];
                if (tokenFilter(token))
                {
                    Last = token;
                    return token;
                }
            }
        }

        public bool HasNext() => HasNext(filter);

        public bool HasNext(Func<CssToken, bool> tokenFilter)
        {
            int i = _next;
            while (true)
            {
                if (i == List.Count)
                {
                    return false;
                }

                if (tokenFilter(List[i]))
                {
                    return true;
                }

                i++;
            }
        }

        public CssToken Peek() => Peek(filter);

        public CssToken Peek(Func<CssToken, bool> tokenFilter)
        {
            int next = _next;
            int lastRet = _lastRet;
            CssToken? last = Last;
            if (HasNext(tokenFilter))
            {
                CssToken token = Next(tokenFilter);
                _next = next;
                _lastRet = lastRet;
                Last = last;
                return token;
            }

            throw new CssNoSuchElementException();
        }
    }
}
