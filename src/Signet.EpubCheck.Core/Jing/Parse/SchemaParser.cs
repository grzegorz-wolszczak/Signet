using System;
using System.Collections.Generic;
using System.Text;
using Signet.EpubCheck.Core.Jing.Patterns;
using Signet.EpubCheck.Core.Jing.Sax;
using Signet.EpubCheck.Core.Util;

namespace Signet.EpubCheck.Core.Jing.Parse;

/// <summary>
/// Reads a schema in the XML syntax of RELAX NG (Jing's <c>SchemaParser</c>): one state per RELAX NG element, each the
/// content handler while its element is open; the states hand the patterns and name classes they make to their parent.
/// </summary>
internal sealed class SchemaParser
{
    private const string RelaxNgUriPrefix = "http://relaxng.org/ns/structure/";
    private const string NameAttribute = "name";
    private const string NsAttribute = "ns";
    private const string DatatypeLibraryAttribute = "datatypeLibrary";
    private const string TypeAttribute = "type";
    private const string HrefAttribute = "href";
    private const string CombineAttribute = "combine";
    private const string BaseAttribute = "base";
    private const char PrefixSeparator = ':';

    private readonly XmlEventReader _xr;
    private readonly ISaxErrorHandler? _eh;
    private readonly ISchemaBuilder _schemaBuilder;
    private readonly JingLocalizer _localizer;
    private readonly XmlBaseHandler _xmlBaseHandler = new();
    private readonly ContextImpl _context;
    private readonly Dictionary<string, Func<State>> _patternMap;
    private readonly Dictionary<string, Func<State>> _nameClassMap;
    private string _relaxngUri = WellKnownNamespaces.RelaxNg;
    private Pattern? _startPattern;
    private ILocator? _locator;
    private bool _hadError;

    public SchemaParser(XmlEventReader xr, ISaxErrorHandler? eh, ISchemaBuilder schemaBuilder, IIncludedGrammar? grammar, IScope scope,
        JingLocalizer localizer)
    {
        _xr = xr;
        _eh = eh;
        _schemaBuilder = schemaBuilder;
        _localizer = localizer;
        _context = new ContextImpl(this);
        if (eh is not null)
        {
            xr.ErrorHandler = eh;
        }

        xr.DtdHandler = _context;
        _patternMap = new Dictionary<string, Func<State>>(StringComparer.Ordinal)
        {
            ["zeroOrMore"] = () => new ZeroOrMoreState(this),
            ["oneOrMore"] = () => new OneOrMoreState(this),
            ["optional"] = () => new OptionalState(this),
            ["list"] = () => new ListState(this),
            ["choice"] = () => new ChoiceState(this),
            ["interleave"] = () => new InterleaveState(this),
            ["group"] = () => new GroupState(this),
            ["mixed"] = () => new MixedState(this),
            ["element"] = () => new ElementState(this),
            ["attribute"] = () => new AttributeState(this),
            ["empty"] = () => new EmptyState(this),
            ["text"] = () => new TextState(this),
            ["value"] = () => new ValueState(this),
            ["data"] = () => new DataState(this),
            ["notAllowed"] = () => new NotAllowedState(this),
            ["grammar"] = () => new GrammarState(this),
            ["ref"] = () => new RefState(this),
            ["parentRef"] = () => new ParentRefState(this),
            ["externalRef"] = () => new ExternalRefState(this),
        };
        _nameClassMap = new Dictionary<string, Func<State>>(StringComparer.Ordinal)
        {
            ["name"] = () => new NameState(this),
            ["anyName"] = () => new AnyNameState(this),
            ["nsName"] = () => new NsNameState(this),
            ["choice"] = () => new NameClassChoiceState(this),
        };
        new RootState(this, grammar, scope, GrammarSection.InheritNs).Set();
    }

    // The context in which the name classes of an except are read.
    private enum NameClassContext
    {
        Pattern = 0,
        AnyName = 1,
        NsName = 2,
    }

    /// <summary>The pattern read (<c>null</c> for an included grammar: its definitions went into the including one).</summary>
    /// <exception cref="IllegalSchemaException">The schema has errors.</exception>
    public Pattern? GetParsedPattern()
    {
        if (_hadError)
        {
            throw new IllegalSchemaException();
        }

        return _startPattern;
    }

    private void Error(string key, params string?[] args) => Error(key, _locator, args);

    private void Error(string key, ILocator? loc, params string?[] args)
    {
        _hadError = true;
        _eh?.OnError(new SaxParseException(_localizer.Message(key, args), loc));
    }

    private void Warning(string key, params string?[] args) =>
        _eh?.OnWarning(new SaxParseException(_localizer.Message(key, args), _locator));

