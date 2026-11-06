using System;
using System.Collections.Generic;
using System.Globalization;
using System.Numerics;
using System.Text;
using System.Text.RegularExpressions;

namespace Signet.EpubCheck.Core.Jing.Datatype.Xsd.RegularExpressions;

/// <summary>A regular expression of XML Schema could not be translated (Jing's <c>RegexSyntaxException</c>).</summary>
internal sealed class RegexSyntaxException : Exception
{
    /// <summary>The position is not known.</summary>
    public const int UnknownPosition = -1;

    public RegexSyntaxException()
    {
    }

    public RegexSyntaxException(string message)
        : base(message)
    {
    }

    public RegexSyntaxException(string message, Exception innerException)
        : base(message, innerException)
    {
    }

    public RegexSyntaxException(string message, int position)
        : base(message)
    {
        Position = position;
    }

    /// <summary>The index in the expression where the error was found, or <see cref="UnknownPosition"/>.</summary>
    public int Position { get; } = UnknownPosition;
}

/// <summary>A compiled XML Schema regular expression: it matches a whole string (Jing's <c>Regex</c>).</summary>
internal sealed class XsdRegex(Regex regex)
{
    private static readonly TimeSpan s_noTimeout = Regex.InfiniteMatchTimeout;

    /// <summary>Compiles an XML Schema regular expression (Jing's java <c>RegexEngineImpl</c>).</summary>
    /// <exception cref="RegexSyntaxException">It is not a correct expression.</exception>
    public static XsdRegex Compile(string expression, JingLocalizer localizer) =>
        new(new Regex(@"\A(?:" + XsdRegexTranslator.Translate(expression, localizer) + @")\z", RegexOptions.CultureInvariant, s_noTimeout));

    /// <summary>Whether the expression matches all of <paramref name="s"/>.</summary>
    public bool Matches(string s) => regex.IsMatch(s);
}

/// <summary>
/// Translates XML Schema regular expressions into .NET ones: a port of Jing's <c>Translator</c> (for
/// <c>java.util.regex</c>). The parsing, the errors and their positions are Jing's; the character classes are computed
/// as sets of code points (<see cref="CodePointSet"/>) and printed so that .NET, which matches UTF-16 units, matches a
/// character outside the BMP as one character, as Java does.
/// </summary>
internal sealed class XsdRegexTranslator
{
    private const char Eos = '\0';
    private const string Digits = "0123456789";
    private const string IsPrefix = "Is";
    private const string SupplementaryGroupStart = "(?:";

    private readonly string _regExp;
    private readonly int _length;
    private readonly JingLocalizer _localizer;
    private readonly StringBuilder _result = new();
    private int _pos;
    private char _curChar;
    private bool _eos;

    private XsdRegexTranslator(string regExp, JingLocalizer localizer)
    {
        _regExp = regExp;
        _length = regExp.Length;
        _localizer = localizer;
        Advance();
    }

    /// <exception cref="RegexSyntaxException">It is not a correct expression.</exception>
    public static string Translate(string regExp, JingLocalizer localizer)
    {
        XsdRegexTranslator tr = new(regExp, localizer);
        tr.TranslateTop();
        return tr._result.ToString();
    }

    private void Advance()
    {
        if (_pos < _length)
        {
            _curChar = _regExp[_pos++];
        }
        else
        {
            _pos++;
            _curChar = Eos;
            _eos = true;
        }
    }

    private void TranslateTop()
    {
        TranslateRegExp();
        if (!_eos)
        {
            throw MakeException("expected_eos");
        }
    }

    private void TranslateRegExp()
    {
        TranslateBranch();
        while (_curChar == '|')
        {
            CopyCurChar();
            TranslateBranch();
        }
    }

    private void TranslateBranch()
    {
        while (TranslateAtom())
        {
            TranslateQuantifier();
        }
    }

    private void TranslateQuantifier()
    {
        switch (_curChar)
        {
            case '*':
            case '?':
            case '+':
                CopyCurChar();
                return;
            case '{':
                CopyCurChar();
                TranslateQuantity();
                Expect('}');
                CopyCurChar();
                break;
        }
    }

