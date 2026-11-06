using System;
using System.Collections.Generic;
using System.Text;
using Signet.EpubCheck.Core.Jing.Patterns;
using Signet.EpubCheck.Core.Jing.Sax;

namespace Signet.EpubCheck.Core.Jing.Parse.Compact;

/// <summary>
/// The parser of the RELAX NG compact syntax: a port of Jing's <c>CompactSyntax.jj</c> (the JavaCC grammar), production
/// by production, as a recursive descent parser. JavaCC decides most choices on the next token; the few places where
/// Jing's grammar looks further (<c>LOOKAHEAD(2)</c> and the syntactic lookahead that tells a grammar from a pattern
/// at the top level) look the same tokens ahead here. Comments are not kept: Jing's schema builder throws them away.
/// A syntax error stops the parse at the token where it was found (Jing reports "syntax error" there); the other errors
/// are reported and the parse goes on, as in Jing.
/// </summary>
internal sealed class CompactSyntax : IParseContext
{
    // The contexts of a name class (bit flags of Jing: an element name is context 0).
    private const int InElement = 0;
    private const int InAttribute = 1;
    private const int InAnyName = 2;
    private const int InNsName = 4;

    private const string XmlPrefix = "xml";
    private const string XmlnsName = "xmlns";
    private const string XsdPrefix = "xsd";
    private const string ErrorNamespace = "#error";
    private const string BuiltinTokenType = "token";
    private const char PrefixSeparator = ':';
    private const string PrefixStarSuffix = ":*";
    private const int FirstLine = 1;
    private const int FirstColumn = 1;
    private const int TripleQuotedMinLength = 6;
    private const int TripleQuoteLength = 3;

    private readonly CompactLexer _lexer;
    private readonly string? _sourceUri;
    private readonly ISchemaBuilder _sb;
    private readonly ISaxErrorHandler? _eh;
    private readonly JingLocalizer _localizer;
    private readonly Dictionary<string, string> _namespaceMap = new(StringComparer.Ordinal);
    private readonly Dictionary<string, string> _datatypesMap = new(StringComparer.Ordinal);
    private readonly HashSet<string> _attributeNames = new(StringComparer.Ordinal);
    private string _defaultNamespace = GrammarSection.InheritNs;
    private bool _hadError;
    private bool _annotationsIncludeElements;
    private int _next;

    public CompactSyntax(string text, string? sourceUri, ISchemaBuilder sb, ISaxErrorHandler? eh, JingLocalizer localizer)
    {
        _lexer = new CompactLexer(new CompactCharStream(text));
        _sourceUri = sourceUri;
        _sb = sb;
        _eh = eh;
        _localizer = localizer;
    }

    public string? BaseUri => _sourceUri;

    /// <exception cref="IllegalSchemaException">The schema has errors (reported).</exception>
    public Pattern Parse(IScope scope) => Run(() => Input(scope))!;

    /// <exception cref="IllegalSchemaException">The schema has errors (reported).</exception>
    public Pattern? ParseInclude(IIncludedGrammar g) => Run(() => IncludedGrammar(g));

    public string? ResolveNamespacePrefix(string prefix) =>
        _namespaceMap.TryGetValue(prefix, out string? result) && result.Length > 0 ? result : null;

    public ISet<string> Prefixes() => new HashSet<string>(_namespaceMap.Keys, StringComparer.Ordinal);

    public bool IsUnparsedEntity(string entityName) => false;

    public bool IsNotation(string notationName) => false;

    public IParseContext Copy() => this;

    private Pattern? Run(Func<Pattern?> production)
    {
        try
        {
            Pattern? p = production();
            if (!_hadError)
            {
                return p;
            }
        }
        catch (ParseException e)
        {
            Error("syntax_error", e.Token);
        }
        catch (EscapeSyntaxException e)
        {
            Report(_localizer.Message(e.Key), e.LineNumber, e.ColumnNumber);
        }

        throw new IllegalSchemaException();
    }

    private Pattern Input(IScope scope)
    {
        Preamble();
        Pattern p;
        if (TopLevelLookahead())
        {
            p = TopLevelGrammar(scope);
        }
        else
        {
            p = Expr(true, scope, null, null);
            Consume(TokenKind.Eof);
        }

        return p;
    }

    private Pattern? IncludedGrammar(IIncludedGrammar g)
    {
        Preamble();
        AnnotationsImpl? a;
        if (TopLevelLookahead())
        {
            a = GrammarBody(g, g, null);
        }
        else
        {
            a = Annotations();
            Consume(TokenKind.Grammar);
            Consume(TokenKind.LeftBrace);
            a = GrammarBody(g, g, a);
            Consume(TokenKind.RightBrace);
        }

        Pattern? p = g.EndIncludedGrammar(_sb.MakeLocation(_sourceUri, FirstLine, FirstColumn), a);
        Consume(TokenKind.Eof);
        return p;
    }

