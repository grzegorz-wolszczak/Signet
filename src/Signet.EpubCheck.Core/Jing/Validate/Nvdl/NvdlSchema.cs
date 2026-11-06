using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using Signet.EpubCheck.Core.Jing.Datatype;
using Signet.EpubCheck.Core.Jing.Parse;
using Signet.EpubCheck.Core.Jing.Sax;

namespace Signet.EpubCheck.Core.Jing.Validate.Nvdl;

/// <summary>Reads NVDL schemas (Jing's NVDL <c>SchemaReceiverImpl</c> and <c>SchemaImpl.Handler</c>).</summary>
public sealed class NvdlSchemaReader
{
    /// <summary>Where the errors of the schema go.</summary>
    public ISaxErrorHandler? ErrorHandler { get; init; }

    /// <summary>Finds the schemas the NVDL schema refers to.</summary>
    public ISchemaResolver Resolver { get; init; } = BasicSchemaResolver.Instance;

    /// <summary>The datatype libraries of the RELAX NG schemas (the XML Schema datatypes when <c>null</c>).</summary>
    public IDatatypeLibraryFactory? DatatypeLibraryFactory { get; init; }

    /// <summary>The language of the messages about the schema.</summary>
    public CultureInfo? Culture { get; init; }

    /// <summary>Whether the messages of the Schematron schemas it uses are only the statements (see
    /// <see cref="AutoSchemaReader.SchematronStatementsOnly"/>).</summary>
    public bool SchematronStatementsOnly { get; init; }

    /// <summary>Reads the NVDL schema of <paramref name="source"/> and the schemas it uses.</summary>
    /// <exception cref="IncorrectSchemaException">The schema has errors (reported to <see cref="ErrorHandler"/>).</exception>
    /// <exception cref="SaxParseException">A schema document is not well-formed.</exception>
    public ISchema CreateSchema(InputSource source)
    {
        ArgumentNullException.ThrowIfNull(source);
        return NvdlSchema.Read(source, new SchemaReadSettings(ErrorHandler, Resolver, DatatypeLibraryFactory, Culture, SchematronStatementsOnly), isAttributesSchema: false);
    }
}

/// <summary>NVDL for <see cref="AutoSchemaReader"/>.</summary>
internal sealed class NvdlLanguage : ISchemaLanguage
{
    public string NamespaceUri => NvdlSchema.NvdlUri;

    public ISchema CreateSchema(InputSource source, SchemaReadSettings settings, bool isAttributesSchema) =>
        NvdlSchema.Read(source, settings, isAttributesSchema);
}

/// <summary>An NVDL schema: its modes, from the start mode, and its triggers (Jing's NVDL <c>SchemaImpl</c>).</summary>
internal sealed class NvdlSchema : ISchema
{
    public const string NvdlUri = "http://purl.oclc.org/dsdl/nvdl/ns/structure/1.0";

    /// <summary>The element whose attributes an attributes schema validates (Jing's <c>ValidatorImpl.OWNER_NAME</c>).</summary>
    public static readonly Name OwnerName = new("http://purl.oclc.org/dsdl/nvdl/ns/instance/1.0", "virtualElement");

    private const string ImplicitModeName = "#implicit";
    private const string WrapperModeName = "#wrapper";
    private const string NvdlSchemaResource = "Signet.EpubCheck.Core.Jing.Resources.nvdl.rng";
    private const string NvdlSchemaSystemId = "resource:/jing/nvdl.rng";

    // nvdl.rng, the RELAX NG schema of NVDL, which every NVDL schema is validated with while it is read.
    private static readonly Lazy<RelaxNgSchema> s_nvdlSchema = new(LoadNvdlSchema);

    private readonly Dictionary<string, Mode> _modeMap = new(StringComparer.Ordinal);
    private readonly List<Trigger> _triggers = [];
    private readonly Mode _defaultBaseMode;
    private readonly bool _attributesSchema;
    private int _anonymousModeCounter;

    private NvdlSchema(bool attributesSchema)
    {
        _attributesSchema = attributesSchema;
        MakeBuiltinMode("#allow", typeof(AllowAction));
        MakeBuiltinMode("#attach", typeof(AttachAction));
        MakeBuiltinMode("#unwrap", typeof(UnwrapAction));
        _defaultBaseMode = MakeBuiltinMode("#reject", typeof(RejectAction));
    }

