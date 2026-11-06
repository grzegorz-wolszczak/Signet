using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using Signet.EpubCheck.Core.Jing.Datatype;

namespace Signet.EpubCheck.Core.Jing.Patterns;

/// <summary>A validation context that can also give a prefix bound to a namespace (Jing's <c>MatchContext</c>).</summary>
internal interface IMatchContext : IValidationContext
{
    /// <summary>A prefix bound to <paramref name="namespaceUri"/> in scope, or <c>null</c>.</summary>
    string? GetPrefix(string namespaceUri);
}

/// <summary>
/// Matches the events of a document against a pattern by derivatives and words the errors (Jing's
/// <c>PatternMatcher</c>). Each <c>Match…</c> method returns <c>false</c> when it found an error, then
/// <see cref="ErrorMessage"/> says what; after an error the matcher recovers and goes on.
/// </summary>
internal sealed class PatternMatcher
{
    private const int UndefinedTokenIndex = -3;
    private const int InconsistentTokenIndex = -2;
    private const string Or = "or";
    private const string And = "and";

    // The prefixes tried for a namespace that has none in scope.
    private static readonly string[] s_generatedPrefixes = ["ns", "ns-", "ns_", "NS", "NS-", "NS_"];

    private readonly Shared _shared;
    private PatternMemo _memo;
    private bool _hadError;
    private bool _ignoreNextEndTagOrAttributeValue;
    private List<DataDerivFailure> _dataDerivFailureList = [];

    public PatternMatcher(Pattern start, ValidatorPatternBuilder builder, JingLocalizer localizer)
    {
        _shared = new Shared(start, builder, localizer);
        _memo = builder.GetPatternMemo(start);
    }

    private PatternMatcher(PatternMemo memo, Shared shared)
    {
        _memo = memo;
        _shared = shared;
    }

    // How formatNames writes the names: of elements or attributes, joined with "and" or "or".
    [Flags]
    private enum FormatNames
    {
        ElementAnd = 0x0,
        Attribute = 0x1,
        Or = 0x2,
    }

    /// <summary>The text of the last error.</summary>
    public string? ErrorMessage { get; private set; }

    /// <summary>Whether the content of the current element is data (its text is buffered and matched at once).</summary>
    public bool IsTextTyped { get; private set; }

    /// <summary>Whether no error was found so far.</summary>
    public bool IsValidSoFar => !_hadError;

    private JingLocalizer Localizer => _shared.Localizer;

    /// <summary>A matcher at the start of a new document.</summary>
    public PatternMatcher Start() => new(_shared.Builder.GetPatternMemo(_shared.Start), _shared);

    /// <summary>A copy of the matcher in its current state.</summary>
    public PatternMatcher Copy()
    {
        PatternMatcher copy = (PatternMatcher)MemberwiseClone();
        copy._dataDerivFailureList = [];
        return copy;
    }

    public bool MatchStartDocument() => !_memo.IsNotAllowed || Error("schema_allows_nothing");

    public static bool MatchEndDocument() => true;

    public bool MatchStartTagOpen(Name name, string? qName, IMatchContext context)
    {
        if (SetMemo(_memo.StartTagOpenDeriv(name)))
        {
            return true;
        }

        PatternMemo next = _memo.StartTagOpenRecoverDeriv(name);
        bool ok = IgnoreError();
        if (!next.IsNotAllowed)
        {
            if (!ok)
            {
                HashSet<Name> missing = RequiredElementNames();
                if (missing.Count != 0)
                {
                    Error(missing.Count == 1 ? "unexpected_element_required_element_missing" : "unexpected_element_required_elements_missing",
                        ErrorArgQName(qName, name, context, isAttribute: false),
                        FormatNamesOf(missing, FormatNames.ElementAnd, context));
                }
                else
                {
                    Error("element_not_allowed_yet", ErrorArgQName(qName, name, context, isAttribute: false), ExpectedContent(context));
                }
            }
        }
        else
        {
            ValidatorPatternBuilder builder = _shared.Builder;
            next = builder.GetPatternMemo(builder.MakeAfter(_shared.FindElement(name), _memo.Pattern));
            if (!ok)
            {
                Error(next.IsNotAllowed ? "unknown_element" : "out_of_context_element",
                    ErrorArgQName(qName, name, context, isAttribute: false), ExpectedContent(context));
            }
        }

        _memo = next;
        return ok;
    }

