using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using Signet.EpubCheck.Core.Jing.Sax;
using Signet.EpubCheck.Core.Jing.Validate.Schematron.XPath;

namespace Signet.EpubCheck.Core.Jing.Validate.Schematron;

/// <summary>ISO Schematron and Schematron 1.5 for <see cref="AutoSchemaReader"/> (Jing's <c>SchematronSchemaReaderFactory</c>).</summary>
internal sealed class SchematronLanguage(string namespaceUri) : ISchemaLanguage
{
    public const string IsoNamespace = "http://purl.oclc.org/dsdl/schematron";
    public const string Schematron15Namespace = "http://www.ascc.net/xml/schematron";

    public string NamespaceUri { get; } = namespaceUri;

    public ISchema CreateSchema(InputSource source, SchemaReadSettings settings, bool isAttributesSchema) =>
        SchematronSchema.Read(source, settings, NamespaceUri);
}

/// <summary>
/// A Schematron schema, compiled for evaluation (what Jing's <c>iso-schematron.xsl</c> or <c>schematron.xsl</c> compiles
/// it to, run by Saxon): the active patterns of the phase with their rules, the global variables, the abstract rules.
/// The schema is first validated with Jing's RELAX NG schema of its language; the checks of Jing's stylesheet (phases,
/// active patterns, extended rules, diagnostics) follow.
/// </summary>
internal sealed class SchematronSchema : ISchema
{
    private const string AllPhases = "#ALL";

    private static readonly Dictionary<string, Lazy<RelaxNgSchema>> s_languageSchemas = new(StringComparer.Ordinal)
    {
        [SchematronLanguage.IsoNamespace] = new(() => LoadCompact("iso-schematron.rnc")),
        [SchematronLanguage.Schematron15Namespace] = new(() => LoadCompact("schematron.rnc")),
    };

    private SchematronSchema(string? baseUri)
    {
        BaseUri = baseUri;
    }

    public string? BaseUri { get; }

    public List<CompiledPattern> Patterns { get; } = [];

    public List<(string Name, XPathExpr Value)> GlobalLets { get; } = [];

    public Dictionary<string, CompiledRule> AbstractRules { get; } = new(StringComparer.Ordinal);

    /// <exception cref="IncorrectSchemaException">The schema has errors (reported).</exception>
    public static SchematronSchema Read(InputSource source, SchemaReadSettings settings, string ns)
    {
        CountingErrorHandler ceh = new(settings.ErrorHandler);
        XdmTreeBuilder builder = new();
        RelaxNgValidator validator = s_languageSchemas[ns].Value.CreateValidator(ceh, settings.Culture);
        new XmlEventReader { ContentHandler = new TeeHandler(validator.ContentHandler, builder), ErrorHandler = ceh }.Parse(source);
        if (ceh.HadErrorOrFatalError)
        {
            throw new IncorrectSchemaException();
        }

        Compiler compiler = new(new SchematronSchema(source.SystemId), ns, ceh, JingLocalizer.ForSchematron(settings.Culture), source.SystemId);
        SchematronSchema schema = compiler.Compile(builder.Document!);
        return ceh.HadErrorOrFatalError ? throw new IncorrectSchemaException() : schema;
    }

    public IValidator CreateValidator(ISaxErrorHandler errorHandler, CultureInfo? culture = null) =>
        new SchematronValidator(this, errorHandler, JingLocalizer.ForSchematron(culture));

    private static RelaxNgSchema LoadCompact(string name)
    {
        string resource = "Signet.EpubCheck.Core.Jing.Resources." + name;
        using Stream stream = typeof(SchematronSchema).Assembly.GetManifestResourceStream(resource)
            ?? throw new InvalidOperationException("No resource " + resource);
        using StreamReader reader = new(stream);
        string text = reader.ReadToEnd();
        return new RelaxNgSchemaReader { Syntax = RelaxNgSyntax.Compact }.CreateSchema(InputSource.FromString("resource:/jing/" + name, text));
    }

    /// <summary>A pattern: its rules in order, the first that matches a node fires.</summary>
    internal sealed record CompiledPattern(List<CompiledRule> Rules);

