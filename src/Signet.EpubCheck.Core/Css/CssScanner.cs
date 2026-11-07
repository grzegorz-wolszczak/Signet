using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;

namespace Signet.EpubCheck.Core.Css;

/// <summary>The scanner of CSS (upstream's <c>CssScanner</c>): turns the characters of a style sheet into tokens.</summary>
internal sealed class CssScanner
{
    public static readonly CharMatcher Whitespace = CharMatcher.AnyOf(" \t\n\r\f");
    public static readonly CharMatcher Quotes = CharMatcher.AnyOf("\"'");
    public static readonly CharMatcher HexChar = CharMatcher.AnyOf("AaBbCcDdEeFf0123456789");
    public static readonly CharMatcher Terminator = CharMatcher.AnyOf(";}{");

    private static readonly CharMatcher s_notWhitespace = Whitespace.Negate();
    private static readonly CharMatcher s_u = CharMatcher.AnyOf("Uu");
    private static readonly CharMatcher s_o = CharMatcher.AnyOf("Oo");
    private static readonly CharMatcher s_n = CharMatcher.AnyOf("Nn");
    private static readonly CharMatcher s_a = CharMatcher.AnyOf("Aa");
    private static readonly CharMatcher s_nmStart = CharMatcher.InRange('A', 'Z').Or(CharMatcher.InRange('a', 'z')).Or(CharMatcher.Is('_')).Or(CharMatcher.InRange('\u0080', '￿'));
    private static readonly CharMatcher s_nmChar = s_nmStart.Or(CharMatcher.InRange('0', '9')).Or(CharMatcher.Is('-'));
    private static readonly CharMatcher s_qntStart = s_nmStart.Or(CharMatcher.Is('%'));
    private static readonly CharMatcher s_numEnd = CharMatcher.InRange('0', '9');
    private static readonly CharMatcher s_num = s_numEnd.Or(CharMatcher.Is('.'));
    private static readonly CharMatcher s_unary = CharMatcher.AnyOf("+-");
    private static readonly CharMatcher s_numStart = s_num.Or(s_unary);
    private static readonly CharMatcher s_urangeStart = HexChar.Or(CharMatcher.AnyOf("?"));
    private static readonly CharMatcher s_urangeChar = HexChar.Or(CharMatcher.AnyOf("?-"));

    private static readonly int[] s_cdo = ['!', '-', '-'];
    private static readonly int[] s_cdc = ['-', '>'];
    private static readonly int[] s_uri = ['r', 'l', '('];
    private static readonly int[] s_only = ['n', 'l', 'y'];
    private static readonly int[] s_not = ['o', 't'];
    private static readonly int[] s_and = ['n', 'd'];

    // Update when adding more.
    private const int QuantityTokenMaxLength = 4;

    private static readonly (string Unit, CssTokenType Type)[] s_quantities =
    [
        ("dpcm", CssTokenType.QntyResolution),
        ("dppx", CssTokenType.QntyResolution),
        ("grad", CssTokenType.QntyAngle),
        ("turn", CssTokenType.QntyAngle),
        ("vmin", CssTokenType.QntyLength),
        ("vmax", CssTokenType.QntyLength),
        ("deg", CssTokenType.QntyAngle),
        ("khz", CssTokenType.QntyFreq),
        ("rad", CssTokenType.QntyAngle),
        ("rem", CssTokenType.QntyRems),
        ("dpi", CssTokenType.QntyResolution),
        ("em", CssTokenType.QntyEms),
        ("cm", CssTokenType.QntyLength),
        ("px", CssTokenType.QntyLength),
        ("mm", CssTokenType.QntyLength),
        ("in", CssTokenType.QntyLength),
        ("pt", CssTokenType.QntyLength),
        ("pc", CssTokenType.QntyLength),
        ("ch", CssTokenType.QntyChs),
        ("vw", CssTokenType.QntyLength),
        ("vh", CssTokenType.QntyLength),
        ("ex", CssTokenType.QntyExs),
        ("ms", CssTokenType.QntyTime),
        ("hz", CssTokenType.QntyFreq),
        ("%", CssTokenType.QntyPercentage),
        ("q", CssTokenType.QntyLength),
        ("s", CssTokenType.QntyTime),
    ];

    private readonly CssReader _reader;
    private readonly Action<CssToken> _consumer;
    private readonly ICssErrorHandler _errorHandler;
    private readonly CultureInfo? _culture;
    private readonly EscapeMemoizer _escapes;
    private CssToken.TokenBuilder _builder = null!;
    private char _cur;

