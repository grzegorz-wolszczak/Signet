using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Signet.EpubCheck.Core.Jing.Sax;

namespace Signet.EpubCheck.Core.Jing.Validate.Nvdl;

// The model of an NVDL schema: modes with their rules for element and attribute namespaces, and the actions of the
// rules (Jing's validate.nvdl package, one type per Jing class).

/// <summary>What a section of the document gets from the actions of the rules that match it (Jing's <c>SectionState</c>).</summary>
internal interface ISectionState
{
    /// <summary>The child sections go through <paramref name="modeUsage"/>, attached to <paramref name="handler"/> (may be null).</summary>
    void AddChildMode(ModeUsage modeUsage, IContentHandler? handler);

    void AddValidator(ISchema schema, ModeUsage modeUsage);

    void AddActiveHandler(IContentHandler handler, ModeUsage attributeModeUsage);

    void AddAttributeValidationModeUsage(ModeUsage modeUsage);

    void Reject();

    void AttachPlaceholder(ModeUsage modeUsage, IContentHandler? handler);
}

/// <summary>An action of a rule, with the mode it uses (Jing's <c>Action</c>).</summary>
internal abstract class NvdlAction(ModeUsage modeUsage)
{
    public ModeUsage ModeUsage { get; } = modeUsage;

    public override bool Equals(object? obj) => obj is not null && obj.GetType() == GetType() && ((NvdlAction)obj).ModeUsage.Equals(ModeUsage);

    public override int GetHashCode() => GetType().GetHashCode() ^ ModeUsage.GetHashCode();
}

/// <summary>An action that validates, allows or rejects (Jing's <c>NoResultAction</c>).</summary>
internal abstract class NoResultAction(ModeUsage modeUsage) : NvdlAction(modeUsage)
{
    public abstract void Perform(ISectionState state);

    public abstract NoResultAction ChangeCurrentMode(Mode mode);
}

/// <summary>An action that decides where the section goes: attach, unwrap, attachPlaceholder (Jing's <c>ResultAction</c>).</summary>
internal abstract class ResultAction(ModeUsage modeUsage) : NvdlAction(modeUsage)
{
    public abstract void Perform(IContentHandler? handler, ISectionState state);

    public abstract ResultAction ChangeCurrentMode(Mode mode);
}

internal sealed class AllowAction(ModeUsage modeUsage) : NoResultAction(modeUsage)
{
    public override void Perform(ISectionState state)
    {
        state.AddChildMode(ModeUsage, null);
        state.AddAttributeValidationModeUsage(ModeUsage);
    }

    public override NoResultAction ChangeCurrentMode(Mode mode) => new AllowAction(ModeUsage.ChangeCurrentMode(mode));
}

internal sealed class RejectAction(ModeUsage modeUsage) : NoResultAction(modeUsage)
{
    public override void Perform(ISectionState state)
    {
        state.Reject();
        state.AddChildMode(ModeUsage, null);
        state.AddAttributeValidationModeUsage(ModeUsage);
    }

    public override NoResultAction ChangeCurrentMode(Mode mode) => new RejectAction(ModeUsage.ChangeCurrentMode(mode));
}

internal sealed class ValidateAction(ModeUsage modeUsage, ISchema schema) : NoResultAction(modeUsage)
{
    public ISchema Schema { get; } = schema;

    public override void Perform(ISectionState state) => state.AddValidator(Schema, ModeUsage);

    public override NoResultAction ChangeCurrentMode(Mode mode) => new ValidateAction(ModeUsage.ChangeCurrentMode(mode), Schema);

    public override bool Equals(object? obj) => base.Equals(obj) && ReferenceEquals(Schema, ((ValidateAction)obj!).Schema);

    public override int GetHashCode() => base.GetHashCode() ^ Schema.GetHashCode();
}

internal sealed class AttachAction(ModeUsage modeUsage) : ResultAction(modeUsage)
{
    public override void Perform(IContentHandler? handler, ISectionState state)
    {
        if (handler is not null)
        {
            state.AddActiveHandler(handler, ModeUsage);
        }
        else
        {
            state.AddAttributeValidationModeUsage(ModeUsage);
        }

        state.AddChildMode(ModeUsage, handler);
    }

