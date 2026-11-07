using System;
using System.Collections.Generic;
using System.Text;
using Signet.EpubCheck.Core.Jing.Sax;
using Signet.EpubCheck.Core.Jing.Validate.Schematron.XPath;
using static Signet.EpubCheck.Core.Jing.Validate.Schematron.SchematronSchema;

namespace Signet.EpubCheck.Core.Jing.Validate.Schematron;

/// <summary>
/// Validates a document against a Schematron schema (Jing's Schematron <c>ValidatorImpl</c> with the stylesheet the
/// schema was compiled to, and its <c>OutputHandler</c>): the document is built as a tree, then at its end each node is
/// visited in document order and, for each pattern in order, the first rule whose context matches fires. A failed
/// assert or a true report is an error: <c>assertion failed:</c> or <c>report:</c>, then the statement on a new line,
/// indented by two spaces, its white space collapsed, at the place of the node.
/// </summary>
internal sealed class SchematronValidator : IValidator, IDtdHandler
{
    private const string Indent = "  ";

    private readonly SchematronSchema _schema;
    private readonly ISaxErrorHandler _eh;
    private readonly JingLocalizer _localizer;
    private readonly Handler _handler;

    public SchematronValidator(SchematronSchema schema, ISaxErrorHandler eh, JingLocalizer localizer)
    {
        _schema = schema;
        _eh = eh;
        _localizer = localizer;
        _handler = new Handler(this);
    }

    public IContentHandler ContentHandler => _handler;

    public IDtdHandler DtdHandler => this;

    public void Reset() => _handler.Reset();

    public void NotationDecl(string name, string? publicId, string? systemId)
    {
    }

    public void UnparsedEntityDecl(string name, string? publicId, string? systemId, string notationName)
    {
    }

    private void Run(XdmDocument document)
    {
        GlobalVariables globals = new(_schema, document);
        try
        {
            Visit(document, globals);
        }
        catch (XPathException e)
        {
            throw new SaxException(_localizer.Message("xpath_dynamic_error", e.Message), e);
        }
    }

    private void Visit(XdmNode node, IXPathVariables globals)
    {
        XPathContext context = new() { Item = node, Current = node, Variables = globals, BaseUri = _schema.BaseUri };
        foreach (CompiledPattern pattern in _schema.Patterns)
        {
            foreach (CompiledRule rule in pattern.Rules)
            {
                if (rule.Context!.Matches(node, context))
                {
                    RunItems(rule.Items, node, context, null);
                    break;
                }
            }
        }

        foreach (XdmNode child in node.Children)
        {
            if (child.Kind == XdmNodeKind.Element)
            {
                Visit(child, globals);
            }
        }
    }

    private void RunItems(List<RuleItem> items, XdmNode node, XPathContext context, HashSet<string>? extending)
    {
        foreach (RuleItem item in items)
        {
            switch (item)
            {
                case LetItem let:
                    context = context.WithVariable(let.Name, let.Value.Evaluate(context));
                    break;
                case ExtendsItem extends:
                    extending ??= [];
                    if (_schema.AbstractRules.TryGetValue(extends.RuleId, out CompiledRule? abstractRule) && extending.Add(extends.RuleId))
                    {
                        RunItems(abstractRule.Items, node, context, extending);
                        extending.Remove(extends.RuleId);
                    }

                    break;
                case AssertionItem assertion:
                    bool result = assertion.Test.EvaluateBoolean(context);
                    if (result == assertion.IsReport)
                    {
                        Report(assertion, node, context);
                    }

                    break;
            }
        }
    }

    private void Report(AssertionItem assertion, XdmNode node, XPathContext context)
    {
        StringBuilder message = new(_schema.StatementsOnly ? string.Empty : _localizer.Message(assertion.IsReport ? "report" : "failed_assertion"));
        if (assertion.Statement is not null)
        {
            message.Append(Environment.NewLine).Append(Indent);
            foreach (MessagePart part in assertion.Statement)
            {
                AppendCollapsed(message, part switch
                {
                    TextPart text => text.Text,
                    ValueOfPart valueOf => XPathValues.StringOfFirst(valueOf.Select.Evaluate(context)),
                    NamePart { Path: null } => node.QName,
                    NamePart name => (name.Path!.Evaluate(context).Count > 0 && name.Path.Evaluate(context)[0] is XdmNode n) ? n.QName : string.Empty,
                    _ => string.Empty,
                });
            }

            if (message[^1] == ' ')
            {
                message.Length--;
            }
        }

        _eh.OnError(new SaxParseException(message.ToString(), new FixedLocator(node.SystemId, node.LineNumber, node.ColumnNumber)));
    }

    // Jing's OutputHandler: every run of white space becomes one space, none after another space.
    private static void AppendCollapsed(StringBuilder message, string text)
    {
        foreach (char c in text)
        {
            if (c is ' ' or '\r' or '\n' or '\t')
            {
                if (message.Length == 0 || message[^1] != ' ')
                {
                    message.Append(' ');
                }
            }
            else
            {
                message.Append(c);
            }
        }
    }

    // The global variables (schema, phase and pattern lets), evaluated once per document when first used.
    private sealed class GlobalVariables(SchematronSchema schema, XdmDocument document) : IXPathVariables
    {
        private readonly Dictionary<string, IReadOnlyList<object>> _values = new(StringComparer.Ordinal);
        private readonly HashSet<string> _evaluating = new(StringComparer.Ordinal);

        public IReadOnlyList<object> Get(string name)
        {
            if (_values.TryGetValue(name, out IReadOnlyList<object>? value))
            {
                return value;
            }

            int index = schema.GlobalLets.FindIndex(l => l.Name == name);
            if (index < 0 || !_evaluating.Add(name))
            {
                throw new XPathException("variable $" + name + " is not defined");
            }

            XPathContext context = new() { Item = document, Current = document, Variables = this, BaseUri = schema.BaseUri };
            value = schema.GlobalLets[index].Value.Evaluate(context);
            _evaluating.Remove(name);
            _values[name] = value;
            return value;
        }
    }

    private sealed class Handler(SchematronValidator owner) : IContentHandler
    {
        private XdmTreeBuilder _builder = new();

        public void Reset() => _builder = new XdmTreeBuilder();

        public void SetDocumentLocator(ILocator locator) => _builder.SetDocumentLocator(locator);

        public void StartDocument() => _builder.StartDocument();

        public void EndDocument()
        {
            _builder.EndDocument();
            owner.Run(_builder.Document!);
        }

        public void StartPrefixMapping(string prefix, string uri) => _builder.StartPrefixMapping(prefix, uri);

        public void EndPrefixMapping(string prefix) => _builder.EndPrefixMapping(prefix);

        public void StartElement(string namespaceUri, string localName, string qName, IAttributes attributes) =>
            _builder.StartElement(namespaceUri, localName, qName, attributes);

        public void EndElement(string namespaceUri, string localName, string qName) => _builder.EndElement(namespaceUri, localName, qName);

        public void Characters(string text) => _builder.Characters(text);

        public void ProcessingInstruction(string target, string data) => _builder.ProcessingInstruction(target, data);
    }
}
