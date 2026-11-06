using System;
using System.Collections.Generic;
using System.Globalization;
using Signet.EpubCheck.Core.Jing.Sax;

namespace Signet.EpubCheck.Core.Jing.Validate.Nvdl;

/// <summary>
/// Validates a document against an NVDL schema (Jing's NVDL <c>ValidatorImpl</c>): the document is cut into sections
/// at namespace changes (and triggers), the rules of the current mode decide what each section gets (validated by a
/// schema, attached to its parent, allowed, rejected...), and the events of a section go to the validators it is attached
/// to. Attributes are grouped by namespace and filtered for each validator.
/// </summary>
internal sealed class NvdlValidator : IValidator, IContentHandler, IDtdHandler
{
    private const string NoNamespace = "\0";
    private const string InstanceNamespace = "http://purl.oclc.org/dsdl/nvdl/ns/instance/1.0";
    private const string PlaceholderElement = "placeholder";
    private const string NsAttribute = "ns";
    private const string LocalNameAttribute = "localName";

    private readonly ISaxErrorHandler _eh;
    private readonly CultureInfo? _culture;
    private readonly IReadOnlyList<Trigger> _triggers;
    private readonly Mode _startMode;
    private readonly JingLocalizer _localizer;
    private readonly Dictionary<ISchema, Stack<IValidator>> _validatorCache = new(ReferenceEqualityComparer.Instance);
    private readonly HashSet<NoResultAction> _noResultActions = [];
    private readonly Dictionary<string, List<int>> _attributeNamespaceIndexSets = new(StringComparer.Ordinal);
    private readonly List<SortedSet<int>> _activeHandlersAttributeIndexSets = [];
    private readonly HashSet<ISchema> _attributeSchemas = new(ReferenceEqualityComparer.Instance);
    private readonly Stack<string> _elementsLocalNameStack = new();
    private ILocator? _locator;
    private Section _currentSection = null!;
    private PrefixMapping? _prefixMapping;
    private bool _attributeNamespaceRejected;
    private IAttributes? _filteredAttributes;

    public NvdlValidator(Mode startMode, IReadOnlyList<Trigger> triggers, ISaxErrorHandler eh, CultureInfo? culture)
    {
        _startMode = startMode;
        _triggers = triggers;
        _eh = eh;
        _culture = culture;
        _localizer = JingLocalizer.ForNvdl(culture);
        InitCurrentSection();
    }

    public IContentHandler ContentHandler => this;

    public IDtdHandler DtdHandler => this;

    public void SetDocumentLocator(ILocator locator) => _locator = locator;

    public void StartDocument()
    {
    }

    public void EndDocument()
    {
    }

    public void Characters(string text)
    {
        foreach (IContentHandler handler in _currentSection.ActiveHandlers)
        {
            handler.Characters(text);
        }
    }

    public void ProcessingInstruction(string target, string data)
    {
    }

    public void StartElement(string namespaceUri, string localName, string qName, IAttributes attributes)
    {
        if (namespaceUri != _currentSection.Ns || Trigger(namespaceUri, localName, _elementsLocalNameStack.Peek()))
        {
            StartSection(namespaceUri);
        }

        _elementsLocalNameStack.Push(localName);
        _currentSection.Depth++;
        if (_currentSection.ContextDependent)
        {
            _currentSection.Context.Add(localName);
        }

        bool transformAttributes = ProcessAttributes(attributes);
        for (int i = 0; i < _currentSection.ActiveHandlers.Count; i++)
        {
            _currentSection.ActiveHandlers[i].StartElement(namespaceUri, localName, qName,
                transformAttributes ? FilterAttributes(_activeHandlersAttributeIndexSets[i], attributes) : attributes);
        }

        if (_currentSection.Depth == 1 && _currentSection.PlaceholderHandlers.Count > 0)
        {
            AttributeList atts = new();
            atts.Add(string.Empty, NsAttribute, NsAttribute, namespaceUri);
            atts.Add(string.Empty, LocalNameAttribute, LocalNameAttribute, localName);
            foreach (IContentHandler? handler in _currentSection.PlaceholderHandlers)
            {
                handler?.StartPrefixMapping(string.Empty, InstanceNamespace);
                handler?.StartElement(InstanceNamespace, PlaceholderElement, PlaceholderElement, atts);
            }
        }
    }

