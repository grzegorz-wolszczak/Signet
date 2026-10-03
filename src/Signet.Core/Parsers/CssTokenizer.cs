using System;
using System.Collections.Generic;
using System.Text;
using Signet.Core.Localization;

namespace Signet.Core.Parsers;

/// <summary>
/// A CSS structure scanner. It does not parse the full CSS grammar: it recognizes
/// <c>@…</c> rules (statement and block), selector groups, <c>{ }</c> blocks, declarations
/// <c>property: value;</c> and comments — enough for <see cref="CssInfo"/> to
/// list the selectors and their offsets and to reformat a stylesheet.
/// </summary>
/// <remarks>
/// Known limitations:
/// <list type="bullet">
/// <item>A comment <em>inside</em> a declaration (<c>color: /* x */ red</c>) is kept
/// literally in the value, not as a separate token — there is no special "tail comment" formatting
/// for such a case.</item>
/// <item>The classification of the content of an <c>@…</c> block (declarations vs nested rules) comes from
/// a look-ahead scan (<c>{</c> before <c>;</c>/<c>}</c>), not from the rule name.</item>
/// <item>Escaping and whitespace normalization in identifiers/strings is simpler
/// than in a full CSS tokenizer.</item>
/// </list>
/// </remarks>
internal static class CssTokenizer
{
    /// <summary>The tokenization result: the token stream (terminated by <see cref="CssTokenType.CssEnd"/>) and the list of structural errors.</summary>
    /// <param name="Tokens">The tokens in source order; the last one has type <see cref="CssTokenType.CssEnd"/>.</param>
    /// <param name="Errors">Messages (in the interface language) about unclosed constructs (block / string / comment) and excess <c>}</c>.</param>
    /// <param name="ErrorPositions">
    /// The source offset (after CRLF → LF normalization) for each <paramref name="Errors"/> entry, or
    /// <c>null</c> when the error has no position (e.g. an unclosed block at the end of the input). Kept separate from the text,
    /// so that translated messages need not be parsed.
    /// </param>
    public readonly record struct Result(IReadOnlyList<CssToken> Tokens, IReadOnlyList<string> Errors, IReadOnlyList<int?> ErrorPositions);

    private static readonly char[] Whitespace = { ' ', '\t', '\r', '\n', '\f', '\v' };

    /// <summary>
    /// Tokenizes <paramref name="source"/>. The source is normalized
    /// (<c>\r\n</c> → <c>\n</c>, a trailing <c>\n</c> appended); token offsets are relative to that
    /// normalized form.
    /// </summary>
    public static Result Tokenize(string source)
    {
        ArgumentNullException.ThrowIfNull(source);
        return new Scanner(source).Run();
    }

