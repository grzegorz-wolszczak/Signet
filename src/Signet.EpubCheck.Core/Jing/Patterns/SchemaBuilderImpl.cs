using System;
using System.Collections.Generic;
using Signet.EpubCheck.Core.Jing.Datatype;
using Signet.EpubCheck.Core.Jing.Parse;
using Signet.EpubCheck.Core.Jing.Sax;

namespace Signet.EpubCheck.Core.Jing.Patterns;

/// <summary>
/// Builds the patterns of a schema from what the parser reads, and simplifies and checks them (Jing's
/// <c>SchemaBuilderImpl</c>): grammars, definitions and their <c>combine</c>, includes and their overrides, external
/// references, datatypes; then the recursion check, the expansion of the references and the restrictions of RELAX NG
/// section 7. Errors go to the error handler; a schema with errors ends in <see cref="IllegalSchemaException"/>.
/// </summary>
internal sealed class SchemaBuilderImpl : ISchemaBuilder
{
    private readonly SchemaBuilderImpl? _parent;
    private readonly ISubParser _subParser;
    private readonly SchemaPatternBuilder _pb;
    private readonly IDatatypeLibraryFactory _datatypeLibraryFactory;
    private readonly string _inheritNs;
    private readonly ISaxErrorHandler? _eh;
    private readonly OpenIncludes? _openIncludes;
    private readonly AttributeNameClassChecker _attributeNameClassChecker = new();
    private bool _hadError;

    private SchemaBuilderImpl(ISubParser subParser, ISaxErrorHandler? eh, IDatatypeLibraryFactory datatypeLibraryFactory,
        SchemaPatternBuilder pb, JingLocalizer localizer)
    {
        _subParser = subParser;
        _eh = eh;
        _datatypeLibraryFactory = datatypeLibraryFactory;
        _pb = pb;
        _inheritNs = string.Empty;
        Localizer = localizer;
    }

    private SchemaBuilderImpl(string inheritNs, string? uri, SchemaBuilderImpl parent)
    {
        _parent = parent;
        _subParser = parent._subParser;
        _eh = parent._eh;
        _datatypeLibraryFactory = parent._datatypeLibraryFactory;
        _pb = parent._pb;
        _inheritNs = parent.ResolveInherit(inheritNs);
        _openIncludes = new OpenIncludes(uri, parent._openIncludes);
        Localizer = parent.Localizer;
    }

    /// <summary>The messages of the pattern module.</summary>
    public JingLocalizer Localizer { get; }

    /// <summary>Parses, simplifies and checks a schema.</summary>
    /// <exception cref="IllegalSchemaException">The schema has errors (reported to <paramref name="eh"/>).</exception>
    public static Pattern Parse(ISubParseable parseable, ISaxErrorHandler? eh, IDatatypeLibraryFactory? datatypeLibraryFactory,
        SchemaPatternBuilder pb, bool isAttributesPattern, JingLocalizer localizer)
    {
        SchemaBuilderImpl sb = new(parseable, eh, new BuiltinDatatypeLibraryFactory(datatypeLibraryFactory, localizer), pb, localizer);
        Pattern pattern = parseable.Parse(sb, new RootScope(sb));
        if (isAttributesPattern)
        {
            pattern = sb.WrapAttributesPattern(pattern);
        }

        return sb.ExpandPattern(pattern);
    }

    public Pattern MakeChoice(List<Pattern> patterns, ILocator? loc, AnnotationsImpl? anno) => Fold(patterns, _pb.MakeChoice);

    public Pattern MakeInterleave(List<Pattern> patterns, ILocator? loc, AnnotationsImpl? anno) => Fold(patterns, _pb.MakeInterleave);

    public Pattern MakeGroup(List<Pattern> patterns, ILocator? loc, AnnotationsImpl? anno) => Fold(patterns, _pb.MakeGroup);

    public Pattern MakeOneOrMore(Pattern p, ILocator? loc, AnnotationsImpl? anno) => _pb.MakeOneOrMore(p);