    public void EndElement(string namespaceUri, string localName, string qName)
    {
        _elementsLocalNameStack.Pop();
        foreach (IContentHandler handler in _currentSection.ActiveHandlers)
        {
            handler.EndElement(namespaceUri, localName, qName);
        }

        _currentSection.Depth--;
        if (_currentSection.ContextDependent)
        {
            _currentSection.Context.RemoveAt(_currentSection.Context.Count - 1);
        }

        if (_currentSection.Depth == 0)
        {
            foreach (IContentHandler? handler in _currentSection.PlaceholderHandlers)
            {
                handler?.EndPrefixMapping(string.Empty);
                handler?.EndElement(InstanceNamespace, PlaceholderElement, PlaceholderElement);
            }

            EndSection();
        }
    }

    public void StartPrefixMapping(string prefix, string uri) => _prefixMapping = new PrefixMapping(prefix, uri, _prefixMapping);

    public void EndPrefixMapping(string prefix) => _prefixMapping = _prefixMapping?.Parent;

    public void NotationDecl(string name, string? publicId, string? systemId)
    {
    }

    public void UnparsedEntityDecl(string name, string? publicId, string? systemId, string notationName)
    {
    }

    public void Reset()
    {
        for (; _currentSection is not null; _currentSection = _currentSection.Parent!)
        {
            for (int i = 0; i < _currentSection.Validators.Count; i++)
            {
                ReleaseValidator(_currentSection.Schemas[i], _currentSection.Validators[i]);
            }
        }

        _elementsLocalNameStack.Clear();
        _prefixMapping = null;
        InitCurrentSection();
    }

    private static IAttributes FilterAttributes(SortedSet<int> indexSet, IAttributes attributes) =>
        indexSet.Count == attributes.Length ? attributes : new FilteredAttributes(indexSet, attributes);

    private void InitCurrentSection()
    {
        _currentSection = new Section(this, NoNamespace, null);
        _currentSection.AddChildMode(new ModeUsage(_startMode, _startMode), null);
    }

    private bool Trigger(string ns, string name, string parent)
    {
        foreach (Trigger t in _triggers)
        {
            if (t.Namespace == ns && t.ElementNames.Contains(name) && !t.ElementNames.Contains(parent))
            {
                return true;
            }
        }

        return false;
    }

    // Jing groups the attributes by namespace in a Hashtable; the port goes through the namespaces in document order.
    private bool ProcessAttributes(IAttributes attributes)
    {
        if (_currentSection.AttributeProcessing == Mode.AttributeProcessingNone || attributes.Length == 0)
        {
            return false;
        }

        _attributeNamespaceIndexSets.Clear();
        for (int i = 0; i < attributes.Length; i++)
        {
            string ns = attributes.GetUri(i);
            if (!_attributeNamespaceIndexSets.TryGetValue(ns, out List<int>? indexSet))
            {
                indexSet = [];
                _attributeNamespaceIndexSets[ns] = indexSet;
            }

            indexSet.Add(i);
        }

        if (_currentSection.AttributeProcessing == Mode.AttributeProcessingQualified
            && _attributeNamespaceIndexSets.Count == 1 && _attributeNamespaceIndexSets.ContainsKey(string.Empty))
        {
            return false;
        }

        List<ModeUsage> handlerModes = _currentSection.ActiveHandlersAttributeModeUsage;
        _activeHandlersAttributeIndexSets.Clear();
        for (int i = 0; i < handlerModes.Count; i++)
        {
            _activeHandlersAttributeIndexSets.Add([]);
        }

        bool transform = false;
        foreach ((string ns, List<int> indexSet) in _attributeNamespaceIndexSets)
        {
            _attributeSchemas.Clear();
            _filteredAttributes = null;
            _attributeNamespaceRejected = false;
            for (int i = 0; i < handlerModes.Count; i++)
            {
                AttributeActionSet actions = ProcessAttributeSection(handlerModes[i], ns, indexSet, attributes);
                if (actions.Attach)
                {
                    _activeHandlersAttributeIndexSets[i].UnionWith(indexSet);
                }
                else
                {
                    transform = true;
                }
            }

            foreach (ModeUsage modeUsage in _currentSection.AttributeValidationModeUsages)
            {
                ProcessAttributeSection(modeUsage, ns, indexSet, attributes);
            }
        }

        return transform;
    }