    public CssScanner(TextReader input, string systemId, ICssErrorHandler errorHandler, Action<CssToken> consumer, CultureInfo? culture)
    {
        _consumer = consumer ?? throw new ArgumentNullException(nameof(consumer));
        _errorHandler = errorHandler ?? throw new ArgumentNullException(nameof(errorHandler));
        _reader = new CssReader(input, systemId, CssReader.DefaultPushbackBufferSize);
        _escapes = new EscapeMemoizer(_reader);
        _culture = culture;
    }

    /// <summary>The length of the line end at the start of <paramref name="chars"/> (0 when it does not start with one).</summary>
    public static int IsNewLine(int[] chars)
    {
        if (chars.Length <= 1)
        {
            throw new ArgumentException("Two characters are needed.", nameof(chars));
        }

        if (chars[0] == '\r' && chars[1] == '\n')
        {
            return 2;
        }

        return chars[0] is '\n' or '\r' or '\f' ? 1 : 0;
    }

    /// <summary>Scans the whole input, handing each token to the consumer.</summary>
    public void Scan()
    {
        while (true)
        {
            int ch = _reader.Next();
            if (ch == -1)
            {
                break;
            }

            _builder = new CssToken.TokenBuilder(_reader, _errorHandler, _culture);
            _cur = (char)ch;
            int next = _reader.Peek();
            _escapes.Reset(_builder);
            if (Whitespace.Matches(_cur))
            {
                Ws();
            }
            else if (_cur == '-' && Equal(_reader.Peek(2), s_cdc, false))
            {
                Cdc();
            }
            else if (s_o.Matches(_cur) && MatchesOrEof(_reader.At(4), Whitespace) && Equal(_reader.Peek(3), s_only, true))
            {
                Only();
            }
            else if (s_n.Matches(_cur) && MatchesOrEof(_reader.At(3), Whitespace) && Equal(_reader.Peek(2), s_not, true))
            {
                Not();
            }
            else if (s_a.Matches(_cur) && MatchesOrEof(_reader.At(3), Whitespace) && Equal(_reader.Peek(2), s_and, true))
            {
                And();
            }
            else if (s_u.Matches(_cur) && Equal(_reader.Peek(3), s_uri, true))
            {
                Uri();
            }
            else if (s_u.Matches(_cur) && next == '+' && MatchesOrEof(_reader.At(2), s_urangeStart))
            {
                Urange();
            }
            else if (s_nmStart.Matches(_cur)
                || (_cur == '-' && (Matches(next, s_nmStart) || _reader.Peek() == '-' || _escapes.Get(1) is not null))
                || _escapes.Get(0) is not null)
            {
                Ident();
                if (_reader.Peek() == '(')
                {
                    Function();
                }
            }
            else if (_cur == '@'
                && ((Matches(next, s_nmStart) || _escapes.Get(1) is not null)
                    || (next == '-' && Matches(_reader.At(2), s_nmStart))
                    || _escapes.Get(2) is not null))
            {
                AtKeyword();
            }
            else if (s_numEnd.Matches(_cur)
                || (s_numStart.Matches(_cur) && Matches(next, s_numEnd))
                || (s_unary.Matches(_cur) && next == '.' && Matches(_reader.At(2), s_numEnd)))
            {
                Num();
            }
            else if (_cur == '<' && Equal(_reader.Peek(3), s_cdo, false))
            {
                Cdo();
            }
            else if (_cur == '/' && next == '*')
            {
                Comment();
            }
            else if (Quotes.Matches(_cur))
            {
                String();
            }
            else if (_cur == '#' && (Matches(next, s_nmChar) || _escapes.Get(1) is not null))
            {
                HashName();
            }
            else if (_cur == '.' && (Matches(next, s_nmChar) || _escapes.Get(1) is not null))
            {
                ClassName();
            }
            else if (_cur == '!' && ForwardMatch("important", true, false))
            {
                Important();
            }
            else if (_cur == '~' && next == '=')
            {
                Match(CssTokenType.Includes, "~=");
            }
            else if (_cur == '|' && next == '=')
            {
                Match(CssTokenType.DashMatch, "|=");
            }
            else if (_cur == '^' && next == '=')
            {
                Match(CssTokenType.PrefixMatch, "^=");
            }
            else if (_cur == '$' && next == '=')
            {
                Match(CssTokenType.SuffixMatch, "$=");
            }
            else if (_cur == '*' && next == '=')
            {
                Match(CssTokenType.SubstringMatch, "*=");
            }
            else
            {
                _builder.Type = CssTokenType.Char;
                _builder.Append(_cur);
            }

            _consumer(_builder.AsToken());
        }
    }