    public override ResultAction ChangeCurrentMode(Mode mode) => new AttachAction(ModeUsage.ChangeCurrentMode(mode));
}

internal sealed class AttachPlaceholderAction(ModeUsage modeUsage) : ResultAction(modeUsage)
{
    public override void Perform(IContentHandler? handler, ISectionState state) => state.AttachPlaceholder(ModeUsage, handler);

    public override ResultAction ChangeCurrentMode(Mode mode) => new AttachPlaceholderAction(ModeUsage.ChangeCurrentMode(mode));
}

internal sealed class UnwrapAction(ModeUsage modeUsage) : ResultAction(modeUsage)
{
    public override void Perform(IContentHandler? handler, ISectionState state) => state.AddChildMode(ModeUsage, handler);

    public override ResultAction ChangeCurrentMode(Mode mode) => new UnwrapAction(ModeUsage.ChangeCurrentMode(mode));
}

/// <summary>The actions of a rule for elements: at most one result action, any number of others (Jing's <c>ActionSet</c>).</summary>
internal sealed class ActionSet
{
    private readonly List<NoResultAction> _noResultActions = [];

    public ResultAction? ResultAction { get; set; }

    public IReadOnlyList<NoResultAction> NoResultActions => _noResultActions;

    public bool CancelNestedActions { get; set; }

    public void AddNoResultAction(NoResultAction action) => _noResultActions.Add(action);

    public ActionSet ChangeCurrentMode(Mode mode)
    {
        ActionSet actions = new() { ResultAction = ResultAction?.ChangeCurrentMode(mode) };
        actions._noResultActions.AddRange(_noResultActions.Select(a => a.ChangeCurrentMode(mode)));
        return actions;
    }
}

/// <summary>The actions of a rule for attributes: attach, reject, and the schemas to validate them with (Jing's <c>AttributeActionSet</c>).</summary>
internal sealed class AttributeActionSet
{
    private readonly List<ISchema> _schemas = [];

    public bool Attach { get; set; }

    public bool Reject { get; set; }

    public bool CancelNestedActions { get; set; }

    public IReadOnlyList<ISchema> Schemas => _schemas;

    public void AddSchema(ISchema schema) => _schemas.Add(schema);
}

/// <summary>What a rule matches: elements, attributes, both or neither (Jing's <c>ElementsOrAttributes</c>).</summary>
[Flags]
internal enum ElementsOrAttributes
{
    Neither = 0,
    Elements = 1,
    Attributes = 2,
    Both = Elements | Attributes,
}

/// <summary>
/// A namespace a rule is for: a URI, possibly with wildcards (Jing's <c>NamespaceSpecification</c>); <c>##any</c> is
/// <c>anyNamespace</c>.
/// </summary>
internal sealed class NamespaceSpecification(string ns, string wildcard)
{
    public const string DefaultWildcard = "*";
    public const string AnyNamespace = "##any";

    public string Ns { get; } = ns;

    public string Wildcard { get; } = wildcard;

    /// <summary>Whether a namespace could be covered by both this specification and <paramref name="other"/>.</summary>
    public bool Compete(NamespaceSpecification other)
    {
        if (other.Wildcard.Length == 0)
        {
            return Covers(other.Ns);
        }

        string[] otherParts = Split(other.Ns, other.Wildcard);
        if (otherParts.Length == 1)
        {
            return Covers(other.Ns);
        }

        if (Wildcard.Length == 0)
        {
            return other.Covers(Ns);
        }

        string[] parts = Split(Ns, Wildcard);
        if (parts.Length == 1)
        {
            return other.Covers(Ns);
        }

        // ns = a1*a2*...*an and other = b1*b2*...*bm compete when a1/b1 and an/bm are prefixes of one another.
        return MatchPrefix(parts[0], otherParts[0]) && MatchPrefix(parts[^1], otherParts[^1]);
    }