    public bool MatchAttributeName(Name name, string? qName, IMatchContext context)
    {
        if (SetMemo(_memo.StartAttributeDeriv(name)))
        {
            return true;
        }

        _ignoreNextEndTagOrAttributeValue = true;
        if (IgnoreError())
        {
            return true;
        }

        string? quoted = ErrorArgQName(qName, name, context, isAttribute: true);
        NormalizedNameClass nnc = _memo.PossibleAttributeNames();
        if (nnc.IsEmpty)
        {
            Error("no_attributes_allowed", quoted);
        }
        else
        {
            Error("invalid_attribute_name", quoted, ExpectedAttributes(context));
        }

        return false;
    }

    public bool MatchAttributeValue(string value, Name name, string? qName, IMatchContext context)
    {
        if (_ignoreNextEndTagOrAttributeValue)
        {
            _ignoreNextEndTagOrAttributeValue = false;
            return true;
        }

        _dataDerivFailureList.Clear();
        if (SetMemo(_memo.DataDeriv(value, context, _dataDerivFailureList)))
        {
            return true;
        }

        bool ok = Error("invalid_attribute_value", ErrorArgQName(qName, name, context, isAttribute: true),
            FormatDataDerivFailures(value, context));
        _memo = _memo.RecoverAfter();
        return ok;
    }

    public bool MatchStartTagClose(Name name, string? qName, IMatchContext context)
    {
        bool ok;
        if (SetMemo(_memo.EndAttributes()))
        {
            ok = true;
        }
        else
        {
            ok = IgnoreError();
            if (!ok)
            {
                HashSet<Name> missing = RequiredAttributeNames();
                if (missing.Count == 0)
                {
                    Error("required_attributes_missing_expected", ErrorArgQName(qName, name, context, isAttribute: false),
                        ExpectedAttributes(context));
                }
                else
                {
                    Error(missing.Count == 1 ? "required_attribute_missing" : "required_attributes_missing",
                        ErrorArgQName(qName, name, context, isAttribute: false),
                        FormatNamesOf(missing, FormatNames.Attribute, context));
                }
            }

            _memo = _memo.IgnoreMissingAttributes();
        }

        IsTextTyped = _memo.Pattern.ContentType == ContentType.Data;
        return ok;
    }

    public bool MatchTextBeforeEndTag(string text, Name name, string? qName, IMatchContext context)
    {
        if (IsTextTyped)
        {
            _ignoreNextEndTagOrAttributeValue = true;
            return SetDataDeriv(text, name, qName, context);
        }

        return MatchUntypedText(text, context);
    }

    public bool MatchTextBeforeStartTag(string text, IMatchContext context) => MatchUntypedText(text, context);

    public bool MatchUntypedText(IMatchContext context) =>
        SetMemo(_memo.MixedTextDeriv()) || Error("text_not_allowed", ExpectedContent(context));

