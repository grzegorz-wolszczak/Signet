using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace Signet.EpubCheck.Core.Jing.Validate.Schematron.XPath;

/// <summary>An XPath expression is not correct, or failed while it was evaluated.</summary>
internal sealed class XPathException : Exception
{
    public XPathException()
    {
    }

    public XPathException(string message)
        : base(message)
    {
    }

    public XPathException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}

internal enum XPathTokenKind
{
    End,
    Name,
    Variable,
    StringLiteral,
    NumberLiteral,
    Symbol,
}

internal readonly record struct XPathToken(XPathTokenKind Kind, string Text, int Position);

/// <summary>
/// Parses the subset of XPath 2.0 the Schematron schemas of EPUBCheck use (and a little more): paths with every axis
/// but namespace, predicates, filter expressions, <c>if</c>, <c>some</c>/<c>every</c>, <c>for</c>, the comparison,
/// arithmetic, union, <c>intersect</c>/<c>except</c> and <c>is</c> operators, sequences and function calls.
/// </summary>
internal sealed class XPathParser
{
    private static readonly HashSet<string> s_kindTests = new(StringComparer.Ordinal)
    {
        "node", "text", "comment", "processing-instruction", "element", "attribute", "document-node",
    };

    private readonly List<XPathToken> _tokens;
    private readonly IReadOnlyDictionary<string, string> _namespaces;
    private int _pos;

    private XPathParser(string expression, IReadOnlyDictionary<string, string> namespaces)
    {
        _tokens = Tokenize(expression);
        _namespaces = namespaces;
    }

    /// <summary>Parses <paramref name="expression"/>; prefixes are looked up in <paramref name="namespaces"/>.</summary>
    /// <exception cref="XPathException">The expression is not correct.</exception>
    public static XPathExpr Parse(string expression, IReadOnlyDictionary<string, string> namespaces)
    {
        XPathParser parser = new(expression, namespaces);
        XPathExpr expr = parser.ParseExpr();
        if (parser.Current.Kind != XPathTokenKind.End)
        {
            throw new XPathException("unexpected \"" + parser.Current.Text + "\" in XPath expression \"" + expression + "\"");
        }

        return expr;
    }

    private XPathToken Current => _tokens[_pos];

    private XPathToken Peek(int k) => _tokens[Math.Min(_pos + k, _tokens.Count - 1)];

    private bool IsSymbol(string s) => Current.Kind == XPathTokenKind.Symbol && Current.Text == s;

    private bool IsName(string s) => Current.Kind == XPathTokenKind.Name && Current.Text == s;

    private XPathToken Next() => _tokens[_pos++];

    private void Expect(string symbol)
    {
        if (!IsSymbol(symbol))
        {
            throw new XPathException("expected \"" + symbol + "\" but found \"" + Current.Text + "\"");
        }

        _pos++;
    }

    private XPathExpr ParseExpr()
    {
        XPathExpr first = ParseExprSingle();
        if (!IsSymbol(","))
        {
            return first;
        }

        List<XPathExpr> items = [first];
        while (IsSymbol(","))
        {
            _pos++;
            items.Add(ParseExprSingle());
        }

        return new SequenceExpr(items);
    }

    private XPathExpr ParseExprSingle()
    {
        if (Current.Kind == XPathTokenKind.Name && Peek(1).Kind == XPathTokenKind.Variable)
        {
            switch (Current.Text)
            {
                case "some" or "every":
                    return ParseQuantified();
                case "for":
                    return ParseFor();
            }
        }

        if (IsName("if") && Peek(1) is { Kind: XPathTokenKind.Symbol, Text: "(" })
        {
            _pos += 2;
            XPathExpr condition = ParseExpr();
            Expect(")");
            ExpectName("then");
            XPathExpr then = ParseExprSingle();
            ExpectName("else");
            return new IfExpr(condition, then, ParseExprSingle());
        }

        return ParseOr();
    }

    private void ExpectName(string name)
    {
        if (!IsName(name))
        {
            throw new XPathException("expected \"" + name + "\" but found \"" + Current.Text + "\"");
        }

        _pos++;
    }

    private List<(string, XPathExpr)> ParseBindings()
    {
        List<(string, XPathExpr)> bindings = [];
        do
        {
            if (bindings.Count > 0)
            {
                _pos++;
            }

            string name = Next().Text;
            ExpectName("in");
            bindings.Add((name, ParseExprSingle()));
        }
        while (IsSymbol(","));
        return bindings;
    }