    private void Function()
    {
        _builder.Type = CssTokenType.Function;
        _builder.Append(_reader.Next());
    }

    private void Uri()
    {
        _builder.Append("url(");
        _reader.Forward(3);
        _reader.Forward(s_notWhitespace);
        int uriStart = _reader.Next();
        if (uriStart == -1)
        {
            _builder.Error(CssErrorCode.ScannerPrematureEof, _reader);
        }
        else if (Quotes.Matches((char)uriStart))
        {
            _builder.Append('\'');
            String();
            _builder.Append('\'');
            _reader.Forward(s_notWhitespace);
            if (_reader.Peek() > -1)
            {
                _reader.Next(); // ')'
            }
        }
        else
        {
            _builder.Append(uriStart);
            StringBuilder buffer = new();
            while (true)
            {
                CssReader.Mark mark = _reader.GetMark();
                int ch = _reader.Next();
                if (ch == -1)
                {
                    _builder.Error(CssErrorCode.ScannerPrematureEof, _reader);
                    _reader.Unread(ch, mark);
                    break;
                }

                if (ch == ')')
                {
                    break;
                }

                buffer.Append((char)ch);
            }

            _builder.Append(Whitespace.TrimTrailingFrom(buffer.ToString()));
        }

        _builder.Append(')');
        _builder.Type = CssTokenType.Uri;
        if (_reader.CurChar != ')' && _builder.Errors.Count == 0)
        {
            _builder.Error(CssErrorCode.ScannerIllegalSyntax, _reader, _reader.CurChar);
        }
    }

    private void String()
    {
        _builder.Type = CssTokenType.String;
        int quoteType = _reader.CurChar;
        while (true)
        {
            CssReader.Mark mark = _reader.GetMark();
            int ch = _reader.Next();
            if (ch == -1)
            {
                _builder.Error(CssErrorCode.ScannerPrematureEof, _reader);
                _reader.Unread(ch, mark);
                break;
            }

            if (ch is '\n' or '\r' or '\f')
            {
                _builder.Error(CssErrorCode.ScannerIllegalChar, _reader, "NEWLINE", "STRING");
                _reader.Forward(Terminator);
                break;
            }

            if (ch == '\\')
            {
                int newLine = IsNewLine(_reader.Peek(2));
                if (newLine > 0)
                {
                    _reader.Forward(newLine);
                    continue;
                }
            }
            else if (ch == quoteType && _reader.PrevChar != '\\')
            {
                break;
            }

            _builder.Append(ch);
        }
    }

    private void AtKeyword()
    {
        _builder.Type = CssTokenType.AtKeyword;
        _builder.Append(_cur); // @
        Append(s_nmStart);
        Append(s_nmChar);
    }

    private void Ident()
    {
        _builder.Type = CssTokenType.Ident;
        CssEscape? escape = _escapes.Get(0);
        if (escape is not null)
        {
            int length = escape.Render(_builder, s_nmStart);
            _reader.Forward(length);
        }
        else
        {
            _builder.Append(_cur);
        }

        if (_cur == '-' || (escape is not null && escape.Character == '-'))
        {
            if (_escapes.Get(1) is { } second)
            {
                _reader.Forward(second.Render(_builder, s_nmStart));
            }
            else
            {
                _builder.Append(_reader.Next());
            }
        }

        Append(s_nmChar);
    }

    private void Match(CssTokenType type, string text)
    {
        _builder.Type = type;
        _builder.Append(text);
        _reader.Next();
    }

    private void HashName()
    {
        _builder.Type = CssTokenType.HashName;
        _builder.Append('#');
        Append(s_nmChar);
    }

    private void ClassName()
    {
        _builder.Type = CssTokenType.ClassName;
        _builder.Append('.');
        Append(s_nmChar);
    }

    private void Important()
    {
        _builder.Type = CssTokenType.Important;
        _builder.Append("!important");
    }

    private void Comment()
    {
        _builder.Type = CssTokenType.Comment;
        _reader.Next(); // '*'
        while (true)
        {
            CssReader.Mark mark = _reader.GetMark();
            int ch = _reader.Next();
            if (ch == -1)
            {
                _builder.Error(CssErrorCode.ScannerPrematureEof, _reader);
                _reader.Unread(ch, mark);
                break;
            }

            if (ch == '*' && _reader.Peek() == '/')
            {
                _reader.Next();
                break;
            }

            _builder.Append(ch);
        }
    }