    public bool MatchEndTag(Name name, string? qName, IMatchContext context)
    {
        if (_ignoreNextEndTagOrAttributeValue)
        {
            _ignoreNextEndTagOrAttributeValue = false;
            return true;
        }

        if (IsTextTyped)
        {
            return SetDataDeriv(string.Empty, name, qName, context);
        }

        if (SetMemo(_memo.EndTagDeriv()))
        {
            return true;
        }

        bool ok = IgnoreError();
        PatternMemo next = _memo.RecoverAfter();

        // The derivative may be notAllowed only because the parent is: then this is not an error of this element.
        if (!ok && (!next.IsNotAllowed || _memo.EmptyAfter().EndTagDeriv().IsNotAllowed))
        {
            HashSet<Name> missing = RequiredElementNames();
            if (missing.Count != 0)
            {
                Error(missing.Count == 1 ? "incomplete_element_required_element_missing" : "incomplete_element_required_elements_missing",
                    ErrorArgQName(qName, name, context, isAttribute: false),
                    FormatNamesOf(missing, FormatNames.ElementAnd, context));
            }
            else
            {
                Error("incomplete_element_required_elements_missing_expected",
                    ErrorArgQName(qName, name, context, isAttribute: false), ExpectedContent(context));
            }
        }

        _memo = next;
        return ok;
    }

    public NormalizedNameClass PossibleStartTagNames() => _memo.PossibleStartTagNames();

    public NormalizedNameClass PossibleAttributeNames() => _memo.PossibleAttributeNames();

    public HashSet<Name> RequiredElementNames() => _memo.Pattern.Apply(_shared.Builder.RequiredElementsFunction);

    public HashSet<Name> RequiredAttributeNames() => _memo.Pattern.Apply(_shared.Builder.RequiredAttributesFunction);

    private bool MatchUntypedText(string text, IMatchContext context) =>
        DataDerivFunction.IsBlank(text) || MatchUntypedText(context);

    private bool SetDataDeriv(string text, Name name, string? qName, IMatchContext context)
    {
        IsTextTyped = false;
        PatternMemo textOnlyMemo = _memo.TextOnly();
        _dataDerivFailureList.Clear();
        if (SetMemo(textOnlyMemo.DataDeriv(text, context, _dataDerivFailureList)))
        {
            return true;
        }

        PatternMemo next = _memo.RecoverAfter();
        bool ok = IgnoreError();
        if (!ok && (!next.IsNotAllowed || textOnlyMemo.EmptyAfter().DataDeriv(text, context).IsNotAllowed))
        {
            NormalizedNameClass nnc = _memo.PossibleStartTagNames();
            if (!nnc.IsEmpty && DataDerivFunction.IsBlank(text))
            {
                Error("blank_not_allowed", ErrorArgQName(qName, name, context, isAttribute: false), ExpectedContent(context));
            }
            else
            {
                Error("invalid_element_value", ErrorArgQName(qName, name, context, isAttribute: false),
                    FormatDataDerivFailures(text, context));
            }
        }

        _memo = next;
        return ok;
    }

    private bool SetMemo(PatternMemo m)
    {
        if (m.IsNotAllowed)
        {
            return false;
        }

        _memo = m;
        return true;
    }

    private bool IgnoreError() => _hadError && _memo.IsNotAllowed;

    // Returns true when the error was ignored (it follows another one), false otherwise.
    private bool Error(string key, params string?[] args)
    {
        if (IgnoreError())
        {
            return true;
        }

        _hadError = true;
        ErrorMessage = Localizer.Message(key, args);
        return false;
    }

    private string? ErrorArgQName(string? qName, Name name, IMatchContext context, bool isAttribute)
    {
        if (IgnoreError())
        {
            return null;
        }

        if (string.IsNullOrEmpty(qName))
        {
            string ns = name.NamespaceUri;
            string localName = name.LocalName;
            if (ns.Length == 0 || (!isAttribute && string.Equals(ns, context.ResolveNamespacePrefix(string.Empty), StringComparison.Ordinal)))
            {
                qName = localName;
            }
            else
            {
                // Without a prefix the parser did not give the qualified names properly.
                string? prefix = context.GetPrefix(ns);
                qName = prefix is not null ? prefix + ":" + localName : "{" + ns + "}" + localName;
            }
        }

        return QuoteQName(qName);
    }