    private QuantifiedExpr ParseQuantified()
    {
        bool every = Next().Text == "every";
        List<(string, XPathExpr)> bindings = ParseBindings();
        ExpectName("satisfies");
        return new QuantifiedExpr(every, bindings, ParseExprSingle());
    }

    private ForExpr ParseFor()
    {
        _pos++;
        List<(string, XPathExpr)> bindings = ParseBindings();
        ExpectName("return");
        return new ForExpr(bindings, ParseExprSingle());
    }

    private XPathExpr ParseOr()
    {
        XPathExpr left = ParseAnd();
        while (IsName("or"))
        {
            _pos++;
            left = new OrExpr(left, ParseAnd());
        }

        return left;
    }

    private XPathExpr ParseAnd()
    {
        XPathExpr left = ParseComparison();
        while (IsName("and"))
        {
            _pos++;
            left = new AndExpr(left, ParseComparison());
        }

        return left;
    }

    private XPathExpr ParseComparison()
    {
        XPathExpr left = ParseRange();
        string? op = Current switch
        {
            { Kind: XPathTokenKind.Symbol, Text: "=" or "!=" or "<" or "<=" or ">" or ">=" or "<<" or ">>" } t => t.Text,
            { Kind: XPathTokenKind.Name, Text: "eq" or "ne" or "lt" or "le" or "gt" or "ge" or "is" } t => t.Text,
            _ => null,
        };
        if (op is null)
        {
            return left;
        }

        _pos++;
        return new ComparisonExpr(op, left, ParseRange());
    }

    private XPathExpr ParseRange()
    {
        XPathExpr left = ParseAdditive();
        if (!IsName("to"))
        {
            return left;
        }

        _pos++;
        return new RangeExpr(left, ParseAdditive());
    }

    private XPathExpr ParseAdditive()
    {
        XPathExpr left = ParseMultiplicative();
        while (IsSymbol("+") || IsSymbol("-"))
        {
            string op = Next().Text;
            left = new ArithmeticExpr(op, left, ParseMultiplicative());
        }

        return left;
    }

    private XPathExpr ParseMultiplicative()
    {
        XPathExpr left = ParseUnion();
        while (IsSymbol("*") || IsName("div") || IsName("idiv") || IsName("mod"))
        {
            string op = Next().Text;
            left = new ArithmeticExpr(op, left, ParseUnion());
        }

        return left;
    }

    private XPathExpr ParseUnion()
    {
        XPathExpr left = ParseIntersectExcept();
        while (IsSymbol("|") || IsName("union"))
        {
            _pos++;
            left = new SetExpr("union", left, ParseIntersectExcept());
        }

        return left;
    }

    private XPathExpr ParseIntersectExcept()
    {
        XPathExpr left = ParseUnary();
        while (IsName("intersect") || IsName("except"))
        {
            string op = Next().Text;
            left = new SetExpr(op, left, ParseUnary());
        }

        return left;
    }

    private XPathExpr ParseUnary()
    {
        bool negate = false;
        while (IsSymbol("-") || IsSymbol("+"))
        {
            negate ^= Next().Text == "-";
        }

        XPathExpr operand = ParsePath();
        return negate ? new NegateExpr(operand) : operand;
    }

    private XPathExpr ParsePath()
    {
        List<(bool Descendant, XPathExpr Step)> steps = [];
        bool absolute = false;
        if (IsSymbol("/") || IsSymbol("//"))
        {
            absolute = true;
            bool descendant = Next().Text == "//";
            if (descendant)
            {
                steps.Add((false, new AxisStep(Axis.DescendantOrSelf, new KindTest(null), [])));
            }
            else if (!StartsStep())
            {
                return new PathExpr(true, []);
            }
        }

        steps.Add((false, ParseStep()));
        while (IsSymbol("/") || IsSymbol("//"))
        {
            if (Next().Text == "//")
            {
                steps.Add((false, new AxisStep(Axis.DescendantOrSelf, new KindTest(null), [])));
            }

            steps.Add((false, ParseStep()));
        }

        return !absolute && steps.Count == 1 ? steps[0].Step : new PathExpr(absolute, [.. steps.ConvertAll(s => s.Step)]);
    }