    private NameClass ExpandName(string name, string ns, AnnotationsImpl? anno)
    {
        int ic = name.IndexOf(PrefixSeparator, StringComparison.Ordinal);
        if (ic == -1)
        {
            return _schemaBuilder.MakeName(ns, CheckNcName(name), null, null, anno);
        }

        string prefix = CheckNcName(name[..ic]);
        string localName = CheckNcName(name[(ic + 1)..]);
        for (PrefixMapping? tem = _context.PrefixMapping; tem is not null; tem = tem.Next)
        {
            if (string.Equals(tem.Prefix, prefix, StringComparison.Ordinal))
            {
                return _schemaBuilder.MakeName(tem.Uri, localName, prefix, null, anno);
            }
        }

        Error("undefined_prefix", prefix);
        return _schemaBuilder.MakeName(string.Empty, localName, null, null, anno);
    }

    private string CheckNcName(string str)
    {
        if (!Naming.IsNcname(str))
        {
            Error("invalid_ncname", str);
        }

        return str;
    }

    private ILocator? MakeLocation() =>
        _locator is null ? null : _schemaBuilder.MakeLocation(_locator.SystemId, _locator.LineNumber, _locator.ColumnNumber);

    private void CheckUriNoFragmentId(string s)
    {
        CheckUri(s);
        if (JingUri.HasFragmentId(s))
        {
            Error("href_fragment_id");
        }
    }

    private void CheckUri(string s)
    {
        if (!JingUri.IsValid(s))
        {
            Error("invalid_uri", s);
        }
    }

    private sealed record PrefixMapping(string Prefix, string Uri, PrefixMapping? Next);

    // The namespaces in scope and the DTD of the schema document.
    private class AbstractContext : DtdContext, IParseContext
    {
        protected AbstractContext()
        {
            PrefixMapping = new PrefixMapping("xml", WellKnownNamespaces.Xml, null);
        }

        protected AbstractContext(AbstractContext context)
            : base(context)
        {
            PrefixMapping = context.PrefixMapping;
        }

        public PrefixMapping? PrefixMapping { get; set; }

        public virtual string? BaseUri => null;

        public string? ResolveNamespacePrefix(string prefix)
        {
            for (PrefixMapping? p = PrefixMapping; p is not null; p = p.Next)
            {
                if (string.Equals(p.Prefix, prefix, StringComparison.Ordinal))
                {
                    return p.Uri;
                }
            }

            return null;
        }

        public ISet<string> Prefixes()
        {
            HashSet<string> set = new(StringComparer.Ordinal);
            for (PrefixMapping? p = PrefixMapping; p is not null; p = p.Next)
            {
                set.Add(p.Prefix);
            }

            return set;
        }

        public IParseContext Copy() => new SavedContext(this);
    }

    private sealed class SavedContext(AbstractContext context) : AbstractContext(context)
    {
        private readonly string? _baseUri = context.BaseUri;

        public override string? BaseUri => _baseUri;
    }

    private sealed class ContextImpl(SchemaParser parser) : AbstractContext
    {
        public override string? BaseUri => parser._xmlBaseHandler.GetBaseUri();
    }

    // A content handler of the parser: the namespace declarations go to the context.
    private abstract class Handler(SchemaParser parser) : IContentHandler
    {
        protected SchemaParser Parser { get; } = parser;

        public void SetDocumentLocator(ILocator locator)
        {
            Parser._locator = locator;
            Parser._xmlBaseHandler.SetLocator(locator);
        }

        public virtual void StartDocument()
        {
        }

        public virtual void EndDocument()
        {
        }

        public void StartPrefixMapping(string prefix, string uri) =>
            Parser._context.PrefixMapping = new PrefixMapping(prefix, uri, Parser._context.PrefixMapping);

        public void EndPrefixMapping(string prefix) => Parser._context.PrefixMapping = Parser._context.PrefixMapping!.Next;

        public abstract void StartElement(string namespaceUri, string localName, string qName, IAttributes attributes);

        public abstract void EndElement(string namespaceUri, string localName, string qName);

        public abstract void Characters(string text);

        public void ProcessingInstruction(string target, string data)
        {
        }
    }

    // The state of an open RELAX NG element.
    private abstract class State(SchemaParser parser) : Handler(parser)
    {
        public State? Parent { get; private set; }

        public string? NsInherit { get; set; }

        public string? Ns { get; set; }

        public string? DatatypeLibrary { get; set; }

        public IScope? Scope { get; set; }

        public ILocator? StartLocation { get; private set; }

        public AnnotationsImpl? Annotations { get; set; }

        public void Set() => Parser._xr.ContentHandler = this;

        public abstract State? CreateChildState(string localName);

        public virtual RootState? ToRootState() => null;