    private string? FormatDataDerivFailures(string str, IMatchContext context)
    {
        if (IgnoreError())
        {
            return null;
        }

        if (_dataDerivFailureList.Count == 0)
        {
            return string.Empty;
        }

        if (_dataDerivFailureList.Count > 1)
        {
            // Duplicates removed; Jing goes through a HashSet, whose order is that of identity hash codes (unspecified):
            // the port keeps the order the failures were found in.
            _dataDerivFailureList = _dataDerivFailureList.Distinct().ToList();
        }

        List<string> stringValues = [];
        HashSet<Name> names = [];
        List<string> messages = [];
        int tokenIndex = UndefinedTokenIndex;
        int tokenStart = -1;
        int tokenEnd = -1;
        foreach (DataDerivFailure fail in _dataDerivFailureList)
        {
            IDatatype dt = fail.Datatype;
            string? s = fail.StringValue;
            if (s is not null)
            {
                object? value = fail.Value;

                // Jing gives Datatype2 some special meaning here.
                if (value is Name valueName && dt is IDatatype2)
                {
                    names.Add(valueName);
                }
                else if (value is string valueString && dt is IDatatype2)
                {
                    stringValues.Add(valueString);
                }
                else
                {
                    stringValues.Add(s);
                }
            }
            else
            {
                string? message = fail.Message;
                if (message is not null)
                {
                    messages.Add(message);
                }
                else if (fail.Except is not null)
                {
                    return string.Empty;
                }
                else
                {
                    messages.Add(Localizer.Message("require_datatype", fail.DatatypeName.LocalName));
                }
            }

            switch (tokenIndex)
            {
                case InconsistentTokenIndex:
                    break;
                case UndefinedTokenIndex:
                    tokenIndex = fail.TokenIndex;
                    tokenStart = fail.TokenStart;
                    tokenEnd = fail.TokenEnd;
                    break;
                default:
                    if (tokenIndex != fail.TokenIndex)
                    {
                        tokenIndex = InconsistentTokenIndex;
                    }

                    break;
            }
        }

        if (stringValues.Count > 0)
        {
            stringValues.Sort(StringComparer.Ordinal);
            messages.Add(Localizer.Message("require_values", FormatList(stringValues.Select(QuoteValue).ToList(), Or)));
        }

        if (names.Count > 0)
        {
            messages.Add(Localizer.Message("require_qnames", FormatNamesOf(names, FormatNames.Or, context)));
        }

        if (messages.Count == 0)
        {
            return string.Empty;
        }

        string arg = FormatList(messages, Or);
        if (tokenIndex >= 0 && tokenStart >= 0 && tokenEnd <= str.Length)
        {
            if (tokenStart == str.Length)
            {
                return Localizer.Message("missing_token", arg);
            }

            return Localizer.Message("token_failures", QuoteValue(str[tokenStart..tokenEnd]), arg);
        }

        return Localizer.Message("data_failures", arg);
    }

    private string? ExpectedAttributes(IMatchContext context)
    {
        if (IgnoreError())
        {
            return null;
        }

        NormalizedNameClass nnc = _memo.PossibleAttributeNames();
        if (nnc.IsEmpty)
        {
            return string.Empty;
        }

        ISet<Name> expectedNames = nnc.IncludedNames;
        if (expectedNames.Count == 0)
        {
            return string.Empty;
        }

        return Localizer.Message(nnc.IsAnyNameIncluded || nnc.IncludedNamespaces.Count != 0 ? "expected_attribute_or_other_ns" : "expected_attribute",
            FormatNamesOf(expectedNames, FormatNames.Attribute | FormatNames.Or, context));
    }