    private bool StartsStep() => Current.Kind switch
    {
        XPathTokenKind.Name or XPathTokenKind.Variable or XPathTokenKind.StringLiteral or XPathTokenKind.NumberLiteral => true,
        XPathTokenKind.Symbol => Current.Text is "@" or "." or ".." or "*" or "(",
        _ => false,
    };

    private XPathExpr ParseStep()
    {
        if (IsSymbol(".."))
        {
            _pos++;
            return WithPredicates(new AxisStep(Axis.Parent, new KindTest(null), []));
        }

        if (IsSymbol("@"))
        {
            _pos++;
            return ParseAxisStep(Axis.Attribute);
        }

        if (Current.Kind == XPathTokenKind.Name && Peek(1) is { Kind: XPathTokenKind.Symbol, Text: "::" })
        {
            string axisName = Next().Text;
            _pos++;
            return ParseAxisStep(ParseAxis(axisName));
        }

        if (IsSymbol("*") || (Current.Kind == XPathTokenKind.Name && !IsFunctionCall()))
        {
            return ParseAxisStep(Axis.Child);
        }

        XPathExpr primary = ParsePrimary();
        return WithPredicates(primary);
    }

    private bool IsFunctionCall() =>
        Peek(1) is { Kind: XPathTokenKind.Symbol, Text: "(" } && !s_kindTests.Contains(Current.Text);

    private static Axis ParseAxis(string name) => name switch
    {
        "child" => Axis.Child,
        "descendant" => Axis.Descendant,
        "attribute" => Axis.Attribute,
        "self" => Axis.Self,
        "descendant-or-self" => Axis.DescendantOrSelf,
        "following-sibling" => Axis.FollowingSibling,
        "following" => Axis.Following,
        "parent" => Axis.Parent,
        "ancestor" => Axis.Ancestor,
        "preceding-sibling" => Axis.PrecedingSibling,
        "preceding" => Axis.Preceding,
        "ancestor-or-self" => Axis.AncestorOrSelf,
        _ => throw new XPathException("unsupported axis \"" + name + "\""),
    };

    private XPathExpr ParseAxisStep(Axis axis)
    {
        NodeTest test = ParseNodeTest();
        return WithPredicates(new AxisStep(axis, test, []));
    }

    private NodeTest ParseNodeTest()
    {
        if (IsSymbol("*"))
        {
            _pos++;
            if (IsSymbol(":"))
            {
                _pos++;
                return new NameTest(null, Next().Text);
            }

            return new NameTest(null, null);
        }

        XPathToken name = Next();
        if (name.Kind != XPathTokenKind.Name)
        {
            throw new XPathException("expected a name but found \"" + name.Text + "\"");
        }

        if (s_kindTests.Contains(name.Text) && IsSymbol("("))
        {
            _pos++;
            while (!IsSymbol(")"))
            {
                Next();
            }

            _pos++;
            return new KindTest(name.Text);
        }

        int colon = name.Text.IndexOf(':', StringComparison.Ordinal);
        if (colon < 0)
        {
            return new NameTest(string.Empty, name.Text);
        }

        string ns = ResolvePrefix(name.Text[..colon]);
        string local = name.Text[(colon + 1)..];
        return new NameTest(ns, local == "*" ? null : local);
    }

    private string ResolvePrefix(string prefix) =>
        _namespaces.TryGetValue(prefix, out string? ns) ? ns : throw new XPathException("undeclared namespace prefix \"" + prefix + "\"");

    private XPathExpr WithPredicates(XPathExpr expr)
    {
        List<XPathExpr> predicates = [];
        while (IsSymbol("["))
        {
            _pos++;
            predicates.Add(ParseExpr());
            Expect("]");
        }

        if (predicates.Count == 0)
        {
            return expr;
        }

        return expr is AxisStep step ? new AxisStep(step.Axis, step.Test, [.. step.Predicates, .. predicates]) : new FilterExpr(expr, predicates);
    }

