using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace Signet.EpubCheck.Core.Css;

/// <summary>The kinds of CSS tokens (upstream's <c>CssToken.Type</c>).</summary>
internal enum CssTokenType
{
    S,
    Comment,
    Cdo,
    Cdc,
    Ident,
    Function,
    AtKeyword,
    Number,
    Integer,
    String,
    Uri,
    HashName,
    ClassName,
    Urange,
    Includes,
    DashMatch,
    PrefixMatch,
    SuffixMatch,
    SubstringMatch,
    QntyDimen,
    QntyPercentage,
    QntyLength,
    QntyEms,
    QntyExs,
    QntyChs,
    QntyRems,
    QntyAngle,
    QntyTime,
    QntyFreq,
    QntyResolution,
    Only,
    Not,
    And,
    Important,
    Char,
}

/// <summary>A token of a style sheet (upstream's <c>CssToken</c>).</summary>
internal sealed class CssToken : IEquatable<CssToken>
{
    internal CssToken(CssTokenType type, CssLocation location, string chars, List<CssScannerException>? errors)
    {
        Type = type;
        Location = location;
        Chars = chars;
        Errors = errors;
    }

    /// <summary>The kind.</summary>
    public CssTokenType Type { get; }

    /// <summary>Where the token starts.</summary>
    public CssLocation Location { get; }

    /// <summary>The characters of the token.</summary>
    public string Chars { get; }

    /// <summary>The errors found in the token, or <c>null</c>.</summary>
    public List<CssScannerException>? Errors { get; }

    /// <summary>Whether the scanner found errors in the token.</summary>
    public bool HasErrors => Errors is not null;

    /// <summary>The only character of the token.</summary>
    public char Char => Chars.Length == 1 ? Chars[0] : throw new InvalidOperationException("The token has not one character.");

    /// <inheritdoc />
    public bool Equals(CssToken? other) => other is not null && other.Type == Type && other.Chars == Chars && other.Location.Equals(Location);

    /// <inheritdoc />
    public override bool Equals(object? obj) => Equals(obj as CssToken);

    /// <inheritdoc />
    public override int GetHashCode() => HashCode.Combine(Type, Chars, Location);

    /// <inheritdoc />
    public override string ToString() =>
        $"CssToken{{type={Type}, value={Chars}, errors={(Errors is null ? "none" : string.Join(", ", Errors))}}}";

    /// <summary>The predicates the grammar uses to find a token (upstream's <c>CssToken.Matchers</c>).</summary>
    internal static class Matchers
    {
        public static readonly Func<CssToken, bool> SemiOpenBrace = token => IsChar(token, ';', '{');
        public static readonly Func<CssToken, bool> SemiCloseBrace = token => IsChar(token, ';', '}');
        public static readonly Func<CssToken, bool> CommaOpenBrace = token => IsChar(token, ',', '{');
        public static readonly Func<CssToken, bool> Colon = token => IsChar(token, ':');
        public static readonly Func<CssToken, bool> Pipe = token => IsChar(token, '|');
        public static readonly Func<CssToken, bool> CloseBrace = token => IsChar(token, '}');
        public static readonly Func<CssToken, bool> OpenBrace = token => IsChar(token, '{');
        public static readonly Func<CssToken, bool> CombinatorChar = token => IsChar(token, '>', '+', '~');
        public static readonly Func<CssToken, bool> Semi = token => IsChar(token, ';');
        public static readonly Func<CssToken, bool> Comma = token => IsChar(token, ',');
        public static readonly Func<CssToken, bool> CloseParen = token => IsChar(token, ')');
        public static readonly Func<CssToken, bool> OpenParen = token => IsChar(token, '(');
        public static readonly Func<CssToken, bool> StarPipe = token => IsChar(token, '*', '|');
        public static readonly Func<CssToken, bool> Star = token => IsChar(token, '*');
        public static readonly Func<CssToken, bool> OpenSquareBracket = token => IsChar(token, '[');
        public static readonly Func<CssToken, bool> CloseSquareBracket = token => IsChar(token, ']');
        public static readonly Func<CssToken, bool> StringIdent = token => token.Type is CssTokenType.Ident or CssTokenType.String;

        public static readonly Func<CssToken, bool> AttributeSelectorMatchers = token =>
            IsChar(token, '=') || token.Type is CssTokenType.Includes or CssTokenType.DashMatch or CssTokenType.PrefixMatch
                or CssTokenType.SuffixMatch or CssTokenType.SubstringMatch;

        private static bool IsChar(CssToken token, params char[] chars) => token.Type == CssTokenType.Char && Array.IndexOf(chars, token.Char) >= 0;
    }

    /// <summary>Builds a token while the scanner reads it (upstream's <c>CssToken.TokenBuilder</c>).</summary>
    internal sealed class TokenBuilder
    {
        private readonly StringBuilder _chars = new();
        private readonly ICssErrorHandler _errorHandler;
        private readonly CultureInfo? _culture;
        private readonly int _line;
        private readonly int _column;
        private readonly int _offset;
        private readonly string _systemId;

        public TokenBuilder(CssReader reader, ICssErrorHandler errorHandler, CultureInfo? culture)
        {
            _systemId = reader.SystemId;
            _line = reader.Line;
            _column = reader.Column;
            _offset = reader.Offset;
            _errorHandler = errorHandler;
            _culture = culture;
        }

        /// <summary>The kind of the token.</summary>
        public CssTokenType Type { get; set; }

        /// <summary>The errors found so far.</summary>
        public List<CssScannerException> Errors { get; } = [];

        /// <summary>The number of characters.</summary>
        public int Length => _chars.Length;

        /// <summary>The last character.</summary>
        public char Last => _chars[^1];

        public TokenBuilder Append(int ch)
        {
            _chars.Append((char)ch);
            return this;
        }

        public TokenBuilder Append(string text)
        {
            _chars.Append(text);
            return this;
        }

        public TokenBuilder Append(int[] chars)
        {
            foreach (int ch in chars)
            {
                Append(ch);
            }

            return this;
        }

        /// <summary>Records an error of the token and tells the handler (which may stop the parse by throwing).</summary>
        public void Error(CssErrorCode errorCode, CssReader reader, params object?[] arguments)
        {
            CssScannerException exception = new(errorCode, CssLocation.Create(reader), _culture, arguments);
            Errors.Add(exception);
            _errorHandler.HandleError(exception);
        }

        public CssToken AsToken() => new(Type, new CssLocation(_line, _column, _offset, _systemId), _chars.ToString(), Errors);

        public int[] ToArray()
        {
            int[] array = new int[_chars.Length];
            for (int i = 0; i < _chars.Length; i++)
            {
                array[i] = _chars[i];
            }

            return array;
        }

        public override string ToString() => _chars.ToString();
    }
}