    private Pattern TopLevelGrammar(IScope scope)
    {
        IGrammar g = _sb.MakeGrammar(scope);
        AnnotationsImpl? a = GrammarBody(g, g, null);
        Pattern p = g.EndGrammar(_sb.MakeLocation(_sourceUri, FirstLine, FirstColumn), a);
        Consume(TokenKind.Eof);
        return p;
    }

    // TopLevelLookahead(): whether the schema is a grammar (definitions) rather than a pattern. JavaCC tries the
    // alternatives in order, backtracking inside the lookahead.
    private bool TopLevelLookahead()
    {
        if (Kind(1) == TokenKind.PrefixedName && Kind(2) == TokenKind.LeftBracket)
        {
            return true;
        }

        if (IsIdentifier(Kind(1)) && Kind(2) is TokenKind.LeftBracket or TokenKind.Equals or TokenKind.InterleaveAssign or TokenKind.ChoiceAssign)
        {
            return true;
        }

        if (IsGrammarKeyword(Kind(1)))
        {
            return true;
        }

        int i = 1;
        if (ScanBody(ref i) && ScanAfterAnnotations(i))
        {
            return true;
        }

        i = 1;
        if (!ScanDocumentation(ref i))
        {
            return false;
        }

        int afterDocumentation = i;
        if (!ScanBody(ref i))
        {
            i = afterDocumentation;
        }

        return ScanAfterAnnotations(i);
    }

    private bool ScanAfterAnnotations(int i) =>
        (IsIdentifier(Kind(i)) && Kind(i + 1) is TokenKind.Equals or TokenKind.InterleaveAssign or TokenKind.ChoiceAssign)
        || IsGrammarKeyword(Kind(i));

    private bool ScanDocumentation(ref int i)
    {
        if (Kind(i) is not (TokenKind.Documentation or TokenKind.DocumentationAfterSingleLineComment))
        {
            return false;
        }

        while (Kind(i) is TokenKind.Documentation or TokenKind.DocumentationAfterSingleLineComment)
        {
            i++;
            while (Kind(i) == TokenKind.DocumentationContinue)
            {
                i++;
            }
        }

        return true;
    }

    // LookaheadBody(): "[" (PREFIXED_NAME | UnprefixedName | "=" | LITERAL | "~" | LookaheadBody)* "]".
    private bool ScanBody(ref int i)
    {
        if (Kind(i) != TokenKind.LeftBracket)
        {
            return false;
        }

        int j = i + 1;
        while (true)
        {
            TokenKind kind = Kind(j);
            if (kind is TokenKind.PrefixedName or TokenKind.Equals or TokenKind.Literal or TokenKind.Tilde || IsUnprefixedName(kind))
            {
                j++;
            }
            else if (kind != TokenKind.LeftBracket || !ScanBody(ref j))
            {
                break;
            }
        }

        if (Kind(j) != TokenKind.RightBracket)
        {
            return false;
        }

        i = j + 1;
        return true;
    }

    private void Preamble()
    {
        while (Kind(1) is TokenKind.Namespace or TokenKind.Default or TokenKind.Datatypes)
        {
            if (Kind(1) == TokenKind.Datatypes)
            {
                DatatypesDecl();
            }
            else
            {
                NamespaceDecl();
            }
        }

        _namespaceMap[XmlPrefix] = WellKnownNamespaces.Xml;
        _datatypesMap.TryAdd(XsdPrefix, WellKnownNamespaces.XmlSchemaDatatypes);
    }

    private void NamespaceDecl()
    {
        LocatedString? prefix = null;
        bool isDefault = false;
        if (Kind(1) == TokenKind.Namespace)
        {
            Consume(TokenKind.Namespace);
            prefix = UnprefixedName();
        }
        else
        {
            Consume(TokenKind.Default);
            isDefault = true;
            Consume(TokenKind.Namespace);
            if (IsUnprefixedName(Kind(1)))
            {
                prefix = UnprefixedName();
            }
        }

        Consume(TokenKind.Equals);
        string namespaceName = NamespaceName();
        if (isDefault)
        {
            _defaultNamespace = namespaceName;
        }

        if (prefix is null)
        {
            return;
        }

        if (prefix.Value == XmlnsName)
        {
            Error("xmlns_prefix", prefix.Token);
        }
        else if (prefix.Value == XmlPrefix)
        {
            if (namespaceName != WellKnownNamespaces.Xml)
            {
                Error("xml_prefix_bad_uri", prefix.Token);
            }
        }
        else if (namespaceName == WellKnownNamespaces.Xml)
        {
            Error("xml_uri_bad_prefix", prefix.Token);
        }
        else
        {
            // Jing also notes the prefix of the compatibility annotations here, for the documentation elements it
            // makes for Trang; this builder keeps no annotation elements.
            _namespaceMap[prefix.Value] = namespaceName;
        }
    }