    public bool Covers(string uri)
    {
        if (Ns == AnyNamespace || Wildcard.Length == 0)
        {
            return Ns == uri;
        }

        string[] parts = Split(Ns, Wildcard);
        if (parts.Length == 1)
        {
            return Ns == uri;
        }

        if (!uri.StartsWith(parts[0], StringComparison.Ordinal) || !uri.EndsWith(parts[^1], StringComparison.Ordinal))
        {
            return false;
        }

        int start = parts[0].Length;
        int end = uri.Length - parts[^1].Length;
        for (int i = 1; i < parts.Length - 1; i++)
        {
            if (start > end)
            {
                return false;
            }

            int match = uri.IndexOf(parts[i], start, StringComparison.Ordinal);
            if (match == -1 || match + parts[i].Length > end)
            {
                return false;
            }

            start = match + parts[i].Length;
        }

        return true;
    }

    public override bool Equals(object? obj) => obj is NamespaceSpecification other && Ns == other.Ns && Wildcard == other.Wildcard;

    public override int GetHashCode() => StringComparer.Ordinal.GetHashCode(Wildcard + "|" + Ns);

    private static bool MatchPrefix(string s1, string s2) =>
        s1.StartsWith(s2, StringComparison.Ordinal) || s2.StartsWith(s1, StringComparison.Ordinal);

    // Java's StringTokenizer with the wildcard's characters as delimiters, returning them: a delimiter becomes "".
    private static string[] Split(string value, string wildcard)
    {
        List<string> parts = [];
        StringBuilder token = new();
        foreach (char c in value)
        {
            if (wildcard.Contains(c, StringComparison.Ordinal))
            {
                if (token.Length > 0)
                {
                    parts.Add(token.ToString());
                    token.Clear();
                }

                parts.Add(c.ToString() == wildcard ? string.Empty : c.ToString());
            }
            else
            {
                token.Append(c);
            }
        }

        if (token.Length > 0)
        {
            parts.Add(token.ToString());
        }

        return parts.Count == 0 ? [value] : [.. parts];
    }
}

/// <summary>
/// The modes by the path of element names that leads to an element (Jing's <c>ContextMap</c>): looked up from the last
/// name back, a root path only at the top of the section.
/// </summary>
internal sealed class ContextMap
{
    private readonly Dictionary<string, ContextMap> _nameTable = new(StringComparer.Ordinal);
    private Mode? _rootValue;
    private Mode? _otherValue;

    public Mode? Get(IReadOnlyList<string> context) => Get(context, context.Count);

    public bool Put(bool isRoot, IReadOnlyList<string> names, Mode value) => Put(isRoot, names, names.Count, value);

    public IEnumerable<Mode> Values()
    {
        if (_rootValue is not null)
        {
            yield return _rootValue;
        }

        if (_otherValue is not null)
        {
            yield return _otherValue;
        }

        foreach (ContextMap nested in _nameTable.Values)
        {
            foreach (Mode mode in nested.Values())
            {
                yield return mode;
            }
        }
    }

    public override bool Equals(object? obj) =>
        obj is ContextMap other && Equals(_rootValue, other._rootValue) && Equals(_otherValue, other._otherValue)
        && _nameTable.Count == other._nameTable.Count
        && _nameTable.All(e => other._nameTable.TryGetValue(e.Key, out ContextMap? o) && e.Value.Equals(o));

    public override int GetHashCode()
    {
        int hc = (_rootValue?.GetHashCode() ?? 0) ^ (_otherValue?.GetHashCode() ?? 0);
        foreach ((string key, ContextMap value) in _nameTable)
        {
            hc ^= StringComparer.Ordinal.GetHashCode(key) ^ value.GetHashCode();
        }

        return hc;
    }

    private Mode? Get(IReadOnlyList<string> context, int len)
    {
        if (len > 0 && _nameTable.TryGetValue(context[len - 1], out ContextMap? nestedMap) && nestedMap.Get(context, len - 1) is { } value)
        {
            return value;
        }

        return _rootValue is not null && len == 0 ? _rootValue : _otherValue;
    }