    public Mode? StartMode { get; private set; }

    public IReadOnlyList<Trigger> Triggers => _triggers;

    /// <exception cref="IncorrectSchemaException">The schema has errors (reported).</exception>
    public static NvdlSchema Read(InputSource source, SchemaReadSettings settings, bool isAttributesSchema)
    {
        NvdlSchema schema = new(isAttributesSchema);
        Handler handler = new(schema, settings);
        new XmlEventReader { ContentHandler = handler, ErrorHandler = settings.ErrorHandler }.Parse(source);
        return handler.GetSchema();
    }

    public IValidator CreateValidator(ISaxErrorHandler errorHandler, CultureInfo? culture = null) =>
        new NvdlValidator(StartMode!, _triggers, errorHandler, culture);

    private static RelaxNgSchema LoadNvdlSchema()
    {
        using Stream stream = typeof(NvdlSchema).Assembly.GetManifestResourceStream(NvdlSchemaResource)
            ?? throw new InvalidOperationException("No resource " + NvdlSchemaResource);
        using MemoryStream copy = new();
        stream.CopyTo(copy);
        byte[] bytes = copy.ToArray();
        return new RelaxNgSchemaReader().CreateSchema(new InputSource(NvdlSchemaSystemId, () => new MemoryStream(bytes)));
    }

    private Mode NewAnonymousMode() => new(_defaultBaseMode, _anonymousModeCounter++);

    private Mode MakeBuiltinMode(string name, Type actionType)
    {
        Mode mode = LookupCreateMode(name)!;
        ActionSet actions = new();
        ModeUsage modeUsage = new(Mode.Current, mode);
        if (actionType == typeof(AttachAction))
        {
            actions.ResultAction = new AttachAction(modeUsage);
        }
        else if (actionType == typeof(AllowAction))
        {
            actions.AddNoResultAction(new AllowAction(modeUsage));
        }
        else if (actionType == typeof(UnwrapAction))
        {
            actions.ResultAction = new UnwrapAction(modeUsage);
        }
        else
        {
            actions.AddNoResultAction(new RejectAction(modeUsage));
        }

        mode.BindElement(NamespaceSpecification.AnyNamespace, NamespaceSpecification.DefaultWildcard, actions);
        mode.NoteDefined(null);

        // An attributes schema rejects attributes in the built-in modes; any other attaches them.
        AttributeActionSet attributeActions = new();
        if (_attributesSchema)
        {
            attributeActions.Reject = true;
        }
        else
        {
            attributeActions.Attach = true;
        }

        mode.BindAttribute(NamespaceSpecification.AnyNamespace, NamespaceSpecification.DefaultWildcard, attributeActions);
        return mode;
    }

    private Mode? LookupCreateMode(string? name)
    {
        if (name is null)
        {
            return null;
        }

        name = name.Trim();
        if (!_modeMap.TryGetValue(name, out Mode? mode))
        {
            mode = new Mode(name, _defaultBaseMode);
            _modeMap[name] = mode;
        }

        return mode;
    }

    /// <summary>Receives the NVDL document: validates it with nvdl.rng and builds the modes from its elements.</summary>
    private sealed class Handler : DelegatingContentHandler
    {
        private const string RulesElement = "rules";
        private const string ModeElement = "mode";
        private const string BaseAttribute = "base";
        private const string LegacyRncMediaType = "application/x-rnc";
        private const string RncMediaType = "application/relax-ng-compact-syntax";

        private readonly NvdlSchema _schema;
        private readonly SchemaReadSettings _settings;
        private readonly ISaxErrorHandler? _eh;
        private readonly CountingErrorHandler _ceh;
        private readonly JingLocalizer _localizer;
        private readonly XmlBaseHandler _xmlBaseHandler = new();
        private readonly Stack<ModeData> _modeDataStack = new();
        private readonly Stack<string> _nvdlStack = new();
        private ILocator? _locator;
        private RelaxNgValidator? _validator;
        private bool _hadError;
        private int _foreignDepth;
        private string? _defaultSchemaType;
        private ModeData _md = new();