    private string NamespaceName()
    {
        if (Kind(1) == TokenKind.Inherit)
        {
            Consume(TokenKind.Inherit);
            return GrammarSection.InheritNs;
        }

        return Literal();
    }

    private void DatatypesDecl()
    {
        Consume(TokenKind.Datatypes);
        LocatedString prefix = UnprefixedName();
        Consume(TokenKind.Equals);
        _datatypesMap[prefix.Value] = Literal();
    }

    private Pattern AnnotatedPrimaryExpr(bool topLevel, IScope scope, CompactToken?[] except)
    {
        AnnotationsImpl? a = Annotations();
        Pattern p = PrimaryExpr(topLevel, scope, a, except);
        FollowingAnnotations(topLevel);
        return p;
    }

    // (">>" AnnotationElement)*: allowed after a pattern that is not the whole schema.
    private void FollowingAnnotations(bool topLevel)
    {
        while (Kind(1) == TokenKind.FollowAnnotation)
        {
            CompactToken t = Consume(TokenKind.FollowAnnotation);
            AnnotationElement(false);
            if (topLevel)
            {
                Error("top_level_follow_annotation", t);
            }
        }
    }

    private Pattern PrimaryExpr(bool topLevel, IScope scope, AnnotationsImpl? a, CompactToken?[] except) => Kind(1) switch
    {
        TokenKind.Element => ElementExpr(scope, a),
        TokenKind.Attribute => AttributeExpr(scope, a),
        TokenKind.Grammar => GrammarExpr(scope, a),
        TokenKind.External => ExternalRefExpr(scope, a),
        TokenKind.List => ListExpr(scope, a),
        TokenKind.Mixed => MixedExpr(scope, a),
        TokenKind.LeftParen => ParenExpr(topLevel, scope, a),
        TokenKind.Identifier or TokenKind.EscapedIdentifier => IdentifierExpr(scope, a),
        TokenKind.Parent => ParentExpr(scope, a),
        TokenKind.String or TokenKind.Token or TokenKind.PrefixedName => DataExpr(topLevel, scope, a, except),
        TokenKind.Literal => ValueExpr(topLevel, a),
        TokenKind.Text => _sb.MakeText(Location(Consume(TokenKind.Text)), a),
        TokenKind.Empty => _sb.MakeEmpty(Location(Consume(TokenKind.Empty)), a),
        TokenKind.NotAllowed => _sb.MakeNotAllowed(Location(Consume(TokenKind.NotAllowed)), a),
        _ => throw new ParseException(Token(1)),
    };

    private Pattern Expr(bool topLevel, IScope scope, CompactToken? t, AnnotationsImpl? a)
    {
        List<Pattern> patterns = [];
        bool[] hadOccur = new bool[1];
        CompactToken?[] except = new CompactToken?[1];
        Pattern p = UnaryExpr(topLevel, scope, hadOccur, except);
        patterns.Add(p);
        TokenKind op = Kind(1);
        if (op is TokenKind.Choice or TokenKind.Interleave or TokenKind.Comma)
        {
            // Jing checks the "-" of the first operand before the loop only for a choice.
            if (op == TokenKind.Choice)
            {
                CheckExcept(except);
            }

            while (Kind(1) == op)
            {
                t = Consume(op);
                patterns.Add(UnaryExpr(topLevel, scope, null, except));
                CheckExcept(except);
            }

            ILocator loc = Location(t!);
            p = op switch
            {
                TokenKind.Choice => _sb.MakeChoice(patterns, loc, a),
                TokenKind.Interleave => _sb.MakeInterleave(patterns, loc, a),
                _ => _sb.MakeGroup(patterns, loc, a),
            };
        }

        // Jing wraps a single annotated pattern in a group of one (or annotates it): the same pattern for this builder.
        return p;
    }

    private Pattern UnaryExpr(bool topLevel, IScope scope, bool[]? hadOccur, CompactToken?[] except)
    {
        Pattern p = AnnotatedPrimaryExpr(topLevel, scope, except);
        if (Kind(1) is TokenKind.Plus or TokenKind.QuestionMark or TokenKind.Star)
        {
            if (hadOccur is not null)
            {
                hadOccur[0] = true;
            }

            CompactToken t = Consume(Kind(1));
            CheckExcept(except);
            ILocator loc = Location(t);
            p = t.Kind switch
            {
                TokenKind.Plus => _sb.MakeOneOrMore(p, loc, null),
                TokenKind.QuestionMark => _sb.MakeOptional(p, loc, null),
                _ => _sb.MakeZeroOrMore(p, loc, null),
            };
            FollowingAnnotations(topLevel);
        }

        return p;
    }

    private Pattern ElementExpr(IScope scope, AnnotationsImpl? a)
    {
        CompactToken t = Consume(TokenKind.Element);
        NameClass nc = NameClass(InElement);
        Pattern p = BracedExpr(scope);
        return _sb.MakeElement(nc, p, Location(t), a);
    }