    // Numbers that do not fit an int become int.MaxValue, as in Jing (java.util.regex has the same limit as .NET).
    private void TranslateQuantity()
    {
        string lower = ParseQuantExact();
        int lowerValue = -1;
        if (int.TryParse(lower, NumberStyles.None, CultureInfo.InvariantCulture, out int parsedLower))
        {
            lowerValue = parsedLower;
            _result.Append(lower);
        }
        else
        {
            _result.Append(int.MaxValue.ToString(CultureInfo.InvariantCulture));
        }

        if (_curChar != ',')
        {
            return;
        }

        CopyCurChar();
        if (_curChar == '}')
        {
            return;
        }

        string upper = ParseQuantExact();
        if (int.TryParse(upper, NumberStyles.None, CultureInfo.InvariantCulture, out int upperValue))
        {
            _result.Append(upper);
            if (lowerValue < 0 || upperValue < lowerValue)
            {
                throw MakeException("invalid_quantity_range");
            }
        }
        else
        {
            _result.Append(int.MaxValue.ToString(CultureInfo.InvariantCulture));
            if (lowerValue < 0 && BigInteger.Parse(lower, CultureInfo.InvariantCulture) > BigInteger.Parse(upper, CultureInfo.InvariantCulture))
            {
                throw MakeException("invalid_quantity_range");
            }
        }
    }

    private string ParseQuantExact()
    {
        StringBuilder buf = new();
        do
        {
            if (!Digits.Contains(_curChar, StringComparison.Ordinal))
            {
                throw MakeException("expected_digit");
            }

            buf.Append(_curChar);
            Advance();
        }
        while (_curChar is not (',' or '}'));
        return buf.ToString();
    }

    private void CopyCurChar()
    {
        _result.Append(_curChar);
        Advance();
    }

    private bool TranslateAtom()
    {
        switch (_curChar)
        {
            case Eos when _eos:
            case '?':
            case '*':
            case '+':
            case ')':
            case '{':
            case '}':
            case '|':
            case ']':
                return false;
            case '(':
                CopyCurChar();
                TranslateRegExp();
                Expect(')');
                CopyCurChar();
                return true;
            case '\\':
                Advance();
                _result.Append(ParseEsc().Set.ToPattern());
                return true;
            case '[':
                Advance();
                _result.Append(ParseCharClassExpr().ToPattern());
                return true;
            case '.':
                _result.Append(XsdCharClasses.Dot.ToPattern());
                Advance();
                return true;
            case '$':
            case '^':
                _result.Append('\\');
                break;
        }

        // Java matches a surrogate pair as one character; .NET needs a group for a quantifier to apply to both halves.
        if (char.IsHighSurrogate(_curChar) && _pos < _length && char.IsLowSurrogate(_regExp[_pos]))
        {
            _result.Append(SupplementaryGroupStart).Append(_curChar);
            Advance();
            _result.Append(_curChar).Append(')');
            Advance();
            return true;
        }

        CopyCurChar();
        return true;
    }

    private ParsedClass ParseEsc()
    {
        switch (_curChar)
        {
            case 'n':
                Advance();
                return ParsedClass.Single('\n');
            case 'r':
                Advance();
                return ParsedClass.Single('\r');
            case 't':
                Advance();
                return ParsedClass.Single('\t');
            case '\\' or '|' or '.' or '-' or '^' or '?' or '*' or '+' or '(' or ')' or '{' or '}' or '[' or ']':
                break;
            case 's':
                Advance();
                return new ParsedClass(XsdCharClasses.Space);
            case 'S':
                Advance();
                return new ParsedClass(XsdCharClasses.Space.Complement());
            case 'i':
                Advance();
                return new ParsedClass(XsdCharClasses.NameStart);
            case 'I':
                Advance();
                return new ParsedClass(XsdCharClasses.NameStart.Complement());
            case 'c':
                Advance();
                return new ParsedClass(XsdCharClasses.NameChar);
            case 'C':
                Advance();
                return new ParsedClass(XsdCharClasses.NameChar.Complement());
            case 'd':
                Advance();
                return new ParsedClass(XsdCharClasses.Digit);
            case 'D':
                Advance();
                return new ParsedClass(XsdCharClasses.Digit.Complement());
            case 'w':
                Advance();
                return new ParsedClass(XsdCharClasses.WordChar);
            case 'W':
                Advance();
                return new ParsedClass(XsdCharClasses.WordChar.Complement());
            case 'p':
                Advance();
                return new ParsedClass(ParseProp());
            case 'P':
                Advance();
                return new ParsedClass(ParseProp().Complement());
            default:
                throw MakeException("bad_escape");
        }

        ParsedClass tem = ParsedClass.Single(_curChar);
        Advance();
        return tem;
    }