        public Handler(NvdlSchema schema, SchemaReadSettings settings)
        {
            _schema = schema;
            _settings = settings;
            _eh = settings.ErrorHandler;
            _ceh = new CountingErrorHandler(_eh);
            _localizer = JingLocalizer.ForNvdl(settings.Culture);
        }

        public override void SetDocumentLocator(ILocator locator)
        {
            _xmlBaseHandler.SetLocator(locator);
            _locator = locator;
        }

        public override void StartDocument()
        {
            _validator = s_nvdlSchema.Value.CreateValidator(_ceh, _settings.Culture);
            Delegate = _validator.ContentHandler;
            if (_locator is not null)
            {
                base.SetDocumentLocator(_locator);
            }

            base.StartDocument();
        }

        /// <exception cref="IncorrectSchemaException">The schema has errors (reported).</exception>
        public NvdlSchema GetSchema()
        {
            if (_validator is null || _ceh.HadErrorOrFatalError)
            {
                throw new IncorrectSchemaException();
            }

            // Jing walks a Hashtable of the modes: the port reports in the order the modes were first named.
            HashSet<Mode> openModes = [];
            HashSet<Mode> checkedModes = [];
            foreach ((string modeName, Mode mode) in _schema._modeMap)
            {
                if (!mode.IsDefined)
                {
                    Error("undefined_mode", modeName, mode.WhereUsed);
                }

                for (Mode? tem = mode; tem is not null; tem = tem.BaseMode)
                {
                    if (checkedModes.Contains(tem))
                    {
                        break;
                    }

                    if (openModes.Contains(tem))
                    {
                        Error("mode_cycle", tem.Name, tem.WhereDefined);
                        break;
                    }

                    openModes.Add(tem);
                }

                checkedModes.UnionWith(openModes);
                openModes.Clear();
            }

            return _hadError ? throw new IncorrectSchemaException() : _schema;
        }

        public override void StartElement(string namespaceUri, string localName, string qName, IAttributes attributes)
        {
            base.StartElement(namespaceUri, localName, qName, attributes);
            _xmlBaseHandler.StartElement();
            if (AttributeValue(attributes, WellKnownNamespaces.Xml, BaseAttribute) is { } xmlBase)
            {
                _xmlBaseHandler.XmlBaseAttribute(xmlBase);
            }

            if (namespaceUri != NvdlUri || _foreignDepth > 0)
            {
                _foreignDepth++;
                return;
            }

            if (_ceh.HadErrorOrFatalError)
            {
                return;
            }

            switch (localName)
            {
                case RulesElement:
                    ParseRules(attributes);
                    break;
                case ModeElement:
                    string parent = _nvdlStack.Peek();
                    if (parent == RulesElement)
                    {
                        ParseMode(attributes);
                    }
                    else if (parent == ModeElement)
                    {
                        ParseIncludedMode();
                    }
                    else
                    {
                        ParseNestedMode();
                    }

                    break;
                case "namespace":
                    _md.AnyNamespace = false;
                    ParseRule(GetNs(attributes), attributes);
                    break;
                case "anyNamespace":
                    _md.AnyNamespace = true;
                    ParseRule(NamespaceSpecification.AnyNamespace, attributes);
                    break;
                case "validate":
                    ParseValidate(attributes);
                    break;
                case "reject":
                    ParseReject(attributes);
                    break;
                case "attach":
                    ParseAttach(attributes);
                    break;
                case "unwrap":
                    ParseResultAction(attributes, m => new UnwrapAction(m));
                    break;
                case "attachPlaceholder":
                    ParseResultAction(attributes, m => new AttachPlaceholderAction(m));
                    break;
                case "allow":
                    ParseAllow(attributes);
                    break;
                case "context":
                    ParseContext(attributes);
                    break;
                case "option":
                    ParseOption(attributes);
                    break;
                case "trigger":
                    ParseTrigger(attributes);
                    break;
                case "schema":
                    Error("embedded_schemas");
                    break;
                case "cancelNestedActions":
                    ParseCancelNestedActions(attributes);
                    break;
                case "message":
                    break;
                default:
                    throw new InvalidOperationException("unexpected element \"" + localName + "\"");
            }

            _nvdlStack.Push(localName);
        }