    private Pattern AttributeExpr(IScope scope, AnnotationsImpl? a)
    {
        CompactToken t = Consume(TokenKind.Attribute);
        NameClass nc = NameClass(InAttribute);
        Pattern p = BracedExpr(scope);
        return _sb.MakeAttribute(nc, p, Location(t), a);
    }

    private Pattern ListExpr(IScope scope, AnnotationsImpl? a)
    {
        CompactToken t = Consume(TokenKind.List);
        return _sb.MakeList(BracedExpr(scope), Location(t), a);
    }

    private Pattern MixedExpr(IScope scope, AnnotationsImpl? a)
    {
        CompactToken t = Consume(TokenKind.Mixed);
        return _sb.MakeMixed(BracedExpr(scope), Location(t), a);
    }

    private Pattern BracedExpr(IScope scope)
    {
        Consume(TokenKind.LeftBrace);
        Pattern p = Expr(false, scope, null, null);
        Consume(TokenKind.RightBrace);
        return p;
    }

    private NameClass NameClass(int context)
    {
        AnnotationsImpl? a = Annotations();
        TokenKind kind = Kind(1);
        if (kind == TokenKind.Star)
        {
            return AnyNameExceptClass(context, a);
        }

        if (kind == TokenKind.PrefixStar)
        {
            return NsNameExceptClass(context, a);
        }

        NameClass nc = PrimaryNameClass(context, a);
        AnnotateAfter();
        return NameClassAlternatives(context, nc);
    }

    private void AnnotateAfter()
    {
        while (Kind(1) == TokenKind.FollowAnnotation)
        {
            Consume(TokenKind.FollowAnnotation);
            AnnotationElement(false);
        }
    }

    private NameClass NameClassAlternatives(int context, NameClass nc)
    {
        if (Kind(1) != TokenKind.Choice)
        {
            return nc;
        }

        List<NameClass> nameClasses = [nc];
        CompactToken t = Token(1);
        while (Kind(1) == TokenKind.Choice)
        {
            t = Consume(TokenKind.Choice);
            nameClasses.Add(BasicNameClass(context));
            AnnotateAfter();
        }

        return _sb.MakeNameClassChoice(nameClasses, Location(t), null);
    }

    private NameClass BasicNameClass(int context)
    {
        AnnotationsImpl? a = Annotations();
        return Kind(1) is TokenKind.PrefixStar or TokenKind.Star ? OpenNameClass(context, a) : PrimaryNameClass(context, a);
    }

    private NameClass PrimaryNameClass(int context, AnnotationsImpl? a)
    {
        TokenKind kind = Kind(1);
        if (kind == TokenKind.PrefixedName)
        {
            return PrefixedNameClass(a);
        }

        if (kind == TokenKind.LeftParen)
        {
            return ParenNameClass(context);
        }

        if (IsUnprefixedName(kind))
        {
            LocatedString name = UnprefixedName();
            string ns = (context & (InAttribute | InElement)) == InAttribute ? string.Empty : _defaultNamespace;
            return _sb.MakeName(ns, name.Value, null, Location(name.Token), a);
        }

        throw new ParseException(Token(1));
    }

    private NameClass OpenNameClass(int context, AnnotationsImpl? a)
    {
        if (Kind(1) == TokenKind.PrefixStar)
        {
            LocatedString ns = NsName();
            CheckNsName(context, ns);
            return _sb.MakeNsName(ns.Value, Location(ns.Token), a);
        }

        CompactToken t = Consume(TokenKind.Star);
        CheckAnyName(context, t);
        return _sb.MakeAnyName(Location(t), a);
    }

    private NameClass PrefixedNameClass(AnnotationsImpl? a)
    {
        CompactToken t = Consume(TokenKind.PrefixedName);
        (string prefix, string localName) = SplitQName(t.Image);
        return _sb.MakeName(LookupPrefix(prefix, t), localName, prefix, Location(t), a);
    }

    private NameClass NsNameExceptClass(int context, AnnotationsImpl? a)
    {
        LocatedString ns = NsName();
        CheckNsName(context, ns);
        NameClass nc;
        if (Kind(1) == TokenKind.Minus)
        {
            nc = _sb.MakeNsName(ns.Value, ExceptNameClass(context | InNsName), Location(ns.Token), a);
            AnnotateAfter();
            return nc;
        }

        nc = _sb.MakeNsName(ns.Value, Location(ns.Token), a);
        AnnotateAfter();
        return NameClassAlternatives(context, nc);
    }

    private LocatedString NsName()
    {
        CompactToken t = Consume(TokenKind.PrefixStar);
        string prefix = t.Image[..^PrefixStarSuffix.Length];
        return new LocatedString(LookupPrefix(prefix, t), t);
    }