    private bool Put(bool isRoot, IReadOnlyList<string> names, int len, Mode value)
    {
        if (len == 0)
        {
            if (isRoot)
            {
                if (_rootValue is not null)
                {
                    return false;
                }

                _rootValue = value;
            }
            else
            {
                if (_otherValue is not null)
                {
                    return false;
                }

                _otherValue = value;
            }

            return true;
        }

        string name = names[len - 1];
        if (!_nameTable.TryGetValue(name, out ContextMap? nestedMap))
        {
            nestedMap = new ContextMap();
            _nameTable[name] = nestedMap;
        }

        return nestedMap.Put(isRoot, names, len - 1, value);
    }
}

/// <summary>A mode an action uses, relative to the mode the action is in, with its context-dependent modes (Jing's <c>ModeUsage</c>).</summary>
internal sealed class ModeUsage
{
    private const int NotComputed = -1;

    private readonly Mode _mode;
    private readonly Mode _currentMode;
    private ContextMap? _modeMap;
    private int _attributeProcessing = NotComputed;

    public ModeUsage(Mode mode, Mode currentMode)
        : this(mode, currentMode, null)
    {
    }

    private ModeUsage(Mode mode, Mode currentMode, ContextMap? modeMap)
    {
        _mode = mode;
        _currentMode = currentMode;
        _modeMap = modeMap;
    }

    public bool IsContextDependent => _modeMap is not null;

    public int AttributeProcessing
    {
        get
        {
            if (_attributeProcessing == NotComputed)
            {
                _attributeProcessing = Resolve(_mode).AttributeProcessing;
                if (_modeMap is not null)
                {
                    foreach (Mode m in _modeMap.Values())
                    {
                        if (_attributeProcessing == Mode.AttributeProcessingFull)
                        {
                            break;
                        }

                        _attributeProcessing = Math.Max(Resolve(m).AttributeProcessing, _attributeProcessing);
                    }
                }
            }

            return _attributeProcessing;
        }
    }

    public ModeUsage ChangeCurrentMode(Mode currentMode) => new(_mode, currentMode, _modeMap);

    public Mode GetMode(IReadOnlyList<string> context)
    {
        if (_modeMap?.Get(context) is { } m)
        {
            return Resolve(m);
        }

        return Resolve(_mode);
    }

    public bool AddContext(bool isRoot, IReadOnlyList<string> names, Mode mode)
    {
        _modeMap ??= new ContextMap();
        return _modeMap.Put(isRoot, names, mode);
    }

    public override bool Equals(object? obj) =>
        obj is ModeUsage other && ReferenceEquals(_mode, other._mode) && ReferenceEquals(_currentMode, other._currentMode)
        && Equals(_modeMap, other._modeMap);

    public override int GetHashCode() => _mode.GetHashCode() ^ _currentMode.GetHashCode() ^ (_modeMap?.GetHashCode() ?? 0);

    // #current is the mode the action is in; an anonymous mode never defined (no nested mode in the action) too.
    private Mode Resolve(Mode mode) => ReferenceEquals(mode, Mode.Current) || (mode.IsAnonymous && !mode.IsDefined) ? _currentMode : mode;
}

/// <summary>A mode: rules for the namespaces of elements and attributes, a base mode, included modes (Jing's <c>Mode</c>).</summary>
internal sealed class Mode
{
    public const int AttributeProcessingNone = 0;
    public const int AttributeProcessingQualified = 1;
    public const int AttributeProcessingFull = 2;

    private const string AnonymousModeNamePrefix = "#anonymous#";
    private const int NotComputed = -1;

    private readonly Dictionary<string, ActionSet?> _elementMap = new(StringComparer.Ordinal);
    private readonly Dictionary<string, AttributeActionSet?> _attributeMap = new(StringComparer.Ordinal);
    private readonly Dictionary<NamespaceSpecification, ActionSet> _nssElementMap = [];
    private readonly Dictionary<NamespaceSpecification, AttributeActionSet> _nssAttributeMap = [];
    private readonly List<Mode> _includedModes = [];
    private int _attributeProcessing = NotComputed;

    public Mode(string name, Mode? baseMode)
    {
        Name = name;
        BaseMode = baseMode;
    }

    /// <summary>An anonymous mode (of an action without <c>useMode</c>, or a mode inside a mode).</summary>
    public Mode(Mode? baseMode, int anonymousNumber)
        : this(AnonymousModeNamePrefix + anonymousNumber, baseMode)
    {
        IsAnonymous = true;
    }