        public virtual NameClassChoiceState? ToNameClassChoiceState() => null;

        public virtual void SetParent(State parent)
        {
            Parent = parent;
            NsInherit = parent.GetNs();
            DatatypeLibrary = parent.DatatypeLibrary;
            Scope = parent.Scope;
            StartLocation = Parser.MakeLocation();
        }

        public string GetNs() => Ns ?? NsInherit!;

        public virtual bool IsRelaxNgElement(string uri) => string.Equals(uri, Parser._relaxngUri, StringComparison.Ordinal);

        public override void StartElement(string namespaceUri, string localName, string qName, IAttributes attributes)
        {
            Parser._xmlBaseHandler.StartElement();
            if (IsRelaxNgElement(namespaceUri))
            {
                State? state = CreateChildState(localName);
                if (state is null)
                {
                    Parser._xr.ContentHandler = new Skipper(Parser, this);
                    return;
                }

                state.SetParent(this);
                state.Set();
                state.Attributes(attributes);
            }
            else
            {
                CheckForeignElement();
                ForeignElementHandler feh = new(Parser, this);
                feh.StartElement(namespaceUri, localName, qName, attributes);
                Parser._xr.ContentHandler = feh;
            }
        }

        public override void EndElement(string namespaceUri, string localName, string qName)
        {
            Parser._xmlBaseHandler.EndElement();
            Parent!.Set();
            End();
        }

        public virtual void SetName(string name) => Parser.Error("illegal_name_attribute");

        public virtual void SetOtherAttribute(string name, string value) => Parser.Error("illegal_attribute_ignored", name);

        public virtual void EndAttributes()
        {
        }

        public virtual void CheckForeignElement()
        {
        }

        public void Attributes(IAttributes atts)
        {
            for (int i = 0; i < atts.Length; i++)
            {
                string uri = atts.GetUri(i);
                if (uri.Length == 0)
                {
                    string name = atts.GetLocalName(i);
                    switch (name)
                    {
                        case NameAttribute:
                            SetName(JavaStrings.Trim(atts.GetValue(i)));
                            break;
                        case NsAttribute:
                            Ns = atts.GetValue(i);
                            break;
                        case DatatypeLibraryAttribute:
                            string datatypeLibrary = atts.GetValue(i);
                            Parser.CheckUri(datatypeLibrary);
                            if (datatypeLibrary.Length != 0 && !JingUri.IsAbsolute(datatypeLibrary))
                            {
                                Parser.Error("relative_datatype_library");
                            }

                            if (JingUri.HasFragmentId(datatypeLibrary))
                            {
                                Parser.Error("fragment_identifier_datatype_library");
                            }

                            DatatypeLibrary = JingUri.EscapeDisallowedChars(datatypeLibrary);
                            break;
                        default:
                            SetOtherAttribute(name, atts.GetValue(i));
                            break;
                    }
                }
                else if (string.Equals(uri, Parser._relaxngUri, StringComparison.Ordinal))
                {
                    Parser.Error("qualified_attribute", atts.GetLocalName(i));
                }
                else if (string.Equals(uri, WellKnownNamespaces.Xml, StringComparison.Ordinal)
                    && string.Equals(atts.GetLocalName(i), BaseAttribute, StringComparison.Ordinal))
                {
                    Parser._xmlBaseHandler.XmlBaseAttribute(atts.GetValue(i));
                }
                else
                {
                    Annotations ??= new AnnotationsImpl();
                    Annotations.AddAttribute(uri, atts.GetLocalName(i), atts.GetValue(i));
                }
            }

            EndAttributes();
        }

        public abstract void End();

        public virtual void EndPatternChild(Pattern pattern)
        {
            // Cannot happen.
        }

        public virtual void EndNameClassChild(NameClass nc)
        {
            // Cannot happen.
        }

        public override void Characters(string text)
        {
            foreach (char c in text)
            {
                if (c is not (' ' or '\r' or '\n' or '\t'))
                {
                    Parser.Error("illegal_characters_ignored");
                    break;
                }
            }
        }
    }

    // A foreign element and its content: skipped (Jing's builder does not keep annotations).
    private sealed class ForeignElementHandler(SchemaParser parser, State nextState) : Handler(parser)
    {
        private int _level;

        public override void StartElement(string namespaceUri, string localName, string qName, IAttributes attributes) => _level++;

        public override void EndElement(string namespaceUri, string localName, string qName)
        {
            if (--_level == 0)
            {
                nextState.Set();
            }
        }

        public override void Characters(string text)
        {
        }
    }

    // The content of a RELAX NG element that was not expected: skipped.
    private sealed class Skipper(SchemaParser parser, State nextState) : Handler(parser)
    {
        private int _level = 1;