    private string? ExpectedContent(IMatchContext context)
    {
        if (IgnoreError())
        {
            return null;
        }

        List<string> expected = [];
        if (!_memo.EndTagDeriv().IsNotAllowed)
        {
            expected.Add(Localizer.Message("element_end_tag"));
        }

        // The content type is not well defined for after patterns.
        switch (_memo.EmptyAfter().Pattern.ContentType)
        {
            case ContentType.Mixed:
                // (element foo { empty }, text) is mixed, but text is not allowed everywhere in it.
                if (!_memo.MixedTextDeriv().IsNotAllowed)
                {
                    expected.Add(Localizer.Message("text"));
                }

                break;
            case ContentType.Data:
                expected.Add(Localizer.Message("data"));
                break;
        }

        NormalizedNameClass nnc = _memo.PossibleStartTagNames();
        ISet<Name> expectedNames = nnc.IncludedNames;

        // Nothing is said about wildcards.
        if (expectedNames.Count != 0)
        {
            expected.Add(Localizer.Message("element_list", FormatNamesOf(expectedNames, FormatNames.Or, context)));
            if (nnc.IsAnyNameIncluded || nnc.IncludedNamespaces.Count != 0)
            {
                expected.Add(Localizer.Message("element_other_ns"));
            }
        }

        return expected.Count == 0 ? string.Empty : Localizer.Message("expected", FormatList(expected, Or));
    }

    private string FormatNamesOf(ICollection<Name> names, FormatNames flags, IMatchContext context)
    {
        if (names.Count == 0)
        {
            return string.Empty;
        }

        Dictionary<string, string> nsDecls = new(StringComparer.Ordinal);
        List<string> qNames = GenerateQNames(names, flags, context, nsDecls);
        qNames.Sort(StringComparer.Ordinal);
        string result = FormatList(qNames.Select(QuoteQName).ToList(), flags.HasFlag(FormatNames.Or) ? Or : And);
        if (nsDecls.Count != 0)
        {
            result = Localizer.Message("qnames_nsdecls", result, FormatNamespaceDecls(nsDecls));
        }

        return result;
    }

    private static List<string> GenerateQNames(ICollection<Name> names, FormatNames flags, IMatchContext context, Dictionary<string, string> nsDecls)
    {
        bool isAttribute = flags.HasFlag(FormatNames.Attribute);
        string? defaultNamespace;
        if (isAttribute)
        {
            defaultNamespace = string.Empty;
        }
        else
        {
            defaultNamespace = context.ResolveNamespacePrefix(string.Empty);
            if (names.Any(n => n.NamespaceUri.Length == 0))
            {
                if (defaultNamespace is not null)
                {
                    nsDecls[string.Empty] = string.Empty;
                }

                defaultNamespace = string.Empty;
            }
        }

        List<string> qNames = [];
        HashSet<string> undeclaredNamespaces = new(StringComparer.Ordinal);
        List<Name> namesWithUndeclaredNamespaces = [];
        foreach (Name name in names)
        {
            string ns = name.NamespaceUri;
            string? prefix;
            if (string.Equals(ns, defaultNamespace, StringComparison.Ordinal))
            {
                prefix = string.Empty;
            }
            else
            {
                prefix = context.GetPrefix(ns);

                // An attribute in a namespace whose only prefix is the empty one: its namespace counts as undeclared.
                if (isAttribute && prefix is { Length: 0 } && ns.Length != 0)
                {
                    prefix = null;
                }
            }

            if (prefix is null)
            {
                undeclaredNamespaces.Add(ns);
                namesWithUndeclaredNamespaces.Add(name);
            }
            else
            {
                qNames.Add(MakeQName(prefix, name.LocalName));
            }
        }

        if (namesWithUndeclaredNamespaces.Count == 0)
        {
            return qNames;
        }

        if (undeclaredNamespaces.Count == 1 && defaultNamespace is null)
        {
            nsDecls[undeclaredNamespaces.First()] = string.Empty;
        }
        else
        {
            ChoosePrefixes(undeclaredNamespaces, context, nsDecls);
        }

        // Now every namespace has a prefix in nsDecls.
        foreach (Name name in namesWithUndeclaredNamespaces)
        {
            qNames.Add(MakeQName(nsDecls[name.NamespaceUri], name.LocalName));
        }

        return qNames;
    }

