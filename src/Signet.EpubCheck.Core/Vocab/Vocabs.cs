using System;
using System.Collections.Frozen;
using System.Collections.Generic;
using System.Linq;

namespace Signet.EpubCheck.Core.Vocab;

/// <summary>How a property of a vocabulary may be used (upstream's <c>PropertyStatus</c>).</summary>
public enum PropertyStatus
{
    /// <summary>The property may be used.</summary>
    Allowed,

    /// <summary>The property is deprecated (<c>OPF-086</c>).</summary>
    Deprecated,

    /// <summary>The property is outdated (<c>OBS-001</c>).</summary>
    Outdated,

    /// <summary>The property may not be used in XHTML content documents (<c>OPF-087</c>).</summary>
    DisallowedInXhtml,
}

/// <summary>A property of a vocabulary (upstream's <c>Property</c>); two properties are equal when their full names are.</summary>
public sealed class VocabProperty : IEquatable<VocabProperty>
{
    private const string XhtmlMimeType = "application/xhtml+xml";

    private VocabProperty(string name, string vocabUri, string? prefix, Enum? value, PropertyStatus status)
    {
        Name = name;
        VocabUri = vocabUri;
        FullName = vocabUri + name;
        PrefixedName = string.IsNullOrEmpty(prefix) ? name : prefix + ":" + name;
        Value = value;
        Status = status;
    }

    /// <summary>The name of the property in its vocabulary.</summary>
    public string Name { get; }

    /// <summary>The URI of the vocabulary followed by the name.</summary>
    public string FullName { get; }

    /// <summary>The name with the prefix of the vocabulary, when it has one.</summary>
    public string PrefixedName { get; }

    /// <summary>The URI of the vocabulary.</summary>
    public string VocabUri { get; }

    /// <summary>The enum member of a known property, or <c>null</c> for a property of an unchecked vocabulary.</summary>
    public Enum? Value { get; }

    /// <summary>How the property may be used.</summary>
    public PropertyStatus Status { get; }

    /// <summary>Whether the property is deprecated.</summary>
    public bool IsDeprecated => Status == PropertyStatus.Deprecated;

    /// <summary>Whether the property is outdated.</summary>
    public bool IsOutdated => Status == PropertyStatus.Outdated;

    /// <summary>A property of an unchecked vocabulary.</summary>
    public static VocabProperty Of(string name, string vocabUri, string? prefix) => new(name, vocabUri, prefix, null, PropertyStatus.Allowed);

    /// <summary>A known property, with its enum member and status.</summary>
    public static VocabProperty Of(string name, string vocabUri, string? prefix, Enum value, PropertyStatus status = PropertyStatus.Allowed) =>
        new(name, vocabUri, prefix, value ?? throw new ArgumentNullException(nameof(value)), status);

    /// <summary>The enum members of type <typeparamref name="TEnum"/> among <paramref name="properties"/>.</summary>
    public static IReadOnlySet<TEnum> Filter<TEnum>(IEnumerable<VocabProperty>? properties)
        where TEnum : struct, Enum =>
        properties is null ? FrozenSet<TEnum>.Empty : properties.Select(property => property.Value).OfType<TEnum>().ToHashSet();

    /// <summary>Whether the property may be used in a document of <paramref name="mimeType"/>.</summary>
    public bool IsAllowed(string? mimeType) => Status != PropertyStatus.DisallowedInXhtml || mimeType != XhtmlMimeType;

    /// <summary>The enum member of the property.</summary>
    /// <exception cref="InvalidOperationException">The property is not a known one.</exception>
    public TEnum ToEnum<TEnum>()
        where TEnum : struct, Enum =>
        Value is TEnum value ? value : throw new InvalidOperationException($"{this} is not a {typeof(TEnum).Name}.");

    /// <inheritdoc />
    public override string ToString() => "Property [" + PrefixedName + "]";

    /// <inheritdoc />
    public bool Equals(VocabProperty? other) => other is not null && FullName == other.FullName;

    /// <inheritdoc />
    public override bool Equals(object? obj) => Equals(obj as VocabProperty);

    /// <inheritdoc />
    public override int GetHashCode() => StringComparer.Ordinal.GetHashCode(FullName);
}

/// <summary>A vocabulary of properties (upstream's <c>Vocab</c>).</summary>
public interface IVocab
{
    /// <summary>The base URI of the vocabulary.</summary>
    string Uri { get; }

    /// <summary>The property <paramref name="name"/>, or <c>null</c> when the vocabulary has none.</summary>
    VocabProperty? Lookup(string name);