    /// <summary>
    /// Serializes a token stream back to CSS, using the multi-line / single-line templates.
    /// </summary>
    /// <param name="tokens">The tokens (e.g. from <see cref="Tokenize"/> or after modification for removing selectors).</param>
    /// <param name="multiline">
    /// <c>true</c> → the multi-line format (each property on its own line); <c>false</c> → single-line.
    /// </param>
    public static string Serialize(IReadOnlyList<CssToken> tokens, bool multiline)
    {
        ArgumentNullException.ThrowIfNull(tokens);

        // The templates for each token type (index = token type).
        string[] t = multiline
            ? new[] { "  ", " {\n", "", " {\n", "", " ", ";\n", "}\n", "\n", "}\n\n", "", "", "\n" }
            : new[] { "", "{", "", "{", "", "", ";", "}", "\n", "}\n", "", "", "\n" };

        StringBuilder output = new();
        int lvl = 0;
        bool tailComment = false;

        for (int i = 0; i < tokens.Count; i++)
        {
            CssToken tok = tokens[i];
            switch (tok.Type)
            {
                case CssTokenType.CharsetAt:
                    output.Append("@charset ").Append(tok.Data).Append(t[6]);
                    break;

                case CssTokenType.ImportAt:
                    output.Append(Indent(lvl, t[0])).Append("@import ").Append(tok.Data).Append(t[6]);
                    break;

                case CssTokenType.NamespaceAt:
                    output.Append("@namespace ").Append(tok.Data).Append(t[6]);
                    break;

                case CssTokenType.LayerAt:
                    output.Append("@layer ").Append(tok.Data).Append(t[6]);
                    break;

                case CssTokenType.AtRuleBegin:
                    output.Append(Indent(lvl, t[0])).Append(tok.Data).Append(t[1]);
                    break;

                case CssTokenType.AtRuleUnknown:
                    // A statement (without a block) is terminated with ';', not ' {'.
                    output.Append(Indent(lvl, t[0])).Append(tok.Data)
                          .Append(NextNonCommentIs(tokens, i, CssTokenType.AtBlockBegin) ? t[1] : t[6]);
                    break;

                case CssTokenType.Selector:
                case CssTokenType.InvalidRule:
                    output.Append(Indent(lvl, t[0]))
                          .Append(JoinSelectorGroup(tok.Data, "," + t[2]))
                          .Append(t[3]);
                    break;

                case CssTokenType.Property:
                    output.Append(Indent(lvl, t[0])).Append(tok.Data).Append(':').Append(t[5]);
                    break;

                case CssTokenType.PropertyValue:
                    if (i + 1 < tokens.Count && tokens[i + 1].Type == CssTokenType.Comment && tokens[i + 1].Line == tok.Line)
                    {
                        tailComment = true;
                        output.Append(tok.Data).Append(';');
                    }
                    else
                    {
                        output.Append(tok.Data).Append(t[6]);
                    }

                    break;

                case CssTokenType.SelBlockBegin:
                case CssTokenType.AtBlockBegin:
                    lvl++;
                    break;

                case CssTokenType.SelBlockEnd:
                    lvl = Math.Max(0, lvl - 1);
                    output.Append(Indent(lvl, t[0])).Append(t[7]);
                    if (lvl == 0)
                    {
                        output.Append(t[8]);
                    }

                    break;

                case CssTokenType.AtBlockEnd:
                    lvl = Math.Max(0, lvl - 1);
                    output.Append(Indent(lvl, t[0])).Append(t[9]);
                    break;

                case CssTokenType.Comment:
                    if (multiline || lvl == 0)
                    {
                        output.Append(tailComment ? " " : Indent(lvl, t[0])).Append(t[11]).Append(tok.Data).Append(t[12]);
                    }
                    else
                    {
                        output.Append(t[11]).Append(tok.Data);
                    }

                    tailComment = false;
                    break;

                case CssTokenType.CssEnd:
                    break;
            }
        }

        return output.ToString().Trim(Whitespace);
    }

    private static string Indent(int lvl, string baseIndent)
    {
        if (lvl <= 0 || baseIndent.Length == 0)
        {
            return string.Empty;
        }

        StringBuilder sb = new(baseIndent.Length * lvl);
        for (int i = 0; i < lvl; i++)
        {
            sb.Append(baseIndent);
        }

        return sb.ToString();
    }

    private static bool NextNonCommentIs(IReadOnlyList<CssToken> tokens, int from, CssTokenType type)
    {
        for (int i = from + 1; i < tokens.Count; i++)
        {
            if (tokens[i].Type == CssTokenType.Comment)
            {
                continue;
            }

            return tokens[i].Type == type;
        }

        return false;
    }

    /// <summary>
    /// Splits on commas (empty members are dropped) and joins with the separator.
    /// </summary>
    private static string JoinSelectorGroup(string data, string separator)
    {
        List<string> parts = new();
        foreach (string part in data.Split(','))
        {
            if (part.Length != 0)
            {
                parts.Add(part);
            }
        }

        return string.Join(separator, parts);
    }

    // =====================================================================
    //  The scanner proper
    // =====================================================================