    private void Cdo()
    {
        _builder.Type = CssTokenType.Cdo;
        _builder.Append("<!--");
        _reader.Forward(3);
    }

    private void Num()
    {
        _builder.Type = CssTokenType.Integer;
        _builder.Append(_cur);
        if (_cur == '.')
        {
            _builder.Type = CssTokenType.Number;
        }

        while (true)
        {
            CssReader.Mark mark = _reader.GetMark();
            int nm = _reader.Next();
            if (nm == -1)
            {
                if (_builder.Length == 1 && _builder.Last == '.')
                {
                    _builder.Type = CssTokenType.Char;
                }

                _reader.Unread(nm, mark);
                break;
            }

            if (!s_num.Matches((char)nm))
            {
                _reader.Unread(nm, mark);
                break;
            }

            if (nm == '.' && !s_numEnd.Matches((char)_reader.Peek()))
            {
                _reader.Unread(nm, mark);
                break;
            }

            _builder.Append(nm);
            if (nm == '.')
            {
                _builder.Type = CssTokenType.Number;
            }
        }

        int qnt = _reader.Peek();
        if ((qnt > -1 && s_qntStart.Matches((char)qnt)) || IsNextEscape())
        {
            Quantity();
        }
    }

    private void Quantity()
    {
        _builder.Type = CssTokenType.QntyDimen;
        CssToken.TokenBuilder suffix = new(_reader, _errorHandler, _culture);
        Append(s_qntStart, suffix);
        if (suffix.Last != '%')
        {
            // QNTSTART = NMSTART | '%'
            Append(s_nmChar, suffix);
        }

        if (suffix.Length > QuantityTokenMaxLength)
        {
            _builder.Append(suffix.ToString());
            return;
        }

        int[] ident = suffix.ToArray();
        string? match = null;
        foreach ((string unit, CssTokenType type) in s_quantities)
        {
            if (Equal(ident, [.. unit.Select(c => (int)c)], true))
            {
                _builder.Type = type;
                match = unit;
                break;
            }
        }

        if (_builder.Type == CssTokenType.QntyDimen)
        {
            _builder.Append(ident);
        }
        else
        {
            _builder.Append(match!);
        }
    }

    private void And()
    {
        _builder.Type = CssTokenType.And;
        _builder.Append("and");
        _reader.Forward(2);
    }

    private void Not()
    {
        _builder.Type = CssTokenType.Not;
        _builder.Append("not");
        _reader.Forward(2);
    }

    private void Only()
    {
        _builder.Type = CssTokenType.Only;
        _builder.Append("only");
        _reader.Forward(3);
    }

    private void Cdc()
    {
        _builder.Type = CssTokenType.Cdc;
        _builder.Append("-->");
        _reader.Forward(2);
    }

    private void Ws()
    {
        _builder.Type = CssTokenType.S;
        _builder.Append(' ');
        _reader.Forward(s_notWhitespace);
    }

    private void Urange()
    {
        _builder.Type = CssTokenType.Urange;
        _reader.Next(); // '+'
        List<int> chars = [];
        int count = 0;
        while (true)
        {
            CssReader.Mark mark = _reader.GetMark();
            int ch = _reader.Next();
            if (ch == -1)
            {
                _reader.Unread(ch, mark);
                break;
            }

            if (s_urangeChar.Matches((char)ch))
            {
                count = ch == '-' ? 0 : count + 1;
                if (count == 7)
                {
                    _builder.Error(CssErrorCode.ScannerIllegalUrange, _reader, "U+" + ToText(chars) + (char)ch);
                }

                chars.Add(ch);
            }
            else
            {
                _reader.Unread(ch, mark);
                break;
            }
        }

        _builder.Append("U+");
        _builder.Append([.. chars]);
    }

    private bool IsNextEscape()
    {
        bool result = false;
        CssReader.Mark mark = _reader.GetMark();
        int ch = _reader.Next();
        if (ch == '\\')
        {
            try
            {
                result = CssEscape.Create(_reader, _builder) is not null;
            }
            catch (CssException)
            {
                // ignored
            }
        }

        _reader.Unread(ch, mark);
        return result;
    }

    private void Append(CharMatcher matcher) => Append(matcher, _builder);