    private AttributeActionSet ProcessAttributeSection(ModeUsage modeUsage, string ns, List<int> indexSet, IAttributes attributes)
    {
        Mode mode = modeUsage.GetMode(_currentSection.Context);
        AttributeActionSet actions = mode.GetAttributeActions(ns)!;
        if (actions.Reject && !_attributeNamespaceRejected)
        {
            _attributeNamespaceRejected = true;
            _eh.OnError(new SaxParseException(_localizer.Message("reject_attribute", ns), _locator));
        }

        foreach (ISchema schema in actions.Schemas)
        {
            if (!_attributeSchemas.Add(schema))
            {
                continue;
            }

            _filteredAttributes ??= FilterAttributes(new SortedSet<int>(indexSet), attributes);
            ValidateAttributes(schema, _filteredAttributes);
        }

        return actions;
    }

    private void ValidateAttributes(ISchema schema, IAttributes attributes)
    {
        IValidator validator = CreateValidator(schema);
        IContentHandler ch = validator.ContentHandler;
        InitHandler(ch);
        ch.StartElement(NvdlSchema.OwnerName.NamespaceUri, NvdlSchema.OwnerName.LocalName, NvdlSchema.OwnerName.LocalName, attributes);
        ch.EndElement(NvdlSchema.OwnerName.NamespaceUri, NvdlSchema.OwnerName.LocalName, NvdlSchema.OwnerName.LocalName);
        CleanupHandler(ch);
        ReleaseValidator(schema, validator);
    }

    private void StartSection(string uri)
    {
        Section section = new(this, uri, _currentSection);
        _noResultActions.Clear();
        foreach (Program program in _currentSection.ChildPrograms)
        {
            ActionSet actions = program.ModeUsage.GetMode(_currentSection.Context).GetElementActions(uri)!;
            actions.ResultAction?.Perform(program.Handler, section);
            foreach (NoResultAction action in actions.NoResultActions)
            {
                if (_noResultActions.Add(action))
                {
                    action.Perform(section);
                }
            }
        }

        foreach (IValidator? validator in section.Validators)
        {
            InitHandler(validator!.ContentHandler);
        }

        _currentSection = section;
    }

    private void InitHandler(IContentHandler ch)
    {
        if (_locator is not null)
        {
            ch.SetDocumentLocator(_locator);
        }

        ch.StartDocument();
        for (PrefixMapping? pm = _prefixMapping; pm is not null; pm = pm.Parent)
        {
            ch.StartPrefixMapping(pm.Prefix, pm.Uri);
        }
    }

    private void EndSection()
    {
        for (int i = 0; i < _currentSection.Validators.Count; i++)
        {
            IValidator validator = _currentSection.Validators[i]!;
            CleanupHandler(validator.ContentHandler);
            ReleaseValidator(_currentSection.Schemas[i], validator);
            _currentSection.Validators[i] = null;
        }

        _currentSection = _currentSection.Parent!;
    }

    private void CleanupHandler(IContentHandler vh)
    {
        for (PrefixMapping? pm = _prefixMapping; pm is not null; pm = pm.Parent)
        {
            vh.EndPrefixMapping(pm.Prefix);
        }

        vh.EndDocument();
    }

    private IValidator CreateValidator(ISchema schema)
    {
        if (!_validatorCache.TryGetValue(schema, out Stack<IValidator>? stack))
        {
            stack = new Stack<IValidator>();
            _validatorCache[schema] = stack;
        }

        return stack.Count == 0 ? schema.CreateValidator(_eh, _culture) : stack.Pop();
    }