    private sealed class Scanner
    {
        private readonly string _src;
        private readonly List<CssToken> _tokens = new();
        private readonly List<string> _errors = new();
        private readonly List<int?> _errorPositions = new();
        private int _i;

        public Scanner(string source)
        {
            string normalized = source.Replace("\r\n", "\n", StringComparison.Ordinal);
            _src = normalized + "\n";
        }

        public Result Run()
        {
            ParseItems(insideBlock: false);
            Emit(CssTokenType.CssEnd, _src.Length, string.Empty);
            return new Result(_tokens, _errors, _errorPositions);
        }

        private char Cur => _i < _src.Length ? _src[_i] : '\0';

        private void AddError(string message, int? position)
        {
            _errors.Add(message);
            _errorPositions.Add(position);
        }

        private char At(int idx) => idx >= 0 && idx < _src.Length ? _src[idx] : '\0';

        private bool Eof => _i >= _src.Length;

        private void Emit(CssTokenType type, int pos, string data)
        {
            (int line, int col) = LineCol(pos);
            _tokens.Add(new CssToken(type, pos, line, col, data));
        }

        private (int Line, int Col) LineCol(int pos)
        {
            int line = 1;
            int lineStart = 0;
            int limit = Math.Min(pos, _src.Length);
            for (int k = 0; k < limit; k++)
            {
                if (_src[k] == '\n')
                {
                    line++;
                    lineStart = k + 1;
                }
            }

            return (line, pos - lineStart);
        }

        private void SkipWhitespace()
        {
            while (!Eof && Array.IndexOf(Whitespace, Cur) >= 0)
            {
                _i++;
            }
        }

        private bool AtCommentStart => Cur == '/' && At(_i + 1) == '*';

        private void ReadComment()
        {
            int start = _i;
            _i += 2;
            while (!Eof && !(Cur == '*' && At(_i + 1) == '/'))
            {
                _i++;
            }

            if (Eof)
            {
                AddError(CoreStrings.Format("Css_UnterminatedComment", start), start);
                Emit(CssTokenType.Comment, start, _src[start..] + "*/");
                return;
            }

            _i += 2;
            Emit(CssTokenType.Comment, start, _src[start.._i]);
        }

        /// <summary>Consumes whitespace and emits the comments encountered. Returns <c>false</c> at EOF.</summary>
        private bool SkipTrivia()
        {
            while (true)
            {
                SkipWhitespace();
                if (AtCommentStart)
                {
                    ReadComment();
                    continue;
                }

                return !Eof;
            }
        }

        private void ParseItems(bool insideBlock)
        {
            while (true)
            {
                if (!SkipTrivia())
                {
                    if (insideBlock)
                    {
                        AddError(CoreStrings.Get("Css_UnterminatedBlock"), null);
                    }

                    return;
                }

                char c = Cur;
                if (c == '}')
                {
                    if (insideBlock)
                    {
                        return;
                    }

                    AddError(CoreStrings.Format("Css_UnexpectedBrace", _i), _i);
                    _i++;
                    continue;
                }

                if (c == '@')
                {
                    ParseAtRule();
                    continue;
                }

                if (Lookahead() == LookaheadKind.Brace)
                {
                    ParseQualifiedRule();
                }
                else
                {
                    ParseDeclaration();
                }
            }
        }

        private enum LookaheadKind
        {
            Brace,
            Semicolon,
            CloseBrace,
            Eof,
        }