    private CodePointSet ParseProp()
    {
        Expect('{');
        int start = _pos;
        while (true)
        {
            Advance();
            if (_curChar == '}')
            {
                break;
            }

            if (!char.IsAsciiLetterOrDigit(_curChar) && _curChar != '-')
            {
                Expect('}');
            }
        }

        string propertyName = _regExp.Substring(start, _pos - 1 - start);
        Advance();
        switch (propertyName.Length)
        {
            case 0:
                throw MakeException("empty_property_name");
            case 2:
                int sci = XsdCharClasses.SubCategories.IndexOf(propertyName, StringComparison.Ordinal);
                if (sci < 0 || sci % 2 == 1)
                {
                    // Jing gives no argument here, so its message reads 'unrecognized Unicode category "{0}"'.
                    throw MakeException("bad_category", propertyName);
                }

                return XsdCharClasses.SubCategory(propertyName);
            case 1:
                if (!XsdCharClasses.Categories.Contains(propertyName[0], StringComparison.Ordinal))
                {
                    throw MakeException("bad_category", propertyName);
                }

                return XsdCharClasses.Category(propertyName[0]);
            default:
                if (!propertyName.StartsWith(IsPrefix, StringComparison.Ordinal))
                {
                    break;
                }

                string blockName = propertyName[IsPrefix.Length..];
                return XsdCharClasses.Block(blockName) ?? throw MakeException("bad_block_name", blockName);
        }

        throw MakeException("bad_property_name", propertyName);
    }

    private void Expect(char c)
    {
        if (_curChar != c)
        {
            throw MakeException("expected", c.ToString());
        }
    }

    private CodePointSet ParseCharClassExpr()
    {
        bool compl = false;
        if (_curChar == '^')
        {
            Advance();
            compl = true;
        }

        List<CodePointSet> members = [];
        do
        {
            ParsedClass lower = ParseCharClassEscOrXmlChar();
            members.Add(lower.Set);
            if (_curChar == '-')
            {
                Advance();
                if (_curChar == '[')
                {
                    break;
                }

                ParsedClass upper = ParseCharClassEscOrXmlChar();
                if (lower.SingleChar < 0 || upper.SingleChar < 0)
                {
                    throw MakeException("multi_range");
                }

                if (lower.SingleChar > upper.SingleChar)
                {
                    throw MakeException("invalid_range");
                }

                members[^1] = CodePointSet.Range(lower.SingleChar, upper.SingleChar);
                if (_curChar == '-')
                {
                    Advance();
                    Expect('[');
                    break;
                }
            }
        }
        while (_curChar != ']');

        CodePointSet result = members.Count == 1 ? members[0] : CodePointSet.Union(members);
        if (compl)
        {
            result = result.Complement();
        }

        if (_curChar == '[')
        {
            Advance();
            result = result.Subtract(ParseCharClassExpr());
            Expect(']');
        }

        Advance();
        return result;
    }

    private ParsedClass ParseCharClassEscOrXmlChar()
    {
        switch (_curChar)
        {
            case Eos when _eos:
                Expect(']');
                break;
            case '\\':
                Advance();
                return ParseEsc();
            case '[':
            case ']':
            case '-':
                throw MakeException("should_quote", _curChar.ToString());
        }

        ParsedClass tem;
        if (char.IsSurrogate(_curChar))
        {
            if (!char.IsHighSurrogate(_curChar))
            {
                throw MakeException("invalid_surrogate");
            }

            char c1 = _curChar;
            Advance();
            if (!char.IsLowSurrogate(_curChar))
            {
                throw MakeException("invalid_surrogate");
            }

            tem = ParsedClass.Single(char.ConvertToUtf32(c1, _curChar));
        }
        else
        {
            tem = ParsedClass.Single(_curChar);
        }

        Advance();
        return tem;
    }

    private RegexSyntaxException MakeException(string key, params string?[] args) =>
        new(_localizer.Message(key, args), _pos - 1);

    // A parsed class and, for a single character (which can bound a range), its code point.
    private readonly record struct ParsedClass(CodePointSet Set, int SingleChar = -1)
    {
        public static ParsedClass Single(int codePoint) => new(CodePointSet.Of(codePoint), codePoint);
    }
}