        public override void EndElement(string namespaceUri, string localName, string qName)
        {
            base.EndElement(namespaceUri, localName, qName);
            _xmlBaseHandler.EndElement();
            if (_foreignDepth > 0)
            {
                _foreignDepth--;
                return;
            }

            if (_ceh.HadErrorOrFatalError)
            {
                return;
            }

            _nvdlStack.Pop();
            if (localName == "validate")
            {
                FinishValidate();
            }
            else if (localName == ModeElement && _nvdlStack.Peek() != RulesElement)
            {
                // The end of an included or a nested mode.
                _md = _modeDataStack.Pop();
            }
        }

        private static string? AttributeValue(IAttributes attributes, string uri, string localName)
        {
            for (int i = 0; i < attributes.Length; i++)
            {
                if (attributes.GetUri(i) == uri && attributes.GetLocalName(i) == localName)
                {
                    return attributes.GetValue(i);
                }
            }

            return null;
        }

        private static string? Attribute(IAttributes attributes, string localName) => AttributeValue(attributes, string.Empty, localName);

        private void ParseRules(IAttributes attributes)
        {
            _schema.StartMode = GetModeAttribute(attributes, "startMode");
            if (_schema.StartMode is null)
            {
                _schema.StartMode = _schema.LookupCreateMode(ImplicitModeName)!;
                _md.CurrentMode = _schema.StartMode;
                _schema.StartMode.NoteDefined(null);
            }

            _schema.StartMode.NoteUsed(_locator);

            // An attributes schema starts in a mode that allows any element, to hold the attributes.
            if (_schema._attributesSchema)
            {
                Mode wrapper = _schema.LookupCreateMode(WrapperModeName)!;
                ActionSet actions = new();
                actions.AddNoResultAction(new AllowAction(new ModeUsage(_schema.StartMode, _schema.StartMode)));
                wrapper.BindElement(NamespaceSpecification.AnyNamespace, NamespaceSpecification.DefaultWildcard, actions);
                wrapper.NoteDefined(null);
                _schema.StartMode = wrapper;
            }

            _defaultSchemaType = Attribute(attributes, "schemaType");
        }

        private void ParseMode(IAttributes attributes)
        {
            _md.CurrentMode = GetModeAttribute(attributes, "name")!;
            if (_md.CurrentMode.IsDefined)
            {
                Error("duplicate_mode", _md.CurrentMode.Name);
                Error("first_mode", _md.CurrentMode.Name, _md.CurrentMode.WhereDefined);
            }
            else
            {
                if (GetModeAttribute(attributes, "extends") is { } baseMode)
                {
                    _md.CurrentMode.BaseMode = baseMode;
                }

                _md.CurrentMode.NoteDefined(_locator);
            }
        }

        private void ParseIncludedMode()
        {
            Mode parent = _md.CurrentMode!;
            _modeDataStack.Push(_md);
            _md = new ModeData { CurrentMode = _schema.NewAnonymousMode() };
            _md.CurrentMode.NoteDefined(_locator);
            parent.AddIncludedMode(_md.CurrentMode);
        }

        // A mode inside an action without useMode: the anonymous mode the action already made for it.
        private void ParseNestedMode()
        {
            ModeData oldMd = _md;
            _modeDataStack.Push(_md);
            _md = new ModeData { CurrentMode = oldMd.LastMode };
            if (_md.CurrentMode!.IsDefined)
            {
                Error("duplicate_mode", _md.CurrentMode.Name);
                Error("first_mode", _md.CurrentMode.Name, _md.CurrentMode.WhereDefined);
            }
            else
            {
                _md.CurrentMode.NoteDefined(_locator);
            }
        }

        private void ParseRule(string ns, IAttributes attributes)
        {
            _md.Match = ToElementsOrAttributes(Attribute(attributes, "match"), ElementsOrAttributes.Elements);
            string wildcard = Attribute(attributes, "wildCard") ?? NamespaceSpecification.DefaultWildcard;
            if (_md.Match.HasFlag(ElementsOrAttributes.Attributes))
            {
                _md.AttributeActions = new AttributeActionSet();
                if (!_md.CurrentMode!.BindAttribute(ns, wildcard, _md.AttributeActions))
                {
                    if (ns == NamespaceSpecification.AnyNamespace)
                    {
                        Error("duplicate_attribute_action_any_namespace");
                    }
                    else
                    {
                        Error("duplicate_attribute_action", ns);
                    }
                }
            }
            else
            {
                _md.AttributeActions = null;
            }

            if (_md.Match.HasFlag(ElementsOrAttributes.Elements))
            {
                _md.Actions = new ActionSet();
                if (!_md.CurrentMode!.BindElement(ns, wildcard, _md.Actions))
                {
                    if (ns == NamespaceSpecification.AnyNamespace)
                    {
                        Error("duplicate_element_action_any_namespace");
                    }
                    else
                    {
                        Error("duplicate_element_action", ns);
                    }
                }
            }
            else
            {
                _md.Actions = null;
            }
        }

