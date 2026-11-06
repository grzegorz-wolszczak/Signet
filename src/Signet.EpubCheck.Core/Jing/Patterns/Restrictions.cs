using System;
using System.Collections.Generic;
using Signet.EpubCheck.Core.Jing.Sax;

namespace Signet.EpubCheck.Core.Jing.Patterns;

/// <summary>A schema breaks a restriction of RELAX NG section 7 (Jing's <c>RestrictionViolationException</c>).</summary>
internal sealed class RestrictionViolationException : Exception
{
    public RestrictionViolationException()
    {
        MessageId = string.Empty;
    }

    public RestrictionViolationException(string messageId)
        : base(messageId)
    {
        MessageId = messageId;
    }

    public RestrictionViolationException(string messageId, Exception innerException)
        : base(messageId, innerException)
    {
        MessageId = messageId;
    }

    public RestrictionViolationException(string messageId, Name name)
        : this(messageId)
    {
        ViolationName = name;
    }

    public RestrictionViolationException(string messageId, string namespaceUri, bool isNamespace)
        : this(messageId)
    {
        _ = isNamespace;
        NamespaceUri = namespaceUri;
    }

    public string MessageId { get; }

    public ILocator? Locator { get; private set; }

    public Name? ViolationName { get; }

    public string? NamespaceUri { get; }

    public void MaybeSetLocator(ILocator? locator) => Locator ??= locator;
}

/// <summary>The element names the operands of an <c>interleave</c> can start with (Jing's <c>Alphabet</c>).</summary>
internal sealed class Alphabet
{
    private NameClass? _nameClass;

    public bool IsEmpty => _nameClass is null;

    public void AddElement(NameClass? nc)
    {
        if (_nameClass is null)
        {
            _nameClass = nc;
        }
        else if (nc is not null)
        {
            _nameClass = new ChoiceNameClass(_nameClass, nc);
        }
    }

    public void AddAlphabet(Alphabet a) => AddElement(a._nameClass);

    public void CheckOverlap(Alphabet a)
    {
        if (_nameClass is not null && a._nameClass is not null)
        {
            OverlapDetector.CheckOverlap(_nameClass, a._nameClass,
                "interleave_element_overlap_name", "interleave_element_overlap_ns", "interleave_element_overlap");
        }
    }
}

/// <summary>Finds two attribute patterns of an element that can match the same attribute (Jing's <c>DuplicateAttributeDetector</c>).</summary>
internal sealed class DuplicateAttributeDetector
{
    private readonly List<NameClass> _nameClasses = [];
    private AlternativeRange? _alternatives;

    public void AddAttribute(NameClass nc)
    {
        int lim = _nameClasses.Count;
        for (AlternativeRange? a = _alternatives; a is not null; a = a.Parent)
        {
            for (int i = a.EndIndex; i < lim; i++)
            {
                CheckAttributeOverlap(nc, _nameClasses[i]);
            }

            lim = a.StartIndex;
        }

        for (int i = 0; i < lim; i++)
        {
            CheckAttributeOverlap(nc, _nameClasses[i]);
        }

        _nameClasses.Add(nc);
    }

    public void StartChoice() => _alternatives = new AlternativeRange(_nameClasses.Count, _alternatives);

    public void Alternative() => _alternatives!.EndIndex = _nameClasses.Count;

    public void EndChoice() => _alternatives = _alternatives!.Parent;

    private static void CheckAttributeOverlap(NameClass nc1, NameClass nc2) =>
        OverlapDetector.CheckOverlap(nc1, nc2, "duplicate_attribute_name", "duplicate_attribute_ns", "duplicate_attribute");

    private sealed class AlternativeRange(int startIndex, AlternativeRange? parent)
    {
        public int StartIndex { get; } = startIndex;

        public int EndIndex { get; set; } = startIndex;

        public AlternativeRange? Parent { get; } = parent;
    }
}