    public Pattern MakeZeroOrMore(Pattern p, ILocator? loc, AnnotationsImpl? anno) => _pb.MakeZeroOrMore(p);

    public Pattern MakeOptional(Pattern p, ILocator? loc, AnnotationsImpl? anno) => _pb.MakeOptional(p);

    public Pattern MakeList(Pattern p, ILocator? loc, AnnotationsImpl? anno) => _pb.MakeList(p, loc);

    public Pattern MakeMixed(Pattern p, ILocator? loc, AnnotationsImpl? anno) => _pb.MakeMixed(p);

    public Pattern MakeEmpty(ILocator? loc, AnnotationsImpl? anno) => _pb.MakeEmpty();

    public Pattern MakeNotAllowed(ILocator? loc, AnnotationsImpl? anno) => _pb.MakeUnexpandedNotAllowed();

    public Pattern MakeText(ILocator? loc, AnnotationsImpl? anno) => _pb.MakeText();

    public Pattern MakeErrorPattern() => _pb.MakeError();

    public NameClass MakeErrorNameClass() => new ErrorNameClass();

    public Pattern MakeAttribute(NameClass nc, Pattern p, ILocator? loc, AnnotationsImpl? anno)
    {
        string? messageId = _attributeNameClassChecker.CheckNameClass(nc);
        if (messageId is not null)
        {
            Error(messageId, loc);
        }

        return _pb.MakeAttribute(nc, p, loc, anno?.DefaultValue);
    }

    public Pattern MakeElement(NameClass nc, Pattern p, ILocator? loc, AnnotationsImpl? anno) => _pb.MakeElement(nc, p, loc);

    public IDataPatternBuilder MakeDataPatternBuilder(string datatypeLibrary, string type, ILocator? loc)
    {
        IDatatypeLibrary? dl = _datatypeLibraryFactory.CreateDatatypeLibrary(datatypeLibrary);
        if (dl is null)
        {
            Error("unrecognized_datatype_library", loc, datatypeLibrary);
        }
        else
        {
            try
            {
                return new DataPatternBuilderImpl(this, dl.CreateDatatypeBuilder(type), new Name(datatypeLibrary, type));
            }
            catch (DatatypeException e)
            {
                string? detail = e.Detail;
                if (detail is not null)
                {
                    Error("unsupported_datatype_detail", loc, datatypeLibrary, type, detail);
                }
                else
                {
                    Error("unrecognized_datatype", loc, datatypeLibrary, type);
                }
            }
        }

        return new DummyDataPatternBuilder(_pb);
    }

    public Pattern MakeValue(string datatypeLibrary, string type, string value, IParseContext context, string ns, ILocator? loc, AnnotationsImpl? anno)
    {
        IDatatypeLibrary? dl = _datatypeLibraryFactory.CreateDatatypeLibrary(datatypeLibrary);
        if (dl is null)
        {
            Error("unrecognized_datatype_library", loc, datatypeLibrary);
            return _pb.MakeError();
        }

        IDatatypeBuilder dtb;
        try
        {
            dtb = dl.CreateDatatypeBuilder(type);
        }
        catch (DatatypeException)
        {
            Error("unrecognized_datatype", loc, datatypeLibrary, type);
            return _pb.MakeError();
        }

        try
        {
            IDatatype dt = dtb.CreateDatatype();
            object? obj = dt.CreateValue(value, new ValidationContextImpl(this, context, ns));
            if (obj is not null)
            {
                return _pb.MakeValue(dt, new Name(datatypeLibrary, type), obj, value);
            }

            Error("invalid_value", loc, value);
        }
        catch (DatatypeException e)
        {
            // Jing names keys that are not in its bundle here ("datatype_requires_param"): the port uses the ones that are.
            string? detail = e.Detail;
            if (detail is not null)
            {
                Error("datatype_requires_parameter_detail", loc, detail);
            }
            else
            {
                Error("datatype_requires_parameter", loc);
            }
        }

        return _pb.MakeError();
    }

    public IGrammar MakeGrammar(IScope? parent) => new GrammarImpl(this, parent);