        private void ParseValidate(IAttributes attributes)
        {
            _md.SchemaUriRef = GetSchema(attributes);
            _md.SchemaUriBase = _xmlBaseHandler.GetBaseUri();
            _md.SchemaType = Attribute(attributes, "schemaType") ?? _defaultSchemaType;
            if (_md.SchemaType == LegacyRncMediaType)
            {
                Warning("legacy_rnc_media_type", _locator);
            }

            _md.ModeUsage = _md.Actions is not null ? GetModeUsage(attributes) : null;
        }

        private void FinishValidate()
        {
            if (_md.SchemaUriRef is null)
            {
                return;
            }

            try
            {
                if (_md.AttributeActions is not null)
                {
                    _md.AttributeActions.AddSchema(CreateSubSchema(true));
                }

                if (_md.Actions is not null)
                {
                    _md.Actions.AddNoResultAction(new ValidateAction(_md.ModeUsage!, CreateSubSchema(false)));
                }
            }
            catch (IncorrectSchemaException)
            {
                _hadError = true;
            }
        }

        // A compact syntax media type gives a compact schema; anything else is read by its root element.
        private ISchema CreateSubSchema(bool isAttributesSchema)
        {
            InputSource source = _settings.Resolver.Resolve(_md.SchemaUriRef!, _md.SchemaUriBase);
            string? schemaType = _md.SchemaType?.Trim();
            return schemaType is RncMediaType or LegacyRncMediaType
                ? RelaxNgSchemaReader.From(_settings, RelaxNgSyntax.Compact, isAttributesSchema).CreateSchema(source)
                : AutoSchemaReader.CreateSchema(source, _settings, isAttributesSchema);
        }

        // The port supports no option (Jing's are those of its RELAX NG reader: check-id-idref, feasible): an option
        // that must be supported is an error, any other is ignored.
        private void ParseOption(IAttributes attributes)
        {
            string? mustSupportValue = Attribute(attributes, "mustSupport")?.Trim();
            bool mustSupport = mustSupportValue is "1" or "true";
            if (mustSupport)
            {
                Error("unknown_option", JingUri.Resolve(NvdlUri, Attribute(attributes, "name") ?? string.Empty));
            }
        }

        private void ParseTrigger(IAttributes attributes)
        {
            string ns = Attribute(attributes, "ns")!;
            string nameList = Attribute(attributes, "nameList")!;
            HashSet<string> names = [.. nameList.Split([' ', '\t', '\n', '\r', '\f'], StringSplitOptions.RemoveEmptyEntries)];
            _schema._triggers.Add(new Trigger(ns, names));
        }

        private void ParseAttach(IAttributes attributes)
        {
            if (_md.AttributeActions is not null)
            {
                _md.AttributeActions.Attach = true;
            }

            ParseResultAction(attributes, m => new AttachAction(m));
        }

        private void ParseResultAction(IAttributes attributes, Func<ModeUsage, ResultAction> make)
        {
            if (_md.Actions is not null)
            {
                _md.ModeUsage = GetModeUsage(attributes);
                _md.Actions.ResultAction = make(_md.ModeUsage);
            }
            else
            {
                _md.ModeUsage = null;
            }
        }

        // Allow on attributes is the same as nothing.
        private void ParseAllow(IAttributes attributes)
        {
            if (_md.Actions is not null)
            {
                _md.ModeUsage = GetModeUsage(attributes);
                _md.Actions.AddNoResultAction(new AllowAction(_md.ModeUsage));
            }
            else
            {
                _md.ModeUsage = null;
            }
        }