    private void Append(CharMatcher matcher, CssToken.TokenBuilder builder)
    {
        while (true)
        {
            CssReader.Mark mark = _reader.GetMark();
            int ch = _reader.Next();
            if (ch > -1 && matcher.Matches((char)ch))
            {
                builder.Append(ch);
            }
            else if (ch == '\\')
            {
                CssEscape? escape = CssEscape.Create(_reader, builder);
                if (escape is not null)
                {
                    _reader.Forward(escape.Render(builder, matcher));
                }
                else
                {
                    _reader.Unread(ch, mark);
                    break;
                }
            }
            else
            {
                _reader.Unread(ch, mark);
                break;
            }
        }
    }

    private bool ForwardMatch(string match, bool ignoreCase, bool resetOnTrue)
    {
        CssReader.Mark mark = _reader.GetMark();
        List<int> chars = [];
        StringBuilder builder = new();
        bool result = true;
        bool seenChar = false;
        while (true)
        {
            chars.Add(_reader.Next());
            char ch = (char)_reader.CurChar;
            if (_reader.CurChar == -1)
            {
                result = false;
                break;
            }

            if (Whitespace.Matches(ch))
            {
                if (seenChar)
                {
                    builder.Append(ch);
                }
            }
            else
            {
                if (builder.Length == 0)
                {
                    seenChar = true;
                }

                builder.Append(ch);
                int index = builder.Length - 1;

                // Upstream's test for a case-sensitive match is inverted; EPUBCheck only asks for a case-insensitive one.
                if (!ignoreCase && builder[index] == match[index])
                {
                    result = false;
                    break;
                }

                if (ignoreCase && char.ToLowerInvariant(builder[index]) != char.ToLowerInvariant(match[index]))
                {
                    result = false;
                    break;
                }
            }

            if (builder.Length == match.Length)
            {
                if (!string.Equals(match, builder.ToString(), StringComparison.OrdinalIgnoreCase))
                {
                    result = false;
                }

                break;
            }
        }

        if (!result || resetOnTrue)
        {
            _reader.Unread(chars, mark);
        }

        return result;
    }

    private static string ToText(List<int> ints)
    {
        StringBuilder builder = new();
        foreach (int i in ints)
        {
            builder.Append((char)i);
        }

        return builder.ToString();
    }

    private static bool Equal(int[] a, int[] b, bool ignoreAsciiCase)
    {
        if (a.Length != b.Length)
        {
            return false;
        }

        for (int i = 0; i < a.Length; i++)
        {
            if (!ignoreAsciiCase)
            {
                if (a[i] != b[i])
                {
                    return false;
                }
            }
            else if (a[i] == -1 && b[i] == -1)
            {
                // both ended
            }
            else if (a[i] == -1 || b[i] == -1 || AsciiLower((char)a[i]) != AsciiLower((char)b[i]))
            {
                return false;
            }
        }

        return true;
    }

    private static char AsciiLower(char c) => c is >= 'A' and <= 'Z' ? (char)(c + ('a' - 'A')) : c;

    private static bool MatchesOrEof(int ch, CharMatcher matcher) => ch == -1 || matcher.Matches((char)ch);

    private static bool Matches(int ch, CharMatcher matcher) => ch != -1 && matcher.Matches((char)ch);

    // Remembers the escape that starts n characters ahead while one token is read.
    private sealed class EscapeMemoizer(CssReader reader)
    {
        private readonly Dictionary<int, CssEscape?> _map = [];
        private CssToken.TokenBuilder? _errors;

        public void Reset(CssToken.TokenBuilder errors)
        {
            _map.Clear();
            _errors = errors;
        }

        public CssEscape? Get(int n)
        {
            if (!_map.TryGetValue(n, out CssEscape? escape))
            {
                escape = Create(n);
                _map[n] = escape;
            }

            return escape;
        }

        private CssEscape? Create(int n)
        {
            List<int> chars = [];
            CssReader.Mark mark = reader.GetMark();
            for (int i = 0; i < n; i++)
            {
                int ch = reader.Next();
                chars.Add(ch);
                if (ch == -1)
                {
                    reader.Unread(chars, mark);
                    return null;
                }
            }

            if (reader.CurChar == '\\')
            {
                try
                {
                    CssEscape? escape = CssEscape.Create(reader, _errors!);
                    reader.Unread(chars, mark);
                    return escape;
                }
                catch (CssException)
                {
                    // ignored, the escape is reported when it is read
                }
            }

            reader.Unread(chars, mark);
            return null;
        }
    }
}