    private NameClass AnyNameExceptClass(int context, AnnotationsImpl? a)
    {
        CompactToken t = Consume(TokenKind.Star);
        CheckAnyName(context, t);
        NameClass nc;
        if (Kind(1) == TokenKind.Minus)
        {
            nc = _sb.MakeAnyName(ExceptNameClass(context | InAnyName), Location(t), a);
            AnnotateAfter();
            return nc;
        }

        nc = _sb.MakeAnyName(Location(t), a);
        AnnotateAfter();
        return NameClassAlternatives(context, nc);
    }

    private NameClass ParenNameClass(int context)
    {
        Consume(TokenKind.LeftParen);
        NameClass nc = NameClass(context);
        Consume(TokenKind.RightParen);
        return nc;
    }

    private NameClass ExceptNameClass(int context)
    {
        Consume(TokenKind.Minus);
        return BasicNameClass(context);
    }

    private Pattern GrammarExpr(IScope scope, AnnotationsImpl? a)
    {
        CompactToken t = Consume(TokenKind.Grammar);
        IGrammar g = _sb.MakeGrammar(scope);
        Consume(TokenKind.LeftBrace);
        a = GrammarBody(g, g, a);
        Consume(TokenKind.RightBrace);
        return g.EndGrammar(Location(t), a);
    }

    private Pattern ParenExpr(bool topLevel, IScope scope, AnnotationsImpl? a)
    {
        CompactToken t = Consume(TokenKind.LeftParen);
        Pattern p = Expr(topLevel, scope, t, a);
        Consume(TokenKind.RightParen);
        return p;
    }

    private AnnotationsImpl? GrammarBody(IGrammarSection section, IScope scope, AnnotationsImpl? a)
    {
        while (AnnotationElementNotKeywordAhead())
        {
            AnnotationElementNotKeyword();
        }

        while (IsAnnotationsStart(Kind(1)) || IsIdentifier(Kind(1)) || Kind(1) is TokenKind.Start or TokenKind.Include or TokenKind.Div)
        {
            GrammarComponent(section, scope);
        }

        return a;
    }

    private void GrammarComponent(IGrammarSection section, IScope scope)
    {
        AnnotationsImpl? a = Annotations();
        switch (Kind(1))
        {
            case TokenKind.Include:
                Include(section, scope, a);
                break;
            case TokenKind.Div:
                Div(section, scope, a, includeBody: false);
                break;
            default:
                Definition(section, scope, a);
                break;
        }

        while (AnnotationElementNotKeywordAhead())
        {
            AnnotationElementNotKeyword();
        }
    }

    private void Definition(IGrammarSection section, IScope scope, AnnotationsImpl? a)
    {
        if (Kind(1) == TokenKind.Start)
        {
            CompactToken t = Consume(TokenKind.Start);
            GrammarCombine? startCombine = AssignOp();
            Pattern start = Expr(false, scope, null, null);
            section.Define(GrammarSection.Start, startCombine, start, Location(t), a);
            return;
        }

        LocatedString name = Identifier();
        GrammarCombine? combine = AssignOp();
        Pattern p = Expr(false, scope, null, null);
        section.Define(name.Value, combine, p, Location(name.Token), a);
    }

    private GrammarCombine? AssignOp()
    {
        switch (Kind(1))
        {
            case TokenKind.Equals:
                Consume(TokenKind.Equals);
                return null;
            case TokenKind.ChoiceAssign:
                Consume(TokenKind.ChoiceAssign);
                return GrammarCombine.Choice;
            case TokenKind.InterleaveAssign:
                Consume(TokenKind.InterleaveAssign);
                return GrammarCombine.Interleave;
            default:
                throw new ParseException(Token(1));
        }
    }

    private void Include(IGrammarSection section, IScope scope, AnnotationsImpl? a)
    {
        // An include in the compact syntax is never inside another include (the include body has no "include").
        IInclude include = section.MakeInclude()!;
        CompactToken t = Consume(TokenKind.Include);
        string href = Literal();
        string ns = Inherit();
        if (Kind(1) == TokenKind.LeftBrace)
        {
            Consume(TokenKind.LeftBrace);
            a = IncludeBody(include, scope, a);
            Consume(TokenKind.RightBrace);
        }

        try
        {
            include.EndInclude(href, _sourceUri, ns, Location(t), a);
        }
        catch (IllegalSchemaException)
        {
            // Reported.
        }
    }

    private AnnotationsImpl? IncludeBody(IGrammarSection section, IScope scope, AnnotationsImpl? a)
    {
        while (AnnotationElementNotKeywordAhead())
        {
            AnnotationElementNotKeyword();
        }

        while (IsAnnotationsStart(Kind(1)) || IsIdentifier(Kind(1)) || Kind(1) is TokenKind.Start or TokenKind.Div)
        {
            AnnotationsImpl? componentAnnotations = Annotations();
            if (Kind(1) == TokenKind.Div)
            {
                Div(section, scope, componentAnnotations, includeBody: true);
            }
            else
            {
                Definition(section, scope, componentAnnotations);
            }

            while (AnnotationElementNotKeywordAhead())
            {
                AnnotationElementNotKeyword();
            }
        }

        return a;
    }