        private void ParseReject(IAttributes attributes)
        {
            if (_md.Actions is not null)
            {
                _md.ModeUsage = GetModeUsage(attributes);
                _md.Actions.AddNoResultAction(new RejectAction(_md.ModeUsage));
            }
            else
            {
                _md.ModeUsage = null;
            }

            if (_md.AttributeActions is not null)
            {
                _md.AttributeActions.Reject = true;
            }
        }

        private void ParseCancelNestedActions(IAttributes attributes)
        {
            if (_md.Actions is not null)
            {
                _md.ModeUsage = GetModeUsage(attributes);
                _md.Actions.CancelNestedActions = true;
            }
            else
            {
                _md.ModeUsage = null;
            }

            if (_md.AttributeActions is not null)
            {
                _md.AttributeActions.CancelNestedActions = true;
            }
        }

        private void ParseContext(IAttributes attributes)
        {
            Mode mode = GetUseMode(attributes);
            _md.LastMode = mode;
            try
            {
                List<NvdlPath> paths = NvdlPath.Parse(Attribute(attributes, "path") ?? string.Empty);
                if (_md.ModeUsage is not null)
                {
                    foreach (NvdlPath path in paths.Where(p => !_md.ModeUsage.AddContext(p.IsRoot, p.Names, mode)))
                    {
                        Error("duplicate_path", path.ToString());
                    }
                }
            }
            catch (NvdlPathException e)
            {
                Error(e.MessageKey);
            }
        }

        private string? GetSchema(IAttributes attributes)
        {
            string? schemaUri = Attribute(attributes, "schema");
            if (schemaUri?.Length == 0)
            {
                Error("no_schema");
                schemaUri = null;
            }

            if (schemaUri is not null && JingUri.HasFragmentId(schemaUri))
            {
                Error("schema_fragment_id");
            }

            return schemaUri;
        }

        private static ElementsOrAttributes ToElementsOrAttributes(string? value, ElementsOrAttributes defaultValue)
        {
            if (value is null)
            {
                return defaultValue;
            }

            ElementsOrAttributes eoa = ElementsOrAttributes.Neither;
            if (value.Contains("elements", StringComparison.Ordinal))
            {
                eoa |= ElementsOrAttributes.Elements;
            }

            if (value.Contains("attributes", StringComparison.Ordinal))
            {
                eoa |= ElementsOrAttributes.Attributes;
            }

            return eoa;
        }

        private ModeUsage GetModeUsage(IAttributes attributes)
        {
            _md.LastMode = GetUseMode(attributes);
            return new ModeUsage(_md.LastMode, _md.CurrentMode!);
        }

        private Mode GetUseMode(IAttributes attributes)
        {
            Mode? mode = GetModeAttribute(attributes, "useMode");
            if (mode is null)
            {
                return _schema.NewAnonymousMode();
            }

            mode.NoteUsed(_locator);
            return mode;
        }

        private Mode? GetModeAttribute(IAttributes attributes, string localName) => _schema.LookupCreateMode(Attribute(attributes, localName));

        private string GetNs(IAttributes attributes)
        {
            string ns = Attribute(attributes, "ns")!;
            if (!JingUri.IsAbsolute(ns) && ns.Length > 0)
            {
                Error("ns_absolute");
            }

            return ns;
        }

        private void Error(string key, params string?[] args) => Error(key, _locator, args);

        private void Error(string key, string? arg, ILocator? locator) => Error(key, locator, arg);

        private void Error(string key, ILocator? locator, params string?[] args)
        {
            _hadError = true;
            _eh?.OnError(new SaxParseException(_localizer.Message(key, args), locator));
        }

        private void Warning(string key, ILocator? locator) => _eh?.OnWarning(new SaxParseException(_localizer.Message(key), locator));
    }

    // What the handler knows about the rule and the action it is in (Jing's ModeData).
    private sealed class ModeData
    {
        public Mode? CurrentMode { get; set; }

        public ElementsOrAttributes Match { get; set; }

        public ActionSet? Actions { get; set; }

        public AttributeActionSet? AttributeActions { get; set; }

        public string? SchemaUriRef { get; set; }

        public string? SchemaUriBase { get; set; }

        public string? SchemaType { get; set; }

        public ModeUsage? ModeUsage { get; set; }

        public bool AnyNamespace { get; set; }

        public Mode? LastMode { get; set; }
    }
}