        public override void StartElement(string namespaceUri, string localName, string qName, IAttributes attributes) => ++_level;

        public override void EndElement(string namespaceUri, string localName, string qName)
        {
            if (--_level == 0)
            {
                nextState.Set();
            }
        }

        public override void Characters(string text)
        {
        }
    }

    private abstract class EmptyContentState(SchemaParser parser) : State(parser)
    {
        public override State? CreateChildState(string localName)
        {
            Parser.Error("expected_empty", localName);
            return null;
        }

        public abstract Pattern MakePattern();

        public override void End() => Parent!.EndPatternChild(MakePattern());
    }

    private abstract class PatternContainerState(SchemaParser parser) : State(parser)
    {
        protected List<Pattern> ChildPatterns { get; } = [];

        public override State? CreateChildState(string localName)
        {
            if (!Parser._patternMap.TryGetValue(localName, out Func<State>? create))
            {
                Parser.Error("expected_pattern", localName);
                return null;
            }

            return create();
        }

        public virtual Pattern BuildPattern(List<Pattern> patterns, ILocator? loc, AnnotationsImpl? anno) =>
            patterns.Count == 1 && anno is null ? patterns[0] : Parser._schemaBuilder.MakeGroup(patterns, loc, anno);

        public override void EndPatternChild(Pattern pattern) => ChildPatterns.Add(pattern);

        public override void End()
        {
            if (ChildPatterns.Count == 0)
            {
                Parser.Error("missing_children");
                EndPatternChild(Parser._schemaBuilder.MakeErrorPattern());
            }

            SendPatternToParent(BuildPattern(ChildPatterns, StartLocation, Annotations));
        }

        protected virtual void SendPatternToParent(Pattern p) => Parent!.EndPatternChild(p);
    }

    private sealed class GroupState(SchemaParser parser) : PatternContainerState(parser);

    private sealed class ZeroOrMoreState(SchemaParser parser) : PatternContainerState(parser)
    {
        public override Pattern BuildPattern(List<Pattern> patterns, ILocator? loc, AnnotationsImpl? anno) =>
            Parser._schemaBuilder.MakeZeroOrMore(base.BuildPattern(patterns, loc, null), loc, anno);
    }

    private sealed class OneOrMoreState(SchemaParser parser) : PatternContainerState(parser)
    {
        public override Pattern BuildPattern(List<Pattern> patterns, ILocator? loc, AnnotationsImpl? anno) =>
            Parser._schemaBuilder.MakeOneOrMore(base.BuildPattern(patterns, loc, null), loc, anno);
    }

    private sealed class OptionalState(SchemaParser parser) : PatternContainerState(parser)
    {
        public override Pattern BuildPattern(List<Pattern> patterns, ILocator? loc, AnnotationsImpl? anno) =>
            Parser._schemaBuilder.MakeOptional(base.BuildPattern(patterns, loc, null), loc, anno);
    }

    private sealed class ListState(SchemaParser parser) : PatternContainerState(parser)
    {
        public override Pattern BuildPattern(List<Pattern> patterns, ILocator? loc, AnnotationsImpl? anno) =>
            Parser._schemaBuilder.MakeList(base.BuildPattern(patterns, loc, null), loc, anno);
    }

    private sealed class ChoiceState(SchemaParser parser) : PatternContainerState(parser)
    {
        public override Pattern BuildPattern(List<Pattern> patterns, ILocator? loc, AnnotationsImpl? anno) =>
            Parser._schemaBuilder.MakeChoice(patterns, loc, anno);
    }

    private sealed class InterleaveState(SchemaParser parser) : PatternContainerState(parser)
    {
        public override Pattern BuildPattern(List<Pattern> patterns, ILocator? loc, AnnotationsImpl? anno) =>
            Parser._schemaBuilder.MakeInterleave(patterns, loc, anno);
    }

    private sealed class MixedState(SchemaParser parser) : PatternContainerState(parser)
    {
        public override Pattern BuildPattern(List<Pattern> patterns, ILocator? loc, AnnotationsImpl? anno) =>
            Parser._schemaBuilder.MakeMixed(base.BuildPattern(patterns, loc, null), loc, anno);
    }

    private interface INameClassRef
    {
        void SetNameClass(NameClass nc);
    }

    private sealed class ElementState(SchemaParser parser) : PatternContainerState(parser), INameClassRef
    {
        private NameClass? _nameClass;
        private string? _name;

        public override void SetName(string name) => _name = name;

        public void SetNameClass(NameClass nc) => _nameClass = nc;

        public override void EndAttributes()
        {
            if (_name is not null)
            {
                _nameClass = Parser.ExpandName(_name, GetNs(), null);
            }
            else
            {
                new NameClassChildState(Parser, this, this).Set();
            }
        }