        /// <summary>Which of <c>{ ; }</c> appears first at the current level (aware of <c>() [] '' "" /* */</c>).</summary>
        private LookaheadKind Lookahead()
        {
            int depthParen = 0;
            int depthBracket = 0;
            for (int k = _i; k < _src.Length; k++)
            {
                char c = _src[k];
                switch (c)
                {
                    case '/' when k + 1 < _src.Length && _src[k + 1] == '*':
                        k += 2;
                        while (k + 1 < _src.Length && !(_src[k] == '*' && _src[k + 1] == '/'))
                        {
                            k++;
                        }

                        k++;
                        break;

                    case '"':
                    case '\'':
                        k = SkipString(k);
                        break;

                    case '(':
                        depthParen++;
                        break;
                    case ')':
                        if (depthParen > 0)
                        {
                            depthParen--;
                        }

                        break;
                    case '[':
                        depthBracket++;
                        break;
                    case ']':
                        if (depthBracket > 0)
                        {
                            depthBracket--;
                        }

                        break;

                    case '{' when depthParen == 0 && depthBracket == 0:
                        return LookaheadKind.Brace;
                    case ';' when depthParen == 0 && depthBracket == 0:
                        return LookaheadKind.Semicolon;
                    case '}' when depthParen == 0 && depthBracket == 0:
                        return LookaheadKind.CloseBrace;
                }
            }

            return LookaheadKind.Eof;
        }

        /// <summary>Returns the index of the character closing a string (or the last index when it is unclosed).</summary>
        private int SkipString(int start)
        {
            char quote = _src[start];
            int k = start + 1;
            while (k < _src.Length)
            {
                char c = _src[k];
                if (c == '\\')
                {
                    k += 2;
                    continue;
                }

                if (c == quote || c == '\n')
                {
                    return k;
                }

                k++;
            }

            AddError(CoreStrings.Format("Css_UnterminatedString", start), start);
            return _src.Length - 1;
        }

        /// <summary>Reads raw text up to one of <paramref name="stops"/> at level 0; does not consume the stop character.</summary>
        private string ReadRawUntil(string stops)
        {
            StringBuilder sb = new();
            int depthParen = 0;
            int depthBracket = 0;

            while (!Eof)
            {
                char c = Cur;

                if (c == '/' && At(_i + 1) == '*')
                {
                    int cs = _i;
                    _i += 2;
                    while (!Eof && !(Cur == '*' && At(_i + 1) == '/'))
                    {
                        _i++;
                    }

                    if (Eof)
                    {
                        AddError(CoreStrings.Format("Css_UnterminatedComment", cs), cs);
                        sb.Append(_src[cs..]).Append("*/");
                        break;
                    }

                    _i += 2;
                    sb.Append(_src[cs.._i]);
                    continue;
                }

                if (c is '"' or '\'')
                {
                    int end = SkipString(_i);
                    sb.Append(_src[_i..(end + 1)]);
                    _i = end + 1;
                    continue;
                }

                if (c == '(')
                {
                    depthParen++;
                }
                else if (c == ')' && depthParen > 0)
                {
                    depthParen--;
                }
                else if (c == '[')
                {
                    depthBracket++;
                }
                else if (c == ']' && depthBracket > 0)
                {
                    depthBracket--;
                }
                else if (depthParen == 0 && depthBracket == 0 && stops.Contains(c, StringComparison.Ordinal))
                {
                    break;
                }

                sb.Append(c);
                _i++;
            }

            return sb.ToString();
        }

        private void ParseQualifiedRule()
        {
            int selStart = _i;
            string raw = ReadRawUntil("{}");
            string selText = CollapseWhitespace(raw).Trim();
            Emit(CssTokenType.Selector, selStart, selText);

            if (Cur != '{')
            {
                AddError(CoreStrings.Format("Css_SelectorWithoutBlock", selStart), selStart);
                return;
            }

            int brace = _i;
            _i++;
            Emit(CssTokenType.SelBlockBegin, brace, string.Empty);

            ParseItems(insideBlock: true);

            int close = _i;
            if (Cur == '}')
            {
                _i++;
            }

            Emit(CssTokenType.SelBlockEnd, close, string.Empty);
        }

