using System;
using System.Collections.Generic;
using System.Linq;

namespace Signet.EpubCheck.Core.Jing.Patterns;

/// <summary>How closely a name class matches a name (the most specific wins when an element is looked for in recovery).</summary>
internal enum Specificity
{
    None = -1,
    AnyName = 0,
    NsName = 1,
    Name = 2,
}

/// <summary>Visits the structure of a name class (Jing's <c>NameClassVisitor</c>).</summary>
internal interface INameClassVisitor
{
    void VisitChoice(NameClass nc1, NameClass nc2);

    void VisitNsName(string ns);

    void VisitNsNameExcept(string ns, NameClass nc);

    void VisitAnyName();

    void VisitAnyNameExcept(NameClass nc);

    void VisitName(Name name);

    void VisitNull();

    void VisitError();
}

/// <summary>A set of names (Jing's pattern <c>NameClass</c>).</summary>
internal abstract class NameClass
{
    public abstract bool Contains(Name name);

    public abstract Specificity ContainsSpecificity(Name name);

    public abstract void Accept(INameClassVisitor visitor);

    /// <summary>Whether the class has a wildcard.</summary>
    public abstract bool IsOpen { get; }
}

internal sealed class SimpleNameClass(Name name) : NameClass
{
    public Name Name { get; } = name;

    public override bool Contains(Name name) => Name.Equals(name);

    public override Specificity ContainsSpecificity(Name name) => Contains(name) ? Specificity.Name : Specificity.None;

    public override int GetHashCode() => Name.GetHashCode();

    public override bool Equals(object? obj) => obj is SimpleNameClass other && Name.Equals(other.Name);

    public override void Accept(INameClassVisitor visitor) => visitor.VisitName(Name);

    public override bool IsOpen => false;
}

internal sealed class AnyNameClass : NameClass
{
    private static readonly int s_hashCode = typeof(AnyNameClass).GetHashCode();

    public override bool Contains(Name name) => true;

    public override Specificity ContainsSpecificity(Name name) => Specificity.AnyName;

    public override bool Equals(object? obj) => obj is AnyNameClass;

    public override int GetHashCode() => s_hashCode;

    public override void Accept(INameClassVisitor visitor) => visitor.VisitAnyName();

    public override bool IsOpen => true;
}

internal sealed class AnyNameExceptNameClass(NameClass nameClass) : NameClass
{
    private readonly NameClass _nameClass = nameClass;

    public override bool Contains(Name name) => !_nameClass.Contains(name);

    public override Specificity ContainsSpecificity(Name name) => Contains(name) ? Specificity.AnyName : Specificity.None;

    public override bool Equals(object? obj) => obj is AnyNameExceptNameClass other && _nameClass.Equals(other._nameClass);

    public override int GetHashCode() => ~_nameClass.GetHashCode();

    public override void Accept(INameClassVisitor visitor) => visitor.VisitAnyNameExcept(_nameClass);

    public override bool IsOpen => true;
}

internal sealed class ChoiceNameClass(NameClass nameClass1, NameClass nameClass2) : NameClass
{
    private readonly NameClass _nameClass1 = nameClass1;
    private readonly NameClass _nameClass2 = nameClass2;

    public override bool Contains(Name name) => _nameClass1.Contains(name) || _nameClass2.Contains(name);

    public override Specificity ContainsSpecificity(Name name)
    {
        Specificity s1 = _nameClass1.ContainsSpecificity(name);
        Specificity s2 = _nameClass2.ContainsSpecificity(name);
        return s1 > s2 ? s1 : s2;
    }

    public override int GetHashCode() => _nameClass1.GetHashCode() ^ _nameClass2.GetHashCode();

    public override bool Equals(object? obj) =>
        obj is ChoiceNameClass other && _nameClass1.Equals(other._nameClass1) && _nameClass2.Equals(other._nameClass2);

    public override void Accept(INameClassVisitor visitor) => visitor.VisitChoice(_nameClass1, _nameClass2);

    public override bool IsOpen => _nameClass1.IsOpen || _nameClass2.IsOpen;
}

internal sealed class NsNameClass(string namespaceUri) : NameClass
{
    public string NamespaceUri { get; } = namespaceUri;

    public override bool Contains(Name name) => string.Equals(NamespaceUri, name.NamespaceUri, StringComparison.Ordinal);

    public override Specificity ContainsSpecificity(Name name) => Contains(name) ? Specificity.NsName : Specificity.None;

    public override int GetHashCode() => StringComparer.Ordinal.GetHashCode(NamespaceUri);

    public override bool Equals(object? obj) =>
        obj is NsNameClass other && string.Equals(NamespaceUri, other.NamespaceUri, StringComparison.Ordinal);