    /// <summary>A rule: its context (null for an abstract rule) and its lets, asserts, reports and extends in order.</summary>
    internal sealed record CompiledRule(XsltPattern? Context, List<RuleItem> Items);

    internal abstract record RuleItem;

    internal sealed record LetItem(string Name, XPathExpr Value) : RuleItem;

    internal sealed record ExtendsItem(string RuleId) : RuleItem;

    /// <summary>An assert (fires when the test is false) or a report (fires when it is true), with its message.</summary>
    internal sealed record AssertionItem(bool IsReport, XPathExpr Test, List<MessagePart>? Statement) : RuleItem;

    internal abstract record MessagePart;

    internal sealed record TextPart(string Text) : MessagePart;

    internal sealed record ValueOfPart(XPathExpr Select) : MessagePart;

    /// <summary><c>name</c>: the name of the context node, or of the first node of the path.</summary>
    internal sealed record NamePart(XPathExpr? Path) : MessagePart;

    private sealed class TeeHandler(IContentHandler first, IContentHandler second) : IContentHandler
    {
        public void SetDocumentLocator(ILocator locator)
        {
            first.SetDocumentLocator(locator);
            second.SetDocumentLocator(locator);
        }

        public void StartDocument()
        {
            first.StartDocument();
            second.StartDocument();
        }

        public void EndDocument()
        {
            first.EndDocument();
            second.EndDocument();
        }

        public void StartPrefixMapping(string prefix, string uri)
        {
            first.StartPrefixMapping(prefix, uri);
            second.StartPrefixMapping(prefix, uri);
        }

        public void EndPrefixMapping(string prefix)
        {
            first.EndPrefixMapping(prefix);
            second.EndPrefixMapping(prefix);
        }

        public void StartElement(string namespaceUri, string localName, string qName, IAttributes attributes)
        {
            first.StartElement(namespaceUri, localName, qName, attributes);
            second.StartElement(namespaceUri, localName, qName, attributes);
        }

        public void EndElement(string namespaceUri, string localName, string qName)
        {
            first.EndElement(namespaceUri, localName, qName);
            second.EndElement(namespaceUri, localName, qName);
        }

        public void Characters(string text)
        {
            first.Characters(text);
            second.Characters(text);
        }

        public void ProcessingInstruction(string target, string data)
        {
            first.ProcessingInstruction(target, data);
            second.ProcessingInstruction(target, data);
        }
    }

    // Does what Jing's stylesheet does with the schema document: the checks of its "check" mode, then the compilation.
    private sealed class Compiler(SchematronSchema schema, string ns, CountingErrorHandler eh, JingLocalizer localizer, string? systemId)
    {
        // The xml prefix is always bound in XPath.
        private readonly Dictionary<string, string> _namespaces = new(StringComparer.Ordinal) { ["xml"] = WellKnownNamespaces.Xml };
        private XdmNode _root = null!;