    /// <summary>The mode an action is in (<c>#current</c>).</summary>
    public static Mode Current { get; } = new("#current", null);

    public string Name { get; }

    public Mode? BaseMode { get; set; }

    public bool IsAnonymous { get; }

    public bool IsDefined { get; private set; }

    public ILocator? WhereDefined { get; private set; }

    public ILocator? WhereUsed { get; private set; }

    public int AttributeProcessing
    {
        get
        {
            if (_attributeProcessing == NotComputed)
            {
                _attributeProcessing = BaseMode?.AttributeProcessing ?? AttributeProcessingNone;
                foreach ((NamespaceSpecification nss, AttributeActionSet actions) in _nssAttributeMap)
                {
                    if (_attributeProcessing == AttributeProcessingFull)
                    {
                        break;
                    }

                    if (!actions.Attach || actions.Reject || actions.Schemas.Count > 0)
                    {
                        _attributeProcessing = nss.Ns.Length == 0 || nss.Ns == NamespaceSpecification.AnyNamespace
                            ? AttributeProcessingFull
                            : AttributeProcessingQualified;
                    }
                }
            }

            return _attributeProcessing;
        }
    }

    public void AddIncludedMode(Mode mode) => _includedModes.Add(mode);

    public ActionSet? GetElementActions(string ns) =>
        GetElementActionsExplicit(ns) ?? GetElementActionsExplicit(NamespaceSpecification.AnyNamespace);

    public AttributeActionSet? GetAttributeActions(string ns) =>
        GetAttributeActionsExplicit(ns) ?? GetAttributeActionsExplicit(NamespaceSpecification.AnyNamespace);

    public void NoteUsed(ILocator? locator)
    {
        if (WhereUsed is null && locator is not null)
        {
            WhereUsed = FixedLocator.CopyOf(locator);
        }
    }

    public void NoteDefined(ILocator? locator)
    {
        IsDefined = true;
        if (WhereDefined is null && locator is not null)
        {
            WhereDefined = FixedLocator.CopyOf(locator);
        }
    }

    public bool BindElement(string ns, string wildcard, ActionSet actions)
    {
        NamespaceSpecification nss = new(ns, wildcard);
        if (_nssElementMap.ContainsKey(nss) || _nssElementMap.Keys.Any(nss.Compete))
        {
            return false;
        }

        _nssElementMap[nss] = actions;
        return true;
    }

    public bool BindAttribute(string ns, string wildcard, AttributeActionSet actions)
    {
        NamespaceSpecification nss = new(ns, wildcard);
        if (_nssAttributeMap.ContainsKey(nss) || _nssAttributeMap.Keys.Any(nss.Compete))
        {
            return false;
        }

        _nssAttributeMap[nss] = actions;
        return true;
    }

    private ActionSet? GetElementActionsExplicit(string ns)
    {
        _elementMap.TryGetValue(ns, out ActionSet? actions);
        if (actions is null)
        {
            actions = _nssElementMap.FirstOrDefault(e => e.Key.Covers(ns)).Value;
            if (actions is not null)
            {
                _elementMap[ns] = actions;
            }
        }

        if (actions is null && _includedModes.Count > 0)
        {
            foreach (Mode includedMode in _includedModes)
            {
                actions = includedMode.GetElementActionsExplicit(ns);
                if (actions is not null)
                {
                    break;
                }
            }

            if (actions is not null)
            {
                actions = actions.ChangeCurrentMode(this);
                _elementMap[ns] = actions;
            }
        }

        if (actions is null && BaseMode is not null)
        {
            actions = BaseMode.GetElementActionsExplicit(ns);
            if (actions is not null)
            {
                actions = actions.ChangeCurrentMode(this);
                _elementMap[ns] = actions;
            }
        }

        return actions is { CancelNestedActions: true } ? null : actions;
    }