    /// <exception cref="IllegalSchemaException">Never: errors of the referenced schema are noted, an error pattern stands for it.</exception>
    public Pattern MakeExternalRef(string href, string? baseUri, string ns, IScope scope, ILocator? loc, AnnotationsImpl? anno)
    {
        ISubParseable subParseable = _subParser.CreateSubParseable(href, baseUri);
        string? uri = subParseable.Uri;
        for (OpenIncludes? inc = _openIncludes; inc is not null; inc = inc.Parent)
        {
            if (string.Equals(inc.Uri, uri, StringComparison.Ordinal))
            {
                Error("recursive_include", loc, uri);
                return _pb.MakeError();
            }
        }

        try
        {
            return subParseable.Parse(new SchemaBuilderImpl(ns, uri, this), scope);
        }
        catch (IllegalSchemaException)
        {
            NoteError();
            return _pb.MakeError();
        }
    }

    public NameClass MakeNameClassChoice(List<NameClass> nameClasses, ILocator? loc, AnnotationsImpl? anno)
    {
        if (nameClasses.Count == 0)
        {
            throw new ArgumentException("No name class", nameof(nameClasses));
        }

        NameClass result = nameClasses[0];
        for (int i = 1; i < nameClasses.Count; i++)
        {
            result = new ChoiceNameClass(result, nameClasses[i]);
        }

        return result;
    }

    public NameClass MakeName(string ns, string localName, string? prefix, ILocator? loc, AnnotationsImpl? anno) =>
        new SimpleNameClass(new Name(ResolveInherit(ns), localName));

    public NameClass MakeNsName(string ns, ILocator? loc, AnnotationsImpl? anno) => new NsNameClass(ResolveInherit(ns));

    public NameClass MakeNsName(string ns, NameClass except, ILocator? loc, AnnotationsImpl? anno) =>
        new NsNameExceptNameClass(ResolveInherit(ns), except);

    public NameClass MakeAnyName(ILocator? loc, AnnotationsImpl? anno) => new AnyNameClass();

    public NameClass MakeAnyName(NameClass except, ILocator? loc, AnnotationsImpl? anno) => new AnyNameExceptNameClass(except);

    public ILocator MakeLocation(string? systemId, int lineNumber, int columnNumber) => new FixedLocator(systemId, lineNumber, columnNumber);

    private static Pattern Fold(List<Pattern> patterns, Func<Pattern, Pattern, Pattern> combine)
    {
        if (patterns.Count == 0)
        {
            throw new ArgumentException("No pattern", nameof(patterns));
        }

        Pattern result = patterns[0];
        for (int i = 1; i < patterns.Count; i++)
        {
            result = combine(result, patterns[i]);
        }

        return result;
    }

    // There is no locator for this element.
    private Pattern WrapAttributesPattern(Pattern pattern) => MakeElement(MakeAnyName(null, null), pattern, null, null);

    private Pattern ExpandPattern(Pattern pattern)
    {
        if (!_hadError)
        {
            try
            {
                pattern.CheckRecursion(0, Localizer);
                pattern = pattern.Expand(_pb);
                pattern.CheckRestrictions(RestrictionContext.Start, null, null);
                if (!_hadError)
                {
                    return pattern;
                }
            }
            catch (SaxParseException e)
            {
                Error(e);
            }
            catch (RestrictionViolationException e)
            {
                if (e.ViolationName is not null)
                {
                    Error(e.MessageId, e.Locator, NameFormatter.Format(e.ViolationName, Localizer));
                }
                else if (e.NamespaceUri is not null)
                {
                    Error(e.MessageId, e.Locator, e.NamespaceUri);
                }
                else
                {
                    Error(e.MessageId, e.Locator);
                }
            }
        }

        throw new IllegalSchemaException();
    }

    private string ResolveInherit(string ns) => ReferenceEquals(ns, GrammarSection.InheritNs) ? _inheritNs : ns;

    private void Error(SaxParseException message)
    {
        NoteError();
        _eh?.OnError(message);
    }