    public override void Accept(INameClassVisitor visitor) => visitor.VisitNsName(NamespaceUri);

    public override bool IsOpen => true;
}

internal sealed class NsNameExceptNameClass(string namespaceUri, NameClass nameClass) : NameClass
{
    private readonly string _namespaceUri = namespaceUri;
    private readonly NameClass _nameClass = nameClass;

    public override bool Contains(Name name) =>
        string.Equals(_namespaceUri, name.NamespaceUri, StringComparison.Ordinal) && !_nameClass.Contains(name);

    public override Specificity ContainsSpecificity(Name name) => Contains(name) ? Specificity.NsName : Specificity.None;

    public override bool Equals(object? obj) =>
        obj is NsNameExceptNameClass other
        && string.Equals(_namespaceUri, other._namespaceUri, StringComparison.Ordinal)
        && _nameClass.Equals(other._nameClass);

    public override int GetHashCode() => StringComparer.Ordinal.GetHashCode(_namespaceUri) ^ _nameClass.GetHashCode();

    public override void Accept(INameClassVisitor visitor) => visitor.VisitNsNameExcept(_namespaceUri, _nameClass);

    public override bool IsOpen => true;
}

/// <summary>The name class of an element pattern whose content expanded to <c>notAllowed</c>: no name.</summary>
internal sealed class NullNameClass : NameClass
{
    private static readonly int s_hashCode = typeof(NullNameClass).GetHashCode();

    public override bool Contains(Name name) => false;

    public override Specificity ContainsSpecificity(Name name) => Specificity.None;

    public override int GetHashCode() => s_hashCode;

    public override bool Equals(object? obj) => obj is NullNameClass;

    public override void Accept(INameClassVisitor visitor) => visitor.VisitNull();

    public override bool IsOpen => false;
}

/// <summary>The name class made in place of a wrong one (the schema already has an error).</summary>
internal sealed class ErrorNameClass : NameClass
{
    public override bool Contains(Name name) => false;

    public override Specificity ContainsSpecificity(Name name) => Specificity.None;

    public override void Accept(INameClassVisitor visitor) => visitor.VisitError();

    public override bool IsOpen => false;

    // Identity, as in Jing (which does not override equals).
    public override bool Equals(object? obj) => ReferenceEquals(this, obj);

    public override int GetHashCode() => Pattern.Identity(this);
}

/// <summary>
/// A name class reduced to sets of names and namespaces (Jing's <c>NormalizedNameClass</c>), for the "expected …" part
/// of the messages.
/// </summary>
internal abstract class NormalizedNameClass
{
    protected NormalizedNameClass(ISet<Name> includedNames)
    {
        IncludedNames = includedNames;
    }

    public virtual bool IsEmpty => IncludedNames.Count == 0;

    public ISet<Name> IncludedNames { get; }

    public virtual bool IsAnyNameIncluded => false;

    public virtual ISet<string> IncludedNamespaces => EmptySet<string>.Instance;

    public virtual bool Contains(Name name) => IncludedNames.Contains(name);

    public override int GetHashCode() => SetHashCode(IncludedNames);

    public abstract override bool Equals(object? obj);

    protected static bool Equal(NormalizedNameClass nc1, NormalizedNameClass nc2) => nc1.IncludedNames.SetEquals(nc2.IncludedNames);

    // Java's Set.hashCode: the sum of the hash codes of the members.
    protected static int SetHashCode<T>(IEnumerable<T> set) => set.Aggregate(0, (h, x) => unchecked(h + (x?.GetHashCode() ?? 0)));

    private static class EmptySet<T>
    {
        public static readonly ISet<T> Instance = new HashSet<T>();
    }
}

internal sealed class NormalizedAnyNameClass(ISet<Name> includedNames, ISet<string> excludedNamespaces, ISet<Name> excludedNames)
    : NormalizedNameClass(includedNames)
{
    public ISet<string> ExcludedNamespaces { get; } = excludedNamespaces;

    public ISet<Name> ExcludedNames { get; } = excludedNames;

    public override bool IsAnyNameIncluded => true;

    public override bool IsEmpty => false;

    public override bool Contains(Name name) =>
        ExcludedNamespaces.Contains(name.NamespaceUri) ? base.Contains(name) : !ExcludedNames.Contains(name);

    public override int GetHashCode() => base.GetHashCode() ^ SetHashCode(ExcludedNamespaces) ^ SetHashCode(ExcludedNames);

    public override bool Equals(object? obj) =>
        obj is NormalizedAnyNameClass other
        && ExcludedNamespaces.SetEquals(other.ExcludedNamespaces)
        && ExcludedNames.SetEquals(other.ExcludedNames)
        && Equal(this, other);
}