    private void Div(IGrammarSection section, IScope scope, AnnotationsImpl? a, bool includeBody)
    {
        IDiv div = section.MakeDiv();
        CompactToken t = Consume(TokenKind.Div);
        Consume(TokenKind.LeftBrace);
        a = includeBody ? IncludeBody(div, scope, a) : GrammarBody(div, scope, a);
        Consume(TokenKind.RightBrace);
        div.EndDiv(Location(t), a);
    }

    private Pattern ExternalRefExpr(IScope scope, AnnotationsImpl? a)
    {
        CompactToken t = Consume(TokenKind.External);
        string href = Literal();
        string ns = Inherit();
        try
        {
            return _sb.MakeExternalRef(href, _sourceUri, ns, scope, Location(t), a);
        }
        catch (IllegalSchemaException)
        {
            return _sb.MakeErrorPattern();
        }
    }

    private string Inherit()
    {
        if (Kind(1) != TokenKind.Inherit)
        {
            return _defaultNamespace;
        }

        Consume(TokenKind.Inherit);
        Consume(TokenKind.Equals);
        return Prefix();
    }

    private Pattern ParentExpr(IScope scope, AnnotationsImpl? a)
    {
        Consume(TokenKind.Parent);
        LocatedString name = Identifier();
        return scope.MakeParentRef(name.Value, Location(name.Token), a);
    }

    private Pattern IdentifierExpr(IScope scope, AnnotationsImpl? a)
    {
        LocatedString name = Identifier();
        return scope.MakeRef(name.Value, Location(name.Token), a);
    }

    private Pattern ValueExpr(bool topLevel, AnnotationsImpl? a)
    {
        CompactToken first = Token(1);
        string value = Literal();
        if (topLevel && _annotationsIncludeElements)
        {
            Error("top_level_follow_annotation", first);
            a = null;
        }

        return _sb.MakeValue(string.Empty, BuiltinTokenType, value, this, _defaultNamespace, Location(first), a);
    }

    private Pattern DataExpr(bool topLevel, IScope scope, AnnotationsImpl? a, CompactToken?[] except)
    {
        CompactToken datatypeToken = Consume(Kind(1));
        string datatype = datatypeToken.Image;
        ILocator loc = Location(datatypeToken);
        string datatypeUri;
        int colon = datatype.IndexOf(PrefixSeparator, StringComparison.Ordinal);
        if (colon < 0)
        {
            datatypeUri = string.Empty;
        }
        else
        {
            datatypeUri = LookupDatatype(datatype[..colon], datatypeToken);
            datatype = datatype[(colon + 1)..];
        }

        if (Kind(1) == TokenKind.Literal)
        {
            string s = Literal();
            if (topLevel && _annotationsIncludeElements)
            {
                Error("top_level_follow_annotation", datatypeToken);
                a = null;
            }

            return _sb.MakeValue(datatypeUri, datatype, s, this, _defaultNamespace, loc, a);
        }

        IDataPatternBuilder dpb = _sb.MakeDataPatternBuilder(datatypeUri, datatype, loc);
        Pattern? e = null;
        if (Kind(1) == TokenKind.LeftBrace)
        {
            Params(dpb);
        }

        if (Kind(1) == TokenKind.Minus)
        {
            e = Except(scope, except);
        }

        return e is null ? dpb.MakePattern(loc, a) : dpb.MakePattern(e, loc, a);
    }

    private LocatedString Identifier() => Kind(1) switch
    {
        TokenKind.Identifier => new LocatedString(Token(1).Image, Consume(TokenKind.Identifier)),
        TokenKind.EscapedIdentifier => new LocatedString(Token(1).Image[1..], Consume(TokenKind.EscapedIdentifier)),
        _ => throw new ParseException(Token(1)),
    };

    private string Prefix()
    {
        CompactToken t = Token(1);
        string prefix = t.Kind switch
        {
            TokenKind.Identifier => t.Image,
            TokenKind.EscapedIdentifier => t.Image[1..],
            _ when CompactLexer.IsKeyword(t.Kind) => t.Image,
            _ => throw new ParseException(t),
        };
        Consume(t.Kind);
        return LookupPrefix(prefix, t);
    }

    private LocatedString UnprefixedName()
    {
        if (CompactLexer.IsKeyword(Kind(1)))
        {
            CompactToken t = Consume(Kind(1));
            return new LocatedString(t.Image, t);
        }

        return Identifier();
    }

    private void Params(IDataPatternBuilder dpb)
    {
        Consume(TokenKind.LeftBrace);
        while (IsAnnotationsStart(Kind(1)) || IsUnprefixedName(Kind(1)))
        {
            AnnotationsImpl? a = Annotations();
            LocatedString name = UnprefixedName();
            Consume(TokenKind.Equals);
            string value = Literal();
            dpb.AddParam(name.Value, value, this, _defaultNamespace, Location(name.Token), a);
        }

        Consume(TokenKind.RightBrace);
    }