    private void Error(string key, ILocator? loc, params string?[] args) =>
        Error(new SaxParseException(Localizer.Message(key, args), loc));

    private void NoteError()
    {
        if (!_hadError && _parent is not null)
        {
            _parent.NoteError();
        }

        _hadError = true;
    }

    private sealed record OpenIncludes(string? Uri, OpenIncludes? Parent);

    private sealed class DummyDataPatternBuilder(SchemaPatternBuilder pb) : IDataPatternBuilder
    {
        public void AddParam(string name, string value, IParseContext context, string ns, ILocator? loc, AnnotationsImpl? anno)
        {
        }

        public Pattern MakePattern(ILocator? loc, AnnotationsImpl? anno) => pb.MakeError();

        public Pattern MakePattern(Pattern except, ILocator? loc, AnnotationsImpl? anno) => pb.MakeError();
    }

    // The context of a parameter or value: the ns attribute is the default namespace.
    private sealed class ValidationContextImpl(SchemaBuilderImpl sb, IValidationContext vc, string ns) : IValidationContext
    {
        private readonly string? _ns = ns.Length == 0 ? null : ns;

        public string? BaseUri => vc.BaseUri;

        public string? ResolveNamespacePrefix(string prefix)
        {
            string? result = prefix.Length == 0 ? _ns : vc.ResolveNamespacePrefix(prefix);
            if (ReferenceEquals(result, GrammarSection.InheritNs))
            {
                return sb._inheritNs.Length == 0 ? null : sb._inheritNs;
            }

            return result;
        }

        public bool IsUnparsedEntity(string entityName) => vc.IsUnparsedEntity(entityName);

        public bool IsNotation(string notationName) => vc.IsNotation(notationName);
    }

    private sealed class DataPatternBuilderImpl(SchemaBuilderImpl sb, IDatatypeBuilder dtb, Name dtName) : IDataPatternBuilder
    {
        private readonly List<string> _params = [];

        public void AddParam(string name, string value, IParseContext context, string ns, ILocator? loc, AnnotationsImpl? anno)
        {
            try
            {
                dtb.AddParameter(name, value, new ValidationContextImpl(sb, context, ns));
                _params.Add(name);
                _params.Add(value);
            }
            catch (DatatypeException e)
            {
                string? detail = e.Detail;
                int pos = e.Index;
                string? displayedParam = pos == DatatypeException.Unknown ? null : DisplayParam(value, pos);
                if (displayedParam is not null)
                {
                    if (detail is not null)
                    {
                        sb.Error("invalid_param_detail_display", loc, detail, displayedParam);
                    }
                    else
                    {
                        sb.Error("invalid_param_display", loc, displayedParam);
                    }
                }
                else if (detail is not null)
                {
                    sb.Error("invalid_param_detail", loc, detail);
                }
                else
                {
                    sb.Error("invalid_param", loc);
                }
            }
        }

        public Pattern MakePattern(ILocator? loc, AnnotationsImpl? anno)
        {
            try
            {
                return sb._pb.MakeData(dtb.CreateDatatype(), dtName, _params);
            }
            catch (DatatypeException e)
            {
                ReportInvalidParams(e, loc);
                return sb._pb.MakeError();
            }
        }

        public Pattern MakePattern(Pattern except, ILocator? loc, AnnotationsImpl? anno)
        {
            try
            {
                return sb._pb.MakeDataExcept(dtb.CreateDatatype(), dtName, _params, except, loc);
            }
            catch (DatatypeException e)
            {
                ReportInvalidParams(e, loc);
                return sb._pb.MakeError();
            }
        }

        private void ReportInvalidParams(DatatypeException e, ILocator? loc)
        {
            string? detail = e.Detail;
            if (detail is not null)
            {
                sb.Error("invalid_params_detail", loc, detail);
            }
            else
            {
                sb.Error("invalid_params", loc);
            }
        }

        private string DisplayParam(string value, int pos)
        {
            pos = Math.Clamp(pos, 0, value.Length);
            return sb.Localizer.Message("display_param", value[..pos], value[pos..]);
        }
    }