        private void ParseAtRule()
        {
            int atStart = _i;
            _i++; // '@'
            int nameStart = _i;
            while (!Eof && (char.IsLetterOrDigit(Cur) || Cur is '-' or '_'))
            {
                _i++;
            }

            string keyword = _src[nameStart.._i];
            string atKeyword = "@" + keyword;

            SkipWhitespace();
            string raw = ReadRawUntil("{;}");
            string args = CollapseWhitespace(raw).Trim();

            if (Cur == '{')
            {
                string prelude = args.Length > 0 ? atKeyword + " " + args : atKeyword;
                Emit(CssTokenType.AtRuleBegin, atStart, prelude);
                int brace = _i;
                _i++;
                Emit(CssTokenType.AtBlockBegin, brace, string.Empty);

                ParseItems(insideBlock: true);

                int close = _i;
                if (Cur == '}')
                {
                    _i++;
                }

                Emit(CssTokenType.AtBlockEnd, close, string.Empty);
                return;
            }

            // Statement form.
            if (Cur == ';')
            {
                _i++;
            }

            switch (keyword.ToLowerInvariant())
            {
                case "charset":
                    Emit(CssTokenType.CharsetAt, atStart, args);
                    break;
                case "import":
                    Emit(CssTokenType.ImportAt, atStart, args);
                    break;
                case "namespace":
                    Emit(CssTokenType.NamespaceAt, atStart, args);
                    break;
                case "layer":
                    Emit(CssTokenType.LayerAt, atStart, args);
                    break;
                default:
                    Emit(CssTokenType.AtRuleUnknown, atStart, args.Length > 0 ? atKeyword + " " + args : atKeyword);
                    break;
            }
        }

        private void ParseDeclaration()
        {
            int declStart = _i;
            string raw = ReadRawUntil(";}");

            if (Cur == ';')
            {
                _i++;
            }

            string text = raw.Trim(Whitespace);
            if (text.Length == 0)
            {
                return;
            }

            int colon = FirstTopLevelColon(text);
            if (colon < 0)
            {
                AddError(CoreStrings.Format("Css_MalformedDeclaration", declStart, text), declStart);
                return;
            }

            string name = CollapseWhitespace(text[..colon]).Trim();
            string value = CollapseWhitespace(text[(colon + 1)..]).Trim();
            int valueOffset = declStart + raw.IndexOf(':', StringComparison.Ordinal) + 1;

            Emit(CssTokenType.Property, declStart, name);
            Emit(CssTokenType.PropertyValue, Math.Min(valueOffset, _src.Length), value);
        }

        private static int FirstTopLevelColon(string text)
        {
            int depthParen = 0;
            int depthBracket = 0;
            for (int k = 0; k < text.Length; k++)
            {
                char c = text[k];
                switch (c)
                {
                    case '"':
                    case '\'':
                        k = SkipStringInText(text, k);
                        break;
                    case '(':
                        depthParen++;
                        break;
                    case ')':
                        if (depthParen > 0)
                        {
                            depthParen--;
                        }

                        break;
                    case '[':
                        depthBracket++;
                        break;
                    case ']':
                        if (depthBracket > 0)
                        {
                            depthBracket--;
                        }

                        break;
                    case ':' when depthParen == 0 && depthBracket == 0:
                        return k;
                }
            }

            return -1;
        }

        private static int SkipStringInText(string text, int start)
        {
            char quote = text[start];
            for (int k = start + 1; k < text.Length; k++)
            {
                if (text[k] == '\\')
                {
                    k++;
                    continue;
                }

                if (text[k] == quote)
                {
                    return k;
                }
            }

            return text.Length - 1;
        }

        /// <summary>Whitespace normalization: a run of whitespace → a single space.</summary>
        private static string CollapseWhitespace(string value)
        {
            StringBuilder sb = new(value.Length);
            bool inWs = false;
            foreach (char c in value)
            {
                if (Array.IndexOf(Whitespace, c) >= 0)
                {
                    inWs = true;
                    continue;
                }

                if (inWs && sb.Length > 0)
                {
                    sb.Append(' ');
                }

                inWs = false;
                sb.Append(c);
            }

            return sb.ToString();
        }
    }
}