internal sealed class NormalizedNsNameClass(ISet<Name> includedNames, IReadOnlyDictionary<string, HashSet<string>> nsMap)
    : NormalizedNameClass(includedNames)
{
    private readonly IReadOnlyDictionary<string, HashSet<string>> _nsMap = nsMap;

    public override ISet<string> IncludedNamespaces { get; } = new HashSet<string>(nsMap.Keys, StringComparer.Ordinal);

    public override bool IsEmpty => base.IsEmpty && _nsMap.Count == 0;

    public override bool Contains(Name name) =>
        _nsMap.TryGetValue(name.NamespaceUri, out HashSet<string>? excludedLocalNames)
            ? !excludedLocalNames.Contains(name.LocalName)
            : base.Contains(name);

    public override int GetHashCode() => base.GetHashCode() ^ _nsMap.Count;

    public override bool Equals(object? obj) =>
        obj is NormalizedNsNameClass other
        && _nsMap.Count == other._nsMap.Count
        && _nsMap.All(e => other._nsMap.TryGetValue(e.Key, out HashSet<string>? names) && names.SetEquals(e.Value))
        && Equal(this, other);
}

/// <summary>Reduces a name class to a <see cref="NormalizedNameClass"/> (Jing's <c>AbstractNameClassNormalizer</c>).</summary>
internal abstract class AbstractNameClassNormalizer
{
    // A namespace or local name no real name has: probes the wildcards.
    private const string Impossible = "\u0000";

    protected abstract bool Contains(Name name);

    protected abstract void Accept(INameClassVisitor visitor);

    public NormalizedNameClass Normalize()
    {
        MentionCollector mentioned = new();
        Accept(mentioned);
        if (Contains(new Name(Impossible, Impossible)))
        {
            HashSet<Name> includedNames = [];
            HashSet<string> excludedNamespaces = new(StringComparer.Ordinal);
            HashSet<Name> excludedNames = [];
            foreach (string ns in mentioned.Namespaces)
            {
                if (!Contains(new Name(ns, Impossible)))
                {
                    excludedNamespaces.Add(ns);
                }
            }

            foreach (Name name in mentioned.Names)
            {
                bool inClass = Contains(name);
                if (excludedNamespaces.Contains(name.NamespaceUri))
                {
                    if (inClass)
                    {
                        includedNames.Add(name);
                    }
                }
                else if (!inClass)
                {
                    excludedNames.Add(name);
                }
            }

            return new NormalizedAnyNameClass(includedNames, excludedNamespaces, excludedNames);
        }

        Dictionary<string, HashSet<string>> nsMap = new(StringComparer.Ordinal);
        foreach (string ns in mentioned.Namespaces)
        {
            if (Contains(new Name(ns, Impossible)) && !nsMap.ContainsKey(ns))
            {
                nsMap[ns] = new HashSet<string>(StringComparer.Ordinal);
            }
        }

        HashSet<Name> included = [];
        foreach (Name name in mentioned.Names)
        {
            bool inClass = Contains(name);
            if (!nsMap.TryGetValue(name.NamespaceUri, out HashSet<string>? excluded))
            {
                if (inClass)
                {
                    included.Add(name);
                }
            }
            else if (!inClass)
            {
                excluded.Add(name.LocalName);
            }
        }

        return new NormalizedNsNameClass(included, nsMap);
    }

    private sealed class MentionCollector : INameClassVisitor
    {
        public List<Name> Names { get; } = [];

        public List<string> Namespaces { get; } = [];

        public void VisitChoice(NameClass nc1, NameClass nc2)
        {
            nc1.Accept(this);
            nc2.Accept(this);
        }

        public void VisitNsName(string ns) => Namespaces.Add(ns);

        public void VisitNsNameExcept(string ns, NameClass nc)
        {
            Namespaces.Add(ns);
            nc.Accept(this);
        }

        public void VisitAnyName()
        {
        }

        public void VisitAnyNameExcept(NameClass nc) => nc.Accept(this);

        public void VisitName(Name name) => Names.Add(name);

        public void VisitNull()
        {
        }

        public void VisitError()
        {
        }
    }
}

internal class NameClassNormalizer(NameClass nameClass) : AbstractNameClassNormalizer
{
    public NameClass NameClass { get; set; } = nameClass;

    protected override bool Contains(Name name) => NameClass.Contains(name);

    protected override void Accept(INameClassVisitor visitor) => NameClass.Accept(visitor);
}

/// <summary>The normalized union of name classes.</summary>
internal sealed class UnionNameClassNormalizer() : NameClassNormalizer(new NullNameClass())
{
    public void Add(NameClass nameClass) => NameClass = new ChoiceNameClass(NameClass, nameClass);
}