    private sealed class RootScope(SchemaBuilderImpl sb) : IScope
    {
        public Pattern MakeParentRef(string name, ILocator? loc, AnnotationsImpl? anno)
        {
            sb.Error("parent_ref_outside_grammar", loc);
            return sb.MakeErrorPattern();
        }

        public Pattern MakeRef(string name, ILocator? loc, AnnotationsImpl? anno)
        {
            sb.Error("ref_outside_grammar", loc);
            return sb.MakeErrorPattern();
        }
    }

    private sealed class GrammarImpl : IGrammar, IDiv, IIncludedGrammar
    {
        private readonly SchemaBuilderImpl _sb;

        // In the order the names were first seen: Jing walks a HashMap here, in an order of its own.
        private readonly Dictionary<string, RefPattern> _defines;
        private readonly List<RefPattern> _defineOrder;
        private readonly RefPattern _startRef;
        private readonly IScope? _parent;

        public GrammarImpl(SchemaBuilderImpl sb, IScope? parent)
        {
            _sb = sb;
            _parent = parent;
            _defines = new Dictionary<string, RefPattern>(StringComparer.Ordinal);
            _defineOrder = [];
            _startRef = new RefPattern(null);
        }

        // The grammar of an included schema: its definitions are those of the including grammar.
        public GrammarImpl(SchemaBuilderImpl sb, GrammarImpl g)
        {
            _sb = sb;
            _parent = g._parent;
            _startRef = g._startRef;
            _defines = g._defines;
            _defineOrder = g._defineOrder;
        }

        public Pattern EndGrammar(ILocator? loc, AnnotationsImpl? anno)
        {
            foreach (RefPattern rp in _defineOrder)
            {
                if (rp.Target is null)
                {
                    _sb.Error("reference_to_undefined", rp.RefLocator, rp.Name);
                    rp.Target = _sb._pb.MakeError();
                }
            }

            Pattern? start = _startRef.Target;
            if (start is null)
            {
                _sb.Error("missing_start_element", loc);
                start = _sb._pb.MakeError();
            }

            return start;
        }

        public void EndDiv(ILocator? loc, AnnotationsImpl? anno)
        {
        }

        public Pattern? EndIncludedGrammar(ILocator? loc, AnnotationsImpl? anno) => null;

        public void Define(string name, GrammarCombine? combine, Pattern pattern, ILocator? loc, AnnotationsImpl? anno) =>
            Define(Lookup(name), combine, pattern, loc);

        public void Define(RefPattern rp, GrammarCombine? combine, Pattern pattern, ILocator? loc)
        {
            switch (rp.ReplacementStatus)
            {
                case ReplacementStatus.Keep:
                    if (combine is null)
                    {
                        if (rp.IsCombineImplicit)
                        {
                            if (rp.Name is null)
                            {
                                _sb.Error("duplicate_start", loc);
                            }
                            else
                            {
                                _sb.Error("duplicate_define", loc, rp.Name);
                            }
                        }
                        else
                        {
                            rp.SetCombineImplicit();
                        }
                    }
                    else
                    {
                        CombineType combineType = combine == GrammarCombine.Choice ? CombineType.Choice : CombineType.Interleave;
                        if (rp.CombineType != CombineType.None && rp.CombineType != combineType)
                        {
                            if (rp.Name is null)
                            {
                                _sb.Error("conflict_combine_start", loc);
                            }
                            else
                            {
                                _sb.Error("conflict_combine_define", loc, rp.Name);
                            }
                        }

                        rp.CombineType = combineType;
                    }

                    if (rp.Target is null)
                    {
                        rp.Target = pattern;
                    }
                    else if (rp.CombineType == CombineType.Interleave)
                    {
                        rp.Target = _sb._pb.MakeInterleave(rp.Target, pattern);
                    }
                    else
                    {
                        rp.Target = _sb._pb.MakeChoice(rp.Target, pattern);
                    }

                    break;
                case ReplacementStatus.Require:
                    rp.ReplacementStatus = ReplacementStatus.Ignore;
                    break;
                case ReplacementStatus.Ignore:
                    break;
            }
        }