    private Pattern Except(IScope scope, CompactToken?[] except)
    {
        CompactToken t = Consume(TokenKind.Minus);
        AnnotationsImpl? a = Annotations();
        CompactToken?[] innerExcept = new CompactToken?[1];
        Pattern p = PrimaryExpr(false, scope, a, innerExcept);
        CheckExcept(innerExcept);
        except[0] = t;
        return p;
    }

    private void Documentation()
    {
        Consume(Kind(1));
        while (Kind(1) == TokenKind.DocumentationContinue)
        {
            Consume(TokenKind.DocumentationContinue);
        }
    }

    // AnnotationsImpl(): documentation comments, then "[" attributes and elements "]". Only a:defaultValue is kept.
    private AnnotationsImpl? Annotations()
    {
        AnnotationsImpl? a = null;
        if (Kind(1) is TokenKind.Documentation or TokenKind.DocumentationAfterSingleLineComment)
        {
            a = new AnnotationsImpl();
            while (Kind(1) is TokenKind.Documentation or TokenKind.DocumentationAfterSingleLineComment)
            {
                Documentation();
            }
        }

        if (Kind(1) == TokenKind.LeftBracket)
        {
            Consume(TokenKind.LeftBracket);
            a ??= new AnnotationsImpl();
            _attributeNames.Clear();
            _annotationsIncludeElements = false;
            while (Kind(1) == TokenKind.PrefixedName && Kind(2) == TokenKind.Equals)
            {
                PrefixedAnnotationAttribute(a, false);
            }

            while (IsAnnotationElementStart(Kind(1)))
            {
                AnnotationElement(false);
                _annotationsIncludeElements = true;
            }

            Consume(TokenKind.RightBracket);
        }

        return a;
    }

    private void AnnotationAttribute(AnnotationsImpl a)
    {
        if (Kind(1) == TokenKind.PrefixedName)
        {
            PrefixedAnnotationAttribute(a, true);
            return;
        }

        LocatedString name = UnprefixedName();
        Consume(TokenKind.Equals);
        string value = Literal();
        if (name.Value == XmlnsName)
        {
            Error("xmlns_annotation_attribute", name.Token);
        }
        else
        {
            AddAttribute(a, string.Empty, name.Value, value, name.Token);
        }
    }

    private void PrefixedAnnotationAttribute(AnnotationsImpl a, bool nested)
    {
        CompactToken t = Consume(TokenKind.PrefixedName);
        Consume(TokenKind.Equals);
        string value = Literal();
        (string prefix, string localName) = SplitQName(t.Image);
        string ns = LookupPrefix(prefix, t);
        if (ReferenceEquals(ns, GrammarSection.InheritNs))
        {
            Error("inherited_annotation_namespace", t);
        }
        else if (ns.Length == 0 && !nested)
        {
            Error("unqualified_annotation_attribute", t);
        }
        else if (ns == WellKnownNamespaces.RelaxNg && !nested)
        {
            Error("relax_ng_namespace", t);
        }
        else if (ns == WellKnownNamespaces.Xmlns)
        {
            Error("xmlns_annotation_attribute_uri", t);
        }
        else
        {
            AddAttribute(a, ns, localName, value, t);
        }
    }

    private void AnnotationElement(bool nested)
    {
        if (Kind(1) == TokenKind.PrefixedName)
        {
            PrefixedAnnotationElement(nested);
            return;
        }

        UnprefixedName();
        AnnotationElementContent();
    }

    private void AnnotationElementNotKeyword()
    {
        if (Kind(1) == TokenKind.PrefixedName)
        {
            PrefixedAnnotationElement(false);
            return;
        }

        Identifier();
        AnnotationElementContent();
    }

    private void PrefixedAnnotationElement(bool nested)
    {
        CompactToken t = Consume(TokenKind.PrefixedName);
        string ns = LookupPrefix(SplitQName(t.Image).Prefix, t);
        if (ReferenceEquals(ns, GrammarSection.InheritNs))
        {
            Error("inherited_annotation_namespace", t);
        }
        else if (!nested && ns == WellKnownNamespaces.RelaxNg)
        {
            Error("relax_ng_namespace", t);
        }

        AnnotationElementContent();
    }

    // The attributes of an annotation element go nowhere (Jing's builder keeps nothing of it), but are checked.
    private void AnnotationElementContent()
    {
        AnnotationsImpl ignored = new();
        Consume(TokenKind.LeftBracket);
        _attributeNames.Clear();
        while (AnnotationAttributeAhead())
        {
            AnnotationAttribute(ignored);
        }

        while (true)
        {
            if (Kind(1) == TokenKind.Literal)
            {
                Consume(TokenKind.Literal);
                while (Kind(1) == TokenKind.Tilde)
                {
                    Consume(TokenKind.Tilde);
                    Consume(TokenKind.Literal);
                }
            }
            else if (IsAnnotationElementStart(Kind(1)))
            {
                AnnotationElement(true);
            }
            else
            {
                break;
            }
        }

        Consume(TokenKind.RightBracket);
    }