    private XPathExpr ParsePrimary()
    {
        XPathToken t = Next();
        switch (t.Kind)
        {
            case XPathTokenKind.StringLiteral:
                return new LiteralExpr(t.Text);
            case XPathTokenKind.NumberLiteral:
                return new LiteralExpr(double.Parse(t.Text, NumberStyles.Float, CultureInfo.InvariantCulture));
            case XPathTokenKind.Variable:
                return new VariableExpr(t.Text);
            case XPathTokenKind.Symbol when t.Text == ".":
                return new ContextItemExpr();
            case XPathTokenKind.Symbol when t.Text == "(":
                if (IsSymbol(")"))
                {
                    _pos++;
                    return new SequenceExpr([]);
                }

                XPathExpr inner = ParseExpr();
                Expect(")");
                return new ParenthesizedExpr(inner);
            case XPathTokenKind.Name when IsSymbol("("):
                _pos++;
                List<XPathExpr> args = [];
                if (!IsSymbol(")"))
                {
                    args.Add(ParseExprSingle());
                    while (IsSymbol(","))
                    {
                        _pos++;
                        args.Add(ParseExprSingle());
                    }
                }

                Expect(")");
                return new FunctionCallExpr(t.Text, args);
            default:
                throw new XPathException("unexpected \"" + t.Text + "\"");
        }
    }

    // Names may contain "-" and "."; "prefix:local", "prefix:*" are one name token; "::" and other symbols apart.
    private static List<XPathToken> Tokenize(string s)
    {
        List<XPathToken> tokens = [];
        int i = 0;
        while (i < s.Length)
        {
            char c = s[i];
            if (char.IsWhiteSpace(c))
            {
                i++;
                continue;
            }

            int start = i;
            if (c is '"' or '\'')
            {
                StringBuilder sb = new();
                i++;
                while (true)
                {
                    if (i >= s.Length)
                    {
                        throw new XPathException("unterminated string literal in \"" + s + "\"");
                    }

                    if (s[i] == c)
                    {
                        if (i + 1 < s.Length && s[i + 1] == c)
                        {
                            sb.Append(c);
                            i += 2;
                            continue;
                        }

                        i++;
                        break;
                    }

                    sb.Append(s[i++]);
                }

                tokens.Add(new XPathToken(XPathTokenKind.StringLiteral, sb.ToString(), start));
            }
            else if (char.IsAsciiDigit(c) || (c == '.' && i + 1 < s.Length && char.IsAsciiDigit(s[i + 1])))
            {
                while (i < s.Length && (char.IsAsciiDigit(s[i]) || s[i] == '.'))
                {
                    i++;
                }

                if (i < s.Length && s[i] is 'e' or 'E')
                {
                    i++;
                    if (i < s.Length && s[i] is '+' or '-')
                    {
                        i++;
                    }

                    while (i < s.Length && char.IsAsciiDigit(s[i]))
                    {
                        i++;
                    }
                }

                tokens.Add(new XPathToken(XPathTokenKind.NumberLiteral, s[start..i], start));
            }
            else if (c == '$')
            {
                i++;
                int nameStart = i;
                i = SkipName(s, i, allowWildcard: false);
                tokens.Add(new XPathToken(XPathTokenKind.Variable, s[nameStart..i], start));
            }
            else if (IsNameStart(c))
            {
                i = SkipName(s, i, allowWildcard: true);
                tokens.Add(new XPathToken(XPathTokenKind.Name, s[start..i], start));
            }
            else
            {
                string two = i + 1 < s.Length ? s.Substring(i, 2) : string.Empty;
                string symbol = two is "//" or "::" or "!=" or "<=" or ">=" or "<<" or ">>" or ".." ? two : c.ToString();
                i += symbol.Length;
                tokens.Add(new XPathToken(XPathTokenKind.Symbol, symbol, start));
            }
        }

        tokens.Add(new XPathToken(XPathTokenKind.End, string.Empty, s.Length));
        return tokens;
    }

    private static bool IsNameStart(char c) => char.IsLetter(c) || c == '_';

    private static bool IsNameChar(char c) => char.IsLetterOrDigit(c) || c is '_' or '-' or '.';

    private static int SkipName(string s, int i, bool allowWildcard)
    {
        while (i < s.Length && IsNameChar(s[i]))
        {
            i++;
        }

        // A prefix: "p:local" or "p:*", but not "p::axis".
        if (i + 1 < s.Length && s[i] == ':' && s[i + 1] != ':')
        {
            if (allowWildcard && s[i + 1] == '*')
            {
                return i + 2;
            }

            if (IsNameStart(s[i + 1]))
            {
                i++;
                while (i < s.Length && IsNameChar(s[i]))
                {
                    i++;
                }
            }
        }

        return i;
    }
}