/// <summary>Finds a name two name classes share (Jing's <c>OverlapDetector</c>).</summary>
internal sealed class OverlapDetector : INameClassVisitor
{
    // A namespace or local name no real name has; compared by reference.
    private static readonly string s_impossible = new('\u0000', 1);

    private readonly NameClass _nc1;
    private readonly NameClass _nc2;
    private Name? _overlapExample;

    private OverlapDetector(NameClass nc1, NameClass nc2)
    {
        _nc1 = nc1;
        _nc2 = nc2;
        nc1.Accept(this);
        nc2.Accept(this);
    }

    public static void CheckOverlap(NameClass nc1, NameClass nc2, string messageForName, string messageForNs, string messageForOther)
    {
        if (nc2 is SimpleNameClass snc2)
        {
            if (nc1.Contains(snc2.Name))
            {
                throw new RestrictionViolationException(messageForName, snc2.Name);
            }
        }
        else if (nc1 is SimpleNameClass snc1)
        {
            if (nc2.Contains(snc1.Name))
            {
                throw new RestrictionViolationException(messageForName, snc1.Name);
            }
        }
        else
        {
            Name? name = new OverlapDetector(nc1, nc2)._overlapExample;
            if (name is null)
            {
                return;
            }

            if (!ReferenceEquals(name.LocalName, s_impossible))
            {
                throw new RestrictionViolationException(messageForName, name);
            }

            if (ReferenceEquals(name.NamespaceUri, s_impossible))
            {
                throw new RestrictionViolationException(messageForOther);
            }

            throw new RestrictionViolationException(messageForNs, name.NamespaceUri, isNamespace: true);
        }
    }

    public void VisitChoice(NameClass nc1, NameClass nc2)
    {
        nc1.Accept(this);
        nc2.Accept(this);
    }

    public void VisitNsName(string ns) => Probe(new Name(ns, s_impossible));

    public void VisitNsNameExcept(string ns, NameClass nc)
    {
        Probe(new Name(ns, s_impossible));
        nc.Accept(this);
    }

    public void VisitAnyName() => Probe(new Name(s_impossible, s_impossible));

    public void VisitAnyNameExcept(NameClass nc)
    {
        Probe(new Name(s_impossible, s_impossible));
        nc.Accept(this);
    }

    public void VisitName(Name name) => Probe(name);

    public void VisitNull()
    {
    }

    public void VisitError()
    {
    }

    private void Probe(Name name)
    {
        if (_nc1.Contains(name) && _nc2.Contains(name))
        {
            _overlapExample = name;
        }
    }
}

/// <summary>Rejects attribute names in the <c>xmlns</c> namespace or named <c>xmlns</c> (Jing's <c>AttributeNameClassChecker</c>).</summary>
internal sealed class AttributeNameClassChecker : INameClassVisitor
{
    private const string XmlnsLocalName = "xmlns";
    private string? _errorMessageId;

    public string? CheckNameClass(NameClass nc)
    {
        _errorMessageId = null;
        nc.Accept(this);
        return _errorMessageId;
    }

    public void VisitChoice(NameClass nc1, NameClass nc2)
    {
        nc1.Accept(this);
        nc2.Accept(this);
    }

    public void VisitNsName(string ns)
    {
        if (string.Equals(ns, WellKnownNamespaces.Xmlns, StringComparison.Ordinal))
        {
            _errorMessageId = "xmlns_uri_attribute";
        }
    }

    public void VisitNsNameExcept(string ns, NameClass nc)
    {
        VisitNsName(ns);
        nc.Accept(this);
    }

    public void VisitAnyName()
    {
    }

    public void VisitAnyNameExcept(NameClass nc) => nc.Accept(this);

    public void VisitName(Name name)
    {
        VisitNsName(name.NamespaceUri);
        if (name.Equals(new Name(string.Empty, XmlnsLocalName)))
        {
            _errorMessageId = "xmlns_attribute";
        }
    }

    public void VisitNull()
    {
    }

    public void VisitError()
    {
    }
}