        public RefPattern Lookup(string name) => ReferenceEquals(name, GrammarSection.Start) ? _startRef : Lookup1(name);

        public Pattern MakeRef(string name, ILocator? loc, AnnotationsImpl? anno)
        {
            RefPattern p = Lookup1(name);
            if (p.RefLocator is null && loc is not null)
            {
                p.RefLocator = loc;
            }

            return p;
        }

        public Pattern MakeParentRef(string name, ILocator? loc, AnnotationsImpl? anno)
        {
            if (_parent is null)
            {
                _sb.Error("parent_ref_outside_grammar", loc);
                return _sb.MakeErrorPattern();
            }

            return _parent.MakeRef(name, loc, anno);
        }

        public IDiv MakeDiv() => this;

        public IInclude? MakeInclude() => new IncludeImpl(_sb, this);

        private RefPattern Lookup1(string name)
        {
            if (!_defines.TryGetValue(name, out RefPattern? p))
            {
                p = new RefPattern(name);
                _defines[name] = p;
                _defineOrder.Add(p);
            }

            return p;
        }
    }

    private sealed class IncludeImpl(SchemaBuilderImpl sb, GrammarImpl grammar) : IInclude, IDiv
    {
        private Override? _overrides;

        public void Define(string name, GrammarCombine? combine, Pattern pattern, ILocator? loc, AnnotationsImpl? anno)
        {
            RefPattern rp = grammar.Lookup(name);
            _overrides = new Override(rp, _overrides);
            grammar.Define(rp, combine, pattern, loc);
        }

        public void EndDiv(ILocator? loc, AnnotationsImpl? anno)
        {
        }

        public IDiv MakeDiv() => this;

        public IInclude? MakeInclude() => null;

        public void EndInclude(string href, string? baseUri, string ns, ILocator? loc, AnnotationsImpl? anno)
        {
            ISubParseable subParseable = sb._subParser.CreateSubParseable(href, baseUri);
            string? uri = subParseable.Uri;
            for (OpenIncludes? inc = sb._openIncludes; inc is not null; inc = inc.Parent)
            {
                if (string.Equals(inc.Uri, uri, StringComparison.Ordinal))
                {
                    sb.Error("recursive_include", loc, uri);
                    return;
                }
            }

            for (Override? o = _overrides; o is not null; o = o.Next)
            {
                o.SavedStatus = o.Target.ReplacementStatus;
                o.Target.ReplacementStatus = ReplacementStatus.Require;
            }

            try
            {
                SchemaBuilderImpl isb = new(ns, uri, sb);
                subParseable.ParseAsInclude(isb, new GrammarImpl(isb, grammar));
                for (Override? o = _overrides; o is not null; o = o.Next)
                {
                    if (o.Target.ReplacementStatus == ReplacementStatus.Require)
                    {
                        if (o.Target.Name is null)
                        {
                            sb.Error("missing_start_replacement", loc);
                        }
                        else
                        {
                            sb.Error("missing_define_replacement", loc, o.Target.Name);
                        }
                    }
                }
            }
            catch (IllegalSchemaException)
            {
                sb.NoteError();
            }
            finally
            {
                for (Override? o = _overrides; o is not null; o = o.Next)
                {
                    o.Target.ReplacementStatus = o.SavedStatus;
                }
            }
        }

        private sealed class Override(RefPattern target, Override? next)
        {
            public RefPattern Target { get; } = target;

            public Override? Next { get; } = next;

            public ReplacementStatus SavedStatus { get; set; }
        }
    }
}

/// <summary>Writes a name for a message about the schema (Jing's <c>NameFormatter</c>).</summary>
internal static class NameFormatter
{
    public static string Format(Name name, JingLocalizer localizer) =>
        name.NamespaceUri.Length == 0
            ? localizer.Message("name_absent_namespace", name.LocalName)
            : localizer.Message("name_with_namespace", name.NamespaceUri, name.LocalName);
}