        public override Pattern BuildPattern(List<Pattern> patterns, ILocator? loc, AnnotationsImpl? anno) =>
            Parser._schemaBuilder.MakeElement(_nameClass!, base.BuildPattern(patterns, loc, null), loc, anno);
    }

    private sealed class RootState : PatternContainerState
    {
        private readonly IIncludedGrammar? _grammar;

        public RootState(SchemaParser parser, IIncludedGrammar? grammar, IScope scope, string ns)
            : base(parser)
        {
            _grammar = grammar;
            Scope = scope;
            NsInherit = ns;
            DatatypeLibrary = string.Empty;
        }

        public override RootState ToRootState() => this;

        public override State? CreateChildState(string localName)
        {
            if (_grammar is null)
            {
                return base.CreateChildState(localName);
            }

            if (string.Equals(localName, "grammar", StringComparison.Ordinal))
            {
                return new MergeGrammarState(Parser, _grammar);
            }

            Parser.Error("expected_grammar", localName);
            return null;
        }

        public override void CheckForeignElement() => Parser.Error("root_bad_namespace_uri", WellKnownNamespaces.RelaxNg);

        public override void EndPatternChild(Pattern pattern) => Parser._startPattern = pattern;

        public override bool IsRelaxNgElement(string uri)
        {
            if (!uri.StartsWith(RelaxNgUriPrefix, StringComparison.Ordinal))
            {
                return false;
            }

            if (!string.Equals(uri, WellKnownNamespaces.RelaxNg, StringComparison.Ordinal))
            {
                Parser.Warning("wrong_uri_version", WellKnownNamespaces.RelaxNg[RelaxNgUriPrefix.Length..], uri[RelaxNgUriPrefix.Length..]);
            }

            Parser._relaxngUri = uri;
            return true;
        }
    }

    private sealed class NotAllowedState(SchemaParser parser) : EmptyContentState(parser)
    {
        public override Pattern MakePattern() => Parser._schemaBuilder.MakeNotAllowed(StartLocation, Annotations);
    }

    private sealed class EmptyState(SchemaParser parser) : EmptyContentState(parser)
    {
        public override Pattern MakePattern() => Parser._schemaBuilder.MakeEmpty(StartLocation, Annotations);
    }

    private sealed class TextState(SchemaParser parser) : EmptyContentState(parser)
    {
        public override Pattern MakePattern() => Parser._schemaBuilder.MakeText(StartLocation, Annotations);
    }

    private sealed class ValueState(SchemaParser parser) : EmptyContentState(parser)
    {
        private const string DefaultType = "token";
        private readonly StringBuilder _buf = new();
        private string? _type;

        public override void SetOtherAttribute(string name, string value)
        {
            if (string.Equals(name, TypeAttribute, StringComparison.Ordinal))
            {
                _type = Parser.CheckNcName(JavaStrings.Trim(value));
            }
            else
            {
                base.SetOtherAttribute(name, value);
            }
        }

        public override void Characters(string text) => _buf.Append(text);

        public override void CheckForeignElement() => Parser.Error("value_contains_foreign_element");

        public override Pattern MakePattern() =>
            _type is null ? MakePattern(string.Empty, DefaultType) : MakePattern(DatatypeLibrary!, _type);

        private Pattern MakePattern(string datatypeLibrary, string type) =>
            Parser._schemaBuilder.MakeValue(datatypeLibrary, type, _buf.ToString(), Parser._context, GetNs(), StartLocation, Annotations);
    }

    private sealed class DataState(SchemaParser parser) : State(parser)
    {
        private string? _type;
        private Pattern? _except;
        private IDataPatternBuilder? _dpb;

        public override State? CreateChildState(string localName)
        {
            if (string.Equals(localName, "param", StringComparison.Ordinal))
            {
                if (_except is not null)
                {
                    Parser.Error("param_after_except");
                }

                return new ParamState(Parser, _dpb);
            }

            if (string.Equals(localName, "except", StringComparison.Ordinal))
            {
                if (_except is not null)
                {
                    Parser.Error("multiple_except");
                }

                return new ChoiceState(Parser);
            }

            Parser.Error("expected_param_except", localName);
            return null;
        }

        public override void SetOtherAttribute(string name, string value)
        {
            if (string.Equals(name, TypeAttribute, StringComparison.Ordinal))
            {
                _type = Parser.CheckNcName(JavaStrings.Trim(value));
            }
            else
            {
                base.SetOtherAttribute(name, value);
            }
        }

        public override void EndAttributes()
        {
            if (_type is null)
            {
                Parser.Error("missing_type_attribute");
            }
            else
            {
                _dpb = Parser._schemaBuilder.MakeDataPatternBuilder(DatatypeLibrary!, _type, StartLocation);
            }
        }