    /// <summary>Whether using the vocabulary with <paramref name="prefix"/> is deprecated.</summary>
    bool IsDeprecated(string prefix);
}

/// <summary>A vocabulary with a fixed set of properties, one per member of <typeparamref name="TEnum"/> (upstream's <c>EnumVocab</c>).</summary>
public sealed class EnumVocab<TEnum> : IVocab
    where TEnum : struct, Enum
{
    private readonly FrozenDictionary<string, VocabProperty> _byName;
    private readonly FrozenDictionary<TEnum, VocabProperty> _byValue;

    /// <summary>Creates the vocabulary of <paramref name="uri"/> from the members, their names and statuses.</summary>
    public EnumVocab(string? uri, string? prefix, IEnumerable<(TEnum Property, string Name, PropertyStatus Status)> entries)
    {
        ArgumentNullException.ThrowIfNull(entries);
        Uri = uri ?? string.Empty;
        VocabProperty[] properties = entries.Select(entry => VocabProperty.Of(entry.Name, Uri, prefix, entry.Property, entry.Status)).ToArray();
        _byName = properties.ToFrozenDictionary(property => property.Name, StringComparer.Ordinal);
        _byValue = properties.ToFrozenDictionary(property => (TEnum)property.Value!);
    }

    /// <inheritdoc />
    public string Uri { get; }

    /// <inheritdoc />
    public VocabProperty? Lookup(string name) => _byName.GetValueOrDefault(name);

    /// <inheritdoc />
    public bool IsDeprecated(string prefix) => false;

    /// <summary>The property of <paramref name="property"/>.</summary>
    public VocabProperty Get(TEnum property) => _byValue[property];

    /// <summary>The name of <paramref name="property"/>.</summary>
    public string GetName(TEnum property) => _byValue[property].Name;

    /// <summary>The names of <paramref name="properties"/>.</summary>
    public IEnumerable<string> GetNames(IEnumerable<TEnum> properties)
    {
        ArgumentNullException.ThrowIfNull(properties);
        return properties.Select(GetName);
    }
}

/// <summary>A vocabulary that has every property (upstream's <c>UncheckedVocab</c>).</summary>
public class UncheckedVocab : IVocab
{
    private readonly string? _prefix;

    /// <summary>Creates the vocabulary of <paramref name="uri"/>, used with <paramref name="prefix"/>.</summary>
    public UncheckedVocab(string uri, string? prefix)
    {
        Uri = uri;
        _prefix = prefix;
    }

    /// <inheritdoc />
    public string Uri { get; }

    /// <inheritdoc />
    public VocabProperty? Lookup(string name) => VocabProperty.Of(name, Uri, _prefix);

    /// <inheritdoc />
    public virtual bool IsDeprecated(string prefix) => false;
}

/// <summary>An unchecked vocabulary whose reserved prefix is deprecated (upstream's <c>DeprecatedVocab</c>).</summary>
public sealed class DeprecatedVocab : UncheckedVocab
{
    private readonly string _prefix;

    /// <summary>Creates the vocabulary of <paramref name="uri"/>, deprecated with <paramref name="prefix"/>.</summary>
    public DeprecatedVocab(string uri, string prefix)
        : base(uri, prefix)
    {
        _prefix = prefix;
    }

    /// <inheritdoc />
    public override bool IsDeprecated(string prefix) => _prefix == prefix;
}

/// <summary>Several vocabularies of one URI looked up in order (upstream's <c>AggregateVocab</c>).</summary>
public sealed class AggregateVocab : IVocab
{
    private readonly IVocab[] _vocabs;

    private AggregateVocab(IVocab[] vocabs)
    {
        Uri = vocabs.Length > 0 ? vocabs[0].Uri ?? string.Empty : string.Empty;
        if (vocabs.Any(vocab => (vocab.Uri ?? string.Empty) != Uri))
        {
            throw new ArgumentException("Aggregated vocabs must share the same base URI", nameof(vocabs));
        }

        _vocabs = vocabs;
    }

    /// <inheritdoc />
    public string Uri { get; }

    /// <summary>The vocabularies <paramref name="vocabs"/> as one.</summary>
    public static IVocab Of(params IVocab[] vocabs)
    {
        ArgumentNullException.ThrowIfNull(vocabs);
        return new AggregateVocab(vocabs.ToArray());
    }

    /// <inheritdoc />
    public VocabProperty? Lookup(string name) => _vocabs.Select(vocab => vocab.Lookup(name)).FirstOrDefault(found => found is not null);

    /// <inheritdoc />
    public bool IsDeprecated(string prefix) => _vocabs.Any(vocab => vocab.IsDeprecated(prefix));
}