        public SchematronSchema Compile(XdmDocument document)
        {
            _root = document.Children.First(c => c.Kind == XdmNodeKind.Element);
            foreach (XdmNode nsElement in Children(_root, "ns"))
            {
                _namespaces[Attr(nsElement, "prefix") ?? string.Empty] = Attr(nsElement, "uri") ?? string.Empty;
            }

            List<XdmNode> phases = [.. Children(_root, "phase").Where(p => Attr(p, "id") is not null)];
            List<XdmNode> patterns = [.. Children(_root, "pattern")];
            Dictionary<string, XdmNode> rulesById = new(StringComparer.Ordinal);
            foreach (XdmNode rule in patterns.SelectMany(p => Children(p, "rule")))
            {
                if (Id(rule) is { } id)
                {
                    rulesById.TryAdd(id, rule);
                }
            }

            HashSet<string> diagnostics = [.. Children(_root, "diagnostics").SelectMany(d => Children(d, "diagnostic")).Select(Id).OfType<string>()];
            Check(phases, patterns, rulesById, diagnostics);

            AddLets(_root);
            string? defaultPhase = Attr(_root, "defaultPhase") is { } dp ? Normalize(dp) : null;
            string phase = defaultPhase ?? AllPhases;
            IEnumerable<XdmNode> active;
            if (phase == AllPhases)
            {
                foreach (XdmNode p in phases)
                {
                    AddLets(p);
                }

                active = patterns;
            }
            else
            {
                XdmNode? phaseElement = phases.FirstOrDefault(p => Id(p) == phase);
                if (phaseElement is not null)
                {
                    AddLets(phaseElement);
                }

                HashSet<string> activeIds = phaseElement is null ? [] : [.. Children(phaseElement, "active").Select(a => Normalize(Attr(a, "pattern") ?? string.Empty))];
                active = patterns.Where(p => Id(p) is { } id && activeIds.Contains(id));
            }

            foreach (XdmNode pattern in active)
            {
                AddLets(pattern);
                CompiledPattern compiled = new([]);
                foreach (XdmNode rule in Children(pattern, "rule"))
                {
                    if (Attr(rule, "context") is { } context)
                    {
                        compiled.Rules.Add(new CompiledRule(Compile(rule, context, x => new XsltPattern(x)), CompileItems(rule)));
                    }
                    else if (Id(rule) is { } id)
                    {
                        schema.AbstractRules.TryAdd(id, new CompiledRule(null, CompileItems(rule)));
                    }
                }

                schema.Patterns.Add(compiled);
            }

            // An abstract rule outside the active patterns can still be extended.
            foreach ((string id, XdmNode rule) in rulesById.Where(r => Attr(r.Value, "context") is null && !schema.AbstractRules.ContainsKey(r.Key)))
            {
                schema.AbstractRules[id] = new CompiledRule(null, CompileItems(rule));
            }

            return schema;
        }

        private void Check(List<XdmNode> phases, List<XdmNode> patterns, Dictionary<string, XdmNode> rulesById, HashSet<string> diagnostics)
        {
            if (Attr(_root, "defaultPhase") is { } defaultPhase && !phases.Exists(p => Id(p) == Normalize(defaultPhase)))
            {
                Error("default_phase_missing", _root, Normalize(defaultPhase));
            }

            HashSet<string> patternIds = [.. patterns.Select(Id).OfType<string>()];
            IEnumerable<XdmNode> checkedNodes = phases.SelectMany(p => Children(p, "active"))
                .Concat(patterns.SelectMany(p => Children(p, "rule")).SelectMany(r => r.Children.Where(c => c.Kind == XdmNodeKind.Element && c.NamespaceUri == ns)))
                .OrderBy(n => n.Order);
            foreach (XdmNode node in checkedNodes)
            {
                switch (node.LocalName)
                {
                    case "active":
                        string pattern = Normalize(Attr(node, "pattern") ?? string.Empty);
                        if (!patternIds.Contains(pattern))
                        {
                            Error("active_missing", node, pattern);
                        }

                        break;
                    case "extends":
                        string ruleId = Normalize(Attr(node, "rule") ?? string.Empty);
                        rulesById.TryGetValue(ruleId, out XdmNode? target);
                        if (target is null)
                        {
                            Error("extends_missing", node, ruleId);
                        }
                        else
                        {
                            if (Attr(target, "context") is not null)
                            {
                                Error("extends_concrete", node, ruleId);
                            }

                            if (HasCycle(target, [node.Parent!], rulesById))
                            {
                                Error("extends_cycle", node, ruleId);
                            }
                        }

                        break;
                    case "assert" or "report":
                        foreach (string head in (Attr(node, "diagnostics") ?? string.Empty).Split([' ', '\t', '\n', '\r'], StringSplitOptions.RemoveEmptyEntries))
                        {
                            if (!diagnostics.Contains(head))
                            {
                                Error("diagnostic_missing", null, head);
                            }
                        }

                        break;
                }
            }
        }

        // Jing's check-cycles: following the extends of a rule leads back to a rule already on the way.
        private bool HasCycle(XdmNode rule, List<XdmNode> path, Dictionary<string, XdmNode> rulesById)
        {
            if (path.Contains(rule))
            {
                return true;
            }

            List<XdmNode> next = [.. path, rule];
            foreach (XdmNode extends in Children(rule, "extends"))
            {
                if (rulesById.TryGetValue(Normalize(Attr(extends, "rule") ?? string.Empty), out XdmNode? target) && HasCycle(target, next, rulesById))
                {
                    return true;
                }
            }

            return false;
        }