        public override void End()
        {
            Pattern p;
            if (_dpb is not null)
            {
                p = _except is not null ? _dpb.MakePattern(_except, StartLocation, Annotations) : _dpb.MakePattern(StartLocation, Annotations);
            }
            else
            {
                p = Parser._schemaBuilder.MakeErrorPattern();
            }

            Parent!.EndPatternChild(p);
        }

        public override void EndPatternChild(Pattern pattern) => _except = pattern;
    }

    private sealed class ParamState(SchemaParser parser, IDataPatternBuilder? dpb) : State(parser)
    {
        private readonly StringBuilder _buf = new();
        private string? _name;

        public override void SetName(string name) => _name = Parser.CheckNcName(name);

        public override void EndAttributes()
        {
            if (_name is null)
            {
                Parser.Error("missing_name_attribute");
            }
        }

        public override State? CreateChildState(string localName)
        {
            Parser.Error("expected_empty", localName);
            return null;
        }

        public override void Characters(string text) => _buf.Append(text);

        public override void CheckForeignElement() => Parser.Error("param_contains_foreign_element");

        public override void End()
        {
            if (_name is null || dpb is null)
            {
                return;
            }

            dpb.AddParam(_name, _buf.ToString(), Parser._context, GetNs(), StartLocation, Annotations);
        }
    }

    private sealed class AttributeState(SchemaParser parser) : PatternContainerState(parser), INameClassRef
    {
        private NameClass? _nameClass;
        private string? _name;

        public override void SetName(string name) => _name = name;

        public void SetNameClass(NameClass nc) => _nameClass = nc;

        public override void EndAttributes()
        {
            if (_name is not null)
            {
                _nameClass = Parser.ExpandName(_name, Ns ?? string.Empty, null);
            }
            else
            {
                new NameClassChildState(Parser, this, this).Set();
            }
        }

        public override void End()
        {
            if (ChildPatterns.Count == 0)
            {
                EndPatternChild(Parser._schemaBuilder.MakeText(StartLocation, null));
            }

            base.End();
        }

        public override Pattern BuildPattern(List<Pattern> patterns, ILocator? loc, AnnotationsImpl? anno) =>
            Parser._schemaBuilder.MakeAttribute(_nameClass!, base.BuildPattern(patterns, loc, null), loc, anno);

        public override State? CreateChildState(string localName)
        {
            State? tem = base.CreateChildState(localName);
            if (tem is not null && ChildPatterns.Count != 0)
            {
                Parser.Error("attribute_multi_pattern");
            }

            return tem;
        }
    }

    private class GrammarSectionState(SchemaParser parser, IGrammarSection? section) : State(parser)
    {
        protected IGrammarSection? Section { get; set; } = section;

        public override State? CreateChildState(string localName)
        {
            switch (localName)
            {
                case "define":
                    return new DefineState(Parser, Section!);
                case "start":
                    return new StartState(Parser, Section!);
                case "include":
                    IInclude? include = Section!.MakeInclude();
                    if (include is not null)
                    {
                        return new IncludeState(Parser, include);
                    }

                    break;
                case "div":
                    return new DivState(Parser, Section!.MakeDiv());
            }

            Parser.Error("expected_define", localName);
            return null;
        }

        public override void End()
        {
        }
    }

    private sealed class DivState(SchemaParser parser, IDiv div) : GrammarSectionState(parser, div)
    {
        public override void End()
        {
            base.End();
            div.EndDiv(StartLocation, Annotations);
        }
    }

    private sealed class IncludeState(SchemaParser parser, IInclude include) : GrammarSectionState(parser, include)
    {
        private string? _href;
        private string? _base;

        public override void SetOtherAttribute(string name, string value)
        {
            if (string.Equals(name, HrefAttribute, StringComparison.Ordinal))
            {
                _href = value;
                Parser.CheckUriNoFragmentId(value);
            }
            else
            {
                base.SetOtherAttribute(name, value);
            }
        }

        public override void EndAttributes()
        {
            if (_href is null)
            {
                Parser.Error("missing_href_attribute");
            }
            else
            {
                _base = Parser._xmlBaseHandler.GetBaseUri();
            }
        }

        public override void End()
        {
            base.End();
            if (_href is not null)
            {
                try
                {
                    include.EndInclude(_href, _base, GetNs(), StartLocation, Annotations);
                }
                catch (IllegalSchemaException)
                {
                    // Reported already.
                }
            }
        }
    }

    private sealed class MergeGrammarState(SchemaParser parser, IIncludedGrammar grammar) : GrammarSectionState(parser, grammar)
    {
        public override void End()
        {
            base.End();
            Parent!.EndPatternChild(grammar.EndIncludedGrammar(StartLocation, Annotations)!);
        }
    }