    private AttributeActionSet? GetAttributeActionsExplicit(string ns)
    {
        _attributeMap.TryGetValue(ns, out AttributeActionSet? actions);
        if (actions is null)
        {
            actions = _nssAttributeMap.FirstOrDefault(e => e.Key.Covers(ns)).Value;
            if (actions is not null)
            {
                _attributeMap[ns] = actions;
            }
        }

        if (actions is null && _includedModes.Count > 0)
        {
            foreach (Mode includedMode in _includedModes)
            {
                actions = includedMode.GetAttributeActionsExplicit(ns);
                if (actions is not null)
                {
                    break;
                }
            }

            if (actions is not null)
            {
                _attributeMap[ns] = actions;
            }
        }

        if (actions is null && BaseMode is not null)
        {
            actions = BaseMode.GetAttributeActionsExplicit(ns);
            if (actions is not null)
            {
                _attributeMap[ns] = actions;
            }
        }

        return actions is { CancelNestedActions: true } ? null : actions;
    }
}

/// <summary>The paths of a <c>context</c> element (Jing's <c>Path</c>): <c>a/b|/c</c>.</summary>
internal sealed class NvdlPath(bool isRoot, IReadOnlyList<string> names)
{
    private enum State
    {
        Start,
        InName,
        AfterName,
        AfterSlash,
    }

    public bool IsRoot { get; } = isRoot;

    public IReadOnlyList<string> Names { get; } = names;

    /// <exception cref="NvdlPathException">The path is not correct (the message key says why).</exception>
    public static List<NvdlPath> Parse(string str)
    {
        State state = State.Start;
        int nameStartIndex = -1;
        List<NvdlPath> paths = [];
        List<string> names = [];
        bool root = false;
        for (int i = 0; i < str.Length; i++)
        {
            char c = str[i];
            switch (c)
            {
                case ' ' or '\r' or '\n' or '\t':
                    if (state == State.InName)
                    {
                        names.Add(MakeName(str, nameStartIndex, i));
                        state = State.AfterName;
                    }

                    break;
                case '/':
                    switch (state)
                    {
                        case State.InName:
                            names.Add(MakeName(str, nameStartIndex, i));
                            break;
                        case State.Start:
                            root = true;
                            break;
                        case State.AfterSlash:
                            throw new NvdlPathException("unexpected_slash");
                    }

                    state = State.AfterSlash;
                    break;
                case '|':
                    switch (state)
                    {
                        case State.Start:
                            throw new NvdlPathException("empty_path");
                        case State.AfterSlash:
                            throw new NvdlPathException("expected_name");
                        case State.InName:
                            names.Add(MakeName(str, nameStartIndex, i));
                            break;
                    }

                    paths.Add(new NvdlPath(root, names));
                    root = false;
                    names = [];
                    state = State.Start;
                    break;
                default:
                    switch (state)
                    {
                        case State.AfterName:
                            throw new NvdlPathException("expected_slash");
                        case State.AfterSlash or State.Start:
                            nameStartIndex = i;
                            state = State.InName;
                            break;
                    }

                    break;
            }
        }

        switch (state)
        {
            case State.Start:
                throw new NvdlPathException("empty_path");
            case State.AfterSlash:
                throw new NvdlPathException("expected_name");
            case State.InName:
                names.Add(MakeName(str, nameStartIndex, str.Length));
                break;
        }

        paths.Add(new NvdlPath(root, names));
        return paths;
    }

    public override string ToString() => (IsRoot ? "/" : string.Empty) + string.Join('/', Names);

    private static string MakeName(string str, int start, int end)
    {
        string name = str[start..end];
        return Naming.IsNcname(name) ? name : throw new NvdlPathException("invalid_name");
    }
}

/// <summary>A <c>context</c> path is not correct (Jing's <c>Path.ParseException</c>): the message is the key of the message.</summary>
internal sealed class NvdlPathException : Exception
{
    public NvdlPathException()
    {
    }

    public NvdlPathException(string messageKey)
        : base(messageKey)
    {
    }

    public NvdlPathException(string message, Exception innerException)
        : base(message, innerException)
    {
    }

    public string MessageKey => Message;
}

/// <summary>Elements of a namespace that start a new section even inside one of their namespace (Jing's <c>Trigger</c>).</summary>
internal sealed record Trigger(string Namespace, IReadOnlySet<string> ElementNames);