        private void AddLets(XdmNode parent)
        {
            foreach (XdmNode let in Children(parent, "let"))
            {
                if (Attr(let, "name") is { } name && Attr(let, "value") is { } value && Compile(let, value, x => x) is { } expr)
                {
                    schema.GlobalLets.Add((name, expr));
                }
            }
        }

        private List<RuleItem> CompileItems(XdmNode rule)
        {
            List<RuleItem> items = [];
            foreach (XdmNode child in rule.Children.Where(c => c.Kind == XdmNodeKind.Element && c.NamespaceUri == ns))
            {
                switch (child.LocalName)
                {
                    case "let":
                        if (Attr(child, "name") is { } name && Compile(child, Attr(child, "value") ?? string.Empty, x => x) is { } value)
                        {
                            items.Add(new LetItem(name, value));
                        }

                        break;
                    case "extends":
                        items.Add(new ExtendsItem(Normalize(Attr(child, "rule") ?? string.Empty)));
                        break;
                    case "assert" or "report":
                        if (Compile(child, Attr(child, "test") ?? string.Empty, x => x) is { } test)
                        {
                            items.Add(new AssertionItem(child.LocalName == "report", test, Statement(child)));
                        }

                        break;
                }
            }

            return items;
        }

        // Jing writes a statement only when the assertion has a child element or its first text is not blank.
        private List<MessagePart>? Statement(XdmNode assertion)
        {
            bool hasElement = assertion.Children.Any(c => c.Kind == XdmNodeKind.Element);
            XdmNode? firstText = assertion.Children.FirstOrDefault(c => c.Kind == XdmNodeKind.Text);
            if (!hasElement && (firstText is null || string.IsNullOrWhiteSpace(firstText.StringValue)))
            {
                return null;
            }

            List<MessagePart> parts = [];
            AddParts(assertion, parts);
            return parts;
        }

        private void AddParts(XdmNode parent, List<MessagePart> parts)
        {
            foreach (XdmNode child in parent.Children)
            {
                if (child.Kind == XdmNodeKind.Text)
                {
                    parts.Add(new TextPart(child.StringValue));
                    continue;
                }

                if (child.NamespaceUri != ns)
                {
                    continue;
                }

                switch (child.LocalName)
                {
                    case "name":
                        parts.Add(new NamePart(Attr(child, "path") is { } path ? Compile(child, path, x => x) : null));
                        break;
                    case "value-of":
                        if (Compile(child, Attr(child, "select") ?? string.Empty, x => x) is { } select)
                        {
                            parts.Add(new ValueOfPart(select));
                        }

                        break;
                    case "emph" or "dir" or "span":
                        AddParts(child, parts);
                        break;
                }
            }
        }

        private T? Compile<T>(XdmNode element, string expression, Func<XPathExpr, T> make)
            where T : class
        {
            try
            {
                return make(XPathParser.Parse(expression, _namespaces));
            }
            catch (XPathException e)
            {
                eh.OnError(new SaxParseException(localizer.Message("xpath_syntax_error", expression, e.Message), Locator(element)));
                return null;
            }
        }

        private void Error(string key, XdmNode? node, string arg) =>
            eh.OnError(new SaxParseException(localizer.Message(key, arg), node is null ? new FixedLocator(systemId, FixedLocator.Unknown, FixedLocator.Unknown) : Locator(node)));

        private FixedLocator Locator(XdmNode node) => new(node.SystemId ?? systemId, node.LineNumber, node.ColumnNumber);

        private IEnumerable<XdmNode> Children(XdmNode parent, string localName) =>
            parent.Children.Where(c => c.Kind == XdmNodeKind.Element && c.NamespaceUri == ns && c.LocalName == localName);

        private static string? Attr(XdmNode element, string localName) =>
            element.Attributes.FirstOrDefault(a => a.NamespaceUri.Length == 0 && a.LocalName == localName)?.StringValue;

        private static string? Id(XdmNode element) => Attr(element, "id") is { } id ? Normalize(id) : null;

        private static string Normalize(string s) => string.Join(' ', s.Split([' ', '\t', '\n', '\r'], StringSplitOptions.RemoveEmptyEntries));
    }
}