    private sealed class GrammarState(SchemaParser parser) : GrammarSectionState(parser, null)
    {
        private IGrammar? _grammar;

        public override void SetParent(State parent)
        {
            base.SetParent(parent);
            _grammar = Parser._schemaBuilder.MakeGrammar(Scope);
            Section = _grammar;
            Scope = _grammar;
        }

        public override void End()
        {
            base.End();
            Parent!.EndPatternChild(_grammar!.EndGrammar(StartLocation, Annotations));
        }
    }

    private class RefState(SchemaParser parser) : EmptyContentState(parser)
    {
        protected string? Name { get; private set; }

        public override void EndAttributes()
        {
            if (Name is null)
            {
                Parser.Error("missing_name_attribute");
            }
        }

        public override void SetName(string name) => Name = Parser.CheckNcName(name);

        public override Pattern MakePattern() =>
            Name is null ? Parser._schemaBuilder.MakeErrorPattern() : Scope!.MakeRef(Name, StartLocation, Annotations);
    }

    private sealed class ParentRefState(SchemaParser parser) : RefState(parser)
    {
        public override Pattern MakePattern() =>
            Name is null ? Parser._schemaBuilder.MakeErrorPattern() : Scope!.MakeParentRef(Name, StartLocation, Annotations);
    }

    private sealed class ExternalRefState(SchemaParser parser) : EmptyContentState(parser)
    {
        private string? _href;
        private string? _base;

        public override void SetOtherAttribute(string name, string value)
        {
            if (string.Equals(name, HrefAttribute, StringComparison.Ordinal))
            {
                _href = value;
                Parser.CheckUriNoFragmentId(value);
            }
            else
            {
                base.SetOtherAttribute(name, value);
            }
        }

        public override void EndAttributes()
        {
            if (_href is null)
            {
                Parser.Error("missing_href_attribute");
            }
            else
            {
                _base = Parser._xmlBaseHandler.GetBaseUri();
            }
        }

        public override Pattern MakePattern()
        {
            if (_href is not null)
            {
                try
                {
                    return Parser._schemaBuilder.MakeExternalRef(_href, _base, GetNs(), Scope!, StartLocation, Annotations);
                }
                catch (IllegalSchemaException)
                {
                    // Reported already.
                }
            }

            return Parser._schemaBuilder.MakeErrorPattern();
        }
    }

    private abstract class DefinitionState(SchemaParser parser, IGrammarSection section) : PatternContainerState(parser)
    {
        protected GrammarCombine? Combine { get; private set; }

        protected IGrammarSection Section { get; } = section;

        public override void SetOtherAttribute(string name, string value)
        {
            if (string.Equals(name, CombineAttribute, StringComparison.Ordinal))
            {
                value = JavaStrings.Trim(value);
                switch (value)
                {
                    case "choice":
                        Combine = GrammarCombine.Choice;
                        break;
                    case "interleave":
                        Combine = GrammarCombine.Interleave;
                        break;
                    default:
                        Parser.Error("combine_attribute_bad_value", value);
                        break;
                }
            }
            else
            {
                base.SetOtherAttribute(name, value);
            }
        }

        public override Pattern BuildPattern(List<Pattern> patterns, ILocator? loc, AnnotationsImpl? anno) =>
            base.BuildPattern(patterns, loc, null);
    }

    private sealed class DefineState(SchemaParser parser, IGrammarSection section) : DefinitionState(parser, section)
    {
        private string? _name;

        public override void SetName(string name) => _name = Parser.CheckNcName(name);

        public override void EndAttributes()
        {
            if (_name is null)
            {
                Parser.Error("missing_name_attribute");
            }
        }

        protected override void SendPatternToParent(Pattern p)
        {
            if (_name is not null)
            {
                Section.Define(_name, Combine, p, StartLocation, Annotations);
            }
        }
    }

    private sealed class StartState(SchemaParser parser, IGrammarSection section) : DefinitionState(parser, section)
    {
        protected override void SendPatternToParent(Pattern p) => Section.Define(GrammarSection.Start, Combine, p, StartLocation, Annotations);

        public override State? CreateChildState(string localName)
        {
            State? tem = base.CreateChildState(localName);
            if (tem is not null && ChildPatterns.Count != 0)
            {
                Parser.Error("start_multi_pattern");
            }

            return tem;
        }
    }

    private abstract class NameClassContainerState(SchemaParser parser) : State(parser)
    {
        public override State? CreateChildState(string localName)
        {
            if (!Parser._nameClassMap.TryGetValue(localName, out Func<State>? create))
            {
                Parser.Error("expected_name_class", localName);
                return null;
            }

            return create();
        }
    }