    private void ReleaseValidator(ISchema schema, IValidator? validator)
    {
        if (validator is null)
        {
            return;
        }

        validator.Reset();
        _validatorCache[schema].Push(validator);
    }

    private sealed record PrefixMapping(string Prefix, string Uri, PrefixMapping? Parent);

    private sealed record Program(ModeUsage ModeUsage, IContentHandler? Handler);

    // A section: the elements of one namespace, with what the rules gave it (Jing's ValidatorImpl.Section).
    private sealed class Section(NvdlValidator owner, string ns, Section? parent) : ISectionState
    {
        public Section? Parent { get; } = parent;

        public string Ns { get; } = ns;

        public int Depth { get; set; }

        public List<IValidator?> Validators { get; } = [];

        public List<ISchema> Schemas { get; } = [];

        public List<IContentHandler> ActiveHandlers { get; } = [];

        public List<ModeUsage> ActiveHandlersAttributeModeUsage { get; } = [];

        public List<ModeUsage> AttributeValidationModeUsages { get; } = [];

        public List<Program> ChildPrograms { get; } = [];

        public List<string> Context { get; } = [];

        public bool ContextDependent { get; private set; }

        public int AttributeProcessing { get; private set; } = Mode.AttributeProcessingNone;

        public List<IContentHandler?> PlaceholderHandlers { get; } = [];

        public List<ModeUsage> PlaceholderModeUsages { get; } = [];

        public void AddChildMode(ModeUsage modeUsage, IContentHandler? handler)
        {
            ChildPrograms.Add(new Program(modeUsage, handler));
            ContextDependent |= modeUsage.IsContextDependent;
        }

        public void AddValidator(ISchema schema, ModeUsage modeUsage)
        {
            Schemas.Add(schema);
            IValidator validator = owner.CreateValidator(schema);
            Validators.Add(validator);
            ActiveHandlers.Add(validator.ContentHandler);
            ActiveHandlersAttributeModeUsage.Add(modeUsage);
            AttributeProcessing = Math.Max(AttributeProcessing, modeUsage.AttributeProcessing);
            ChildPrograms.Add(new Program(modeUsage, validator.ContentHandler));
            ContextDependent |= modeUsage.IsContextDependent;
        }

        public void AddActiveHandler(IContentHandler handler, ModeUsage attributeModeUsage)
        {
            ActiveHandlers.Add(handler);
            ActiveHandlersAttributeModeUsage.Add(attributeModeUsage);
            AttributeProcessing = Math.Max(AttributeProcessing, attributeModeUsage.AttributeProcessing);
            ContextDependent |= attributeModeUsage.IsContextDependent;
        }

        public void AddAttributeValidationModeUsage(ModeUsage modeUsage)
        {
            int ap = modeUsage.AttributeProcessing;
            if (ap != Mode.AttributeProcessingNone)
            {
                AttributeValidationModeUsages.Add(modeUsage);
                AttributeProcessing = Math.Max(ap, AttributeProcessing);
                ContextDependent |= modeUsage.IsContextDependent;
            }
        }

        public void Reject() => owner._eh.OnError(new SaxParseException(owner._localizer.Message("reject_element", Ns), owner._locator));

        public void AttachPlaceholder(ModeUsage modeUsage, IContentHandler? handler)
        {
            PlaceholderHandlers.Add(handler);
            PlaceholderModeUsages.Add(modeUsage);
        }
    }

    // The attributes of some namespaces of a start-tag (Jing's FilteredAttributes).
    private sealed class FilteredAttributes(SortedSet<int> indexSet, IAttributes attributes) : IAttributes
    {
        private readonly int[] _indexes = [.. indexSet];

        public int Length => _indexes.Length;

        public string GetUri(int index) => attributes.GetUri(_indexes[index]);

        public string GetLocalName(int index) => attributes.GetLocalName(_indexes[index]);

        public string GetQName(int index) => attributes.GetQName(_indexes[index]);

        public string GetValue(int index) => attributes.GetValue(_indexes[index]);
    }
}