    private static void ChoosePrefixes(HashSet<string> nsSet, IMatchContext context, Dictionary<string, string> nsDecls)
    {
        List<string> nsList = [.. nsSet];
        nsList.Sort(StringComparer.Ordinal);
        int len = nsList.Count;
        string? prefix;
        int tryIndex = 0;
        do
        {
            if (tryIndex < s_generatedPrefixes.Length)
            {
                prefix = s_generatedPrefixes[tryIndex];
            }
            else
            {
                // Otherwise as many underscores in front as it takes.
                prefix = "_" + s_generatedPrefixes[0];
                for (int i = s_generatedPrefixes.Length; i < tryIndex; i++)
                {
                    prefix += "_" + prefix;
                }
            }

            for (int i = 0; i < len; i++)
            {
                if (context.ResolveNamespacePrefix(len == 1 ? prefix : prefix + (i + 1).ToString(CultureInfo.InvariantCulture)) is not null)
                {
                    prefix = null;
                    break;
                }
            }

            ++tryIndex;
        }
        while (prefix is null);

        for (int i = 0; i < len; i++)
        {
            nsDecls[nsList[i]] = len == 1 ? prefix : prefix + (i + 1).ToString(CultureInfo.InvariantCulture);
        }
    }

    private string FormatList(List<string> list, string conjunction)
    {
        switch (list.Count)
        {
            case 0:
                return string.Empty;
            case 1:
                return list[0];
            case 2:
                return Localizer.Message(conjunction + "_list_pair", list[0], list[1]);
        }

        string s = Localizer.Message(conjunction + "_list_many_first", list[0]);
        for (int i = 1; i < list.Count - 1; i++)
        {
            s = Localizer.Message(conjunction + "_list_many_middle", s, list[i]);
        }

        return Localizer.Message(conjunction + "_list_many_last", s, list[^1]);
    }

    // nsDecls maps namespaces to prefixes.
    private static string FormatNamespaceDecls(Dictionary<string, string> nsDecls)
    {
        List<string> list = [];
        foreach ((string ns, string prefix) in nsDecls)
        {
            StringBuilder buf = new();
            buf.Append(prefix.Length == 0 ? "xmlns" : "xmlns:" + prefix).Append('=');
            AppendAttributeValue(buf, ns);
            list.Add(buf.ToString());
        }

        list.Sort(StringComparer.Ordinal);
        return string.Join(' ', list);
    }

    private static string? QuoteForAttributeValue(char c) => c switch
    {
        '<' => "&lt;",
        '"' => "&quot;",
        '&' => "&amp;",
        '\n' => "&#xA;",
        '\r' => "&#xD;",
        '\t' => "&#x9;",
        _ => null,
    };

    private static StringBuilder AppendAttributeValue(StringBuilder buf, string value)
    {
        buf.Append('"');
        foreach (char c in value)
        {
            string? quoted = QuoteForAttributeValue(c);
            if (quoted is not null)
            {
                buf.Append(quoted);
            }
            else
            {
                buf.Append(c);
            }
        }

        return buf.Append('"');
    }

    private static string QuoteValue(string str) => AppendAttributeValue(new StringBuilder(), str).ToString();

    private static string MakeQName(string prefix, string localName) => prefix.Length == 0 ? localName : prefix + ":" + localName;

    private string QuoteQName(string qName) => Localizer.Message("qname", qName);

    private sealed class Shared(Pattern start, ValidatorPatternBuilder builder, JingLocalizer localizer)
    {
        private Dictionary<Name, Pattern>? _recoverPatternTable;

        public Pattern Start { get; } = start;

        public ValidatorPatternBuilder Builder { get; } = builder;

        public JingLocalizer Localizer { get; } = localizer;

        public Pattern FindElement(Name name)
        {
            _recoverPatternTable ??= [];
            if (!_recoverPatternTable.TryGetValue(name, out Pattern? p))
            {
                p = FindElementFunction.FindElement(Builder, name, Start);
                _recoverPatternTable[name] = p;
            }

            return p;
        }
    }
}