    // The name class child of an element or attribute without a name attribute.
    private sealed class NameClassChildState : NameClassContainerState
    {
        private readonly State _prevState;
        private readonly INameClassRef _nameClassRef;

        public NameClassChildState(SchemaParser parser, State prevState, INameClassRef nameClassRef)
            : base(parser)
        {
            _prevState = prevState;
            _nameClassRef = nameClassRef;
            SetParent(prevState.Parent!);
            Ns = prevState.Ns;
        }

        public override void EndNameClassChild(NameClass nc)
        {
            _nameClassRef.SetNameClass(nc);
            _prevState.Set();
        }

        public override void End()
        {
            _nameClassRef.SetNameClass(Parser._schemaBuilder.MakeErrorNameClass());
            Parser.Error("missing_name_class");
            _prevState.Set();
            _prevState.End();
        }
    }

    private abstract class NameClassBaseState(SchemaParser parser) : State(parser)
    {
        public abstract NameClass MakeNameClass();

        public override void End() => Parent!.EndNameClassChild(MakeNameClass());
    }

    private sealed class NameState(SchemaParser parser) : NameClassBaseState(parser)
    {
        private readonly StringBuilder _buf = new();

        public override State? CreateChildState(string localName)
        {
            Parser.Error("expected_name", localName);
            return null;
        }

        public override void Characters(string text) => _buf.Append(text);

        public override void CheckForeignElement() => Parser.Error("name_contains_foreign_element");

        public override NameClass MakeNameClass() => Parser.ExpandName(JavaStrings.Trim(_buf.ToString()), GetNs(), Annotations);
    }

    private class AnyNameState(SchemaParser parser) : NameClassBaseState(parser)
    {
        private NameClass? _except;

        public override State? CreateChildState(string localName)
        {
            if (string.Equals(localName, "except", StringComparison.Ordinal))
            {
                if (_except is not null)
                {
                    Parser.Error("multiple_except");
                }

                return new NameClassChoiceState(Parser, Context);
            }

            Parser.Error("expected_except", localName);
            return null;
        }

        public override NameClass MakeNameClass() => _except is null ? MakeNameClassNoExcept() : MakeNameClassExcept(_except);

        public override void EndNameClassChild(NameClass nc) => _except = nc;

        protected virtual NameClassContext Context => NameClassContext.AnyName;

        protected virtual NameClass MakeNameClassNoExcept() => Parser._schemaBuilder.MakeAnyName(StartLocation, Annotations);

        protected virtual NameClass MakeNameClassExcept(NameClass except) => Parser._schemaBuilder.MakeAnyName(except, StartLocation, Annotations);
    }

    private sealed class NsNameState(SchemaParser parser) : AnyNameState(parser)
    {
        protected override NameClassContext Context => NameClassContext.NsName;

        protected override NameClass MakeNameClassNoExcept() => Parser._schemaBuilder.MakeNsName(GetNs(), null, null);

        protected override NameClass MakeNameClassExcept(NameClass except) => Parser._schemaBuilder.MakeNsName(GetNs(), except, null, null);
    }

    private sealed class NameClassChoiceState(SchemaParser parser, NameClassContext context = NameClassContext.Pattern)
        : NameClassContainerState(parser)
    {
        private readonly List<NameClass> _nameClasses = [];
        private NameClassContext _context = context;

        public override NameClassChoiceState ToNameClassChoiceState() => this;

        public override void SetParent(State parent)
        {
            base.SetParent(parent);
            NameClassChoiceState? parentChoice = parent.ToNameClassChoiceState();
            if (parentChoice is not null)
            {
                _context = parentChoice._context;
            }
        }

        public override State? CreateChildState(string localName)
        {
            if (string.Equals(localName, "anyName", StringComparison.Ordinal))
            {
                if (_context >= NameClassContext.AnyName)
                {
                    Parser.Error(_context == NameClassContext.AnyName ? "any_name_except_contains_any_name" : "ns_name_except_contains_any_name");
                    return null;
                }
            }
            else if (string.Equals(localName, "nsName", StringComparison.Ordinal) && _context == NameClassContext.NsName)
            {
                Parser.Error("ns_name_except_contains_ns_name");
                return null;
            }

            return base.CreateChildState(localName);
        }

        public override void EndNameClassChild(NameClass nc) => _nameClasses.Add(nc);

        public override void End()
        {
            if (_nameClasses.Count == 0)
            {
                Parser.Error("missing_name_class");
                Parent!.EndNameClassChild(Parser._schemaBuilder.MakeErrorNameClass());
                return;
            }

            Parent!.EndNameClassChild(Parser._schemaBuilder.MakeNameClassChoice(_nameClasses, StartLocation, Annotations));
        }
    }
}