    private string Literal()
    {
        string s = Unquote(Consume(TokenKind.Literal).Image);
        if (Kind(1) != TokenKind.Tilde)
        {
            return s;
        }

        StringBuilder buf = new(s);
        while (Kind(1) == TokenKind.Tilde)
        {
            Consume(TokenKind.Tilde);
            buf.Append(Unquote(Consume(TokenKind.Literal).Image));
        }

        return buf.ToString();
    }

    private static string Unquote(string s)
    {
        if (s.Length >= TripleQuotedMinLength && s[0] == s[1])
        {
            s = s.Replace(CompactCharStream.NewlineMarker, '\n');
            return s[TripleQuoteLength..^TripleQuoteLength];
        }

        return s[1..^1];
    }

    private void AddAttribute(AnnotationsImpl a, string ns, string localName, string value, CompactToken tok)
    {
        if (!_attributeNames.Add(ns + "#" + localName))
        {
            Error("duplicate_attribute", tok, ns, localName);
        }
        else
        {
            a.AddAttribute(ns, localName, value);
        }
    }

    private void CheckExcept(CompactToken?[] except)
    {
        if (except[0] is { } t)
        {
            Error("except_missing_parentheses", t);
        }
    }

    private void CheckNsName(int context, LocatedString ns)
    {
        if ((context & InNsName) != 0)
        {
            Error("ns_name_except_contains_ns_name", ns.Token);
        }
    }

    private void CheckAnyName(int context, CompactToken t)
    {
        if ((context & InNsName) != 0)
        {
            Error("ns_name_except_contains_any_name", t);
        }

        if ((context & InAnyName) != 0)
        {
            Error("any_name_except_contains_any_name", t);
        }
    }

    private string LookupPrefix(string prefix, CompactToken t)
    {
        if (_namespaceMap.TryGetValue(prefix, out string? ns))
        {
            return ns;
        }

        Error("undeclared_prefix", t, prefix);
        return ErrorNamespace;
    }

    private string LookupDatatype(string prefix, CompactToken t)
    {
        if (_datatypesMap.TryGetValue(prefix, out string? ns))
        {
            return ns;
        }

        Error("undeclared_prefix", t, prefix);
        return string.Empty;
    }

    private bool AnnotationElementNotKeywordAhead() =>
        Kind(1) is TokenKind.PrefixedName or TokenKind.Identifier or TokenKind.EscapedIdentifier && Kind(2) == TokenKind.LeftBracket;

    private bool AnnotationAttributeAhead() =>
        (Kind(1) == TokenKind.PrefixedName || IsUnprefixedName(Kind(1))) && Kind(2) == TokenKind.Equals;

    private static bool IsAnnotationElementStart(TokenKind kind) => kind == TokenKind.PrefixedName || IsUnprefixedName(kind);

    private static bool IsAnnotationsStart(TokenKind kind) =>
        kind is TokenKind.LeftBracket or TokenKind.Documentation or TokenKind.DocumentationAfterSingleLineComment;

    private static bool IsIdentifier(TokenKind kind) => kind is TokenKind.Identifier or TokenKind.EscapedIdentifier;

    private static bool IsUnprefixedName(TokenKind kind) => IsIdentifier(kind) || CompactLexer.IsKeyword(kind);

    private static bool IsGrammarKeyword(TokenKind kind) => kind is TokenKind.Start or TokenKind.Div or TokenKind.Include;

    private static (string Prefix, string LocalName) SplitQName(string qName)
    {
        int colon = qName.IndexOf(PrefixSeparator, StringComparison.Ordinal);
        return (qName[..colon], qName[(colon + 1)..]);
    }

    private CompactToken Token(int k) => _lexer.Get(_next + k - 1);

    private TokenKind Kind(int k) => Token(k).Kind;

    private CompactToken Consume(TokenKind kind)
    {
        CompactToken t = Token(1);
        if (t.Kind != kind)
        {
            throw new ParseException(t);
        }

        _next++;
        return t;
    }

    private ILocator Location(CompactToken t) => _sb.MakeLocation(_sourceUri, t.BeginLine, t.BeginColumn);

    private void Error(string key, CompactToken tok, params string?[] args)
    {
        _hadError = true;
        Report(_localizer.Message(key, args), tok.BeginLine, tok.BeginColumn);
    }

    private void Report(string message, int line, int column)
    {
        _hadError = true;
        _eh?.OnError(new SaxParseException(message, new FixedLocator(_sourceUri, line, column)));
    }

    private sealed record LocatedString(string Value, CompactToken Token);

    /// <summary>The parse cannot go on at <see cref="Token"/> (JavaCC's <c>ParseException</c>).</summary>
    private sealed class ParseException(CompactToken token) : Exception
    {
        public CompactToken Token { get; } = token;
    }
}
